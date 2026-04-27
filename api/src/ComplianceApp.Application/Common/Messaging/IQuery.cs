using MediatR;

namespace ComplianceApp.Application.Common.Messaging;

/// <summary>
/// Marker for read-only requests. Bypasses TransactionBehaviour.
/// </summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;
