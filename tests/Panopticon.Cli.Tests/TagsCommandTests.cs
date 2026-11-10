using Panopticon.Cli.Config;

namespace Panopticon.Cli.Tests;

// specs/cli-bridge "Personal tags in the config" (tags D8).
public sealed class TagsCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("panopticon-tags-").FullName;

    private string ConfigPath => Path.Combine(_root, "panopticon", "config.json");

    private CliConfigFile Config => new(ConfigPath);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private (int Exit, string Out, string Err) Run(params string[] args)
    {
        var (stdout, stderr) = (new StringWriter(), new StringWriter());
        var exit = new TagsCommand(Config, stdout, stderr).Run(args);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private void Saved(params string[] tags) => Config.Write(new CliConfig("https://p.example.com/", "pnp_saved", tags));

    private static List<string> Lines(string output) => [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    [Fact]
    public void With_none_saved_it_says_so()
    {
        Assert.Equal((0, "No personal tags."), (Run().Exit, Run().Out.Trim()));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public void Adding_a_tag_lists_it_with_the_others_and_keeps_the_login()
    {
        Saved("dev");

        var added = Run("add", "  language :  csharp ");

        Assert.Equal(0, added.Exit);
        Assert.Equal(["dev", "language: csharp"], Lines(Run().Out));
        var saved = Config.Read(new StringWriter())!;
        Assert.Equal(("https://p.example.com/", "pnp_saved"), (saved.ServerUrl, saved.Token));
    }

    [Fact]
    public void Adding_with_no_file_creates_one()
    {
        Assert.Equal(0, Run("add", "dev", "area: cli").Exit);

        Assert.Equal(["dev", "area: cli"], Lines(Run().Out));
    }

    [Fact]
    public void Removing_a_tag_ignores_case()
    {
        Saved("dev", "Language: CSharp");

        Assert.Equal(0, Run("remove", "DEV", "language: csharp").Exit);

        Assert.Equal("No personal tags.", Run().Out.Trim());
    }

    // Review fix 11.5: a hand-edited config with a null entry is refused with a message, not a crash.
    [Fact]
    public void A_null_entry_in_a_hand_edited_config_does_not_crash_remove()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, """{ "serverUrl": "https://p.example.com/", "token": "pnp_saved", "tags": [null, "dev"] }""");

        var refused = Run("remove", "bad tag");
        Assert.Equal(1, refused.Exit);
        Assert.Contains("bad tag", refused.Err, StringComparison.Ordinal);

        Assert.Equal(0, Run("remove", "dev").Exit);
    }

    [Theory]
    [InlineData("add", "has space")]
    [InlineData("add", "key:")]
    [InlineData("add", "ok", "has space")]
    public void A_malformed_tag_is_refused_naming_it_and_changes_nothing(params string[] args)
    {
        Saved("dev");
        var before = File.ReadAllText(ConfigPath);

        var (exit, _, err) = Run(args);

        Assert.Equal(1, exit);
        Assert.Contains($"\"{args[^1]}\"", err, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    [Theory]
    [InlineData("DEV")]
    [InlineData("new", "New")]
    public void A_duplicate_is_refused_and_changes_nothing(params string[] tags)
    {
        Saved("dev");
        var before = File.ReadAllText(ConfigPath);

        var (exit, _, err) = Run(["add", .. tags]);

        Assert.Equal(1, exit);
        Assert.Contains("already one of your personal tags", err, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void The_21st_tag_is_refused_and_changes_nothing()
    {
        Saved([.. Enumerable.Range(1, 20).Select(i => $"tag-{i}")]);
        var before = File.ReadAllText(ConfigPath);

        var (exit, _, err) = Run("add", "one-too-many");

        Assert.Equal(1, exit);
        Assert.Contains("at most 20", err, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void Removing_one_that_isnt_saved_is_refused_and_changes_nothing()
    {
        Saved("dev", "urgent");
        var before = File.ReadAllText(ConfigPath);

        var (exit, _, err) = Run("remove", "urgent", "missing");

        Assert.Equal(1, exit);
        Assert.Contains("\"missing\" isn't one of your personal tags", err, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void A_hand_edited_malformed_tag_can_be_removed_by_its_text()
    {
        Saved("dev", "has space");

        Assert.Equal(0, Run("remove", "HAS SPACE").Exit);

        Assert.Equal(["dev"], Lines(Run().Out));
    }

    [Fact]
    public void An_unreadable_file_is_left_alone_and_named_without_its_content()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "{ \"token\": \"pnp_secret\"");

        foreach (var args in new[] { Array.Empty<string>(), ["add", "dev"], ["remove", "dev"] })
        {
            var (exit, _, err) = Run(args);
            Assert.Equal(1, exit);
            Assert.Contains(ConfigPath, err, StringComparison.Ordinal);
            Assert.DoesNotContain("pnp_secret", err, StringComparison.Ordinal);
        }

        Assert.Equal("{ \"token\": \"pnp_secret\"", File.ReadAllText(ConfigPath));
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("frobnicate")]
    public void A_bad_subcommand_shows_the_usage(string subcommand)
    {
        var (exit, _, err) = Run(subcommand);

        Assert.Equal(1, exit);
        Assert.Contains("Usage: panopticon tags", err, StringComparison.Ordinal);
    }
}
