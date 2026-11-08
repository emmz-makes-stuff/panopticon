using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>A logger that keeps what it's given, for asserting on log output.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    public IReadOnlyList<(LogLevel Level, string Message)> Entries => [.. _entries];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
