using System.Text;
using Panopticon.Contracts;

namespace Panopticon.Server.Distribution;

/// <summary>
/// The install scripts, filled in per request from the catalogue and the public URL (design D3), so a script needs no
/// JSON parsing and can only install its own server's release.
/// </summary>
public static class CliInstallScripts
{
    private static readonly Lazy<string> _shTemplate = new(() => ReadTemplate("install.sh"));
    private static readonly Lazy<string> _ps1Template = new(() => ReadTemplate("install.ps1"));

    /// <summary>The <c>sh</c> installer for Linux and macOS.</summary>
    public static string RenderSh(CliCatalogue catalogue, Uri root)
    {
        if (catalogue.IsEmpty)
        {
            throw new InvalidOperationException("There are no CLI builds to install.");
        }

        var cases = new StringBuilder();
        foreach (var build in catalogue.Builds.Values.Where(b => !b.Platform.StartsWith("win-", StringComparison.Ordinal)))
        {
            cases.Append(FormattableString.Invariant($"    {build.Platform}) url={ShQuote(CliEndpoints.DownloadUrl(root, build))}; sha256={ShQuote(build.Sha256)} ;;\n"));
        }

        return _shTemplate.Value
            .Replace("@@SERVER_DISPLAY@@", ServerText(root), StringComparison.Ordinal)
            .Replace("@@SERVER@@", ShQuote(ServerText(root)), StringComparison.Ordinal)
            .Replace("@@VERSION@@", catalogue.Version!, StringComparison.Ordinal)
            .Replace("@@PLATFORM_CASES@@\n", cases.ToString(), StringComparison.Ordinal)
            .Replace("@@PLATFORM_LIST@@", PlatformList(catalogue), StringComparison.Ordinal);
    }

    /// <summary>The PowerShell installer for Windows.</summary>
    public static string RenderPs1(CliCatalogue catalogue, Uri root)
    {
        if (catalogue.IsEmpty)
        {
            throw new InvalidOperationException("There are no CLI builds to install.");
        }

        var windows = catalogue.Builds.GetValueOrDefault(CliPlatforms.WinX64);
        return _ps1Template.Value
            .Replace("@@SERVER_DISPLAY@@", ServerText(root), StringComparison.Ordinal)
            .Replace("@@SERVER@@", PsQuote(ServerText(root)), StringComparison.Ordinal)
            .Replace("@@VERSION@@", catalogue.Version!, StringComparison.Ordinal)
            .Replace("@@WIN_URL@@", PsQuote(windows is null ? "" : CliEndpoints.DownloadUrl(root, windows)), StringComparison.Ordinal)
            .Replace("@@WIN_SHA256@@", PsQuote(windows?.Sha256 ?? ""), StringComparison.Ordinal)
            .Replace("@@PLATFORM_LIST@@", PlatformList(catalogue), StringComparison.Ordinal);
    }

    /// <summary>The server as people type it in <c>panopticon login --server</c>: the public URL without a trailing slash.</summary>
    public static string ServerText(Uri root) => root.ToString().TrimEnd('/');

    /// <summary>A single-quoted PowerShell string: nothing in it is expanded.</summary>
    public static string PsQuote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string PlatformList(CliCatalogue catalogue) => string.Join(", ", catalogue.Builds.Keys.Order(StringComparer.Ordinal));

    /// <summary>A single-quoted <c>sh</c> word: nothing in it is expanded.</summary>
    public static string ShQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string ReadTemplate(string name)
    {
        var resource = $"Panopticon.Server.Distribution.{name}";
        using var stream = typeof(CliInstallScripts).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The embedded install script {resource} is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
