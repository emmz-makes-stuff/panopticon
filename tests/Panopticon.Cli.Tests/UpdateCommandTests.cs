using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Panopticon.Cli.Config;
using Panopticon.Cli.Update;
using Panopticon.Contracts;

namespace Panopticon.Cli.Tests;

// Spec cli-distribution: Self-update and Safe self-update (design D6), through the HTTP and file-system seams.
public sealed class UpdateCommandTests : IDisposable
{
    private const string Server = "https://panopticon.example.com";
    private const string Platform = CliPlatforms.LinuxX64;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _root = Directory.CreateTempSubdirectory("panopticon-update-").FullName;
    private readonly string _binary;
    private readonly StubServer _server = new();

    public UpdateCommandTests()
    {
        _binary = Path.Combine(_root, "bin", "panopticon");
        Directory.CreateDirectory(Path.GetDirectoryName(_binary)!);
        File.WriteAllText(_binary, "old build");
    }

    public void Dispose()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Path.GetDirectoryName(_binary)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private CliConfigFile Config => new(Path.Combine(_root, "config", "config.json"));

    private async Task<Run> RunAsync(
        string currentVersion = "0.1.1",
        string[]? args = null,
        Dictionary<string, string>? environment = null,
        BinaryReplacer? replacer = null,
        string? binaryPath = null,
        CancellationToken? cancellationToken = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var command = new UpdateCommand(
            () => new HttpClient(_server, disposeHandler: false),
            name => environment?.GetValueOrDefault(name),
            Config,
            currentVersion,
            Platform,
            binaryPath ?? _binary,
            replacer ?? new BinaryReplacer(windows: false),
            stdout,
            stderr,
            TimeSpan.FromMilliseconds(200));
        var exit = await command.RunAsync(args ?? ["--server", Server], cancellationToken ?? Ct);
        return new Run(exit, stdout.ToString(), stderr.ToString());
    }

    private string[] LeftoverTempFiles() => Directory.GetFiles(Path.GetDirectoryName(_binary)!, ".panopticon-update-*");

    [Fact]
    public async Task A_newer_build_replaces_the_binary()
    {
        _server.Serve("0.2.0", "new build");

        var run = await RunAsync("0.1.1");

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal("new build", await File.ReadAllTextAsync(_binary, Ct));
        Assert.Contains("Updated panopticon from 0.1.1 to 0.2.0.", run.Stdout, StringComparison.Ordinal);
        Assert.Contains("Restart your agent", run.Stdout, StringComparison.Ordinal);
        Assert.Empty(LeftoverTempFiles());
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal((UnixFileMode)0b111_101_101, File.GetUnixFileMode(_binary));
        }
    }

    [Fact]
    public async Task An_older_build_on_the_server_is_installed_too()
    {
        _server.Serve("0.2.0", "server's build");

        var run = await RunAsync("0.3.0");

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal("server's build", await File.ReadAllTextAsync(_binary, Ct));
        Assert.Contains("from 0.3.0 to 0.2.0", run.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_version_changes_nothing()
    {
        _server.Serve("0.2.0", "new build");

        var run = await RunAsync("0.2.0");

        Assert.Equal(0, run.Exit);
        Assert.Contains("panopticon 0.2.0 is already up to date with https://panopticon.example.com", run.Stdout, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
        Assert.Equal(["/api/cli"], _server.Paths);
    }

    [Fact]
    public async Task A_checksum_mismatch_keeps_the_old_binary()
    {
        _server.Serve("0.2.0", "new build", sha256: new string('0', 64));

        var run = await RunAsync();

        Assert.Equal(1, run.Exit);
        Assert.Contains("Checksum mismatch", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed.", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task A_short_download_keeps_the_old_binary()
    {
        // No Content-Length, so only the body itself shows it's short.
        _server.Serve("0.2.0", "new build", size: 999, buildContent: bytes => new StreamContent(new TestBody(bytes)));

        var run = await RunAsync();

        Assert.Equal(1, run.Exit);
        Assert.Contains("Checksum mismatch", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task A_content_length_that_disagrees_with_the_manifest_is_not_downloaded()
    {
        _server.Serve("0.2.0", "new build", size: 999);

        var run = await RunAsync();

        Assert.Equal(1, run.Exit);
        Assert.Contains("is sending 9 bytes", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("its manifest says 999", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task A_body_longer_than_the_manifest_says_is_stopped_early()
    {
        // The manifest says 9 bytes; the body (with no Content-Length) would go on for a megabyte.
        var body = new TestBody(new byte[1024 * 1024]);
        _server.Serve("0.2.0", "new build", buildContent: _ => new StreamContent(body, bufferSize: 4096));

        var run = await RunAsync();

        Assert.Equal(1, run.Exit);
        Assert.Contains("ran past the 9 bytes the manifest says", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
        Assert.Empty(LeftoverTempFiles());
        Assert.True(body.Position < 1024 * 1024, $"the download read {body.Position} bytes: it should stop well before the end");
    }

    [Fact]
    public async Task A_download_that_breaks_off_is_a_download_failure_not_a_replace_failure()
    {
        _server.Serve("0.2.0", "new build", buildContent: bytes => new StreamContent(new TestBody(bytes[..4], fail: true)));

        var run = await RunAsync();

        Assert.Equal(1, run.Exit);
        Assert.Contains("broke off", run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Can't replace", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task A_server_that_times_out_is_reported_as_a_timeout()
    {
        _server.TimesOut();

        var run = await RunAsync();

        Assert.Equal(1, run.Exit);
        Assert.Contains("stopped answering, so the update timed out", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
    }

    [Fact]
    public async Task A_body_that_stalls_times_out_and_leaves_no_temp_file()
    {
        _server.Serve("0.2.0", "new build", buildContent: bytes => new StreamContent(new TestBody(bytes[..4], stall: true)));

        var run = await RunAsync();

        Assert.Equal(1, run.Exit);
        Assert.Contains("stopped answering, so the update timed out", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task Ctrl_c_during_the_download_exits_130_and_leaves_no_temp_file()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _server.Serve("0.2.0", "new build", buildContent: bytes => new StreamContent(new TestBody(bytes[..4], stall: true, onStall: cancel.Cancel)));

        var run = await RunAsync(cancellationToken: cancel.Token);

        Assert.Equal(UpdateCommand.CancelledExitCode, run.Exit);
        Assert.Contains("Update cancelled. Nothing was changed.", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task A_server_without_builds_changes_nothing()
    {
        _server.NoBuilds();

        var run = await RunAsync();

        Assert.Equal(1, run.Exit);
        Assert.Contains("has no CLI builds", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("make install", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
    }

    [Fact]
    public async Task No_build_for_this_platform_changes_nothing()
    {
        _server.Serve("0.2.0", "mac build", platform: CliPlatforms.OsxArm64);

        var run = await RunAsync();

        Assert.Equal(1, run.Exit);
        Assert.Contains("no CLI build for linux-x64 (it has: osx-arm64)", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
    }

    [Fact]
    public async Task An_unreachable_server_changes_nothing()
    {
        _server.Unreachable();

        var run = await RunAsync();

        Assert.Equal(1, run.Exit);
        Assert.Contains("Couldn't reach the Panopticon server at https://panopticon.example.com", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
    }

    [Fact]
    public async Task An_unwritable_directory_changes_nothing()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Skip("Needs Unix permissions, as a user other than root.");
            return;
        }

        _server.Serve("0.2.0", "new build");
        File.SetUnixFileMode(Path.GetDirectoryName(_binary)!, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var run = await RunAsync();

        Assert.Equal(1, run.Exit);
        Assert.Contains($"Can't replace {_binary}", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
    }

    [Fact]
    public async Task No_server_known_says_how_to_name_one()
    {
        var run = await RunAsync(args: []);

        Assert.Equal(1, run.Exit);
        Assert.Contains("--server", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("panopticon login", run.Stderr, StringComparison.Ordinal);
        Assert.Empty(_server.Paths);
    }

    [Fact]
    public async Task The_server_comes_from_the_environment_then_the_saved_login()
    {
        _server.Serve("0.2.0", "new build");
        Config.Write(new CliConfig("https://saved.example.com", "pnp_saved"));

        var fromSaved = await RunAsync("0.2.0", args: []);
        var fromEnvironment = await RunAsync("0.2.0", args: [], environment: new() { ["PANOPTICON_SERVER_URL"] = Server });

        Assert.Contains("https://saved.example.com", fromSaved.Stdout, StringComparison.Ordinal);
        Assert.Contains("https://panopticon.example.com", fromEnvironment.Stdout, StringComparison.Ordinal);
        Assert.All(_server.Requests, r => Assert.Null(r.Headers.Authorization));
    }

    [Fact]
    public async Task Windows_moves_the_running_binary_aside_then_moves_the_new_one_in()
    {
        _server.Serve("0.2.0", "new build");

        var run = await RunAsync(replacer: new BinaryReplacer(windows: true));

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal("new build", await File.ReadAllTextAsync(_binary, Ct));
        Assert.Equal("old build", await File.ReadAllTextAsync(BinaryReplacer.OldPath(_binary), Ct));

        BinaryReplacer.CleanUpAfterUpdate(_binary);

        Assert.False(File.Exists(BinaryReplacer.OldPath(_binary)));
    }

    [Fact]
    public async Task Windows_puts_the_old_binary_back_if_the_new_one_cant_move_in()
    {
        _server.Serve("0.2.0", "new build");
        var moves = 0;
        var replacer = new BinaryReplacer(windows: true, (from, to, overwrite) =>
        {
            if (++moves == 2)
            {
                throw new IOException("The process cannot access the file.");
            }

            File.Move(from, to, overwrite);
        });

        var run = await RunAsync(replacer: replacer);

        Assert.Equal(1, run.Exit);
        Assert.Contains("Nothing was changed.", run.Stderr, StringComparison.Ordinal);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
        Assert.False(File.Exists(BinaryReplacer.OldPath(_binary)));
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task A_plain_http_server_is_refused_before_any_request()
    {
        _server.Serve("0.2.0", "new build");

        var run = await RunAsync(args: ["--server", "http://panopticon.example.com"]);

        Assert.Equal(1, run.Exit);
        Assert.Contains("needs an https server", run.Stderr, StringComparison.Ordinal);
        Assert.Empty(_server.Paths);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
    }

    [Fact]
    public async Task A_plain_http_build_url_is_not_downloaded()
    {
        _server.Serve("0.2.0", "new build", buildUrlBase: "http://cdn.example.com");

        var run = await RunAsync();

        Assert.Equal(1, run.Exit);
        Assert.Contains("over plain http (http://cdn.example.com/cli/linux-x64/panopticon)", run.Stderr, StringComparison.Ordinal);
        Assert.Equal(["/api/cli"], _server.Paths);
        Assert.Equal("old build", await File.ReadAllTextAsync(_binary, Ct));
    }

    [Theory]
    [InlineData("http://localhost:7480")]
    [InlineData("http://127.0.0.1:7480")]
    [InlineData("http://[::1]:7480")]
    public async Task A_server_on_this_machine_may_use_http(string server)
    {
        _server.Serve("0.2.0", "new build", buildUrlBase: server);

        var run = await RunAsync(args: ["--server", server]);

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal("new build", await File.ReadAllTextAsync(_binary, Ct));
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet.exe")]
    [InlineData("testhost")]
    [InlineData("panopticon.dll")]
    public async Task A_host_running_the_cli_is_never_replaced(string host)
    {
        _server.Serve("0.2.0", "new build");
        var hostPath = Path.Combine(Path.GetDirectoryName(_binary)!, host);
        await File.WriteAllTextAsync(hostPath, "the host", Ct);

        var run = await RunAsync(binaryPath: hostPath);

        Assert.Equal(1, run.Exit);
        Assert.Contains($"running inside {hostPath}, not as the panopticon binary", run.Stderr, StringComparison.Ordinal);
        Assert.Empty(_server.Paths);
        Assert.Equal("the host", await File.ReadAllTextAsync(hostPath, Ct));
        Assert.Empty(LeftoverTempFiles());
    }

    [Theory]
    [InlineData("/usr/local/bin/panopticon", true)]
    [InlineData("/AppData/Local/Programs/panopticon/panopticon.exe", true)]
    [InlineData("/home/e/.dotnet/dotnet", false)]
    [InlineData("/src/bin/panopticon.dll", false)]
    [InlineData(null, false)]
    public void Only_the_panopticon_binary_counts_as_the_cli(string? path, bool expected) =>
        Assert.Equal(expected, BinaryReplacer.IsCliBinary(path));

    [Fact]
    public async Task An_unknown_option_is_a_usage_error()
    {
        var run = await RunAsync(args: ["--force"]);

        Assert.Equal(1, run.Exit);
        Assert.Contains("Usage: panopticon update [--server <url>]", run.Stderr, StringComparison.Ordinal);
    }

    private sealed record Run(int Exit, string Stdout, string Stderr);

    /// <summary>A fake Panopticon server: the manifest at /api/cli, and one build to download.</summary>
    private sealed class StubServer : HttpMessageHandler
    {
        private Func<HttpRequestMessage, HttpResponseMessage> _respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public List<HttpRequestMessage> Requests { get; } = [];

        public IEnumerable<string> Paths => Requests.Select(r => r.RequestUri!.AbsolutePath);

        public void Serve(string version, string content, string platform = Platform, string? sha256 = null, long? size = null, string buildUrlBase = Server, Func<byte[], HttpContent>? buildContent = null)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var file = CliPlatforms.FileName(platform);
            var manifest = new CliManifest(
                version,
                $"{Server}/install.sh",
                $"{Server}/install.ps1",
                new Dictionary<string, CliPlatformBuild>
                {
                    [platform] = new($"{buildUrlBase}/cli/{platform}/{file}", size ?? bytes.Length, sha256 ?? Convert.ToHexStringLower(SHA256.HashData(bytes))),
                });
            var json = JsonSerializer.Serialize(manifest, ContractsJsonContext.Default.CliManifest);
            _respond = request => request.RequestUri!.AbsolutePath switch
            {
                "/api/cli" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") },
                var path when path == $"/cli/{platform}/{file}" => new HttpResponseMessage(HttpStatusCode.OK) { Content = buildContent?.Invoke(bytes) ?? new ByteArrayContent(bytes) },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        public void NoBuilds() =>
            _respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"status":404,"detail":"This server has no CLI builds. Build the CLI from source instead: clone the repo and run make install."}""",
                    Encoding.UTF8,
                    "application/problem+json"),
            };

        public void Unreachable() => _respond = _ => throw new HttpRequestException("Connection refused.");

        /// <summary>What HttpClient throws when its Timeout passes before the headers arrive.</summary>
        public void TimesOut() =>
            _respond = _ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException());

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    /// <summary>
    /// A response body with no length (it can't seek), which hands out <c>content</c> and then ends, breaks off as a
    /// dropped connection does, or stalls until it's cancelled.
    /// </summary>
    private sealed class TestBody(byte[] content, bool fail = false, bool stall = false, Action? onStall = null) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < content.Length)
            {
                var count = Math.Min(buffer.Length, content.Length - _position);
                content.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }

            if (fail)
            {
                throw new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely.");
            }

            if (stall)
            {
                onStall?.Invoke();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
