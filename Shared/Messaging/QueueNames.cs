namespace ReportingPlatform.Messaging;

public static class QueueNames
{
    public const string OperationRequests  = "reporting.operation-requests";
    public const string OperationResponses = "reporting.operation-responses";
    public const string OperationProgress  = "reporting.operation-progress";
    public const string CancelRequests     = "reporting.cancel-requests";
    // Priority queues share the same exchange; consumers bind by routing key.
    public const string OperationRequestsHigh   = "reporting.operation-requests.high";
    public const string OperationRequestsNormal = "reporting.operation-requests.normal";
    public const string OperationRequestsLow    = "reporting.operation-requests.low";
}
