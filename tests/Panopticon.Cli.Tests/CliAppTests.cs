namespace Panopticon.Cli.Tests;

public class CliAppTests
{
    [Fact]
    public async Task Help_lists_the_mcp_command()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliApp.RunAsync(["--help"], stdout, stderr);

        Assert.Equal(0, exit);
        Assert.Contains("mcp", stdout.ToString(), StringComparison.Ordinal);
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
