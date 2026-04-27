using MediatR;

namespace ComplianceApp.Application.Common.Messaging;

/// <summary>
/// Marker for state-mutating requests. Routed through TransactionBehaviour.
/// Use Unit as the response type for commands that don't return a value.
/// </summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;
