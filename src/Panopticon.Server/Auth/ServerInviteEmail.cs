using System.Net;

namespace Panopticon.Server.Auth;

/// <summary>
/// The email a server invite sends (spec team-members, "Invitation email"; server-invites D2): who invited them, and a
/// link to the sign-in page. It holds no secret, so it's safe to forward or to have scanned.
/// </summary>
public static class ServerInviteEmail
{
    public static string Subject(string inviter) => $"{inviter} invited you to PANOPTICON";

    /// <param name="to">The invited address.</param>
    /// <param name="inviter">The inviter's display name, which they chose: plain text, never markup.</param>
    /// <param name="signInPage">The server's sign-in page, on <c>Panopticon:PublicUrl</c>.</param>
    public static OutgoingEmail Create(string to, string inviter, Uri signInPage)
    {
        ArgumentNullException.ThrowIfNull(signInPage);
        var link = signInPage.ToString();
        var text =
            $"""
            {inviter} invited you to PANOPTICON, a live dashboard of agents working through OpenSpec changes.

            To join, sign in with this email address ({to}) here:

            {link}

            If you weren't expecting this, you can ignore this email.
            """;
        var html =
            $"""
            <p>{WebUtility.HtmlEncode(inviter)} invited you to PANOPTICON, a live dashboard of agents working through OpenSpec changes.</p>
            <p>To join, sign in with this email address ({WebUtility.HtmlEncode(to)}):</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Sign in to PANOPTICON</a></p>
            <p>If you weren't expecting this, you can ignore this email.</p>
            """;
        return new OutgoingEmail(to, Subject(inviter), text, html);
    }
}

/// <summary>Sends a ready-made server invitation from the outbox. There's nothing to look up or store.</summary>
public sealed record ServerInviteJob(OutgoingEmail Email) : IEmailJob
{
    public const string JobDescription = "Server invitation";

    public string Description => JobDescription;

    public Task<OutgoingEmail?> PrepareAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        Task.FromResult<OutgoingEmail?>(Email);
}
