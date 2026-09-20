using Microsoft.Extensions.Logging.Abstractions;

namespace Ledger.GraphQL.Api.Observability;

/// <summary>
/// HotChocolate builds the services that back its request pipeline (diagnostic event listeners,
/// request middleware) in its own schema-scope container, which deliberately does not fall back to
/// the application container — so a listener cannot take <c>ILogger&lt;T&gt;</c> or
/// <c>IOptions&lt;T&gt;</c> as a constructor dependency.
/// <para>
/// The composition root therefore hands the host's logger factory to the listener through this sink
/// once, at startup. Nothing else in the application uses it.
/// </para>
/// </summary>
public static class SchemaDiagnosticSink
{
    private static ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    public static ILoggerFactory LoggerFactory
    {
        get => _loggerFactory;
        set => _loggerFactory = value ?? NullLoggerFactory.Instance;
    }
}