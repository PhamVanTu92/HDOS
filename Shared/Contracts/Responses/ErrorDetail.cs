using MessagePack;

namespace ReportingPlatform.Contracts.Responses;

[MessagePackObject]
public sealed record ErrorDetail
{
    [Key("code")]
    public required string Code { get; init; }

    [Key("message")]
    public required string Message { get; init; }

    [Key("detailsJson")]
    public string? DetailsJson { get; init; }

    [Key("retryable")]
    public bool Retryable { get; init; }
}
