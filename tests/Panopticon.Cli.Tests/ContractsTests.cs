using System.Text.Json;
using Panopticon.Contracts;

namespace Panopticon.Cli.Tests;

// The CLI is the Native AOT consumer of the contracts, so their source-generated round trip is tested here.
public class ContractsTests
{
    private static readonly ReportProgressArguments _arguments = new(
        new RepoRef("dev.azure.com", "acme/Nova", "nova-api"),
        "walking-skeleton",
        new AgentRef("claude-code", "vm-1"),
        "## 1. Setup\n- [x] 1.1\n",
        Section: 2);

    [Fact]
    public void Arguments_round_trip_through_the_source_generated_context()
    {
        var json = JsonSerializer.Serialize(_arguments, ContractsJsonContext.Default.ReportProgressArguments);
        var back = JsonSerializer.Deserialize(json, ContractsJsonContext.Default.ReportProgressArguments);

        Assert.Equal(_arguments, back);
        Assert.Contains("\"tasksMarkdown\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_round_trips_and_omits_a_missing_section()
    {
        var result = new ReportProgressResult("c1", new ProgressCount(0, 0), CurrentSection: null);

        var json = JsonSerializer.Serialize(result, ContractsJsonContext.Default.ReportProgressResult);

        Assert.Equal(result, JsonSerializer.Deserialize(json, ContractsJsonContext.Default.ReportProgressResult));
        Assert.DoesNotContain("currentSection", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Tool_arguments_use_the_wire_names()
    {
        var arguments = _arguments.ToToolArguments();

        Assert.Equal(["repo", "change", "agent", "tasksMarkdown", "section"], arguments.Keys);
        var repo = Assert.IsType<JsonElement>(arguments["repo"]);
        Assert.Equal("acme/Nova", repo.GetProperty("owner").GetString());
        Assert.Equal(2, Assert.IsType<JsonElement>(arguments["section"]).GetInt32());
    }

    [Fact]
    public void Tool_arguments_leave_out_an_unreported_section()
    {
        var arguments = (_arguments with { Section = null }).ToToolArguments();

        Assert.DoesNotContain("section", arguments.Keys);
    }

    [Fact]
    public void Describe_summarises_overall_and_section()
    {
        var result = new ReportProgressResult(
            "c1",
            new ProgressCount(7, 20),
            new SectionSummary(2, "Core", new ProgressCount(2, 5)));

        Assert.Equal("Overall 7/20 · section 2 Core 2/5", result.Describe());
    }
}
