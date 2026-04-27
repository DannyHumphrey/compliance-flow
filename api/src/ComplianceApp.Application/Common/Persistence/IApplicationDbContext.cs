using ComplianceApp.Domain.ComplianceTypes;
using Microsoft.EntityFrameworkCore;

namespace ComplianceApp.Application.Common.Persistence;

/// <summary>
/// Application-facing surface of the DbContext. Handlers depend on this
/// rather than the concrete <c>ApplicationDbContext</c> so they can be
/// unit-tested with NSubstitute and so Infrastructure stays swappable.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<ComplianceType> ComplianceTypes { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
