using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Cli.Config;

/// <summary>What <c>panopticon login</c> saves: the server it logged in to and the API token it received.</summary>
internal sealed record SavedLogin(string? ServerUrl, string? Token);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(SavedLogin))]
internal sealed partial class CliJsonContext : JsonSerializerContext;

/// <summary>
/// The CLI's config file (team-auth design D14). It's written atomically, through a temp file next to it, and on Unix
/// it's readable by the user only: the file is 0600 and its directory 0700, set before anything is written.
/// </summary>
internal sealed class CliConfigFile(string path)
{
    private const UnixFileMode UserOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode UserOnlyDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public string Path { get; } = path;

    /// <summary>
    /// <c>%APPDATA%\panopticon\config.json</c> on Windows; elsewhere <c>$XDG_CONFIG_HOME/panopticon/config.json</c>,
    /// with <c>~/.config</c> when <c>XDG_CONFIG_HOME</c> is unset.
    /// </summary>
    public static string DefaultPath(Func<string, string?> getEnvironmentVariable, bool isWindows, string homeDirectory)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        string root;
        if (isWindows)
        {
            root = getEnvironmentVariable("APPDATA") is { Length: > 0 } appData
                ? appData
                : System.IO.Path.Combine(homeDirectory, "AppData", "Roaming");
        }
        else
        {
            // XDG says a relative XDG_CONFIG_HOME is invalid and should be ignored.
            root = getEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg && System.IO.Path.IsPathRooted(xdg)
                ? xdg
                : System.IO.Path.Combine(homeDirectory, ".config");
        }

        return System.IO.Path.Combine(root, "panopticon", "config.json");
    }

    public static CliConfigFile ForCurrentUser() => new(DefaultPath(
        Environment.GetEnvironmentVariable,
        OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

    /// <summary>
    /// The saved login, or null when there's none. An unreadable file also counts as none, and is reported to
    /// <paramref name="stderr"/> by its path only: never its content, which holds the token.
    /// </summary>
    public SavedLogin? Read(TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(Path);
            return JsonSerializer.Deserialize(stream, CliJsonContext.Default.SavedLogin);
        }
        catch (JsonException)
        {
            // The parser's message can quote the file, so it isn't passed on.
            stderr.WriteLine($"panopticon: ignoring the saved login at {Path}: it isn't valid JSON. Run 'panopticon login' again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"panopticon: ignoring the saved login at {Path}: {ex.Message}");
        }

        return null;
    }

    /// <summary>Saves <paramref name="login"/>, replacing whatever was there.</summary>
    public void Write(SavedLogin login)
    {
        ArgumentNullException.ThrowIfNull(login);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UserOnlyDirectory);
            File.SetUnixFileMode(directory, UserOnlyDirectory);
        }

        var temp = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UserOnlyFile;
            }

            using (var stream = new FileStream(temp, options))
            {
                JsonSerializer.Serialize(stream, login, CliJsonContext.Default.SavedLogin);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
