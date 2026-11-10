using Panopticon.Cli.Config;

namespace Panopticon.Cli.Bridge;

/// <summary>The bridge's configuration, read once at start-up (spec cli-bridge, "Configuration").</summary>
/// <param name="TokenFromEnvironment">
/// Whether <see cref="Token"/> came from <c>PANOPTICON_TOKEN</c> rather than the saved login, so a rejected token's
/// error can say which one to fix.
/// </param>
/// <param name="SavedLoginServer">
/// Set when a saved token was not used because the login was saved for a different server than
/// <c>PANOPTICON_SERVER_URL</c>: the saved login's server, for the error.
/// </param>
internal sealed record BridgeSettings(
    string? ServerUrl,
    string? Token,
    string WorkingDirectory,
    string MachineName,
    bool TokenFromEnvironment = false,
    string? SavedLoginServer = null)
{
    public const string ServerUrlVariable = "PANOPTICON_SERVER_URL";
    public const string TokenVariable = "PANOPTICON_TOKEN";

    /// <summary>
    /// The config file the personal tags are read from, afresh for each report, so <c>panopticon tags</c> takes effect
    /// without a restart (tags D8). Null means no personal tags: each report then sends an empty list.
    /// </summary>
    public CliConfigFile? Config { get; init; }

    /// <summary>How long the archive watcher waits before retrying a failed archived report (spec cli-bridge).</summary>
    public TimeSpan ArchiveRetryInterval { get; init; } = TimeSpan.FromSeconds(30);

    public static BridgeSettings FromEnvironment(TextWriter stderr) =>
        Resolve(Environment.GetEnvironmentVariable, CliConfigFile.ForCurrentUser(), stderr, Environment.CurrentDirectory, Environment.MachineName);

    /// <summary>
    /// Each value from its environment variable, then from the login <c>panopticon login</c> saved (design D14). The
    /// file is read only when a variable is unset, so a fully configured environment never touches it. The saved token
    /// belongs to the saved server: it's used only when the server in use is that one, so it's never sent elsewhere.
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
        string? savedLoginServer = null;
        if (serverUrl is null || token is null)
        {
            var saved = config.Read(stderr);
            var savedServer = NonBlank(saved?.ServerUrl);
            var savedToken = NonBlank(saved?.Token);
            serverUrl ??= savedServer;
            if (token is null && savedToken is not null)
            {
                if (SameServer(serverUrl, savedServer))
                {
                    token = savedToken;
                }
                else
                {
                    savedLoginServer = savedServer;
                }
            }
        }

        return new BridgeSettings(serverUrl, token, workingDirectory, machineName, tokenFromEnvironment, savedLoginServer)
        {
            Config = config,
        };
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Whether two server URLs name the same server: scheme, host, port and base path, ignoring case, a trailing
    /// slash and a trailing <c>/mcp</c>. Anything that isn't an http or https URL matches nothing.
    /// </summary>
    internal static bool SameServer(string? a, string? b) =>
        Root(a) is { } left && Root(b) is { } right && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string? Root(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var path = parsed.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^"/mcp".Length];
        }

        return $"{parsed.Scheme}://{parsed.Authority}{path}";
    }

    /// <summary>
    /// The server's online MCP endpoint and the token to send, or an error naming every missing variable.
    /// <c>PANOPTICON_SERVER_URL</c> is the server's base URL; a URL already ending in <c>/mcp</c> is used as it is.
    /// </summary>
    public (Uri Endpoint, string Token) RequireServer()
    {
        // A malformed server URL is reported below as that, not as a mismatch with the saved login.
        if (SavedLoginServer is not null && string.IsNullOrWhiteSpace(Token) && Root(ServerUrl) is not null)
        {
            throw new BridgeException(
                $"{TokenVariable} is not set, and this machine's saved login is for {SafeDisplay(SavedLoginServer)}, not {ServerUrlVariable} ({SafeDisplay(ServerUrl!)}). Run 'panopticon login --server {SafeDisplay(ServerUrl!)}', or set {TokenVariable} in the agent's MCP server environment.");
        }

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

    private static string SafeDisplay(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed) ? Display(parsed) : url;
}
