namespace Panopticon.Cli.Bridge;

/// <summary>
/// The changes this bridge has reported on in this session, each with the client name that last reported it, and the
/// repo they live in (change-documents-and-history D4). The <see cref="ArchiveWatcher"/> watches exactly these.
/// </summary>
internal sealed class ReportedChanges
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _clients = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource<string> _repoRoot = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes with the repo root once the first report succeeds: before then there's nothing to watch.</summary>
    public Task<string> RepoRoot => _repoRoot.Task;

    /// <summary>Notes a successful report on an active change, by the client that sent it.</summary>
    public void Record(string repoRoot, string change, string clientName)
    {
        lock (_gate)
        {
            _clients[change] = clientName;
        }

        _repoRoot.TrySetResult(repoRoot);
    }

    /// <summary>Stops watching a change: it has been reported archived.</summary>
    public void Forget(string change)
    {
        lock (_gate)
        {
            _clients.Remove(change);
        }
    }

    /// <summary>The changes and their clients, as they are now.</summary>
    public IReadOnlyList<(string Change, string Client)> Snapshot()
    {
        lock (_gate)
        {
            return [.. _clients.Select(entry => (entry.Key, entry.Value))];
        }
    }
}
