using System.Text.Json;

namespace Panopticon.Contracts;

/// <summary>
/// Reads a string property from loosely shaped JSON (hook events, tool inputs), for the CLI and the contracts alike
/// (code review §9.3). Anything that isn't an object with that property as a JSON string reads as missing.
/// </summary>
public static class JsonText
{
    /// <summary><paramref name="property"/> of <paramref name="element"/> as a string, or null.</summary>
    public static string? Get(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(property, out var field)
        && field.ValueKind == JsonValueKind.String
            ? field.GetString()
            : null;

    /// <summary>As <see cref="Get"/>, but an empty or all-whitespace string reads as missing too.</summary>
    public static string? NonBlank(JsonElement? element, string property) =>
        Get(element, property) is { } text && !string.IsNullOrWhiteSpace(text) ? text : null;
}
