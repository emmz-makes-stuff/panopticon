using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Panopticon.Contracts;

namespace Panopticon.Cli.Bridge;

/// <summary>
/// Calls the server's online MCP endpoint (design D1) as an MCP client, for reports, the Project list and tag changes, with the bearer token, and turns
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
        ArgumentNullException.ThrowIfNull(report);
        var progress = await CallAsync(
            endpoint,
            token,
            new ToolCall<ReportProgressResult>(
                ContractsAssembly.ReportProgressToolName,
                report.ToToolArguments(),
                ContractsJsonContext.Default.ReportProgressResult,
                "the report",
                "progress result"),
            tokenFromEnvironment,
            cancellationToken);

        // Records bind missing members as null; a result without its counts is as unusable as one that won't parse.
        if (progress.ChangeId is null
            || progress.Overall is null
            || progress.CurrentSection is { Title: null } or { Progress: null })
        {
            throw Malformed(BridgeSettings.Display(endpoint), "progress result", "a required member is missing.", null);
        }

        return progress;
    }

    /// <summary>Lists the reporter's Projects (projects D6), with the same server, token and errors as a report.</summary>
    public async Task<ListProjectsResult> ListProjectsAsync(
        Uri endpoint,
        string token,
        bool tokenFromEnvironment,
        CancellationToken cancellationToken)
    {
        var list = await CallAsync(
            endpoint,
            token,
            new ToolCall<ListProjectsResult>(
                ContractsAssembly.ListProjectsToolName,
                new Dictionary<string, object?>(),
                ContractsJsonContext.Default.ListProjectsResult,
                "the Project list",
                "Project list"),
            tokenFromEnvironment,
            cancellationToken);

        if (list.Projects is null || list.Projects.Any(p => p is null || p.Id is null || p.Name is null || p.Code is null || p.Role is null))
        {
            throw Malformed(BridgeSettings.Display(endpoint), "Project list", "a required member is missing.", null);
        }

        return list;
    }

    /// <summary>
    /// Forwards <c>tag_change</c> (tags D8), with the same server, token and errors as a report. A refusal, such as an
    /// unknown tag, comes back with the server's own message, which lists the Project's tags.
    /// </summary>
    public async Task<TagChangeResult> TagChangeAsync(
        Uri endpoint,
        string token,
        TagChangeArguments arguments,
        bool tokenFromEnvironment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var tagged = await CallAsync(
            endpoint,
            token,
            new ToolCall<TagChangeResult>(
                ContractsAssembly.TagChangeToolName,
                arguments.ToToolArguments(),
                ContractsJsonContext.Default.TagChangeResult,
                "the tag change",
                "tag change result"),
            tokenFromEnvironment,
            cancellationToken);

        if (tagged.ChangeId is null || tagged.Change is null || tagged.Code is null || tagged.Tags is null || tagged.Tags.Any(t => t is null))
        {
            throw Malformed(BridgeSettings.Display(endpoint), "tag change result", "a required member is missing.", null);
        }

        return tagged;
    }

    /// <summary>
    /// Calls one of the server's tools and returns its structured result (projects D6). Configuration, transport and
    /// protocol failures, a refusal, and a missing or malformed result are each a <see cref="BridgeException"/>.
    /// </summary>
    public async Task<T> CallAsync<T>(
        Uri endpoint,
        string token,
        ToolCall<T> call,
        bool tokenFromEnvironment,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(call);
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

            result = await client.CallToolAsync(call.Name, call.Arguments, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && Describe(exception, server, tokenFromEnvironment) is { } message)
        {
            throw new BridgeException(message, exception);
        }

        if (result.IsError == true)
        {
            throw new BridgeException($"The Panopticon server refused {call.Action}: {Text(result)}");
        }

        T? value = null;
        if (result.StructuredContent is { } structured)
        {
            try
            {
                value = structured.Deserialize(call.Result);
            }
            catch (JsonException exception)
            {
                throw Malformed(server, call.ResultName, exception.Message, exception);
            }
        }

        return value ?? throw new BridgeException($"The Panopticon server at {server} returned no {call.ResultName}.");
    }

    private static BridgeException Malformed(string server, string resultName, string reason, Exception? inner) =>
        new($"The Panopticon server at {server} returned a malformed {resultName}: {reason}", inner);

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

/// <summary>One call to a server tool, for <see cref="ServerForwarder.CallAsync{T}"/>.</summary>
/// <param name="Name">The tool's name.</param>
/// <param name="Arguments">Its arguments, built without reflection (AOT-safe).</param>
/// <param name="Result">How to read its structured result.</param>
/// <param name="Action">What a refusal refused, e.g. "the report": "The Panopticon server refused the report: …".</param>
/// <param name="ResultName">What the result is called in errors, e.g. "progress result".</param>
internal sealed record ToolCall<T>(
    string Name,
    IReadOnlyDictionary<string, object?> Arguments,
    JsonTypeInfo<T> Result,
    string Action,
    string ResultName);
