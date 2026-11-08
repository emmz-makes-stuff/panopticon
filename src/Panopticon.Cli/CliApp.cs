using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;
using Panopticon.Cli.Login;

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
            stdout.WriteLine("  login [--server <url>]  Log this machine in: approve a code in the dashboard, and save a token");
            stdout.WriteLine("  mcp                     Run the local MCP bridge over stdio");
            stdout.WriteLine();
            stdout.WriteLine("Environment (each overrides the login 'panopticon login' saved):");
            stdout.WriteLine($"  {BridgeSettings.ServerUrlVariable}  The Panopticon server's base URL");
            stdout.WriteLine($"  {BridgeSettings.TokenVariable}       The API token for the server");
            stdout.WriteLine();
            stdout.WriteLine($"Saved login: {CliConfigFile.ForCurrentUser().Path}");
            return 0;
        }

        if (args[0] == "mcp")
        {
            // stdout carries the MCP protocol from here on; everything else goes to stderr.
            using var host = BridgeHost.Build(
                BridgeSettings.FromEnvironment(stderr),
                static () => new HttpClient(),
                static mcp => mcp.WithStdioServerTransport(),
                stderr);
            await host.RunAsync();
            return 0;
        }

        if (args[0] == "login")
        {
            using var cancel = new CancellationTokenSource();
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                // Ctrl+C ends the wait cleanly, with exit code 130, rather than killing the process.
                e.Cancel = true;
                cancel.Cancel();
            };
            Console.CancelKeyPress += onCancel;
            try
            {
                var login = new LoginCommand(
                    static () => new HttpClient(),
                    Environment.GetEnvironmentVariable,
                    CliConfigFile.ForCurrentUser(),
                    Environment.MachineName,
                    Task.Delay,
                    stdout,
                    stderr);
                return await login.RunAsync(args[1..], cancel.Token);
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }
        }

        await stderr.WriteLineAsync($"Unknown command '{args[0]}'. Run 'panopticon --help'.");
        return 1;
    }
}
