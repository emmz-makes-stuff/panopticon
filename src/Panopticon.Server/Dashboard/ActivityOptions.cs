using Microsoft.Extensions.Options;

namespace Panopticon.Server.Dashboard;

/// <summary>
/// When a card goes quiet (filters-and-idle-cards D1; spec: live-dashboard, Quiet thresholds are settings): idle once its
/// change's last report is older than <see cref="IdleAfter"/>, stale once older than <see cref="StaleAfter"/>. The
/// browser works the state out from these; the server only serves them.
/// </summary>
public sealed class ActivityOptions
{
    public const string Section = "Panopticon:Activity";
    public const string IdleAfterKey = Section + ":IdleAfter";
    public const string StaleAfterKey = Section + ":StaleAfter";

    public static readonly TimeSpan DefaultIdleAfter = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromHours(4);

    public TimeSpan IdleAfter { get; set; } = DefaultIdleAfter;

    public TimeSpan StaleAfter { get; set; } = DefaultStaleAfter;

    /// <summary>Null when valid, otherwise the start-up error, naming the settings involved.</summary>
    public string? Problem() =>
        IdleAfter <= TimeSpan.Zero ? $"{IdleAfterKey} must be a positive TimeSpan."
        : StaleAfter <= TimeSpan.Zero ? $"{StaleAfterKey} must be a positive TimeSpan."
        : StaleAfter <= IdleAfter ? $"{StaleAfterKey} ({StaleAfter}) must be longer than {IdleAfterKey} ({IdleAfter})."
        : null;
}

/// <summary>Start-up validation of <see cref="ActivityOptions"/>, with the message saying which setting is wrong.</summary>
public sealed class ActivityOptionsValidation : IValidateOptions<ActivityOptions>
{
    public ValidateOptionsResult Validate(string? name, ActivityOptions options) =>
        options.Problem() is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
}

/// <summary>The deployment's settings the web app needs (<c>GET /api/settings</c>), in whole seconds.</summary>
public sealed record DashboardSettings(long IdleAfterSeconds, long StaleAfterSeconds)
{
    public static DashboardSettings From(ActivityOptions activity) =>
        new((long)activity.IdleAfter.TotalSeconds, (long)activity.StaleAfter.TotalSeconds);
}
