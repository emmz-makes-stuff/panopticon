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
        await using var factory = await FactoryAsync(builds.Path, "https://panopticon.team");
        using var http = factory.CreateClient();

        using var response = await http.GetAsync(CliPlatforms.ManifestPath, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var manifest = (await response.Content.ReadFromJsonAsync<CliManifest>(Ct))!;
        Assert.Equal("0.2.0", manifest.Version);
        Assert.Equal("https://panopticon.team/install.sh", manifest.InstallSh);
        Assert.Equal("https://panopticon.team/install.ps1", manifest.InstallPs1);
        Assert.Equal(CliPlatforms.All.Order(), manifest.Platforms.Keys.Order());
        var linux = manifest.Platforms[CliPlatforms.LinuxX64];
        Assert.Equal("https://panopticon.team/cli/linux-x64/panopticon", linux.Url);
        Assert.Equal(builds.Bytes(CliPlatforms.LinuxX64).Length, linux.Size);
        Assert.Equal(builds.Sha256(CliPlatforms.LinuxX64), linux.Sha256);
        Assert.Equal("https://panopticon.team/cli/win-x64/panopticon.exe", manifest.Platforms[CliPlatforms.WinX64].Url);
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
