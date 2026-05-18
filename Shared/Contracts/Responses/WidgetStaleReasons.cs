namespace ReportingPlatform.Contracts.Responses;

public static class WidgetStaleReasons
{
    public const string DataUpdated      = "data_updated";
    public const string MetadataChanged  = "metadata_changed";
    public const string ManualRefresh    = "manual_refresh";
    public const string CacheInvalidated = "cache_invalidated";
}
