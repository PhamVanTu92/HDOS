namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record TableFooter
{
    public bool Show { get; init; }
    public IReadOnlyDictionary<string, double>? Totals { get; init; }
}
