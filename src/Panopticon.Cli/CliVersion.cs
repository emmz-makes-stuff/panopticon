using System.Reflection;

namespace Panopticon.Cli;

/// <summary>
/// The CLI's version (design D5): the release build stamps it with <c>-p:Version=X.Y.Z</c>; a local build is
/// <c>0.0.0-dev</c> from the project file.
/// </summary>
internal static class CliVersion
{
    /// <summary>The informational version, which the SDK may suffix with <c>+&lt;commit&gt;</c>.</summary>
    public static string Informational { get; } =
        typeof(CliVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>The version people and the server compare: the informational version without its build metadata.</summary>
    public static string Current { get; } = WithoutBuildMetadata(Informational);

    /// <summary>Drops any <c>+&lt;metadata&gt;</c> suffix, so <c>0.2.0+abc123</c> reads <c>0.2.0</c>.</summary>
    public static string WithoutBuildMetadata(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
