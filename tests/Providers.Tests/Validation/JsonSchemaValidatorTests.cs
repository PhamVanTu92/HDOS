using Json.Schema;

namespace Providers.Tests.Validation;

public sealed class JsonSchemaValidatorTests
{
    private static readonly string RequiredStartDateSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["startDate"],
          "properties": {
            "startDate": { "type": "string" }
          }
        }
        """;

    // T3: Valid params pass schema validation
    [Fact]
    public async Task ValidateAsync_ValidParams_ReturnsSuccess()
    {
        var registry  = MakeRegistryWithSchema(RequiredStartDateSchema);
        var validator = new JsonSchemaParamsValidator(registry);
        var @params   = JsonDocument.Parse("""{"startDate":"2026-01-01"}""").RootElement;

        var result = await validator.ValidateAsync("op.test", @params);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // T4: Missing required field fails schema validation
    [Fact]
    public async Task ValidateAsync_MissingRequiredField_ReturnsValidationError()
    {
        var registry  = MakeRegistryWithSchema(RequiredStartDateSchema);
        var validator = new JsonSchemaParamsValidator(registry);
        var @params   = JsonDocument.Parse("{}").RootElement;

        var result = await validator.ValidateAsync("op.test", @params);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, e => Assert.Equal(ErrorCodes.ValidationError, e.Code));
    }

    // T5: PARAMS_TOO_LARGE guard fires before schema lookup
    [Fact]
    public async Task ValidateAsync_OversizedParams_ReturnsTooLargeWithoutSchemaHit()
    {
        // Registry with a schema that would reject anything — should never be reached
        var registry  = MakeRegistryWithSchema("""{"type":"string"}""");
        var validator = new JsonSchemaParamsValidator(registry);

        var hugeValue = new string('x', 65_537);
        var @params   = JsonDocument.Parse("\"" + hugeValue + "\"").RootElement;

        var result = await validator.ValidateAsync("op.test", @params);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.ParamsTooLarge, result.Errors[0].Code);
    }

    // No schema registered → always passes
    [Fact]
    public async Task ValidateAsync_NoSchemaRegistered_AlwaysReturnsSuccess()
    {
        var registry  = MakeRegistryWithoutSchema();
        var validator = new JsonSchemaParamsValidator(registry);
        var @params   = JsonDocument.Parse("{}").RootElement;

        var result = await validator.ValidateAsync("op.test", @params);

        Assert.True(result.IsValid);
    }

    private static IOperationRegistry MakeRegistryWithSchema(string schemaJson)
    {
        var compiled = JsonSchema.FromText(schemaJson);
        var reg = new OperationRegistration
        {
            OperationPattern = "op.test",
            HandlerType      = "internal",
            CompiledSchema   = compiled,
        };
        return new FakeOperationRegistry(reg);
    }

    private static IOperationRegistry MakeRegistryWithoutSchema()
    {
        var reg = new OperationRegistration
        {
            OperationPattern = "op.test",
            HandlerType      = "internal",
        };
        return new FakeOperationRegistry(reg);
    }

    private sealed class FakeOperationRegistry : IOperationRegistry
    {
        private readonly OperationRegistration _reg;
        public FakeOperationRegistry(OperationRegistration reg) => _reg = reg;

        public Task<OperationRegistration?> ResolveAsync(string operation, CancellationToken ct = default) =>
            Task.FromResult<OperationRegistration?>(
                operation == _reg.OperationPattern ? _reg : null);

        public Task<IReadOnlyList<OperationRegistration>> GetAllActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OperationRegistration>>(new[] { _reg });

        public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
