namespace ReportingPlatform.Transformers.Context;

public sealed record WidgetRenderContext
{
    public required WidgetDefinition Widget { get; init; }

    /// <summary>
    /// Active filter values (filterKey → JsonElement) from the dashboard request.
    /// Used by filter transformers to echo <c>CurrentValue</c> back to the client.
    /// </summary>
    public required IReadOnlyDictionary<string, JsonElement> Filters { get; init; }

    /// <summary>
    /// Pre-fetched options for <c>filter_dropdown</c> widgets that have an
    /// <c>optionsSource</c> config. Keyed by widgetId. Null if the pre-fetch phase
    /// produced no results. Filter transformers must not perform adapter calls.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<FilterOption>>? DropdownOptions { get; init; }
}
