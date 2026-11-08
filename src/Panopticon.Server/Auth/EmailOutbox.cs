using System.Threading.Channels;

namespace Panopticon.Server.Auth;

/// <summary>
/// An email to prepare and send off the request path. <see cref="PrepareAsync"/> runs in its own scope, and answers
/// null when there's nothing to send.
/// </summary>
public interface IEmailJob
{
    /// <summary>What the job is, for logs: never anything secret.</summary>
    string Description { get; }

    Task<OutgoingEmail?> PrepareAsync(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>
/// Runs email jobs off the request path, so a request answers in the same time, with the same work, whether or not
/// it leads to an email (spec dashboard-sign-in, "Requesting a sign-in link"). A failed job is logged, never surfaced:
/// the person asking has already been told to check their email.
/// </summary>
public sealed partial class EmailOutbox(IServiceScopeFactory scopes, ILogger<EmailOutbox> logger) : BackgroundService
{
    // Bounded so a flood can't grow memory without limit; the rate limits keep it far below this.
    private readonly Channel<IEmailJob> _queue = Channel.CreateBounded<IEmailJob>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>Queues a job; false when the queue is full and it was dropped (and logged).</summary>
    public bool Enqueue(IEmailJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (_queue.Writer.TryWrite(job))
        {
            return true;
        }

        LogDropped(job.Description);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await job.PrepareAsync(scope.ServiceProvider, stoppingToken) is { } email)
                {
                    await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(email, stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // The email may hold a secret link, so only the job's description is logged.
                LogSendFailed(exception, job.Description);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending an email failed ({Description})")]
    private partial void LogSendFailed(Exception exception, string description);

    [LoggerMessage(Level = LogLevel.Error, Message = "The email queue is full; an email was dropped ({Description})")]
    private partial void LogDropped(string description);
}
