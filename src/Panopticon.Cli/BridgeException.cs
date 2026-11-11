using System.Net;

namespace Panopticon.Cli;

/// <summary>
/// A failure the agent should see as a <c>report_progress</c> tool error, or that <c>panopticon hook</c> logs, or a
/// command prints. Its message is shown as-is, so it must never hold a token or remote-URL credentials.
/// </summary>
internal sealed class BridgeException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>The server's status code, when the server answered with a refusal (the REST hook endpoints).</summary>
    public HttpStatusCode? StatusCode { get; init; }
}
