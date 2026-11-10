using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Cli.Config;

/// <summary>
/// The CLI's config (tags D8): what <c>panopticon login</c> saves, the server it logged in to and the API token it
/// received, and the personal tags <c>panopticon tags</c> keeps, as canonical texts. Each writer changes only its own
/// part, through <see cref="CliConfigFile.Update"/>.
/// </summary>
internal sealed record CliConfig(string? ServerUrl, string? Token, IReadOnlyList<string>? Tags = null)
{
    public static CliConfig Empty { get; } = new(null, null);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(CliConfig))]
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
    /// The saved config, or null when there's none. An unreadable file also counts as none, and is reported to
    /// <paramref name="stderr"/> by its path only: never its content, which holds the token.
    /// </summary>
    public CliConfig? Read(TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        var (config, problem) = Load();
        if (problem is not null)
        {
            stderr.WriteLine($"panopticon: ignoring the saved login at {Path}: {problem}");
        }

        return config;
    }

    /// <summary>
    /// Reads the whole config, applies <paramref name="change"/> and writes the result back, so each writer keeps the
    /// parts it doesn't change: <c>login</c> keeps the tags, and a tag write keeps the login (tags D8). Returns what
    /// was written.
    /// </summary>
    /// <param name="startOverIfUnreadable">
    /// What to do with a file that's there but can't be read: start from an empty config (<c>login</c>, which is how a
    /// broken file is repaired), or refuse, so nothing in it is lost.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// The file can't be read and <paramref name="startOverIfUnreadable"/> is false. The message names the file only.
    /// </exception>
    public CliConfig Update(Func<CliConfig, CliConfig> change, bool startOverIfUnreadable)
    {
        ArgumentNullException.ThrowIfNull(change);
        var current = startOverIfUnreadable ? Load().Config : ReadStrict();
        var updated = change(current ?? CliConfig.Empty);
        Write(updated);
        return updated;
    }

    /// <summary>The saved config, or null when there's no file.</summary>
    /// <exception cref="InvalidDataException">The file is there but can't be read. The message names the file only.</exception>
    public CliConfig? ReadStrict()
    {
        var (config, problem) = Load();
        return problem is null
            ? config
            : throw new InvalidDataException($"The config at {Path} can't be read ({problem}), so it was left as it is.");
    }

    // The config, or null with no problem when there's no file, or null with the problem (never the content).
    private (CliConfig? Config, string? Problem) Load()
    {
        if (!File.Exists(Path))
        {
            return (null, null);
        }

        try
        {
            using var stream = File.OpenRead(Path);
            return (JsonSerializer.Deserialize(stream, CliJsonContext.Default.CliConfig), null);
        }
        catch (JsonException)
        {
            // The parser's message can quote the file, so it isn't passed on.
            return (null, "it isn't valid JSON. Run 'panopticon login' again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Saves <paramref name="config"/>, replacing whatever was there. Commands write through <see cref="Update"/>
    /// instead, so they keep what they don't change.
    /// </summary>
    public void Write(CliConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
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
                JsonSerializer.Serialize(stream, config, CliJsonContext.Default.CliConfig);
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
