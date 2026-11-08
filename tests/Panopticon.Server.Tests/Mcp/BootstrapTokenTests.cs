using Panopticon.Server.Auth;

namespace Panopticon.Server.Tests.Mcp;

public class BootstrapTokenTests
{
    [Theory]
    [InlineData("secret", "secret", true)]
    [InlineData("secret", "Secret", false)]
    [InlineData("secret", "secret-but-longer", false)]
    [InlineData("", "secret", false)]
    [InlineData("secret", "", false)]
    [InlineData("secret", null, false)]
    public void Matches_only_the_exact_configured_token(string presented, string? expected, bool matches) =>
        Assert.Equal(matches, BootstrapTokenHandler.Matches(presented, expected));
}
