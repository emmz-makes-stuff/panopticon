using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Panopticon.Server.Data;

namespace Panopticon.Server.Auth;

public sealed record SignInRequest(string? Email);

public sealed record ConfirmRequest(string? Secret);

/// <summary>The signed-in member, as the dashboard sees them.</summary>
public sealed record SessionMember(Guid Id, string Email, string DisplayName);

/// <summary>Magic-link sign-in (spec dashboard-sign-in; design D8).</summary>
public static class SignInEndpoints
{
    public const string SignInPath = "/api/auth/sign-in";
    public const string ConfirmPath = "/api/auth/confirm";
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Why a link was refused, for the confirm page to word its message and offer a new link.</summary>
    public static class LinkRefusal
    {
        public const string Invalid = "invalid";
        public const string Used = "used";
        public const string Expired = "expired";
    }

    public static IEndpointRouteBuilder MapSignIn(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(SignInPath, RequestLinkAsync);
        endpoints.MapPost(ConfirmPath, ConfirmAsync);
        return endpoints;
    }

    /// <summary>
    /// Spends a sign-in link and starts a session. Only this POST spends a link (spec "Confirming before signing in"):
    /// opening the emailed URL just loads the confirm page, so a mail scanner fetching it changes nothing.
    /// </summary>
    internal static async Task<IResult> ConfirmAsync(
        ConfirmRequest request,
        HttpContext context,
        PanopticonDb db,
        SessionStore sessions,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Secret))
        {
            return Refused(LinkRefusal.Invalid);
        }

        var hash = Secrets.Hash(request.Secret.Trim());
        var link = await db.SignInLinks.Include(l => l.Member).SingleOrDefaultAsync(l => l.SecretHash == hash, cancellationToken);
        var now = time.GetUtcNow();
        if (link is null || !link.Member.IsActive)
        {
            return Refused(LinkRefusal.Invalid);
        }

        if (link.UsedAt is not null)
        {
            return Refused(LinkRefusal.Used);
        }

        if (link.ExpiresAt <= now)
        {
            return Refused(LinkRefusal.Expired);
        }

        // Spend it atomically: of two concurrent confirms, only one updates the row.
        var spent = await db.SignInLinks
            .Where(l => l.Id == link.Id && l.UsedAt == null)
            .ExecuteUpdateAsync(l => l.SetProperty(x => x.UsedAt, now), cancellationToken);
        if (spent == 0)
        {
            return Refused(LinkRefusal.Used);
        }

        await sessions.StartAsync(link.MemberId, context.Response, cancellationToken);
        return TypedResults.Ok(new SessionMember(link.Member.Id, link.Member.Email, link.Member.DisplayName));
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Refused(string reason) => TypedResults.Problem(
        detail: reason switch
        {
            LinkRefusal.Used => "This sign-in link has already been used. Ask for a new one.",
            LinkRefusal.Expired => "This sign-in link has expired. Ask for a new one.",
            _ => "This sign-in link isn't valid. Ask for a new one.",
        },
        statusCode: StatusCodes.Status400BadRequest,
        extensions: new Dictionary<string, object?> { ["reason"] = reason });

    /// <summary>
    /// Always 202 for a valid address, member or not; the email (if any) goes out in the background. Over the limit
    /// is a 429 whoever the address belongs to.
    /// </summary>
    internal static async Task<IResult> RequestLinkAsync(
        SignInRequest request,
        HttpContext context,
        PanopticonDb db,
        AuthLimits limits,
        EmailOutbox emails,
        IOptions<PublicUrlOptions> publicUrl,
        IHostEnvironment environment,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!MemberEmail.IsValid(request.Email))
        {
            return Problem(HttpStatusCode.BadRequest, "Enter a valid email address.");
        }

        var key = MemberEmail.Key(request.Email!);
        if (!limits.TrySignIn(key, AuthLimits.ClientKey(context)))
        {
            return Problem(HttpStatusCode.TooManyRequests, "Too many sign-in requests. Try again in a few minutes.");
        }

        var member = await db.Members.SingleOrDefaultAsync(m => m.EmailKey == key && m.RemovedAt == null, cancellationToken);
        if (member is not null)
        {
            var secret = Secrets.NewSecret();
            var now = time.GetUtcNow();
            db.SignInLinks.Add(new SignInLink
            {
                MemberId = member.Id,
                SecretHash = Secrets.Hash(secret),
                CreatedAt = now,
                ExpiresAt = now + LinkLifetime,
            });
            await db.SaveChangesAsync(cancellationToken);

            var link = $"{LinkRoot(publicUrl.Value, context, environment)}sign-in/confirm#{secret}";
            emails.Enqueue(SignInEmail.Create(member.Email, link));
        }

        return TypedResults.Accepted((string?)null, new { message = "Check your email for a sign-in link." });
    }

    internal static IResult Problem(HttpStatusCode status, string detail) =>
        TypedResults.Problem(detail: detail, statusCode: (int)status);

    // Links are built on the configured public URL, never the Host header; only Development may fall back to the request.
    internal static Uri LinkRoot(PublicUrlOptions publicUrl, HttpContext context, IHostEnvironment environment) =>
        publicUrl.TryGetUri(out var root)
            ? root
            : environment.IsDevelopment()
                ? new Uri($"{context.Request.Scheme}://{context.Request.Host}/")
                : throw new InvalidOperationException($"{PublicUrlOptions.ConfigKey} is not configured.");
}

/// <summary>The sign-in email: plain words, the link, and no tracking.</summary>
public static class SignInEmail
{
    public const string Subject = "Sign in to a PANOPTICON";

    public static OutgoingEmail Create(string to, string link)
    {
        var minutes = (int)SignInEndpoints.LinkLifetime.TotalMinutes;
        var text =
            $"""
            Here's your link to sign in to a PANOPTICON:

            {link}

            It works once, and expires in {minutes} minutes. If you didn't ask to sign in, you can ignore this email.
            """;
        var encoded = WebUtility.HtmlEncode(link);
        var html =
            $"""
            <p>Here's your link to sign in to a PANOPTICON:</p>
            <p><a href="{encoded}">Sign in</a></p>
            <p>It works once, and expires in {minutes} minutes. If you didn't ask to sign in, you can ignore this email.</p>
            """;
        return new OutgoingEmail(to, Subject, text, html);
    }
}
