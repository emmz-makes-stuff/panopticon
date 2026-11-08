using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using Panopticon.Contracts;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Mcp;

// specs/progress-reporting: Document checks. Each rejected report is a tool error naming the problem, and records nothing.
public class DocumentCheckTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Tasks = "## 1. Setup\n- [x] 1.1\n";

    private static readonly ReportProgressArguments _report = new(
        new RepoRef("github.com", "emmz", "panopticon"),
        "team-auth",
        new AgentRef("claude-code", "vm-1"),
        Tasks,
        Documents: [new ChangeDocument("tasks.md", Tasks)]);

    private static readonly Dictionary<string, (ChangeDocument[] Documents, string Expected)> _rejected = new()
    {
        ["Path escapes the folder"] = ([new("../../secrets.md", "x")], "'../../secrets.md'"),
        ["Rooted path"] = ([new("/etc/passwd.md", "x")], "'/etc/passwd.md' is rooted"),
        ["Drive path"] = ([new("C:/notes.md", "x")], "'C:/notes.md' is rooted"),
        ["Backslash"] = ([new(@"specs\a\spec.md", "x")], "contains '\\'"),
        ["Dot segment"] = ([new("specs/./spec.md", "x")], "has a '.' segment"),
        ["Empty segment"] = ([new("specs//spec.md", "x")], "has an empty segment"),
        ["Empty path"] = ([new("", "x")], "'' is empty"),
        ["Too long"] = ([new(new string('a', 510) + ".md", "x")], "longer than 512 characters"),
        ["Not Markdown or YAML"] = ([new("diagram.png", "x")], "'diagram.png' isn't Markdown or YAML"),
        ["Duplicate"] = ([new("design.md", "a"), new("design.md", "b")], "'design.md' appears more than once"),
        ["Tasks mismatch"] = ([new("tasks.md", "- [ ] different")], "'tasks.md' differs from tasksMarkdown"),
        ["One file too large"] = ([new("devlog/huge.md", new string('a', DocumentLimits.MaxDocumentBytes + 1))], "'devlog/huge.md' is 262145 bytes; the limit is 262144 bytes (256 KB)"),
        ["Multi-byte characters count as bytes"] = ([new("design.md", new string('é', (DocumentLimits.MaxDocumentBytes / 2) + 1))], "'design.md' is 262146 bytes"),
        ["Oversized report"] = (Fill(17), "the limit is 4194304 bytes (4 MB)"),
    };

    public static TheoryData<string> Rejected => [.. _rejected.Keys];

    [Theory]
    [MemberData(nameof(Rejected))]
    public async Task Rejected_documents_are_named_and_nothing_is_recorded(string scenario)
    {
        var (documents, expected) = _rejected[scenario];
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var client = await factory.CreateMcpClientAsync(cancellationToken: ct);

        var result = await client.CallToolAsync("report_progress", (_report with { Documents = documents }).ToToolArguments(), cancellationToken: ct);

        Assert.True(result.IsError);
        Assert.Contains(expected, Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
        await AssertNothingRecordedAsync(factory, ct);
    }

    [Theory]
    [InlineData("team-auth")]
    [InlineData("2026-11-08-other-change")]
    [InlineData("2026-11-08-team-auth/../x")]
    public async Task An_archive_folder_not_named_for_the_change_is_rejected(string archive)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var client = await factory.CreateMcpClientAsync(cancellationToken: ct);

        var result = await client.CallToolAsync("report_progress", (_report with { Archive = archive }).ToToolArguments(), cancellationToken: ct);

        Assert.True(result.IsError);
        Assert.Contains($"'{archive}'", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
        await AssertNothingRecordedAsync(factory, ct);
    }

    [Fact]
    public async Task Documents_within_the_limits_are_accepted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var client = await factory.CreateMcpClientAsync(cancellationToken: ct);
        ChangeDocument[] documents =
        [
            new("tasks.md", Tasks),
            new("proposal.md", "# Proposal\n"),
            new("specs/cli-bridge/spec.md", new string('a', DocumentLimits.MaxDocumentBytes)),
            new(".openspec.yaml", "schema: spec-driven\n"),
            new("devlog/20261108-1515-section-1-done.MD", "# Done\n"),
            new("notes/a:b.md", "A colon is a legal file name character on Unix.\n"),
        ];

        var result = await client.CallToolAsync("report_progress", (_report with { Documents = documents }).ToToolArguments(), cancellationToken: ct);

        Assert.NotEqual(true, result.IsError);
    }

    // Each file under 256 KB, 17 of them over 4 MB in total (the "Folder too large" shape).
    private static ChangeDocument[] Fill(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new ChangeDocument($"devlog/note-{i:D2}.md", new string('a', DocumentLimits.MaxDocumentBytes - 1)))];

    private static async Task AssertNothingRecordedAsync(PanopticonFactory factory, CancellationToken ct)
    {
        await using var db = factory.NewDb();
        Assert.Equal(0, await db.Changes.CountAsync(ct));
        Assert.Equal(0, await db.ReportEvents.CountAsync(ct));
    }
}
