using Ganss.Xss;
using Markdig;

namespace ReportingPlatform.Transformers.Layout;

/// <summary>
/// chartType: "text_widget" — four-step secure rendering pipeline:
///   1. Read template from VisualConfig["content"] (raw Markdown or HTML).
///   2. Substitute {{filterKey}} placeholders: only allowlisted filter keys;
///      values are HTML-encoded before substitution to prevent injection.
///   3. Render with Markdig (commonmark + extra features).
///   4. Sanitize with HtmlSanitizer (strips &lt;script&gt;, on* attributes,
///      javascript: URIs, data: URIs) — this step is a security invariant.
///
/// Unknown {{placeholders}} pass through unchanged (not an error).
/// </summary>
internal sealed class TextWidgetTransformer : IWidgetTransformer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = BuildSanitizer();

    public string ChartType => "text_widget";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc      = ctx.VisualConfig();
        var content = vc.TryGetString("content") ?? string.Empty;

        // Step 2: substitute {{key}} placeholders — values HTML-encoded first
        var (substituted, appliedVars) = SubstitutePlaceholders(content, ctx.Filters);

        // Step 3: Markdown → HTML
        var html = Markdown.ToHtml(substituted, Pipeline);

        // Step 4: sanitize (security invariant — never skip)
        var sanitized = Sanitizer.Sanitize(html);

        var result = new TextWidgetData
        {
            Content           = sanitized,
            RenderMode        = "html",
            TemplateVariables = appliedVars.Count > 0 ? appliedVars : null,
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.TextWidgetData));
    }

    private static (string Result, Dictionary<string, string> Applied) SubstitutePlaceholders(
        string template,
        IReadOnlyDictionary<string, JsonElement> filters)
    {
        var applied = new Dictionary<string, string>(StringComparer.Ordinal);
        var result  = template;

        foreach (var (key, value) in filters)
        {
            var placeholder = $"{{{{{key}}}}}";
            if (!result.Contains(placeholder, StringComparison.Ordinal))
                continue;

            // HTML-encode the value before substituting to prevent injection
            var raw     = value.ToStringValue() ?? string.Empty;
            var encoded = System.Net.WebUtility.HtmlEncode(raw);
            result      = result.Replace(placeholder, encoded, StringComparison.Ordinal);
            applied[key] = raw;
        }

        return (result, applied);
    }

    private static HtmlSanitizer BuildSanitizer()
    {
        var s = new HtmlSanitizer();

        // Strip all event handlers (on*)
        s.AllowedAttributes.Remove("style");
        s.UriAttributes.Add("action");

        // Disallow javascript: and data: URI schemes
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("mailto");

        return s;
    }
}
