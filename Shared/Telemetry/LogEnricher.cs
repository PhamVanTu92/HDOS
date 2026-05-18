using Serilog.Core;
using Serilog.Events;

namespace ReportingPlatform.Telemetry;

// Stamps every log event with the current OTel Activity's requestId, tenantId,
// and correlationId baggage items so all log lines for a request are co-queryable.
public sealed class RequestContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        var activity = Activity.Current;
        if (activity is null)
            return;

        AddBaggageTag(logEvent, factory, activity, "requestId");
        AddBaggageTag(logEvent, factory, activity, "tenantId");
        AddBaggageTag(logEvent, factory, activity, "correlationId");

        // Always stamp traceId + spanId for correlation with the trace backend.
        logEvent.AddPropertyIfAbsent(factory.CreateProperty("traceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(factory.CreateProperty("spanId",  activity.SpanId.ToString()));
    }

    private static void AddBaggageTag(
        LogEvent logEvent,
        ILogEventPropertyFactory factory,
        Activity activity,
        string key)
    {
        var value = activity.GetBaggageItem(key);
        if (value is not null)
            logEvent.AddPropertyIfAbsent(factory.CreateProperty(key, value));
    }
}
