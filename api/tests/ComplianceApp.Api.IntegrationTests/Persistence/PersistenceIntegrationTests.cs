using ComplianceApp.Domain.ComplianceTypes;
using ComplianceApp.Infrastructure.Persistence;
using ComplianceApp.Infrastructure.Persistence.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ComplianceApp.Api.IntegrationTests.Persistence;

/// <summary>
/// End-to-end persistence checks against a Testcontainers Postgres:
///   - the migration applies cleanly
///   - the auditable interceptor stamps CreatedAt / UpdatedAt
///   - the tenant query filter isolates rows by OrganisationId
///   - the interceptor stamps OrganisationId on insert when missing
/// </summary>
public class PersistenceIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly StubCurrentUserService _currentUser = new();
    private DbContextOptions<ApplicationDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var interceptor = new AuditableEntityInterceptor(_currentUser, TimeProvider.System);

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;

        // Use the testable context (which includes TestTenantOwnedEntity) to
        // create the schema, so a single container backs every test in this
        // class and we don't need to mix Migrate + EnsureCreated.
        await using var setup = new TestableDbContext(_options, _currentUser);
        await setup.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_AppliesCleanly_OnAFreshContainer()
    {
        // Apply the actual migration (not EnsureCreated) against a brand-new
        // schema to prove the generated SQL is valid against real Postgres.
        await using var freshDb = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await freshDb.StartAsync();

        var interceptor = new AuditableEntityInterceptor(_currentUser, TimeProvider.System);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(freshDb.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;

        await using var ctx = new ApplicationDbContext(options, _currentUser);

        await ctx.Database.MigrateAsync();

        var applied = await ctx.Database.GetAppliedMigrationsAsync();
        applied.Should().Contain(m => m.EndsWith("_InitialCreate"));

        // Round-trip insert through the migrated table to prove it's usable.
        ctx.ComplianceTypes.Add(ComplianceType.CreateSystem("EICR", "EICR"));
        await ctx.SaveChangesAsync();
        (await ctx.ComplianceTypes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Interceptor_OnInsert_StampsCreatedAt()
    {
        await using var ctx = new TestableDbContext(_options, _currentUser);
        await ctx.ComplianceTypes.ExecuteDeleteAsync();

        var before = DateTime.UtcNow.AddSeconds(-1);
        var type = ComplianceType.CreateSystem("EICR", "Electrical Installation Condition Report");

        ctx.ComplianceTypes.Add(type);
        await ctx.SaveChangesAsync();

        var saved = await ctx.ComplianceTypes.AsNoTracking().SingleAsync(x => x.Id == type.Id);
        saved.CreatedAt.Should().BeOnOrAfter(before);
        saved.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Interceptor_OnUpdate_StampsUpdatedAt()
    {
        await using var ctx = new TestableDbContext(_options, _currentUser);
        var type = ComplianceType.CreateSystem("GAS", "Gas Safety");
        ctx.ComplianceTypes.Add(type);
        await ctx.SaveChangesAsync();

        // Force a tracked modification.
        ctx.Entry(type).Property(nameof(ComplianceType.Name)).CurrentValue = "Gas Safety Certificate";
        await ctx.SaveChangesAsync();

        var saved = await ctx.ComplianceTypes.AsNoTracking().SingleAsync(x => x.Id == type.Id);
        saved.UpdatedAt.Should().NotBeNull();
        saved.UpdatedAt!.Value.Should().BeOnOrAfter(saved.CreatedAt);
    }

    [Fact]
    public async Task Interceptor_OnInsertOfTenantOwned_StampsOrganisationIdFromCurrentUser()
    {
        var orgId = Guid.NewGuid();
        _currentUser.UserId = Guid.NewGuid();
        _currentUser.OrganisationId = orgId;

        await using var ctx = new TestableDbContext(_options, _currentUser);
        await ctx.TestEntities.IgnoreQueryFilters().ExecuteDeleteAsync();

        var entity = TestTenantOwnedEntity.Create("hello");
        // Note: OrganisationId left at default (Guid.Empty) — interceptor should fill it.
        ctx.TestEntities.Add(entity);
        await ctx.SaveChangesAsync();

        var saved = await ctx.TestEntities.AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == entity.Id);
        saved.OrganisationId.Should().Be(orgId);
    }

    [Fact]
    public async Task QueryFilter_IsolatesRowsByOrganisationId()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        // Seed one row per org with the interceptor stamping the tenant.
        _currentUser.UserId = Guid.NewGuid();
        _currentUser.OrganisationId = orgA;
        await using (var ctx = new TestableDbContext(_options, _currentUser))
        {
            await ctx.TestEntities.IgnoreQueryFilters().ExecuteDeleteAsync();
            ctx.TestEntities.Add(TestTenantOwnedEntity.Create("A1"));
            await ctx.SaveChangesAsync();
        }

        _currentUser.OrganisationId = orgB;
        await using (var ctx = new TestableDbContext(_options, _currentUser))
        {
            ctx.TestEntities.Add(TestTenantOwnedEntity.Create("B1"));
            await ctx.SaveChangesAsync();
        }

        // As Org A we should see only A1.
        _currentUser.OrganisationId = orgA;
        await using (var ctx = new TestableDbContext(_options, _currentUser))
        {
            var rows = await ctx.TestEntities.AsNoTracking().ToListAsync();
            rows.Should().ContainSingle().Which.Name.Should().Be("A1");
        }

        // As Org B we should see only B1.
        _currentUser.OrganisationId = orgB;
        await using (var ctx = new TestableDbContext(_options, _currentUser))
        {
            var rows = await ctx.TestEntities.AsNoTracking().ToListAsync();
            rows.Should().ContainSingle().Which.Name.Should().Be("B1");
        }

        // IgnoreQueryFilters bypasses the filter — both rows visible.
        await using (var ctx = new TestableDbContext(_options, _currentUser))
        {
            var rows = await ctx.TestEntities.AsNoTracking().IgnoreQueryFilters().ToListAsync();
            rows.Should().HaveCount(2);
        }
    }
}
