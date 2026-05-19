using System.Text.Json;
using ReportingPlatform.Transformers.Tests.Helpers;
using ReportingPlatform.Transformers.Visualization;
using Xunit;

namespace ReportingPlatform.Transformers.Tests.Visualization;

public sealed class VisualizationTransformerTests
{
    private static readonly string Vc = """{"xKey":"date","yKey":"revenue","xType":"time","xLabel":"Date","yLabel":"Revenue"}""";

    private static IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> TimeSeriesRows() =>
    [
        TransformerTestHelpers.Row(("date", "2024-01"), ("revenue", 1000.0)),
        TransformerTestHelpers.Row(("date", "2024-02"), ("revenue", 1200.0)),
        TransformerTestHelpers.Row(("date", "2024-03"), ("revenue", 950.0)),
    ];

    [Fact]
    public async Task LineChartTransformer_MatchesGoldenFile()
    {
        var t      = new LineChartTransformer();
        var ctx    = TransformerTestHelpers.Ctx("line", Vc);
        var result = await t.TransformAsync(TimeSeriesRows(), null, ctx);

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        TransformerTestHelpers.AssertMatchesGolden("LineChartTransformer", result);
    }

    [Fact]
    public async Task BarChartTransformer_MatchesGoldenFile()
    {
        var t      = new BarChartTransformer();
        var ctx    = TransformerTestHelpers.Ctx("bar", Vc);
        var result = await t.TransformAsync(TimeSeriesRows(), null, ctx);

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        TransformerTestHelpers.AssertMatchesGolden("BarChartTransformer", result);
    }

    [Fact]
    public async Task AreaChartTransformer_MatchesGoldenFile()
    {
        var t      = new AreaChartTransformer();
        var ctx    = TransformerTestHelpers.Ctx("area", Vc);
        var result = await t.TransformAsync(TimeSeriesRows(), null, ctx);

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        TransformerTestHelpers.AssertMatchesGolden("AreaChartTransformer", result);
    }

    [Fact]
    public async Task PieChartTransformer_MatchesGoldenFile()
    {
        var rows = new[]
        {
            TransformerTestHelpers.Row(("label", "APAC"), ("value", 400.0)),
            TransformerTestHelpers.Row(("label", "EMEA"), ("value", 600.0)),
            TransformerTestHelpers.Row(("label", "NA"),   ("value", 1000.0)),
        };
        var vc  = """{"labelKey":"label","valueKey":"value"}""";
        var t   = new PieChartTransformer();
        var ctx = TransformerTestHelpers.Ctx("pie", vc);

        var result = await t.TransformAsync(rows, null, ctx);
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        // total = 2000
        Assert.Equal(2000.0, result.GetProperty("total").GetDouble());
        TransformerTestHelpers.AssertMatchesGolden("PieChartTransformer", result);
    }

    [Fact]
    public async Task DonutChartTransformer_ProducesSameStructureAsPie()
    {
        var rows = new[]
        {
            TransformerTestHelpers.Row(("label", "X"), ("value", 50.0)),
            TransformerTestHelpers.Row(("label", "Y"), ("value", 50.0)),
        };
        var vc   = """{"labelKey":"label","valueKey":"value"}""";
        var pie  = new PieChartTransformer();
        var donut= new DonutChartTransformer();
        var ctx  = TransformerTestHelpers.Ctx("donut", vc);

        var pieResult   = await pie.TransformAsync(rows, null, ctx);
        var donutResult = await donut.TransformAsync(rows, null, ctx);

        // Both produce the same JSON structure
        Assert.Equal(
            JsonSerializer.Serialize(pieResult),
            JsonSerializer.Serialize(donutResult));

        TransformerTestHelpers.AssertMatchesGolden("DonutChartTransformer", donutResult);
    }

    [Fact]
    public async Task KpiTransformer_MatchesGoldenFile()
    {
        var rows = new[]
        {
            TransformerTestHelpers.Row(("value", 98500.0), ("prev_value", 91000.0)),
        };
        var vc  = """{"valueKey":"value","previousKey":"prev_value","format":"currency","label":"Total Revenue","positiveDirection":"up"}""";
        var t   = new KpiTransformer();
        var ctx = TransformerTestHelpers.Ctx("kpi", vc);

        var result = await t.TransformAsync(rows, null, ctx);
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal(98500.0, result.GetProperty("value").GetDouble());
        TransformerTestHelpers.AssertMatchesGolden("KpiTransformer", result);
    }

    [Fact]
    public async Task GaugeTransformer_MatchesGoldenFile()
    {
        var rows = new[]
        {
            TransformerTestHelpers.Row(("score", 72.5)),
        };
        var vc  = """{"valueKey":"score","min":0,"max":100,"unit":"%"}""";
        var t   = new GaugeTransformer();
        var ctx = TransformerTestHelpers.Ctx("gauge", vc);

        var result = await t.TransformAsync(rows, null, ctx);
        Assert.Equal(72.5, result.GetProperty("value").GetDouble());
        TransformerTestHelpers.AssertMatchesGolden("GaugeTransformer", result);
    }

    [Fact]
    public async Task HeatmapTransformer_MatchesGoldenFile()
    {
        var rows = new[]
        {
            TransformerTestHelpers.Row(("hour", "09:00"), ("day", "Mon"), ("count", 42.0)),
            TransformerTestHelpers.Row(("hour", "09:00"), ("day", "Tue"), ("count", 31.0)),
            TransformerTestHelpers.Row(("hour", "10:00"), ("day", "Mon"), ("count", 55.0)),
        };
        var vc  = """{"xKey":"hour","yKey":"day","valueKey":"count"}""";
        var t   = new HeatmapTransformer();
        var ctx = TransformerTestHelpers.Ctx("heatmap", vc);

        var result = await t.TransformAsync(rows, null, ctx);
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        TransformerTestHelpers.AssertMatchesGolden("HeatmapTransformer", result);
    }

    [Fact]
    public async Task ScatterTransformer_MatchesGoldenFile()
    {
        var rows = new[]
        {
            TransformerTestHelpers.Row(("x", 1.0), ("y", 2.0), ("label", "A")),
            TransformerTestHelpers.Row(("x", 3.0), ("y", 4.0), ("label", "B")),
        };
        var vc  = """{"xKey":"x","yKey":"y","labelKey":"label"}""";
        var t   = new ScatterTransformer();
        var ctx = TransformerTestHelpers.Ctx("scatter", vc);

        var result = await t.TransformAsync(rows, null, ctx);
        TransformerTestHelpers.AssertMatchesGolden("ScatterTransformer", result);
    }

    [Fact]
    public async Task FunnelTransformer_MatchesGoldenFile()
    {
        var rows = new[]
        {
            TransformerTestHelpers.Row(("stage", "Visitors"),  ("count", 10000L)),
            TransformerTestHelpers.Row(("stage", "Leads"),     ("count", 3000L)),
            TransformerTestHelpers.Row(("stage", "MQLs"),      ("count", 900L)),
            TransformerTestHelpers.Row(("stage", "Customers"), ("count", 180L)),
        };
        var vc  = """{"labelKey":"stage","valueKey":"count"}""";
        var t   = new FunnelTransformer();
        var ctx = TransformerTestHelpers.Ctx("funnel", vc);

        var result = await t.TransformAsync(rows, null, ctx);

        // First step = 100 % of start
        var steps = result.GetProperty("steps");
        Assert.Equal(100.0, steps[0].GetProperty("percentOfStart").GetDouble());
        // Subsequent steps < 100 %
        Assert.True(steps[1].GetProperty("percentOfStart").GetDouble() < 100.0);
        TransformerTestHelpers.AssertMatchesGolden("FunnelTransformer", result);
    }
}
