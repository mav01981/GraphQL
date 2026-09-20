using Microsoft.AspNetCore.Authorization;

namespace Ledger.GraphQL.Api.Security;

/// <summary>
/// The policy and role vocabulary of the demo (spec §5). Two things matter here:
/// the policies are evaluated per field, not per endpoint, and they are cumulative —
/// an auditor sees everything an accountant sees, an accountant everything a viewer sees.
/// </summary>
public static class AuthPolicies
{
    public const string ViewBalance = "ViewBalance";
    public const string ViewAmount = "ViewAmount";
    public const string ViewCounterparty = "ViewCounterparty";

    public const string Viewer = "viewer";
    public const string Accountant = "accountant";
    public const string Auditor = "auditor";

    public static IReadOnlyList<string> AllRoles { get; } = [Viewer, Accountant, Auditor];

    public static AuthorizationBuilder AddLedgerPolicies(this AuthorizationBuilder builder)
    {
        builder.AddPolicy(ViewBalance, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context => HasAnyRole(context, Accountant, Auditor)));

        builder.AddPolicy(ViewAmount, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context => HasAnyRole(context, Accountant, Auditor)));

        builder.AddPolicy(ViewCounterparty, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context => HasAnyRole(context, Auditor)));

        return builder;
    }

    private static bool HasAnyRole(AuthorizationHandlerContext context, params string[] roles) =>
        roles.Any(context.User.IsInRole);
}