using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Panopticon.Cli.Bridge;

/// <summary>
/// Watches <c>openspec/changes/</c> while the bridge runs, and reports a change archived by itself when its folder
/// moves to <c>archive/</c>, as the client that last reported it (change-documents-and-history D4; spec: cli-bridge,
/// Watching for archives). Only changes reported in this session are watched. Failures are logged, never thrown: the
/// bridge keeps serving, and the change stays watched, so a later folder event or the agent's next report retries.
/// </summary>
internal sealed partial class ArchiveWatcher(
    ReportedChanges reported,
    ProgressReporter reporter,
    ILogger<ArchiveWatcher> logger) : BackgroundService
{
    /// <summary>How long the folder must stay quiet before a rescan; a move shows up as several events.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string repoRoot;
        try
        {
            repoRoot = await reported.RepoRoot.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var changesRoot = ChangeDocuments.ChangesRoot(repoRoot);
        var signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        using var watcher = TryWatch(changesRoot, signals.Writer);

        // A first scan covers an archive that happened before the watcher started.
        signals.Writer.TryWrite(true);
        try
        {
            while (await signals.Reader.WaitToReadAsync(stoppingToken))
            {
                // Debounce: wait until the folder has been quiet for a while, then scan once.
                while (signals.Reader.TryRead(out _))
                {
                    await Task.Delay(Debounce, stoppingToken);
                }

                await ScanAsync(repoRoot, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private FileSystemWatcher? TryWatch(string changesRoot, ChannelWriter<bool> signals)
    {
        try
        {
            var watcher = new FileSystemWatcher(changesRoot)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName,
            };
            watcher.Renamed += (_, _) => signals.TryWrite(true);
            watcher.Deleted += (_, _) => signals.TryWrite(true);

            // A lost event (a buffer overflow, say) is covered by a full rescan.
            watcher.Error += (_, args) =>
            {
                LogWatcherError(args.GetException().Message);
                signals.TryWrite(true);
            };
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            LogCantWatch(changesRoot, exception.Message);
            return null;
        }
    }

    private async Task ScanAsync(string repoRoot, CancellationToken cancellationToken)
    {
        foreach (var (change, client) in reported.Snapshot())
        {
            try
            {
                if (Directory.Exists(ChangeDocuments.ResolveChangeDirectory(repoRoot, change))
                    || ChangeDocuments.FindArchive(repoRoot, change) is null)
                {
                    continue;
                }

                // The reporter reads the archive, sends it marked archived, and forgets the change once that succeeds.
                await reporter.ReportAsync(client, change, section: null, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // The reporter has already logged a BridgeException; anything else is logged here. Either way, the
                // watcher carries on.
                if (exception is not BridgeException)
                {
                    LogScanFailed(change, exception.Message);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't watch {Path} for archived changes: {Reason}")]
    private partial void LogCantWatch(string path, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The archive watcher lost events and will rescan: {Reason}")]
    private partial void LogWatcherError(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't report change {Change} archived: {Reason}")]
    private partial void LogScanFailed(string change, string reason);
}
