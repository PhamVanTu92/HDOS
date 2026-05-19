namespace ReportingPlatform.Transformers.Helpers;

internal static class JsonElementHelpers
{
    // --- JsonElement property reading ---

    internal static string? TryGetString(this JsonElement el, string property) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(property, out var prop) &&
        prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    internal static double? TryGetDouble(this JsonElement el, string property) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(property, out var prop) &&
        prop.ValueKind == JsonValueKind.Number
            ? prop.GetDouble()
            : null;

    internal static bool TryGetBool(this JsonElement el, string property, bool defaultValue = false) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(property, out var prop)
            ? prop.ValueKind switch
            {
                JsonValueKind.True  => true,
                JsonValueKind.False => false,
                _ => defaultValue
            }
            : defaultValue;

    internal static JsonElement TryGetElement(this JsonElement el, string property) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(property, out var prop)
            ? prop
            : default;

    // --- Row value reading ---

    internal static JsonElement GetRowValue(
        this IReadOnlyDictionary<string, JsonElement> row, string key) =>
        row.TryGetValue(key, out var v) ? v : default;

    internal static double? ToDouble(this JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.GetDouble(),
        JsonValueKind.String => double.TryParse(el.GetString(), out var d) ? d : null,
        _ => null
    };

    internal static long? ToLong(this JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : (long?)null,
        JsonValueKind.String => long.TryParse(el.GetString(), out var l) ? l : null,
        _ => null
    };

    internal static string? ToStringValue(this JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True   => "true",
        JsonValueKind.False  => "false",
        JsonValueKind.Null   => null,
        _ => el.GetRawText()
    };

    // --- Null element ---

    private static readonly JsonElement _null = CreateNull();

    private static JsonElement CreateNull()
    {
        using var doc = JsonDocument.Parse("null");
        return doc.RootElement.Clone();
    }

    internal static JsonElement NullElement() => _null;

    internal static JsonElement ToJsonElement(this string? s) =>
        s is null ? _null : JsonSerializer.SerializeToElement(s);

    internal static JsonElement ToJsonElement(this double d) =>
        JsonSerializer.SerializeToElement(d);

    // --- VisualConfig shorthand ---

    internal static JsonElement VisualConfig(this WidgetRenderContext ctx) =>
        ctx.Widget.VisualConfig ?? default;
}
