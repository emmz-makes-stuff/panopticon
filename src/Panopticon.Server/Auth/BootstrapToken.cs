using System.Security.Cryptography;
using System.Text;

namespace Panopticon.Server.Auth;

/// <summary>
/// The walking skeleton's single shared bearer token. Since team-auth it's a Development convenience only (D4, D5):
/// <see cref="ApiTokenHandler"/> accepts it there, as a reporter with no member.
/// </summary>
public sealed class BootstrapTokenOptions
{
    public const string ConfigKey = "Panopticon:BootstrapToken";

    public string? Token { get; set; }
}

public static class BootstrapToken
{
    /// <summary>Compares fixed-length hashes, so timing reveals neither content nor length.</summary>
    public static bool Matches(string presented, string? expected)
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
