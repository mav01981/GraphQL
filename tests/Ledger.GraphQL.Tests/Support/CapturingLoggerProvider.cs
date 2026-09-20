using Microsoft.Extensions.Logging;

namespace Ledger.GraphQL.Tests.Support;

/// <summary>Keeps the log records emitted by the hosted application so tests (and failures) can
/// assert on the SQL statements the resolvers actually issued.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _records = [];
    private readonly object _gate = new();

    public IReadOnlyList<string> Records
    {
        get
        {
            lock (_gate)
            {
                return [.. _records];
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _records.Clear();
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal void Record(string message)
    {
        lock (_gate)
        {
            _records.Add(message);
        }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

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

            var message = formatter(state, exception);
            provider.Record($"[{logLevel}] {categoryName}: {message}{(exception is null ? string.Empty : $" :: {exception}")}");
        }
    }
}