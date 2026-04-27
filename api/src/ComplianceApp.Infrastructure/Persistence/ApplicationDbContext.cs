using System.Linq.Expressions;
using ComplianceApp.Application.Common.Authentication;
using ComplianceApp.Application.Common.Persistence;
using ComplianceApp.Domain.Common;
using ComplianceApp.Domain.ComplianceTypes;
using Microsoft.EntityFrameworkCore;

namespace ComplianceApp.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentUserService _currentUser;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<ComplianceType> ComplianceTypes => Set<ComplianceType>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Applies a global query filter to every entity that implements
    /// <see cref="ITenantOwned"/> so that <c>OrganisationId</c> always equals
    /// the current request's tenant. Closes the door on accidental
    /// cross-tenant reads at the data-access boundary.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType)) continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var orgIdProperty = Expression.Property(parameter, nameof(ITenantOwned.OrganisationId));
            var currentOrgId = Expression.Property(
                Expression.Constant(this),
                nameof(CurrentOrganisationIdOrEmpty));
            var body = Expression.Equal(orgIdProperty, currentOrgId);
            var lambda = Expression.Lambda(body, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }

    /// <summary>
    /// Used by the global query filter expression. Returns
    /// <see cref="Guid.Empty"/> when there is no current user, which means
    /// unauthenticated queries return no rows from tenant-owned tables.
    /// </summary>
    public Guid CurrentOrganisationIdOrEmpty => _currentUser.OrganisationId ?? Guid.Empty;
}
