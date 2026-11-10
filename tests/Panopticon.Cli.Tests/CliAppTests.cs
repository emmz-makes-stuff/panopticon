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
    }

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
