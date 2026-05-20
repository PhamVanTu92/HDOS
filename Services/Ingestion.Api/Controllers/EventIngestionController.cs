using ReportingPlatform.IngestionApi.Services;

namespace ReportingPlatform.IngestionApi.Controllers;

[ApiController]
[Route("api/v1/events")]
[Authorize(Policy = "IngestionScope")]
public sealed class EventIngestionController : ControllerBase
{
    private const int MaxBatchSize = 1_000;

    private readonly IPublishEndpoint _bus;
    private readonly ISchemaValidator _schemaValidator;
    private readonly ILogger<EventIngestionController> _logger;

    public EventIngestionController(
        IPublishEndpoint bus,
        ISchemaValidator schemaValidator,
        ILogger<EventIngestionController> logger)
    {
        _bus             = bus;
        _schemaValidator = schemaValidator;
        _logger          = logger;
    }

    /// <summary>POST /api/v1/events — ingest a single event.</summary>
    [HttpPost]
    public Task<IActionResult> IngestSingle(
        [FromBody] IngestSingleRequest request,
        CancellationToken ct)
        => IngestCoreAsync([request], ct);

    /// <summary>POST /api/v1/events/batch — ingest up to 1 000 events.</summary>
    [HttpPost("batch")]
    public async Task<IActionResult> IngestBatch(
        [FromBody] IngestBatchRequest batch,
        CancellationToken ct)
    {
        if (batch.Events.Count > MaxBatchSize)
            return BadRequest(new IngestErrorResponse
            {
                Error   = "BATCH_TOO_LARGE",
                Message = $"Batch limit is {MaxBatchSize} events.",
            });

        return await IngestCoreAsync(batch.Events, ct);
    }

    // ── Shared pipeline ────────────────────────────────────────────────

    private async Task<IActionResult> IngestCoreAsync(
        IReadOnlyList<IngestSingleRequest> events,
        CancellationToken ct)
    {
        // TenantId comes from JWT — never from the request body (Patch 3 invariant).
        var tenantId = User.FindFirstValue("tenant_id")
                    ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new InvalidOperationException("tenant_id claim missing from JWT.");

        var eventIds  = new List<string>(events.Count);
        var envelopes = new List<IngestEventEnvelope>(events.Count);

        foreach (var req in events)
        {
            // Optional schema validation (§1.5 / §1.5.1)
            var validationError = await _schemaValidator.ValidateAsync(
                tenantId, req.EventType, req.Payload, ct);

            if (validationError is not null)
                return UnprocessableEntity(new IngestErrorResponse
                {
                    Error   = "EVENT_SCHEMA_VIOLATION",
                    Message = validationError,
                });

            var eventId = Guid.CreateVersion7().ToString("N");
            eventIds.Add(eventId);

            envelopes.Add(new IngestEventEnvelope
            {
                EventType  = req.EventType,
                TenantId   = tenantId,
                OccurredAt = req.OccurredAt,
                Payload    = req.Payload,
            });
        }

        // Publish all envelopes to events.raw topic exchange.
        foreach (var envelope in envelopes)
            await _bus.Publish(envelope, ct);

        _logger.LogInformation(
            "Accepted {Count} event(s) for tenant {TenantId}",
            envelopes.Count, tenantId);

        return StatusCode(201, new IngestResponse
        {
            Accepted = envelopes.Count,
            EventIds = eventIds,
        });
    }
}
