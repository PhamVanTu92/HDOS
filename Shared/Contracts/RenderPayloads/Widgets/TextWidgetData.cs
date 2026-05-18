namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

public sealed record TextWidgetData
{
    // Content with template variables already substituted server-side.
    public required string Content { get; init; }

    // "markdown" | "html"
    public required string RenderMode { get; init; }

    // Informational: shows which variables were substituted.
    public IReadOnlyDictionary<string, string>? TemplateVariables { get; init; }
}
