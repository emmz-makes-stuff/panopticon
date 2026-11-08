namespace Panopticon.Server.Auth;

/// <summary>Authorization policies (team-auth D4).</summary>
public static class AuthPolicies
{
    /// <summary>A signed-in member, by session cookie: the dashboard and its APIs.</summary>
    public const string Member = "Member";

    /// <summary>A reporter, by bearer token: the online MCP endpoint.</summary>
    public const string Reporter = "Reporter";
}
