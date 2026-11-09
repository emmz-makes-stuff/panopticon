namespace Panopticon.Server.Data;

/// <summary>
/// The lower-cased twin of an identity value (design D6): what the <c>*Key</c> columns hold, and what reports and
/// filters match on, so identity is case-insensitive in one place.
/// </summary>
public static class IdentityKey
{
    public static string Of(string value) => value.Trim().ToLowerInvariant();
}
