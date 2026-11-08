using Microsoft.Extensions.Logging;

namespace Panopticon.Cli.Bridge;

/// <summary>
/// One-line-per-entry logs to a writer (stderr for <c>panopticon mcp</c>, where stdout is the MCP protocol). Small
/// and reflection-free, unlike the console logger's options binding.
/// </summary>
internal sealed class TextWriterLoggerProvider(TextWriter output) : ILoggerProvider
{
    private readonly Lock _gate = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        lock (_gate)
        {
            output.WriteLine(line);
            output.Flush();
        }
    }

    private sealed class Logger(TextWriterLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"panopticon {Label(logLevel)} {category}: {formatter(state, exception)}";
            provider.Write(exception is null ? line : $"{line}{Environment.NewLine}{exception}");
        }

        private static string Label(LogLevel level) => level switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            _ => "crit",
        };
    }
}
