using ComplianceApp.Domain.Common;
using FluentAssertions;

namespace ComplianceApp.Domain.Tests.Common;

public class TenantOwnedEntityTests
{
    private sealed class TestEntity : TenantOwnedEntity
    {
        public static TestEntity Create(Guid organisationId)
        {
            return new TestEntity
            {
                Id = Guid.NewGuid(),
                OrganisationId = organisationId,
                CreatedAt = DateTime.UtcNow,
            };
        }
    }

    [Fact]
    public void TenantOwnedEntity_ImplementsITenantOwned()
    {
        var entity = TestEntity.Create(Guid.NewGuid());

        entity.Should().BeAssignableTo<ITenantOwned>();
        entity.Should().BeAssignableTo<BaseEntity>();
    }

    [Fact]
    public void Create_SetsIdAndOrganisationId()
    {
        var orgId = Guid.NewGuid();

        var entity = TestEntity.Create(orgId);

        entity.Id.Should().NotBeEmpty();
        entity.OrganisationId.Should().Be(orgId);
    }

    [Fact]
    public void Create_SetsCreatedAtAndLeavesUpdatedAtNull()
    {
        var before = DateTime.UtcNow;

        var entity = TestEntity.Create(Guid.NewGuid());

        entity.CreatedAt.Should().BeOnOrAfter(before);
        entity.UpdatedAt.Should().BeNull();
    }
}
