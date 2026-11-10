using Microsoft.AspNetCore.Http.HttpResults;
using Panopticon.Server.Auth;
using Panopticon.Server.Projects;

namespace Panopticon.Server.Changes;

/// <summary>
/// The change page and history read API (change-documents-and-history D9; specs: change-documents, change-history),
/// for signed-in members only.
/// </summary>
public static class ChangeEndpoints
{
    public const string HistoryPath = "/api/history";

    /// <summary>The history's filter choices, among archived changes (filters-and-idle-cards D5).</summary>
    public const string HistoryFiltersPath = "/api/history/filters";

    public static string ChangePath(Guid id) => $"/api/changes/{id}";

    public static string DocumentPath(Guid id, string path) => $"/api/changes/{id}/documents/{path}";

    public static IEndpointRouteBuilder MapChanges(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/changes/{id:guid}",
            async Task<Results<Ok<ChangePage>, NotFound>> (Guid id, Viewer viewer, ChangePages pages, CancellationToken cancellationToken) =>
                await pages.GetAsync(id, viewer, cancellationToken) is { } page ? TypedResults.Ok(page) : TypedResults.NotFound())
            .RequireAuthorization(AuthPolicies.Member);

        endpoints.MapGet(
            "/api/changes/{id:guid}/documents/{**path}",
            async Task<Results<Ok<RenderedDocument>, NotFound>> (Guid id, string path, Viewer viewer, ChangePages pages, CancellationToken cancellationToken) =>
                await pages.RenderAsync(id, path, viewer, cancellationToken) is { } document ? TypedResults.Ok(document) : TypedResults.NotFound())
            .RequireAuthorization(AuthPolicies.Member);

        endpoints.MapGet(
            HistoryPath,
            async Task<Results<Ok<HistoryPage>, BadRequest<string>>> (
                DateTimeOffset? before,
                int? limit,
                string? repo,
                string? owner,
                string? member,
                string? machine,
                string? client,
                string? project,
                string[]? tag,
                Viewer viewer,
                ChangePages pages,
                CancellationToken cancellationToken) =>
            {
                var size = limit ?? ChangePages.HistoryPageSize;
                if (size is < 1 or > ChangePages.HistoryPageSize)
                {
                    return TypedResults.BadRequest($"limit must be between 1 and {ChangePages.HistoryPageSize}.");
                }

                if (!HistoryFilter.TryParse(repo, owner, member, machine, client, project, tag, out var filter, out var error))
                {
                    return TypedResults.BadRequest(error);
                }

                return TypedResults.Ok(await pages.HistoryAsync(filter, before, size, viewer, cancellationToken));
            })
            .RequireAuthorization(AuthPolicies.Member);

        endpoints.MapGet(
            HistoryFiltersPath,
            async (Viewer viewer, ChangePages pages, CancellationToken cancellationToken) =>
                TypedResults.Ok(await pages.HistoryChoicesAsync(viewer, cancellationToken)))
            .RequireAuthorization(AuthPolicies.Member);

        return endpoints;
    }
}
