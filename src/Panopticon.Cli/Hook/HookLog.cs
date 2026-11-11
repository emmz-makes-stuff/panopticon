using System.Globalization;
using Panopticon.Cli.Config;

namespace Panopticon.Cli.Hook;

/// <summary>
/// <c>hook.log</c>, next to the CLI config: where <c>panopticon hook</c> writes its failures, since its stdout belongs
/// to Claude Code (spec agent-presence, Hooks never break the session). One line per failure, never the token. At
/// <see cref="MaxBytes"/> it's moved aside to <c>hook.log.1</c>, so it can't grow without end. Writing it never throws.
/// </summary>
internal sealed class HookLog(string path, TimeProvider clock)
{
    public const string FileName = "hook.log";

    public const long MaxBytes = 1024 * 1024;

    public string Path { get; } = path;

    public static HookLog NextTo(CliConfigFile config, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(config);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(config.Path))!;
        return new(System.IO.Path.Combine(directory, FileName), clock);
    }

    public void Write(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(directory);
            }
            else
            {
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            if (File.Exists(Path) && new FileInfo(Path).Length >= MaxBytes)
            {
                File.Move(Path, Path + ".1", overwrite: true);
            }

            var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.ReadWrite };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var writer = new StreamWriter(new FileStream(Path, options));
            var line = message.ReplaceLineEndings(" ");
            writer.WriteLine($"{clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)} {line}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nowhere left to say it: the hook must still exit 0 with no output.
        }
    }
}
