using Panopticon.Server.Auth;

namespace Panopticon.Server.Tests.Auth;

// team-auth design D3: secret format and entropy, hashing, user codes.
public class SecretsTests
{
    [Fact]
    public void Secrets_are_32_random_bytes_in_base64url()
    {
        var secret = Secrets.NewSecret();

        Assert.Equal(43, secret.Length); // 32 bytes, unpadded base64url
        Assert.Matches("^[A-Za-z0-9_-]{43}$", secret);
        Assert.NotEqual(secret, Secrets.NewSecret());
    }

    [Fact]
    public void Api_tokens_carry_the_prefix_and_a_full_secret()
    {
        var token = Secrets.NewApiToken();

        Assert.StartsWith("pnp_", token, StringComparison.Ordinal);
        Assert.Matches("^pnp_[A-Za-z0-9_-]{43}$", token);
    }

    [Fact]
    public void Hash_is_sha256_and_stable()
    {
        var hash = Secrets.Hash("abc");

        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, Secrets.Hash("abc"));
        Assert.NotEqual(hash, Secrets.Hash("abd"));
    }

    [Fact]
    public void User_codes_are_eight_letters_from_the_alphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = Secrets.NewUserCode();
            Assert.Equal(8, code.Length);
            Assert.All(code, c => Assert.Contains(c, Secrets.UserCodeAlphabet));
        }
    }

    [Fact]
    public void User_codes_are_shown_with_a_hyphen() =>
        Assert.Equal("BCDF-GHJK", Secrets.FormatUserCode("BCDFGHJK"));

    [Theory]
    [InlineData("BCDF-GHJK", "BCDFGHJK")]
    [InlineData("bcdf-ghjk", "BCDFGHJK")]
    [InlineData("bcdfghjk", "BCDFGHJK")]
    [InlineData(" BCDF GHJK ", "BCDFGHJK")]
    [InlineData("BCDF-GHJ", null)]
    [InlineData("BCDF-GHJKL", null)]
    [InlineData("BCDF-GHJA", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Typed_codes_are_normalized_ignoring_case_and_the_hyphen(string? typed, string? normalized) =>
        Assert.Equal(normalized, Secrets.NormalizeUserCode(typed));
}
