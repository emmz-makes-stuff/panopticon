using Panopticon.Cli.Git;
using Panopticon.Contracts;

namespace Panopticon.Cli.Tests.Git;

// Spec cli-bridge, "Repo identity from git remote". ("No origin remote" needs a repo: see GitRepositoryTests.)
public class GitRemoteTests
{
    [Theory]
    // Spec scenarios
    [InlineData("git@github.com:emmz/panopticon.git", "github.com", "emmz", "panopticon")]
    [InlineData("https://github.com/emmz/panopticon", "github.com", "emmz", "panopticon")]
    [InlineData("https://gitlab.com/acme/platform/api.git", "gitlab.com", "acme/platform", "api")]
    [InlineData("https://dev.azure.com/acme/Nova/_git/nova-api", "dev.azure.com", "acme/Nova", "nova-api")]
    [InlineData("git@ssh.dev.azure.com:v3/acme/Nova/nova-api", "dev.azure.com", "acme/Nova", "nova-api")]
    [InlineData("https://user:secret@github.com/emmz/panopticon.git", "github.com", "emmz", "panopticon")]
    // Other forms of the same remotes
    [InlineData("ssh://git@github.com/emmz/panopticon.git", "github.com", "emmz", "panopticon")]
    [InlineData("ssh://git@gitlab.example.com:2222/acme/platform/api.git", "gitlab.example.com", "acme/platform", "api")]
    [InlineData("github.com:emmz/panopticon", "github.com", "emmz", "panopticon")]
    [InlineData("https://acme@dev.azure.com/acme/Nova/_git/nova-api", "dev.azure.com", "acme/Nova", "nova-api")]
    [InlineData("ssh://git@ssh.dev.azure.com/v3/acme/Nova/nova-api", "dev.azure.com", "acme/Nova", "nova-api")]
    [InlineData("http://git.local:8080/team/tool.git/", "git.local", "team", "tool")]
    [InlineData("git://example.org/team/tool.git", "example.org", "team", "tool")]
    [InlineData("  https://GitHub.com/emmz/panopticon.git\n", "github.com", "emmz", "panopticon")]
    public void Parses_the_identity(string url, string host, string owner, string name)
    {
        Assert.Equal(new RepoRef(host, owner, name), GitRemote.Parse(url));
    }

    [Fact]
    public void A_v3_segment_is_kept_outside_Azure_DevOps_SSH()
    {
        Assert.Equal(new RepoRef("gitlab.com", "v3/acme", "api"), GitRemote.Parse("git@gitlab.com:v3/acme/api.git"));
    }

    [Theory]
    [InlineData("/srv/git/panopticon.git")]
    [InlineData("../panopticon")]
    [InlineData("file:///srv/git/panopticon.git")]
    [InlineData(@"C:\src\panopticon")]
    [InlineData("https://github.com/panopticon")]
    [InlineData("git@github.com:panopticon.git")]
    [InlineData("")]
    public void Rejects_what_it_cannot_identify(string url)
    {
        Assert.Throws<FormatException>(() => GitRemote.Parse(url));
    }

    [Fact]
    public void Credentials_never_reach_the_identity_or_an_error()
    {
        var identity = GitRemote.Parse("https://user:secret@github.com/emmz/panopticon.git");
        Assert.DoesNotContain("secret", $"{identity}", StringComparison.Ordinal);
        Assert.DoesNotContain("user", $"{identity}", StringComparison.Ordinal);

        var error = Assert.Throws<FormatException>(() => GitRemote.Parse("https://user:secret@github.com/panopticon"));
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }
}
