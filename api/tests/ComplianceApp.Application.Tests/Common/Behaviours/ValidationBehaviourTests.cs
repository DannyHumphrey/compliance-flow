using ComplianceApp.Application.Common.Behaviours;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using NSubstitute;

namespace ComplianceApp.Application.Tests.Common.Behaviours;

public class ValidationBehaviourTests
{
    public record TestRequest(string Name) : IRequest<string>;

    [Fact]
    public async Task Handle_WithNoValidators_CallsNext()
    {
        var sut = new ValidationBehaviour<TestRequest, string>(Array.Empty<IValidator<TestRequest>>());
        var nextCalled = false;
        Task<string> Next() { nextCalled = true; return Task.FromResult("ok"); }

        var result = await sut.Handle(new TestRequest("anything"), Next, CancellationToken.None);

        result.Should().Be("ok");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsNextAndReturnsResponse()
    {
        var validator = Substitute.For<IValidator<TestRequest>>();
        validator
            .ValidateAsync(Arg.Any<ValidationContext<TestRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        var sut = new ValidationBehaviour<TestRequest, string>(new[] { validator });

        var result = await sut.Handle(new TestRequest("ok"), () => Task.FromResult("handled"), CancellationToken.None);

        result.Should().Be("handled");
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ThrowsValidationExceptionAndDoesNotCallNext()
    {
        var validator = Substitute.For<IValidator<TestRequest>>();
        validator
            .ValidateAsync(Arg.Any<ValidationContext<TestRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[]
            {
                new ValidationFailure("Name", "Name is required"),
            }));

        var sut = new ValidationBehaviour<TestRequest, string>(new[] { validator });
        var nextCalled = false;
        Task<string> Next() { nextCalled = true; return Task.FromResult("should not run"); }

        var act = async () => await sut.Handle(new TestRequest(""), Next, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithMultipleValidators_AggregatesAllFailures()
    {
        var first = Substitute.For<IValidator<TestRequest>>();
        first
            .ValidateAsync(Arg.Any<ValidationContext<TestRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[]
            {
                new ValidationFailure("Name", "Name is required"),
            }));

        var second = Substitute.For<IValidator<TestRequest>>();
        second
            .ValidateAsync(Arg.Any<ValidationContext<TestRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[]
            {
                new ValidationFailure("Name", "Name must be at most 200 chars"),
            }));

        var sut = new ValidationBehaviour<TestRequest, string>(new[] { first, second });

        var act = async () => await sut.Handle(new TestRequest(""), () => Task.FromResult("x"), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().HaveCount(2);
    }
}
