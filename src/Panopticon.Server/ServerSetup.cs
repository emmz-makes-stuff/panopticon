using Amazon.S3;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using Panopticon.Server.Auth;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Documents;
using Panopticon.Server.Live;
using Panopticon.Server.Reporting;

namespace Panopticon.Server;

/// <summary>Service registration and endpoint mapping for the Panopticon server.</summary>
public static class ServerSetup
{
    public const string ConnectionStringName = "Panopticon";
    public const string McpPath = "/mcp";

    public static WebApplicationBuilder AddPanopticon(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        // Settings, validated when the host starts so a misconfigured server fails naming the setting.
        services.AddOptions<BootstrapTokenOptions>()
            .Configure(o => o.Token = configuration[BootstrapTokenOptions.ConfigKey])
            .Validate(o => !string.IsNullOrWhiteSpace(o.Token), $"{BootstrapTokenOptions.ConfigKey} is not configured.")
            .ValidateOnStart();

        services.AddOptions<S3Options>()
            .Bind(configuration.GetSection(S3Options.Section))
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.AddOptions<DatabaseOptions>()
            .Configure(o => o.ConnectionString = configuration.GetConnectionString(ConnectionStringName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), $"ConnectionStrings:{ConnectionStringName} is not configured.")
            .ValidateOnStart();

        // Storage.
        services.AddDbContext<PanopticonDb>((sp, options) =>
            DbOptions.Configure(
                (DbContextOptionsBuilder<PanopticonDb>)options,
                sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString!));
        services.AddSingleton<IAmazonS3>(sp => sp.GetRequiredService<IOptions<S3Options>>().Value.CreateClient());
        services.AddSingleton<IDocumentStore>(sp => new S3DocumentStore(
            sp.GetRequiredService<IAmazonS3>(),
            sp.GetRequiredService<IOptions<S3Options>>().Value.Bucket!));
        services.AddSingleton(TimeProvider.System);

        // Reporting and live updates.
        services.AddScoped<ReportRecorder>();
        services.AddScoped<ChangeCards>();
        services.AddSingleton<ChangeBroadcaster>();

        // Auth: the walking skeleton's bootstrap bearer token (design D5).
        services.AddAuthentication(BootstrapTokenHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, BootstrapTokenHandler>(BootstrapTokenHandler.SchemeName, _ => { });
        services.AddAuthorization();

        services.AddMcpServer()
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<ReportProgressTool>();

        return builder;
    }

    public static WebApplication MapPanopticon(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/healthz", () => TypedResults.Ok("ok"));
        app.MapMcp(McpPath).RequireAuthorization();

        return app;
    }

    private sealed class DatabaseOptions
    {
        public string? ConnectionString { get; set; }
    }
}
