using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Panopticon.Cli.Config;
using Panopticon.Cli.Git;
using Panopticon.Contracts;

namespace Panopticon.Cli.Bridge;

/// <summary>
/// Builds a report from the local working copy (repo identity, the change's folder or its archive, the agent) and
/// forwards it.
/// </summary>
internal sealed partial class ProgressReporter(
    BridgeSettings settings,
    ServerForwarder forwarder,
    ReportedChanges reported,
    ILogger<ProgressReporter> logger)
{
    /// <summary>Used when the agent gave no client name at initialisation.</summary>
    public const string UnknownClient = "unknown-client";

    // One report at a time per change (spec cli-bridge, One report at a time per change): the watcher's archived report
    // waits for a report already read from the active folder, so it can't overtake it and leave a fresh active change.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _changeLocks = new(StringComparer.Ordinal);

    public async Task<ReportProgressResult> ReportAsync(
        string? clientName,
        string change,
        int? section,
        CancellationToken cancellationToken)
    {
        var gate = _changeLocks.GetOrAdd(change, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReportLockedAsync(clientName, change, section, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ReportProgressResult> ReportLockedAsync(
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
            var folder = await ChangeDocuments.ReadAsync(root, change, cancellationToken);
            var project = await ProjectLink.ReadAsync(root, cancellationToken);
            var agent = new AgentRef(
                string.IsNullOrWhiteSpace(clientName) ? UnknownClient : clientName.Trim(),
                settings.MachineName);

            var result = await forwarder.ForwardAsync(
                endpoint,
                token,
                new ReportProgressArguments(repo, change, agent, folder.Tasks, section, folder.Documents, folder.Archive, project?.ToString())
                {
                    PersonalTags = ReadPersonalTags(),
                },
                settings.TokenFromEnvironment,
                cancellationToken);

            // The archive watcher follows active changes reported in this session, and lets go once one is archived.
            if (folder.Archive is { } archive)
            {
                reported.Forget(change);
                LogReportedArchived(change, archive, result.Overall.Done, result.Overall.Total);
                return result with { Archived = archive };
            }
            else
            {
                reported.Record(root, change, agent.Client);
                LogReported(change, result.Overall.Done, result.Overall.Total, result.CurrentSection?.Number);
            }

            return result;
        }
        catch (BridgeException exception)
        {
            LogFailed(change, exception.Message);
            throw;
        }
    }

    /// <summary>
    /// The config's personal tags as they are now, in canonical text; an empty list when there are none, which clears
    /// the agent's tags on the change (tags D5, D8).
    /// - **A file that can't be read** gives null: the report goes without the field, so the server keeps the tags
    ///   it has, and a warning names the file (never its content). A broken file neither blocks reports nor wipes
    ///   tags (Emmz, 2026-11-10).
    /// - **A file that holds a tag that isn't one** fails the report, naming the file and the tag, never the token
    ///   (spec cli-bridge, A malformed file).
    /// </summary>
    private IReadOnlyList<string>? ReadPersonalTags()
    {
        if (settings.Config is not { } config)
        {
            return [];
        }

        CliConfig? saved;
        try
        {
            saved = config.ReadStrict();
        }
        catch (InvalidDataException exception)
        {
            LogTagsUnreadable(exception.Message);
            return null;
        }

        if (!TagText.TryParseAll(saved?.Tags ?? [], TagText.MaxPersonalTags, out var tags, out var error))
        {
            throw new BridgeException(
                $"The personal tags in {config.Path} aren't valid: {error} Fix them with 'panopticon tags', or edit the file.");
        }

        return [.. tags.Select(t => t.Text)];
    }

    /// <summary>The repo identity from an origin URL; one that isn't a repo URL is a <see cref="BridgeException"/>.</summary>
    internal static RepoRef ParseRemote(string originUrl)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Reported change {Change} archived as {Archive}: overall {Done}/{Total}")]
    private partial void LogReportedArchived(string change, string archive, int done, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Problem} The report was sent without personal tags, so the server keeps the ones it has.")]
    private partial void LogTagsUnreadable(string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Report for change {Change} failed: {Reason}")]
    private partial void LogFailed(string change, string reason);
}
