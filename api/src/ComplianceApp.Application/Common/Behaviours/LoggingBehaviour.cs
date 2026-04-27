using MediatR;
using Microsoft.Extensions.Logging;

namespace ComplianceApp.Application.Common.Behaviours;

/// <summary>
/// Logs the start, success, and failure of every MediatR request.
/// Outermost behaviour — wraps everything else so failures from any inner
/// behaviour (validation, transaction) are also captured.
/// </summary>
public class LoggingBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehaviour<TRequest, TResponse>> _logger;

    public LoggingBehaviour(ILogger<LoggingBehaviour<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        _logger.LogInformation("Handling {RequestName}", requestName);

        try
        {
            var response = await next();
            _logger.LogInformation("Handled {RequestName}", requestName);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failure handling {RequestName}", requestName);
            throw;
        }
    }
}
