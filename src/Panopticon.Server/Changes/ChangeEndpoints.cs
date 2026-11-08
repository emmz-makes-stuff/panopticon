using Microsoft.AspNetCore.Http.HttpResults;
using Panopticon.Server.Auth;

namespace Panopticon.Server.Changes;

/// <summary>
/// The change page and history read API (change-documents-and-history D9; specs: change-documents, change-history),
/// for signed-in members only.
/// </summary>
public static class ChangeEndpoints
{
    public const string HistoryPath = "/api/history";

    public static string ChangePath(Guid id) => $"/api/changes/{id}";

    public static string DocumentPath(Guid id, string path) => $"/api/changes/{id}/documents/{path}";

    public static IEndpointRouteBuilder MapChanges(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/changes/{id:guid}",
            async Task<Results<Ok<ChangePage>, NotFound>> (Guid id, ChangePages pages, CancellationToken cancellationToken) =>
                await pages.GetAsync(id, cancellationToken) is { } page ? TypedResults.Ok(page) : TypedResults.NotFound())
            .RequireAuthorization(AuthPolicies.Member);

        endpoints.MapGet(
            "/api/changes/{id:guid}/documents/{**path}",
            async Task<Results<Ok<RenderedDocument>, NotFound>> (Guid id, string path, ChangePages pages, CancellationToken cancellationToken) =>
                await pages.RenderAsync(id, path, cancellationToken) is { } document ? TypedResults.Ok(document) : TypedResults.NotFound())
            .RequireAuthorization(AuthPolicies.Member);

        endpoints.MapGet(
            HistoryPath,
            async Task<Results<Ok<HistoryPage>, BadRequest<string>>> (DateTimeOffset? before, int? limit, ChangePages pages, CancellationToken cancellationToken) =>
            {
                var size = limit ?? ChangePages.HistoryPageSize;
                if (size is < 1 or > ChangePages.HistoryPageSize)
                {
                    return TypedResults.BadRequest($"limit must be between 1 and {ChangePages.HistoryPageSize}.");
                }

                return TypedResults.Ok(await pages.HistoryAsync(before, size, cancellationToken));
            })
            .RequireAuthorization(AuthPolicies.Member);

        return endpoints;
    }
}
