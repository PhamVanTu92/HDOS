using System.Text.Json;
using ReportingPlatform.Transformers.Layout;
using ReportingPlatform.Transformers.Tests.Helpers;
using Xunit;

namespace ReportingPlatform.Transformers.Tests.Layout;

public sealed class LayoutTransformerTests
{
    // ------------------------------------------------------------------
    // TextWidget — XSS sanitization
    // ------------------------------------------------------------------

    [Fact]
    public async Task TextWidgetTransformer_XssScriptTagStripped()
    {
        // Content embeds a <script> tag — must be removed by HtmlSanitizer
        var vc  = """{"content":"# Hello\n\n<script>alert('xss')</script>\n\nWorld"}""";
        var t   = new TextWidgetTransformer();
        var ctx = TransformerTestHelpers.Ctx("text_widget", vc);

        var result = await t.TransformAsync([], null, ctx);
        var content = result.GetProperty("content").GetString()!;

        Assert.DoesNotContain("<script>",    content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert('xss')", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello",             content);
        Assert.Contains("World",             content);
    }

    [Fact]
    public async Task TextWidgetTransformer_OnErrorAttributeStripped()
    {
        var vc  = """{"content":"<img src=x onerror=alert(1)>"}""";
        var t   = new TextWidgetTransformer();
        var ctx = TransformerTestHelpers.Ctx("text_widget", vc);

        var result  = await t.TransformAsync([], null, ctx);
        var content = result.GetProperty("content").GetString()!;

        Assert.DoesNotContain("onerror", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TextWidgetTransformer_JavascriptUriStripped()
    {
        var vc  = """{"content":"<a href=\"javascript:alert(1)\">click</a>"}""";
        var t   = new TextWidgetTransformer();
        var ctx = TransformerTestHelpers.Ctx("text_widget", vc);

        var result  = await t.TransformAsync([], null, ctx);
        var content = result.GetProperty("content").GetString()!;

        Assert.DoesNotContain("javascript:", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TextWidgetTransformer_PlaceholderSubstitution_OnlyAllowlistedKeys()
    {
        var vc = """{"content":"Hello {{name}}, your region is {{region}}."}""";
        var filters = new Dictionary<string, JsonElement>
        {
            ["name"]   = JsonSerializer.SerializeToElement("Alice"),
            ["region"] = JsonSerializer.SerializeToElement("APAC"),
        };
        var t   = new TextWidgetTransformer();
        var ctx = TransformerTestHelpers.Ctx("text_widget", vc, filters);

        var result  = await t.TransformAsync([], null, ctx);
        var content = result.GetProperty("content").GetString()!;

        Assert.Contains("Alice", content);
        Assert.Contains("APAC",  content);
        Assert.DoesNotContain("{{name}}",   content);
        Assert.DoesNotContain("{{region}}", content);
    }

    [Fact]
    public async Task TextWidgetTransformer_UnknownPlaceholder_PassedThrough()
    {
        // Unknown placeholders are left as-is (not errored, not substituted)
        var vc  = """{"content":"Hello {{unknown_key}}!"}""";
        var t   = new TextWidgetTransformer();
        var ctx = TransformerTestHelpers.Ctx("text_widget", vc);

        var result  = await t.TransformAsync([], null, ctx);
        var content = result.GetProperty("content").GetString()!;

        // Unknown placeholders are left as-is: still present in the rendered HTML,
        // no substitution occurred, no exception thrown.
        Assert.Contains("{{unknown_key}}", content);
    }

    [Fact]
    public async Task TextWidgetTransformer_HtmlEncodesValueBeforeSubstitution()
    {
        // If a filter value contains HTML special chars, they must be HTML-encoded
        // BEFORE substitution so the sanitizer doesn't see raw HTML injection.
        var vc = """{"content":"Value: {{val}}"}""";
        var filters = new Dictionary<string, JsonElement>
        {
            ["val"] = JsonSerializer.SerializeToElement("<b>bold</b>"),
        };
        var t   = new TextWidgetTransformer();
        var ctx = TransformerTestHelpers.Ctx("text_widget", vc, filters);

        var result  = await t.TransformAsync([], null, ctx);
        var content = result.GetProperty("content").GetString()!;

        // The raw <b> tag must NOT appear; it should be HTML-encoded as &lt;b&gt;
        Assert.DoesNotContain("<b>bold</b>", content);
    }

    // ------------------------------------------------------------------
    // TabContainer
    // ------------------------------------------------------------------

    [Fact]
    public async Task TabContainerTransformer_MatchesGoldenFile()
    {
        var vc = """
        {
          "tabs": [
            {"id":"tab1","label":"Overview","widgetIds":["w2","w3"],"default":true},
            {"id":"tab2","label":"Details","widgetIds":["w4"]}
          ]
        }
        """;
        var t   = new TabContainerTransformer();
        var ctx = TransformerTestHelpers.Ctx("tab_container", vc);

        var result = await t.TransformAsync([], null, ctx);
        var tabs   = result.GetProperty("tabs");
        Assert.Equal(2, tabs.GetArrayLength());
        Assert.Equal("Overview", tabs[0].GetProperty("label").GetString());
        Assert.True(tabs[0].GetProperty("default").GetBoolean());
        TransformerTestHelpers.AssertMatchesGolden("TabContainerTransformer", result);
    }
}
