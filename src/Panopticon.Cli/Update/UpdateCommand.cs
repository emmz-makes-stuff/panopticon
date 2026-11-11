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
    TextWriter stderr)
{
    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!TryParse(args, out var serverArgument, out var usageError))
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

        if (string.IsNullOrEmpty(binaryPath))
        {
            await stderr.WriteLineAsync("Can't tell where this panopticon binary is, so it can't replace itself.");
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
            TryDelete(temp);
        }

        await stdout.WriteLineAsync($"Updated panopticon from {currentVersion} to {manifest.Version}.");
        await stdout.WriteLineAsync("Restart your agent so its panopticon mcp bridge runs the new version.");
        return 0;
    }

    /// <summary>Streams the build to <paramref name="temp"/>, checking its size and SHA-256 as it goes.</summary>
    private async Task<bool> DownloadAsync(HttpClient http, Uri url, string temp, CliPlatformBuild build, string display, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await stderr.WriteLineAsync($"Couldn't download {url} from {display}: {(int)response.StatusCode} {response.StatusCode}. Nothing was changed.");
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
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                sha256.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                size += read;
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

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool TryParse(IReadOnlyList<string> args, out string? server, out string error)
    {
        server = null;
        error = string.Empty;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--server" when i + 1 < args.Count:
                    server = args[++i];
                    break;
                case "--server":
                    error = "--server needs a URL: 'panopticon update --server <url>'.";
                    return false;
                default:
                    error = $"Unknown option '{args[i]}'. Usage: panopticon update [--server <url>]";
                    return false;
            }
        }

        return true;
    }
}
