using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;
using Panopticon.Contracts;

namespace Panopticon.Cli.Update;

/// <summary>
/// <c>panopticon update [--server &lt;url&gt;]</c> (spec cli-distribution: Self-update, Safe self-update; design D6).
/// It installs the build the server serves for this platform, so the CLI matches the server it reports to, whether
/// that's newer or older. It needs no token. Whatever goes wrong, the binary already there keeps working.
/// </summary>
internal sealed class UpdateCommand(
    Func<HttpClient> createHttpClient,
    Func<string, string?> getEnvironmentVariable,
    CliConfigFile config,
    string currentVersion,
    string platform,
    string? binaryPath,
    BinaryReplacer replacer,
    TextWriter stdout,
    TextWriter stderr,
    TimeSpan? stallTimeout = null)
{
    /// <summary>
    /// How long a download may go without a byte before it's given up. <c>HttpClient.Timeout</c> stops at the headers
    /// once a body is streamed, so the body needs its own limit.
    /// </summary>
    private readonly TimeSpan _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(30);

    /// <summary>The exit code for Ctrl+C, as for <c>panopticon login</c>.</summary>
    public const int CancelledExitCode = 130;

    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!ServerAddress.TryParseArguments("update", args, out var serverArgument, out var usageError))
        {
            await stderr.WriteLineAsync(usageError);
            return 1;
        }

        var server = ServerAddress.Resolve(serverArgument, getEnvironmentVariable, config, stderr);
        if (server is null)
        {
            await stderr.WriteLineAsync(
                "No server to update from: pass --server <url>, or run 'panopticon login' first.");
            return 1;
        }

        if (!ServerAddress.TryRoot(server, out var root))
        {
            await stderr.WriteLineAsync($"The server must be an absolute http or https URL, not '{server}'.");
            return 1;
        }

        if (!IsSecure(root))
        {
            await stderr.WriteLineAsync(
                $"panopticon update needs an https server, not {BridgeSettings.Display(root)}: over plain http, anyone on the way could swap the build and its checksum. Nothing was changed.");
            return 1;
        }

        if (string.IsNullOrEmpty(binaryPath))
        {
            await stderr.WriteLineAsync("Can't tell where this panopticon binary is, so it can't replace itself.");
            return 1;
        }

        // Run as `dotnet panopticon.dll update`, the process is the dotnet host: replacing it would break .NET itself.
        if (!BinaryReplacer.IsCliBinary(binaryPath))
        {
            await stderr.WriteLineAsync(
                $"This CLI is running inside {binaryPath}, not as the panopticon binary, so update won't replace it. Run update from an installed panopticon, or rebuild from source. Nothing was changed.");
            return 1;
        }

        var display = BridgeSettings.Display(root);
        using var http = createHttpClient();
        try
        {
            return await UpdateAsync(http, root, display, binaryPath, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            await stderr.WriteLineAsync($"Couldn't reach the Panopticon server at {display}: {ex.Message} Nothing was changed.");
            return 1;
        }
        catch (JsonException)
        {
            await stderr.WriteLineAsync($"The server at {display} didn't answer like a Panopticon server. Check the URL. Nothing was changed.");
            return 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await stderr.WriteLineAsync("Update cancelled. Nothing was changed.");
            return CancelledExitCode;
        }
        catch (OperationCanceledException)
        {
            // HttpClient's timeout on the headers, or the stall timeout on the body.
            await stderr.WriteLineAsync($"The server at {display} stopped answering, so the update timed out. Nothing was changed.");
            return 1;
        }
    }

    private async Task<int> UpdateAsync(HttpClient http, Uri root, string display, string binaryPath, CancellationToken cancellationToken)
    {
        CliManifest manifest;
        using (var response = await http.GetAsync(new Uri(root, CliPlatforms.ManifestPath.TrimStart('/')), cancellationToken))
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                await stderr.WriteLineAsync($"The server at {display} has no CLI builds: {await ServerAddress.ProblemAsync(response, cancellationToken)} Nothing was changed.");
                return 1;
            }

            if (!response.IsSuccessStatusCode)
            {
                await stderr.WriteLineAsync($"The server at {display} refused: {await ServerAddress.ProblemAsync(response, cancellationToken)} Nothing was changed.");
                return 1;
            }

            manifest = await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CliManifest, cancellationToken)
                ?? throw new JsonException();
        }

        if (string.Equals(manifest.Version, currentVersion, StringComparison.Ordinal))
        {
            await stdout.WriteLineAsync($"panopticon {currentVersion} is already up to date with {display}.");
            return 0;
        }

        if (manifest.Platforms is null || !manifest.Platforms.TryGetValue(platform, out var build))
        {
            var available = manifest.Platforms is { Count: > 0 } ? string.Join(", ", manifest.Platforms.Keys.Order(StringComparer.Ordinal)) : "none";
            await stderr.WriteLineAsync($"The server at {display} has no CLI build for {platform} (it has: {available}). Nothing was changed.");
            return 1;
        }

        if (!Uri.TryCreate(build.Url, UriKind.Absolute, out var buildUrl)
            || (buildUrl.Scheme != Uri.UriSchemeHttp && buildUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new JsonException();
        }

        if (!IsSecure(buildUrl))
        {
            await stderr.WriteLineAsync($"The server at {display} offers this build over plain http ({buildUrl}), so it wasn't downloaded. Nothing was changed.");
            return 1;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(binaryPath))!;
        var temp = Path.Combine(directory, ".panopticon-update-" + Path.GetRandomFileName());
        try
        {
            if (!await DownloadAsync(http, buildUrl, temp, build, display, cancellationToken))
            {
                return 1;
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, (UnixFileMode)0b111_101_101);
            }

            replacer.Replace(temp, binaryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await stderr.WriteLineAsync($"Can't replace {binaryPath}: {ex.Message} Nothing was changed.");
            return 1;
        }
        finally
        {
            BinaryReplacer.DeleteQuietly(temp);
        }

        await stdout.WriteLineAsync($"Updated panopticon from {currentVersion} to {manifest.Version}.");
        await stdout.WriteLineAsync("Restart your agent so its panopticon mcp bridge runs the new version.");
        return 0;
    }

    /// <summary>
    /// Streams the build to <paramref name="temp"/>, hashing it as it goes. It stops as soon as the body is longer than
    /// the manifest says, and gives up on a body that stalls. A failure on the network side is reported here; one
    /// writing the file comes out as an exception, for the caller to report as a file problem.
    /// </summary>
    private async Task<bool> DownloadAsync(HttpClient http, Uri url, string temp, CliPlatformBuild build, string display, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await stderr.WriteLineAsync($"Couldn't download {url} from {display}: {(int)response.StatusCode} {response.StatusCode}. Nothing was changed.");
            return false;
        }

        if (response.Content.Headers.ContentLength is { } length && length != build.Size)
        {
            await stderr.WriteLineAsync(
                $"The server at {display} is sending {length} bytes for {url}, but its manifest says {build.Size}, so it wasn't downloaded. Nothing was changed.");
            return false;
        }

        long size;
        byte[] hash;
        await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            size = 0;
            while (true)
            {
                int read;
                try
                {
                    read = await ReadWithinStallTimeoutAsync(body, buffer, cancellationToken);
                }
                catch (IOException ex)
                {
                    await stderr.WriteLineAsync($"The download of {url} from {display} broke off: {ex.Message} Nothing was changed.");
                    return false;
                }

                if (read == 0)
                {
                    break;
                }

                size += read;
                if (size > build.Size)
                {
                    await stderr.WriteLineAsync(
                        $"The download of {url} ran past the {build.Size} bytes the manifest says, so it was stopped. Nothing was changed.");
                    return false;
                }

                sha256.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            hash = sha256.GetHashAndReset();
        }

        var actual = Convert.ToHexStringLower(hash);
        if (size != build.Size || !string.Equals(actual, build.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            await stderr.WriteLineAsync(
                $"Checksum mismatch for {url}: expected {build.Sha256} ({build.Size} bytes), got {actual} ({size} bytes). Nothing was changed.");
            return false;
        }

        return true;
    }

    /// <summary>One read of the body, given up (as a cancellation the caller didn't ask for) after the stall timeout.</summary>
    private async Task<int> ReadWithinStallTimeoutAsync(Stream body, byte[] buffer, CancellationToken cancellationToken)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(_stallTimeout);
        return await body.ReadAsync(buffer, stall.Token);
    }

    /// <summary>https, or http to this machine only (spec: Updates only over HTTPS).</summary>
    internal static bool IsSecure(Uri url) => url.Scheme == Uri.UriSchemeHttps || url.IsLoopback;

}
