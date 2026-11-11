using System.Diagnostics;
using System.Runtime.InteropServices;
using Panopticon.Cli.IntegrationTests.Infrastructure;
using Panopticon.Server.Auth;
using Panopticon.Server.Distribution;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Cli.IntegrationTests;

// Spec cli-distribution: Self-update and Version command, end to end. A copy of the published Native AOT binary updates
// itself from a server on a real Kestrel port, whose catalogue holds this machine's build under another version.
public sealed class PublishedCliUpdateTests(PostgresFixture postgres, S3Fixture s3) : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string _platform = RuntimeInformation.RuntimeIdentifier;

    private readonly string _root = Directory.CreateTempSubdirectory("panopticon-cli-update-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task The_binary_replaces_itself_with_the_servers_build_and_still_runs()
    {
        var binary = CopyOfPublishedCli();
        var before = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(binary, before);
        using var builds = CliBuildsDirectory.Create("9.9.9", []);
        builds.WriteBuild(_platform, await File.ReadAllBytesAsync(PublishedCli.Path, Ct));
        var (server, factory) = await StartServerAsync(builds.Path);
        await using var _ = factory;

        var update = await RunAsync(binary, "update", "--server", server);

        Assert.True(update.Exit == 0, update.ToString());
        Assert.Contains("Updated panopticon from 0.0.0-dev to 9.9.9.", update.Stdout, StringComparison.Ordinal);
        Assert.True(File.GetLastWriteTimeUtc(binary) > before, "The binary on disk should be the downloaded build.");
        Assert.Empty(Directory.GetFiles(_root, ".panopticon-update-*"));
        var version = await RunAsync(binary, "--version");
        Assert.Equal(0, version.Exit);
        Assert.Equal("0.0.0-dev", version.Stdout.Trim());
    }

    [Fact]
    public async Task The_same_version_leaves_the_binary_alone()
    {
        var binary = CopyOfPublishedCli();
        var before = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(binary, before);
        using var builds = CliBuildsDirectory.Create("0.0.0-dev", []);
        builds.WriteBuild(_platform, await File.ReadAllBytesAsync(PublishedCli.Path, Ct));
        var (server, factory) = await StartServerAsync(builds.Path);
        await using var _ = factory;

        var update = await RunAsync(binary, "update", "--server", server);

        Assert.True(update.Exit == 0, update.ToString());
        Assert.Contains("panopticon 0.0.0-dev is already up to date", update.Stdout, StringComparison.Ordinal);
        Assert.Equal(before, File.GetLastWriteTimeUtc(binary));
    }

    private string CopyOfPublishedCli()
    {
        var binary = Path.Combine(_root, Path.GetFileName(PublishedCli.Path));
        File.Copy(PublishedCli.Path, binary);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(binary, (UnixFileMode)0b111_101_101);
        }

        return binary;
    }

    private async Task<(string Server, PanopticonFactory Factory)> StartServerAsync(string cliDirectory)
    {
        var port = FreePort.Next();
        var server = $"http://127.0.0.1:{port}";
        var factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct))
        {
            Settings = new Dictionary<string, string>
            {
                [PublicUrlOptions.ConfigKey] = server,
                [CliDistributionOptions.ConfigKey] = cliDirectory,
            },
        };
        factory.UseKestrel(port);
        factory.StartServer();
        return (server, factory);
    }

    private async Task<ProcessRun> RunAsync(string binary, params string[] args)
    {
        var start = new ProcessStartInfo(binary, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _root,
        };

        // Never the real saved login: an empty config directory, and no server or token from the environment.
        start.Environment["XDG_CONFIG_HOME"] = Path.Combine(_root, "config");
        start.Environment["APPDATA"] = Path.Combine(_root, "config");
        start.Environment.Remove("PANOPTICON_SERVER_URL");
        start.Environment.Remove("PANOPTICON_TOKEN");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct);
        return new ProcessRun(process.ExitCode, await stdout, await stderr);
    }

    private sealed record ProcessRun(int Exit, string Stdout, string Stderr)
    {
        public override string ToString() => $"exit {Exit}\nstdout:\n{Stdout}\nstderr:\n{Stderr}";
    }
}
