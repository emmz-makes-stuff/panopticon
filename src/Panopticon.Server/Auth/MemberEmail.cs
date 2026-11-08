using System.Net.Mail;

namespace Panopticon.Server.Auth;

/// <summary>How member emails are matched and checked (spec team-members, "Members are identified by email").</summary>
public static class MemberEmail
{
    public const int MaxLength = 320;

    /// <summary>The address as given, trimmed.</summary>
    public static string Clean(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim();
    }

    /// <summary>The case-insensitive key that identifies a member.</summary>
    public static string Key(string email) => Clean(email).ToLowerInvariant();

    /// <summary>
    /// Deliberately loose (design D10): one <c>@</c> with something either side, no whitespace, and a shape
    /// <see cref="MailAddress"/> accepts as a bare address. The real check is whether the member can sign in.
    /// </summary>
    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var cleaned = email.Trim();
        var at = cleaned.IndexOf('@', StringComparison.Ordinal);
        if (cleaned.Length > MaxLength
            || at <= 0
            || at == cleaned.Length - 1
            || cleaned.IndexOf('@', at + 1) >= 0
            || cleaned.Any(char.IsWhiteSpace))
        {
            return false;
        }

        return MailAddress.TryCreate(cleaned, out var parsed) && parsed.Address == cleaned;
    }

    /// <summary>The default display name: the part before <c>@</c>, cut to the display-name limit.</summary>
    public static string DefaultDisplayName(string email)
    {
        var local = Clean(email).Split('@')[0];
        return local.Length <= MemberNames.MaxLength ? local : local[..MemberNames.MaxLength];
    }
}

/// <summary>Display-name rules (spec team-members, "Display names").</summary>
public static class MemberNames
{
    public const int MaxLength = 50;

    /// <summary>The trimmed name, or null when it's blank or too long.</summary>
    public static string? Validate(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength ? null : trimmed;
    }
}
