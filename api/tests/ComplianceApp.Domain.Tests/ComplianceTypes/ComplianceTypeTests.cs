using ComplianceApp.Domain.ComplianceTypes;
using ComplianceApp.Domain.Exceptions;
using FluentAssertions;

namespace ComplianceApp.Domain.Tests.ComplianceTypes;

public class ComplianceTypeTests
{
    [Fact]
    public void CreateSystem_NormalisesCodeToUppercaseAndTrimsName()
    {
        var type = ComplianceType.CreateSystem(" eicr ", "  Electrical Installation Condition Report  ");

        type.Code.Should().Be("EICR");
        type.Name.Should().Be("Electrical Installation Condition Report");
        type.OrganisationId.Should().BeNull();
        type.IsSystemDefined.Should().BeTrue();
    }

    [Fact]
    public void CreateForOrganisation_SetsOrganisationIdAndIsNotSystemDefined()
    {
        var orgId = Guid.NewGuid();

        var type = ComplianceType.CreateForOrganisation(orgId, "CUSTOM", "Custom Check");

        type.OrganisationId.Should().Be(orgId);
        type.IsSystemDefined.Should().BeFalse();
    }

    [Fact]
    public void CreateForOrganisation_WithEmptyOrganisationId_Throws()
    {
        var act = () => ComplianceType.CreateForOrganisation(Guid.Empty, "CUSTOM", "Custom");

        act.Should().Throw<DomainException>()
            .WithMessage("*OrganisationId*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateSystem_WithBlankCode_Throws(string code)
    {
        var act = () => ComplianceType.CreateSystem(code, "Name");

        act.Should().Throw<DomainException>()
            .WithMessage("Code*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateSystem_WithBlankName_Throws(string name)
    {
        var act = () => ComplianceType.CreateSystem("CODE", name);

        act.Should().Throw<DomainException>()
            .WithMessage("Name*");
    }
}
