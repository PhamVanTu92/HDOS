namespace ReportingPlatform.QueryBuilder.Builder;

public sealed record QueryBuilderResult(
    IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Rows,
    long TotalRows);
