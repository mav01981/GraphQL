using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ledger.GraphQL.Api.Observability;

namespace Ledger.GraphQL.Tests.Support;

/// <summary>A GraphQL response plus the evidence the tests assert on: status, payload size and the
/// number of SQL statements the request issued (published as <c>x-sql-queries</c> by
/// <see cref="SqlQueryCountMiddleware"/> when <c>Observability:ExposeSqlQueryCount</c> is on).</summary>
public sealed record GraphQLResponse(HttpStatusCode StatusCode, JsonElement Root, int ByteCount, int SqlStatements)
{
    public JsonElement Data => Root.GetProperty("data");

    public bool HasErrors => Root.TryGetProperty("errors", out _);

    public IReadOnlyList<string> ErrorCodes =>
        Errors
            .Select(error => error.TryGetProperty("extensions", out var extensions)
                             && extensions.TryGetProperty("code", out var code)
                ? code.GetString() ?? string.Empty
                : string.Empty)
            .ToList();

    public IReadOnlyList<string> ErrorMessages =>
        Errors.Select(error => error.GetProperty("message").GetString() ?? string.Empty).ToList();

    public IReadOnlyList<JsonElement> Errors =>
        Root.TryGetProperty("errors", out var errors)
            ? errors.EnumerateArray().ToList()
            : [];
}

public static class GraphQLTestClientExtensions
{
    /// <summary>Posts a GraphQL document as a test client and returns the parsed response.</summary>
    public static async Task<GraphQLResponse> PostGraphQLAsync(
        this HttpClient client,
        string query,
        object? variables = null)
    {
        var payload = new Dictionary<string, object?> { ["query"] = query };
        if (variables is not null)
        {
            payload["variables"] = variables;
        }

        using var response = await client.PostAsJsonAsync("/graphql", payload);
        var body = await response.Content.ReadAsStringAsync();

        var sqlStatements = response.Headers.TryGetValues(SqlQueryCountMiddleware.HeaderName, out var values)
                            && int.TryParse(values.FirstOrDefault(), out var count)
            ? count
            : -1;

        // 401/403 responses carry no body at all; keep the helper total so tests can assert on status.
        var root = body.StartsWith('{') || body.StartsWith('[')
            ? JsonDocument.Parse(body).RootElement.Clone()
            : default;

        return new GraphQLResponse(response.StatusCode, root, body.Length, sqlStatements);
    }
}
