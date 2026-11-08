using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>Everything a test server logs, with its category and level.</summary>
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<(string Category, LogLevel Level, string Message)> _entries = new();

    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Entries => [.. _entries];

    /// <summary>All messages as one string, for "is this anywhere in the log" checks.</summary>
    public string Text => string.Join('\n', _entries.Select(e => e.Message));

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<(string, LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                entries.Enqueue((category, logLevel, formatter(state, exception) + (exception is null ? "" : $" {exception.GetType().Name}: {exception.Message}")));
            }
        }
    }
}
