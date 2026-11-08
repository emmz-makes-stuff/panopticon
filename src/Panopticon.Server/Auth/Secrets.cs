using System.Security.Cryptography;
using System.Text;

namespace Panopticon.Server.Auth;

/// <summary>
/// Secrets and codes for team-auth (design D3). Secrets are 32 random bytes, base64url-encoded; only their SHA-256
/// hash is stored, and they're looked up by that hash. They're high-entropy, so a slow KDF would add nothing.
/// </summary>
public static class Secrets
{
    public const int SecretBytes = 32;

    /// <summary>The prefix that marks a Panopticon API token, so it's recognisable if it leaks.</summary>
    public const string ApiTokenPrefix = "pnp_";

    /// <summary>Consonants only (no vowels, no Y), so a code can't spell a word and has no I/O to mistake for 1/0.</summary>
    public const string UserCodeAlphabet = "BCDFGHJKLMNPQRSTVWXZ";

    public const int UserCodeLength = 8;

    /// <summary>A new secret for a sign-in link, a session or a device login.</summary>
    public static string NewSecret() => Base64UrlEncode(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>A new API token: <see cref="ApiTokenPrefix"/> plus a secret.</summary>
    public static string NewApiToken() => ApiTokenPrefix + NewSecret();

    /// <summary>The stored form of a secret.</summary>
    public static byte[] Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>A new user code, normalized: <see cref="UserCodeLength"/> letters with no hyphen.</summary>
    public static string NewUserCode()
    {
        Span<char> code = stackalloc char[UserCodeLength];
        for (var i = 0; i < code.Length; i++)
        {
            code[i] = UserCodeAlphabet[RandomNumberGenerator.GetInt32(UserCodeAlphabet.Length)];
        }

        return new string(code);
    }

    /// <summary>How a user code is shown: <c>XXXX-XXXX</c>.</summary>
    public static string FormatUserCode(string normalized)
    {
        ArgumentNullException.ThrowIfNull(normalized);
        return normalized.Length == UserCodeLength ? $"{normalized[..4]}-{normalized[4..]}" : normalized;
    }

    /// <summary>
    /// A typed code in its normalized form, ignoring case, hyphens and spaces; null when it can't be a code.
    /// </summary>
    public static string? NormalizeUserCode(string? typed)
    {
        if (typed is null)
        {
            return null;
        }

        var builder = new StringBuilder(UserCodeLength);
        foreach (var c in typed)
        {
            if (c is '-' or ' ')
            {
                continue;
            }

            var upper = char.ToUpperInvariant(c);
            if (!UserCodeAlphabet.Contains(upper, StringComparison.Ordinal) || builder.Length == UserCodeLength)
            {
                return null;
            }

            builder.Append(upper);
        }

        return builder.Length == UserCodeLength ? builder.ToString() : null;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
