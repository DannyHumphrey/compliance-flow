using ComplianceApp.Application.Common.Behaviours;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ComplianceApp.Application.Tests.Common.Behaviours;

public class LoggingBehaviourTests
{
    public record TestRequest(string Value) : IRequest<string>;

    [Fact]
    public async Task Handle_WhenHandlerSucceeds_LogsStartAndCompletion()
    {
        var logger = Substitute.For<ILogger<LoggingBehaviour<TestRequest, string>>>();
        var sut = new LoggingBehaviour<TestRequest, string>(logger);

        var result = await sut.Handle(new TestRequest("x"), () => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
        logger.ReceivedCalls()
            .Count(c => (LogLevel)c.GetArguments()[0]! == LogLevel.Information)
            .Should().Be(2);
    }

    [Fact]
    public async Task Handle_WhenHandlerThrows_LogsErrorAndRethrows()
    {
        var logger = Substitute.For<ILogger<LoggingBehaviour<TestRequest, string>>>();
        var sut = new LoggingBehaviour<TestRequest, string>(logger);
        var boom = new InvalidOperationException("nope");

        var act = async () => await sut.Handle(
            new TestRequest("x"),
            () => Task.FromException<string>(boom),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(boom);

        logger.ReceivedCalls()
            .Any(c => (LogLevel)c.GetArguments()[0]! == LogLevel.Error)
            .Should().BeTrue();
    }
}
