using System.Security.Cryptography;
using System.Text;
using Panopticon.Contracts;

namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>
/// A temporary CLI builds directory laid out as the image's <c>cli/</c> is (cli-install design D1): a <c>version</c>
/// file and <c>&lt;platform&gt;/&lt;file&gt;</c> for each build. Point a server at it with
/// <see cref="Panopticon.Server.Distribution.CliDistributionOptions.ConfigKey"/>.
/// </summary>
public sealed class CliBuildsDirectory : IDisposable
{
    private CliBuildsDirectory(string path) => Path = path;

    public string Path { get; }

    /// <summary>A directory with <paramref name="version"/> and a small, distinct fake build for each platform.</summary>
    public static CliBuildsDirectory Create(string? version = "0.2.0", IEnumerable<string>? platforms = null)
    {
        var dir = new CliBuildsDirectory(Directory.CreateTempSubdirectory("panopticon-cli-builds-").FullName);
        if (version is not null)
        {
            File.WriteAllText(System.IO.Path.Combine(dir.Path, "version"), version + "\n");
        }

        foreach (var platform in platforms ?? CliPlatforms.All)
        {
            dir.WriteBuild(platform, Encoding.UTF8.GetBytes($"fake {platform} build {version}\n"));
        }

        return dir;
    }

    /// <summary>Writes (or replaces) one platform's build, and returns its path.</summary>
    public string WriteBuild(string platform, byte[] bytes)
    {
        var folder = Directory.CreateDirectory(System.IO.Path.Combine(Path, platform)).FullName;
        var file = System.IO.Path.Combine(folder, CliPlatforms.FileName(platform));
        File.WriteAllBytes(file, bytes);
        return file;
    }

    public byte[] Bytes(string platform) =>
        File.ReadAllBytes(System.IO.Path.Combine(Path, platform, CliPlatforms.FileName(platform)));

    public string Sha256(string platform) => Convert.ToHexStringLower(SHA256.HashData(Bytes(platform)));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is harmless.
        }
    }
}
