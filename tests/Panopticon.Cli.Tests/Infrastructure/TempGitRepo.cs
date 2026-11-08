using System.Diagnostics;

namespace Panopticon.Cli.Tests.Infrastructure;

/// <summary>A throwaway directory under the temp folder, optionally made a git repo with an origin remote.</summary>
public sealed class TempGitRepo : IDisposable
{
    private TempGitRepo(string path) => Root = path;

    /// <summary>The directory as created (it may sit behind a symlink, e.g. /var → /private/var on macOS).</summary>
    public string Root { get; }

    public static TempGitRepo CreateEmptyDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "panopticon-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempGitRepo(path);
    }

    public static TempGitRepo Create(string? originUrl = "git@github.com:emmz/panopticon.git")
    {
        var repo = CreateEmptyDirectory();
        repo.Git("init", "--quiet");
        if (originUrl is not null)
        {
            repo.Git("remote", "add", "origin", originUrl);
        }

        return repo;
    }

    /// <summary>Writes <c>openspec/changes/&lt;change&gt;/tasks.md</c> and returns its path.</summary>
    public string WriteTasks(string change, string content)
    {
        var directory = Path.Combine(Root, "openspec", "changes", change);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "tasks.md");
        File.WriteAllText(path, content);
        return path;
    }

    public string CreateSubdirectory(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Git(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr}");
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp folder doesn't affect other tests.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: git can leave read-only object files on some platforms.
        }
    }
}
