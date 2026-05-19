using System.Text.Json;
using ReportingPlatform.Contracts.Definitions;
using ReportingPlatform.Contracts.RenderPayloads.Shared;
using ReportingPlatform.Transformers.Context;

namespace ReportingPlatform.Transformers.Tests.Helpers;

internal static class TransformerTestHelpers
{
    internal static IReadOnlyDictionary<string, JsonElement> Row(params (string Key, object? Value)[] cols)
    {
        return cols.ToDictionary(
            c => c.Key,
            c => c.Value is null
                ? JsonSerializer.SerializeToElement((object?)null)
                : JsonSerializer.SerializeToElement(c.Value),
            StringComparer.Ordinal);
    }

    internal static WidgetRenderContext Ctx(
        string chartType,
        string? visualConfigJson = null,
        IReadOnlyDictionary<string, JsonElement>? filters = null)
    {
        JsonElement vc = default;
        if (visualConfigJson is not null)
            vc = JsonSerializer.Deserialize<JsonElement>(visualConfigJson);

        return new WidgetRenderContext
        {
            Widget = new WidgetDefinition
            {
                WidgetId     = "w1",
                ChartType    = chartType,
                Title        = "Test Widget",
                DatasourceId = "ds1",
                VisualConfig = vc.ValueKind == JsonValueKind.Undefined ? null : vc,
            },
            Filters        = filters ?? new Dictionary<string, JsonElement>(),
            DropdownOptions = null,
        };
    }

    /// <summary>
    /// Reads an existing golden file or returns null when the file does not exist.
    /// When env var REGEN_GOLDEN=1, writes the actual output instead of comparing.
    /// </summary>
    internal static string? LoadGolden(string transformerName)
    {
        var path = GoldenPath(transformerName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    internal static void AssertMatchesGolden(string transformerName, JsonElement actual)
    {
        var regen = Environment.GetEnvironmentVariable("REGEN_GOLDEN") == "1";
        var path  = GoldenPath(transformerName);

        var actualJson = JsonSerializer.Serialize(actual, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        if (regen)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actualJson);
            return;
        }

        var golden = LoadGolden(transformerName);
        if (golden is null)
        {
            // Auto-generate on first run — subsequent runs compare
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actualJson);
            return;
        }

        // Normalize both sides to ignore formatting differences
        var goldenNorm = NormalizeJson(golden);
        var actualNorm = NormalizeJson(actualJson);

        Xunit.Assert.Equal(goldenNorm, actualNorm);
    }

    private static string NormalizeJson(string json) =>
        JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonElement>(json),
            new JsonSerializerOptions { WriteIndented = false });

    private static string GoldenPath(string name)
    {
        // Walk up from the test DLL location to find tests/Transformers.Tests/golden/
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "golden");
            if (Directory.Exists(candidate)) return Path.Combine(candidate, $"{name}.json");

            // Also check if we're inside bin/Debug/net9.0 — go up 3 levels
            var attempt = Path.Combine(dir, "..", "..", "..", "golden");
            if (Directory.Exists(attempt))
                return Path.GetFullPath(Path.Combine(attempt, $"{name}.json"));

            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "golden", $"{name}.json");
    }
}
