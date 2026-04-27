using ComplianceApp.Application.Common.Authentication;
using Microsoft.AspNetCore.Http;

namespace ComplianceApp.Infrastructure.Authentication;

/// <summary>
/// Reads the authenticated user's id + organisation id from the bearer token's
/// claims via <see cref="IHttpContextAccessor"/>. Returns nulls (rather than
/// throwing) when there is no HTTP context — that lets background workers and
/// integration tests use the same Application layer without faking auth.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId => GetGuidClaim(ComplianceAppClaimTypes.Subject);

    public Guid? OrganisationId => GetGuidClaim(ComplianceAppClaimTypes.OrganisationId);

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    private Guid? GetGuidClaim(string claimType)
    {
        var value = _httpContextAccessor.HttpContext?.User?.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var guid) ? guid : null;
    }
}
