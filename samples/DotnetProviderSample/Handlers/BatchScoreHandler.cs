using ReportingPlatform.ProviderSdk;

namespace DotnetProviderSample.Handlers;

public sealed class BatchScoreHandler : IOperationHandler<BatchScoreParams, BatchScoreResult>
{
    private const int ChunkSize = 10;

    public async Task<OperationResult<BatchScoreResult>> HandleAsync(
        OperationContext<BatchScoreParams> ctx, CancellationToken ct)
    {
        var txns    = ctx.Params.Transactions;
        var results = new List<FraudScoreResult>(txns.Length);
        var chunks  = (int)Math.Ceiling(txns.Length / (double)ChunkSize);

        for (int chunk = 0; chunk < chunks; chunk++)
        {
            ct.ThrowIfCancellationRequested();
            var slice = txns.Skip(chunk * ChunkSize).Take(ChunkSize);
            foreach (var txn in slice)
            {
                var score    = Random.Shared.NextDouble();
                var riskBand = score < 0.30 ? "LOW" : score < 0.70 ? "MEDIUM" : "HIGH";
                results.Add(new FraudScoreResult
                {
                    TransactionId = txn.TransactionId,
                    Score         = Math.Round(score, 4),
                    RiskBand      = riskBand,
                });
            }

            // Small mock latency per chunk
            await Task.Delay(5, ct);

            var percent = Math.Min(99, (int)((chunk + 1) / (double)chunks * 99));
            await ctx.Progress.ReportAsync(percent, $"Scored chunk {chunk + 1}/{chunks}", ct);
        }

        return OperationResult<BatchScoreResult>.Success(new BatchScoreResult
        {
            Results   = results.ToArray(),
            Processed = results.Count,
        });
    }
}
