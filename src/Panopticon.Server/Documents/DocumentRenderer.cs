using System.Net;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Caching.Memory;

namespace Panopticon.Server.Documents;

/// <summary>
/// Renders a synced document to HTML for the change page (change-documents-and-history D8; spec: change-documents,
/// Rendered documents and Safe rendering). Documents are untrusted: raw HTML is never passed through, links and
/// images keep only <c>http</c>, <c>https</c>, <c>mailto</c> and same-page <c>#</c> targets, and images become links,
/// so a document can't make a browser fetch anything. The web app inserts this HTML as is, so this is the one place
/// that makes it safe.
/// </summary>
public sealed class DocumentRenderer : IDisposable
{
    /// <summary>The cache's budget, in characters of rendered HTML.</summary>
    public const long CacheSizeLimit = 32L * 1024 * 1024;

    // Picked one by one rather than UseAdvancedExtensions(), which brings generic attributes ({onclick=...}) and
    // media links (<iframe>), both of which emit markup a document controls.
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers()
        .UseEmphasisExtras()
        .UsePipeTables()
        .UseGridTables()
        .UseListExtras()
        .UseTaskLists()
        .UseDefinitionLists()
        .UseFootnotes()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    private static readonly string[] _yamlExtensions = [".yaml", ".yml"];

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = CacheSizeLimit });

    /// <summary>
    /// The HTML for a document. <paramref name="sha256"/> is its content's hash: content is immutable, so the result
    /// is cached by it and never goes stale.
    /// </summary>
    public string Render(string path, string sha256, string content)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(content);

        var isYaml = _yamlExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        var key = (isYaml ? "yaml:" : "md:") + sha256;
        if (_cache.TryGetValue(key, out string? cached) && cached is not null)
        {
            return cached;
        }

        var html = isYaml ? RenderYaml(content) : RenderMarkdown(content);
        _cache.Set(key, html, new MemoryCacheEntryOptions { Size = Math.Max(1, html.Length) });
        return html;
    }

    public void Dispose() => _cache.Dispose();

    internal static string RenderYaml(string content) =>
        $"<pre><code class=\"language-yaml\">{WebUtility.HtmlEncode(content)}</code></pre>\n";

    internal static string RenderMarkdown(string content)
    {
        var document = Markdown.Parse(content, _pipeline);
        MakeLinksSafe(document);
        return document.ToHtml(_pipeline);
    }

    /// <summary>The post-parse pass of D8: unsafe targets lose their link but keep their text, and images become links.</summary>
    private static void MakeLinksSafe(MarkdownDocument document)
    {
        // Materialised first: the loop replaces inlines in the tree it walks.
        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            if (!IsAllowed(autolink.Url))
            {
                autolink.ReplaceBy(new LiteralInline(autolink.Url));
            }
        }

        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            var url = link.GetDynamicUrl?.Invoke() ?? link.Url;
            if (link.IsImage)
            {
                // An image's children are its alt text, which becomes the link's text.
                link.IsImage = false;
            }

            if (!IsAllowed(url))
            {
                Unwrap(link);
            }
        }
    }

    private static void Unwrap(LinkInline link)
    {
        var children = link.ToList();
        Inline previous = link;
        foreach (var child in children)
        {
            child.Remove();
            previous.InsertAfter(child);
            previous = child;
        }

        if (children.Count == 0 && !string.IsNullOrEmpty(link.Url))
        {
            link.InsertAfter(new LiteralInline(link.Url));
        }

        link.Remove();
    }

    internal static bool IsAllowed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var trimmed = url.Trim();
        if (trimmed.StartsWith('#'))
        {
            return true;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "mailto"
            && trimmed.StartsWith(uri.Scheme + ":", StringComparison.OrdinalIgnoreCase);
    }
}
