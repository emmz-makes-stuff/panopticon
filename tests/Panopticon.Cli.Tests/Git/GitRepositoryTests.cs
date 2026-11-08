using Panopticon.Cli.Git;
using Panopticon.Cli.Tests.Infrastructure;

namespace Panopticon.Cli.Tests.Git;

// Spec cli-bridge, "Repo root" and "Repo identity from git remote" / "No origin remote".
public class GitRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Started_in_a_subdirectory_finds_the_top_level()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", "## 1. Setup\n- [ ] 1.1 Do it\n");
        var subdirectory = repo.CreateSubdirectory(Path.Combine("src", "app"));

        var root = await GitRepository.FindRootAsync(subdirectory, Ct);

        // The root may be reported through a resolved symlink (macOS /var → /private/var), so check by content.
        Assert.True(File.Exists(Path.Combine(root, "openspec", "changes", "walking-skeleton", "tasks.md")));
        Assert.Equal("## 1. Setup\n- [ ] 1.1 Do it\n", await ChangeDocuments.ReadTasksAsync(root, "walking-skeleton", Ct));
    }

    [Fact]
    public async Task Not_a_git_repo_says_no_repository_was_found()
    {
        using var directory = TempGitRepo.CreateEmptyDirectory();

        var error = await Assert.ThrowsAsync<BridgeException>(() => GitRepository.FindRootAsync(directory.Root, Ct));

        Assert.Contains("No git repository was found", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_the_origin_url()
    {
        using var repo = TempGitRepo.Create("https://gitlab.com/acme/platform/api.git");

        var url = await GitRepository.GetOriginUrlAsync(repo.Root, Ct);

        Assert.Equal("https://gitlab.com/acme/platform/api.git", url);
    }

    [Fact]
    public async Task No_origin_remote_says_a_remote_is_required()
    {
        using var repo = TempGitRepo.Create(originUrl: null);
        repo.Git("remote", "add", "upstream", "git@github.com:emmz/panopticon.git");

        var error = await Assert.ThrowsAsync<BridgeException>(() => GitRepository.GetOriginUrlAsync(repo.Root, Ct));

        Assert.Contains("no 'origin' remote", error.Message, StringComparison.Ordinal);
        Assert.Contains("needs a remote", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_a_call_kills_the_child_process()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses sh.");
        using var directory = TempGitRepo.CreateEmptyDirectory();
        var pidFile = Path.Combine(directory.Root, "pid");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // A stand-in for a git that hangs: it records its process id, then sleeps.
        var run = GitRepository.RunProcessAsync("sh", directory.Root, ["-c", $"echo $$ > '{pidFile}'; exec sleep 30"], cancel.Token);
        var pid = await WaitForPidAsync(pidFile);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (IsRunning(pid) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, Ct);
        }

        Assert.False(IsRunning(pid), $"Process {pid} is still running after the call was cancelled.");
    }

    private static async Task<int> WaitForPidAsync(string pidFile)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(pidFile) && int.TryParse((await File.ReadAllTextAsync(pidFile, Ct)).Trim(), out var pid))
            {
                return pid;
            }

            await Task.Delay(20, Ct);
        }

        throw new TimeoutException("The child process never wrote its id.");
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
