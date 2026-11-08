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
internal sealed class ServerForwarder(Func<HttpClient> createHttpClient)
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <param name="endpoint">The server's MCP endpoint, from <see cref="BridgeSettings.RequireServer"/>.</param>
    /// <param name="token">The bearer token, from the same call.</param>
    /// <param name="report">The report to send.</param>
    /// <param name="tokenFromEnvironment">Whether the token came from <c>PANOPTICON_TOKEN</c>, for a rejected token's error.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ReportProgressResult> ForwardAsync(
        Uri endpoint,
        string token,
        ReportProgressArguments report,
        bool tokenFromEnvironment,
        CancellationToken cancellationToken)
    {
        var server = BridgeSettings.Display(endpoint);

        var http = createHttpClient();
        http.Timeout = RequestTimeout;

        // One short-lived client per report: the server endpoint is stateless, and this survives server restarts.
        // The client only disposes the session it connects, so the transport (and the HTTP client it owns) is disposed
        // here, including when connecting fails.
        await using var transport = new HttpClientTransport(
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
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && Describe(exception, server, tokenFromEnvironment) is { } message)
        {
            throw new BridgeException(message, exception);
        }

        if (result.IsError == true)
        {
            throw new BridgeException($"The Panopticon server refused the report: {Text(result)}");
        }

        if (result.StructuredContent is not { } structured || Deserialize(structured, server) is not { } progress)
        {
            throw new BridgeException($"The Panopticon server at {server} returned no progress result.");
        }

        return progress;
    }

    private static ReportProgressResult? Deserialize(JsonElement structured, string server)
    {
        ReportProgressResult? progress;
        try
        {
            progress = structured.Deserialize(ContractsJsonContext.Default.ReportProgressResult);
        }
        catch (JsonException exception)
        {
            throw Malformed(server, exception.Message, exception);
        }

        // Records bind missing members as null; a result without its counts is as unusable as one that won't parse.
        if (progress is not null
            && (progress.ChangeId is null
                || progress.Overall is null
                || progress.CurrentSection is { Title: null } or { Progress: null }))
        {
            throw Malformed(server, "a required member is missing.", null);
        }

        return progress;
    }

    private static BridgeException Malformed(string server, string reason, Exception? inner) =>
        new($"The Panopticon server at {server} returned a malformed progress result: {reason}", inner);

    // Maps a transport or protocol failure to a message for the agent; null lets anything unexpected propagate.
    private static string? Describe(Exception exception, string server, bool tokenFromEnvironment)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                // A set PANOPTICON_TOKEN overrides the saved login, so 'panopticon login' alone can't fix it: say so.
                case HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } http
                    when tokenFromEnvironment:
                    return $"The Panopticon server at {server} rejected the token from {BridgeSettings.TokenVariable} "
                        + $"({(int)http.StatusCode!}). {BridgeSettings.TokenVariable} overrides the saved login: correct it, "
                        + "or remove it from the agent's MCP server environment to use the token from 'panopticon login'.";
                case HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } http:
                    return $"The Panopticon server at {server} rejected the saved login's token ({(int)http.StatusCode!}): "
                        + "it was revoked, or its member was removed. Run 'panopticon login' to get a new token.";
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
