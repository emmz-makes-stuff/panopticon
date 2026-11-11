namespace Panopticon.Cli.Tests;

public class CliAppTests
{
    [Fact]
    public async Task Help_lists_the_login_mcp_and_tags_commands()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliApp.RunAsync(["--help"], stdout, stderr);

        Assert.Equal(0, exit);
        var help = stdout.ToString();
        Assert.Contains("  login [--server <url>]", help, StringComparison.Ordinal);
        Assert.Contains("  mcp ", help, StringComparison.Ordinal);
        Assert.Contains("  tags ", help, StringComparison.Ordinal);
        Assert.Contains("  tags add <tag>...", help, StringComparison.Ordinal);
        Assert.Contains("  tags remove <tag>...", help, StringComparison.Ordinal);
        Assert.Contains("config.json", help, StringComparison.Ordinal);
        Assert.Contains("  --version, version ", help, StringComparison.Ordinal);
        Assert.Contains("  update [--server <url>]", help, StringComparison.Ordinal);
    }

    // Spec cli-distribution: Version command. A local build is 0.0.0-dev (design D5).
    [Theory]
    [InlineData("--version")]
    [InlineData("version")]
    public async Task Version_prints_the_version_without_build_metadata(string command)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliApp.RunAsync([command], stdout, stderr);

        Assert.Equal(0, exit);
        Assert.Equal("0.0.0-dev", stdout.ToString().Trim());
        Assert.Empty(stderr.ToString());
    }

    [Theory]
    [InlineData("0.2.0", "0.2.0")]
    [InlineData("0.2.0+abc123", "0.2.0")]
    [InlineData("0.0.0-dev+abc123", "0.0.0-dev")]
    public void Build_metadata_is_dropped(string informational, string expected) =>
        Assert.Equal(expected, CliVersion.WithoutBuildMetadata(informational));

    [Fact]
    public async Task Unknown_command_fails_with_a_message()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliApp.RunAsync(["frobnicate"], stdout, stderr);

        Assert.Equal(1, exit);
        Assert.Contains("frobnicate", stderr.ToString(), StringComparison.Ordinal);
    }
}
