using System.ComponentModel;
using System.Diagnostics;

namespace Panopticon.Cli.Git;

/// <summary>Reads what the bridge needs from the local git working tree, by running <c>git</c>.</summary>
internal static class GitRepository
{
    /// <summary>The working tree's top-level directory, found from <paramref name="workingDirectory"/>.</summary>
    public static async Task<string> FindRootAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(workingDirectory, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            throw new BridgeException(
                $"No git repository was found from '{workingDirectory}'. Run the bridge inside the repo's working tree."
                + Detail(result.Stderr));
        }

        return Path.GetFullPath(result.Stdout.Trim());
    }

    /// <summary>The URL of the <c>origin</c> remote. It may hold credentials: never log it or put it in an error.</summary>
    public static async Task<string> GetOriginUrlAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(repoRoot, ["remote", "get-url", "origin"], cancellationToken);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            throw new BridgeException(
                "The repo has no 'origin' remote. Panopticon needs a remote to identify the repo: add one with "
                + "'git remote add origin <url>'.");
        }

        return result.Stdout.Trim();
    }

    /// <summary>The checked-out branch's short name, or null on a detached HEAD.</summary>
    public static async Task<string?> GetBranchAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(repoRoot, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout) ? result.Stdout.Trim() : null;
    }

    private static string Detail(string stderr)
    {
        var line = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return line is null ? "" : $" (git: {line})";
    }

    private static async Task<GitResult> RunGitAsync(
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RunProcessAsync("git", workingDirectory, arguments, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            throw new BridgeException("git isn't installed or isn't on PATH.", exception);
        }
    }

    /// <summary>Runs a process to completion; cancelling kills it (and anything it started) rather than leaving it running.</summary>
    internal static async Task<GitResult> RunProcessAsync(
        string fileName,
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Never let git prompt: the bridge's stdin belongs to the MCP protocol.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new BridgeException($"{fileName} could not be started.");
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new BridgeException($"The working directory '{workingDirectory}' doesn't exist.", exception);
        }

        using (process)
        {
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                throw;
            }

            return new GitResult(process.ExitCode, await stdout, await stderr);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It exited on its own in the meantime.
        }
    }

    internal sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
