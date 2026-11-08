namespace Panopticon.Cli.Bridge;

/// <summary>The bridge's configuration, read once at start-up (spec cli-bridge, "Configuration").</summary>
internal sealed record BridgeSettings(string? ServerUrl, string? Token, string WorkingDirectory, string MachineName)
{
    public const string ServerUrlVariable = "PANOPTICON_SERVER_URL";
    public const string TokenVariable = "PANOPTICON_TOKEN";

    public static BridgeSettings FromEnvironment() => new(
        Environment.GetEnvironmentVariable(ServerUrlVariable),
        Environment.GetEnvironmentVariable(TokenVariable),
        Environment.CurrentDirectory,
        Environment.MachineName);

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
                $"The bridge isn't configured: set {string.Join(" and ", missing)} in the agent's MCP server environment.");
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
