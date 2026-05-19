namespace ReportingPlatform.Operations.Context.Params;

public sealed record AdminCacheFlushParams
{
    /// <summary>Null flushes all caches for the tenant.</summary>
    public string? DashboardCode { get; init; }
}
