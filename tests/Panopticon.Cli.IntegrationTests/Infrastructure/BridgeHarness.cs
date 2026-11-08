using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Panopticon.Cli.Bridge;
using Panopticon.Contracts;

namespace Panopticon.Cli.IntegrationTests.Infrastructure;

/// <summary>
/// The real bridge host, in-process, over a pair of pipes instead of stdio, with an MCP client that introduces
/// itself by a chosen client name.
/// </summary>
public sealed class BridgeHarness : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly StringWriter _log;

    private BridgeHarness(IHost host, McpClient client, StringWriter log)
    {
        _host = host;
        Client = client;
        _log = log;
    }

    public McpClient Client { get; }

    /// <summary>Everything the bridge logged (stderr in production).</summary>
    public string Log => _log.ToString();

    internal static async Task<BridgeHarness> StartAsync(
        BridgeSettings settings,
        Func<HttpClient> createHttpClient,
        string clientName,
        CancellationToken cancellationToken)
    {
        var toBridge = new Pipe();
        var toClient = new Pipe();
        var log = new StringWriter();

        var host = BridgeHost.Build(
            settings,
            createHttpClient,
            mcp => mcp.WithStreamServerTransport(toBridge.Reader.AsStream(), toClient.Writer.AsStream()),
            log);
        await host.StartAsync(cancellationToken);

        var client = await McpClient.CreateAsync(
            new StreamClientTransport(toBridge.Writer.AsStream(), toClient.Reader.AsStream()),
            new McpClientOptions { ClientInfo = new Implementation { Name = clientName, Version = "1.0.0" } },
            cancellationToken: cancellationToken);

        return new BridgeHarness(host, client, log);
    }

    public async Task<CallToolResult> ReportAsync(string change, int? section, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?> { ["change"] = change };
        if (section is not null)
        {
            arguments["section"] = section;
        }

        return await Client.CallToolAsync(ContractsAssembly.ReportProgressToolName, arguments, cancellationToken: cancellationToken);
    }

    public static string Text(CallToolResult result) =>
        string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
    }
}
