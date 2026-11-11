using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Panopticon.Cli.Bridge;
using Panopticon.Contracts;

namespace Panopticon.Cli.Hook;

/// <summary>
/// The CLI's client for the hook endpoints under <c>/api/agent/</c> (agent-interaction D3): plain JSON with the
/// reporting token. Every failure is a <see cref="BridgeException"/> naming the server and what failed, and never the
/// token. Each call is bounded by the caller's token; the <see cref="HttpClient"/> it's given owns any timeout.
/// </summary>
internal sealed class AgentApiClient(HttpClient http, Uri baseUrl, string token)
{
    // The server's guard against cross-site writes: every request that isn't a GET carries it.
    private const string RequestHeader = "X-Panopticon-Request";

    private string Server => BridgeSettings.Display(baseUrl);

    /// <summary>Sends a session's new state (204).</summary>
    public async Task SendEventAsync(SessionEventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var response = await SendAsync(
            HttpMethod.Post, AgentApiPaths.SessionEvents, request, ContractsJsonContext.Default.SessionEventRequest, "the session event", cancellationToken);
    }

    /// <summary>Posts a session's prompt; the server says whether it's held (D3, D6).</summary>
    public async Task<PromptCreated> AskAsync(PromptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var response = await SendAsync(
            HttpMethod.Post, AgentApiPaths.Prompts, request, ContractsJsonContext.Default.PromptRequest, "the prompt", cancellationToken);
        return await ReadAsync(response, ContractsJsonContext.Default.PromptCreated, "prompt", cancellationToken);
    }

    /// <summary>Uploads a held permission prompt's input preview, already cut to 2 KB (204).</summary>
    public async Task SetPreviewAsync(Guid promptId, string preview, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Put, AgentApiPaths.Preview(promptId), new PromptPreview(preview), ContractsJsonContext.Default.PromptPreview, "the prompt's preview", cancellationToken);
    }

    /// <summary>One long-poll on a held prompt: where it stands, or null once the server doesn't know it (404).</summary>
    public async Task<PromptWaitResult?> WaitAsync(Guid promptId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync<object>(HttpMethod.Get, AgentApiPaths.Wait(promptId), null, null, "the wait for an answer", cancellationToken);
            return await ReadAsync(response, ContractsJsonContext.Default.PromptWaitResult, "wait result", cancellationToken);
        }
        catch (BridgeException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>The member's away switch (D8), for <c>panopticon away</c>.</summary>
    public async Task<AwayState> GetAwayAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync<object>(HttpMethod.Get, AgentApiPaths.Away, null, null, "the away state", cancellationToken);
        return await ReadAsync(response, ContractsJsonContext.Default.AwayState, "away state", cancellationToken);
    }

    /// <summary>Turns the member's away switch on or off, and gives the state that results.</summary>
    public async Task<AwayState> SetAwayAsync(bool away, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Put, AgentApiPaths.Away, new AwayState(away), ContractsJsonContext.Default.AwayState, "the away switch", cancellationToken);
        return await ReadAsync(response, ContractsJsonContext.Default.AwayState, "away state", cancellationToken);
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, string what, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(type, cancellationToken)
                ?? throw new BridgeException($"The Panopticon server at {Server} returned no {what}.");
        }
        catch (JsonException exception)
        {
            throw new BridgeException($"The Panopticon server at {Server} returned a malformed {what}: {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            // SendAsync buffers the body, so a dropped connection normally surfaces there; this keeps the promise that
            // every failure is a BridgeException if the body is ever read as a stream.
            throw new BridgeException($"The connection to the Panopticon server at {Server} dropped while reading the {what}: {exception.Message}", exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync<TBody>(
        HttpMethod method,
        string path,
        TBody? body,
        JsonTypeInfo<TBody>? bodyType,
        string what,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (method != HttpMethod.Get)
        {
            request.Headers.Add(RequestHeader, "1");
        }

        if (body is not null && bodyType is not null)
        {
            request.Content = JsonContent.Create(body, bodyType);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new BridgeException($"The Panopticon server at {Server} couldn't be reached for {what}: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BridgeException($"The Panopticon server at {Server} didn't answer {what} in time.", exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw new BridgeException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    $"The Panopticon server at {Server} refused {what} ({(int)response.StatusCode}): run 'panopticon login' again, or check {BridgeSettings.TokenVariable}.",
                _ => $"The Panopticon server at {Server} refused {what} ({(int)response.StatusCode}){await ProblemAsync(response, cancellationToken)}.",
            })
            {
                StatusCode = response.StatusCode,
            };
        }
    }

    /// <summary>A problem response's detail, as ": detail", or nothing.</summary>
    private static async Task<string> ProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String
                    ? $": {detail.GetString()}"
                    : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
