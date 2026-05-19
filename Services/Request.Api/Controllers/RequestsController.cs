namespace ReportingPlatform.RequestApi.Controllers;

/// <summary>
/// HTTP API for request submission, result polling, and cancellation.
/// All endpoints are transport-agnostic — they delegate to the same
/// <see cref="RequestSubmissionService"/> used by the Hub.
/// </summary>
[ApiController]
[Route("api/v1/requests")]
[Authorize]
public sealed class RequestsController : ControllerBase
{
    private readonly RequestSubmissionService _submission;
    private readonly OwnerStore              _ownerStore;
    private readonly ResultStore             _resultStore;
    private readonly OrphanDetector          _orphanDetector;
    private readonly ICancelBus              _cancelBus;

    public RequestsController(
        RequestSubmissionService submission,
        OwnerStore ownerStore,
        ResultStore resultStore,
        OrphanDetector orphanDetector,
        ICancelBus cancelBus)
    {
        _submission     = submission;
        _ownerStore     = ownerStore;
        _resultStore    = resultStore;
        _orphanDetector = orphanDetector;
        _cancelBus      = cancelBus;
    }

    // ── POST /api/v1/requests ────────────────────────────────────────────────

    /// <summary>
    /// Submit an operation request. Returns 202 Accepted with a <see cref="SubmitAck"/>.
    /// The terminal result arrives later via SignalR push.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SubmitAck), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SubmitAsync(
        [FromBody] RequestEnvelope envelope,
        [FromHeader(Name = "X-Connection-Id")] string? connectionId,
        CancellationToken ct)
    {
        EnforceTenantMatch(envelope.TenantId);
        var ack = await _submission.SubmitAsync(envelope, connectionId, ct);
        return Accepted(ack);
    }

    // ── GET /api/v1/requests/{requestId}/result ──────────────────────────────

    /// <summary>
    /// Reconnection fallback. Returns a uniform envelope with a <c>status</c> discriminator.
    /// </summary>
    [HttpGet("{requestId}/result")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetResultAsync(string requestId, CancellationToken ct)
    {
        // 1. Terminal result cached (5-min TTL)
        var stored = await _resultStore.GetAsync(requestId, ct);
        if (stored is not null)
        {
            return Ok(new
            {
                status    = "completed",
                requestId,
                result    = stored,
            });
        }

        // 2. Owner record present — still in flight
        var owner = await _ownerStore.GetAsync(requestId, ct);
        if (owner is not null)
        {
            return Accepted(new
            {
                status      = "in_flight",
                requestId,
                submittedAt = owner.SubmittedAt,
            });
        }

        // 3. Orphan detection
        var orphanStatus = await _orphanDetector.CheckAsync(requestId, ct);
        return NotFound(new { status = orphanStatus, requestId });
    }

    // ── POST /api/v1/requests/{requestId}/cancel ─────────────────────────────

    /// <summary>
    /// Cancel a request. Best-effort — the operation may already have completed.
    /// Returns 202 Accepted immediately.
    /// </summary>
    [HttpPost("{requestId}/cancel")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CancelAsync(string requestId, CancellationToken ct)
    {
        var userId   = UserId();
        var tenantId = TenantId();
        await _cancelBus.PublishCancelAsync(requestId, userId, tenantId, ct);
        return Accepted();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private string UserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException();

    private string TenantId() =>
        User.FindFirstValue("tenant")
        ?? throw new UnauthorizedAccessException();

    private void EnforceTenantMatch(string envelopeTenantId)
    {
        var jwtTenant = User.FindFirstValue("tenant");
        if (!string.Equals(jwtTenant, envelopeTenantId, StringComparison.Ordinal))
            throw new OperationException("FORBIDDEN", "Tenant claim mismatch.");
    }
}
