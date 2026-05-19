using Providers.Tests.Fakes;

namespace Providers.Tests.Concurrency;

// T6: No torn state or null OperationPattern under concurrent reads during reload.
// Docker-free — tests the in-memory snapshot pattern, not Postgres I/O.
public sealed class T6_ConcurrentReadsTests
{
    [Fact]
    public async Task ConcurrentReadsAcrossReloads_NoTornState()
    {
        var registry    = new FakeOperationRegistry();
        var initialOps  = GenerateRegistrations(count: 100);
        registry.SeedSnapshot(initialOps);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int errorCount = 0;
        int readCount  = 0;

        // 50 concurrent reader tasks
        var readerTasks = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var op     = $"op.{Random.Shared.Next(0, 100)}";
                var result = await registry.ResolveAsync(op, cts.Token);

                // Torn state would manifest as a non-null result with a null OperationPattern
                if (result is not null && result.OperationPattern is null)
                    Interlocked.Increment(ref errorCount);

                Interlocked.Increment(ref readCount);
            }
        })).ToList();

        // 10 reload tasks, each seeding 5 new snapshots with a slight delay between swaps
        var reloadTasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            for (int j = 0; j < 5; j++)
            {
                var newOps = GenerateRegistrations(count: 100 + j + (i * 5));
                registry.SeedSnapshot(newOps);
                Thread.Sleep(50);
            }
        })).ToList();

        await Task.WhenAll(reloadTasks);
        cts.Cancel();

        try { await Task.WhenAll(readerTasks); }
        catch (OperationCanceledException) { }

        Assert.Equal(0, errorCount);
        Assert.True(readCount > 10_000, $"Expected ≥ 10,000 reads; got {readCount}. Readers may not have run.");
    }

    private static IReadOnlyList<OperationRegistration> GenerateRegistrations(int count) =>
        Enumerable.Range(0, count).Select(i => new OperationRegistration
        {
            OperationPattern = $"op.{i}",
            HandlerType      = "internal",
            Status           = "active",
        }).ToList();
}
