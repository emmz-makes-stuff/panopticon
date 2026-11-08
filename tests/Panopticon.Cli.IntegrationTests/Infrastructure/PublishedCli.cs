namespace Panopticon.Cli.IntegrationTests.Infrastructure;

/// <summary>
/// The Native AOT binary that <c>make aot</c> publishes to <c>artifacts/cli</c> (<c>make test</c> runs it first), or
/// the file named by <c>PANOPTICON_CLI_PATH</c>.
/// </summary>
public static class PublishedCli
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        if (Environment.GetEnvironmentVariable("PANOPTICON_CLI_PATH") is { Length: > 0 } configured)
        {
            return Require(configured);
        }

        var name = OperatingSystem.IsWindows() ? "panopticon.exe" : "panopticon";
        return Require(System.IO.Path.Combine(RepoRoot.Path, "artifacts", "cli", name));
    }

    // A missing binary fails the test loudly rather than skipping it: the AOT build is what's under test.
    private static string Require(string path) => File.Exists(path)
        ? path
        : throw new InvalidOperationException($"The published CLI isn't at '{path}'. Run 'make aot' (or 'make test', which runs it first).");
}
