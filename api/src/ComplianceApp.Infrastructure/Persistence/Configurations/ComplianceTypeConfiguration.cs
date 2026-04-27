using ComplianceApp.Domain.ComplianceTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ComplianceApp.Infrastructure.Persistence.Configurations;

public class ComplianceTypeConfiguration : IEntityTypeConfiguration<ComplianceType>
{
    public void Configure(EntityTypeBuilder<ComplianceType> builder)
    {
        builder.ToTable("compliance_types");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.OrganisationId);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt);

        // Code is unique per scope (system-global = null org, or per-org).
        builder.HasIndex(x => new { x.OrganisationId, x.Code })
            .IsUnique();
    }
}
