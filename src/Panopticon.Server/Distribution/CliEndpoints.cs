using System.Net;
using Microsoft.Extensions.Options;
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

    public static WebApplication MapCliDistribution(this WebApplication app)
    {
        app.MapGet(CliPlatforms.ManifestPath, Manifest);
        app.MapGet("/cli/{platform}/{file}", Download);
        app.MapGet(InstallShPath, (HttpContext context, CliCatalogue catalogue, IOptions<PublicUrlOptions> publicUrl, IHostEnvironment environment) =>
            InstallScript(context, catalogue, publicUrl, environment, CliInstallScripts.RenderSh));
        app.MapGet(InstallPs1Path, (HttpContext context, CliCatalogue catalogue, IOptions<PublicUrlOptions> publicUrl, IHostEnvironment environment) =>
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

    internal static IResult Download(string platform, string file, CliCatalogue catalogue)
    {
        if (catalogue.IsEmpty)
        {
            return NotFoundText(CliCatalogue.NoBuildsMessage);
        }

        if (!catalogue.Builds.TryGetValue(platform, out var build) || !string.Equals(file, build.FileName, StringComparison.Ordinal))
        {
            return NotFoundText($"There's no CLI build at /cli/{platform}/{file}.");
        }

        return TypedResults.PhysicalFile(build.FilePath, "application/octet-stream", build.FileName);
    }

    /// <summary>A build's absolute download URL.</summary>
    public static string DownloadUrl(Uri root, CliBuild build) =>
        new Uri(root, $"cli/{build.Platform}/{build.FileName}").ToString();

    internal static IResult NotFoundText(string message) =>
        TypedResults.Text(message + "\n", "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
}
