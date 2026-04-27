namespace ComplianceApp.Infrastructure.Authentication;

/// <summary>
/// Issues short-lived JWTs signed with the dev symmetric key.
/// Only registered when <see cref="DevAuthOptions.Enabled"/> is true.
/// </summary>
public interface IDevTokenIssuer
{
    DevToken Issue(Guid userId, Guid organisationId);
}

public record DevToken(string AccessToken, DateTimeOffset ExpiresAt);
