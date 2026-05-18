using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Operations;

public sealed record DatasourcePreviewPayload
{
    public required IReadOnlyList<TableColumn> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Rows { get; init; }
    public required long TotalRows { get; init; }
    public required bool Truncated { get; init; }
}
