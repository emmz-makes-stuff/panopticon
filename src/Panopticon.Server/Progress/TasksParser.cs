using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Panopticon.Server.Progress;

/// <summary>Raised when a <c>tasks.md</c> is larger than <see cref="TasksParser.MaxBytes"/>.</summary>
public sealed class TasksDocumentTooLargeException(int actualBytes)
    : Exception($"tasks.md is {actualBytes} bytes; the limit is {TasksParser.MaxBytes} bytes (1 MiB).")
{
    public int ActualBytes { get; } = actualBytes;
}

/// <summary>
/// Turns an OpenSpec <c>tasks.md</c> into per-section and overall progress (spec: task-progress).
/// The input is untrusted: raw HTML is disabled and input over 1 MiB is rejected.
/// </summary>
public static partial class TasksParser
{
    public const int MaxBytes = 1024 * 1024;

    // HTML disabled: embedded HTML is parsed as literal text, never as markup (design D8).
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .Build();

    public static ParsedTasks Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var bytes = Encoding.UTF8.GetByteCount(markdown);
        if (bytes > MaxBytes)
        {
            throw new TasksDocumentTooLargeException(bytes);
        }

        var document = Markdown.Parse(markdown, _pipeline);

        var sections = new List<SectionProgress>();
        var overall = TaskProgress.Empty;
        string? title = null;
        var number = 0;
        var progress = TaskProgress.Empty;
        var position = 0;

        foreach (var block in document)
        {
            if (block is HeadingBlock { Level: 2 } heading)
            {
                if (title is not null)
                {
                    sections.Add(new SectionProgress(number, title, progress));
                }

                position++;
                (number, title) = ReadHeading(heading, position);
                progress = TaskProgress.Empty;
                continue;
            }

            foreach (var item in ListItems(block))
            {
                if (ReadCheckbox(item, markdown) is { } done)
                {
                    overall = overall.Add(done);
                    if (title is not null)
                    {
                        progress = progress.Add(done);
                    }
                }
            }
        }

        if (title is not null)
        {
            sections.Add(new SectionProgress(number, title, progress));
        }

        return new ParsedTasks(sections, overall);
    }

    private static IEnumerable<ListItemBlock> ListItems(Block block) => block switch
    {
        ListItemBlock item => [item, .. item.Descendants<ListItemBlock>()],
        ContainerBlock container => container.Descendants<ListItemBlock>(),
        _ => [],
    };

    /// <summary>The checkbox state of a list item, or null when the item has no checkbox.</summary>
    private static bool? ReadCheckbox(ListItemBlock item, string markdown)
    {
        if (item.Count == 0 || item[0] is not ParagraphBlock paragraph)
        {
            return null;
        }

        var start = paragraph.Span.Start;
        var length = Math.Min(paragraph.Span.Length, 16);
        if (start < 0 || length <= 0)
        {
            return null;
        }

        var match = CheckboxPattern().Match(markdown, start, length);
        if (!match.Success)
        {
            return null;
        }

        // Only a box holding just `x` (either case, any spacing) counts as done.
        return match.Groups["mark"].Value.Trim() is "x" or "X";
    }

    private static (int Number, string Title) ReadHeading(HeadingBlock heading, int position)
    {
        var text = InlineText(heading.Inline).Trim();
        var numbered = NumberedHeadingPattern().Match(text);
        if (numbered.Success && int.TryParse(numbered.Groups["number"].Value, out var number))
        {
            return (number, numbered.Groups["title"].Value.Trim());
        }

        return (position, text);
    }

    private static string InlineText(ContainerInline? inline)
    {
        if (inline is null)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var node in inline.Descendants<Inline>())
        {
            switch (node)
            {
                case LiteralInline literal:
                    text.Append(literal.Content.AsSpan());
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                case LineBreakInline:
                    text.Append(' ');
                    break;
                default:
                    break;
            }
        }

        return text.ToString();
    }

    // A checkbox is a bracket pair holding 0-3 non-bracket characters at the very start of the item,
    // followed by whitespace or the end of the line: `[ ]`, `[x]`, `[ x]`, `[~]`, `[-]`, `[]`.
    // `[link](url)` and `[TODO]` are not checkboxes.
    [GeneratedRegex(@"\G\[(?<mark>[^\[\]\r\n]{0,3})\](?=[ \t\r\n]|$)")]
    private static partial Regex CheckboxPattern();

    [GeneratedRegex(@"^(?<number>\d+)\.?\s+(?<title>.+)$")]
    private static partial Regex NumberedHeadingPattern();
}
