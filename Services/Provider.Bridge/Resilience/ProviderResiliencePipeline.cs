using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ReportingPlatform.Bridge.Resilience;

public sealed class ProviderResiliencePipeline
{
    private readonly ConcurrentDictionary<string, ResiliencePipeline> _pipelines = new();

    public ResiliencePipeline GetOrCreate(ProviderRegistration registration)
    {
        return _pipelines.GetOrAdd(registration.ProviderId, _ => Build(registration));
    }

    private static ResiliencePipeline Build(ProviderRegistration reg)
    {
        var cb = reg.CircuitBreaker;
        return new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromMilliseconds(reg.TimeoutMs),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio      = cb.FailureThreshold / 100.0,
                SamplingDuration  = TimeSpan.FromSeconds(cb.WindowSeconds),
                BreakDuration     = TimeSpan.FromSeconds(cb.CooldownSeconds),
                MinimumThroughput = 3,
                ShouldHandle      = args => ValueTask.FromResult(args.Outcome.Exception is not null),
            })
            .Build();
    }
}
