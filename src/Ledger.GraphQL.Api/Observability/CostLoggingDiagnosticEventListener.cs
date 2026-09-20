using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using Ledger.GraphQL.Api.Graph;

namespace Ledger.GraphQL.Api.Observability;

/// <summary>
/// Logs the analysed operation cost for every request (spec §7: "query cost logged per request").
/// The number in the log is the same one the cost limit is enforced against, which is what makes the
/// "we bounded this" claim in the interview story checkable rather than hand-waved.
/// <para>
/// The logger factory is resolved per request from the request scope rather than cached: the
/// schema-scope container deliberately does not fall back to application services (see
/// <see cref="SchemaDiagnosticSink"/>), and a cached factory would bind this schema-wide singleton to
/// whichever host built first — wrong for parallel test hosts and for schema warmup ordering.
/// </para>
/// </summary>
public sealed class CostLoggingDiagnosticEventListener : ExecutionDiagnosticEventListener
{
    public override void OperationCost(RequestContext context, double fieldCost, double typeCost)
    {
        var loggerFactory = context.RequestServices.GetService<ILoggerFactory>()
                            ?? SchemaDiagnosticSink.LoggerFactory;

        loggerFactory.CreateLogger<CostLoggingDiagnosticEventListener>().LogInformation(
            "GraphQL operation cost: fieldCost={FieldCost}, typeCost={TypeCost}",
            fieldCost,
            typeCost);
    }
}