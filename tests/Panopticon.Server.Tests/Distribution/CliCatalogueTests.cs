using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Panopticon.Contracts;
using Panopticon.Server.Distribution;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Distribution;

// Spec cli-distribution: Version manifest, Server without CLI builds (design D1).
public class CliCatalogueTests
{
    private static CliCatalogue Load(string directory) => CliCatalogue.Load(directory, NullLogger.Instance);

    [Fact]
    public void A_full_directory_has_every_platform_with_its_size_and_sha256()
    {
        using var builds = CliBuildsDirectory.Create("0.2.0");

        var catalogue = Load(builds.Path);

        Assert.False(catalogue.IsEmpty);
        Assert.Equal("0.2.0", catalogue.Version);
        Assert.Equal(CliPlatforms.All.Order(), catalogue.Builds.Keys.Order());
        foreach (var platform in CliPlatforms.All)
        {
            var build = catalogue.Builds[platform];
            Assert.Equal(CliPlatforms.FileName(platform), build.FileName);
            Assert.Equal(builds.Bytes(platform).Length, build.Size);
            Assert.Equal(builds.Sha256(platform), build.Sha256);
        }

        Assert.Equal("panopticon.exe", catalogue.Builds[CliPlatforms.WinX64].FileName);
        Assert.Equal("panopticon", catalogue.Builds[CliPlatforms.LinuxX64].FileName);
    }

    [Fact]
    public void Platforms_that_are_missing_are_left_out()
    {
        using var builds = CliBuildsDirectory.Create("0.2.0", [CliPlatforms.LinuxArm64]);

        var catalogue = Load(builds.Path);

        Assert.Equal([CliPlatforms.LinuxArm64], catalogue.Builds.Keys);
    }

    [Fact]
    public void An_unreadable_build_is_logged_and_left_out_rather_than_failing_start_up()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Skip("Needs Unix permissions, as a user other than root.");
            return;
        }

        using var builds = CliBuildsDirectory.Create("0.2.0");
        var unreadable = Path.Combine(builds.Path, CliPlatforms.OsxArm64, "panopticon");
        File.SetUnixFileMode(unreadable, UnixFileMode.None);
        var logger = new ListLogger<CliCatalogue>();

        var catalogue = CliCatalogue.Load(builds.Path, logger);

        Assert.False(catalogue.IsEmpty);
        Assert.DoesNotContain(CliPlatforms.OsxArm64, catalogue.Builds.Keys);
        Assert.Equal(3, catalogue.Builds.Count);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(unreadable, StringComparison.Ordinal));
    }

    [Fact]
    public void Only_unreadable_builds_is_empty()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Skip("Needs Unix permissions, as a user other than root.");
            return;
        }

        using var builds = CliBuildsDirectory.Create("0.2.0", [CliPlatforms.LinuxX64]);
        File.SetUnixFileMode(Path.Combine(builds.Path, CliPlatforms.LinuxX64, "panopticon"), UnixFileMode.None);

        Assert.True(Load(builds.Path).IsEmpty);
    }

    [Fact]
    public void A_missing_directory_is_empty()
    {
        var catalogue = Load(Path.Combine(Path.GetTempPath(), "panopticon-no-such-dir-" + Guid.NewGuid().ToString("N")));

        Assert.True(catalogue.IsEmpty);
        Assert.Null(catalogue.Version);
        Assert.Empty(catalogue.Builds);
    }

    [Fact]
    public void No_version_file_is_empty()
    {
        using var builds = CliBuildsDirectory.Create(version: null);

        Assert.True(Load(builds.Path).IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0.2.0; rm -rf /")]
    [InlineData("$(id)")]
    public void A_version_that_isnt_a_plain_version_is_empty(string version)
    {
        using var builds = CliBuildsDirectory.Create(version);

        Assert.True(Load(builds.Path).IsEmpty);
    }

    [Fact]
    public void A_version_with_no_builds_is_empty()
    {
        using var builds = CliBuildsDirectory.Create("0.2.0", []);

        Assert.True(Load(builds.Path).IsEmpty);
    }

    [Fact]
    public void An_unexpected_platform_or_file_name_is_ignored()
    {
        using var builds = CliBuildsDirectory.Create("0.2.0", [CliPlatforms.LinuxX64]);
        Directory.CreateDirectory(Path.Combine(builds.Path, "freebsd-x64"));
        File.WriteAllText(Path.Combine(builds.Path, "freebsd-x64", "panopticon"), "nope");
        Directory.CreateDirectory(Path.Combine(builds.Path, CliPlatforms.WinX64));
        File.WriteAllText(Path.Combine(builds.Path, CliPlatforms.WinX64, "panopticon"), "no .exe");

        var catalogue = Load(builds.Path);

        Assert.Equal([CliPlatforms.LinuxX64], catalogue.Builds.Keys);
    }
}
