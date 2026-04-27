using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ComplianceApp.Infrastructure.Authentication;

/// <summary>
/// Issues HS256 JWTs from the dev symmetric key. Phase 1 stand-in for Cognito —
/// the JWT bearer middleware validates these against the same key/issuer/audience.
/// </summary>
public class DevTokenIssuer : IDevTokenIssuer
{
    private readonly DevAuthOptions _options;
    private readonly TimeProvider _timeProvider;

    public DevTokenIssuer(IOptions<DevAuthOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public DevToken Issue(Guid userId, Guid organisationId)
    {
        var now = _timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_options.TokenLifetimeMinutes);

        var claims = new[]
        {
            new Claim(ComplianceAppClaimTypes.Subject, userId.ToString()),
            new Claim(ComplianceAppClaimTypes.OrganisationId, organisationId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);

        return new DevToken(encoded, expires);
    }
}
