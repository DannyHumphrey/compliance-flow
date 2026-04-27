using ComplianceApp.Application.Common.Messaging;
using ComplianceApp.Application.Common.Persistence;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ComplianceApp.Application.Common.Behaviours;

/// <summary>
/// Wraps every <see cref="ICommand{TResponse}"/> in a database transaction.
/// Queries (and any non-command requests) bypass this behaviour entirely
/// thanks to the generic constraint on <typeparamref name="TRequest"/>.
/// </summary>
public class TransactionBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TransactionBehaviour<TRequest, TResponse>> _logger;

    public TransactionBehaviour(
        IUnitOfWork unitOfWork,
        ILogger<TransactionBehaviour<TRequest, TResponse>> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var response = await next();
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Rolling back transaction for {RequestName}",
                typeof(TRequest).Name);

            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
