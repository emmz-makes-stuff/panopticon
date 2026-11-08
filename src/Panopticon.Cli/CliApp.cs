namespace Panopticon.Cli;

/// <summary>Command dispatch for the <c>panopticon</c> CLI.</summary>
internal static class CliApp
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            stdout.WriteLine("Usage: panopticon <command>");
            stdout.WriteLine();
            stdout.WriteLine("Commands:");
            stdout.WriteLine("  mcp    Run the local MCP bridge over stdio");
            return 0;
        }

        stderr.WriteLine($"Unknown command '{args[0]}'. Run 'panopticon --help'.");
        return 1;
    }
}
