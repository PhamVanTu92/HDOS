using System.Text.Json;
using ReportingPlatform.Contracts.RenderPayloads.Shared;
using ReportingPlatform.Transformers.Filter;
using ReportingPlatform.Transformers.Tests.Helpers;
using Xunit;

namespace ReportingPlatform.Transformers.Tests.Filter;

public sealed class FilterTransformerTests
{
    // ------------------------------------------------------------------
    // FilterDropdown
    // ------------------------------------------------------------------

    [Fact]
    public async Task FilterDropdownTransformer_StaticOptions_NoAdapterCall()
    {
        var vc = """
        {
          "filterKey": "status",
          "label": "Status",
          "staticOptions": [
            {"value":"active","label":"Active"},
            {"value":"inactive","label":"Inactive"}
          ]
        }
        """;
        var t   = new FilterDropdownTransformer();
        var ctx = TransformerTestHelpers.Ctx("filter_dropdown", vc);

        // No adapter rows provided — options come from staticOptions only
        var result = await t.TransformAsync([], null, ctx);

        Assert.Equal("status", result.GetProperty("filterKey").GetString());
        var opts = result.GetProperty("options");
        Assert.Equal(2, opts.GetArrayLength());
        Assert.Equal("active", opts[0].GetProperty("value").GetString());
        TransformerTestHelpers.AssertMatchesGolden("FilterDropdownTransformer", result);
    }

    [Fact]
    public async Task FilterDropdownTransformer_PreFetchedOptions_UsedInstead()
    {
        var vc  = """{"filterKey":"region","label":"Region","optionsSource":{"source":"regions"}}""";
        var t   = new FilterDropdownTransformer();

        var prefetched = new Dictionary<string, IReadOnlyList<FilterOption>>
        {
            ["w1"] = new[]
            {
                new FilterOption { Value = "apac", Label = "APAC" },
                new FilterOption { Value = "emea", Label = "EMEA" },
            },
        };

        var ctx = TransformerTestHelpers.Ctx("filter_dropdown", vc) with
        {
            DropdownOptions = prefetched,
        };

        var result = await t.TransformAsync([], null, ctx);
        var opts   = result.GetProperty("options");

        // Pre-fetched options used (2 items), NOT adapter rows
        Assert.Equal(2, opts.GetArrayLength());
        Assert.Equal("apac", opts[0].GetProperty("value").GetString());
    }

    // ------------------------------------------------------------------
    // FilterDateRange
    // ------------------------------------------------------------------

    [Fact]
    public async Task FilterDateRangeTransformer_MatchesGoldenFile()
    {
        var vc  = """{"filterKey":"date","label":"Date Range"}""";
        // Fixed clock: 2026-01-15 UTC — golden file uses static dates 2026-01-08 / 2026-01-15
        // so the test never drifts with the calendar.
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        var t   = new FilterDateRangeTransformer(clock);
        var ctx = TransformerTestHelpers.Ctx("filter_date_range", vc);

        var result = await t.TransformAsync([], null, ctx);
        Assert.Equal("date", result.GetProperty("filterKey").GetString());
        Assert.True(result.GetProperty("presets").GetArrayLength() > 0);
        TransformerTestHelpers.AssertMatchesGolden("FilterDateRangeTransformer", result);
    }

    // ------------------------------------------------------------------
    // FilterSlider
    // ------------------------------------------------------------------

    [Fact]
    public async Task FilterSliderTransformer_MatchesGoldenFile()
    {
        var vc  = """{"filterKey":"price","label":"Price","min":0,"max":1000,"step":10,"format":"currency"}""";
        var t   = new FilterSliderTransformer();
        var ctx = TransformerTestHelpers.Ctx("filter_slider", vc);

        var result = await t.TransformAsync([], null, ctx);
        Assert.Equal(0.0,    result.GetProperty("min").GetDouble());
        Assert.Equal(1000.0, result.GetProperty("max").GetDouble());
        TransformerTestHelpers.AssertMatchesGolden("FilterSliderTransformer", result);
    }

    // ------------------------------------------------------------------
    // FilterSearch
    // ------------------------------------------------------------------

    [Fact]
    public async Task FilterSearchTransformer_MatchesGoldenFile()
    {
        var vc  = """{"filterKey":"q","label":"Search","placeholder":"Type to search..."}""";
        var t   = new FilterSearchTransformer();
        var ctx = TransformerTestHelpers.Ctx("filter_search", vc);

        var result = await t.TransformAsync([], null, ctx);
        Assert.Equal("q",                  result.GetProperty("filterKey").GetString());
        Assert.Equal("Type to search...",  result.GetProperty("placeholder").GetString());
        TransformerTestHelpers.AssertMatchesGolden("FilterSearchTransformer", result);
    }
}
