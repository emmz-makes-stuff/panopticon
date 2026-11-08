namespace Panopticon.Cli;

/// <summary>
/// A failure the agent should see as a <c>report_progress</c> tool error. Its message is shown to the agent as-is, so
/// it must never hold a token or remote-URL credentials.
/// </summary>
internal sealed class BridgeException(string message, Exception? innerException = null)
    : Exception(message, innerException);
