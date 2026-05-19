namespace ReportingPlatform.HubContracts;

/// <summary>
/// Strongly-typed client interface for <see cref="MainHub"/>.
/// <para>
/// Defines the four server-to-client push methods. <c>Response.Dispatcher.Worker</c> uses
/// <c>IHubContext&lt;MainHub, IMainHubClient&gt;</c> to call these methods type-safely —
/// no raw string method names anywhere in production code.
/// </para>
/// </summary>
public interface IMainHubClient
{
    /// <summary>Called when an operation completes successfully (Status = Done).</summary>
    Task RequestCompleted(ResponseDispatchPushMessage push);

    /// <summary>Called when an operation fails or times out (Status = Failed | Timeout).</summary>
    Task RequestFailed(ResponseDispatchPushMessage push);

    /// <summary>Called when an operation is cancelled (Status = Cancelled).</summary>
    Task RequestCancelled(ResponseDispatchPushMessage push);

    /// <summary>
    /// Called when widget data becomes stale due to an ingestion event.
    /// <paramref name="channel"/> format: <c>widget:{dashboardCode}:{widgetId}</c>.
    /// </summary>
    Task WidgetStale(string channel, WidgetStaleHint hint);
}
