using ReportingPlatform.Transformers.Engine;
using ReportingPlatform.Transformers.Filter;
using ReportingPlatform.Transformers.Layout;
using ReportingPlatform.Transformers.Registry;
using ReportingPlatform.Transformers.Table;
using ReportingPlatform.Transformers.Visualization;

namespace ReportingPlatform.Transformers.Extensions;

public static class TransformersExtensions
{
    /// <summary>
    /// Registers all 19 widget transformers, <see cref="ComputedColumnEngine"/>,
    /// and <see cref="TransformerRegistry"/>.
    /// </summary>
    public static IServiceCollection AddPlatformTransformers(
        this IServiceCollection services)
    {
        // Visualization (10)
        services.AddSingleton<IWidgetTransformer, LineChartTransformer>();
        services.AddSingleton<IWidgetTransformer, BarChartTransformer>();
        services.AddSingleton<IWidgetTransformer, AreaChartTransformer>();
        services.AddSingleton<IWidgetTransformer, PieChartTransformer>();
        services.AddSingleton<IWidgetTransformer, DonutChartTransformer>();
        services.AddSingleton<IWidgetTransformer, KpiTransformer>();
        services.AddSingleton<IWidgetTransformer, GaugeTransformer>();
        services.AddSingleton<IWidgetTransformer, HeatmapTransformer>();
        services.AddSingleton<IWidgetTransformer, ScatterTransformer>();
        services.AddSingleton<IWidgetTransformer, FunnelTransformer>();

        // Table (3)
        services.AddSingleton<IWidgetTransformer, SimpleTableTransformer>();
        services.AddSingleton<IWidgetTransformer, AdvancedTableTransformer>();
        services.AddSingleton<IWidgetTransformer, PivotTableTransformer>();

        // Filter (4)
        services.AddSingleton<IWidgetTransformer, FilterDropdownTransformer>();
        services.AddSingleton<IWidgetTransformer, FilterDateRangeTransformer>();
        services.AddSingleton<IWidgetTransformer, FilterSliderTransformer>();
        services.AddSingleton<IWidgetTransformer, FilterSearchTransformer>();

        // Layout (2)
        services.AddSingleton<IWidgetTransformer, TextWidgetTransformer>();
        services.AddSingleton<IWidgetTransformer, TabContainerTransformer>();

        // Registry aggregates all registered transformers
        services.AddSingleton<TransformerRegistry>();

        // ComputedColumnEngine — invoked by resolver, not a transformer
        services.AddSingleton<IComputedColumnEngine, ComputedColumnEngine>();

        return services;
    }
}
