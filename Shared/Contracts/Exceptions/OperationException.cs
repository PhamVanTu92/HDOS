namespace ReportingPlatform.Contracts.Exceptions;

public sealed class OperationException : Exception
{
    public string Code { get; }

    public OperationException(string code, string message)
        : base(message) => Code = code;

    public OperationException(string code, string message, Exception inner)
        : base(message, inner) => Code = code;
}
