using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Cli.Bridge;

/// <summary>What <c>.panopticon.json</c> holds. Unknown fields are ignored, so later changes can add to it.</summary>
internal sealed record ProjectLinkFile(JsonElement? Project);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProjectLinkFile))]
internal sealed partial class ProjectLinkJsonContext : JsonSerializerContext;

/// <summary>
/// The repo's link to a Project (projects D7; spec cli-bridge, Project link file): <c>.panopticon.json</c> at the repo
/// root, read on every report with no cache, so an edit takes effect at once.
/// </summary>
internal static class ProjectLink
{
    public const string FileName = ".panopticon.json";

    private const string Shape = "It should look like {\"project\": \"<id>\"}, with an id that list_projects gives.";

    /// <summary>
    /// The Project id the repo is linked to, or null when there's no link file. A file that's there but malformed is a
    /// <see cref="BridgeException"/>, never an unlinked report, so a typo can't hide work in a personal scope.
    /// </summary>
    public static async Task<Guid?> ReadAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var path = Path.Combine(repoRoot, FileName);
        ProjectLinkFile? file;
        try
        {
            await using var stream = File.OpenRead(path);
            file = await JsonSerializer.DeserializeAsync(stream, ProjectLinkJsonContext.Default.ProjectLinkFile, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (JsonException exception)
        {
            throw new BridgeException($"{FileName} isn't valid JSON ({exception.Message}). {Shape}", exception);
        }
        catch (IOException exception)
        {
            throw new BridgeException($"{FileName} couldn't be read: {exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new BridgeException($"{FileName} couldn't be read: {exception.Message}", exception);
        }

        if (file?.Project is not { } project)
        {
            throw new BridgeException($"{FileName} has no \"project\". {Shape}");
        }

        return project.ValueKind == JsonValueKind.String && Guid.TryParse(project.GetString(), out var id)
            ? id
            : throw new BridgeException($"{FileName}'s \"project\" must be a Project id from list_projects, not {project.GetRawText()}. {Shape}");
    }
}
