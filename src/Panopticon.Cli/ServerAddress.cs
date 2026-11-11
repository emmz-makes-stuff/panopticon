using System.Text.Json;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;

namespace Panopticon.Cli;

/// <summary>
/// Which server a command talks to, shared by <c>login</c> and <c>update</c>: <c>--server</c>, then
/// <c>PANOPTICON_SERVER_URL</c>, then the server saved by the last login.
/// </summary>
internal static class ServerAddress
{
    public static string? Resolve(string? argument, Func<string, string?> getEnvironmentVariable, CliConfigFile config, TextWriter stderr)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            return argument;
        }

        if (getEnvironmentVariable(BridgeSettings.ServerUrlVariable) is { } fromEnvironment && !string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        return config.Read(stderr)?.ServerUrl is { } saved && !string.IsNullOrWhiteSpace(saved) ? saved : null;
    }

    /// <summary>The server's base URL with a trailing slash, so relative paths resolve under it.</summary>
    public static bool TryRoot(string server, out Uri root)
    {
        root = null!;
        if (!Uri.TryCreate(server.Trim(), UriKind.Absolute, out var serverUri)
            || (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        root = new UriBuilder(serverUri) { Path = serverUri.AbsolutePath.TrimEnd('/') + "/", Query = string.Empty, Fragment = string.Empty }.Uri;
        return true;
    }

    /// <summary>A refusal's Problem Details <c>detail</c>, or its status when there's none.</summary>
    public static async Task<string> ProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String
                && detail.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
        }

        return $"{(int)response.StatusCode} {response.StatusCode}.";
    }
}
