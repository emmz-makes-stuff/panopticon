using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;
using Panopticon.Server.Documents;

namespace Panopticon.Server.Progress;

/// <summary>What one backfill run did.</summary>
/// <param name="Filled">Events given their progress.</param>
/// <param name="Unreadable">Events whose <c>tasks.md</c> is missing or unparseable, now marked so.</param>
/// <param name="Skipped">Events whose <c>tasks.md</c> failed to read this run, left pending for the next.</param>
/// <param name="Stopped">Whether read errors in a row stopped the run before every pending event was seen.</param>
public sealed record BackfillResult(int Filled, int Unreadable, int Skipped, bool Stopped);

/// <summary>
/// Fills in the progress of reports accepted before it was kept (change-metrics D3). It runs once at start, in the
/// background, a batch at a time. It only touches events with no counts yet, so it's idempotent: an event skipped
/// after a read error, or left by a run that stopped, is picked up by the next start.
/// </summary>
public sealed partial class ProgressBackfill(IServiceScopeFactory scopes, ILogger<ProgressBackfill> logger) : BackgroundService
{
    public const int BatchSize = 200;

    /// <summary>Read errors in a row that mean the store itself is down, rather than one object failing.</summary>
    public const int MaxFailuresInARow = 3;

    public async Task<BackfillResult> RunAsync(CancellationToken cancellationToken)
    {
        var (filled, unreadable, skipped, failuresInARow) = (0, 0, 0, 0);
        var failedHashes = new HashSet<string>(StringComparer.Ordinal);
        var afterId = 0L;
        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PanopticonDb>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentStore>();

            var batch = await db.ReportEvents
                .Where(e => e.OverallTotal == null && e.Id > afterId)
                .OrderBy(e => e.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return Finish(new BackfillResult(filled, unreadable, skipped, Stopped: false));
            }

            // Identical reports share one stored tasks.md, so each is fetched and parsed once per batch, and one that
            // failed isn't tried again this run.
            var parsedByHash = new Dictionary<string, ParsedTasks?>(StringComparer.Ordinal);
            foreach (var reportEvent in batch)
            {
                if (failedHashes.Contains(reportEvent.TasksSha256))
                {
                    skipped++;
                    continue;
                }

                if (!parsedByHash.TryGetValue(reportEvent.TasksSha256, out var parsed))
                {
                    try
                    {
                        parsed = Parse(await documents.GetAsync(reportEvent.TasksSha256, cancellationToken));
                        failuresInARow = 0;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        // One object failing (a 403, say) mustn't hold up the rest: skip it, and the next start retries
                        // it. Several failing in a row means the store is down, so stop, keeping what's filled.
                        failedHashes.Add(reportEvent.TasksSha256);
                        skipped++;
                        LogSkipped(exception, reportEvent.Id, reportEvent.TasksSha256);
                        if (++failuresInARow >= MaxFailuresInARow)
                        {
                            await db.SaveChangesAsync(cancellationToken);
                            LogStopped(failuresInARow, reportEvent.Id);
                            return Finish(new BackfillResult(filled, unreadable, skipped, Stopped: true));
                        }

                        continue;
                    }

                    parsedByHash[reportEvent.TasksSha256] = parsed;
                }

                if (parsed is null)
                {
                    ReportProgress.MarkUnreadable(reportEvent);
                    LogUnreadable(reportEvent.Id, reportEvent.TasksSha256);
                    unreadable++;
                }
                else
                {
                    ReportProgress.Fill(reportEvent, parsed, CurrentSection.Select(parsed, reportEvent.ReportedSection));
                    filled++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            afterId = batch[^1].Id;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the start path: the server serves while this runs.
        await Task.Yield();
        try
        {
            await RunAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // The metrics cover what's been filled so far; the next start carries on.
            LogFailed(exception);
        }
    }

    /// <summary>Null when the content is missing, or too large to have been accepted as a report.</summary>
    private static ParsedTasks? Parse(string? tasksMarkdown)
    {
        if (tasksMarkdown is null)
        {
            return null;
        }

        try
        {
            return TasksParser.Parse(tasksMarkdown);
        }
        catch (TasksDocumentTooLargeException)
        {
            return null;
        }
    }

    private BackfillResult Finish(BackfillResult result)
    {
        if (result.Filled > 0 || result.Unreadable > 0 || result.Skipped > 0)
        {
            LogFinished(result.Filled, result.Unreadable, result.Skipped);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Progress backfill: filled {Filled} reports, {Unreadable} unreadable, {Skipped} skipped")]
    private partial void LogFinished(int filled, int unreadable, int skipped);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Progress backfill: report event {EventId} has no readable tasks.md ({Sha256}); it's left out of the metrics")]
    private partial void LogUnreadable(long eventId, string sha256);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Progress backfill: couldn't read report event {EventId}'s tasks.md ({Sha256}); skipped, the next start retries")]
    private partial void LogSkipped(Exception exception, long eventId, string sha256);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Progress backfill stopped after {Failures} read errors in a row, at report event {EventId}; the next start retries")]
    private partial void LogStopped(int failures, long eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Progress backfill failed; the next start retries")]
    private partial void LogFailed(Exception exception);
}
