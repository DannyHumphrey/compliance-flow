using ComplianceApp.Application.Common.Authentication;
using ComplianceApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ComplianceApp.Api.IntegrationTests.Persistence;

/// <summary>
/// Test-only DbContext that adds <see cref="TestTenantOwnedEntity"/> on top
/// of <see cref="ApplicationDbContext"/>. Configures the test entity BEFORE
/// calling <c>base.OnModelCreating</c> so it gets picked up by the parent's
/// query-filter loop alongside any production tenant-owned entities.
/// </summary>
public class TestableDbContext : ApplicationDbContext
{
    public TestableDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<TestTenantOwnedEntity> TestEntities => Set<TestTenantOwnedEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestTenantOwnedEntity>(b =>
        {
            b.ToTable("test_tenant_entities");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.Property(x => x.OrganisationId).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt);
        });

        base.OnModelCreating(modelBuilder);
    }
}
