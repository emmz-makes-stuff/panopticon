using System.Net.Http.Json;

namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>Calls the API the way the dashboard does: JSON, with the request header that marks it (design D6).</summary>
public static class ApiRequests
{
    public const string RequestHeader = "X-Panopticon-Request";

    public static Task<HttpResponseMessage> PostApiAsync(this HttpClient http, string path, object? body, CancellationToken cancellationToken) =>
        http.SendApiAsync(HttpMethod.Post, path, body, cancellationToken);

    public static async Task<HttpResponseMessage> SendApiAsync(
        this HttpClient http,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(RequestHeader, "1");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, cancellationToken);
    }
}
