namespace ReportingPlatform.HubContracts;

/// <summary>
/// SignalR hub shared between <c>Realtime.Hub</c> (which maps it) and
/// <c>Response.Dispatcher.Worker</c> (which pushes via IHubContext).
/// <para>
/// Both services reference <c>Shared/HubContracts</c> — the same hub type means the
/// same SignalR backplane channel names, enabling cross-process push.
/// </para>
/// </summary>
public sealed class MainHub : Hub<IMainHubClient>
{
    private readonly RequestSubmissionService              _submission;
    private readonly ICancelBus                           _cancelBus;
    private readonly ILogger<MainHub>                     _logger;

    public MainHub(
        RequestSubmissionService submission,
        ICancelBus cancelBus,
        ILogger<MainHub> logger)
    {
        _submission = submission;
        _cancelBus  = cancelBus;
        _logger     = logger;
    }

    // ── Connection lifecycle ─────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        // Join user-level group for multi-tab fan-out fallback (DECISIONS.md).
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup());
        _logger.LogDebug("Hub connected connectionId={ConnectionId} userId={UserId}",
            Context.ConnectionId, UserId());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup());
        _logger.LogDebug("Hub disconnected connectionId={ConnectionId} userId={UserId}",
            Context.ConnectionId, UserId());
        await base.OnDisconnectedAsync(exception);
    }

    // ── Client → Server methods ──────────────────────────────────────────────

    /// <summary>
    /// Submit a request over SignalR WebSocket.
    /// Identical backend behaviour to <c>POST /api/v1/requests</c>.
    /// </summary>
    [HubMethodName("SubmitRequest")]
    public async Task<SubmitAck> SubmitRequestAsync(RequestEnvelope envelope)
    {
        EnforceTenantMatch(envelope.TenantId);
        var connectionId = Context.ConnectionId;

        _logger.LogInformation(
            "Hub submit operation={Operation} requestId={RequestId} connectionId={ConnectionId}",
            envelope.Operation, envelope.RequestId, connectionId);

        // Let OperationException propagate — HubExceptionFilter in Realtime.Hub translates it.
        return await _submission.SubmitAsync(envelope, connectionId, Context.ConnectionAborted);
    }

    /// <summary>
    /// Cancel a request over SignalR WebSocket.
    /// Equivalent to <c>POST /api/v1/requests/{id}/cancel</c>.
    /// </summary>
    [HubMethodName("CancelRequest")]
    public async Task CancelRequestAsync(string requestId)
    {
        var userId   = UserId();
        var tenantId = TenantId();

        _logger.LogInformation(
            "Hub cancel requestId={RequestId} userId={UserId}", requestId, userId);

        await _cancelBus.PublishCancelAsync(requestId, userId, tenantId,
            Context.ConnectionAborted);
    }

    /// <summary>Join the WidgetStale group for a specific widget channel.</summary>
    [HubMethodName("SubscribeWidget")]
    public async Task SubscribeWidgetAsync(string channel)
    {
        ValidateWidgetChannel(channel);
        await Groups.AddToGroupAsync(Context.ConnectionId, channel);
    }

    /// <summary>Leave a WidgetStale group.</summary>
    [HubMethodName("UnsubscribeWidget")]
    public Task UnsubscribeWidgetAsync(string channel) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, channel);

    // ── Private helpers ──────────────────────────────────────────────────────

    private string UserId() =>
        Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new HubException("UNAUTHORIZED");

    private string TenantId() =>
        Context.User?.FindFirstValue("tenant")
        ?? throw new HubException("UNAUTHORIZED");

    private string UserGroup() => $"user:{UserId()}";

    private void EnforceTenantMatch(string envelopeTenantId)
    {
        var jwtTenant = Context.User?.FindFirstValue("tenant");
        if (!string.Equals(jwtTenant, envelopeTenantId, StringComparison.Ordinal))
            throw new HubException("FORBIDDEN");
    }

    private static void ValidateWidgetChannel(string channel)
    {
        // Expected format: widget:{dashboardCode}:{widgetId}
        if (!channel.StartsWith("widget:", StringComparison.Ordinal) ||
            channel.Count(c => c == ':') < 2)
            throw new HubException("VALIDATION_ERROR");
    }
}
