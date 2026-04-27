namespace ComplianceApp.Infrastructure.Authentication;

/// <summary>
/// Claim names used by both the dev token issuer and the current-user resolver,
/// kept in one place so the two stay in sync.
/// </summary>
public static class ComplianceAppClaimTypes
{
    /// <summary>Standard JWT subject claim — the user's id.</summary>
    public const string Subject = "sub";

    /// <summary>Cognito-style custom claim carrying the user's tenant.</summary>
    public const string OrganisationId = "custom:organisationId";
}
