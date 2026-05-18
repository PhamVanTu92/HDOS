namespace ReportingPlatform.Contracts.Enums;

public static class ErrorCodes
{
    public const string OperationNotFound      = "OPERATION_NOT_FOUND";
    public const string ProviderUnavailable    = "PROVIDER_UNAVAILABLE";
    public const string ProviderDisconnected   = "PROVIDER_DISCONNECTED";
    public const string ProviderSuspended      = "PROVIDER_SUSPENDED";
    public const string Timeout                = "TIMEOUT";
    public const string Backpressure           = "BACKPRESSURE";
    public const string Cancelled              = "CANCELLED";
    public const string ValidationError        = "VALIDATION_ERROR";
    public const string DuplicateRequest       = "DUPLICATE_REQUEST";
    public const string RateLimited            = "RATE_LIMITED";
    public const string Unauthorized           = "UNAUTHORIZED";
    public const string Forbidden              = "FORBIDDEN";
    public const string InternalError          = "INTERNAL_ERROR";
    public const string ParamsTooLarge         = "PARAMS_TOO_LARGE";
    public const string ResourceUnavailable    = "RESOURCE_UNAVAILABLE";
    public const string DependencyFailed       = "DEPENDENCY_FAILED";
    public const string RateLimitedUpstream    = "RATE_LIMITED_UPSTREAM";
}
