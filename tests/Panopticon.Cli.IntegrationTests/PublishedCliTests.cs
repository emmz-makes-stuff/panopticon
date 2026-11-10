using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Cli.IntegrationTests;

// Spec cli-bridge: Stdio MCP server and Single self-contained binary, end to end. The published Native AOT binary runs
// as a child process over stdio, against the server on a real Kestrel port (Testcontainers-backed).
public class PublishedCliTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Tasks = """
        ## 1. Setup
        - [x] 1.1 First
        - [x] 1.2 Second
        ## 2. Core
        - [x] 2.1 Third
        - [ ] 2.2 Fourth
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The binary reads a saved login when a variable is unset: point it at an empty config directory, never the real one.
    private static readonly string _configHome = Directory.CreateTempSubdirectory("panopticon-cli-config-").FullName;

    [Fact]
    public async Task Published_binary_lists_tools_and_reports_to_the_server()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        server.UseKestrel(0);
        server.StartServer();
        var serverUrl = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        using var repo = TempGitRepo.Create("git@github.com:emmz/panopticon.git");
        repo.WriteTasks("walking-skeleton", Tasks);
        var stderr = new ConcurrentQueue<string>();

        await using var client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = PublishedCli.Path,
                Arguments = ["mcp"],
                WorkingDirectory = repo.Root,
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["PANOPTICON_SERVER_URL"] = serverUrl,
                    ["PANOPTICON_TOKEN"] = await server.SeedApiTokenAsync(cancellationToken: Ct),
                    ["XDG_CONFIG_HOME"] = _configHome,
                    ["APPDATA"] = _configHome,
                },
                StandardErrorLines = stderr.Enqueue,
            }),
            new McpClientOptions { ClientInfo = new Implementation { Name = "e2e-agent", Version = "1.0.0" } },
            cancellationToken: Ct);

        Assert.Equal(["list_projects", "report_progress", "tag_change"], (await client.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).Order(StringComparer.Ordinal));

        var result = await client.CallToolAsync("report_progress", new Dictionary<string, object?> { ["change"] = "walking-skeleton" }, cancellationToken: Ct);

        var text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Assert.True(result.IsError is not true, $"{text}\nstderr:\n{string.Join('\n', stderr)}");
        Assert.StartsWith("Overall 3/4", text, StringComparison.Ordinal);

        await using var db = server.NewDb();
        var card = Assert.Single(await new ChangeCards(db).ListAsync(await Viewer.LoadAsync(db, await server.MemberIdAsync(cancellationToken: Ct), Ct), Ct));
        Assert.Equal(new CardRepo("github.com", "emmz", "panopticon"), card.Repo);
        var agent = Assert.Single(card.Agents);
        Assert.Equal(("e2e-agent", Environment.MachineName), (agent.Client, agent.Machine));

        // Logs go to stderr (the protocol owns stdout).
        Assert.Contains(stderr, line => line.Contains("Reported change walking-skeleton", StringComparison.Ordinal));

        // projects: the agent lists the Projects, links the repo, and the next report lands in the Project.
        var pan = await SeedProjectAsync(server, "PAN");
        var listed = await client.CallToolAsync("list_projects", cancellationToken: Ct);
        Assert.True(listed.IsError is not true, string.Join(" ", listed.Content.OfType<TextContentBlock>().Select(b => b.Text)));
        Assert.Equal(pan.ToString(), listed.StructuredContent!.Value.GetProperty("projects")[0].GetProperty("id").GetString());
        await File.WriteAllTextAsync(Path.Combine(repo.Root, ".panopticon.json"), $"{{ \"project\": \"{pan}\" }}", Ct);
        repo.WriteTasks("linked-work", Tasks);
        var linked = await client.CallToolAsync("report_progress", new Dictionary<string, object?> { ["change"] = "linked-work" }, cancellationToken: Ct);
        Assert.True(linked.IsError is not true, string.Join(" ", linked.Content.OfType<TextContentBlock>().Select(b => b.Text)));
        await using var after = server.NewDb();
        Assert.Equal(pan, (await after.Changes.SingleAsync(c => c.Name == "linked-work", Ct)).ProjectId);
    }

    // tags D8: `panopticon tags add` while the published bridge runs reaches the server on the next report, with no
    // restart. Its own config directory, so the shared one stays tag-free.
    [Fact]
    public async Task Published_binary_sends_a_tag_added_while_it_runs_on_the_next_report()
    {
        await using var server = await PanopticonFactory.CreateAsync(postgres, s3, Ct);
        server.UseKestrel(0);
        server.StartServer();
        var serverUrl = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        var configHome = Directory.CreateTempSubdirectory("panopticon-cli-tags-").FullName;
        using var repo = TempGitRepo.Create("git@github.com:emmz/panopticon.git");
        repo.WriteTasks("walking-skeleton", Tasks);
        var stderr = new ConcurrentQueue<string>();
        await using var client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = PublishedCli.Path,
                Arguments = ["mcp"],
                WorkingDirectory = repo.Root,
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["PANOPTICON_SERVER_URL"] = serverUrl,
                    ["PANOPTICON_TOKEN"] = await server.SeedApiTokenAsync(cancellationToken: Ct),
                    ["XDG_CONFIG_HOME"] = configHome,
                    ["APPDATA"] = configHome,
                },
                StandardErrorLines = stderr.Enqueue,
            }),
            new McpClientOptions { ClientInfo = new Implementation { Name = "e2e-agent", Version = "1.0.0" } },
            cancellationToken: Ct);

        async Task<IReadOnlyList<PersonalTagRef>> ReportAsync()
        {
            var result = await client.CallToolAsync("report_progress", new Dictionary<string, object?> { ["change"] = "walking-skeleton" }, cancellationToken: Ct);
            Assert.True(result.IsError is not true, $"{string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text))}\nstderr:\n{string.Join('\n', stderr)}");
            await using var db = server.NewDb();
            var viewer = await Viewer.LoadAsync(db, await server.MemberIdAsync(cancellationToken: Ct), Ct);
            return Assert.Single(await new ChangeCards(db).ListAsync(viewer, Ct)).PersonalTags;
        }

        Assert.Empty(await ReportAsync());

        var added = await RunPublishedAsync(configHome, "tags", "add", "urgent", "language: csharp");
        Assert.Equal(0, added.ExitCode);
        var listed = await RunPublishedAsync(configHome, "tags");
        Assert.Equal(["urgent", "language: csharp"], listed.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        Assert.Equal([new PersonalTagRef("language", "csharp"), new PersonalTagRef("urgent", null)], await ReportAsync());
    }

    /// <summary>A Project owned by the server's default member, written straight to the database.</summary>
    private static async Task<Guid> SeedProjectAsync(PanopticonFactory server, string code)
    {
        var owner = await server.MemberIdAsync(cancellationToken: Ct);
        await using var db = server.NewDb();
        var project = new Project { Id = Guid.NewGuid(), Name = code, Code = code, CodeKey = code.ToLowerInvariant(), CreatedAt = DateTimeOffset.UtcNow, CreatedByMemberId = owner };
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, MemberId = owner, Role = ProjectRoles.Owner, JoinedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(Ct);
        return project.Id;
    }

    [Fact]
    public async Task Stdout_carries_only_protocol_messages_even_when_the_bridge_logs()
    {
        using var repo = TempGitRepo.Create();
        var startInfo = new ProcessStartInfo(PublishedCli.Path)
        {
            ArgumentList = { "mcp" },
            WorkingDirectory = repo.Root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Unconfigured, so the tool call fails and the bridge logs a warning.
        startInfo.Environment.Remove("PANOPTICON_SERVER_URL");
        startInfo.Environment.Remove("PANOPTICON_TOKEN");
        startInfo.Environment["XDG_CONFIG_HOME"] = _configHome;
        startInfo.Environment["APPDATA"] = _configHome;

        using var process = Process.Start(startInfo)!;
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        try
        {
            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"raw","version":"1"}}}""");
            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"report_progress","arguments":{"change":"walking-skeleton"}}}""");
            await process.StandardInput.FlushAsync(Ct);

            var lines = new List<string>();
            JsonElement? toolResponse = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (toolResponse is null && await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                lines.Add(line);
                using var message = JsonDocument.Parse(line); // Throws if anything but JSON reached stdout.
                if (message.RootElement.TryGetProperty("id", out var id) && id.GetInt32() == 2)
                {
                    toolResponse = message.RootElement.Clone();
                }
            }

            Assert.NotNull(toolResponse);
            Assert.True(toolResponse.Value.GetProperty("result").GetProperty("isError").GetBoolean());
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                process.Kill();
            }
        }

        Assert.Contains("PANOPTICON_TOKEN", await stderr, StringComparison.Ordinal);
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>Runs the published binary to completion with an empty config directory.</summary>
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunPublishedAsync(string configHome, params string[] args)
    {
        var startInfo = new ProcessStartInfo(PublishedCli.Path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment.Remove("PANOPTICON_SERVER_URL");
        startInfo.Environment.Remove("PANOPTICON_TOKEN");
        startInfo.Environment["XDG_CONFIG_HOME"] = configHome;
        startInfo.Environment["APPDATA"] = configHome;

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout, await stderr);
    }

    [Fact]
    public async Task Published_binary_help_lists_login()
    {
        var (exitCode, stdout, _) = await RunPublishedAsync(_configHome, "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("login [--server <url>]", stdout, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_configHome, "panopticon", "config.json"), stdout, StringComparison.Ordinal);
    }

    // The login path (HttpClient and the source-generated contracts) works under Native AOT: against a server that
    // isn't there, it fails with a message and exit code 1, not a trimming or reflection exception.
    [Fact]
    public async Task Published_binary_login_against_an_unreachable_server_fails_cleanly()
    {
        var configHome = Directory.CreateTempSubdirectory("panopticon-cli-login-").FullName;

        // Port 9 (discard) on loopback: nothing listens there.
        var (exitCode, stdout, stderr) = await RunPublishedAsync(configHome, "login", "--server", "http://127.0.0.1:9/");

        Assert.Equal(1, exitCode);
        Assert.Empty(stdout);
        Assert.StartsWith("Couldn't reach the Panopticon server at http://127.0.0.1:9/", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(configHome, "panopticon", "config.json")));
    }
}
