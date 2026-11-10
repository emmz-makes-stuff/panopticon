using System.Net;
using System.Net.Http.Json;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>Setting up Projects through the API, as a signed-in browser would (spec projects).</summary>
public static class ProjectTestHelpers
{
    /// <summary>Creates a Project as this browser's member, who becomes its owner, and returns its id.</summary>
    public static async Task<Guid> CreateProjectAsync(this HttpClient browser, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(browser);
        using var created = await browser.PostApiAsync(ProjectEndpoints.ProjectsPath, new { name = code, code }, cancellationToken);
        if (created.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Creating {code} answered {created.StatusCode}.");
        }

        return (await created.Content.ReadFromJsonAsync<ProjectView>(cancellationToken))!.Id;
    }

    /// <summary>Invites an address into a Project this browser's member owns (making them a member of the server too).</summary>
    public static async Task InviteToProjectAsync(
        this HttpClient browser,
        Guid projectId,
        string email,
        string role,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(browser);
        using var invited = await browser.PostApiAsync(ProjectEndpoints.MembersPath(projectId), new { email, role }, cancellationToken);
        if (invited.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Inviting {email} answered {invited.StatusCode}.");
        }
    }
}
