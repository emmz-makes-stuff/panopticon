using System.Net.Http.Json;
using System.Text.Json;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;
using Panopticon.Contracts;

namespace Panopticon.Cli.Login;

/// <summary>
/// <c>panopticon login [--server &lt;url&gt;]</c> (spec reporter-credentials; design D15). It starts a device login,
/// prints the code and the approval URL (a VM has no browser to open), polls until a member approves or denies it, and
/// saves the server and token for the bridge.
/// </summary>
internal sealed class LoginCommand(
    Func<HttpClient> createHttpClient,
    Func<string, string?> getEnvironmentVariable,
    CliConfigFile config,
    string machineName,
    Func<TimeSpan, CancellationToken, Task> delay,
    TextWriter stdout,
    TextWriter stderr)
{
    public const int Cancelled = 130;
    private const string RequestHeader = "X-Panopticon-Request";

    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!ServerAddress.TryParseArguments("login", args, out var serverArgument, out var usageError))
        {
            await stderr.WriteLineAsync(usageError);
            return 1;
        }

        var server = ServerAddress.Resolve(serverArgument, getEnvironmentVariable, config, stderr);
        if (server is null)
        {
            await stderr.WriteLineAsync(
                "No server to log in to: pass --server <url>, for example 'panopticon login --server https://panopticon.example.com'.");
            return 1;
        }

        if (!ServerAddress.TryRoot(server, out var root))
        {
            await stderr.WriteLineAsync($"The server must be an absolute http or https URL, not '{server}'.");
            return 1;
        }

        var display = BridgeSettings.Display(root);
        using var http = createHttpClient();
        try
        {
            return await LoginAsync(http, root, display, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await stderr.WriteLineAsync("Login cancelled.");
            return Cancelled;
        }
        catch (HttpRequestException ex)
        {
            await stderr.WriteLineAsync($"Couldn't reach the Panopticon server at {display}: {ex.Message}");
            return 1;
        }
        catch (JsonException)
        {
            await stderr.WriteLineAsync($"The server at {display} didn't answer like a Panopticon server. Check the URL.");
            return 1;
        }
    }

    private async Task<int> LoginAsync(HttpClient http, Uri root, string display, CancellationToken cancellationToken)
    {
        DeviceStartResponse start;
        using (var response = await PostAsync(http, new Uri(root, "api/device/start"), new DeviceStartRequest(machineName), ContractsJsonContext.Default.DeviceStartRequest, cancellationToken))
        {
            if (!response.IsSuccessStatusCode)
            {
                await stderr.WriteLineAsync($"The server at {display} refused to start a login: {await ServerAddress.ProblemAsync(response, cancellationToken)}");
                return 1;
            }

            start = await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DeviceStartResponse, cancellationToken)
                ?? throw new JsonException();
        }

        await stdout.WriteLineAsync($"To log in {machineName}, open this URL in a browser where you're signed in to Panopticon:");
        await stdout.WriteLineAsync();
        await stdout.WriteLineAsync($"  {start.ApproveUrl}");
        await stdout.WriteLineAsync();
        await stdout.WriteLineAsync($"and check that it shows the code {start.UserCode}. Waiting for approval...");

        var interval = TimeSpan.FromSeconds(Math.Max(1, start.IntervalSeconds));
        var lifetime = TimeSpan.FromSeconds(Math.Max(1, start.ExpiresInSeconds));
        var waited = TimeSpan.Zero;
        var lostContact = false;
        var pollUri = new Uri(root, "api/device/poll");
        while (true)
        {
            await delay(interval, cancellationToken);
            waited += interval;

            HttpResponseMessage pollResponse;
            try
            {
                pollResponse = await PostAsync(http, pollUri, new DevicePollRequest(start.DeviceSecret), ContractsJsonContext.Default.DevicePollRequest, cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken))
            {
                // A dropped connection or a timeout mid-login: keep trying until the code would have expired anyway.
                if (waited >= lifetime)
                {
                    await stderr.WriteLineAsync($"Lost contact with the server at {display}, and the code has expired. Run 'panopticon login' again.");
                    return 1;
                }

                if (!lostContact)
                {
                    lostContact = true;
                    await stderr.WriteLineAsync($"Lost contact with the server at {display}. Still trying...");
                }

                continue;
            }

            using var response = pollResponse;
            if (!response.IsSuccessStatusCode)
            {
                await stderr.WriteLineAsync($"The server at {display} refused the login: {await ServerAddress.ProblemAsync(response, cancellationToken)}");
                return 1;
            }

            var poll = await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DevicePollResponse, cancellationToken)
                ?? throw new JsonException();
            switch (poll.Status)
            {
                case DevicePollStatus.Pending:
                    continue;
                case DevicePollStatus.SlowDown:
                    interval *= 2;
                    continue;
                case DevicePollStatus.Denied:
                    await stderr.WriteLineAsync("The login was denied. No token was issued.");
                    return 1;
                case DevicePollStatus.Expired:
                    await stderr.WriteLineAsync("The code expired before anyone approved it. Run 'panopticon login' again.");
                    return 1;
                case DevicePollStatus.Approved when poll.Token is { Length: > 0 } token:
                    try
                    {
                        config.Update(saved => saved with { ServerUrl = root.ToString(), Token = token }, startOverIfUnreadable: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // The server hands a token over once, so it's lost now: say how to clean it up.
                        await stderr.WriteLineAsync(
                            $"The login was approved, but saving it to {config.Path} failed: {ex.Message} "
                            + $"Revoke the new token for {machineName} on the Tokens page, fix the problem, and run 'panopticon login' again.");
                        return 1;
                    }

                    var who = poll.Member is { } member ? $"{member.DisplayName} ({member.Email})" : "your member";
                    await stdout.WriteLineAsync($"Logged in to {display} as {who}. Saved to {config.Path}.");
                    return 0;
                default:
                    await stderr.WriteLineAsync($"The server at {display} gave an unexpected answer ('{poll.Status}'). Run 'panopticon login' again.");
                    return 1;
            }
        }
    }

    /// <summary>A network failure or an HTTP timeout, not the user cancelling.</summary>
    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static async Task<HttpResponseMessage> PostAsync<T>(
        HttpClient http,
        Uri uri,
        T body,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body, typeInfo) };
        request.Headers.Add(RequestHeader, "1");
        return await http.SendAsync(request, cancellationToken);
    }
}
