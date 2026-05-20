using ReportingPlatform.Caching;
using ReportingPlatform.Contracts.Enums;
using ReportingPlatform.Contracts.Envelopes;
using ReportingPlatform.Contracts.Exceptions;
using ReportingPlatform.Contracts.Operations;
using ReportingPlatform.Contracts.Store;
using StackExchange.Redis;

namespace ReportingPlatform.Adapters.Implementations;

internal sealed class ExternalProviderAdapter : IDatasourceAdapter
{
    // Regex-free token pattern: "{{filters.key}}" prefix/suffix literals
    private const string TokenPrefix = "{{filters.";
    private const string TokenSuffix = "}}";

    private readonly INestedRequestSubmitter _submission;
    private readonly IResultReader           _results;
    private readonly ISubscriber             _subscriber;
    private readonly ILogger<ExternalProviderAdapter> _logger;

    public ExternalProviderAdapter(
        INestedRequestSubmitter           submission,
        IResultReader                     results,
        ISubscriber                       subscriber,
        ILogger<ExternalProviderAdapter>  logger)
    {
        _submission  = submission;
        _results     = results;
        _subscriber  = subscriber;
        _logger      = logger;
    }

    public async Task<AdapterResult> FetchAsync(AdapterRequest request, CancellationToken ct = default)
    {
        var config = ParseConfig(request.Datasource.ConnectionConfig);

        // Patch 2: effective timeout = min(config, parent remaining).
        var configTimeout = TimeSpan.FromMilliseconds(Math.Min(config.TimeoutMs, 30_000));
        var effectiveTimeout = configTimeout;

        if (request.ParentDeadline.HasValue)
        {
            var parentRemaining = request.ParentDeadline.Value - DateTimeOffset.UtcNow;
            if (parentRemaining <= TimeSpan.Zero)
                throw new AdapterException("PROVIDER_TIMEOUT", "Parent deadline already exceeded.");
            if (parentRemaining < configTimeout)
                effectiveTimeout = parentRemaining;
        }

        var nestedId = Guid.NewGuid().ToString("N");
        var tcs      = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal = RedisChannel.Literal(RedisKeys.SseTerminal(nestedId));

        // ── Subscribe BEFORE submit to avoid notification race ────────────
        await _subscriber.SubscribeAsync(terminal, (_, _) => tcs.TrySetResult(true));

        // EP12: progress forwarding — subscribe BEFORE submit (same race avoidance pattern).
        ISubscriber? progressForwarder = null;
        if (request.ParentWantsProgress && request.ParentRequestId is not null)
        {
            var progressIn  = RedisChannel.Literal(RedisKeys.SseNotify(nestedId));
            var progressOut = RedisChannel.Literal(RedisKeys.SseNotify(request.ParentRequestId));
            progressForwarder = _subscriber;
            await _subscriber.SubscribeAsync(progressIn,
                async (_, value) =>
                {
                    try { await progressForwarder.PublishAsync(progressOut, value); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to forward progress to parent {ParentId}", request.ParentRequestId);
                    }
                });
        }

        try
        {
            var envelope = BuildEnvelope(nestedId, request, config, effectiveTimeout);

            try
            {
                await _submission.SubmitAsync(envelope, connectionId: null, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (OperationException ex) when (ex.Code is "OPERATION_NOT_FOUND" or "OPERATION_NOT_ACTIVE")
            {
                throw new AdapterException("PROVIDER_OPERATION_NOT_FOUND",
                    $"Operation '{config.OperationName}' not found or inactive.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(effectiveTimeout);

            try
            {
                await tcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new AdapterException("PROVIDER_TIMEOUT",
                    $"External provider '{config.OperationName}' did not respond within {effectiveTimeout.TotalMilliseconds:F0} ms.");
            }
        }
        finally
        {
            // Best-effort unsubscribe — don't throw over a cleanup failure.
            try { await _subscriber.UnsubscribeAsync(terminal); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to unsubscribe from terminal channel {Channel}", terminal); }

            if (request.ParentWantsProgress && request.ParentRequestId is not null)
            {
                try { await _subscriber.UnsubscribeAsync(RedisChannel.Literal(RedisKeys.SseNotify(nestedId))); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to unsubscribe from progress channel for {NestedId}", nestedId); }
            }
        }

        var record = await _results.GetAsync(nestedId, ct)
            ?? throw new AdapterException("PROVIDER_RESULT_MISSING",
                $"Result not found in store for nested request '{nestedId}'.");

        return MapToAdapterResult(record, config);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ExternalProviderConfig ParseConfig(JsonElement connectionConfig)
    {
        try
        {
            return connectionConfig.Deserialize(AdaptersJsonContext.Default.ExternalProviderConfig)
                ?? throw new AdapterException("PROVIDER_CONFIG_INVALID",
                    "connectionConfig deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new AdapterException("PROVIDER_CONFIG_INVALID",
                $"connectionConfig is not a valid ExternalProviderConfig: {ex.Message}");
        }
    }

    private static RequestEnvelope BuildEnvelope(
        string nestedId,
        AdapterRequest request,
        ExternalProviderConfig config,
        TimeSpan effectiveTimeout)
    {
        var paramsEl = BuildParams(config.ParamMapping, request.Filters);

        return new RequestEnvelope
        {
            RequestId     = nestedId,
            TenantId      = request.TenantId,
            UserId        = request.UserId ?? "system",    // Patch 1: caller identity
            CorrelationId = request.ParentRequestId,       // parent requestId for correlation
            Operation     = config.OperationName,
            Params        = paramsEl,
            ProviderId    = config.ProviderId,             // EP13: routing hint
            Options = new RequestOptions
            {
                TimeoutMs = (int)effectiveTimeout.TotalMilliseconds,
                Progress  = false,
            },
        };
    }

    private static JsonElement BuildParams(
        IReadOnlyDictionary<string, string> mapping,
        IReadOnlyDictionary<string, JsonElement> filters)
    {
        var dict = new Dictionary<string, JsonElement>(mapping.Count);
        foreach (var (paramName, template) in mapping)
        {
            dict[paramName] = ResolveToken(template, filters);
        }
        return JsonSerializer.SerializeToElement(dict);
    }

    private static JsonElement ResolveToken(
        string template,
        IReadOnlyDictionary<string, JsonElement> filters)
    {
        if (template.StartsWith(TokenPrefix, StringComparison.Ordinal) &&
            template.EndsWith(TokenSuffix, StringComparison.Ordinal))
        {
            var key = template.Substring(
                TokenPrefix.Length,
                template.Length - TokenPrefix.Length - TokenSuffix.Length);

            if (filters.TryGetValue(key, out var val))
                return val;

            return JsonSerializer.SerializeToElement<object?>(null);
        }

        // Literal string value
        return JsonSerializer.SerializeToElement(template);
    }

    private static AdapterResult MapToAdapterResult(ResultStoreRecord record, ExternalProviderConfig config)
    {
        switch (record.Status)
        {
            case ResponseStatus.Done:
                break;
            case ResponseStatus.Failed:
                var code = record.Error?.Code ?? "PROVIDER_FAILED";
                throw new AdapterException("PROVIDER_FAILED", code);
            case ResponseStatus.Cancelled:
                throw new AdapterException("PROVIDER_CANCELLED",
                    $"Provider cancelled operation '{config.OperationName}'.");
            default:
                throw new AdapterException("PROVIDER_FAILED",
                    $"Unexpected provider status '{record.Status}'.");
        }

        if (string.IsNullOrWhiteSpace(record.PayloadJson))
            return new AdapterResult { Rows = [], TotalRows = null, Schema = null };

        JsonDocument payloadDoc;
        try
        {
            payloadDoc = JsonDocument.Parse(record.PayloadJson);
        }
        catch (JsonException ex)
        {
            throw new AdapterException("PROVIDER_PAYLOAD_INVALID",
                $"Provider payload is not valid JSON: {ex.Message}");
        }

        using (payloadDoc)
        {
            var root = payloadDoc.RootElement;

            // Navigate to rowsPath if specified; otherwise use root["rows"]
            JsonElement rowsEl;
            if (config.RowsPath is not null)
            {
                if (!root.TryGetProperty(config.RowsPath, out rowsEl))
                    throw new AdapterException("PROVIDER_PAYLOAD_INVALID",
                        $"Payload has no '{config.RowsPath}' property (rowsPath).");
            }
            else
            {
                if (!root.TryGetProperty("rows", out rowsEl))
                    throw new AdapterException("PROVIDER_PAYLOAD_INVALID",
                        "Payload has no 'rows' property. Set rowsPath to specify an alternate path.");
            }

            if (rowsEl.ValueKind != JsonValueKind.Array)
                throw new AdapterException("PROVIDER_PAYLOAD_INVALID",
                    $"Rows value at '{config.RowsPath ?? "rows"}' is not a JSON array.");

            long? totalRows = root.TryGetProperty("totalRows", out var tr) && tr.ValueKind == JsonValueKind.Number
                ? tr.GetInt64()
                : null;

            IReadOnlyList<ColumnDescriptor>? schema = null;
            if (root.TryGetProperty("schema", out var schemaEl) && schemaEl.ValueKind == JsonValueKind.Array)
            {
                var cols = new List<ColumnDescriptor>();
                foreach (var col in schemaEl.EnumerateArray())
                {
                    if (col.ValueKind != JsonValueKind.Object) continue;
                    var key  = col.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    var type = col.TryGetProperty("type", out var t) ? t.GetString() ?? "string" : "string";
                    cols.Add(new ColumnDescriptor { Key = key, Type = type });
                }
                schema = cols;
            }

            var rows = new List<IReadOnlyDictionary<string, JsonElement>>();
            foreach (var row in rowsEl.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var prop in row.EnumerateObject())
                    dict[prop.Name] = prop.Value.Clone();
                rows.Add(dict);
            }

            return new AdapterResult
            {
                Rows      = rows,
                TotalRows = totalRows,
                Schema    = schema,
            };
        }
    }
}
