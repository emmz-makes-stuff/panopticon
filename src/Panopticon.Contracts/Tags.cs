using System.Diagnostics.CodeAnalysis;

namespace Panopticon.Contracts;

/// <summary>
/// A tag (tags D1; spec tags, Tag syntax): a key, optionally with a value, as in <c>bug</c> or <c>area: onboarding</c>.
/// Two tags are equal when their keys and values are, without regard to case; each keeps the casing first given.
/// </summary>
public sealed record Tag
{
    public Tag(string key, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
        Value = value;
    }

    public string Key { get; }

    /// <summary>Null for a bare key.</summary>
    public string? Value { get; }

    /// <summary>The key lower-cased, as tags are matched.</summary>
    public string KeyKey => Key.ToLowerInvariant();

    /// <summary>The value lower-cased, or the empty string for a bare key, as tags are matched and stored.</summary>
    public string ValueKey => Value?.ToLowerInvariant() ?? string.Empty;

    /// <summary>The canonical text: <c>key</c>, or <c>key: value</c> with one space after the colon.</summary>
    public string Text => Value is null ? Key : $"{Key}: {Value}";

    public bool Equals(Tag? other) =>
        other is not null
        && string.Equals(KeyKey, other.KeyKey, StringComparison.Ordinal)
        && string.Equals(ValueKey, other.ValueKey, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(KeyKey, ValueKey);

    public override string ToString() => Text;
}

/// <summary>Reading tags from text, with the same rules and messages in the CLI and on the server (tags D1).</summary>
public static class TagText
{
    /// <summary>The longest a key, or a value, may be.</summary>
    public const int MaxPartLength = 40;

    /// <summary>The most personal tags a config, or a report, may carry.</summary>
    public const int MaxPersonalTags = 20;

    /// <summary>The most tags a Project may define.</summary>
    public const int MaxProjectTags = 200;

    /// <summary>The most tags one filter may choose.</summary>
    public const int MaxFilterTags = 20;

    /// <summary>
    /// Reads <paramref name="text"/> as a tag. The text and both parts are trimmed, and the first ':' splits key from
    /// value. On failure, <paramref name="error"/> names the tag and the rule it breaks.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Tag? tag, [NotNullWhen(false)] out string? error)
    {
        tag = null;
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            error = "A tag can't be empty.";
            return false;
        }

        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        var key = (colon < 0 ? trimmed : trimmed[..colon]).Trim();
        var value = colon < 0 ? null : trimmed[(colon + 1)..].Trim();

        error = KeyProblem(key) is { } keyProblem
            ? $"\"{trimmed}\": {keyProblem}"
            : value is not null && ValueProblem(value) is { } valueProblem
                ? $"\"{trimmed}\": {valueProblem}"
                : null;
        if (error is not null)
        {
            return false;
        }

        tag = new Tag(key, value);
        return true;
    }

    /// <summary>
    /// Reads every text in <paramref name="texts"/>, collapsing duplicates (the first casing wins), and refusing more
    /// than <paramref name="max"/> distinct tags. On failure, <paramref name="error"/> says why.
    /// </summary>
    public static bool TryParseAll(
        IEnumerable<string> texts,
        int max,
        [NotNullWhen(true)] out IReadOnlyList<Tag>? tags,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(texts);
        tags = null;
        var parsed = new List<Tag>();
        foreach (var text in texts)
        {
            if (!TryParse(text, out var tag, out error))
            {
                return false;
            }

            if (!parsed.Contains(tag))
            {
                parsed.Add(tag);
            }
        }

        if (parsed.Count > max)
        {
            error = $"There are {parsed.Count} tags, and at most {max} are allowed.";
            return false;
        }

        error = null;
        tags = parsed;
        return true;
    }

    private static string? KeyProblem(string key)
    {
        if (key.Length == 0)
        {
            return "a tag needs a key before the ':'.";
        }

        if (key.Length > MaxPartLength)
        {
            return $"a key can be at most {MaxPartLength} characters.";
        }

        return key.All(IsKeyChar) ? null : "a key can only have letters, digits, '-', '_' and '.'.";
    }

    private static string? ValueProblem(string value)
    {
        if (value.Length == 0)
        {
            return "there's no value after the ':'. Leave the ':' out for a tag with no value.";
        }

        if (value.Length > MaxPartLength)
        {
            return $"a value can be at most {MaxPartLength} characters.";
        }

        return value.Any(c => c is '\r' or '\n') ? "a value can't contain a line break." : null;
    }

    private static bool IsKeyChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.';
}
