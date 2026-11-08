using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using Panopticon.Contracts;

namespace Panopticon.Server.Reporting;

/// <summary>
/// The server's checks on a report's documents and archive folder (change-documents-and-history D6, spec:
/// progress-reporting, Document checks). They run before anything is stored, and each failure names the problem.
/// </summary>
internal static partial class DocumentChecks
{
    /// <summary>
    /// Returns the report's documents, checked. A report with no documents (an older CLI) gets just its
    /// <c>tasks.md</c>, and so does one whose documents leave it out, so every manifest holds the tasks.
    /// </summary>
    /// <exception cref="McpException">A path, size, duplicate or <c>tasks.md</c> mismatch; nothing is recorded.</exception>
    public static IReadOnlyList<ChangeDocument> Check(IReadOnlyList<ChangeDocument?>? documents, string tasksMarkdown)
    {
        // Before anything else, so a report of very many tiny documents costs nothing to refuse.
        if (documents?.Count > DocumentLimits.MaxDocuments)
        {
            throw new McpException(string.Create(
                CultureInfo.InvariantCulture,
                $"The report carries {documents.Count} documents; the limit is {DocumentLimits.MaxDocuments}."));
        }

        var checkedDocuments = new List<ChangeDocument>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < (documents?.Count ?? 0); index++)
        {
            var document = documents![index];
            if (document?.Path is not { } path || document.Content is null)
            {
                throw new McpException($"documents[{index}] needs a path and content.");
            }

            CheckPath(path);
            if (!paths.Add(path))
            {
                throw new McpException($"Document '{path}' appears more than once.");
            }

            if (path == DocumentLimits.TasksPath && !string.Equals(document.Content, tasksMarkdown, StringComparison.Ordinal))
            {
                throw new McpException($"Document '{DocumentLimits.TasksPath}' differs from tasksMarkdown.");
            }

            checkedDocuments.Add(document);
        }

        if (!paths.Contains(DocumentLimits.TasksPath))
        {
            checkedDocuments.Add(new ChangeDocument(DocumentLimits.TasksPath, tasksMarkdown));
        }

        long total = 0;
        foreach (var document in checkedDocuments)
        {
            var bytes = Encoding.UTF8.GetByteCount(document.Content);
            var limit = DocumentLimits.MaxBytesFor(document.Path);
            if (bytes > limit)
            {
                throw new McpException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Document '{document.Path}' is {bytes} bytes; the limit is {limit} bytes ({DocumentLimits.Describe(limit)})."));
            }

            total += bytes;
        }

        if (total > DocumentLimits.MaxTotalBytes)
        {
            throw new McpException(string.Create(
                CultureInfo.InvariantCulture,
                $"The documents total {total} bytes; the limit is {DocumentLimits.MaxTotalBytes} bytes ({DocumentLimits.Describe(DocumentLimits.MaxTotalBytes)})."));
        }

        return checkedDocuments;
    }

    /// <summary>An archive folder must be <c>&lt;YYYY-MM-DD&gt;-&lt;change&gt;</c>, as OpenSpec names it (D3).</summary>
    public static void CheckArchive(string archive, string change)
    {
        var match = ArchiveFolder().Match(archive);
        if (!match.Success || !string.Equals(match.Groups["change"].Value, change, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"Archive folder '{archive}' isn't '<YYYY-MM-DD>-{change}'.");
        }
    }

    private static void CheckPath(string path)
    {
        if (DocumentLimits.PathProblem(path) is { } problem)
        {
            throw new McpException($"Document path '{path}' {problem}; it must be a plain relative path inside the change's folder.");
        }

        if (!DocumentLimits.HasSyncedExtension(path))
        {
            throw new McpException($"Document '{path}' isn't Markdown or YAML ({string.Join(", ", DocumentLimits.Extensions)}).");
        }
    }

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}-(?<change>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveFolder();
}
