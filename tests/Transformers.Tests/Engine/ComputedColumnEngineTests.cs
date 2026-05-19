using System.Text.Json;
using ReportingPlatform.Contracts.Enums;
using ReportingPlatform.Contracts.RenderPayloads.Shared;
using ReportingPlatform.Transformers.Engine;
using ReportingPlatform.Transformers.Tests.Helpers;
using Xunit;

namespace ReportingPlatform.Transformers.Tests.Engine;

public sealed class ComputedColumnEngineTests
{
    private readonly ComputedColumnEngine _engine = new();

    private static IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Rows(params double[] values) =>
        values.Select((v, i) =>
            TransformerTestHelpers.Row(("date", $"2024-{i + 1:D2}"), ("sales", v))).ToList();

    private static TableColumn ComputedCol(string transform) => new()
    {
        Key        = "computed",
        Label      = "Computed",
        Type       = "number",
        Computed   = transform,
        ComputedOn = "sales",
        Sortable   = false,
    };

    // ------------------------------------------------------------------
    // delta_from_previous
    // ------------------------------------------------------------------

    [Fact]
    public void DeltaFromPrevious_FirstRowIsNull_RestAreDiff()
    {
        var rows   = Rows(100, 150, 120);
        var result = _engine.Apply(rows, [ComputedCol(ComputedTransform.DeltaFromPrevious)]);

        Assert.Equal(JsonValueKind.Null, result[0]["computed"].ValueKind);
        Assert.Equal(50.0,  result[1]["computed"].GetDouble());
        Assert.Equal(-30.0, result[2]["computed"].GetDouble());
    }

    // ------------------------------------------------------------------
    // percent_change_from_previous
    // ------------------------------------------------------------------

    [Fact]
    public void PercentChangeFromPrevious_CorrectPercentages()
    {
        var rows   = Rows(100, 150, 100);
        var result = _engine.Apply(rows, [ComputedCol(ComputedTransform.PercentChangeFromPrevious)]);

        Assert.Equal(JsonValueKind.Null, result[0]["computed"].ValueKind);
        Assert.Equal(50.0,  result[1]["computed"].GetDouble(), precision: 6);
        Assert.Equal(-33.333333333333336, result[2]["computed"].GetDouble(), precision: 6);
    }

    // ------------------------------------------------------------------
    // percent_of_total
    // ------------------------------------------------------------------

    [Fact]
    public void PercentOfTotal_SumsTo100()
    {
        var rows   = Rows(100, 200, 300, 400);
        var result = _engine.Apply(rows, [ComputedCol(ComputedTransform.PercentOfTotal)]);

        var sum = result.Sum(r => r["computed"].GetDouble());
        Assert.Equal(100.0, sum, precision: 6);
        Assert.Equal(10.0, result[0]["computed"].GetDouble(), precision: 6);  // 100/1000
        Assert.Equal(40.0, result[3]["computed"].GetDouble(), precision: 6);  // 400/1000
    }

    // ------------------------------------------------------------------
    // running_total
    // ------------------------------------------------------------------

    [Fact]
    public void RunningTotal_IsCumulative()
    {
        var rows   = Rows(10, 20, 30);
        var result = _engine.Apply(rows, [ComputedCol(ComputedTransform.RunningTotal)]);

        Assert.Equal(10.0, result[0]["computed"].GetDouble());
        Assert.Equal(30.0, result[1]["computed"].GetDouble());
        Assert.Equal(60.0, result[2]["computed"].GetDouble());
    }

    // ------------------------------------------------------------------
    // moving_average_3
    // ------------------------------------------------------------------

    [Fact]
    public void MovingAverage3_FirstTwoRowsNull()
    {
        var rows   = Rows(10, 20, 30, 40, 50);
        var result = _engine.Apply(rows, [ComputedCol(ComputedTransform.MovingAverage3)]);

        Assert.Equal(JsonValueKind.Null, result[0]["computed"].ValueKind);
        Assert.Equal(JsonValueKind.Null, result[1]["computed"].ValueKind);
        Assert.Equal(20.0, result[2]["computed"].GetDouble());   // (10+20+30)/3
        Assert.Equal(30.0, result[3]["computed"].GetDouble());   // (20+30+40)/3
        Assert.Equal(40.0, result[4]["computed"].GetDouble());   // (30+40+50)/3
    }

    // ------------------------------------------------------------------
    // moving_average_7
    // ------------------------------------------------------------------

    [Fact]
    public void MovingAverage7_First6RowsNull()
    {
        var rows   = Rows(10, 20, 30, 40, 50, 60, 70, 80);
        var result = _engine.Apply(rows, [ComputedCol(ComputedTransform.MovingAverage7)]);

        for (var i = 0; i < 6; i++)
            Assert.Equal(JsonValueKind.Null, result[i]["computed"].ValueKind);

        Assert.Equal(40.0, result[6]["computed"].GetDouble()); // (10+20+30+40+50+60+70)/7
    }

    // ------------------------------------------------------------------
    // rank
    // ------------------------------------------------------------------

    [Fact]
    public void Rank_HighestValueIsRankOne()
    {
        var rows   = Rows(100, 300, 200);
        var result = _engine.Apply(rows, [ComputedCol(ComputedTransform.Rank)]);

        Assert.Equal(3, result[0]["computed"].GetInt32()); // 100 → rank 3
        Assert.Equal(1, result[1]["computed"].GetInt32()); // 300 → rank 1
        Assert.Equal(2, result[2]["computed"].GetInt32()); // 200 → rank 2
    }

    // ------------------------------------------------------------------
    // No computed columns → returns original rows unchanged
    // ------------------------------------------------------------------

    [Fact]
    public void Apply_NoComputedColumns_ReturnsOriginalRows()
    {
        var rows   = Rows(1, 2, 3);
        var result = _engine.Apply(rows, []);

        Assert.Same(rows, result);
    }
}
