using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Cli.Tests.Infrastructure;
using Panopticon.Server.Auth;
using Panopticon.Server.Changes;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Documents;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;
using Testcontainers.PostgreSql;

namespace Panopticon.Cli.IntegrationTests;

/// <summary>
/// The whole team flow against the real container image (team-auth 9.1, extended by change-documents-and-history
/// 6.1). It builds the image from the <c>Dockerfile</c>, migrates and starts it beside Postgres and SeaweedFS on a
/// private network, then works as a member would:
/// <list type="number">
/// <item>sign in through the logged link (Development with no Resend key logs each email);</item>
/// <item>run the published <c>panopticon login</c> and approve its code;</item>
/// <item>create a Project, find its id through the bridge's <c>list_projects</c>, and link the repo to it with
/// <c>.panopticon.json</c> (projects 10.1);</item>
/// <item>report a whole change folder through <c>panopticon mcp</c> using only the saved login;</item>
/// <item>see the card name the member and show the Project's code;</item>
/// <item>read the change page's documents (change-documents-and-history 6.1);</item>
/// <item>archive the folder and see the running bridge's watcher move the change to the history.</item>
/// </list>
/// Building the image takes minutes, so this is opt-in: <c>make test</c> skips the trait, and <c>make e2e</c> runs it.
/// </summary>
public sealed partial class ContainerEndToEndTests : IAsyncLifetime
{
    public const string Trait = "ContainerE2E";

    private const string MemberEmail = "e2e@example.test";
    private const string Change = "team-auth-e2e";
    private const string DbName = "panopticon";
    private const string DbUser = "panopticon";
    private const string DbPassword = "panopticon";
    private const int S3Port = 8333;

    private static readonly TimeSpan _stepTimeout = TimeSpan.FromSeconds(60);

    private readonly string _configHome = Directory.CreateTempSubdirectory("panopticon-e2e-config-").FullName;
    private readonly int _hostPort = FreeTcpPort();
    private readonly INetwork _network = new NetworkBuilder().Build();
    private IFutureDockerImage? _image;
    private PostgreSqlContainer? _postgres;
    private IContainer? _s3;
    private IContainer? _server;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Uri PublicUrl => new($"http://localhost:{_hostPort}/");

    [GeneratedRegex(@"sign-in/confirm#([A-Za-z0-9_-]+)")]
    private static partial Regex SignInLink();

    [GeneratedRegex("[A-Z]{4}-[A-Z]{4}")]
    private static partial Regex UserCode();

    public async ValueTask InitializeAsync()
    {
        await _network.CreateAsync(Ct);

        _postgres = new PostgreSqlBuilder("postgres:17.11-alpine")
            .WithNetwork(_network)
            .WithNetworkAliases("postgres")
            .WithDatabase(DbName)
            .WithUsername(DbUser)
            .WithPassword(DbPassword)
            .Build();

        // As S3Fixture, with a network alias for the server, and the host port for creating the bucket.
        _s3 = new ContainerBuilder("chrislusf/seaweedfs:4.50")
            .WithNetwork(_network)
            .WithNetworkAliases("s3")
            .WithCommand(
                "server", "-dir=/data", "-volume.max=0", "-master.volumeSizeLimitMB=64",
                "-s3", $"-s3.port={S3Port}", "-s3.config=/etc/seaweedfs/s3.json")
            .WithResourceMapping(
                new FileInfo(Path.Combine(AppContext.BaseDirectory, "Infrastructure", "seaweedfs-s3.json")),
                new FileInfo("/etc/seaweedfs/s3.json"))
            .WithPortBinding(S3Port, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request.ForPort(S3Port).ForPath("/healthz")))
            .Build();

        // The build context is the repo root, and Testcontainers honours its .dockerignore.
        _image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(RepoRoot.Path)
            .WithDockerfile("Dockerfile")
            .WithName($"panopticon-e2e:{Guid.NewGuid():N}")
            .WithCleanUp(true)
            .Build();

        await Task.WhenAll(_postgres.StartAsync(Ct), _s3.StartAsync(Ct), _image.CreateAsync(Ct));
        await CreateBucketAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var container in new IAsyncDisposable?[] { _server, _s3, _postgres })
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }
        }

        if (_image is not null)
        {
            // Delete the tag now: Ryuk's clean-up can miss it, and each run's tag is unique. Layers stay cached.
            await _image.DeleteAsync(CancellationToken.None);
            await _image.DisposeAsync();
        }

        await _network.DisposeAsync();
        Directory.Delete(_configHome, recursive: true);
    }

    [Fact]
    [Trait("Category", Trait)]
    public async Task A_member_signs_in_logs_in_a_machine_reports_reads_the_documents_and_archives()
    {
        try
        {
            await MigrateAsync();
            _server = ServerContainer();
            await _server.StartAsync(Ct);

            using var cookies = new HttpClientHandler { CookieContainer = new CookieContainer() };
            using var browser = new HttpClient(cookies) { BaseAddress = PublicUrl };

            // 1. Sign in as the initial member, through the link the server logged instead of emailing.
            (await browser.PostApiAsync(SignInEndpoints.SignInPath, new { email = MemberEmail }, Ct)).EnsureSuccessStatusCode().Dispose();
            var secret = await WaitForAsync(async () => SignInLink().Match(await ServerLogsAsync()) is { Success: true } m ? m.Groups[1].Value : null, "the logged sign-in link");
            using (var confirmed = await browser.PostApiAsync(SignInEndpoints.ConfirmPath, new { secret }, Ct))
            {
                Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            }

            var member = await browser.GetFromJsonAsync<SessionMember>(SessionEndpoints.SessionPath, Ct);
            Assert.Equal(MemberEmail, member!.Email);

            // 2. `panopticon login`, approved from the signed-in browser.
            var login = await LoginAsync(browser);
            Assert.True(login.ExitCode == 0, $"panopticon login exited {login.ExitCode}.\nstdout:\n{login.Stdout}\nstderr:\n{login.Stderr}");
            Assert.Contains($"as {member.DisplayName} ({MemberEmail})", login.Stdout, StringComparison.Ordinal);

            // 3. Create a Project from the browser, as the Projects page does. The member becomes its owner.
            ProjectView project;
            using (var created = await browser.PostApiAsync(ProjectEndpoints.ProjectsPath, new { name = "End to end", code = "e2e" }, Ct))
            {
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                project = (await created.Content.ReadFromJsonAsync<ProjectView>(Ct))!;
            }

            Assert.Equal(("E2E", ProjectRoles.Owner), (project.Code, project.Role));

            // 4. Report a whole change folder through `panopticon mcp`, with no token in the environment: only the
            //    saved login. The agent links the repo first, from list_projects. The bridge stays up, so its archive
            //    watcher sees step 7.
            using var repo = TempGitRepo.Create("git@github.com:emmz/panopticon.git");
            repo.WriteTasks(Change, "## 1. First\n- [x] 1.1 One\n- [ ] 1.2 Two\n");
            repo.WriteFile($"openspec/changes/{Change}/proposal.md", "# Proposal\n\nWhy we sign in.\n");
            repo.WriteFile($"openspec/changes/{Change}/design.md", "# Design\n");
            repo.WriteFile($"openspec/changes/{Change}/specs/team-auth/spec.md", "# Spec\n");
            repo.WriteFile($"openspec/changes/{Change}/devlog/20261108-1200-start.md", "# Start\n");
            await using var bridge = await BridgeAsync(repo.Root);
            var listed = await bridge.ListProjectsAsync();
            var entry = Assert.Single(listed.GetProperty("projects").EnumerateArray());
            Assert.Equal((project.Id.ToString(), "E2E"), (entry.GetProperty("id").GetString(), entry.GetProperty("code").GetString()));
            repo.WriteFile(ProjectLink.FileName, $"{{\n  \"project\": \"{entry.GetProperty("id").GetString()}\"\n}}\n");
            var report = await bridge.ReportAsync();
            Assert.StartsWith("Overall 1/2", report, StringComparison.Ordinal);

            // 5. The card shows the member on the agent row, and the Project's code.
            var card = Assert.Single((await browser.GetFromJsonAsync<List<ChangeCard>>(DashboardEndpoints.ChangesPath, Ct))!);
            Assert.Equal(Change, card.Change);
            Assert.Equal((project.Id, "E2E", false), (card.Project?.Id, card.Project?.Code, card.Personal));
            var agent = Assert.Single(card.Agents);
            Assert.Equal(("e2e-agent", member.DisplayName), (agent.Client, agent.Member));

            // 6. The change page lists every document the bridge sent, and renders one.
            var page = (await browser.GetFromJsonAsync<ChangePage>(ChangeEndpoints.ChangePath(card.ChangeId), Ct))!;
            Assert.Null(page.ArchivedAt);
            Assert.Equal(
                ["design.md", "devlog/20261108-1200-start.md", "proposal.md", "specs/team-auth/spec.md", "tasks.md"],
                page.Documents.Select(d => d.Path).Order(StringComparer.Ordinal));
            var proposal = (await browser.GetFromJsonAsync<RenderedDocument>(ChangeEndpoints.DocumentPath(card.ChangeId, "proposal.md"), Ct))!;
            Assert.Contains("Why we sign in.", proposal.Html, StringComparison.Ordinal);

            // 7. Archive the folder as /opsx:archive would: the running bridge's watcher reports it, and the change
            //    moves from the dashboard to the history.
            var archiveFolder = $"2026-11-08-{Change}";
            Directory.Move(
                Path.Combine(repo.Root, "openspec", "changes", Change),
                Path.Combine(repo.CreateSubdirectory(Path.Combine("openspec", "changes", "archive")), archiveFolder));
            var archived = await WaitForAsync(
                async () => (await browser.GetFromJsonAsync<HistoryPage>(ChangeEndpoints.HistoryPath, Ct))!.Changes.SingleOrDefault(),
                "the archived change in the history");
            Assert.Equal((card.ChangeId, Change, archiveFolder), (archived.ChangeId, archived.Change, archived.ArchiveFolder));
            Assert.Equal("E2E", archived.Project?.Code);
            Assert.Empty((await browser.GetFromJsonAsync<List<ChangeCard>>(DashboardEndpoints.ChangesPath, Ct))!);
            Assert.NotNull((await browser.GetFromJsonAsync<ChangePage>(ChangeEndpoints.ChangePath(card.ChangeId), Ct))!.ArchivedAt);

            // 8. The agent reporting the name again is told it's archived, and nothing changes.
            Assert.Equal($"Overall 1/2 · section 1 First 1/2 · archived as {archiveFolder}", await bridge.ReportAsync());
        }
        catch
        {
            // A failure inside the container is only visible in its log.
            TestContext.Current.TestOutputHelper?.WriteLine($"Server log:\n{await ServerLogsAsync()}");
            throw;
        }
    }

    private IContainer ServerContainer() => ServerBase()
        .WithPortBinding(_hostPort, 8080)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request.ForPort(8080).ForPath("/healthz")))
        .Build();

    private ContainerBuilder ServerBase() => new ContainerBuilder(_image!)
        .WithNetwork(_network)
        .WithEnvironment(new Dictionary<string, string>
        {
            // Development with no Resend key: emails are logged, and the session cookie works over plain http.
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Urls"] = "http://+:8080",
            ["ConnectionStrings__Panopticon"] = $"Host=postgres;Port=5432;Database={DbName};Username={DbUser};Password={DbPassword}",
            ["Panopticon__PublicUrl"] = PublicUrl.ToString(),
            ["Panopticon__InitialMembers"] = MemberEmail,
            ["Panopticon__S3__ServiceUrl"] = $"http://s3:{S3Port}",
            ["Panopticon__S3__AccessKey"] = S3Fixture.AccessKey,
            ["Panopticon__S3__SecretKey"] = S3Fixture.SecretKey,
            ["Panopticon__S3__Bucket"] = "panopticon-documents",
            ["Panopticon__S3__ForcePathStyle"] = "true",
        });

    // The image's own pre-deploy step (Dockerfile: "Run once with --migrate"), then it exits.
    private async Task MigrateAsync()
    {
        await using var migrate = ServerBase().WithCommand("--migrate").Build();
        await migrate.StartAsync(Ct);
        var exitCode = await migrate.GetExitCodeAsync(Ct);
        var (stdout, stderr) = await migrate.GetLogsAsync(ct: Ct);
        Assert.True(exitCode == 0, $"--migrate exited {exitCode}.\n{stdout}\n{stderr}");
    }

    private async Task CreateBucketAsync()
    {
        var options = new S3Options
        {
            ServiceUrl = $"http://{_s3!.Hostname}:{_s3.GetMappedPublicPort(S3Port)}",
            AccessKey = S3Fixture.AccessKey,
            SecretKey = S3Fixture.SecretKey,
            Bucket = "panopticon-documents",
            ForcePathStyle = true,
        };
        using var client = options.CreateClient();
        await client.PutBucketAsync(options.Bucket, Ct);
    }

    private sealed record Run(int ExitCode, string Stdout, string Stderr);

    /// <summary>Runs the published <c>panopticon login</c>, and approves the code it prints from <paramref name="browser"/>.</summary>
    private async Task<Run> LoginAsync(HttpClient browser)
    {
        var startInfo = new ProcessStartInfo(PublishedCli.Path)
        {
            ArgumentList = { "login", "--server", PublicUrl.ToString() },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        IsolateConfig(startInfo.Environment);

        using var process = Process.Start(startInfo)!;
        var stdout = new StringBuilder();
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        var reading = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(Ct) is { } line)
            {
                lock (stdout)
                {
                    stdout.AppendLine(line);
                }
            }
        }, Ct);

        try
        {
            var code = await WaitForAsync(() =>
            {
                lock (stdout)
                {
                    return Task.FromResult(UserCode().Match(stdout.ToString()) is { Success: true } m ? m.Value : null);
                }
            }, "the code from panopticon login");
            (await browser.PostApiAsync($"{DeviceEndpoints.LoginPath(code)}/approve", null, Ct)).EnsureSuccessStatusCode().Dispose();

            // The CLI polls every five seconds.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(_stepTimeout);
            await process.WaitForExitAsync(timeout.Token);
            await reading;
            return new Run(process.ExitCode, stdout.ToString(), await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }

    /// <summary>A running <c>panopticon mcp</c>, kept up so its archive watcher runs between reports.</summary>
    private sealed class BridgeSession(McpClient client, ConcurrentQueue<string> stderr) : IAsyncDisposable
    {
        public async Task<string> ReportAsync()
        {
            var result = await client.CallToolAsync(
                "report_progress", new Dictionary<string, object?> { ["change"] = Change }, cancellationToken: Ct);
            var text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            Assert.True(result.IsError is not true, $"{text}\nstderr:\n{string.Join('\n', stderr)}");
            return text;
        }

        /// <summary>Calls <c>list_projects</c>, and returns its structured result.</summary>
        public async Task<JsonElement> ListProjectsAsync()
        {
            var result = await client.CallToolAsync("list_projects", cancellationToken: Ct);
            var text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            Assert.True(result.IsError is not true && result.StructuredContent is not null, $"{text}\nstderr:\n{string.Join('\n', stderr)}");
            return result.StructuredContent!.Value;
        }

        public ValueTask DisposeAsync() => client.DisposeAsync();
    }

    private async Task<BridgeSession> BridgeAsync(string repoRoot)
    {
        var stderr = new ConcurrentQueue<string>();
        var environment = new Dictionary<string, string?>
        {
            // Unset, so the bridge must use the login just saved.
            [Bridge.BridgeSettings.ServerUrlVariable] = null,
            [Bridge.BridgeSettings.TokenVariable] = null,
        };
        IsolateConfig(environment);

        var client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = PublishedCli.Path,
                Arguments = ["mcp"],
                WorkingDirectory = repoRoot,
                EnvironmentVariables = environment,
                StandardErrorLines = stderr.Enqueue,
            }),
            new McpClientOptions { ClientInfo = new Implementation { Name = "e2e-agent", Version = "1.0.0" } },
            cancellationToken: Ct);
        return new BridgeSession(client, stderr);
    }

    // The CLI reads and writes a saved login: point it at this test's directory, never the real one.
    private void IsolateConfig(IDictionary<string, string?> environment)
    {
        environment["XDG_CONFIG_HOME"] = _configHome;
        environment["APPDATA"] = _configHome;
    }

    private async Task<string> ServerLogsAsync()
    {
        if (_server is null)
        {
            return "(the server container didn't start)";
        }

        var (stdout, stderr) = await _server.GetLogsAsync(ct: CancellationToken.None);
        return stdout + stderr;
    }

    private static async Task<T> WaitForAsync<T>(Func<Task<T?>> probe, string what)
        where T : class
    {
        var deadline = DateTime.UtcNow + _stepTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await probe() is { } found)
            {
                return found;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), Ct);
        }

        throw new TimeoutException($"Waited {_stepTimeout.TotalSeconds:0} s for {what}.");
    }

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
