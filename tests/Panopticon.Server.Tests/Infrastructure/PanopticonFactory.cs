using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Documents;

namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>
/// The real server in process, against its own Postgres database and S3 bucket.
/// Settings go through <c>UseSetting</c> so start-up validation sees them.
/// </summary>
public sealed class PanopticonFactory(
    string connectionString,
    S3Options s3,
    string? bootstrapToken = null) : WebApplicationFactory<Program>
{
    private readonly SemaphoreSlim _memberTokenLock = new(1, 1);
    private string? _memberToken;

    /// <summary>A bootstrap token, for the tests of how Development accepts it (and how elsewhere refuses it).</summary>
    public const string Token = "test-bootstrap-token";

    /// <summary>The member every test server starts with (Panopticon:InitialMembers), unless Settings say otherwise.</summary>
    public const string MemberEmail = "member@example.com";

    /// <summary>The public URL test servers build links on.</summary>
    public const string PublicUrl = "http://panopticon.test/";

    public string ConnectionString { get; } = connectionString;

    /// <summary>A web root to serve instead of the project's wwwroot (which exists only after a web build).</summary>
    public string? WebRoot { get; init; }

    /// <summary>
    /// The host environment: <c>Testing</c>, which counts as "outside Development", so reports need a member's API
    /// token. Tests of Development-only behaviour set it.
    /// </summary>
    public string Environment { get; init; } = "Testing";

    /// <summary>Every email the server sends, when <see cref="CaptureEmail"/> is on (the default).</summary>
    public CapturingEmailSender Emails { get; } = new();

    /// <summary>A clock to use instead of the system's, e.g. a <see cref="ManualClock"/>.</summary>
    public TimeProvider? Clock { get; init; }

    /// <summary>Everything the server logs.</summary>
    public LogCapture Logs { get; } = new();

    /// <summary>Replace the real email sender with <see cref="Emails"/>.</summary>
    public bool CaptureEmail { get; init; } = true;

    /// <summary>Extra configuration settings, applied last.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    public static async Task<PanopticonFactory> CreateAsync(
        PostgresFixture postgres,
        S3Fixture s3,
        CancellationToken cancellationToken,
        string? bootstrapToken = null) =>
        new(await postgres.CreateDatabaseAsync(cancellationToken), await s3.CreateBucketAsync(cancellationToken), bootstrapToken);

    public PanopticonDb NewDb() => PostgresFixture.CreateDb(ConnectionString);

    /// <summary>A browser-like client (https, keeping cookies), as the dashboard is.</summary>
    public HttpClient CreateBrowserClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost/"), HandleCookies = true });

    /// <summary>A browser-like client signed in as a member, through a real sign-in link.</summary>
    public async Task<HttpClient> CreateSignedInClientAsync(string email = MemberEmail, CancellationToken cancellationToken = default)
    {
        var http = CreateBrowserClient();
        var before = Emails.To(email).Count;
        using (var requested = await http.PostApiAsync(Panopticon.Server.Auth.SignInEndpoints.SignInPath, new { email }, cancellationToken))
        {
            requested.EnsureSuccessStatusCode();
        }

        await Emails.WaitForAsync(email, before + 1, cancellationToken);
        using var confirmed = await http.PostApiAsync(
            Panopticon.Server.Auth.SignInEndpoints.ConfirmPath,
            new { secret = Emails.LatestSignInSecret(email) },
            cancellationToken);
        confirmed.EnsureSuccessStatusCode();
        return http;
    }

    /// <summary>
    /// An API token for a member (who must exist), issued the way <c>panopticon login</c> gets one: a device login
    /// started, approved in the member's signed-in browser, and polled for.
    /// </summary>
    public async Task<string> CreateApiTokenAsync(
        string email = MemberEmail,
        string machineName = "vm-1",
        CancellationToken cancellationToken = default)
    {
        using var browser = await CreateSignedInClientAsync(email, cancellationToken);
        using var cli = CreateClient();
        using var started = await cli.PostApiAsync(DeviceEndpoints.StartPath, new { machineName }, cancellationToken);
        started.EnsureSuccessStatusCode();
        var start = (await started.Content.ReadFromJsonAsync<DeviceStartResponse>(cancellationToken))!;
        using (var approved = await browser.PostApiAsync($"{DeviceEndpoints.LoginPath(start.UserCode)}/approve", null, cancellationToken))
        {
            approved.EnsureSuccessStatusCode();
        }

        using var polled = await cli.PostApiAsync(DeviceEndpoints.PollPath, new { start.DeviceSecret }, cancellationToken);
        var poll = (await polled.Content.ReadFromJsonAsync<DevicePollResponse>(cancellationToken))!;
        return poll.Token ?? throw new InvalidOperationException($"The device login wasn't approved: {poll.Status}.");
    }

    /// <summary>
    /// An API token for a member (who must exist), written straight to the database. For a server on Kestrel
    /// (<c>UseKestrel</c>), where the in-memory browser client that <see cref="CreateApiTokenAsync"/> signs in with
    /// can't reach it.
    /// </summary>
    public async Task<string> SeedApiTokenAsync(
        string email = MemberEmail,
        string machineName = "vm-1",
        CancellationToken cancellationToken = default)
    {
        var secret = Secrets.NewApiToken();
        await using var db = NewDb();
        var memberId = await db.Members
            .Where(m => m.EmailKey == Panopticon.Server.Auth.MemberEmail.Key(email))
            .Select(m => m.Id)
            .SingleAsync(cancellationToken);
        db.ApiTokens.Add(new ApiToken
        {
            MemberId = memberId,
            SecretHash = Secrets.Hash(secret),
            MachineName = machineName,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        return secret;
    }

    /// <summary>The default member's API token, issued once per test server and reused.</summary>
    public async Task<string> MemberTokenAsync(CancellationToken cancellationToken = default)
    {
        await _memberTokenLock.WaitAsync(cancellationToken);
        try
        {
            return _memberToken ??= await CreateApiTokenAsync(cancellationToken: cancellationToken);
        }
        finally
        {
            _memberTokenLock.Release();
        }
    }

    /// <summary>An MCP client for <c>/mcp</c>, reporting as the default member with their API token.</summary>
    public async Task<McpClient> CreateMcpClientAsync(CancellationToken cancellationToken = default) =>
        await CreateMcpClientAsync(await MemberTokenAsync(cancellationToken), cancellationToken);

    /// <summary>An MCP client for <c>/mcp</c>, sending the given bearer token, or none.</summary>
    public async Task<McpClient> CreateMcpClientAsync(string? token, CancellationToken cancellationToken = default)
    {
        var http = CreateClient();
        var headers = new Dictionary<string, string>();
        if (token is not null)
        {
            headers["Authorization"] = new AuthenticationHeaderValue("Bearer", token).ToString();
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, ServerSetup.McpPath),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = headers,
            },
            http,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _memberTokenLock.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.UseSetting($"ConnectionStrings:{ServerSetup.ConnectionStringName}", ConnectionString);
        builder.UseSetting($"{S3Options.Section}:ServiceUrl", s3.ServiceUrl);
        builder.UseSetting($"{S3Options.Section}:AccessKey", s3.AccessKey);
        builder.UseSetting($"{S3Options.Section}:SecretKey", s3.SecretKey);
        builder.UseSetting($"{S3Options.Section}:Bucket", s3.Bucket);
        builder.UseSetting($"{S3Options.Section}:ForcePathStyle", "true");
        builder.UseSetting(BootstrapTokenOptions.ConfigKey, bootstrapToken ?? string.Empty);
        builder.UseSetting(InitialMembers.ConfigKey, MemberEmail);
        builder.UseSetting(PublicUrlOptions.ConfigKey, PublicUrl);
        builder.UseSetting(EmailOptions.ApiKeyConfigKey, "re_test_not_a_real_key");
        builder.UseSetting(EmailOptions.FromConfigKey, "Panopticon <panopticon@example.com>");
        if (WebRoot is not null)
        {
            builder.UseWebRoot(WebRoot);
        }

        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

        if (Clock is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton(Clock));
        }

        if (CaptureEmail)
        {
            builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(Emails));
        }
    }
}
