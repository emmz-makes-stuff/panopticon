using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Panopticon.Server.Auth;

/// <summary>
/// The walking skeleton's single shared bearer token (design D5). Replaced by per-user tokens later.
/// </summary>
public sealed class BootstrapTokenOptions
{
    public const string ConfigKey = "Panopticon:BootstrapToken";

    public string? Token { get; set; }
}

/// <summary>Authenticates <c>Authorization: Bearer &lt;token&gt;</c> against the bootstrap token, in constant time.</summary>
public sealed class BootstrapTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<BootstrapTokenOptions> token)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "BootstrapToken";
    private const string BearerPrefix = "Bearer ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            || !Matches(header[BearerPrefix.Length..].Trim(), token.Value.Token))
        {
            // The presented token is never logged or echoed.
            return Task.FromResult(AuthenticateResult.Fail("Invalid bearer token."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "bootstrap")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    /// <summary>Compares fixed-length hashes, so timing reveals neither content nor length.</summary>
    internal static bool Matches(string presented, string? expected)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }
}
