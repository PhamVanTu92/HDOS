using System.Text.Json;

namespace ReportingPlatform.ResponseDispatcher.Services;

/// <summary>
/// Orchestrates terminal-response routing:
/// <list type="number">
///   <item>Read owner record (who submitted the request).</item>
///   <item>Push to connectionId → fall back to user group → log warning.</item>
///   <item>Write to ResultStore (GET /result fallback).</item>
///   <item>Publish SSE terminal signal via Redis pub/sub.</item>
///   <item>Remove requestId from active-progress Set.</item>
///   <item>Delete owner record.</item>
/// </list>
/// </summary>
public sealed class ResponseRouter
{
    private readonly IHubContext<MainHub, IMainHubClient> _hub;
    private readonly OwnerStore      _ownerStore;
    private readonly ResultStore     _resultStore;
    private readonly IDatabase       _redis;
    private readonly DispatcherOptions _opts;
    private readonly ILogger<ResponseRouter> _logger;

    public ResponseRouter(
        IHubContext<MainHub, IMainHubClient> hub,
        OwnerStore ownerStore,
        ResultStore resultStore,
        IDatabase redis,
        IOptions<DispatcherOptions> opts,
        ILogger<ResponseRouter> logger)
    {
        _hub         = hub;
        _ownerStore  = ownerStore;
        _resultStore = resultStore;
        _redis       = redis;
        _opts        = opts.Value;
        _logger      = logger;
    }

    public async Task RouteAsync(OperationResponseMessage msg, CancellationToken ct)
    {
        _logger.LogInformation(
            "Routing response requestId={RequestId} status={Status} operation={Operation}",
            msg.RequestId, msg.Status, msg.Operation);

        // Step 1: Read owner record
        var owner = await _ownerStore.GetAsync(msg.RequestId, ct);

        // Step 2: Push via SignalR (type-safe, no string method names)
        var push = MapToPushMessage(msg);
        await PushToTargetAsync(owner, msg.Status, push, ct);

        // Step 3: Write terminal result to ResultStore (5-min TTL for GET /result fallback)
        await _resultStore.SetAsync(new ResultStoreRecord
        {
            RequestId   = msg.RequestId,
            Status      = msg.Status,
            PayloadJson = msg.PayloadJson,
            Error       = msg.Error,
            ElapsedMs   = msg.ElapsedMs,
            TenantId    = msg.TenantId,
            StoredAt    = DateTimeOffset.UtcNow,
        }, ct);

        // Step 4: Publish terminal SSE signal (closes open SSE streams via pub/sub)
        await _redis.PublishAsync(
            RedisChannel.Literal(RedisKeys.SseTerminal(msg.RequestId)),
            msg.RequestId);

        // Step 5: Remove from active-progress Set
        await _redis.SetRemoveAsync(RedisKeys.ActiveProgress, msg.RequestId);

        // Step 6: Delete owner record (request lifecycle complete)
        await _ownerStore.DeleteAsync(msg.RequestId);

        _logger.LogInformation(
            "Routed requestId={RequestId} status={Status} elapsedMs={ElapsedMs}",
            msg.RequestId, msg.Status, msg.ElapsedMs);
    }

    private async Task PushToTargetAsync(
        OwnerStoreRecord? owner,
        ResponseStatus status,
        ResponseDispatchPushMessage push,
        CancellationToken ct)
    {
        if (owner?.ConnectionId is not null)
        {
            var client = _hub.Clients.Client(owner.ConnectionId);
            await InvokeAsync(client, status, push, ct);
        }
        else if (_opts.FallbackToUserGroup && owner?.UserId is not null)
        {
            var group = _hub.Clients.Group($"user:{owner.UserId}");
            await InvokeAsync(group, status, push, ct);
            _logger.LogDebug(
                "Fan-out to user group for requestId={RequestId} userId={UserId}",
                owner.RequestId, owner.UserId);
        }
        else
        {
            _logger.LogWarning(
                "No owner record for requestId={RequestId} — result stored, no push sent",
                push.RequestId);
        }
    }

    private static Task InvokeAsync(
        IMainHubClient client,
        ResponseStatus status,
        ResponseDispatchPushMessage push,
        CancellationToken ct) =>
        status switch
        {
            ResponseStatus.Done      => client.RequestCompleted(push),
            ResponseStatus.Cancelled => client.RequestCancelled(push),
            _                        => client.RequestFailed(push),
        };

    private static ResponseDispatchPushMessage MapToPushMessage(OperationResponseMessage msg) =>
        new()
        {
            RequestId   = msg.RequestId,
            Status      = msg.Status.ToString().ToLowerInvariant(),
            Operation   = msg.Operation,
            PayloadJson = msg.PayloadJson,
            Error       = msg.Error,
            ElapsedMs   = msg.ElapsedMs,
            TenantId    = msg.TenantId,
        };
}
