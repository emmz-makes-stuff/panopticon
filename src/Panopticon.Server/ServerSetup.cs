using Amazon.S3;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using Panopticon.Server.Auth;
using Panopticon.Server.Changes;
using Panopticon.Server.Dashboard;
using Panopticon.Server.Data;
using Panopticon.Server.Distribution;
using Panopticon.Server.Documents;
using Panopticon.Server.Live;
using Panopticon.Server.Metrics;
using Panopticon.Server.Progress;
using Panopticon.Server.Projects;
using Panopticon.Server.Reporting;
using Resend;

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
            .Configure(o => o.Value = configuration[PublicUrlOptions.ConfigKey] is { } url && !string.IsNullOrWhiteSpace(url) ? url : null)
            .Validate(o => isDevelopment || o.Value is not null, $"{PublicUrlOptions.ConfigKey} is not configured.")
            .Validate(o => o.Value is null || o.TryGetUri(out _), $"{PublicUrlOptions.ConfigKey} must be an absolute http or https URL.")
            .ValidateOnStart();

        // Email (team-auth D9): Resend, except in Development with no key, where emails are logged instead.
        services.AddOptions<EmailOptions>()
            .Configure(o =>
            {
                o.ResendApiKey = configuration[EmailOptions.ApiKeyConfigKey];
                o.From = configuration[EmailOptions.FromConfigKey];
            })
            .Validate(o => isDevelopment || o.HasResendKey, $"{EmailOptions.ApiKeyConfigKey} is not configured.")
            .Validate(o => !o.HasResendKey || !string.IsNullOrWhiteSpace(o.From), $"{EmailOptions.FromConfigKey} is not configured.")
            .ValidateOnStart();

        var resendApiKey = configuration[EmailOptions.ApiKeyConfigKey];
        if (isDevelopment && string.IsNullOrWhiteSpace(resendApiKey))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        else
        {
            services.AddResend(o =>
            {
                o.ApiToken = resendApiKey ?? string.Empty;
                o.ThrowExceptions = true;
            });
            services.AddTransient<IEmailSender, ResendEmailSender>();
        }

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

        // When cards go idle and stale (filters-and-idle-cards D1).
        services.AddOptions<ActivityOptions>()
            .Bind(configuration.GetSection(ActivityOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ActivityOptions>, ActivityOptionsValidation>();

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
        services.AddSingleton<InitialMemberList>();

        // Sign-in: rate limits and the background email queue.
        services.AddSingleton<AuthLimits>();
        services.AddSingleton<EmailOutbox>();
        services.AddHostedService(sp => sp.GetRequiredService<EmailOutbox>());
        services.AddSingleton<AuthCleanup>();
        services.AddHostedService(sp => sp.GetRequiredService<AuthCleanup>());

        // Reporting and live updates.
        services.AddScoped<ReportRecorder>();

        // change-metrics D3: older reports get their progress filled in once, in the background.
        services.AddSingleton<ProgressBackfill>();
        services.AddHostedService(sp => sp.GetRequiredService<ProgressBackfill>());
        services.AddScoped<ChangeCards>();
        services.AddScoped<ChangePages>();
        services.AddScoped<MetricsReader>();
        services.AddSingleton<DocumentRenderer>();
        services.AddSingleton<ChangeBroadcaster>();

        // Auth (team-auth D4): no default scheme, so nothing is looked up unless an endpoint's policy asks for it.
        // Dashboard and /api: the session cookie. /mcp: a bearer token.
        services.AddScoped<SessionStore>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, SessionHandler>(SessionHandler.SchemeName, _ => { })
            .AddScheme<AuthenticationSchemeOptions, ApiTokenHandler>(ApiTokenHandler.SchemeName, _ => { });
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Member, policy => policy
                .AddAuthenticationSchemes(SessionHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(PanopticonClaims.SessionId))
            .AddPolicy(AuthPolicies.Reporter, policy => policy
                .AddAuthenticationSchemes(ApiTokenHandler.SchemeName)
                .RequireAuthenticatedUser());

        // CLI distribution (cli-install D1): the builds bundled with this server, hashed once.
        services.AddOptions<CliDistributionOptions>()
            .Configure(o =>
            {
                if (configuration[CliDistributionOptions.ConfigKey] is { } directory && !string.IsNullOrWhiteSpace(directory))
                {
                    o.Directory = directory;
                }
            });
        services.AddSingleton(sp => CliCatalogue.Load(
            sp.GetRequiredService<IOptions<CliDistributionOptions>>().Value.Directory,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<CliCatalogue>()));

        services.AddMcpServer()
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<ReportProgressTool>()
            .WithTools<ListProjectsTool>()
            .WithTools<TagChangeTool>();

        return builder;
    }

    public static WebApplication MapPanopticon(this WebApplication app)
    {
        // The built web UI (design D10: Vite builds into wwwroot).
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseMiddleware<RequestHeaderMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/healthz", () => TypedResults.Ok("ok"));
        app.MapMcp(McpPath).RequireAuthorization(AuthPolicies.Reporter);
        app.MapSignIn();
        app.MapSession();
        app.MapMembers();
        app.MapDevice();
        app.MapTokens();
        app.MapDashboard();
        app.MapChanges();
        app.MapChangeTags();
        app.MapMetrics();
        app.MapProjects();

        // The CLI builds this server hands out (cli-install). The catalogue is read now, at start-up (design D1).
        _ = app.Services.GetRequiredService<CliCatalogue>();
        app.MapCliDistribution();

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
