using Panopticon.Cli.Config;

namespace Panopticon.Cli.Bridge;

/// <summary>The bridge's configuration, read once at start-up (spec cli-bridge, "Configuration").</summary>
/// <param name="TokenFromEnvironment">
/// Whether <see cref="Token"/> came from <c>PANOPTICON_TOKEN</c> rather than the saved login, so a rejected token's
/// error can say which one to fix.
/// </param>
internal sealed record BridgeSettings(
    string? ServerUrl,
    string? Token,
    string WorkingDirectory,
    string MachineName,
    bool TokenFromEnvironment = false)
{
    public const string ServerUrlVariable = "PANOPTICON_SERVER_URL";
    public const string TokenVariable = "PANOPTICON_TOKEN";

    public static BridgeSettings FromEnvironment(TextWriter stderr) =>
        Resolve(Environment.GetEnvironmentVariable, CliConfigFile.ForCurrentUser(), stderr, Environment.CurrentDirectory, Environment.MachineName);

    /// <summary>
    /// Each value from its environment variable, then from the login <c>panopticon login</c> saved (design D14). The
    /// file is read only when a variable is unset, so a fully configured environment never touches it.
    /// </summary>
    public static BridgeSettings Resolve(
        Func<string, string?> getEnvironmentVariable,
        CliConfigFile config,
        TextWriter stderr,
        string workingDirectory,
        string machineName)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(config);
        var serverUrl = NonBlank(getEnvironmentVariable(ServerUrlVariable));
        var token = NonBlank(getEnvironmentVariable(TokenVariable));
        var tokenFromEnvironment = token is not null;
        if (serverUrl is null || token is null)
        {
            var saved = config.Read(stderr);
            serverUrl ??= NonBlank(saved?.ServerUrl);
            token ??= NonBlank(saved?.Token);
        }

        return new BridgeSettings(serverUrl, token, workingDirectory, machineName, tokenFromEnvironment);
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// The server's online MCP endpoint and the token to send, or an error naming every missing variable.
    /// <c>PANOPTICON_SERVER_URL</c> is the server's base URL; a URL already ending in <c>/mcp</c> is used as it is.
    /// </summary>
    public (Uri Endpoint, string Token) RequireServer()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            missing.Add(ServerUrlVariable);
        }

        if (string.IsNullOrWhiteSpace(Token))
        {
            missing.Add(TokenVariable);
        }

        if (missing.Count > 0)
        {
            throw new BridgeException(
                $"The bridge isn't configured: {string.Join(" and ", missing)} {(missing.Count == 1 ? "is" : "are")} not set. Run 'panopticon login' on this machine, or set {(missing.Count == 1 ? "it" : "them")} in the agent's MCP server environment.");
        }

        if (!Uri.TryCreate(ServerUrl!.Trim(), UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            throw new BridgeException($"{ServerUrlVariable} must be an absolute http or https URL.");
        }

        var path = url.AbsolutePath.TrimEnd('/');
        var builder = new UriBuilder(url)
        {
            Path = path.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase) ? path : $"{path}/mcp",
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return (builder.Uri, Token!.Trim());
    }

    /// <summary>A URL safe to show: no user-info, query or fragment.</summary>
    public static string Display(Uri url) => $"{url.Scheme}://{url.Authority}{url.AbsolutePath}";
}
