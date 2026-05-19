using System.Text.Json;
using ReportingPlatform.Transformers.Table;
using ReportingPlatform.Transformers.Tests.Helpers;
using Xunit;

namespace ReportingPlatform.Transformers.Tests.Table;

public sealed class TableTransformerTests
{
    private static IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> SampleRows(int n = 3) =>
        Enumerable.Range(1, n).Select(i =>
            TransformerTestHelpers.Row(
                ("id",     i),
                ("name",   $"Product {i}"),
                ("amount", i * 100.0))).ToList();

    // ------------------------------------------------------------------
    // SimpleTable
    // ------------------------------------------------------------------

    [Fact]
    public async Task SimpleTableTransformer_MatchesGoldenFile()
    {
        var vc  = """{"columns":[{"key":"id","label":"ID","type":"number"},{"key":"name","label":"Name","type":"string"},{"key":"amount","label":"Amount","type":"number"}]}""";
        var t   = new SimpleTableTransformer();
        var ctx = TransformerTestHelpers.Ctx("simple_table", vc);

        var result = await t.TransformAsync(SampleRows(), 3, ctx);
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal("client", result.GetProperty("pagination").GetProperty("mode").GetString());
        TransformerTestHelpers.AssertMatchesGolden("SimpleTableTransformer", result);
    }

    [Fact]
    public async Task SimpleTableTransformer_InfersColumnsFromFirstRow_WhenNoConfig()
    {
        var t   = new SimpleTableTransformer();
        var ctx = TransformerTestHelpers.Ctx("simple_table");  // no VisualConfig

        var result = await t.TransformAsync(SampleRows(1), 1, ctx);

        var columns = result.GetProperty("columns");
        // Should have 3 inferred columns: id, name, amount
        Assert.Equal(3, columns.GetArrayLength());
    }

    // ------------------------------------------------------------------
    // AdvancedTable
    // ------------------------------------------------------------------

    [Fact]
    public async Task AdvancedTableTransformer_MatchesGoldenFile()
    {
        var vc  = """{"columns":[{"key":"id","label":"ID","type":"number","sortable":true},{"key":"name","label":"Name","type":"string","sortable":true,"filterable":true,"filterType":"text"}]}""";
        var t   = new AdvancedTableTransformer();
        var ctx = TransformerTestHelpers.Ctx("advanced_table", vc);

        var result = await t.TransformAsync(SampleRows(), totalRows: 150, ctx);
        Assert.Equal("server", result.GetProperty("pagination").GetProperty("mode").GetString());
        Assert.Equal(150, result.GetProperty("pagination").GetProperty("totalRows").GetInt64());
        TransformerTestHelpers.AssertMatchesGolden("AdvancedTableTransformer", result);
    }

    [Fact]
    public async Task AdvancedTableTransformer_PaginationDisabled_WhenTotalRowsNull()
    {
        var t   = new AdvancedTableTransformer();
        var ctx = TransformerTestHelpers.Ctx("advanced_table");

        // totalRows = null → pagination mode should be "client" (disabled)
        var result = await t.TransformAsync(SampleRows(5), totalRows: null, ctx);
        Assert.Equal("client", result.GetProperty("pagination").GetProperty("mode").GetString());
        // TotalRows should equal row count when null passed
        Assert.Equal(5, result.GetProperty("pagination").GetProperty("totalRows").GetInt64());
    }

    // ------------------------------------------------------------------
    // PivotTable
    // ------------------------------------------------------------------

    [Fact]
    public async Task PivotTableTransformer_MatchesGoldenFile()
    {
        var rows = new[]
        {
            TransformerTestHelpers.Row(("region","APAC"), ("quarter","Q1"), ("revenue",500.0)),
            TransformerTestHelpers.Row(("region","APAC"), ("quarter","Q2"), ("revenue",620.0)),
            TransformerTestHelpers.Row(("region","EMEA"), ("quarter","Q1"), ("revenue",800.0)),
            TransformerTestHelpers.Row(("region","EMEA"), ("quarter","Q2"), ("revenue",910.0)),
        };
        var vc  = """
        {
          "rowDimensions":    [{"key":"region","label":"Region"}],
          "columnDimensions": [{"key":"quarter","label":"Quarter"}],
          "measures":         [{"key":"revenue","label":"Revenue","aggregate":"sum"}]
        }
        """;
        var t   = new PivotTableTransformer();
        var ctx = TransformerTestHelpers.Ctx("pivot_table", vc);

        var result = await t.TransformAsync(rows, null, ctx);
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        // Should have 4 cells (2 regions × 2 quarters)
        Assert.Equal(4, result.GetProperty("cells").GetArrayLength());
        TransformerTestHelpers.AssertMatchesGolden("PivotTableTransformer", result);
    }
}
