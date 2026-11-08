using Panopticon.Cli.Tests.Infrastructure;

namespace Panopticon.Cli.Tests;

// Spec cli-bridge, "Change documents read locally".
public class ChangeDocumentsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Change_exists_returns_the_current_tasks_content()
    {
        using var repo = TempGitRepo.Create();
        var path = repo.WriteTasks("walking-skeleton", "## 1. Setup\n- [x] 1.1 First\n");

        Assert.Equal("## 1. Setup\n- [x] 1.1 First\n", await ChangeDocuments.ReadTasksAsync(repo.Root, "walking-skeleton", Ct));

        // It reads the file as it is now, not a cached copy.
        await File.WriteAllTextAsync(path, "## 1. Setup\n- [x] 1.1 First\n- [ ] 1.2 Second\n", Ct);
        Assert.EndsWith("1.2 Second\n", await ChangeDocuments.ReadTasksAsync(repo.Root, "walking-skeleton", Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Change_missing_names_the_path_it_looked_for()
    {
        using var repo = TempGitRepo.Create();
        repo.WriteTasks("walking-skeleton", "");

        var error = await Assert.ThrowsAsync<BridgeException>(
            () => ChangeDocuments.ReadTasksAsync(repo.Root, "no-such-change", Ct));

        Assert.Contains(Path.Combine(repo.Root, "openspec", "changes", "no-such-change"), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Change_folder_without_tasks_names_the_file_it_looked_for()
    {
        using var repo = TempGitRepo.Create();
        var directory = repo.CreateSubdirectory(Path.Combine("openspec", "changes", "half-made"));

        var error = await Assert.ThrowsAsync<BridgeException>(
            () => ChangeDocuments.ReadTasksAsync(repo.Root, "half-made", Ct));

        Assert.Contains(Path.Combine(directory, "tasks.md"), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../../etc")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("/etc")]
    [InlineData("C:")]
    [InlineData(" spaced ")]
    [InlineData("")]
    public async Task Path_traversal_is_an_invalid_name_and_reads_nothing(string change)
    {
        using var repo = TempGitRepo.Create();
        // A tasks.md that "../../etc"-style names would reach if they weren't refused.
        File.WriteAllText(Path.Combine(repo.Root, "tasks.md"), "outside");
        repo.WriteTasks("walking-skeleton", "inside");

        var error = await Assert.ThrowsAsync<BridgeException>(() => ChangeDocuments.ReadTasksAsync(repo.Root, change, Ct));

        Assert.StartsWith("Invalid change name", error.Message, StringComparison.Ordinal);
    }
}
