using ComplianceApp.Application.Common.Authentication;
using ComplianceApp.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ComplianceApp.Infrastructure.Persistence.Interceptors;

/// <summary>
/// On every SaveChanges:
///   - stamps <c>CreatedAt</c> on new <see cref="BaseEntity"/> rows
///   - stamps <c>UpdatedAt</c> on modified ones
///   - stamps <c>OrganisationId</c> on new <see cref="ITenantOwned"/> rows
///     from <see cref="ICurrentUserService"/> if it isn't already set
///
/// EF Core's reflection bypasses <c>protected set</c>, so we don't need
/// public setters on the entities.
/// </summary>
public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _timeProvider;

    public AuditableEntityInterceptor(ICurrentUserService currentUser, TimeProvider timeProvider)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var orgId = _currentUser.OrganisationId;

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(BaseEntity.CreatedAt)).CurrentValue = now;
                    StampOrganisationIdIfNeeded(entry, orgId);
                    break;

                case EntityState.Modified:
                    entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue = now;
                    break;
            }
        }
    }

    private static void StampOrganisationIdIfNeeded(EntityEntry<BaseEntity> entry, Guid? orgId)
    {
        if (entry.Entity is not ITenantOwned) return;
        if (orgId is null) return;

        var orgProp = entry.Property(nameof(ITenantOwned.OrganisationId));

        // Don't overwrite an explicitly set tenant — lets seed code or migrations
        // set OrganisationId without the interceptor stomping on it.
        if (orgProp.CurrentValue is Guid existing && existing != Guid.Empty) return;

        orgProp.CurrentValue = orgId.Value;
    }
}
