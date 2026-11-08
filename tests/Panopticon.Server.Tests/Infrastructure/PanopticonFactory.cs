using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    public string ConnectionString { get; } = connectionString;

    /// <summary>A web root to serve instead of the project's wwwroot (which exists only after a web build).</summary>
    public string? WebRoot { get; init; }

    public static async Task<PanopticonFactory> CreateAsync(
        PostgresFixture postgres,
        S3Fixture s3,
        CancellationToken cancellationToken,
        string? bootstrapToken = Token) =>
        new(await postgres.CreateDatabaseAsync(cancellationToken), await s3.CreateBucketAsync(cancellationToken), bootstrapToken);

    public PanopticonDb NewDb() => PostgresFixture.CreateDb(ConnectionString);

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
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{ServerSetup.ConnectionStringName}", ConnectionString);
        builder.UseSetting($"{S3Options.Section}:ServiceUrl", s3.ServiceUrl);
        builder.UseSetting($"{S3Options.Section}:AccessKey", s3.AccessKey);
        builder.UseSetting($"{S3Options.Section}:SecretKey", s3.SecretKey);
        builder.UseSetting($"{S3Options.Section}:Bucket", s3.Bucket);
        builder.UseSetting($"{S3Options.Section}:ForcePathStyle", "true");
        builder.UseSetting(BootstrapTokenOptions.ConfigKey, bootstrapToken ?? string.Empty);
        if (WebRoot is not null)
        {
            builder.UseWebRoot(WebRoot);
        }
    }
}
