using System.Net;
using System.Net.Http.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Panopticon.Contracts;
using Panopticon.Server.Data;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>
/// The setting for the tags tests (spec tags): the factory's member (Emmz) owns <c>PAN</c>, Sam is its editor and Kit
/// its viewer, each signed in. Reports go in through Emmz's MCP client.
/// </summary>
public sealed class TagWorld : IAsyncDisposable
{
    public const string SamEmail = "sam@example.com";
    public const string KitEmail = "kit@example.com";
    public const string Tasks = "## 1. Setup\n- [ ] 1.1 Start\n";

    private TagWorld(PanopticonFactory factory, HttpClient emmz, HttpClient sam, HttpClient kit, Guid pan, McpClient reporter)
    {
        (Factory, Emmz, Sam, Kit, Pan, Reporter) = (factory, emmz, sam, kit, pan, reporter);
    }

    public PanopticonFactory Factory { get; }

    /// <summary>The owner of <c>PAN</c>, signed in.</summary>
    public HttpClient Emmz { get; }

    /// <summary>An editor of <c>PAN</c>, signed in.</summary>
    public HttpClient Sam { get; }

    /// <summary>A viewer of <c>PAN</c>, signed in.</summary>
    public HttpClient Kit { get; }

    public Guid Pan { get; }

    /// <summary>An MCP client with Emmz's token.</summary>
    public McpClient Reporter { get; }

    public static async Task<TagWorld> CreateAsync(PostgresFixture postgres, S3Fixture s3, CancellationToken cancellationToken,
        TimeProvider? clock = null,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        ArgumentNullException.ThrowIfNull(postgres);
        ArgumentNullException.ThrowIfNull(s3);
        var factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(cancellationToken), await s3.CreateBucketAsync(cancellationToken))
        {
            Clock = clock,
            Settings = settings ?? new Dictionary<string, string>(),
        };
        var emmz = await factory.CreateSignedInClientAsync(cancellationToken: cancellationToken);
        var pan = await emmz.CreateProjectAsync("PAN", cancellationToken);
        await emmz.InviteToProjectAsync(pan, SamEmail, ProjectRoles.Editor, cancellationToken);
        await emmz.InviteToProjectAsync(pan, KitEmail, ProjectRoles.Viewer, cancellationToken);
        var sam = await factory.CreateSignedInClientAsync(SamEmail, cancellationToken);
        var kit = await factory.CreateSignedInClientAsync(KitEmail, cancellationToken);
        var reporter = await factory.CreateMcpClientAsync(cancellationToken);
        return new TagWorld(factory, emmz, sam, kit, pan, reporter);
    }

    public static ReportProgressArguments Report(
        string change,
        Guid? project,
        string machine = "vm-1",
        bool archived = false,
        IReadOnlyList<string>? personalTags = null) =>
        new(new RepoRef("github.com", "emmz", "panopticon"), change, new AgentRef("claude-code", machine), Tasks)
        {
            Project = project?.ToString(),
            Archive = archived ? $"2026-11-10-{change}" : null,
            PersonalTags = personalTags,
        };

    /// <summary>Reports, failing the test if the server refuses; returns the result.</summary>
    public static async Task<CallToolResult> ReportAsync(McpClient client, ReportProgressArguments report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(report);
        var result = await client.CallToolAsync(ContractsAssembly.ReportProgressToolName, report.ToToolArguments(), cancellationToken: cancellationToken);
        if (result.IsError is true)
        {
            throw new InvalidOperationException($"The report was refused: {TextOf(result)}");
        }

        return result;
    }

    /// <summary>Reports as Emmz and returns the change's id.</summary>
    public async Task<Guid> ReportChangeAsync(ReportProgressArguments report, CancellationToken cancellationToken)
    {
        var result = await ReportAsync(Reporter, report, cancellationToken);
        return Guid.Parse(result.StructuredContent!.Value.GetProperty("changeId").GetString()!);
    }

    public static string TextOf(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
    }

    /// <summary>Adds a tag to <c>PAN</c> as Emmz, and returns its id.</summary>
    public async Task<Guid> AddPanTagAsync(string tag, CancellationToken cancellationToken)
    {
        using var added = await Emmz.PostApiAsync(ProjectTagEndpoints.TagsPath(Pan), new { tag }, cancellationToken);
        if (added.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Adding {tag} answered {added.StatusCode}.");
        }

        return (await added.Content.ReadFromJsonAsync<ProjectTagView>(cancellationToken))!.Id;
    }

    public async ValueTask DisposeAsync()
    {
        await Reporter.DisposeAsync();
        Emmz.Dispose();
        Sam.Dispose();
        Kit.Dispose();
        await Factory.DisposeAsync();
    }
}
