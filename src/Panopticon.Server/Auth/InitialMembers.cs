using Microsoft.EntityFrameworkCore;
using Panopticon.Server.Data;

namespace Panopticon.Server.Auth;

/// <summary>
/// Seeds <c>Panopticon:InitialMembers</c> at start-up (spec team-members, "Initial members from configuration";
/// design D10). An address that already has a row, removed or not, is left alone. Start-up fails when nobody could
/// sign in.
/// </summary>
public sealed partial class InitialMembers(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<InitialMembers> logger) : IHostedService
{
    public const string ConfigKey = "Panopticon:InitialMembers";

    /// <summary>
    /// The configured addresses: a list (<c>Panopticon:InitialMembers:0</c>, …) or one comma- or semicolon-separated
    /// value, which is easier to set as an environment variable.
    /// </summary>
    public static IReadOnlyList<string> Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(ConfigKey);
        var values = section.GetChildren().Select(child => child.Value).Append(section.Value);
        return [.. values
            .SelectMany(value => (value ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var addresses = Read(configuration);
        var invalid = addresses.Where(address => !MemberEmail.IsValid(address)).ToList();
        if (invalid.Count > 0)
        {
            throw new InvalidOperationException($"{ConfigKey} has an invalid email address: {string.Join(", ", invalid)}.");
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PanopticonDb>();

        var keys = addresses.Select(MemberEmail.Key).ToList();
        var known = await db.Members.Where(m => keys.Contains(m.EmailKey)).Select(m => m.EmailKey).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        foreach (var address in addresses.Where(a => !known.Contains(MemberEmail.Key(a))))
        {
            var email = MemberEmail.Clean(address);
            db.Members.Add(new Member
            {
                Email = email,
                EmailKey = MemberEmail.Key(address),
                DisplayName = MemberEmail.DefaultDisplayName(address),
                CreatedAt = now,
            });
            LogSeeded(email);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another instance seeded the same address first; what's there now is what counts.
            db.ChangeTracker.Clear();
        }

        if (!await db.Members.AnyAsync(m => m.RemovedAt == null, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Nobody can sign in: there are no active members and {ConfigKey} lists none. Set {ConfigKey} to the first members' email addresses.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Added initial member {Email}")]
    private partial void LogSeeded(string email);
}
