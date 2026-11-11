using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Panopticon.Contracts;

namespace Panopticon.Server.Distribution;

/// <summary>Where the server finds the CLI builds it hands out (cli-install design D1).</summary>
public sealed class CliDistributionOptions
{
    public const string ConfigKey = "Panopticon:CliDirectory";

    /// <summary>The directory holding <c>version</c> and <c>&lt;platform&gt;/&lt;file&gt;</c>; by default <c>cli</c> next to the server.</summary>
    public string Directory { get; set; } = Path.Combine(AppContext.BaseDirectory, "cli");
}

/// <summary>One platform's CLI build on disk, with the size and SHA-256 of the bytes served.</summary>
public sealed record CliBuild(string Platform, string FileName, string FilePath, long Size, string Sha256);

/// <summary>
/// The CLI builds this server serves, read once at start-up (design D1): the <c>version</c> file and a
/// <c>&lt;platform&gt;/&lt;file&gt;</c> for each supported platform that's present. With no directory, no readable
/// version or no builds at all, it's empty: the server has no CLI builds, and keeps working.
/// </summary>
public sealed partial class CliCatalogue
{
    /// <summary>What the CLI endpoints and install scripts say when there's nothing to hand out.</summary>
    public const string NoBuildsMessage =
        "This server has no CLI builds. Build the CLI from source instead: clone the repo and run make install.";

    private CliCatalogue(string? version, IReadOnlyDictionary<string, CliBuild> builds)
    {
        Version = version;
        Builds = builds;
    }

    public static CliCatalogue Empty { get; } = new(null, new Dictionary<string, CliBuild>());

    /// <summary>The release version, e.g. <c>0.2.0</c>; null when the catalogue is empty.</summary>
    public string? Version { get; }

    /// <summary>The builds present, by platform.</summary>
    public IReadOnlyDictionary<string, CliBuild> Builds { get; }

    public bool IsEmpty => Version is null;

    /// <summary>Scans <paramref name="directory"/> and hashes every build in it.</summary>
    public static CliCatalogue Load(string directory, ILogger logger)
    {
        var versionFile = Path.Combine(directory, "version");
        if (!File.Exists(versionFile))
        {
            LogNoBuilds(logger, directory, "no version file");
            return Empty;
        }

        // The version goes into URLs and the install scripts, so it must be a plain version string.
        var version = File.ReadAllText(versionFile).Trim();
        if (!VersionPattern().IsMatch(version))
        {
            LogNoBuilds(logger, directory, "the version file doesn't hold a version");
            return Empty;
        }

        var builds = new Dictionary<string, CliBuild>(StringComparer.Ordinal);
        foreach (var platform in CliPlatforms.All)
        {
            var fileName = CliPlatforms.FileName(platform);
            var path = Path.Combine(directory, platform, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            // An unreadable build is left out, never a reason not to start (spec: Server without CLI builds).
            try
            {
                using var stream = File.OpenRead(path);
                var sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
                builds[platform] = new CliBuild(platform, fileName, path, stream.Length, sha256);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogUnreadableBuild(logger, ex, path);
            }
        }

        if (builds.Count == 0)
        {
            LogNoBuilds(logger, directory, "no platform builds");
            return Empty;
        }

        LogLoaded(logger, version, builds.Keys, directory);
        return new CliCatalogue(version, builds);
    }

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.-]{0,63}$")]
    private static partial Regex VersionPattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "No CLI builds in {Directory} ({Reason}): the server won't hand out the CLI.")]
    private static partial void LogNoBuilds(ILogger logger, string directory, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't read the CLI build {Path}: the server won't hand it out.")]
    private static partial void LogUnreadableBuild(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "CLI builds {Version} for {Platforms}, from {Directory}.")]
    private static partial void LogLoaded(ILogger logger, string version, IEnumerable<string> platforms, string directory);
}
