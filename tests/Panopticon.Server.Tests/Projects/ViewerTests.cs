using Panopticon.Server.Data;
using Panopticon.Server.Projects;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Projects;

// agent-interaction D5; spec remote-answers, Who may answer.
public class ViewerTests(PostgresFixture postgres, S3Fixture s3)
{
    private static readonly Guid _emmz = Guid.NewGuid();
    private static readonly Guid _bob = Guid.NewGuid();
    private static readonly Guid _pan = Guid.NewGuid();
    private static readonly Guid _acme = Guid.NewGuid();
    private static readonly ChangeScope _panChange = new(_pan, null);

    [Fact]
    public void A_member_may_answer_their_own_sessions_wherever_they_are()
    {
        var emmz = new Viewer(_emmz, [_pan]);

        Assert.True(emmz.CanAnswer(_emmz, _panChange));
        Assert.True(emmz.CanAnswer(_emmz, new ChangeScope(null, _emmz)));
        Assert.True(emmz.CanAnswer(_emmz, ChangeScope.Bootstrap));
    }

    [Fact]
    public void An_owner_may_answer_others_sessions_on_their_Projects_changes_only()
    {
        var bob = new Viewer(_bob, [_pan, _acme], [_pan]);

        Assert.True(bob.CanAnswer(_emmz, _panChange));
        Assert.False(bob.CanAnswer(_emmz, new ChangeScope(_acme, null)));
        Assert.False(bob.CanAnswer(_emmz, new ChangeScope(null, _emmz)));
        Assert.False(bob.CanAnswer(_emmz, ChangeScope.Bootstrap));
    }

    [Fact]
    public void An_editor_or_viewer_may_not_answer_others_sessions()
    {
        var sam = new Viewer(Guid.NewGuid(), [_pan]);

        Assert.False(sam.CanAnswer(_emmz, _panChange));
    }

    [Fact]
    public void Ownership_of_a_Project_the_member_isnt_in_is_ignored()
    {
        var bob = new Viewer(_bob, [], [_pan]);

        Assert.Empty(bob.OwnedProjectIds);
        Assert.False(bob.CanAnswer(_emmz, _panChange));
    }

    [Fact]
    public void A_role_change_promotes_demotes_or_removes()
    {
        var bob = new Viewer(_bob, [_pan]);

        var promoted = bob.WithProject(_pan, ProjectRoles.Owner);
        Assert.Equal([_pan], promoted.ProjectIds);
        Assert.Equal([_pan], promoted.OwnedProjectIds);

        var demoted = promoted.WithProject(_pan, ProjectRoles.Editor);
        Assert.Equal([_pan], demoted.ProjectIds);
        Assert.Empty(demoted.OwnedProjectIds);

        var left = promoted.WithProject(_pan, role: null);
        Assert.Empty(left.ProjectIds);
        Assert.Empty(left.OwnedProjectIds);

        var joined = left.WithProject(_acme, ProjectRoles.Viewer);
        Assert.Equal([_acme], joined.ProjectIds);
        Assert.Empty(joined.OwnedProjectIds);
    }

    [Fact]
    public async Task Loading_a_viewer_reads_which_Projects_they_own()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var world = await TagWorld.CreateAsync(postgres, s3, ct);
        await using var db = world.Factory.NewDb();

        var emmz = await Viewer.LoadAsync(db, await world.Factory.MemberIdAsync(cancellationToken: ct), ct);
        var sam = await Viewer.LoadAsync(db, await world.Factory.MemberIdAsync(TagWorld.SamEmail, ct), ct);

        Assert.Equal([world.Pan], emmz.ProjectIds);
        Assert.Equal([world.Pan], emmz.OwnedProjectIds);
        Assert.Equal([world.Pan], sam.ProjectIds);
        Assert.Empty(sam.OwnedProjectIds);
    }
}
