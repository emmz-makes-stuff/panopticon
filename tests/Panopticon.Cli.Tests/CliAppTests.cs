namespace Panopticon.Cli.Tests;

public class CliAppTests
{
    [Fact]
    public void Help_lists_the_mcp_command()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = CliApp.Run(["--help"], stdout, stderr);

        Assert.Equal(0, exit);
        Assert.Contains("mcp", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_command_fails_with_a_message()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = CliApp.Run(["frobnicate"], stdout, stderr);

        Assert.Equal(1, exit);
        Assert.Contains("frobnicate", stderr.ToString(), StringComparison.Ordinal);
    }
}
