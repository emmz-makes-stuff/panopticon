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
        // The shared bootstrap token is a Development convenience only (team-auth D5): anywhere else it must not be set.
        var isDevelopment = builder.Environment.IsDevelopment();
        services.AddOptions<BootstrapTokenOptions>()
            .Configure(o => o.Token = configuration[BootstrapTokenOptions.ConfigKey])
            .Validate(
                o => isDevelopment || string.IsNullOrWhiteSpace(o.Token),
                $"{BootstrapTokenOptions.ConfigKey} is only allowed in the Development environment. Remove it, and use per-user API tokens from `panopticon login`.")
            .ValidateOnStart();

        services.AddOptions<PublicUrlOptions>()
            .Configure(o => o.Value = configuration[PublicUrlOptions.ConfigKey])
            .Validate(o => o.Value is null || o.TryGetUri(out _), $"{PublicUrlOptions.ConfigKey} must be an absolute http or https URL.")
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

        // Members (team-auth): the first ones come from configuration.
        services.AddHostedService<InitialMembers>();

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
        // The built web UI (design D10: Vite builds into wwwroot).
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/healthz", () => TypedResults.Ok("ok"));
        app.MapMcp(McpPath).RequireAuthorization();
        app.MapDashboard();

        // Client-side routes fall back to the app shell; unknown API paths stay 404. `nonfile` keeps paths with a file
        // extension (the built assets) out of the fallback, so UseStaticFiles serves them: an endpoint matched here would
        // make the static-files middleware stand aside. Only the `api` and `mcp` segments themselves are excluded (bare or
        // with a sub-path), so `/api` is a 404 but a client route like `/mcp-setup` still gets the shell.
        app.MapFallbackToFile("{*path:nonfile:regex(^(?!(api|mcp)(/|$)).*$)}", "index.html");

        return app;
    }

    /// <summary>Applies any pending EF Core migrations.</summary>
    public static async Task MigrateAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PanopticonDb>().Database.MigrateAsync();
    }

    private sealed class DatabaseOptions
    {
        public string? ConnectionString { get; set; }
    }
}
