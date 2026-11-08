using System.Text;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Contracts;

namespace Panopticon.Cli.Tests;

// Spec cli-bridge: "Change documents read locally", "Document size limits", "Archived change reported" (design D2, D3).
public class ChangeDocumentsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Change = "openspec/changes/walking-skeleton";

    [Fact]
    public async Task Change_exists_returns_the_current_tasks_content()
    {
        using var repo = TempGitRepo.Create();
        var path = repo.WriteTasks("walking-skeleton", "## 1. Setup\n- [x] 1.1 First\n");

        var folder = await ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct);
        Assert.Equal("## 1. Setup\n- [x] 1.1 First\n", folder.Tasks);
        Assert.Null(folder.Archive);

        // It reads the file as it is now, not a cached copy.
        await File.WriteAllTextAsync(path, "## 1. Setup\n- [x] 1.1 First\n- [ ] 1.2 Second\n", Ct);
        Assert.EndsWith("1.2 Second\n", (await ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct)).Tasks, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Whole_folder_is_forwarded_by_relative_path_in_ordinal_order()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", "- [ ] a\n");
        repo.WriteFile($"{Change}/proposal.md", "# Proposal\n");
        repo.WriteFile($"{Change}/design.md", "# Design\n");
        repo.WriteFile($"{Change}/specs/cli-bridge/spec.md", "# Spec\n");
        repo.WriteFile($"{Change}/devlog/20261108-1515-section-1-done.md", "# Done\n");
        repo.WriteFile($"{Change}/.openspec.yaml", "schema: spec-driven\n");

        var folder = await ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct);

        Assert.Equal(
            [".openspec.yaml", "design.md", "devlog/20261108-1515-section-1-done.md", "proposal.md", "specs/cli-bridge/spec.md", "tasks.md"],
            folder.Documents.Select(d => d.Path));
        Assert.Equal("# Spec\n", folder.Documents.Single(d => d.Path == "specs/cli-bridge/spec.md").Content);
    }

    [Fact]
    public async Task Other_files_are_skipped()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", "- [ ] a\n");
        repo.WriteFile($"{Change}/diagram.png", "not really a png");
        repo.WriteFile($"{Change}/notes.txt", "text");
        repo.WriteFile($"{Change}/config.YML", "a: 1\n");

        var folder = await ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct);

        Assert.Equal(["config.YML", "tasks.md"], folder.Documents.Select(d => d.Path));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task A_link_out_of_the_folder_is_not_carried()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic links need privileges on Windows.");
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", "- [ ] a\n");
        var outside = repo.WriteFile("secrets/notes.md", "secret");
        var outsideFolder = repo.CreateSubdirectory("secrets");
        File.CreateSymbolicLink(Path.Combine(repo.Root, "openspec", "changes", "walking-skeleton", "notes.md"), outside);
        Directory.CreateSymbolicLink(Path.Combine(repo.Root, "openspec", "changes", "walking-skeleton", "linked"), outsideFolder);

        var folder = await ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct);

        Assert.Equal(["tasks.md"], folder.Documents.Select(d => d.Path));
    }

    [Fact]
    public async Task A_byte_order_mark_is_dropped_and_invalid_utf8_names_the_file()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", "- [ ] a\n");
        var bom = Path.Combine(repo.Root, "openspec", "changes", "walking-skeleton", "design.md");
        await File.WriteAllBytesAsync(bom, [.. Encoding.UTF8.Preamble, .. "# Design"u8.ToArray()], Ct);

        Assert.Equal("# Design", (await ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct)).Documents.Single(d => d.Path == "design.md").Content);

        await File.WriteAllBytesAsync(bom, [0x23, 0x20, 0xC3, 0x28], Ct);
        var error = await Assert.ThrowsAsync<BridgeException>(() => ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct));
        Assert.StartsWith("'design.md' isn't valid UTF-8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task A_path_the_server_would_refuse_is_named_and_nothing_is_read()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows file names can't hold control characters.");
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", "- [ ] a\n");
        repo.WriteFile($"{Change}/devlog/bad\u0001name.md", "x");

        var error = await Assert.ThrowsAsync<BridgeException>(() => ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct));

        Assert.Equal("'devlog/bad\u0001name.md' can't be sent: its path contains a control character. Nothing was sent.", error.Message);
    }

    // --- Document size limits ---

    [Fact]
    public async Task Tasks_named_with_another_case_is_a_clear_error()
    {
        // On a case-insensitive disk (macOS, Windows) 'tasks.md' finds this file; on Linux it doesn't. Either way the
        // bridge says what's wrong, rather than failing with an InvalidOperationException.
        using var repo = TempGitRepo.Create();
        repo.WriteFile($"{Change}/Tasks.md", "- [ ] a\n");

        var error = await Assert.ThrowsAsync<BridgeException>(() => ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct));

        Assert.StartsWith("Change 'walking-skeleton' has no tasks.md", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_file_too_large_names_it_and_the_limit()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", "- [ ] a\n");
        repo.WriteFile($"{Change}/devlog/huge.md", new string('a', 300 * 1024));

        var error = await Assert.ThrowsAsync<BridgeException>(() => ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct));

        Assert.Equal("'devlog/huge.md' is 307200 bytes; the limit is 262144 bytes (256 KB). Nothing was sent.", error.Message);
    }

    [Fact]
    public async Task Tasks_may_be_up_to_one_mebibyte()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", new string('a', DocumentLimits.MaxTasksBytes));

        Assert.Equal(DocumentLimits.MaxTasksBytes, (await ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct)).Tasks.Length);

        repo.WriteTasks("walking-skeleton", new string('a', DocumentLimits.MaxTasksBytes + 1));
        var error = await Assert.ThrowsAsync<BridgeException>(() => ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct));
        Assert.Contains("'tasks.md' is 1048577 bytes; the limit is 1048576 bytes (1 MiB)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Folder_too_large_names_the_total_limit()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", "- [ ] a\n");
        for (var i = 0; i < 20; i++)
        {
            repo.WriteFile($"{Change}/devlog/note-{i:D2}.md", new string('a', 250 * 1024));
        }

        var error = await Assert.ThrowsAsync<BridgeException>(() => ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct));

        Assert.StartsWith("The change's documents total ", error.Message, StringComparison.Ordinal);
        Assert.Contains("the limit is 4194304 bytes (4 MB)", error.Message, StringComparison.Ordinal);
    }

    // Spec cli-bridge: Document size limits, Too many files (review fix 9.1).
    [Fact]
    public async Task Too_many_files_names_the_document_limit()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", "- [ ] a\n");
        for (var i = 0; i < DocumentLimits.MaxDocuments; i++)
        {
            repo.WriteFile($"{Change}/devlog/note-{i:D3}.md", "x");
        }

        var error = await Assert.ThrowsAsync<BridgeException>(() => ChangeDocuments.ReadAsync(repo.Root, "walking-skeleton", Ct));

        Assert.Equal("The change has 501 Markdown and YAML files; the limit is 500 documents. Nothing was sent.", error.Message);
    }

    // --- Archived change reported ---

    [Fact]
    public async Task Report_after_archive_reads_the_archive_folder()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteFile("openspec/changes/archive/2026-11-08-team-auth/tasks.md", "- [x] a\n");
        repo.WriteFile("openspec/changes/archive/2026-11-08-team-auth/design.md", "# Final\n");
        repo.WriteFile("openspec/changes/archive/2026-11-08-team-auth-extra/tasks.md", "- [ ] other change\n");

        var folder = await ChangeDocuments.ReadAsync(repo.Root, "team-auth", Ct);

        Assert.Equal("2026-11-08-team-auth", folder.Archive);
        Assert.Equal("- [x] a\n", folder.Tasks);
        Assert.Equal(["design.md", "tasks.md"], folder.Documents.Select(d => d.Path));
    }

    [Fact]
    public async Task Archived_twice_under_one_name_uses_the_latest_date()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteFile("openspec/changes/archive/2026-03-01-cleanup/tasks.md", "- [ ] old\n");
        repo.WriteFile("openspec/changes/archive/2026-11-08-cleanup/tasks.md", "- [x] new\n");
        repo.WriteFile("openspec/changes/archive/2026-12-01-Cleanup/tasks.md", "- [x] different case\n");

        var folder = await ChangeDocuments.ReadAsync(repo.Root, "cleanup", Ct);

        Assert.Equal(("2026-11-08-cleanup", "- [x] new\n"), (folder.Archive, folder.Tasks));
    }

    [Fact]
    public async Task An_active_folder_wins_over_an_archive_of_the_same_name()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteFile("openspec/changes/archive/2026-03-01-cleanup/tasks.md", "- [x] old\n");
        repo.WriteTasks("cleanup", "- [ ] reused\n");

        var folder = await ChangeDocuments.ReadAsync(repo.Root, "cleanup", Ct);

        Assert.Equal((null, "- [ ] reused\n"), (folder.Archive, folder.Tasks));
    }

    [Theory]
    [InlineData("2026-11-08-cleanup", "cleanup", true)]
    [InlineData("2026-11-08-2026-01-01-x", "2026-01-01-x", true)]
    [InlineData("x2026-11-08-cleanup", "cleanup", false)]
    [InlineData("2026-11-8-cleanup", "cleanup", false)]
    [InlineData("2026-11-08-cleanups", "cleanup", false)]
    [InlineData("2026-11-08-Cleanup", "cleanup", false)]
    [InlineData("२०२६-११-०८-cleanup", "cleanup", false)]
    public void Archive_names_are_date_dash_change(string folderName, string change, bool expected) =>
        Assert.Equal(expected, ChangeDocuments.IsArchiveOf(folderName, change));

    [Fact]
    public async Task Change_missing_names_the_paths_it_looked_for()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", "");

        var error = await Assert.ThrowsAsync<BridgeException>(
            () => ChangeDocuments.ReadAsync(repo.Root, "no-such-change", Ct));

        Assert.Contains(Path.Combine(repo.Root, "openspec", "changes", "no-such-change"), error.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("archive", "<YYYY-MM-DD>-no-such-change"), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Change_folder_without_tasks_names_the_file_it_looked_for()
    {
        using var repo = TempGitRepo.Create();
        var directory = repo.CreateSubdirectory(Path.Combine("openspec", "changes", "half-made"));

        var error = await Assert.ThrowsAsync<BridgeException>(
            () => ChangeDocuments.ReadAsync(repo.Root, "half-made", Ct));

        Assert.Contains(Path.Combine(directory, "tasks.md"), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../../etc")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("/etc")]
    [InlineData("C:")]
    [InlineData(" spaced ")]
    [InlineData("")]
    public async Task Path_traversal_is_an_invalid_name_and_reads_nothing(string change)
    {
        using var repo = TempGitRepo.Create();
        // A tasks.md that "../../etc"-style names would reach if they weren't refused.
        File.WriteAllText(Path.Combine(repo.Root, "tasks.md"), "outside");
        repo.WriteTasks("walking-skeleton", "inside");

        var error = await Assert.ThrowsAsync<BridgeException>(() => ChangeDocuments.ReadAsync(repo.Root, change, Ct));

        Assert.StartsWith("Invalid change name", error.Message, StringComparison.Ordinal);
    }
}
