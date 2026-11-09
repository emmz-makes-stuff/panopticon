using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;
using Panopticon.Server.Documents;

namespace Panopticon.Server.Progress;

/// <summary>What one backfill run did.</summary>
/// <param name="Filled">Events given their progress.</param>
/// <param name="Unreadable">Events whose <c>tasks.md</c> couldn't be read, now marked so.</param>
/// <param name="Stopped">Whether a store error stopped the run before every pending event was seen.</param>
public sealed record BackfillResult(int Filled, int Unreadable, bool Stopped);

/// <summary>
/// Fills in the progress of reports accepted before it was kept (change-metrics D3). It runs once at start, in the
/// background, a batch at a time. It only touches events with no counts yet, so it's idempotent, and a run stopped by
/// a store error is picked up by the next start.
/// </summary>
public sealed partial class ProgressBackfill(IServiceScopeFactory scopes, ILogger<ProgressBackfill> logger) : BackgroundService
{
    public const int BatchSize = 200;

    public async Task<BackfillResult> RunAsync(CancellationToken cancellationToken)
    {
        var (filled, unreadable) = (0, 0);
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
                return Finish(new BackfillResult(filled, unreadable, Stopped: false));
            }

            // Identical reports share one stored tasks.md, so each is fetched and parsed once per batch.
            var parsedByHash = new Dictionary<string, ParsedTasks?>(StringComparer.Ordinal);
            foreach (var reportEvent in batch)
            {
                if (!parsedByHash.TryGetValue(reportEvent.TasksSha256, out var parsed))
                {
                    try
                    {
                        parsed = Parse(await documents.GetAsync(reportEvent.TasksSha256, cancellationToken));
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        // A transient store error: keep what this batch has filled, and leave the rest for next start.
                        await db.SaveChangesAsync(cancellationToken);
                        LogStopped(exception, reportEvent.Id);
                        return Finish(new BackfillResult(filled, unreadable, Stopped: true));
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
        if (result.Filled > 0 || result.Unreadable > 0)
        {
            LogFinished(result.Filled, result.Unreadable);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Progress backfill: filled {Filled} reports, {Unreadable} unreadable")]
    private partial void LogFinished(int filled, int unreadable);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Progress backfill: report event {EventId} has no readable tasks.md ({Sha256}); it's left out of the metrics")]
    private partial void LogUnreadable(long eventId, string sha256);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Progress backfill stopped at report event {EventId}; the next start retries")]
    private partial void LogStopped(Exception exception, long eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Progress backfill failed; the next start retries")]
    private partial void LogFailed(Exception exception);
}
