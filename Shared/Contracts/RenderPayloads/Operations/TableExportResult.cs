namespace ReportingPlatform.Contracts.RenderPayloads.Operations;

public sealed record TableExportResult
{
    // "csv" | "xlsx"
    public required string Format { get; init; }

    // Non-null for large exports: pre-signed URL to download the file.
    public string? DownloadUrl { get; init; }

    // Non-null for small exports: base64-encoded file content (inline).
    public string? ContentBase64 { get; init; }

    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
}
