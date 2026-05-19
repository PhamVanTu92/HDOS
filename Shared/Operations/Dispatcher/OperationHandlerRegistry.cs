namespace ReportingPlatform.Operations.Dispatcher;

public sealed class OperationHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IOperationHandler> _map;

    public OperationHandlerRegistry(IEnumerable<IOperationHandler> handlers)
    {
        var map = new Dictionary<string, IOperationHandler>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in handlers)
        {
            if (!map.TryAdd(h.OperationName, h))
                throw new InvalidOperationException(
                    $"Duplicate handler registered for operation '{h.OperationName}'.");
        }
        _map = map;
    }

    public IOperationHandler? Resolve(string operationName) =>
        _map.TryGetValue(operationName, out var h) ? h : null;

    public IReadOnlyCollection<string> RegisteredOperations =>
        (IReadOnlyCollection<string>)_map.Keys;
}
