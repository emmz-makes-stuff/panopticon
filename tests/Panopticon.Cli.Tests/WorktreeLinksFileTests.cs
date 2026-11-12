using Panopticon.Cli.Config;

namespace Panopticon.Cli.Tests;

// agent-interaction D9: the link file the bridge writes and the hook reads.
public sealed class WorktreeLinksFileTests : IDisposable
{
    private static readonly DateTimeOffset _at = new(2026, 11, 11, 19, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("panopticon-links-").FullName;

    private string ConfigPath => Path.Combine(_root, "panopticon", "config.json");

    private WorktreeLinksFile Links => WorktreeLinksFile.NextTo(new CliConfigFile(ConfigPath));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void It_lives_next_to_the_config()
    {
        Assert.Equal(Path.Combine(_root, "panopticon", "worktrees.json"), Links.Path);
    }

    [Fact]
    public void Each_working_tree_finds_its_own_change_and_a_new_report_replaces_it()
    {
        Assert.Null(Links.Find("/src/panopticon"));

        Links.Record("/src/panopticon", "tags", _at);
        Links.Record("/src/panopticon-projects", "projects", _at);

        Assert.Equal(new WorktreeLink("tags", _at), Links.Find("/src/panopticon"));
        Assert.Equal("projects", Links.Find("/src/panopticon-projects")?.Change);
        Assert.Null(Links.Find("/src/elsewhere"));

        Links.Record("/src/panopticon", "team-auth", _at.AddMinutes(5));
        Assert.Equal(new WorktreeLink("team-auth", _at.AddMinutes(5)), Links.Find("/src/panopticon"));
        Assert.Equal("projects", Links.Find("/src/panopticon-projects")?.Change);
    }

    [Fact]
    public void A_trailing_separator_names_the_same_working_tree()
    {
        Links.Record("/src/panopticon/", "tags", _at);

        Assert.Equal("tags", Links.Find("/src/panopticon")?.Change);
    }

    [Fact]
    public void Clearing_removes_the_link_only_while_it_names_that_change()
    {
        Links.Record("/src/panopticon", "projects", _at);

        Links.Clear("/src/panopticon", "tags");
        Assert.Equal("projects", Links.Find("/src/panopticon")?.Change);

        Links.Clear("/src/panopticon", "projects");
        Assert.Null(Links.Find("/src/panopticon"));

        // Nothing to clear is no error.
        Links.Clear("/src/never-reported", "projects");
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "/src/panopticon": { "at": "2026-11-11T19:00:00Z" } }""")]
    public void An_unreadable_file_counts_as_empty_and_the_next_write_replaces_it(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Links.Path)!);
        File.WriteAllText(Links.Path, content);

        Assert.Null(Links.Find("/src/panopticon"));

        Links.Record("/src/panopticon", "tags", _at);
        Assert.Equal("tags", Links.Find("/src/panopticon")?.Change);
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void It_is_readable_by_the_user_only()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes only.");

        Links.Record("/src/panopticon", "tags", _at);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Links.Path));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(Links.Path)!));
    }

    [Fact]
    public void Writing_where_no_directory_can_be_made_throws_an_IO_error()
    {
        // The config's directory would sit under a plain file.
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "");
        var links = WorktreeLinksFile.NextTo(new CliConfigFile(Path.Combine(blocker, "config.json")));

        Assert.Null(links.Find("/src/panopticon"));
        Assert.ThrowsAny<IOException>(() => links.Record("/src/panopticon", "tags", _at));
    }
}
