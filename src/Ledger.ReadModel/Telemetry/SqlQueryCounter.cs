namespace Ledger.ReadModel.Telemetry;

/// <summary>
/// Counts the SQL statements issued while executing a single GraphQL request. The scope is carried in
/// an <see cref="AsyncLocal{T}"/> so concurrent requests do not interfere with each other; the
/// mutable state object itself is shared between the scope owner and the interceptor, which is what
/// allows the count to be read back after <c>await next()</c>.
/// </summary>
public sealed class SqlQueryCounter
{
    private const int MaxTrackedStatements = 8;

    private static readonly AsyncLocal<CounterState?> Current = new();

    /// <summary>Number of SQL statements executed in the current scope (0 outside of a scope).</summary>
    public int Count => Current.Value?.Count ?? 0;

    /// <summary>The most recent statements in the current scope, for query logging.</summary>
    public static IReadOnlyList<string> Statements => Current.Value?.Statements ?? [];

    /// <summary>Starts counting for the current async flow and returns a scope that restores the
    /// previous counter on dispose.</summary>
    public IDisposable BeginScope()
    {
        var previous = Current.Value;
        Current.Value = new CounterState();
        return new Scope(previous);
    }

    internal void Record(string statement)
    {
        var state = Current.Value;
        if (state is null)
        {
            return;
        }

        state.Count++;
        if (state.Statements.Count < MaxTrackedStatements)
        {
            state.Statements.Add(statement);
        }
    }

    private sealed class CounterState
    {
        public int Count { get; set; }

        public List<string> Statements { get; } = [];
    }

    private sealed class Scope(CounterState? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Current.Value = previous;
        }
    }
}