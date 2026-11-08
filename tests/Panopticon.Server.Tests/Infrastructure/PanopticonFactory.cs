using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
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
    string? bootstrapToken = PanopticonFactory.Token) : WebApplicationFactory<Program>
{
    public const string Token = "test-bootstrap-token";

    /// <summary>The member every test server starts with (Panopticon:InitialMembers), unless Settings say otherwise.</summary>
    public const string MemberEmail = "member@example.com";

    /// <summary>The public URL test servers build links on.</summary>
    public const string PublicUrl = "http://panopticon.test/";

    public string ConnectionString { get; } = connectionString;

    /// <summary>A web root to serve instead of the project's wwwroot (which exists only after a web build).</summary>
    public string? WebRoot { get; init; }

    /// <summary>
    /// The host environment. Development until the per-user token work (team-auth 6.3) moves the tests off the
    /// bootstrap token, which only Development accepts.
    /// </summary>
    public string Environment { get; init; } = "Development";

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
        string? bootstrapToken = Token) =>
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

    /// <summary>An MCP client for <c>/mcp</c>, sending the given bearer token (the valid one by default).</summary>
    public async Task<McpClient> CreateMcpClientAsync(string? token = Token, CancellationToken cancellationToken = default)
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
