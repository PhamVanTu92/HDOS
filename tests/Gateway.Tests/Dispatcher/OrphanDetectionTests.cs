namespace ReportingPlatform.Gateway.Tests.Dispatcher;

/// <summary>
/// OR1–OR2: Orphan detection tests.
/// Exercises <see cref="OrphanDetector"/> with a <see cref="FakeDatabase"/>.
/// </summary>
public sealed class OrphanDetectionTests
{
    // ── OR1 — Submission log present → "orphaned" ────────────────────────────

    [Fact]
    public async Task OR1_SubmissionLogPresent_ReturnsOrphaned()
    {
        var db = new FakeDatabase();
        // Pre-populate submission log (written by RequestSubmissionService in production)
        db.StringSet(RedisKeys.SubmissionLog("req-or1"), "1");

        var detector = new OrphanDetector(db.Inner, NullLogger<OrphanDetector>.Instance);
        var status   = await detector.CheckAsync("req-or1");

        Assert.Equal("orphaned", status);
    }

    // ── OR2 — No submission log → "not_found" ────────────────────────────────

    [Fact]
    public async Task OR2_NoSubmissionLog_ReturnsNotFound()
    {
        var db       = new FakeDatabase(); // empty — no keys set
        var detector = new OrphanDetector(db.Inner, NullLogger<OrphanDetector>.Instance);
        var status   = await detector.CheckAsync("req-or2");

        Assert.Equal("not_found", status);
    }
}
