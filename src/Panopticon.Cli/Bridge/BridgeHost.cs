using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace Panopticon.Cli.Bridge;

/// <summary>Builds the <c>panopticon mcp</c> host: an MCP server whose only output on its transport is protocol.</summary>
internal static class BridgeHost
{
    public static string Version { get; } =
        typeof(BridgeHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>Tool parameters bind through source-generated metadata (Native AOT), with the SDK's own types.</summary>
    private static readonly JsonSerializerOptions _toolJson = CreateToolJson();

    /// <param name="settings">The configuration.</param>
    /// <param name="createHttpClient">Makes the HTTP client for each report to the server.</param>
    /// <param name="useTransport">Adds the MCP transport: stdio in production, streams in tests.</param>
    /// <param name="logOutput">Where logs go: stderr in production. Never stdout.</param>
    public static IHost Build(
        BridgeSettings settings,
        Func<HttpClient> createHttpClient,
        Action<IMcpServerBuilder> useTransport,
        TextWriter logOutput)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new TextWriterLoggerProvider(logOutput));
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("Panopticon", LogLevel.Information);

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(new ServerForwarder(createHttpClient));
        builder.Services.AddSingleton<ReportedChanges>();
        builder.Services.AddSingleton<ProgressReporter>();
        builder.Services.AddHostedService<ArchiveWatcher>();

        var mcp = builder.Services
            .AddMcpServer(options => options.ServerInfo = new() { Name = "panopticon", Version = Version })
            .WithTools<BridgeTools>(_toolJson);
        useTransport(mcp);

        return builder.Build();
    }

    private static JsonSerializerOptions CreateToolJson()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                BridgeJsonContext.Default,
                McpJsonUtilities.DefaultOptions.TypeInfoResolver),
        };
        options.MakeReadOnly();
        return options;
    }
}

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int?))]
internal sealed partial class BridgeJsonContext : JsonSerializerContext;
