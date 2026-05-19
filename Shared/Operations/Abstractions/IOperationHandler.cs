namespace ReportingPlatform.Operations.Abstractions;

/// <summary>
/// Contract for all operation handlers. Each implementation is singleton-safe
/// (stateless); all per-request state flows through <see cref="OperationHandlerContext"/>.
/// </summary>
public interface IOperationHandler
{
    /// <summary>
    /// Dot-notation operation name, e.g. <c>"dashboard.render"</c>.
    /// Must match the operation name registered in <c>operation_registry</c>.
    /// </summary>
    string OperationName { get; }

    /// <summary>
    /// Execute the operation. Returns the serialized result as <see cref="JsonElement"/>.
    /// Throw <see cref="OperationException"/> for domain errors (code + message).
    /// Any other exception propagates as <c>INTERNAL_ERROR</c>.
    /// </summary>
    Task<JsonElement> HandleAsync(
        OperationHandlerContext context,
        CancellationToken ct = default);
}
