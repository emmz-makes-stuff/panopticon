using System.Text.Json;
using Panopticon.Cli.Hook;

namespace Panopticon.Cli.Tests.Hook;

// agent-interaction D2; spec agent-presence: "Activity text".
public sealed class ActivitySummaryTests
{
    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Bash_with_a_description_shows_the_description_and_never_the_command()
    {
        var summary = ActivitySummary.For("Bash", Input("""{ "command": "make gates", "description": "Run make gates" }"""));

        Assert.Equal("Run make gates", summary);
    }

    [Theory]
    [InlineData("""{ "command": "curl -H 'Authorization: Bearer pnp_secret' https://x" }""")]
    [InlineData("""{ "command": "curl -H 'Authorization: Bearer pnp_secret' https://x", "description": "  " }""")]
    [InlineData("""{ "command": "curl -H 'Authorization: Bearer pnp_secret' https://x", "description": 3 }""")]
    public void Bash_without_a_description_is_running_a_command(string input)
    {
        var summary = ActivitySummary.For("Bash", Input(input));

        Assert.Equal("Running a command", summary);
        Assert.DoesNotContain("pnp_secret", summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Edit", """{ "file_path": "/src/panopticon/openspec/changes/tags/tasks.md", "old_string": "secret" }""", "Editing tasks.md")]
    [InlineData("MultiEdit", """{ "file_path": "/src/a/Program.cs" }""", "Editing Program.cs")]
    [InlineData("Write", """{ "file_path": "C:\\src\\a\\notes.md", "content": "secret" }""", "Editing notes.md")]
    [InlineData("NotebookEdit", """{ "notebook_path": "/src/a/analysis.ipynb" }""", "Editing analysis.ipynb")]
    [InlineData("Read", """{ "file_path": "/src/a/design.md" }""", "Reading design.md")]
    [InlineData("Edit", """{ }""", "Editing a file")]
    [InlineData("Grep", """{ "pattern": "secret" }""", "Searching the code")]
    [InlineData("Glob", """{ "pattern": "**/*.cs" }""", "Searching the code")]
    [InlineData("WebFetch", """{ "url": "https://example.com/?token=secret" }""", "Searching the web")]
    [InlineData("WebSearch", """{ "query": "secret" }""", "Searching the web")]
    [InlineData("Agent", """{ "description": "Review the diff", "prompt": "secret" }""", "Running agent: Review the diff")]
    [InlineData("Task", """{ "description": "Explore the CLI" }""", "Running agent: Explore the CLI")]
    [InlineData("Skill", """{ "skill": "emmz:devlog", "args": "secret" }""", "Using skill emmz:devlog")]
    [InlineData("mcp__panopticon__report_progress", """{ "change": "secret" }""", "Calling panopticon report_progress")]
    [InlineData("mcp__plugin_panopticon_panopticon__tag_change", """{ }""", "Calling plugin_panopticon_panopticon tag_change")]
    [InlineData("TodoWrite", """{ "todos": ["secret"] }""", "Using TodoWrite")]
    [InlineData("mcp__broken", """{ }""", "Using mcp__broken")]
    public void Each_tool_names_itself_and_at_most_a_file_name(string tool, string input, string expected)
    {
        var summary = ActivitySummary.For(tool, Input(input));

        Assert.Equal(expected, summary);
        Assert.DoesNotContain("secret", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_or_odd_input_still_gives_a_line()
    {
        Assert.Equal("Running a command", ActivitySummary.For("Bash", null));
        Assert.Equal("Reading a file", ActivitySummary.For("Read", Input("\"just a string\"")));
        Assert.Equal("Running an agent", ActivitySummary.For("Agent", Input("[]")));
    }

    [Fact]
    public void A_300_character_description_is_cut_to_120()
    {
        var summary = ActivitySummary.For("Bash", Input($$"""{ "description": "{{new string('d', 300)}}" }"""));

        Assert.Equal(new string('d', ActivitySummary.MaxLength), summary);
    }

    [Fact]
    public void A_multi_line_description_becomes_one_line()
    {
        var summary = ActivitySummary.For("Bash", Input("""{ "description": "Run the gates\n\tthen  commit\r\n" }"""));

        Assert.Equal("Run the gates then commit", summary);
    }

    [Fact]
    public void The_cut_never_splits_a_character()
    {
        // 119 letters, then a 2-unit emoji: it doesn't fit whole in 120, so it goes.
        var summary = ActivitySummary.OneLine(new string('a', 119) + "🙂🙂");

        Assert.Equal(new string('a', 119), summary);
    }
}
