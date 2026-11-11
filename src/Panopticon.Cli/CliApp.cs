using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;
using Panopticon.Cli.Login;
using Panopticon.Cli.Update;

namespace Panopticon.Cli;

/// <summary>Command dispatch for the <c>panopticon</c> CLI.</summary>
internal static class CliApp
{
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        // A Windows update leaves the old binary beside the new one, renamed aside; tidy it on the next start (design D6).
        // Only for the panopticon binary itself, never a host such as dotnet running the tests.
        if (BinaryReplacer.IsCliBinary(Environment.ProcessPath))
        {
            BinaryReplacer.CleanUpAfterUpdate(Environment.ProcessPath);
        }

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            stdout.WriteLine("Usage: panopticon <command>");
            stdout.WriteLine();
            stdout.WriteLine("Commands:");
            stdout.WriteLine("  login [--server <url>]  Log this machine in: approve a code in the dashboard, and save a token");
            stdout.WriteLine("  mcp                     Run the local MCP bridge over stdio");
            stdout.WriteLine("  tags                    List the personal tags sent with this machine's reports");
            stdout.WriteLine("  tags add <tag>...       Add personal tags, e.g. dev or \"language: csharp\"");
            stdout.WriteLine("  tags remove <tag>...    Remove personal tags");
            stdout.WriteLine("  update [--server <url>] Install the CLI build this machine's server serves");
            stdout.WriteLine("  --version, version      Print the CLI's version");
            stdout.WriteLine();
            stdout.WriteLine("Environment (each overrides the login 'panopticon login' saved):");
            stdout.WriteLine($"  {BridgeSettings.ServerUrlVariable}  The Panopticon server's base URL");
            stdout.WriteLine($"  {BridgeSettings.TokenVariable}       The API token for the server");
            stdout.WriteLine();
            stdout.WriteLine($"Saved login: {CliConfigFile.ForCurrentUser().Path}");
            return 0;
        }

        if (args[0] is "--version" or "version")
        {
            stdout.WriteLine(CliVersion.Current);
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
            var login = new LoginCommand(
                static () => new HttpClient(),
                Environment.GetEnvironmentVariable,
                CliConfigFile.ForCurrentUser(),
                Environment.MachineName,
                Task.Delay,
                stdout,
                stderr);
            return await UntilCtrlCAsync(cancellationToken => login.RunAsync(args[1..], cancellationToken));
        }

        if (args[0] == "update")
        {
            var update = new UpdateCommand(
                static () => new HttpClient(),
                Environment.GetEnvironmentVariable,
                CliConfigFile.ForCurrentUser(),
                CliVersion.Current,
                System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                Environment.ProcessPath,
                BinaryReplacer.ForThisMachine(),
                stdout,
                stderr);
            return await UntilCtrlCAsync(cancellationToken => update.RunAsync(args[1..], cancellationToken));
        }

        if (args[0] == "tags")
        {
            return new TagsCommand(CliConfigFile.ForCurrentUser(), stdout, stderr).Run(args[1..]);
        }

        await stderr.WriteLineAsync($"Unknown command '{args[0]}'. Run 'panopticon --help'.");
        return 1;
    }

    /// <summary>
    /// Runs a command with a token that Ctrl+C cancels, so the command ends cleanly (exit code 130, temp files gone)
    /// rather than the process being killed part-way.
    /// </summary>
    private static async Task<int> UntilCtrlCAsync(Func<CancellationToken, Task<int>> command)
    {
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            return await command(cancel.Token);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }
}
