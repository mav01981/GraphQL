using Ledger.GraphQL.Api.Security;

namespace Ledger.GraphQL.Api.Security;

/// <summary>
/// Development-only token endpoint. Stands in for a real STS so a reviewer can grab a viewer,
/// accountant or auditor token in one call and immediately see the field-level authorization
/// difference on the same query.
/// </summary>
public static class DevTokenEndpoints
{
    public static IEndpointRouteBuilder MapDevTokenEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dev/token/{role}", IssueAsync)
            .AllowAnonymous()
            .WithName("IssueDevToken");

        app.MapPost("/dev/token/{role}", IssueAsync)
            .AllowAnonymous();

        return app;
    }

    private static IResult IssueAsync(string role, DevTokenIssuer issuer, int? lifetimeMinutes)
    {
        var normalized = role.ToLowerInvariant();

        if (!AuthPolicies.AllRoles.Contains(normalized))
        {
            return Results.BadRequest(new
            {
                error = $"Unknown role '{role}'.",
                knownRoles = AuthPolicies.AllRoles,
            });
        }

        var lifetime = TimeSpan.FromMinutes(Math.Clamp(lifetimeMinutes ?? 60, 1, 24 * 60));

        return Results.Ok(new
        {
            role = normalized,
            accessToken = issuer.Issue(normalized, lifetime),
            expiresInMinutes = lifetime.TotalMinutes,
            usage = $"Authorization: Bearer <accessToken>",
        });
    }
}