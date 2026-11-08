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
}
