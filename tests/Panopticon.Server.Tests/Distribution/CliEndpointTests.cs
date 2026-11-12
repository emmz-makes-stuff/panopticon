using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Panopticon.Contracts;
using Panopticon.Server.Auth;
using Panopticon.Server.Distribution;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Distribution;

// Spec cli-distribution: Version manifest, Binary downloads, Server without CLI builds (design D4).
public class CliEndpointTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PanopticonFactory> FactoryAsync(string? cliDirectory, string publicUrl = PanopticonFactory.PublicUrl)
    {
        // A web root with an app shell, so a route that fell through to the fallback would show as index.html.
        var webRoot = Directory.CreateTempSubdirectory("panopticon-webroot-").FullName;
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>shell</title>", Ct);
        var settings = new Dictionary<string, string> { [PublicUrlOptions.ConfigKey] = publicUrl };
        if (cliDirectory is not null)
        {
            settings[CliDistributionOptions.ConfigKey] = cliDirectory;
        }

        return new PanopticonFactory(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct))
        {
            WebRoot = webRoot,
            Settings = settings,
        };
    }

    [Fact]
    public async Task Manifest_lists_the_version_scripts_and_every_platform_without_sign_in()
    {
        using var builds = CliBuildsDirectory.Create("0.2.0");
        await using var factory = await FactoryAsync(builds.Path, "https://panopticon.example.com");
        using var http = factory.CreateClient();

        using var response = await http.GetAsync(CliPlatforms.ManifestPath, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var manifest = (await response.Content.ReadFromJsonAsync<CliManifest>(Ct))!;
        Assert.Equal("0.2.0", manifest.Version);
        Assert.Equal("https://panopticon.example.com/install.sh", manifest.InstallSh);
        Assert.Equal("https://panopticon.example.com/install.ps1", manifest.InstallPs1);
        Assert.Equal(CliPlatforms.All.Order(), manifest.Platforms.Keys.Order());
        var linux = manifest.Platforms[CliPlatforms.LinuxX64];
        Assert.Equal("https://panopticon.example.com/cli/linux-x64/panopticon", linux.Url);
        Assert.Equal(builds.Bytes(CliPlatforms.LinuxX64).Length, linux.Size);
        Assert.Equal(builds.Sha256(CliPlatforms.LinuxX64), linux.Sha256);
        Assert.Equal("https://panopticon.example.com/cli/win-x64/panopticon.exe", manifest.Platforms[CliPlatforms.WinX64].Url);
    }

    [Fact]
    public async Task Manifest_urls_are_built_on_the_public_url_not_the_host()
    {
        using var builds = CliBuildsDirectory.Create();
        await using var factory = await FactoryAsync(builds.Path, "https://panopticon.example.com/");
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, CliPlatforms.ManifestPath);
        request.Headers.Host = "evil.example.net";

        using var response = await http.SendAsync(request, Ct);

        var manifest = (await response.Content.ReadFromJsonAsync<CliManifest>(Ct))!;
        string[] urls = [manifest.InstallSh, manifest.InstallPs1, .. manifest.Platforms.Values.Select(p => p.Url)];
        Assert.All(urls, url => Assert.StartsWith("https://panopticon.example.com/", url, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("linux-x64", "panopticon")]
    [InlineData("linux-arm64", "panopticon")]
    [InlineData("osx-arm64", "panopticon")]
    [InlineData("win-x64", "panopticon.exe")]
    public async Task Each_build_downloads_as_its_bytes(string platform, string file)
    {
        using var builds = CliBuildsDirectory.Create();
        await using var factory = await FactoryAsync(builds.Path);
        using var http = factory.CreateClient();

        using var response = await http.GetAsync($"/cli/{platform}/{file}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(file, response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        Assert.True(response.Headers.CacheControl?.NoCache, "a download must be revalidated: its URL carries no version");
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal(builds.Sha256(platform), Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Theory]
    [InlineData("/cli/freebsd-x64/panopticon")]
    [InlineData("/cli/linux-x64/panopticon.exe")]
    [InlineData("/cli/win-x64/panopticon")]
    [InlineData("/cli/linux-x64/version")]
    public async Task An_unknown_platform_or_file_is_a_404_not_the_app_shell(string path)
    {
        using var builds = CliBuildsDirectory.Create();
        await using var factory = await FactoryAsync(builds.Path);
        using var http = factory.CreateClient();

        using var response = await http.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("<title>shell</title>", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    // Spec cli-distribution: CLI endpoints answer HEAD (onboarding-guide D6).
    [Theory]
    [InlineData("/cli/osx-arm64/panopticon", HttpStatusCode.OK, "application/octet-stream")]
    [InlineData("/cli/freebsd-x64/panopticon", HttpStatusCode.NotFound, "text/plain")]
    [InlineData("/install.sh", HttpStatusCode.OK, "text/plain")]
    [InlineData("/install.ps1", HttpStatusCode.OK, "text/plain")]
    [InlineData("/api/cli", HttpStatusCode.OK, "application/json")]
    public async Task Head_answers_like_get_and_never_with_the_app_shell(string path, HttpStatusCode status, string mediaType)
    {
        // Over real Kestrel, as in production: it's Kestrel that sends no body for HEAD, which the in-memory test server doesn't.
        using var builds = CliBuildsDirectory.Create();
        await using var factory = await FactoryAsync(builds.Path);
        var port = FreePort.Next();
        factory.UseKestrel(port);
        factory.StartServer();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        using var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, path), Ct);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task Head_on_a_build_gives_its_length()
    {
        using var builds = CliBuildsDirectory.Create();
        await using var factory = await FactoryAsync(builds.Path);
        using var http = factory.CreateClient();

        using var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/cli/osx-arm64/panopticon"), Ct);

        Assert.Equal(builds.Bytes(CliPlatforms.OsxArm64).Length, response.Content.Headers.ContentLength);
    }

    // Spec cli-distribution: Build downloads carry validators (onboarding-guide D7).
    [Fact]
    public async Task A_download_carries_its_sha256_as_etag_and_a_matching_revalidation_is_a_304()
    {
        using var builds = CliBuildsDirectory.Create();
        await using var factory = await FactoryAsync(builds.Path);
        using var http = factory.CreateClient();

        using var first = await http.GetAsync("/cli/linux-x64/panopticon", Ct);
        Assert.Equal($"\"{builds.Sha256(CliPlatforms.LinuxX64)}\"", first.Headers.ETag?.Tag);
        // The build file's own write time, never File.GetLastWriteTimeUtc's 1601 for a file it can't find.
        Assert.True(first.Content.Headers.LastModified > new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.True(first.Headers.CacheControl?.NoCache);

        using var again = new HttpRequestMessage(HttpMethod.Get, "/cli/linux-x64/panopticon");
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var revalidated = await http.SendAsync(again, Ct);

        Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);
        Assert.Empty(await revalidated.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task A_different_build_gets_a_different_etag()
    {
        using var before = CliBuildsDirectory.Create();
        using var after = CliBuildsDirectory.Create();
        after.WriteBuild(CliPlatforms.LinuxX64, "a rebuilt linux-x64 build\n"u8.ToArray());
        await using var oldServer = await FactoryAsync(before.Path);
        await using var newServer = await FactoryAsync(after.Path);

        using var oldResponse = await oldServer.CreateClient().GetAsync("/cli/linux-x64/panopticon", Ct);
        using var newResponse = await newServer.CreateClient().GetAsync("/cli/linux-x64/panopticon", Ct);

        Assert.NotEqual(oldResponse.Headers.ETag?.Tag, newResponse.Headers.ETag?.Tag);
        Assert.Equal($"\"{after.Sha256(CliPlatforms.LinuxX64)}\"", newResponse.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task A_missing_platform_is_a_404()
    {
        using var builds = CliBuildsDirectory.Create("0.2.0", [CliPlatforms.LinuxArm64]);
        await using var factory = await FactoryAsync(builds.Path);
        using var http = factory.CreateClient();

        using var response = await http.GetAsync("/cli/osx-arm64/panopticon", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/cli")]
    [InlineData("/install.sh")]
    [InlineData("/install.ps1")]
    [InlineData("/cli/linux-x64/panopticon")]
    public async Task A_server_without_builds_says_so_with_a_404(string path)
    {
        await using var factory = await FactoryAsync(cliDirectory: null);
        using var http = factory.CreateClient();

        using var response = await http.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("no CLI builds", body, StringComparison.Ordinal);
        Assert.Contains("make install", body, StringComparison.Ordinal);
        Assert.DoesNotContain("<title>shell</title>", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_install_script_downloads_only_from_its_own_server()
    {
        using var builds = CliBuildsDirectory.Create();
        await using var factory = await FactoryAsync(builds.Path, "https://panopticon.example.com/");
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, CliEndpoints.InstallShPath);
        request.Headers.Host = "evil.example.net";

        using var response = await http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var script = await response.Content.ReadAsStringAsync(Ct);
        var urls = System.Text.RegularExpressions.Regex.Matches(script, "https?://[^\\s'\"]+").Select(m => m.Value).ToList();
        Assert.NotEmpty(urls);
        Assert.All(urls, url => Assert.StartsWith("https://panopticon.example.com", url, StringComparison.Ordinal));
        Assert.Contains("server='https://panopticon.example.com'", script, StringComparison.Ordinal);
        Assert.Contains("    linux-x64) url='https://panopticon.example.com/cli/linux-x64/panopticon'; sha256='" + builds.Sha256(CliPlatforms.LinuxX64) + "' ;;", script, StringComparison.Ordinal);
        Assert.DoesNotContain("win-x64)", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_windows_script_names_its_server_and_the_win_x64_build()
    {
        using var builds = CliBuildsDirectory.Create();
        await using var factory = await FactoryAsync(builds.Path, "https://panopticon.example.com/");
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, CliEndpoints.InstallPs1Path);
        request.Headers.Host = "evil.example.net";

        using var response = await http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache);
        var script = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("@@", script, StringComparison.Ordinal);
        Assert.Contains("$server = 'https://panopticon.example.com'", script, StringComparison.Ordinal);
        Assert.Contains("$url = 'https://panopticon.example.com/cli/win-x64/panopticon.exe'", script, StringComparison.Ordinal);
        Assert.Contains("$sha256 = '" + builds.Sha256(CliPlatforms.WinX64) + "'", script, StringComparison.Ordinal);
        Assert.Contains("panopticon login --server $server", script, StringComparison.Ordinal);
        Assert.Contains("Programs\\panopticon", script, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example.net", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_windows_script_without_a_windows_build_says_so_when_run()
    {
        using var builds = CliBuildsDirectory.Create("0.2.0", [CliPlatforms.LinuxX64]);
        await using var factory = await FactoryAsync(builds.Path);
        using var http = factory.CreateClient();

        var script = await http.GetStringAsync(CliEndpoints.InstallPs1Path, Ct);

        Assert.Contains("$url = ''", script, StringComparison.Ordinal);
        Assert.Contains("It has CLI builds for: linux-x64.", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_without_builds_still_serves_the_app()
    {
        await using var factory = await FactoryAsync(cliDirectory: null);
        using var http = factory.CreateClient();

        using var response = await http.GetAsync("/healthz", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
