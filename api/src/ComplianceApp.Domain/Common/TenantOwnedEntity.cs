namespace ComplianceApp.Domain.Common;

public abstract class TenantOwnedEntity : BaseEntity, ITenantOwned
{
    public Guid OrganisationId { get; protected set; }
}
