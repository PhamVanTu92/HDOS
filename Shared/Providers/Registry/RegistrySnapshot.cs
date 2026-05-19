namespace ReportingPlatform.Providers.Registry;

internal sealed record RegistrySnapshot(
    IReadOnlyDictionary<string, OperationRegistration> ByPattern,
    IReadOnlyList<OperationRegistration> All)
{
    public static readonly RegistrySnapshot Empty =
        new(new Dictionary<string, OperationRegistration>(), []);
}
