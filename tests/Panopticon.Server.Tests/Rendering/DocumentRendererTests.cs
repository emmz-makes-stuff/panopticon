using Panopticon.Server.Documents;

namespace Panopticon.Server.Tests.Rendering;

// specs/change-documents: Rendered documents, Safe rendering (design D8).
public sealed class DocumentRendererTests : IDisposable
{
    private readonly DocumentRenderer _renderer = new();

    public void Dispose() => _renderer.Dispose();

    private string Render(string markdown, string path = "design.md") =>
        _renderer.Render(path, S3DocumentStore.Hash(markdown), markdown);

    [Fact]
    public void Script_in_a_document_is_shown_as_text()
    {
        var html = Render("Before\n\n<script>alert(1)</script>\n\nAfter");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_raw_html_is_shown_as_text()
    {
        var html = Render("A <img src=x onerror=alert(1)> and <b>bold</b>.");

        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Block_raw_html_is_shown_as_text()
    {
        var html = Render("<div onclick=\"alert(1)\">\nhi\n</div>\n");

        Assert.DoesNotContain("<div", html, StringComparison.Ordinal);
        Assert.Contains("&lt;div onclick=&quot;alert(1)&quot;&gt;", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("[click](JavaScript:alert(1))")]
    [InlineData("[click](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("[click](vbscript:msgbox(1))")]
    [InlineData("[click](file:///etc/passwd)")]
    [InlineData("[click](./design.md)")]
    [InlineData("[click][ref]\n\n[ref]: javascript:alert(1)")]
    public void Unsafe_link_targets_lose_the_link_but_keep_the_text(string markdown)
    {
        var html = Render(markdown);

        Assert.DoesNotContain("<a", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href", html, StringComparison.Ordinal);
        Assert.Contains("click", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<data:text/html,hi>")]
    public void Unsafe_autolinks_become_text(string markdown)
    {
        var html = Render(markdown);

        Assert.DoesNotContain("href", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[docs](https://example.com/a?b=c)", "https://example.com/a?b=c")]
    [InlineData("[site](http://example.com)", "http://example.com")]
    [InlineData("[mail](mailto:emmz@example.com)", "mailto:emmz@example.com")]
    [InlineData("[up](#design)", "#design")]
    [InlineData("<https://example.com>", "https://example.com")]
    [InlineData("see https://example.com/x for more", "https://example.com/x")]
    public void Safe_link_targets_are_kept(string markdown, string href)
    {
        var html = Render(markdown);

        Assert.Contains($"href=\"{href}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Images_become_links_and_never_img()
    {
        var html = Render("![a diagram](https://example.com/d.png)");

        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://example.com/d.png\">a diagram</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_with_an_unsafe_url_is_just_its_alt_text()
    {
        var html = Render("![tracker](data:image/png;base64,AAAA)");

        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href", html, StringComparison.Ordinal);
        Assert.Contains("tracker", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Generic_attributes_are_not_an_extension()
    {
        var html = Render("# Title {onclick=alert(1)}");

        Assert.DoesNotContain("onclick=\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Tasks_show_as_disabled_checkboxes_ticked_or_not()
    {
        var html = Render("## 1. Setup\n\n- [x] 1.1 Done\n- [ ] 1.2 Not yet\n", "tasks.md");

        Assert.Equal(2, CountOf(html, "type=\"checkbox\""));
        Assert.Equal(2, CountOf(html, "disabled=\"disabled\""));
        Assert.Equal(1, CountOf(html, "checked=\"checked\""));
    }

    [Fact]
    public void Headings_and_tables_render()
    {
        var html = Render("# Design\n\n## Decisions\n\n| A | B |\n|---|---|\n| 1 | 2 |\n");

        Assert.Contains("<h1 id=\"design\">Design</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<h2 id=\"decisions\">Decisions</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<table>", html, StringComparison.Ordinal);
        Assert.Contains("<td>2</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Code_blocks_are_escaped()
    {
        var html = Render("```html\n<script>alert(1)</script>\n```\n");

        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        Assert.Contains("<pre><code class=\"language-html\">&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".openspec.yaml")]
    [InlineData("config.YML")]
    public void Yaml_is_escaped_preformatted_text(string path)
    {
        var html = Render("schema: spec-driven\nnote: \"<script>alert(1)</script>\"\n", path);

        Assert.StartsWith("<pre><code class=\"language-yaml\">schema: spec-driven", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Results_are_cached_by_hash_and_kind()
    {
        const string content = "a: 1\n";
        var sha = S3DocumentStore.Hash(content);

        var first = _renderer.Render("design.md", sha, content);
        var again = _renderer.Render("other.md", sha, content);
        var yaml = _renderer.Render("x.yaml", sha, content);

        Assert.Same(first, again);
        Assert.NotEqual(first, yaml);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
