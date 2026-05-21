namespace ReportingPlatform.HubContracts;

/// <summary>
/// SignalR hub shared between <c>Realtime.Hub</c> (which maps it) and
/// <c>Response.Dispatcher.Worker</c> (which pushes via IHubContext).
///
/// Responsibilities:
///   - Connection lifecycle (join/leave user group)
///   - Widget channel subscriptions (SubscribeWidget / UnsubscribeWidget)
///
/// Request submission and cancellation are handled by the REST API
/// (POST /api/v1/requests, POST /api/v1/requests/{id}/cancel).
/// The Hub is a pure real-time push channel — it does not own request dispatch.
/// </summary>
public sealed class MainHub : Hub<IMainHubClient>
{
    private readonly ILogger<MainHub> _logger;

    public MainHub(ILogger<MainHub> logger) => _logger = logger;

    // ── Connection lifecycle ─────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
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

    // ── Widget subscriptions ─────────────────────────────────────────────────

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

    private string UserGroup() => $"user:{UserId()}";

    private static void ValidateWidgetChannel(string channel)
    {
        // Expected format: widget:{dashboardCode}:{widgetId}
        if (!channel.StartsWith("widget:", StringComparison.Ordinal) ||
            channel.Count(c => c == ':') < 2)
            throw new HubException("VALIDATION_ERROR");
    }
}
