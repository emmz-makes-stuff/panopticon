using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Distribution;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Distribution;

// Spec cli-distribution: Unix install script, Install location, Install scripts name their server (design D3, D9).
// The rendered script runs under sh against the server on a real Kestrel port, installing a fake "build": a tiny sh
// script that prints its version, so the tests run on any Unix machine.
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed class InstallShTests(PostgresFixture postgres, S3Fixture s3) : IAsyncLifetime
{
    private const string Version = "0.2.0";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly CliBuildsDirectory _builds = CliBuildsDirectory.Create(Version);
    private readonly string _home = Directory.CreateTempSubdirectory("panopticon-install-home-").FullName;
    private PanopticonFactory? _factory;
    private string _server = "";

    public async ValueTask InitializeAsync()
    {
        foreach (var platform in new[] { CliPlatforms.LinuxX64, CliPlatforms.LinuxArm64, CliPlatforms.OsxArm64 })
        {
            WriteFakeBuild(platform, $"#!/bin/sh\necho {Version}\n");
        }

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var port = FreePort();
        _server = $"http://127.0.0.1:{port}";
        _factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct))
        {
            Settings = new Dictionary<string, string>
            {
                [PublicUrlOptions.ConfigKey] = _server,
                [CliDistributionOptions.ConfigKey] = _builds.Path,
            },
        };
        _factory.UseKestrel(port);
        _factory.StartServer();
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _builds.Dispose();
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Installs_to_local_bin_and_says_how_to_log_in()
    {
        SkipOnWindows();

        var run = await RunInstallAsync(("PANOPTICON_UNAME_S", "Linux"), ("PANOPTICON_UNAME_M", "aarch64"), ("PANOPTICON_LIBC", "glibc"));

        Assert.True(run.Exit == 0, run.ToString());
        var target = Path.Combine(_home, ".local", "bin", "panopticon");
        Assert.Equal(_builds.Bytes(CliPlatforms.LinuxArm64), await File.ReadAllBytesAsync(target, Ct));
        Assert.True(File.GetUnixFileMode(target).HasFlag(UnixFileMode.UserExecute));
        Assert.Contains($"Installed panopticon {Version} to {target}", run.Stdout, StringComparison.Ordinal);
        Assert.Contains($"panopticon login --server {_server}\n", run.Stdout, StringComparison.Ordinal);
        Assert.Empty(LeftoverTempFiles(Path.GetDirectoryName(target)!));
    }

    [Fact]
    public async Task A_directory_not_on_path_gets_a_hint()
    {
        SkipOnWindows();

        var run = await RunInstallAsync(("PANOPTICON_UNAME_S", "Darwin"), ("PANOPTICON_UNAME_M", "arm64"));

        Assert.True(run.Exit == 0, run.ToString());
        var dir = Path.Combine(_home, ".local", "bin");
        Assert.Contains($"{dir} is not on your PATH", run.Stdout, StringComparison.Ordinal);
        Assert.Contains($"export PATH=\"{dir}:$PATH\"", run.Stdout, StringComparison.Ordinal);
        Assert.Equal(_builds.Bytes(CliPlatforms.OsxArm64), await File.ReadAllBytesAsync(Path.Combine(dir, "panopticon"), Ct));
    }

    [Fact]
    public async Task Installs_to_PANOPTICON_INSTALL_DIR_and_replaces_what_is_there()
    {
        SkipOnWindows();
        var dir = Path.Combine(_home, "opt", "bin");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "panopticon"), "old", Ct);

        var run = await RunInstallAsync(
            pathPrefix: dir,
            ("PANOPTICON_INSTALL_DIR", dir),
            ("PANOPTICON_UNAME_S", "Linux"),
            ("PANOPTICON_UNAME_M", "x86_64"),
            ("PANOPTICON_LIBC", "glibc"));

        Assert.True(run.Exit == 0, run.ToString());
        Assert.Equal(_builds.Bytes(CliPlatforms.LinuxX64), await File.ReadAllBytesAsync(Path.Combine(dir, "panopticon"), Ct));
        Assert.DoesNotContain("is not on your PATH", run.Stdout, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_home, ".local")));
    }

    [Fact]
    public async Task A_checksum_mismatch_installs_nothing_and_keeps_the_old_cli()
    {
        SkipOnWindows();
        var dir = Path.Combine(_home, ".local", "bin");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "panopticon"), "old", Ct);
        var script = await GetScriptAsync();

        // The script carries the hash from start-up; the bytes served now differ.
        WriteFakeBuild(CliPlatforms.LinuxX64, "#!/bin/sh\necho tampered\n");
        var run = await RunAsync(script, null, ("PANOPTICON_UNAME_S", "Linux"), ("PANOPTICON_UNAME_M", "x86_64"), ("PANOPTICON_LIBC", "glibc"));

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("checksum mismatch", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(dir, "panopticon"), Ct));
        Assert.Empty(LeftoverTempFiles(dir));
    }

    [Fact]
    public async Task A_build_that_wont_run_here_installs_nothing()
    {
        SkipOnWindows();
        var dir = Path.Combine(_home, ".local", "bin");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "panopticon"), "old", Ct);
        WriteFakeBuild(CliPlatforms.OsxArm64, "#!/bin/sh\necho 'version GLIBC_2.38 not found' >&2\nexit 1\n");
        await RestartWithFreshCatalogueAsync();

        var run = await RunInstallAsync(("PANOPTICON_UNAME_S", "Darwin"), ("PANOPTICON_UNAME_M", "arm64"));

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("doesn't run on this machine", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("GLIBC_2.38", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(dir, "panopticon"), Ct));
    }

    [Theory]
    [InlineData("Darwin", "x86_64", "unsupported platform: Darwin x86_64")]
    [InlineData("FreeBSD", "amd64", "unsupported platform: FreeBSD amd64")]
    [InlineData("Linux", "armv7l", "unsupported platform: Linux armv7l")]
    public async Task An_unsupported_platform_installs_nothing(string os, string arch, string message)
    {
        SkipOnWindows();

        var run = await RunInstallAsync(("PANOPTICON_UNAME_S", os), ("PANOPTICON_UNAME_M", arch), ("PANOPTICON_LIBC", "glibc"));

        Assert.NotEqual(0, run.Exit);
        Assert.Contains(message, run.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_home, ".local")));
    }

    [Fact]
    public async Task Musl_linux_installs_nothing()
    {
        SkipOnWindows();

        var run = await RunInstallAsync(("PANOPTICON_UNAME_S", "Linux"), ("PANOPTICON_UNAME_M", "x86_64"), ("PANOPTICON_LIBC", "musl"));

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("musl Linux (such as Alpine)", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("glibc", run.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_home, ".local")));
    }

    [Fact]
    public async Task The_script_is_plain_text_and_never_cached()
    {
        SkipOnWindows();
        using var http = new HttpClient();

        using var response = await http.GetAsync($"{_server}/install.sh", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache);
        var script = await response.Content.ReadAsStringAsync(Ct);
        Assert.StartsWith("#!/bin/sh\n", script, StringComparison.Ordinal);
        Assert.DoesNotContain("@@", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", script, StringComparison.Ordinal);
        Assert.Contains(_builds.Sha256(CliPlatforms.LinuxX64), script, StringComparison.Ordinal);
    }

    private async Task<string> GetScriptAsync()
    {
        using var http = new HttpClient();
        return await http.GetStringAsync($"{_server}/install.sh", Ct);
    }

    private async Task<ShRun> RunInstallAsync(params (string Name, string Value)[] environment) =>
        await RunAsync(await GetScriptAsync(), null, environment);

    private async Task<ShRun> RunInstallAsync(string pathPrefix, params (string Name, string Value)[] environment) =>
        await RunAsync(await GetScriptAsync(), pathPrefix, environment);

    private async Task<ShRun> RunAsync(string script, string? pathPrefix, params (string Name, string Value)[] environment)
    {
        var scriptFile = Path.Combine(_home, $"install-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(scriptFile, script, Ct);
        var start = new ProcessStartInfo("/bin/sh", [scriptFile])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _home,
        };
        start.Environment.Clear();
        start.Environment["HOME"] = _home;
        start.Environment["PATH"] = (pathPrefix is null ? "" : pathPrefix + ":") + "/usr/local/bin:/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin";
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct);
        return new ShRun(process.ExitCode, await stdout, await stderr);
    }

    /// <summary>The catalogue hashes at start-up: after changing a build, start a fresh server on the same directory.</summary>
    private async Task RestartWithFreshCatalogueAsync()
    {
        await _factory!.DisposeAsync();
        var port = FreePort();
        _server = $"http://127.0.0.1:{port}";
        _factory = new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct))
        {
            Settings = new Dictionary<string, string>
            {
                [PublicUrlOptions.ConfigKey] = _server,
                [CliDistributionOptions.ConfigKey] = _builds.Path,
            },
        };
        _factory.UseKestrel(port);
        _factory.StartServer();
    }

    private void WriteFakeBuild(string platform, string script)
    {
        var file = _builds.WriteBuild(platform, Encoding.UTF8.GetBytes(script));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static string[] LeftoverTempFiles(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, ".panopticon-install-*") : [];

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void SkipOnWindows() =>
        Assert.SkipWhen(OperatingSystem.IsWindows(), "install.sh runs under sh, on Unix only.");

    private sealed record ShRun(int Exit, string Stdout, string Stderr)
    {
        public override string ToString() => $"exit {Exit}\nstdout:\n{Stdout}\nstderr:\n{Stderr}";
    }
}
