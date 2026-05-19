namespace Providers.Tests.Matching;

public sealed class WildcardMatcherTests
{
    // T1: Literal beats wildcard — most specific registration wins
    [Fact]
    public void Resolve_LiteralBeatsSingleWildcard_BeatsMuliSegmentWildcard()
    {
        var literal   = MakeReg("ml.fraud.score", score: 300);
        var single    = MakeReg("ml.fraud.*",     score: 210);
        var multi     = MakeReg("ml.**",           score: 111);
        var all       = new[] { multi, single, literal };

        var result = WildcardMatcher.Resolve("ml.fraud.score", all);

        Assert.NotNull(result);
        Assert.Equal("ml.fraud.score", result!.OperationPattern);
    }

    // T2: Fallback chain — only multi-segment wildcard registered
    [Fact]
    public void Resolve_FallbackChain_MultiSegmentMatchesDeepOperation()
    {
        var multi = MakeReg("ml.**");
        var all   = new[] { multi };

        var found = WildcardMatcher.Resolve("ml.fraud.score", all);
        Assert.NotNull(found);
        Assert.Equal("ml.**", found!.OperationPattern);

        var notFound = WildcardMatcher.Resolve("dashboard.render", all);
        Assert.Null(notFound);
    }

    [Theory]
    [InlineData("ml.fraud.score",    "ml.fraud.score", true)]
    [InlineData("ml.fraud.score",    "ml.fraud.*",     true)]
    [InlineData("ml.fraud.score",    "ml.**",          true)]
    [InlineData("ml.fraud.score",    "dashboard.render", false)]
    [InlineData("ml.fraud.score.v2", "ml.fraud.*",     false)]   // single * won't cross dot
    [InlineData("ml.fraud.score.v2", "ml.**",          true)]
    [InlineData("ml.fraud.score",    "ml.*",           false)]   // ml.* matches only one segment after ml
    public void Matches_Scenarios(string operation, string pattern, bool expected)
    {
        Assert.Equal(expected, WildcardMatcher.Matches(operation, pattern));
    }

    [Fact]
    public void Resolve_InactiveRegistrations_AreIgnored()
    {
        var inactive = MakeReg("ml.fraud.score", status: "disabled");
        var active   = MakeReg("ml.**");

        var result = WildcardMatcher.Resolve("ml.fraud.score", new[] { inactive, active });

        Assert.NotNull(result);
        Assert.Equal("ml.**", result!.OperationPattern);
    }

    private static OperationRegistration MakeReg(string pattern, int score = 0, string status = "active") =>
        new()
        {
            OperationPattern = pattern,
            HandlerType      = "internal",
            Status           = status,
        };
}
