using System.Net;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Panopticon.Contracts;

namespace Panopticon.Cli.Bridge;

/// <summary>
/// Sends a report to the server's online MCP endpoint (design D1) as an MCP client, with the bearer token, and turns
/// every failure into a <see cref="BridgeException"/> that never echoes the token.
/// </summary>
internal sealed class ServerForwarder(BridgeSettings settings, Func<HttpClient> createHttpClient)
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public async Task<ReportProgressResult> ForwardAsync(ReportProgressArguments report, CancellationToken cancellationToken)
    {
        var (endpoint, token) = settings.RequireServer();
        var server = BridgeSettings.Display(endpoint);

        var http = createHttpClient();
        http.Timeout = RequestTimeout;

        // One short-lived client per report: the server endpoint is stateless, and this survives server restarts.
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
                Name = "panopticon-server",
            },
            http,
            ownsHttpClient: true);

        CallToolResult result;
        try
        {
            await using var client = await McpClient.CreateAsync(
                transport,
                new McpClientOptions { ClientInfo = new Implementation { Name = "panopticon-bridge", Version = BridgeHost.Version } },
                cancellationToken: cancellationToken);

            result = await client.CallToolAsync(
                ContractsAssembly.ReportProgressToolName,
                report.ToToolArguments(),
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && Describe(exception, server) is { } message)
        {
            throw new BridgeException(message, exception);
        }

        if (result.IsError == true)
        {
            throw new BridgeException($"The Panopticon server refused the report: {Text(result)}");
        }

        if (result.StructuredContent is not { } structured
            || structured.Deserialize(ContractsJsonContext.Default.ReportProgressResult) is not { } progress)
        {
            throw new BridgeException($"The Panopticon server at {server} returned no progress result.");
        }

        return progress;
    }

    // Maps a transport or protocol failure to a message for the agent; null lets anything unexpected propagate.
    private static string? Describe(Exception exception, string server)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } http:
                    return $"The Panopticon server at {server} rejected the token ({(int)http.StatusCode!}). "
                        + $"Check {BridgeSettings.TokenVariable}.";
                case HttpRequestException { StatusCode: { } status }:
                    return $"The Panopticon server at {server} answered {(int)status} {status}.";
                case HttpRequestException http:
                    return $"Couldn't reach the Panopticon server at {server}: {http.Message}";
                case TaskCanceledException or TimeoutException:
                    return $"The Panopticon server at {server} didn't answer within {RequestTimeout.TotalSeconds:0} seconds.";
            }
        }

        return exception is McpException ? $"Talking to the Panopticon server at {server} failed: {exception.Message}" : null;
    }

    private static string Text(CallToolResult result) =>
        string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text)) is { Length: > 0 } text
            ? text
            : "(no message)";
}
