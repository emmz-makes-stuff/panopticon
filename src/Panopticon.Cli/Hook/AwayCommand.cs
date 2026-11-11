using System.Globalization;
using Panopticon.Cli.Bridge;
using Panopticon.Contracts;

namespace Panopticon.Cli.Hook;

/// <summary>
/// <c>panopticon away [on|off]</c> (agent-interaction D8; spec remote-answers, Away mode): shows or sets the member's
/// away switch, with the same server and token as reports. While it's on, prompts from the member's sessions wait for
/// an answer on the dashboard.
/// </summary>
internal sealed class AwayCommand(Func<HttpClient> createHttpClient, BridgeSettings settings, TextWriter stdout, TextWriter stderr)
{
    private const string Usage = "Usage: panopticon away [on | off]";

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        bool? turnOn;
        switch (args)
        {
            case []:
                turnOn = null;
                break;
            case ["on"]:
                turnOn = true;
                break;
            case ["off"]:
                turnOn = false;
                break;
            default:
                await stderr.WriteLineAsync(Usage);
                return 1;
        }

        try
        {
            var (baseUrl, token) = settings.RequireApi();
            using var http = createHttpClient();
            http.Timeout = RequestTimeout;
            var client = new AgentApiClient(http, baseUrl, token);
            var state = turnOn is { } on
                ? await client.SetAwayAsync(on, cancellationToken)
                : await client.GetAwayAsync(cancellationToken);
            await stdout.WriteLineAsync(Describe(state));
            return 0;
        }
        catch (BridgeException exception)
        {
            await stderr.WriteLineAsync($"panopticon: {exception.Message}");
            return 1;
        }
    }

    internal static string Describe(AwayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Away)
        {
            return "Not away: your sessions' prompts are answered in the terminal.";
        }

        var since = state.Since is { } at ? $" since {at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}" : "";
        return $"Away{since}: your sessions' questions and permission prompts wait for an answer on the dashboard.";
    }
}
