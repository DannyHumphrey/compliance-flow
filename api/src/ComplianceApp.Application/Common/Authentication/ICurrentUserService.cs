namespace ComplianceApp.Application.Common.Authentication;

/// <summary>
/// Resolves the caller's identity for the current request.
/// Implemented in Infrastructure by reading JWT claims from the HTTP context.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>The authenticated user's id (from the JWT <c>sub</c> claim), or null if not authenticated.</summary>
    Guid? UserId { get; }

    /// <summary>The user's organisation id (from the <c>custom:organisationId</c> claim), or null if not authenticated.</summary>
    Guid? OrganisationId { get; }

    /// <summary>True when the request carries a valid bearer token.</summary>
    bool IsAuthenticated { get; }
}
