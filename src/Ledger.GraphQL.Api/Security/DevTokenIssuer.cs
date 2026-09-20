using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.GraphQL.Api.Security;

/// <summary>
/// Hand-rolled dev STS (spec §5): mints a JWT carrying a single role claim for the demo roles. It is
/// enough to demonstrate field-level authorization without standing up Duende or a real IdP.
/// </summary>
public sealed class DevTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    public string Issue(string role, TimeSpan? lifetime = null)
    {
        var settings = options.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, $"demo-{role}"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(ClaimTypes.Name, $"demo-{role}"),
                new Claim(ClaimTypes.Role, role),
            ],
            notBefore: now,
            expires: now.Add(lifetime ?? TimeSpan.FromMinutes(settings.TokenLifetimeMinutes)),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}