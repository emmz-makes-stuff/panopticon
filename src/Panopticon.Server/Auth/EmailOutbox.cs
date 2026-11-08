using System.Threading.Channels;

namespace Panopticon.Server.Auth;

/// <summary>
/// Sends emails off the request path, so a sign-in request answers in the same time whether or not it sent one (spec
/// dashboard-sign-in, "Requesting a sign-in link"). A failed send is logged, never surfaced: the person asking has
/// already been told to check their email.
/// </summary>
public sealed partial class EmailOutbox(IServiceScopeFactory scopes, ILogger<EmailOutbox> logger) : BackgroundService
{
    // Bounded so a flood can't grow memory without limit; the rate limits keep it far below this.
    private readonly Channel<OutgoingEmail> _queue = Channel.CreateBounded<OutgoingEmail>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>Queues an email; false when the queue is full and it was dropped (and logged).</summary>
    public bool Enqueue(OutgoingEmail email)
    {
        if (_queue.Writer.TryWrite(email))
        {
            return true;
        }

        LogDropped();
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(email, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // The email holds a secret link, so only its subject is logged.
                LogSendFailed(exception, email.Subject);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending an email failed ({Subject})")]
    private partial void LogSendFailed(Exception exception, string subject);

    [LoggerMessage(Level = LogLevel.Error, Message = "The email queue is full; an email was dropped")]
    private partial void LogDropped();
}
