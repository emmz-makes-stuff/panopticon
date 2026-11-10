namespace Panopticon.Server.Auth;

/// <summary>
/// Who the configured initial members are (projects D8): the operators, who alone may remove members from the server.
/// Read from <c>Panopticon:InitialMembers</c> on each call, with the same parsing as start-up seeding.
/// </summary>
public sealed class InitialMemberList(IConfiguration configuration)
{
    /// <summary>Whether the address (by its <see cref="MemberEmail.Key"/>) is an initial member's.</summary>
    public bool Contains(string emailKey) =>
        InitialMembers.Read(configuration).Any(address => MemberEmail.Key(address) == emailKey);
}
