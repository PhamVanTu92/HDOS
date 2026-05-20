using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ReportingPlatform.IngestionApi.Controllers;
using ReportingPlatform.IngestionApi.Models;
using ReportingPlatform.IngestionApi.Services;

namespace ReportingPlatform.Ingestion.Tests.Controllers;

/// <summary>IN1–IN9 — EventIngestionController unit tests.</summary>
public sealed class EventIngestionControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static readonly JsonElement EmptyPayload =
        JsonDocument.Parse("{}").RootElement;

    private static EventIngestionController BuildController(
        IPublishEndpoint? bus = null,
        ISchemaValidator? validator = null,
        string tenantId = "tenant-a")
    {
        bus       ??= Substitute.For<IPublishEndpoint>();
        validator ??= PassthroughValidator();

        var ctrl = new EventIngestionController(
            bus, validator,
            NullLogger<EventIngestionController>.Instance);

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("tenant_id", tenantId),
                    new Claim("scope", "ingestion"),
                ], authenticationType: "Test")),
            },
        };

        return ctrl;
    }

    private static ISchemaValidator PassthroughValidator()
    {
        var v = Substitute.For<ISchemaValidator>();
        v.ValidateAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        return v;
    }

    private static IngestSingleRequest MakeReq(
        string eventType  = "order.shipped",
        string occurredAt = "2026-05-20T10:00:00Z")
        => new()
        {
            EventType  = eventType,
            OccurredAt = occurredAt,
            Payload    = EmptyPayload,
        };

    // ─── IN1: Happy path — single event ─────────────────────────────────────

    [Fact]
    public async Task IN1_SingleEvent_ValidJwt_Returns201_PublishesToBus()
    {
        var bus  = Substitute.For<IPublishEndpoint>();
        var ctrl = BuildController(bus: bus);

        var result = await ctrl.IngestSingle(MakeReq(), CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);

        var body = Assert.IsType<IngestResponse>(created.Value);
        Assert.Equal(1, body.Accepted);
        Assert.Single(body.EventIds);

        await bus.Received(1).Publish(
            Arg.Is<IngestEventEnvelope>(e =>
                e.TenantId  == "tenant-a" &&
                e.EventType == "order.shipped"),
            Arg.Any<CancellationToken>());
    }

    // ─── IN2: Batch of 1000 events — all accepted ────────────────────────────

    [Fact]
    public async Task IN2_BatchEvent_1000Items_AllPublished()
    {
        var bus    = Substitute.For<IPublishEndpoint>();
        var ctrl   = BuildController(bus: bus);
        var events = Enumerable.Range(0, 1_000)
            .Select(_ => MakeReq())
            .ToList();

        var result = await ctrl.IngestBatch(
            new IngestBatchRequest { Events = events }, CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);

        var body = Assert.IsType<IngestResponse>(created.Value);
        Assert.Equal(1_000, body.Accepted);
        await bus.Received(1_000)
            .Publish(Arg.Any<IngestEventEnvelope>(), Arg.Any<CancellationToken>());
    }

    // ─── IN3: Batch of 1001 — hard cap → 400 BATCH_TOO_LARGE ────────────────

    [Fact]
    public async Task IN3_BatchEvent_1001Items_Returns400_BATCH_TOO_LARGE()
    {
        var ctrl   = BuildController();
        var events = Enumerable.Range(0, 1_001)
            .Select(_ => MakeReq())
            .ToList();

        var result = await ctrl.IngestBatch(
            new IngestBatchRequest { Events = events }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var err = Assert.IsType<IngestErrorResponse>(bad.Value);
        Assert.Equal("BATCH_TOO_LARGE", err.Error);
    }

    // ─── IN4: Rate limit — implemented as integration test in Integration/EventIngestionIntegrationTests.cs

    // ─── IN5: Schema validation — no schema registered → pass-through ────────

    [Fact]
    public async Task IN5_SchemaValidation_NoSchemaRegistered_AnyPayloadAccepted()
    {
        var ctrl = BuildController(); // passthrough validator by default

        var result = await ctrl.IngestSingle(MakeReq(), CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    // ─── IN6: Schema validation — invalid payload → 422 SCHEMA_VIOLATION ─────

    [Fact]
    public async Task IN6_SchemaValidation_InvalidPayload_Returns422_SCHEMA_VIOLATION()
    {
        var validator = Substitute.For<ISchemaValidator>();
        validator.ValidateAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("orderId is required."));

        var ctrl = BuildController(validator: validator);

        var result = await ctrl.IngestSingle(MakeReq(), CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var err = Assert.IsType<IngestErrorResponse>(unprocessable.Value);
        Assert.Equal("EVENT_SCHEMA_VIOLATION", err.Error);
        Assert.Contains("orderId", err.Message);
    }

    // ─── IN7: No schema row registered → any payload accepted ─────────────────

    [Fact]
    public async Task IN7_SchemaValidation_NoSchema_AnyPayloadAccepted()
    {
        var ctrl = BuildController(); // passthrough returns null → accepted

        var req = MakeReq() with
        {
            Payload = JsonDocument.Parse("""{"unexpected":true}""").RootElement,
        };

        var result = await ctrl.IngestSingle(req, CancellationToken.None);
        Assert.Equal(201, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ─── IN8: TenantId from JWT — never from body ────────────────────────────

    [Fact]
    public async Task IN8_JwtTenant_TenantIdAlwaysFromJwt_NotBody()
    {
        var bus  = Substitute.For<IPublishEndpoint>();
        var ctrl = BuildController(bus: bus, tenantId: "jwt-tenant");

        await ctrl.IngestSingle(MakeReq(), CancellationToken.None);

        await bus.Received(1).Publish(
            Arg.Is<IngestEventEnvelope>(e => e.TenantId == "jwt-tenant"),
            Arg.Any<CancellationToken>());
    }

    // ─── IN9: Missing ingestion scope — implemented as integration test in Integration/EventIngestionIntegrationTests.cs
}
