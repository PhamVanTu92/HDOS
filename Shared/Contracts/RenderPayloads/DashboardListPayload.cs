namespace ReportingPlatform.Contracts.RenderPayloads;

public sealed record DashboardListPayload
{
    public required IReadOnlyList<DashboardSummary> Dashboards { get; init; }
    public required long TotalCount { get; init; }
    public required bool HasMore { get; init; }
}

public sealed record DashboardSummary
{
    public required string DashboardCode { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Category { get; init; }
    public required string UpdatedAt { get; init; }
    public required int WidgetCount { get; init; }
    public bool IsFavorite { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}
