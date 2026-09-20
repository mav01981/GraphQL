using System.Diagnostics;
using System.Globalization;
using Ledger.ReadModel.Telemetry;
using Microsoft.Extensions.Options;

namespace Ledger.GraphQL.Api.Observability;

/// <summary>
/// Opens a <see cref="SqlQueryCounter"/> scope for the duration of an HTTP request, publishes the
/// resulting statement count as <c>x-sql-queries</c> and logs a single summary line per request.
/// This is how the naive-vs-batched query counts in <c>docs/benchmark-results.md</c> were measured.
/// </summary>
public sealed class SqlQueryCountMiddleware(
    RequestDelegate next,
    SqlQueryCounter counter,
    IOptions<ObservabilityOptions> options,
    ILogger<SqlQueryCountMiddleware> logger)
{
    public const string HeaderName = "x-sql-queries";

    public async Task InvokeAsync(HttpContext context)
    {
        using var scope = counter.BeginScope();
        var stopwatch = Stopwatch.StartNew();

        if (options.Value.ExposeSqlQueryCount)
        {
            // Registered before the response starts so the header is part of the current response.
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = counter.Count.ToString(CultureInfo.InvariantCulture);
                return Task.CompletedTask;
            });
        }

        await next(context);

        stopwatch.Stop();

        logger.LogInformation(
            "HTTP {Method} {Path} → {StatusCode} in {ElapsedMs:0.0} ms, {SqlStatements} SQL statement(s)",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            stopwatch.Elapsed.TotalMilliseconds,
            counter.Count);
    }
}

public static class SqlQueryCountMiddlewareExtensions
{
    public static IApplicationBuilder UseSqlQueryCountLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<SqlQueryCountMiddleware>();
}