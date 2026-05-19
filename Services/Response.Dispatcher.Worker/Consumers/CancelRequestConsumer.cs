using ReportingPlatform.Contracts.Enums;
using ReportingPlatform.Contracts.Responses;

namespace ReportingPlatform.ResponseDispatcher.Consumers;

/// <summary>
/// Handles <see cref="CancelRequestMessage"/> by synthesising a
/// <see cref="OperationResponseMessage"/> with <c>Status = Cancelled</c> and routing it.
/// This is Path A of the cancel race (PHASE_7_PLAN.md §9):
/// the cancellation signal arrives before (or racing with) the operation completing.
/// <para>
/// Because <see cref="ResponseRouter"/> deletes the owner record after the first push,
/// a duplicate response from the Router's own cancellation detection (Path B) will find
/// no owner and produce no duplicate SignalR push.
/// </para>
/// </summary>
public sealed class CancelRequestConsumer(
    ResponseRouter router,
    ILogger<CancelRequestConsumer> logger) : IConsumer<CancelRequestMessage>
{
    public async Task Consume(ConsumeContext<CancelRequestMessage> ctx)
    {
        var msg = ctx.Message;
        logger.LogInformation("Cancel signal for requestId={RequestId} userId={UserId}",
            msg.RequestId, msg.UserId);

        var syntheticResponse = new OperationResponseMessage
        {
            RequestId = msg.RequestId,
            Status    = ResponseStatus.Cancelled,
            TenantId  = msg.TenantId,
            UserId    = msg.UserId,
            Error     = new ErrorDetail
            {
                Code      = "CANCELLED",
                Message   = "Request cancelled by user.",
                Retryable = false,
            },
        };

        await router.RouteAsync(syntheticResponse, ctx.CancellationToken);
    }
}
