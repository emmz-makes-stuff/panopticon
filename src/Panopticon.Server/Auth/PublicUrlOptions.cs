using System.Diagnostics.CodeAnalysis;

namespace Panopticon.Server.Auth;

/// <summary>
/// The server's public base URL (<c>Panopticon:PublicUrl</c>): links in sign-in emails and device-login URLs are
/// built on it, never on the request's Host header. Required outside Development (team-auth D9).
/// </summary>
public sealed class PublicUrlOptions
{
    public const string ConfigKey = "Panopticon:PublicUrl";

    public string? Value { get; set; }

    public bool TryGetUri([NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(Value?.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        uri = new Uri(parsed.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        return true;
    }

    /// <summary>An absolute URL for a path under the public URL, e.g. <c>Link("sign-in/confirm")</c>.</summary>
    public Uri Link(string relativePath)
    {
        if (!TryGetUri(out var root))
        {
            throw new InvalidOperationException($"{ConfigKey} is not configured.");
        }

        return new Uri(root, relativePath.TrimStart('/'));
    }
}
