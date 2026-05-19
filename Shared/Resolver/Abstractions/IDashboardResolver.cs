namespace ReportingPlatform.Resolver.Abstractions;

public interface IDashboardResolver
{
    /// <summary>
    /// Renders a full dashboard: fetches definitions, runs pre-fetch, fans out widget rendering,
    /// assembles <see cref="DashboardRenderPayload"/>.
    /// </summary>
    Task<DashboardRenderPayload> RenderAsync(
        string tenantId,
        string dashboardCode,
        IReadOnlyDictionary<string, JsonElement> filters,
        IReadOnlyDictionary<string, TablePaginationParams>? tableParams = null,
        CancellationToken ct = default,
        string? callerRequestId = null,
        string? callerUserId = null,
        DateTimeOffset? callerDeadline = null);
}
