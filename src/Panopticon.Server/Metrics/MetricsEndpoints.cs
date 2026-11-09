using Microsoft.AspNetCore.Http.HttpResults;
using Panopticon.Server.Auth;
using Panopticon.Server.Changes;

namespace Panopticon.Server.Metrics;

/// <summary>The metrics read API (change-metrics D4), for signed-in members only.</summary>
public static class MetricsEndpoints
{
    public const string MetricsPath = "/api/metrics";

    public static string ChangeMetricsPath(Guid id) => $"/api/changes/{id}/metrics";

    public static IEndpointRouteBuilder MapMetrics(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/changes/{id:guid}/metrics",
            async Task<Results<Ok<ChangeMetrics>, NotFound>> (Guid id, MetricsReader metrics, CancellationToken cancellationToken) =>
                await metrics.ForChangeAsync(id, cancellationToken) is { } found ? TypedResults.Ok(found) : TypedResults.NotFound())
            .RequireAuthorization(AuthPolicies.Member);

        // The history's filters, parsed the same way, so a malformed value is a 400 naming it.
        endpoints.MapGet(
            MetricsPath,
            async Task<Results<Ok<TeamMetrics>, BadRequest<string>>> (
                string? repo,
                string? owner,
                string? member,
                string? machine,
                string? client,
                MetricsReader metrics,
                CancellationToken cancellationToken) =>
                HistoryFilter.TryParse(repo, owner, member, machine, client, out var filter, out var error)
                    ? TypedResults.Ok(await metrics.ForTeamAsync(filter, cancellationToken))
                    : TypedResults.BadRequest(error))
            .RequireAuthorization(AuthPolicies.Member);

        return endpoints;
    }
}
