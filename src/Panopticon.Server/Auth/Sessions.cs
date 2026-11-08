using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Panopticon.Server.Data;

namespace Panopticon.Server.Auth;

/// <summary>The claims Panopticon's principals carry.</summary>
public static class PanopticonClaims
{
    /// <summary>The member's id (absent for the Development bootstrap token).</summary>
    public const string MemberId = ClaimTypes.NameIdentifier;

    /// <summary>The session's id, on dashboard (cookie) principals.</summary>
    public const string SessionId = "panopticon:session";

    public static Guid? GetMemberId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal?.FindFirstValue(MemberId), out var id) ? id : null;

    public static Guid? GetSessionId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal?.FindFirstValue(SessionId), out var id) ? id : null;
}

/// <summary>Dashboard sessions (spec dashboard-sign-in, "Sessions" and "Signing out"; design D4).</summary>
public sealed class SessionStore(PanopticonDb db, TimeProvider time, IHostEnvironment environment)
{
    public const string CookieName = "panopticon_session";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>Sliding expiry is written at most this often, so a busy dashboard doesn't write on every request.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    /// <summary>Starts a session for a member and sets its cookie.</summary>
    public async Task StartAsync(Guid memberId, HttpResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var secret = Secrets.NewSecret();
        var now = time.GetUtcNow();
        var session = new Session
        {
            MemberId = memberId,
            SecretHash = Secrets.Hash(secret),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + Lifetime,
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        SetCookie(response, secret, session.ExpiresAt);
    }

    /// <summary>Ends a session on the server (the cookie stops working everywhere) and clears it here.</summary>
    public async Task EndAsync(Guid sessionId, HttpResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        await db.Sessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync(cancellationToken);
        response.Cookies.Delete(CookieName, CookieOptions(expires: null));
    }

    /// <summary>
    /// The live session and its active member for a cookie secret, sliding its expiry (and the cookie's) when due.
    /// Null for an unknown or expired session, or a removed member.
    /// </summary>
    public async Task<Session?> ResolveAsync(string secret, HttpResponse response, CancellationToken cancellationToken)
    {
        var hash = Secrets.Hash(secret);
        var now = time.GetUtcNow();
        var session = await db.Sessions
            .Include(s => s.Member)
            .SingleOrDefaultAsync(s => s.SecretHash == hash, cancellationToken);
        if (session is null || session.ExpiresAt <= now || !session.Member.IsActive)
        {
            return null;
        }

        if (now - session.LastSeenAt >= TouchInterval)
        {
            session.LastSeenAt = now;
            session.ExpiresAt = now + Lifetime;
            await db.SaveChangesAsync(cancellationToken);
            SetCookie(response, secret, session.ExpiresAt);
        }

        return session;
    }

    /// <summary>Whether a session still exists, unexpired, for an active member (for long-lived requests).</summary>
    public Task<bool> IsLiveAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return db.Sessions.AnyAsync(
            s => s.Id == sessionId && s.ExpiresAt > now && s.Member.RemovedAt == null,
            cancellationToken);
    }

    private void SetCookie(HttpResponse response, string secret, DateTimeOffset expires) =>
        response.Cookies.Append(CookieName, secret, CookieOptions(expires));

    private CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = !environment.IsDevelopment(),
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}

/// <summary>Authenticates the dashboard's session cookie against the sessions table, on every request.</summary>
public sealed class SessionHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionStore sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(SessionStore.CookieName, out var secret) || string.IsNullOrEmpty(secret))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await sessions.ResolveAsync(secret, Response, Context.RequestAborted);
        if (session is null)
        {
            return AuthenticateResult.Fail("The session has ended.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(PanopticonClaims.MemberId, session.MemberId.ToString()),
                new Claim(PanopticonClaims.SessionId, session.Id.ToString()),
                new Claim(ClaimTypes.Name, session.Member.DisplayName),
                new Claim(ClaimTypes.Email, session.Member.Email),
            ],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
