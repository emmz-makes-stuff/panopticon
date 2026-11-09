using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using Panopticon.Contracts;
using Panopticon.Server.Changes;
using Panopticon.Server.Data;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Changes;

// specs/change-documents (Document list, Rendered documents, Documents need a session) and
// specs/change-history (History list, Final snapshot), design D9.
public class ChangeEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Tasks = "## 1. Setup\n- [x] 1.1 One\n- [ ] 1.2 Two\n";

    private static ReportProgressArguments Report(string change, params (string Path, string Content)[] others) => new(
        new RepoRef("github.com", "emmz", "panopticon"),
        change,
        new AgentRef("claude-code", "vm-1"),
        Tasks,
        Documents: [new ChangeDocument("tasks.md", Tasks), .. others.Select(d => new ChangeDocument(d.Path, d.Content))]);

    private static async Task<Guid> ReportAsync(McpClient mcp, ReportProgressArguments report, CancellationToken ct)
    {
        var result = await mcp.CallToolAsync("report_progress", report.ToToolArguments(), cancellationToken: ct);
        Assert.NotEqual(true, result.IsError);
        return Guid.Parse(result.StructuredContent!.Value.GetProperty("changeId").GetString()!);
    }

    // --- Document list ---

    [Fact]
    public async Task Open_an_active_change_lists_its_documents_in_reading_order()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var mcp = await factory.CreateMcpClientAsync(cancellationToken: ct);
        var id = await ReportAsync(mcp, Report(
            "team-auth",
            ("devlog/20261108-0910-section-1-done.md", "# 1"),
            ("specs/live-dashboard/spec.md", "# Spec"),
            ("notes/extra.md", "x"),
            ("design.md", "# Design"),
            ("devlog/20261108-1645-stopped-human-verification.md", "# 3"),
            (".openspec.yaml", "schema: spec-driven"),
            ("specs/cli-bridge/spec.md", "# Spec"),
            ("proposal.md", "# Proposal"),
            ("devlog/20261108-1200-decision-retry.md", "# 2")), ct);

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: ct);
        var page = await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.ChangePath(id), ct);

        Assert.Equal("team-auth", page.GetProperty("card").GetProperty("change").GetString());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("archivedAt").ValueKind);
        var documents = page.GetProperty("documents").EnumerateArray().ToList();
        Assert.Equal(
            [
                "proposal.md", "design.md", "tasks.md", "specs/cli-bridge/spec.md", "specs/live-dashboard/spec.md",
                "devlog/20261108-1645-stopped-human-verification.md", "devlog/20261108-1200-decision-retry.md",
                "devlog/20261108-0910-section-1-done.md", ".openspec.yaml", "notes/extra.md",
            ],
            documents.Select(d => d.GetProperty("path").GetString()));
        Assert.Equal(
            ["proposal", "design", "tasks", "spec", "spec", "devlog", "devlog", "devlog", "meta", "other"],
            documents.Select(d => d.GetProperty("kind").GetString()));
        Assert.Equal("cli-bridge", documents[3].GetProperty("title").GetString());
        Assert.Equal("stopped human verification · 2026-11-08 16:45", documents[5].GetProperty("title").GetString());
        Assert.Equal(Tasks.Length, documents[2].GetProperty("sizeBytes").GetInt32());
    }

    [Fact]
    public async Task A_change_from_an_older_cli_lists_only_its_tasks()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var mcp = await factory.CreateMcpClientAsync(cancellationToken: ct);
        var id = await ReportAsync(mcp, Report("walking-skeleton") with { Documents = null }, ct);

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: ct);
        var page = await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.ChangePath(id), ct);

        var document = Assert.Single(page.GetProperty("documents").EnumerateArray());
        Assert.Equal(("tasks.md", "tasks"), (document.GetProperty("path").GetString(), document.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task An_unknown_change_or_document_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var mcp = await factory.CreateMcpClientAsync(cancellationToken: ct);
        var id = await ReportAsync(mcp, Report("team-auth"), ct);
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: ct);

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(ChangeEndpoints.ChangePath(Guid.NewGuid()), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(ChangeEndpoints.DocumentPath(Guid.NewGuid(), "tasks.md"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(ChangeEndpoints.DocumentPath(id, "design.md"), ct)).StatusCode);
    }

    // --- Rendered documents ---

    [Fact]
    public async Task A_document_is_served_rendered_and_safe()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var mcp = await factory.CreateMcpClientAsync(cancellationToken: ct);
        var id = await ReportAsync(mcp, Report(
            "team-auth",
            ("design.md", "# Design\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n<script>alert(1)</script>\n"),
            ("specs/cli-bridge/spec.md", "## Requirement: Bridge\n")), ct);
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: ct);

        var design = await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.DocumentPath(id, "design.md"), ct);
        var spec = await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.DocumentPath(id, "specs/cli-bridge/spec.md"), ct);
        var tasks = await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.DocumentPath(id, "tasks.md"), ct);

        var html = design.GetProperty("html").GetString()!;
        Assert.Equal("design.md", design.GetProperty("path").GetString());
        Assert.Contains("<h1 id=\"design\">Design</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<table>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        Assert.Contains("Requirement: Bridge</h2>", spec.GetProperty("html").GetString(), StringComparison.Ordinal);
        Assert.Contains("checked=\"checked\"", tasks.GetProperty("html").GetString(), StringComparison.Ordinal);
    }

    // --- Documents need a session, History list: Signed out ---

    [Theory]
    [InlineData("change")]
    [InlineData("document")]
    [InlineData("history")]
    public async Task Not_signed_in_gets_a_401_and_no_content(string what)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var mcp = await factory.CreateMcpClientAsync(cancellationToken: ct);
        var id = await ReportAsync(mcp, Report("team-auth", ("design.md", "# Secret design")) with { Archive = "2026-11-08-team-auth" }, ct);
        var path = what switch
        {
            "change" => ChangeEndpoints.ChangePath(id),
            "document" => ChangeEndpoints.DocumentPath(id, "design.md"),
            _ => ChangeEndpoints.HistoryPath,
        };

        using var http = factory.CreateBrowserClient();
        using var response = await http.GetAsync(path, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("team-auth", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", body, StringComparison.Ordinal);
    }

    // --- History list, Final snapshot ---

    [Fact]
    public async Task Two_archived_changes_are_listed_newest_archive_first()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using var mcp = await factory.CreateMcpClientAsync(cancellationToken: ct);
        var skeleton = await ReportAsync(mcp, Report("walking-skeleton") with { Archive = "2026-11-07-walking-skeleton" }, ct);
        var teamAuth = await ReportAsync(mcp, Report("team-auth", ("design.md", "# Final")) with { Archive = "2026-11-08-team-auth" }, ct);
        await ReportAsync(mcp, Report("change-documents-and-history"), ct);

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: ct);
        var history = await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryPath, ct);

        var changes = history.GetProperty("changes").EnumerateArray().ToList();
        Assert.Equal([teamAuth.ToString(), skeleton.ToString()], changes.Select(c => c.GetProperty("changeId").GetString()));
        var first = changes[0];
        Assert.Equal("team-auth", first.GetProperty("change").GetString());
        Assert.Equal("2026-11-08-team-auth", first.GetProperty("archiveFolder").GetString());
        Assert.Equal("panopticon", first.GetProperty("repo").GetProperty("name").GetString());
        Assert.Equal("vm-1", Assert.Single(first.GetProperty("agents").EnumerateArray()).GetProperty("machine").GetString());
        Assert.NotEqual(JsonValueKind.Null, first.GetProperty("archivedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, history.GetProperty("nextBefore").ValueKind);

        // Opening it from the history shows its final snapshot.
        var page = await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.ChangePath(teamAuth), ct);
        Assert.Equal("2026-11-08-team-auth", page.GetProperty("archiveFolder").GetString());
        Assert.Contains(page.GetProperty("documents").EnumerateArray(), d => d.GetProperty("path").GetString() == "design.md");
        Assert.Equal(1, page.GetProperty("card").GetProperty("overall").GetProperty("done").GetInt32());
    }

    [Fact]
    public async Task Nothing_archived_yet_is_an_empty_history()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        await using (var mcp = await factory.CreateMcpClientAsync(cancellationToken: ct))
        {
            await ReportAsync(mcp, Report("team-auth"), ct);
        }

        using var http = await factory.CreateSignedInClientAsync(cancellationToken: ct);
        var history = await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryPath, ct);

        Assert.Empty(history.GetProperty("changes").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, history.GetProperty("nextBefore").ValueKind);
    }

    [Fact]
    public async Task History_pages_hold_fifty_and_the_cursor_reaches_the_rest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        var start = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        await SeedArchivedAsync(factory, [.. Enumerable.Range(0, 51).Select(i => start.AddMinutes(i))], ct);
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: ct);

        var first = await http.GetFromJsonAsync<JsonElement>(ChangeEndpoints.HistoryPath, ct);
        var next = first.GetProperty("nextBefore").GetDateTimeOffset();
        var second = await http.GetFromJsonAsync<JsonElement>(
            $"{ChangeEndpoints.HistoryPath}?before={Uri.EscapeDataString(next.ToString("O", CultureInfo.InvariantCulture))}",
            ct);

        Assert.Equal(50, first.GetProperty("changes").GetArrayLength());
        Assert.Equal(start.AddMinutes(1), next);
        var last = Assert.Single(second.GetProperty("changes").EnumerateArray());
        Assert.Equal("cleanup-00", last.GetProperty("change").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextBefore").ValueKind);
    }

    // Review fix 7.4: Npgsql refuses a non-UTC DateTimeOffset for timestamptz, which was a 500.
    [Fact]
    public async Task A_cursor_with_another_offset_means_the_same_instant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        var start = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        await SeedArchivedAsync(factory, [start, start.AddMinutes(1), start.AddMinutes(2)], ct);
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: ct);

        var before = start.AddMinutes(2).ToOffset(TimeSpan.FromHours(1)).ToString("O", CultureInfo.InvariantCulture);
        using var response = await http.GetAsync($"{ChangeEndpoints.HistoryPath}?before={Uri.EscapeDataString(before)}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(
            ["cleanup-01", "cleanup-00"],
            page.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("change").GetString()));
    }

    [Fact]
    public async Task A_page_never_ends_inside_a_run_of_equal_archive_times()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        var start = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        await SeedArchivedAsync(factory, [start, start.AddMinutes(1), start.AddMinutes(1), start.AddMinutes(2)], ct);
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: ct);

        var seen = new List<string>();
        string? before = null;
        do
        {
            var url = ChangeEndpoints.HistoryPath + "?limit=2" + (before is null ? string.Empty : $"&before={Uri.EscapeDataString(before)}");
            var page = await http.GetFromJsonAsync<JsonElement>(url, ct);
            seen.AddRange(page.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("change").GetString()!));
            before = page.GetProperty("nextBefore").ValueKind == JsonValueKind.Null
                ? null
                : page.GetProperty("nextBefore").GetDateTimeOffset().ToString("O", CultureInfo.InvariantCulture);
        }
        while (before is not null);

        Assert.Equal(4, seen.Count);
        Assert.Equal(4, seen.Distinct().Count());
        Assert.Equal("cleanup-03", seen[0]);
        Assert.Equal("cleanup-00", seen[^1]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    public async Task A_history_limit_out_of_range_is_a_bad_request(string limit)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await PanopticonFactory.CreateAsync(postgres, s3, ct);
        using var http = await factory.CreateSignedInClientAsync(cancellationToken: ct);

        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync($"{ChangeEndpoints.HistoryPath}?limit={limit}", ct)).StatusCode);
    }

    // Archived changes cleanup-00, cleanup-01, ... in one repo, archived at the given times.
    private static async Task SeedArchivedAsync(PanopticonFactory factory, IReadOnlyList<DateTimeOffset> archivedAt, CancellationToken ct)
    {
        await using var db = factory.NewDb();
        var repo = new Repo
        {
            Id = Guid.NewGuid(),
            Host = "github.com",
            Owner = "emmz",
            Name = "panopticon",
            HostKey = "github.com",
            OwnerKey = "emmz",
            NameKey = "panopticon",
        };
        db.Repos.Add(repo);
        for (var i = 0; i < archivedAt.Count; i++)
        {
            var name = $"cleanup-{i:D2}";
            db.Changes.Add(new Change
            {
                Id = Guid.NewGuid(),
                RepoId = repo.Id,
                Name = name,
                NameKey = name,
                Snapshot = new ProgressSnapshot(),
                LastReportedAt = archivedAt[i],
                ArchivedAt = archivedAt[i],
                ArchiveFolder = $"2026-11-08-{name}",
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
