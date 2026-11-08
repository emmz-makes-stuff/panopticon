using Panopticon.Contracts;

namespace Panopticon.Cli.Git;

/// <summary>
/// Turns a git remote URL into a repo identity (host, owner, name). Accepts URL forms
/// (<c>ssh://</c>, <c>https://</c>, <c>http://</c>, <c>git://</c>) and scp-style <c>[user@]host:path</c>, from any
/// host. Credentials are dropped and never appear in the result or in an error message.
/// </summary>
internal static class GitRemote
{
    // Azure DevOps SSH remotes use a dedicated host and a leading "v3/" segment.
    private static readonly Dictionary<string, string> _sshHostAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ssh.dev.azure.com"] = "dev.azure.com",
    };

    private static readonly string[] _urlSchemes = ["ssh", "https", "http", "git", "git+ssh", "ssh+git"];

    /// <summary>Parses <paramref name="remoteUrl"/>, or throws a <see cref="FormatException"/> that doesn't echo it.</summary>
    public static RepoRef Parse(string remoteUrl)
    {
        ArgumentNullException.ThrowIfNull(remoteUrl);

        var (host, path) = SplitHostAndPath(remoteUrl.Trim())
            ?? throw Unrecognised("it isn't an SSH, scp-style or HTTPS URL");

        var azureSsh = _sshHostAliases.TryGetValue(host, out var alias);
        if (azureSsh)
        {
            host = alias!;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (azureSsh && segments.Count > 0 && segments[0] == "v3")
        {
            segments.RemoveAt(0);
        }

        segments.RemoveAll(segment => segment == "_git");

        if (segments.Count > 0 && segments[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            segments[^1] = segments[^1][..^".git".Length];
        }

        if (segments.Count < 2 || segments.Any(string.IsNullOrWhiteSpace))
        {
            throw Unrecognised("it needs an owner and a repository name in its path");
        }

        return new RepoRef(host.ToLowerInvariant(), string.Join('/', segments.Take(segments.Count - 1)), segments[^1]);
    }

    private static (string Host, string Path)? SplitHostAndPath(string url)
    {
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            var scheme = url[..schemeEnd];
            if (!_urlSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase)
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || string.IsNullOrEmpty(uri.Host))
            {
                return null;
            }

            // Uri.Host never includes the user-info (credentials) or the port.
            return (uri.Host, Uri.UnescapeDataString(uri.AbsolutePath));
        }

        // scp-style: [user@]host:path. A local path (no colon before the first slash) or a Windows
        // drive path ("C:\...") is not a remote we can identify.
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        var slash = url.IndexOf('/', StringComparison.Ordinal);
        if (colon <= 0 || (slash >= 0 && slash < colon) || url.Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        var authority = url[..colon];
        var at = authority.LastIndexOf('@');
        var host = at >= 0 ? authority[(at + 1)..] : authority;
        if (host.Length <= 1)
        {
            return null;
        }

        return (host, url[(colon + 1)..]);
    }

    // The message describes the problem without the URL, which may hold credentials.
    private static FormatException Unrecognised(string reason) =>
        new($"The origin remote URL isn't recognised: {reason}.");
}
