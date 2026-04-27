using ComplianceApp.Domain.Common;

namespace ComplianceApp.Api.IntegrationTests.Persistence;

/// <summary>
/// Stand-in <see cref="ITenantOwned"/> entity used purely to exercise the
/// global query filter and the interceptor's tenant-stamping behaviour
/// while we wait for real domain entities like <c>Property</c> to land in
/// later phases.
/// </summary>
public class TestTenantOwnedEntity : TenantOwnedEntity
{
    public string Name { get; set; } = string.Empty;

    public static TestTenantOwnedEntity Create(string name) =>
        new() { Id = Guid.NewGuid(), Name = name };
}
