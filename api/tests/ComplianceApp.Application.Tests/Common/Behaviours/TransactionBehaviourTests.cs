using ComplianceApp.Application.Common.Behaviours;
using ComplianceApp.Application.Common.Messaging;
using ComplianceApp.Application.Common.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ComplianceApp.Application.Tests.Common.Behaviours;

public class TransactionBehaviourTests
{
    public record TestCommand(string Value) : ICommand<string>;

    [Fact]
    public async Task Handle_WhenHandlerSucceeds_CommitsTransaction()
    {
        var transaction = Substitute.For<IUnitOfWorkTransaction>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(transaction);

        var sut = new TransactionBehaviour<TestCommand, string>(
            unitOfWork,
            NullLogger<TransactionBehaviour<TestCommand, string>>.Instance);

        var result = await sut.Handle(new TestCommand("x"), () => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
        await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Handle_WhenHandlerThrows_RollsBackAndRethrows()
    {
        var transaction = Substitute.For<IUnitOfWorkTransaction>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(transaction);

        var sut = new TransactionBehaviour<TestCommand, string>(
            unitOfWork,
            NullLogger<TransactionBehaviour<TestCommand, string>>.Instance);

        var boom = new InvalidOperationException("handler failed");

        var act = async () => await sut.Handle(
            new TestCommand("x"),
            () => Task.FromException<string>(boom),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(boom);

        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
    }
}
