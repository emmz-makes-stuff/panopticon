using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Panopticon.Server.Auth;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Auth;

// Review fixes 12.4 and 12.5: removal from the server and the Project membership writes take their locks in one order
// (Projects, then members), and a membership write locks the member's row, so none of them deadlock or leave a removed
// member in a Project. Each test holds a lock from a raw connection, as the racing request would, while the endpoint
// runs and waits on it.
public class MembershipLockingTests(PostgresFixture postgres, S3Fixture s3)
{
    private const string Sam = "sam@example.com";
    private const string Kit = "kit@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PanopticonFactory> FactoryAsync() =>
        new(await postgres.CreateDatabaseAsync(Ct), await s3.CreateBucketAsync(Ct), bootstrapToken: null);

    private static Task<HttpResponseMessage> RemoveAsync(HttpClient http, Guid id) =>
        http.SendApiAsync(HttpMethod.Delete, $"{MemberEndpoints.MembersPath}/{id}", null, Ct);

    // Before 12.4 the removal locked the members first and then waited on PAN, while an invite holding PAN waited on the
    // members through its foreign keys: a deadlock, and a 500.
    [Fact]
    public async Task A_removal_and_an_invite_on_the_same_Project_dont_deadlock()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        var pan = await emmz.CreateProjectAsync("PAN", Ct);
        await emmz.InviteToProjectAsync(pan, Sam, "owner", Ct);
        await emmz.InviteToProjectAsync(await emmz.CreateProjectAsync("OTH", Ct), Kit, "viewer", Ct);
        var (emmzId, samId, kitId) = (await factory.MemberIdAsync(cancellationToken: Ct), await factory.MemberIdAsync(Sam, Ct), await factory.MemberIdAsync(Kit, Ct));

        await using var invite = await OpenAsync(factory);
        await using var transaction = await invite.BeginTransactionAsync(Ct);
        await ExecuteAsync(invite, $"SELECT 1 FROM projects WHERE id = '{pan}' FOR UPDATE");

        var removal = RemoveAsync(emmz, samId);
        await WaitForLockWaitAsync(factory);
        await ExecuteAsync(
            invite,
            $"INSERT INTO project_members (project_id, member_id, role, joined_at, invited_by_member_id) VALUES ('{pan}', '{kitId}', 'viewer', now(), '{emmzId}')");
        await transaction.CommitAsync(Ct);

        using var removed = await removal;
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }

    // A membership committed after the removal locked its Projects is in a Project it didn't lock: refused, not deleted.
    [Fact]
    public async Task A_membership_that_arrives_mid_removal_makes_the_removal_a_409()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        await emmz.InviteToProjectAsync(await emmz.CreateProjectAsync("PAN", Ct), Sam, "viewer", Ct);
        var qqq = await emmz.CreateProjectAsync("QQQ", Ct);
        var (emmzId, samId) = (await factory.MemberIdAsync(cancellationToken: Ct), await factory.MemberIdAsync(Sam, Ct));

        // As an invite does: the member's row, then the insert.
        await using var invite = await OpenAsync(factory);
        await using var transaction = await invite.BeginTransactionAsync(Ct);
        await ExecuteAsync(invite, $"SELECT 1 FROM members WHERE id = '{samId}' FOR NO KEY UPDATE");
        await ExecuteAsync(
            invite,
            $"INSERT INTO project_members (project_id, member_id, role, joined_at, invited_by_member_id) VALUES ('{qqq}', '{samId}', 'owner', now(), '{emmzId}')");

        var removal = RemoveAsync(emmz, samId);
        await WaitForLockWaitAsync(factory);
        await transaction.CommitAsync(Ct);

        using var refused = await removal;
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        await using var db = factory.NewDb();
        Assert.Null((await db.Members.SingleAsync(m => m.Id == samId, Ct)).RemovedAt);
        Assert.Equal(2, await db.ProjectMembers.CountAsync(m => m.MemberId == samId, Ct));
    }

    // Before 12.5 the invite read the member unlocked, saw them active, and added a removed member to the Project.
    [Fact]
    public async Task An_invite_racing_a_removal_makes_the_member_active_again_as_specified()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        await emmz.InviteToProjectAsync(await emmz.CreateProjectAsync("PAN", Ct), Sam, "viewer", Ct);
        var qqq = await emmz.CreateProjectAsync("QQQ", Ct);
        var samId = await factory.MemberIdAsync(Sam, Ct);

        // As a removal does: the member's row, removed_at, and their memberships.
        await using var removal = await OpenAsync(factory);
        await using var transaction = await removal.BeginTransactionAsync(Ct);
        await ExecuteAsync(removal, $"SELECT 1 FROM members WHERE id = '{samId}' FOR NO KEY UPDATE");
        await ExecuteAsync(removal, $"UPDATE members SET removed_at = now() WHERE id = '{samId}'");
        await ExecuteAsync(removal, $"DELETE FROM project_members WHERE member_id = '{samId}'");

        var invite = emmz.InviteToProjectAsync(qqq, Sam, "owner", Ct);
        await WaitForLockWaitAsync(factory);
        await transaction.CommitAsync(Ct);
        await invite;

        await using var db = factory.NewDb();
        Assert.Null((await db.Members.SingleAsync(m => m.Id == samId, Ct)).RemovedAt);
        Assert.Equal(qqq, (await db.ProjectMembers.SingleAsync(m => m.MemberId == samId, Ct)).ProjectId);
    }

    [Fact]
    public async Task A_member_removed_while_creating_a_Project_doesnt_get_one()
    {
        await using var factory = await FactoryAsync();
        using var emmz = await factory.CreateSignedInClientAsync(cancellationToken: Ct);
        await emmz.InviteToProjectAsync(await emmz.CreateProjectAsync("PAN", Ct), Sam, "viewer", Ct);
        using var sam = await factory.CreateSignedInClientAsync(Sam, Ct);
        var samId = await factory.MemberIdAsync(Sam, Ct);

        await using var removal = await OpenAsync(factory);
        await using var transaction = await removal.BeginTransactionAsync(Ct);
        await ExecuteAsync(removal, $"SELECT 1 FROM members WHERE id = '{samId}' FOR NO KEY UPDATE");
        await ExecuteAsync(removal, $"UPDATE members SET removed_at = now() WHERE id = '{samId}'");

        var create = sam.PostApiAsync(ProjectEndpoints.ProjectsPath, new { name = "Sam's", code = "SAM" }, Ct);
        await WaitForLockWaitAsync(factory);
        await transaction.CommitAsync(Ct);

        using var refused = await create;
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        await using var db = factory.NewDb();
        Assert.False(await db.Projects.AnyAsync(p => p.Code == "SAM", Ct));
    }

    private static async Task<NpgsqlConnection> OpenAsync(PanopticonFactory factory)
    {
        var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
#pragma warning disable CA2100 // Test SQL built from ids the test made.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>Waits until some backend on this database is waiting for a row lock: the endpoint has reached it.</summary>
    private static async Task WaitForLockWaitAsync(PanopticonFactory factory)
    {
        await using var probe = await OpenAsync(factory);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'",
                probe);
            if ((long)(await command.ExecuteScalarAsync(Ct))! > 0)
            {
                return;
            }

            await Task.Delay(25, Ct);
        }

        Assert.Fail("The request never waited on the lock.");
    }
}
