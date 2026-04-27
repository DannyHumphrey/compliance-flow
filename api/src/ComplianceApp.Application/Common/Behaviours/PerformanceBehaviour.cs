using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ComplianceApp.Application.Common.Behaviours;

/// <summary>
/// Logs a warning when a handler exceeds the slow-request threshold.
/// </summary>
public class PerformanceBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Threshold in milliseconds above which a request is considered slow.</summary>
    public const int SlowRequestThresholdMs = 500;

    private readonly ILogger<PerformanceBehaviour<TRequest, TResponse>> _logger;

    public PerformanceBehaviour(ILogger<PerformanceBehaviour<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var response = await next();

        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;

        if (elapsedMs > SlowRequestThresholdMs)
        {
            _logger.LogWarning(
                "Slow request: {RequestName} took {ElapsedMilliseconds}ms (threshold {ThresholdMilliseconds}ms)",
                typeof(TRequest).Name,
                elapsedMs,
                SlowRequestThresholdMs);
        }

        return response;
    }
}
