using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Panopticon.Cli.Bridge;

namespace Panopticon.Cli;

/// <summary>Command dispatch for the <c>panopticon</c> CLI.</summary>
internal static class CliApp
{
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            stdout.WriteLine("Usage: panopticon <command>");
            stdout.WriteLine();
            stdout.WriteLine("Commands:");
            stdout.WriteLine("  mcp    Run the local MCP bridge over stdio");
            stdout.WriteLine();
            stdout.WriteLine("Environment (mcp):");
            stdout.WriteLine($"  {BridgeSettings.ServerUrlVariable}  The Panopticon server's base URL");
            stdout.WriteLine($"  {BridgeSettings.TokenVariable}       The bearer token for the server");
            return 0;
        }

        if (args[0] == "mcp")
        {
            // stdout carries the MCP protocol from here on; everything else goes to stderr.
            using var host = BridgeHost.Build(
                BridgeSettings.FromEnvironment(),
                static () => new HttpClient(),
                static mcp => mcp.WithStdioServerTransport(),
                stderr);
            await host.RunAsync();
            return 0;
        }

        await stderr.WriteLineAsync($"Unknown command '{args[0]}'. Run 'panopticon --help'.");
        return 1;
    }
}
