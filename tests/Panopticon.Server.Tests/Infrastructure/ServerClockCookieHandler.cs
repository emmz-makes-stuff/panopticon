using System.Globalization;
using System.Text.RegularExpressions;

namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>
/// Moves each <c>Set-Cookie</c> expiry from the server's clock onto the real one, keeping its distance from "now".
/// The cookie container judges expiry by the real clock, so a cookie set under a <see cref="ManualClock"/> date would
/// otherwise expire when real time passes that date (on 2026-11-15 every session cookie set at the 2026-11-08 start
/// did). The server still checks its own expiry, so a test that advances the clock past a session's end is unchanged.
/// </summary>
public sealed partial class ServerClockCookieHandler(TimeProvider serverClock) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return response;
        }

        var shift = DateTimeOffset.UtcNow - serverClock.GetUtcNow();
        var moved = cookies.Select(cookie => Expires().Replace(cookie, match =>
            DateTimeOffset.TryParse(match.Groups["date"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires)
                ? $"{match.Groups["name"].Value}{(expires + shift).ToUniversalTime().ToString("R", CultureInfo.InvariantCulture)}"
                : match.Value)).ToList();
        response.Headers.Remove("Set-Cookie");
        response.Headers.TryAddWithoutValidation("Set-Cookie", moved);
        return response;
    }

    [GeneratedRegex("(?<name>expires=)(?<date>[^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Expires();
}
