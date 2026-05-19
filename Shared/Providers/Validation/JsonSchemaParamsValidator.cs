namespace ReportingPlatform.Providers.Validation;

internal sealed class JsonSchemaParamsValidator : IParamsValidator
{
    private const int ParamsSizeLimit = 65_536;

    private readonly IOperationRegistry _registry;

    public JsonSchemaParamsValidator(IOperationRegistry registry)
    {
        _registry = registry;
    }

    public async Task<ValidationResult> ValidateAsync(
        string operationPattern,
        JsonElement @params,
        CancellationToken cancellationToken = default)
    {
        var rawText = @params.GetRawText();
        if (rawText.Length > ParamsSizeLimit)
        {
            return ValidationResult.Failure(
            [
                new ValidationError
                {
                    Field   = "params",
                    Code    = ErrorCodes.ParamsTooLarge,
                    Message = $"params exceeds maximum size of {ParamsSizeLimit} bytes",
                },
            ]);
        }

        var registration = await _registry.ResolveAsync(operationPattern, cancellationToken);

        if (registration?.CompiledSchema is null)
            return ValidationResult.Success;

        var instance = JsonNode.Parse(rawText);
        var result = registration.CompiledSchema.Evaluate(
            instance,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (result.IsValid)
            return ValidationResult.Success;

        var errors = result.Details
            .Where(d => !d.IsValid && d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => new ValidationError
            {
                Field   = d.InstanceLocation.ToString(),
                Code    = ErrorCodes.ValidationError,
                Message = e.Value,
            }))
            .ToArray();

        return ValidationResult.Failure(errors.Length > 0
            ? errors
            : [new ValidationError { Field = "params", Code = ErrorCodes.ValidationError, Message = "Schema validation failed" }]);
    }
}
