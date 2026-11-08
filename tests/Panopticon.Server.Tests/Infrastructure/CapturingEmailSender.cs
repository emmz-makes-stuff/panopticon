using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Panopticon.Server.Auth;

namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>Keeps every email instead of sending it, so tests can follow the links in them.</summary>
public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<OutgoingEmail> _sent = new();

    /// <summary>When set, every send throws this instead, as a failing provider would.</summary>
    public Exception? FailWith { get; set; }

    public IReadOnlyList<OutgoingEmail> Sent => [.. _sent];

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        _sent.Enqueue(email);
        return Task.CompletedTask;
    }

    /// <summary>The emails sent to an address (any case).</summary>
    public IReadOnlyList<OutgoingEmail> To(string address) =>
        [.. _sent.Where(e => string.Equals(e.To, address, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// Waits until <paramref name="count"/> emails have gone to an address. Emails are sent in the background, in the
    /// order they were queued, so waiting for a later email also proves whether an earlier one was queued.
    /// </summary>
    public async Task<IReadOnlyList<OutgoingEmail>> WaitForAsync(string address, int count = 1, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (To(address).Count < count)
        {
            await Task.Delay(10, timeout.Token);
        }

        return To(address);
    }

    /// <summary>The secret from the newest sign-in link sent to an address.</summary>
    public string LatestSignInSecret(string address)
    {
        var sent = To(address);
        var email = sent.Count > 0 ? sent[^1] : throw new InvalidOperationException($"No email was sent to {address}.");
        return SignInLink().Match(email.Text) is { Success: true } match
            ? match.Groups["secret"].Value
            : throw new InvalidOperationException("The email has no sign-in link.");
    }

    [GeneratedRegex(@"/sign-in/confirm#(?<secret>[A-Za-z0-9_-]+)")]
    private static partial Regex SignInLink();
}
