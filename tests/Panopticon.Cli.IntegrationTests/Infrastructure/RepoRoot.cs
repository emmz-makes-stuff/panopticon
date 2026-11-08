namespace Panopticon.Cli.IntegrationTests.Infrastructure;

/// <summary>The repo root: the nearest directory above the test output that holds <c>Panopticon.slnx</c>.</summary>
public static class RepoRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Panopticon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Couldn't find the repo root (Panopticon.slnx) above the test output.");
    }
}
