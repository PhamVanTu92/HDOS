using ReportingPlatform.ProviderSdk;

namespace DotnetProviderSample.Handlers;

public sealed class FraudScoreHandler : IOperationHandler<FraudScoreParams, FraudScoreResult>
{
    public async Task<OperationResult<FraudScoreResult>> HandleAsync(
        OperationContext<FraudScoreParams> ctx, CancellationToken ct)
    {
        // Mock 2ms latency
        await Task.Delay(2, ct);

        var score    = Random.Shared.NextDouble();
        var riskBand = score < 0.30 ? "LOW" : score < 0.70 ? "MEDIUM" : "HIGH";

        return OperationResult<FraudScoreResult>.Success(new FraudScoreResult
        {
            TransactionId = ctx.Params.TransactionId,
            Score         = Math.Round(score, 4),
            RiskBand      = riskBand,
        });
    }
}
