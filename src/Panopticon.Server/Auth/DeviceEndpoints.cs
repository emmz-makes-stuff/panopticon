using System.Net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Panopticon.Contracts;
using Panopticon.Server.Data;

namespace Panopticon.Server.Auth;

/// <summary>A device login as the approval page sees it: the code, the machine it's for, and where it stands.</summary>
public sealed record DeviceLoginView(string UserCode, string MachineName, string State);

/// <summary>The values of <see cref="DeviceLoginView.State"/>.</summary>
public static class DeviceLoginStates
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Denied = "denied";
    public const string Expired = "expired";
}

/// <summary>
/// The device-style login behind <c>panopticon login</c> (spec reporter-credentials; design D12, D7). The CLI starts a
/// login and polls with a device secret only it holds; a signed-in member approves or denies the login by its code.
/// </summary>
public static class DeviceEndpoints
{
    public const string StartPath = "/api/device/start";
    public const string PollPath = "/api/device/poll";
    public const int MachineNameMaxLength = 100;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapDevice(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(StartPath, StartAsync);
        endpoints.MapPost(PollPath, PollAsync);
        endpoints.MapGet("/api/device/{code}", GetAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapPost("/api/device/{code}/approve", ApproveAsync).RequireAuthorization(AuthPolicies.Member);
        endpoints.MapPost("/api/device/{code}/deny", DenyAsync).RequireAuthorization(AuthPolicies.Member);
        return endpoints;
    }

    public static string LoginPath(string code) => $"/api/device/{code}";

    /// <summary>
    /// The login behind a code, for the approval page to show its machine name before anyone approves (spec
    /// "Approving a device login"). Every lookup counts against the session's attempt limit, since each is a guess.
    /// </summary>
    internal static async Task<IResult> GetAsync(
        string code,
        ClaimsPrincipal user,
        PanopticonDb db,
        AuthLimits limits,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!limits.TryApprovalAttempt(user.GetSessionId()!.Value))
        {
            return TooManyAttempts();
        }

        var normalized = Secrets.NormalizeUserCode(code);
        var login = normalized is null
            ? null
            : await db.DeviceLogins.AsNoTracking().SingleOrDefaultAsync(l => l.UserCode == normalized, cancellationToken);
        if (login is null)
        {
            return InvalidCode();
        }

        var state = login switch
        {
            { DeniedAt: not null } => DeviceLoginStates.Denied,
            { ApprovedAt: not null } => DeviceLoginStates.Approved,
            _ when login.ExpiresAt <= time.GetUtcNow() => DeviceLoginStates.Expired,
            _ => DeviceLoginStates.Pending,
        };
        return TypedResults.Ok(new DeviceLoginView(Secrets.FormatUserCode(login.UserCode), login.MachineName, state));
    }

    internal static Task<IResult> ApproveAsync(
        string code,
        ClaimsPrincipal user,
        PanopticonDb db,
        AuthLimits limits,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        DecideAsync(code, approve: true, user, db, limits, time, cancellationToken);

    internal static Task<IResult> DenyAsync(
        string code,
        ClaimsPrincipal user,
        PanopticonDb db,
        AuthLimits limits,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        DecideAsync(code, approve: false, user, db, limits, time, cancellationToken);

    /// <summary>
    /// Approves or denies a pending, unexpired login, atomically: of two decisions on one code, only the first counts.
    /// An unknown, expired or already decided code is a 404 ("isn't valid"), and nothing changes.
    /// </summary>
    private static async Task<IResult> DecideAsync(
        string code,
        bool approve,
        ClaimsPrincipal user,
        PanopticonDb db,
        AuthLimits limits,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!limits.TryApprovalAttempt(user.GetSessionId()!.Value))
        {
            return TooManyAttempts();
        }

        var normalized = Secrets.NormalizeUserCode(code);
        if (normalized is null)
        {
            return InvalidCode();
        }

        var now = time.GetUtcNow();
        var memberId = user.GetMemberId();
        var pending = db.DeviceLogins.Where(l =>
            l.UserCode == normalized && l.ApprovedAt == null && l.DeniedAt == null && l.ExpiresAt > now);
        var decided = approve
            ? await pending.ExecuteUpdateAsync(
                l => l.SetProperty(x => x.ApprovedByMemberId, memberId).SetProperty(x => x.ApprovedAt, now),
                cancellationToken)
            : await pending.ExecuteUpdateAsync(l => l.SetProperty(x => x.DeniedAt, now), cancellationToken);
        return decided == 0 ? InvalidCode() : TypedResults.NoContent();
    }

    private static IResult InvalidCode() =>
        SignInEndpoints.Problem(
            HttpStatusCode.NotFound,
            "That code isn't valid. It may have expired or been used already: run panopticon login again for a new one.");

    private static IResult TooManyAttempts() =>
        SignInEndpoints.Problem(HttpStatusCode.TooManyRequests, "Too many codes tried. Wait a minute and try again.");

    internal static async Task<IResult> StartAsync(
        DeviceStartRequest request,
        HttpContext context,
        PanopticonDb db,
        AuthLimits limits,
        IOptions<PublicUrlOptions> publicUrl,
        IHostEnvironment environment,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var machineName = request.MachineName?.Trim();
        if (string.IsNullOrEmpty(machineName) || machineName.Length > MachineNameMaxLength)
        {
            return SignInEndpoints.Problem(
                HttpStatusCode.BadRequest,
                $"A machine name must be 1 to {MachineNameMaxLength} characters.");
        }

        if (!limits.TryDeviceStart(AuthLimits.ClientKey(context)))
        {
            return SignInEndpoints.Problem(HttpStatusCode.TooManyRequests, "Too many logins started. Try again in a few minutes.");
        }

        var secret = Secrets.NewSecret();
        var now = time.GetUtcNow();

        // Codes are unique for all time; a clash in 20^8 is all but impossible, but a retry costs nothing.
        for (var attempt = 1; ; attempt++)
        {
            var login = new DeviceLogin
            {
                DeviceSecretHash = Secrets.Hash(secret),
                UserCode = Secrets.NewUserCode(),
                MachineName = machineName,
                CreatedAt = now,
                ExpiresAt = now + Lifetime,
            };
            db.DeviceLogins.Add(login);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (attempt < 3
                && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
                continue;
            }

            var code = Secrets.FormatUserCode(login.UserCode);
            var approveUrl = new Uri(SignInEndpoints.LinkRoot(publicUrl.Value, context, environment), $"device?code={code}");
            return TypedResults.Json(
                new DeviceStartResponse(secret, code, approveUrl.ToString(), (int)PollInterval.TotalSeconds, (int)Lifetime.TotalSeconds),
                ContractsJsonContext.Default.DeviceStartResponse);
        }
    }

    /// <summary>
    /// One answer per poll (design D12). The token is created, and its secret returned, in the same request that
    /// marks it delivered, so it's handed over once. Afterwards, and for a secret the server doesn't know, the answer
    /// is <c>expired</c>. Polling within <see cref="PollInterval"/> of the last poll is <c>slow_down</c> and changes
    /// nothing else.
    /// </summary>
    internal static async Task<IResult> PollAsync(
        DevicePollRequest request,
        PanopticonDb db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceSecret))
        {
            return SignInEndpoints.Problem(HttpStatusCode.BadRequest, "A device secret is required.");
        }

        var hash = Secrets.Hash(request.DeviceSecret.Trim());
        var login = await db.DeviceLogins.AsNoTracking().SingleOrDefaultAsync(l => l.DeviceSecretHash == hash, cancellationToken);
        if (login is null || login.TokenDeliveredAt is not null)
        {
            return Poll(DevicePollStatus.Expired);
        }

        var now = time.GetUtcNow();
        var due = now - PollInterval;
        var polled = await db.DeviceLogins
            .Where(l => l.Id == login.Id && (l.LastPolledAt == null || l.LastPolledAt <= due))
            .ExecuteUpdateAsync(l => l.SetProperty(x => x.LastPolledAt, now), cancellationToken);
        if (polled == 0)
        {
            return Poll(DevicePollStatus.SlowDown);
        }

        if (login.DeniedAt is not null)
        {
            return Poll(DevicePollStatus.Denied);
        }

        if (login.ExpiresAt <= now)
        {
            return Poll(DevicePollStatus.Expired);
        }

        if (login.ApprovedByMemberId is not { } memberId)
        {
            return Poll(DevicePollStatus.Pending);
        }

        var member = await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId, cancellationToken);
        if (!member.IsActive)
        {
            // Removed after approving: there's nobody to issue the token to.
            return Poll(DevicePollStatus.Expired);
        }

        return await DeliverAsync(db, login, member, now, cancellationToken) is { } token
            ? TypedResults.Json(
                new DevicePollResponse(DevicePollStatus.Approved, token, new DeviceMember(member.DisplayName, member.Email)),
                ContractsJsonContext.Default.DevicePollResponse)
            : Poll(DevicePollStatus.Expired);
    }

    /// <summary>Issues the login's token, or returns null when another poll already has.</summary>
    private static async Task<string?> DeliverAsync(
        PanopticonDb db,
        DeviceLogin login,
        Member member,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await db.DeviceLogins
            .Where(l => l.Id == login.Id && l.TokenDeliveredAt == null)
            .ExecuteUpdateAsync(l => l.SetProperty(x => x.TokenDeliveredAt, now), cancellationToken);
        if (claimed == 0)
        {
            return null;
        }

        var secret = Secrets.NewApiToken();
        var token = new ApiToken
        {
            MemberId = member.Id,
            SecretHash = Secrets.Hash(secret),
            MachineName = login.MachineName,
            CreatedAt = now,
        };
        db.ApiTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
        await db.DeviceLogins
            .Where(l => l.Id == login.Id)
            .ExecuteUpdateAsync(l => l.SetProperty(x => x.TokenId, token.Id), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return secret;
    }

    private static Microsoft.AspNetCore.Http.HttpResults.JsonHttpResult<DevicePollResponse> Poll(string status) =>
        TypedResults.Json(new DevicePollResponse(status), ContractsJsonContext.Default.DevicePollResponse);
}
