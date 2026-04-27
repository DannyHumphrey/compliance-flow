using ComplianceApp.Application.Common.Authentication;

namespace ComplianceApp.Api.IntegrationTests.Persistence;

/// <summary>
/// Mutable stub so a single test can switch tenant identity between queries
/// without rebuilding the whole DI graph.
/// </summary>
public class StubCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }

    public Guid? OrganisationId { get; set; }

    public bool IsAuthenticated => UserId.HasValue;
}
