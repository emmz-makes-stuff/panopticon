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
    public void Arguments_without_documents_serialize_exactly_as_before()
    {
        var json = JsonSerializer.Serialize(_arguments, ContractsJsonContext.Default.ReportProgressArguments);

        Assert.Equal(
            """{"repo":{"host":"dev.azure.com","owner":"acme/Nova","name":"nova-api"},"change":"walking-skeleton","agent":{"client":"claude-code","machine":"vm-1"},"tasksMarkdown":"## 1. Setup\n- [x] 1.1\n","section":2}""",
            json);
    }

    [Fact]
    public void Arguments_with_documents_and_an_archive_round_trip()
    {
        var arguments = _arguments with
        {
            Documents = [new ChangeDocument("tasks.md", _arguments.TasksMarkdown), new ChangeDocument("specs/cli-bridge/spec.md", "# Spec\n")],
            Archive = "2026-11-08-walking-skeleton",
        };

        var json = JsonSerializer.Serialize(arguments, ContractsJsonContext.Default.ReportProgressArguments);
        var back = JsonSerializer.Deserialize(json, ContractsJsonContext.Default.ReportProgressArguments);

        Assert.NotNull(back);
        Assert.Equal(arguments.Documents, back.Documents);
        Assert.Equal(arguments, back with { Documents = arguments.Documents });
        Assert.Contains(
            ""","documents":[{"path":"tasks.md","content":"## 1. Setup\n- [x] 1.1\n"},{"path":"specs/cli-bridge/spec.md","content":"# Spec\n"}],"archive":"2026-11-08-walking-skeleton"}""",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Tool_arguments_carry_documents_and_the_archive_by_their_wire_names()
    {
        var arguments = (_arguments with
        {
            Documents = [new ChangeDocument("design.md", "# Design\n")],
            Archive = "2026-11-08-walking-skeleton",
        }).ToToolArguments();

        Assert.Equal(["repo", "change", "agent", "tasksMarkdown", "section", "documents", "archive"], arguments.Keys);
        var document = Assert.Single(Assert.IsType<JsonElement>(arguments["documents"]).EnumerateArray());
        Assert.Equal("design.md", document.GetProperty("path").GetString());
        Assert.Equal("# Design\n", document.GetProperty("content").GetString());
        Assert.Equal("2026-11-08-walking-skeleton", Assert.IsType<JsonElement>(arguments["archive"]).GetString());
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
    public void Device_login_records_round_trip_with_the_wire_names()
    {
        var context = ContractsJsonContext.Default;
        var start = new DeviceStartResponse("secret", "BCDF-GHJK", "https://p.example/device?code=BCDF-GHJK", 5, 600);
        var approved = new DevicePollResponse(DevicePollStatus.Approved, "pnp_token", new DeviceMember("Emmz", "emmz@example.com"));

        var startJson = JsonSerializer.Serialize(start, context.DeviceStartResponse);
        var approvedJson = JsonSerializer.Serialize(approved, context.DevicePollResponse);

        Assert.Equal(start, JsonSerializer.Deserialize(startJson, context.DeviceStartResponse));
        Assert.Equal(approved, JsonSerializer.Deserialize(approvedJson, context.DevicePollResponse));
        Assert.Equal(
            """{"deviceSecret":"secret","userCode":"BCDF-GHJK","approveUrl":"https://p.example/device?code=BCDF-GHJK","intervalSeconds":5,"expiresInSeconds":600}""",
            startJson);
        Assert.Contains("\"member\":{\"displayName\":\"Emmz\",\"email\":\"emmz@example.com\"}", approvedJson, StringComparison.Ordinal);
        Assert.Equal(
            new DeviceStartRequest("vm-1"),
            JsonSerializer.Deserialize("""{"machineName":"vm-1"}""", context.DeviceStartRequest));
        Assert.Equal(
            """{"deviceSecret":"s"}""",
            JsonSerializer.Serialize(new DevicePollRequest("s"), context.DevicePollRequest));
    }

    [Fact]
    public void A_pending_poll_carries_only_its_status()
    {
        var json = JsonSerializer.Serialize(new DevicePollResponse(DevicePollStatus.SlowDown), ContractsJsonContext.Default.DevicePollResponse);

        Assert.Equal("""{"status":"slow_down"}""", json);
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
