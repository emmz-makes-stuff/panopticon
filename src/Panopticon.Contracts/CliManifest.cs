namespace Panopticon.Contracts;

// The CLI builds a server hands out (cli-install design D4). The server answers GET /api/cli with a CliManifest, and
// `panopticon update` reads it through ContractsJsonContext.

/// <summary>
/// Answer to <c>GET /api/cli</c>: the version of the CLI builds this server serves, its install scripts, and each
/// platform's build. Every URL is absolute and built on the server's public URL.
/// </summary>
public sealed record CliManifest(
    string Version,
    string InstallSh,
    string InstallPs1,
    IReadOnlyDictionary<string, CliPlatformBuild> Platforms);

/// <summary>One platform's build: where to download it, and the size and lower-case hex SHA-256 to check it against.</summary>
public sealed record CliPlatformBuild(string Url, long Size, string Sha256);

/// <summary>The platforms (.NET runtime identifiers) a release builds the CLI for, and each one's file name.</summary>
public static class CliPlatforms
{
    public const string LinuxX64 = "linux-x64";
    public const string LinuxArm64 = "linux-arm64";
    public const string OsxArm64 = "osx-arm64";
    public const string WinX64 = "win-x64";

    /// <summary>Every supported platform.</summary>
    public static IReadOnlyList<string> All { get; } = [LinuxX64, LinuxArm64, OsxArm64, WinX64];

    /// <summary>The path of the manifest on a server.</summary>
    public const string ManifestPath = "/api/cli";

    /// <summary>The CLI's file name on a platform: <c>panopticon.exe</c> on Windows, <c>panopticon</c> elsewhere.</summary>
    public static string FileName(string platform) =>
        platform.StartsWith("win-", StringComparison.Ordinal) ? "panopticon.exe" : "panopticon";
}
