using ReportingPlatform.Contracts.RenderPayloads;
using ReportingPlatform.Contracts.RenderPayloads.Operations;
using ReportingPlatform.Contracts.RenderPayloads.Shared;
using ReportingPlatform.Contracts.RenderPayloads.Widgets;

namespace ReportingPlatform.Contracts.Serialization;

// All dashboard render payload types: widget data shapes, envelopes, shared sub-types, operations.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
// Top-level render payloads
[JsonSerializable(typeof(DashboardRenderPayload))]
[JsonSerializable(typeof(DashboardPayload))]
[JsonSerializable(typeof(DashboardListPayload))]
[JsonSerializable(typeof(DashboardSummary))]
[JsonSerializable(typeof(WidgetEnvelope))]
[JsonSerializable(typeof(WidgetMeta))]
[JsonSerializable(typeof(WidgetError))]
[JsonSerializable(typeof(IReadOnlyList<WidgetEnvelope>))]
// Widget data shapes
[JsonSerializable(typeof(TimeSeriesData))]
[JsonSerializable(typeof(PieData))]
[JsonSerializable(typeof(KpiData))]
[JsonSerializable(typeof(GaugeData))]
[JsonSerializable(typeof(HeatmapData))]
[JsonSerializable(typeof(ScatterData))]
[JsonSerializable(typeof(AdvancedTableData))]
[JsonSerializable(typeof(SimpleTableData))]
[JsonSerializable(typeof(PivotTableData))]
[JsonSerializable(typeof(FunnelData))]
[JsonSerializable(typeof(FilterDropdownData))]
[JsonSerializable(typeof(FilterDateRangeData))]
[JsonSerializable(typeof(FilterSliderData))]
[JsonSerializable(typeof(FilterSearchData))]
[JsonSerializable(typeof(TextWidgetData))]
[JsonSerializable(typeof(TabContainerData))]
// Shared sub-types
[JsonSerializable(typeof(ChartSeries))]
[JsonSerializable(typeof(SeriesPoint))]
[JsonSerializable(typeof(AxisDefinition))]
[JsonSerializable(typeof(ChartAxes))]
[JsonSerializable(typeof(ChartAnnotation))]
[JsonSerializable(typeof(PieSlice))]
[JsonSerializable(typeof(GaugeThreshold))]
[JsonSerializable(typeof(HeatmapCell))]
[JsonSerializable(typeof(HeatmapValueRange))]
[JsonSerializable(typeof(ScatterPoint))]
[JsonSerializable(typeof(ScatterSeries))]
[JsonSerializable(typeof(TableColumn))]
[JsonSerializable(typeof(TablePagination))]
[JsonSerializable(typeof(TableSortSpec))]
[JsonSerializable(typeof(TableAppliedFilter))]
[JsonSerializable(typeof(TableFooter))]
[JsonSerializable(typeof(PivotDimension))]
[JsonSerializable(typeof(PivotMeasure))]
[JsonSerializable(typeof(PivotCell))]
[JsonSerializable(typeof(FunnelStep))]
[JsonSerializable(typeof(FilterOption))]
[JsonSerializable(typeof(DateRangeValue))]
[JsonSerializable(typeof(SliderRangeValue))]
[JsonSerializable(typeof(TabDefinition))]
[JsonSerializable(typeof(DrillPathLevel))]
[JsonSerializable(typeof(ClickAction))]
[JsonSerializable(typeof(InteractionConfig))]
[JsonSerializable(typeof(KpiComparison))]
[JsonSerializable(typeof(RefreshPolicy))]
[JsonSerializable(typeof(DatePreset))]
// Operations
[JsonSerializable(typeof(DatasourcePreviewPayload))]
[JsonSerializable(typeof(DatasourceListPayload))]
[JsonSerializable(typeof(FilterOptionsResult))]
[JsonSerializable(typeof(TableExportResult))]
[JsonSerializable(typeof(DrillContextResult))]
public partial class RenderContractsJsonContext : JsonSerializerContext;
