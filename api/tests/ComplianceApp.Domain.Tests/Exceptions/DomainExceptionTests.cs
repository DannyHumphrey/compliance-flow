using ComplianceApp.Domain.Exceptions;
using FluentAssertions;

namespace ComplianceApp.Domain.Tests.Exceptions;

public class DomainExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        var exception = new DomainException("Address is required");

        exception.Message.Should().Be("Address is required");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_SetsBoth()
    {
        var inner = new InvalidOperationException("inner");

        var exception = new DomainException("outer", inner);

        exception.Message.Should().Be("outer");
        exception.InnerException.Should().BeSameAs(inner);
    }
}
