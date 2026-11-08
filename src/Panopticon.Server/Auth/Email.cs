using Resend;

namespace Panopticon.Server.Auth;

/// <summary>An email to one recipient, with plain-text and HTML parts.</summary>
public sealed record OutgoingEmail(string To, string Subject, string Text, string Html);

/// <summary>Sends Panopticon's emails (team-auth D9). Throws when the email can't be sent.</summary>
public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken);
}

/// <summary>Email settings (spec dashboard-sign-in, "Sign-in email delivery").</summary>
public sealed class EmailOptions
{
    public const string ApiKeyConfigKey = "Panopticon:Resend:ApiKey";
    public const string FromConfigKey = "Panopticon:Email:From";

    public string? ResendApiKey { get; set; }
    public string? From { get; set; }

    public bool HasResendKey => !string.IsNullOrWhiteSpace(ResendApiKey);
}

/// <summary>Sends through Resend, from the configured sender.</summary>
public sealed class ResendEmailSender(IResend resend, Microsoft.Extensions.Options.IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        var message = new EmailMessage
        {
            From = options.Value.From!,
            Subject = email.Subject,
            TextBody = email.Text,
            HtmlBody = email.Html,
        };
        message.To.Add(email.To);

        await resend.EmailSendAsync(message, cancellationToken);
    }
}

/// <summary>
/// Development with no Resend key: the email is logged instead of sent, at Warning so the link stands out. Never
/// registered outside Development.
/// </summary>
public sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        LogEmail(email.To, email.Subject, email.Text);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Development: not sending email to {To} (no Resend key). {Subject}\n{Text}")]
    private partial void LogEmail(string to, string subject, string text);
}
