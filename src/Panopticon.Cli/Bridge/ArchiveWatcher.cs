using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Panopticon.Cli.Bridge;

/// <summary>
/// Watches <c>openspec/changes/</c> while the bridge runs, and reports a change archived by itself when its folder
/// moves to <c>archive/</c>, as the client that last reported it (change-documents-and-history D4; spec: cli-bridge,
/// Watching for archives). Only changes reported in this session are watched. Failures are logged, never thrown: the
/// bridge keeps serving, the change stays watched, and the watcher rescans every
/// <see cref="BridgeSettings.ArchiveRetryInterval"/> until the archived report gets through.
/// </summary>
internal sealed partial class ArchiveWatcher(
    BridgeSettings settings,
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
            var retryPending = false;
            while (await NextScanAsync(signals.Reader, retryPending, stoppingToken))
            {
                // Debounce: wait until the folder has been quiet for a while, then scan once.
                while (signals.Reader.TryRead(out _))
                {
                    await Task.Delay(Debounce, stoppingToken);
                }

                retryPending = !await ScanAsync(repoRoot, stoppingToken);
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

    /// <summary>
    /// Waits for a folder event, or, after a failed archived report, for that or the retry interval, whichever comes
    /// first. False once the signals are complete.
    /// </summary>
    private async Task<bool> NextScanAsync(ChannelReader<bool> signals, bool retryPending, CancellationToken stoppingToken)
    {
        if (!retryPending)
        {
            return await signals.WaitToReadAsync(stoppingToken);
        }

        using var retry = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        retry.CancelAfter(settings.ArchiveRetryInterval);
        try
        {
            return await signals.WaitToReadAsync(retry.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return true;
        }
    }

    /// <summary>Reports each watched change whose folder has moved to the archive. False if any report failed.</summary>
    private async Task<bool> ScanAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var allSent = true;
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
                // watcher carries on, and retries after the interval.
                allSent = false;
                if (exception is not BridgeException)
                {
                    LogScanFailed(change, exception.Message);
                }
            }
        }

        if (!allSent)
        {
            LogRetrying(settings.ArchiveRetryInterval.TotalSeconds);
        }

        return allSent;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't watch {Path} for archived changes: {Reason}")]
    private partial void LogCantWatch(string path, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The archive watcher lost events and will rescan: {Reason}")]
    private partial void LogWatcherError(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Will retry the archived report in {Seconds} s")]
    private partial void LogRetrying(double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't report change {Change} archived: {Reason}")]
    private partial void LogScanFailed(string change, string reason);
}
