using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Panopticon.Server.Auth;
using Panopticon.Server.Data;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Auth;

// specs/team-members: "Members are identified by email" and "Initial members from configuration" (design D10).
public class InitialMembersTests(PostgresFixture postgres, S3Fixture s3)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PanopticonFactory> FactoryAsync(string? initialMembers, string? connectionString = null) =>
        new(connectionString ?? await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct))
        {
            Settings = new Dictionary<string, string> { [InitialMembers.ConfigKey] = initialMembers ?? string.Empty },
        };

    [Fact]
    public async Task First_start_adds_each_initial_member_with_a_default_name()
    {
        await using var factory = await FactoryAsync(" Emmz@Example.com , sam@example.com");
        factory.CreateClient().Dispose();

        await using var db = factory.NewDb();
        var members = await db.Members.OrderBy(m => m.EmailKey).ToListAsync(Ct);
        Assert.Collection(
            members,
            emmz => Assert.Equal(("Emmz@Example.com", "emmz@example.com", "Emmz", true), (emmz.Email, emmz.EmailKey, emmz.DisplayName, emmz.IsActive)),
            sam => Assert.Equal(("sam@example.com", "sam"), (sam.Email, sam.DisplayName)));
    }

    [Fact]
    public async Task Restarting_adds_nobody_twice_and_matches_case_insensitively()
    {
        var connectionString = await postgres.CreateDatabaseAsync(Ct);
        await using (var first = await FactoryAsync("emmz@example.com", connectionString))
        {
            first.CreateClient().Dispose();
        }

        await using var second = await FactoryAsync("EMMZ@example.com ", connectionString);
        second.CreateClient().Dispose();

        await using var db = second.NewDb();
        Assert.Equal("emmz@example.com", Assert.Single(await db.Members.ToListAsync(Ct)).EmailKey);
    }

    [Fact]
    public async Task Removed_initial_member_stays_removed()
    {
        var connectionString = await postgres.CreateDatabaseAsync(Ct);
        await using (var db = PostgresFixture.CreateDb(connectionString))
        {
            db.Members.AddRange(Member("emmz@example.com", removed: true), Member("sam@example.com", removed: false));
            await db.SaveChangesAsync(Ct);
        }

        await using var factory = await FactoryAsync("emmz@example.com", connectionString);
        factory.CreateClient().Dispose();

        await using var after = factory.NewDb();
        var emmz = await after.Members.SingleAsync(m => m.EmailKey == "emmz@example.com", Ct);
        Assert.False(emmz.IsActive);
    }

    [Fact]
    public async Task Nobody_can_sign_in_fails_start_up_naming_the_setting()
    {
        await using var factory = await FactoryAsync(initialMembers: null);

        var error = Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Panopticon:InitialMembers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_removed_members_and_no_initial_members_fails_start_up()
    {
        var connectionString = await postgres.CreateDatabaseAsync(Ct);
        await using (var db = PostgresFixture.CreateDb(connectionString))
        {
            db.Members.Add(Member("emmz@example.com", removed: true));
            await db.SaveChangesAsync(Ct);
        }

        await using var factory = await FactoryAsync(initialMembers: null, connectionString);

        Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task An_invalid_initial_member_fails_start_up_naming_it()
    {
        await using var factory = await FactoryAsync("emmz@example.com, not-an-email");

        var error = Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("not-an-email", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Initial_members_read_as_a_list_or_one_separated_value()
    {
        var list = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{InitialMembers.ConfigKey}:0"] = "a@example.com",
            [$"{InitialMembers.ConfigKey}:1"] = "b@example.com",
        }).Build();
        var joined = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [InitialMembers.ConfigKey] = "a@example.com; b@example.com,A@example.com",
        }).Build();

        Assert.Equal(["a@example.com", "b@example.com"], InitialMembers.Read(list));
        Assert.Equal(["a@example.com", "b@example.com"], InitialMembers.Read(joined));
    }

    [Theory]
    [InlineData("emmz@example.com", true)]
    [InlineData(" emmz@example.com ", true)]
    [InlineData("not-an-email", false)]
    [InlineData("@example.com", false)]
    [InlineData("emmz@", false)]
    [InlineData("em mz@example.com", false)]
    [InlineData("a@b@example.com", false)]
    [InlineData("Emmz <emmz@example.com>", false)]
    [InlineData("", false)]
    public void Email_validity_is_deliberately_loose(string email, bool valid) =>
        Assert.Equal(valid, MemberEmail.IsValid(email));

    private static Member Member(string email, bool removed) => new()
    {
        Email = email,
        EmailKey = email,
        DisplayName = email.Split('@')[0],
        CreatedAt = DateTimeOffset.UnixEpoch,
        RemovedAt = removed ? DateTimeOffset.UnixEpoch : null,
    };
}
