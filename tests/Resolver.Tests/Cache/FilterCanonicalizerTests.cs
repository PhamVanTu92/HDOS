namespace ReportingPlatform.Resolver.Tests.Cache;

public sealed class FilterCanonicalizerTests
{
    // ---------------------------------------------------------------
    // Helper: build a filter dictionary from key/value pairs
    // ---------------------------------------------------------------

    private static IReadOnlyDictionary<string, JsonElement> Filters(
        params (string key, object? value)[] pairs)
    {
        var doc = JsonSerializer.SerializeToDocument(
            pairs.ToDictionary(p => p.key, p => p.value));
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    // ---------------------------------------------------------------
    // Rule 1: keys sorted alphabetically
    // ---------------------------------------------------------------

    [Fact]
    public void Canonicalize_SortsKeys_Alphabetically()
    {
        var f = Filters(("zzz", "last"), ("aaa", "first"), ("mmm", "middle"));
        var canonical = FilterCanonicalizer.Canonicalize(f);

        // Keys must appear in alphabetical order
        var aaaPos = canonical.IndexOf("\"aaa\"", StringComparison.Ordinal);
        var mmmPos = canonical.IndexOf("\"mmm\"", StringComparison.Ordinal);
        var zzzPos = canonical.IndexOf("\"zzz\"", StringComparison.Ordinal);

        Assert.True(aaaPos < mmmPos, "aaa must come before mmm");
        Assert.True(mmmPos < zzzPos, "mmm must come before zzz");
    }

    // ---------------------------------------------------------------
    // Rule 2: null / undefined values omitted
    // ---------------------------------------------------------------

    [Fact]
    public void Canonicalize_OmitsNulls()
    {
        var f = Filters(("present", "yes"), ("missing", null));
        var canonical = FilterCanonicalizer.Canonicalize(f);

        Assert.Contains("\"present\"", canonical);
        Assert.DoesNotContain("\"missing\"", canonical);
    }

    // ---------------------------------------------------------------
    // Rule 4: string values are case-preserved (not lowercased)
    // ---------------------------------------------------------------

    [Fact]
    public void Canonicalize_PreservesCase_OnStringValues()
    {
        var f = Filters(("status", "ACTIVE"));
        var canonical = FilterCanonicalizer.Canonicalize(f);

        Assert.Contains("\"ACTIVE\"", canonical);
    }

    // ---------------------------------------------------------------
    // Rule 5: array elements sorted alphabetically
    // ---------------------------------------------------------------

    [Fact]
    public void Canonicalize_SortsArrayElements()
    {
        // ["zebra","apple","mango"] should become ["apple","mango","zebra"]
        var arr = new[] { "zebra", "apple", "mango" };
        var f = new Dictionary<string, JsonElement>
        {
            ["tags"] = JsonSerializer.SerializeToElement(arr),
        };

        var canonical = FilterCanonicalizer.Canonicalize(f);

        var applePos = canonical.IndexOf("\"apple\"", StringComparison.Ordinal);
        var mangoPos = canonical.IndexOf("\"mango\"", StringComparison.Ordinal);
        var zebraPos = canonical.IndexOf("\"zebra\"", StringComparison.Ordinal);

        Assert.True(applePos < mangoPos, "apple must come before mango");
        Assert.True(mangoPos < zebraPos, "mango must come before zebra");
    }

    // ---------------------------------------------------------------
    // Rule 3 + hash stability: same filters always produce the same hash
    // ---------------------------------------------------------------

    [Fact]
    public void Hash_SameFilters_ProduceSameHash()
    {
        var f1 = Filters(("b", 2), ("a", 1));
        var f2 = Filters(("a", 1), ("b", 2)); // different insertion order

        var h1 = FilterCanonicalizer.Hash(FilterCanonicalizer.Canonicalize(f1));
        var h2 = FilterCanonicalizer.Hash(FilterCanonicalizer.Canonicalize(f2));

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void Hash_DifferentFilters_ProduceDifferentHash()
    {
        var f1 = Filters(("status", "active"));
        var f2 = Filters(("status", "inactive"));

        var h1 = FilterCanonicalizer.Hash(FilterCanonicalizer.Canonicalize(f1));
        var h2 = FilterCanonicalizer.Hash(FilterCanonicalizer.Canonicalize(f2));

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void Hash_Returns8Characters()
    {
        var canonical = FilterCanonicalizer.Canonicalize(Filters(("x", "y")));
        var hash = FilterCanonicalizer.Hash(canonical);
        Assert.Equal(8, hash.Length);
    }

    // ---------------------------------------------------------------
    // Rule 6: numbers use raw JSON decimal form
    // ---------------------------------------------------------------

    [Fact]
    public void Canonicalize_Number_UsesRawText()
    {
        var f = new Dictionary<string, JsonElement>
        {
            ["amount"] = JsonSerializer.SerializeToElement(42.5),
        };

        var canonical = FilterCanonicalizer.Canonicalize(f);

        // Raw JSON number — no surrounding quotes
        Assert.Contains("42.5", canonical);
        Assert.DoesNotContain("\"42.5\"", canonical);
    }

    // ---------------------------------------------------------------
    // Empty filters → "{}"
    // ---------------------------------------------------------------

    [Fact]
    public void Canonicalize_EmptyFilters_ReturnsEmptyObject()
    {
        var f = new Dictionary<string, JsonElement>();
        var canonical = FilterCanonicalizer.Canonicalize(f);
        Assert.Equal("{}", canonical);
    }
}
