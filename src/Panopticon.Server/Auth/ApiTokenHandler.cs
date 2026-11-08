using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Panopticon.Server.Data;

namespace Panopticon.Server.Auth;

/// <summary>
/// Authenticates <c>Authorization: Bearer &lt;token&gt;</c> on <c>/mcp</c> (spec progress-reporting, "Bearer token
/// required"; design D4). The token is looked up by its hash, and must be unrevoked with an active member. In
/// Development only, the bootstrap token is also accepted, as a reporter with no member.
/// </summary>
public sealed class ApiTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    PanopticonDb db,
    IOptions<BootstrapTokenOptions> bootstrap,
    IHostEnvironment environment,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiToken";

    /// <summary>The claim holding the token's id, so a request can be traced to the token it used.</summary>
    public const string TokenIdClaim = "panopticon:token";

    /// <summary><c>last_used_at</c> is written at most this often per token, so reporting doesn't write on every call.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header))
        {
            return AuthenticateResult.NoResult();
        }

        // The presented token is never logged or echoed.
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("Invalid bearer token.");
        }

        var presented = header[BearerPrefix.Length..].Trim();
        if (presented.Length == 0)
        {
            return AuthenticateResult.Fail("Invalid bearer token.");
        }

        if (environment.IsDevelopment() && BootstrapToken.Matches(presented, bootstrap.Value.Token))
        {
            return Success([new Claim(ClaimTypes.Name, "bootstrap")]);
        }

        var hash = Secrets.Hash(presented);
        var token = await db.ApiTokens.AsNoTracking()
            .Include(t => t.Member)
            .SingleOrDefaultAsync(t => t.SecretHash == hash, Context.RequestAborted);
        if (token is null || token.RevokedAt is not null || !token.Member.IsActive)
        {
            return AuthenticateResult.Fail("Invalid bearer token.");
        }

        var now = time.GetUtcNow();
        if (token.LastUsedAt is null || token.LastUsedAt <= now - TouchInterval)
        {
            var due = now - TouchInterval;
            await db.ApiTokens
                .Where(t => t.Id == token.Id && (t.LastUsedAt == null || t.LastUsedAt <= due))
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.LastUsedAt, now), Context.RequestAborted);
        }

        return Success(
        [
            new Claim(PanopticonClaims.MemberId, token.MemberId.ToString()),
            new Claim(TokenIdClaim, token.Id.ToString()),
            new Claim(ClaimTypes.Name, token.Member.DisplayName),
        ]);
    }

    private static AuthenticateResult Success(Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
