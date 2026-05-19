namespace ReportingPlatform.QueryBuilder.Builder;

public sealed class AdapterException : Exception
{
    public string ErrorCode { get; }
    public string? Detail { get; }

    public AdapterException(string errorCode, string? detail = null)
        : base(detail is not null ? $"{errorCode}: {detail}" : errorCode)
    {
        ErrorCode = errorCode;
        Detail    = detail;
    }
}
