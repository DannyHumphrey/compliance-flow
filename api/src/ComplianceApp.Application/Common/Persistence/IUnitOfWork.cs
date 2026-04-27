namespace ComplianceApp.Application.Common.Persistence;

/// <summary>
/// Abstracts transaction control for the Application layer so behaviours
/// don't need to know about EF Core. Implemented in Infrastructure (T7).
/// </summary>
public interface IUnitOfWork
{
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
