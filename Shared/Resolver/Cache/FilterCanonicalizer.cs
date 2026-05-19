namespace ReportingPlatform.Resolver.Cache;

/// <summary>
/// Produces a stable, canonical string from a filter dictionary for use in cache keys.
///
/// Six canonicalization rules:
///   1. Keys sorted alphabetically (ordinal)
///   2. Null / undefined values omitted
///   3. Compact JSON (no whitespace)
///   4. String values case-preserved (not lowercased)
///   5. Array elements sorted alphabetically (as strings, ordinal)
///   6. Number values rendered as standard JSON decimals (JsonElement.GetRawText())
/// </summary>
public static class FilterCanonicalizer
{
    /// <summary>
    /// Returns a compact, stable JSON string for <paramref name="filters"/>.
    /// </summary>
    public static string Canonicalize(IReadOnlyDictionary<string, JsonElement> filters)
    {
        // Rules 1 + 2: sort keys alphabetically, omit nulls/undefined
        var sorted = filters
            .Where(kv => kv.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, Value: CanonicalValue(kv.Value)));

        // Rule 3: compact JSON
        var sb = new StringBuilder("{");
        var first = true;
        foreach (var (key, value) in sorted)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"');
            sb.Append(JsonEncode(key));
            sb.Append("\":");
            sb.Append(value);
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Computes SHA256 of the canonical string and returns the first 8 base64url characters.
    /// Provides ~48 bits of collision resistance — sufficient for per-tenant cache key disambiguation.
    /// </summary>
    public static string Hash(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Base64UrlEncode(bytes)[..8];
    }

    private static string CanonicalValue(JsonElement el) => el.ValueKind switch
    {
        // Rule 4: string values case-preserved
        JsonValueKind.String => $"\"{JsonEncode(el.GetString() ?? string.Empty)}\"",
        // Rule 6: numbers use raw text (canonical JSON decimal form)
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True   => "true",
        JsonValueKind.False  => "false",
        // Rule 5: array elements sorted alphabetically as strings
        JsonValueKind.Array  => SortedArrayJson(el),
        // Nested objects: recurse key-sorted
        JsonValueKind.Object => CanonicalizeObject(el),
        _ => "null"
    };

    private static string SortedArrayJson(JsonElement arr)
    {
        var elements = arr.EnumerateArray()
            .Select(el => CanonicalValue(el))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        return "[" + string.Join(",", elements) + "]";
    }

    private static string CanonicalizeObject(JsonElement obj)
    {
        var sorted = obj.EnumerateObject()
            .Where(p => p.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"\"{JsonEncode(p.Name)}\":{CanonicalValue(p.Value)}");

        return "{" + string.Join(",", sorted) + "}";
    }

    private static string JsonEncode(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
               .Replace('+', '-')
               .Replace('/', '_')
               .TrimEnd('=');
}
