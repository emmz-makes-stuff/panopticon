namespace Panopticon.Cli.Update;

/// <summary>
/// Puts a downloaded build in place of the running binary (cli-install design D6). On Unix a rename over it is atomic,
/// and the running process keeps its open file. Windows won't overwrite a running <c>.exe</c> but will rename it, so the
/// old one moves aside to <c>.old</c> first, and comes back if the new one can't be moved in.
/// </summary>
internal sealed class BinaryReplacer(bool windows, Action<string, string, bool>? move = null)
{
    private readonly Action<string, string, bool> _move = move ?? File.Move;

    public static BinaryReplacer ForThisMachine() => new(OperatingSystem.IsWindows());

    public static string OldPath(string binaryPath) => binaryPath + ".old";

    /// <summary>Replaces <paramref name="binaryPath"/> with <paramref name="newFile"/>; on failure the old binary is still in place.</summary>
    public void Replace(string newFile, string binaryPath)
    {
        if (!windows)
        {
            _move(newFile, binaryPath, true);
            return;
        }

        var old = OldPath(binaryPath);
        DeleteQuietly(old);
        _move(binaryPath, old, true);
        try
        {
            _move(newFile, binaryPath, true);
        }
        catch
        {
            _move(old, binaryPath, true);
            throw;
        }
    }

    /// <summary>
    /// Removes the <c>.old</c> file a Windows update left beside the binary. Best effort and silent: another
    /// <c>panopticon</c> (an agent's bridge) may still be running it.
    /// </summary>
    public static void CleanUpAfterUpdate(string? binaryPath)
    {
        if (binaryPath is not null)
        {
            DeleteQuietly(OldPath(binaryPath));
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still in use, or not ours to delete: the next start tries again.
        }
    }
}
