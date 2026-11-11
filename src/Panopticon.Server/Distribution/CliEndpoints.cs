using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Panopticon.Contracts;
using Panopticon.Server.Auth;

namespace Panopticon.Server.Distribution;

/// <summary>
/// The CLI's version manifest and downloads (spec cli-distribution, design D4). None of them needs sign-in: a fresh
/// machine has no token yet, and the CLI does nothing without an approved login.
/// </summary>
public static class CliEndpoints
{
    public const string InstallShPath = "/install.sh";
    public const string InstallPs1Path = "/install.ps1";

    private static readonly string[] _getAndHead = [HttpMethods.Get, HttpMethods.Head];

    public static WebApplication MapCliDistribution(this WebApplication app)
    {
        // HEAD as well as GET, so a HEAD gets these answers and never falls through to the app shell (onboarding-guide D6).
        app.MapMethods(CliPlatforms.ManifestPath, _getAndHead, Manifest);
        app.MapMethods("/cli/{platform}/{file}", _getAndHead, Download);
        app.MapMethods(InstallShPath, _getAndHead, (HttpContext context, CliCatalogue catalogue, IOptions<PublicUrlOptions> publicUrl, IHostEnvironment environment) =>
            InstallScript(context, catalogue, publicUrl, environment, CliInstallScripts.RenderSh));
        app.MapMethods(InstallPs1Path, _getAndHead, (HttpContext context, CliCatalogue catalogue, IOptions<PublicUrlOptions> publicUrl, IHostEnvironment environment) =>
            InstallScript(context, catalogue, publicUrl, environment, CliInstallScripts.RenderPs1));
        return app;
    }

    internal static IResult Manifest(
        HttpContext context,
        CliCatalogue catalogue,
        IOptions<PublicUrlOptions> publicUrl,
        IHostEnvironment environment)
    {
        if (catalogue.IsEmpty)
        {
            return SignInEndpoints.Problem(HttpStatusCode.NotFound, CliCatalogue.NoBuildsMessage);
        }

        var root = SignInEndpoints.LinkRoot(publicUrl.Value, context, environment);
        var platforms = catalogue.Builds.Values.ToDictionary(
            b => b.Platform,
            b => new CliPlatformBuild(DownloadUrl(root, b), b.Size, b.Sha256),
            StringComparer.Ordinal);
        return TypedResults.Ok(new CliManifest(
            catalogue.Version!,
            new Uri(root, InstallShPath.TrimStart('/')).ToString(),
            new Uri(root, InstallPs1Path.TrimStart('/')).ToString(),
            platforms));
    }

    internal static IResult InstallScript(
        HttpContext context,
        CliCatalogue catalogue,
        IOptions<PublicUrlOptions> publicUrl,
        IHostEnvironment environment,
        Func<CliCatalogue, Uri, string> render)
    {
        if (catalogue.IsEmpty)
        {
            return NotFoundText(CliCatalogue.NoBuildsMessage);
        }

        var root = SignInEndpoints.LinkRoot(publicUrl.Value, context, environment);
        return Script(context, render(catalogue, root));
    }

    // Scripts are text, and never cached: each one names the release the server holds now.
    private static Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult Script(HttpContext context, string script)
    {
        context.Response.Headers.CacheControl = "no-cache";
        return TypedResults.Text(script, "text/plain; charset=utf-8");
    }

    internal static IResult Download(HttpContext context, string platform, string file, CliCatalogue catalogue)
    {
        if (catalogue.IsEmpty)
        {
            return NotFoundText(CliCatalogue.NoBuildsMessage);
        }

        if (!catalogue.Builds.TryGetValue(platform, out var build) || !string.Equals(file, build.FileName, StringComparison.Ordinal))
        {
            return NotFoundText($"There's no CLI build at /cli/{platform}/{file}.");
        }

        // The URL carries no version, so a cache must check back each time: stale bytes would fail the manifest's
        // checksum. The ETag is the build's SHA-256, so it changes exactly when the bytes do, and a revalidation of the
        // same build gets a 304 (onboarding-guide D7).
        context.Response.Headers.CacheControl = "no-cache";
        return TypedResults.PhysicalFile(
            build.FilePath,
            "application/octet-stream",
            build.FileName,
            lastModified: build.LastModified,
            entityTag: new EntityTagHeaderValue($"\"{build.Sha256}\""));
    }

    /// <summary>A build's absolute download URL.</summary>
    public static string DownloadUrl(Uri root, CliBuild build) =>
        new Uri(root, $"cli/{build.Platform}/{build.FileName}").ToString();

    internal static IResult NotFoundText(string message) =>
        TypedResults.Text(message + "\n", "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
}
