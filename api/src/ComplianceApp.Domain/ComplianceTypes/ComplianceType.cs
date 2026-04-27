using ComplianceApp.Domain.Common;
using ComplianceApp.Domain.Exceptions;

namespace ComplianceApp.Domain.ComplianceTypes;

/// <summary>
/// A category of compliance work (EICR, Gas Safety, ...). System-wide entries
/// have <see cref="OrganisationId"/> = null and are seeded by the platform;
/// orgs can also create their own custom types in later phases. Because the
/// entity straddles the system/tenant boundary, it does NOT implement
/// <see cref="ITenantOwned"/> — repos filter it explicitly.
/// </summary>
public class ComplianceType : BaseEntity
{
    private ComplianceType()
    {
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>Null for system-defined types; an org id for tenant-specific types.</summary>
    public Guid? OrganisationId { get; private set; }

    public bool IsSystemDefined => OrganisationId is null;

    public static ComplianceType CreateSystem(string code, string name)
    {
        return Create(organisationId: null, code, name);
    }

    public static ComplianceType CreateForOrganisation(Guid organisationId, string code, string name)
    {
        if (organisationId == Guid.Empty)
        {
            throw new DomainException("OrganisationId is required for tenant-specific compliance types");
        }

        return Create(organisationId, code, name);
    }

    private static ComplianceType Create(Guid? organisationId, string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("Code is required");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Name is required");
        }

        return new ComplianceType
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            OrganisationId = organisationId,
        };
    }
}
