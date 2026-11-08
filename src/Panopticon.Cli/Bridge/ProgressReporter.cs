using Microsoft.Extensions.Logging;
using Panopticon.Cli.Git;
using Panopticon.Contracts;

namespace Panopticon.Cli.Bridge;

/// <summary>
/// Builds a report from the local working copy (repo identity, the change's tasks.md, the agent) and forwards it.
/// </summary>
internal sealed partial class ProgressReporter(
    BridgeSettings settings,
    ServerForwarder forwarder,
    ILogger<ProgressReporter> logger)
{
    /// <summary>Used when the agent gave no client name at initialisation.</summary>
    public const string UnknownClient = "unknown-client";

    public async Task<ReportProgressResult> ReportAsync(
        string? clientName,
        string change,
        int? section,
        CancellationToken cancellationToken)
    {
        try
        {
            // Configuration first (and only once), so a misconfigured bridge says so before touching the working copy.
            var (endpoint, token) = settings.RequireServer();

            var root = await GitRepository.FindRootAsync(settings.WorkingDirectory, cancellationToken);
            var repo = ParseRemote(await GitRepository.GetOriginUrlAsync(root, cancellationToken));
            var tasks = await ChangeDocuments.ReadTasksAsync(root, change, cancellationToken);
            var agent = new AgentRef(
                string.IsNullOrWhiteSpace(clientName) ? UnknownClient : clientName.Trim(),
                settings.MachineName);

            var result = await forwarder.ForwardAsync(
                endpoint,
                token,
                new ReportProgressArguments(repo, change, agent, tasks, section),
                cancellationToken);

            LogReported(change, result.Overall.Done, result.Overall.Total, result.CurrentSection?.Number);

            return result;
        }
        catch (BridgeException exception)
        {
            LogFailed(change, exception.Message);
            throw;
        }
    }

    private static RepoRef ParseRemote(string originUrl)
    {
        try
        {
            return GitRemote.Parse(originUrl);
        }
        catch (FormatException exception)
        {
            throw new BridgeException(exception.Message, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reported change {Change}: overall {Done}/{Total}, current section {Section}")]
    private partial void LogReported(string change, int done, int total, int? section);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Report for change {Change} failed: {Reason}")]
    private partial void LogFailed(string change, string reason);
}
