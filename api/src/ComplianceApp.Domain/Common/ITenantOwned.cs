namespace ComplianceApp.Domain.Common;

public interface ITenantOwned
{
    Guid OrganisationId { get; }
}
