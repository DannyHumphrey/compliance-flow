# CLAUDE.md — Engineering Conventions

Guidance for Claude Code when working in this codebase. Read alongside `PROJECT.md` which covers *what* we're building; this file covers *how*.

---

## General Principles

- **Small, focused commits.** One concern per commit, always. Prefer many small PRs to one large one.
- **Tests are not optional.** New code ships with tests. Bug fixes ship with a regression test that would have caught the bug.
- **Fail fast, fail loud.** Validate inputs at the boundary, throw meaningful exceptions, never swallow errors silently.
- **Explicit over implicit.** No magic. No hidden side effects. Dependencies come through constructors or props.
- **Delete more than you add.** If you can remove code while keeping tests green, do it.
- **Don't reformat unrelated code.** Keeps diffs reviewable. Formatting changes go in their own commit.

---

## Backend — .NET 10 API

### Architecture — Clean Architecture + CQRS

```
┌─────────────────────────────────────────────┐
│                     Api                     │  Controllers, middleware
├─────────────────────────────────────────────┤
│                 Application                 │  Commands, Queries, Handlers
├─────────────────────────────────────────────┤
│                   Domain                    │  Entities, value objects
├─────────────────────────────────────────────┤
│               Infrastructure                │  EF Core, AWS, external APIs
└─────────────────────────────────────────────┘
```

**Dependency rule:** inner layers know nothing about outer layers. Domain has zero project references. Application references Domain only. Infrastructure references Application + Domain. API references everything.

### CQRS Pattern — Use MediatR

Every operation is either a **Command** (writes, returns minimal data) or a **Query** (reads, returns data). No exceptions for MVP.

**Commands** — mutate state, one handler per command, return either `Unit` or the created ID.

```csharp
// src/ComplianceApp.Application/Properties/Commands/CreateProperty/CreatePropertyCommand.cs
public record CreatePropertyCommand(
    string AddressLine1,
    string Postcode,
    string? PortfolioName
) : IRequest<Guid>;

public class CreatePropertyCommandHandler : IRequestHandler<CreatePropertyCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreatePropertyCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Guid> Handle(CreatePropertyCommand request, CancellationToken ct)
    {
        var property = Property.Create(
            _currentUser.OrganisationId,
            request.AddressLine1,
            request.Postcode,
            request.PortfolioName);

        _db.Properties.Add(property);
        await _db.SaveChangesAsync(ct);

        return property.Id;
    }
}

public class CreatePropertyCommandValidator : AbstractValidator<CreatePropertyCommand>
{
    public CreatePropertyCommandValidator()
    {
        RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Postcode).NotEmpty().Matches(@"^[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}$");
    }
}
```

**Queries** — read-only, return DTOs (never domain entities).

```csharp
// src/ComplianceApp.Application/Properties/Queries/GetPropertyById/GetPropertyByIdQuery.cs
public record GetPropertyByIdQuery(Guid Id) : IRequest<PropertyDto?>;

public class GetPropertyByIdQueryHandler : IRequestHandler<GetPropertyByIdQuery, PropertyDto?>
{
    private readonly IApplicationDbContext _db;

    public GetPropertyByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PropertyDto?> Handle(GetPropertyByIdQuery request, CancellationToken ct)
    {
        return await _db.Properties
            .AsNoTracking()
            .Where(p => p.Id == request.Id)
            .Select(p => new PropertyDto
            {
                Id = p.Id,
                AddressLine1 = p.AddressLine1,
                Postcode = p.Postcode,
                ComplianceRecords = p.ComplianceRecords
                    .Select(c => new ComplianceSummaryDto { /* ... */ })
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);
    }
}
```

### Folder Structure Per Feature

```
Application/Properties/
├── Commands/
│   ├── CreateProperty/
│   │   ├── CreatePropertyCommand.cs
│   │   ├── CreatePropertyCommandHandler.cs
│   │   └── CreatePropertyCommandValidator.cs
│   └── UpdateProperty/
├── Queries/
│   ├── GetPropertyById/
│   │   ├── GetPropertyByIdQuery.cs
│   │   ├── GetPropertyByIdQueryHandler.cs
│   │   └── PropertyDto.cs
│   └── ListProperties/
└── Events/                  # domain events, not request events
    └── PropertyCreatedEventHandler.cs
```

One folder per command/query. Keeps related files together, makes deletion trivial.

### Controllers — Thin Mediators

Controllers do nothing except translate HTTP to MediatR and back. No business logic.

```csharp
[ApiController]
[Route("api/properties")]
[Authorize]
public class PropertiesController : ControllerBase
{
    private readonly ISender _mediator;

    public PropertiesController(ISender mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreatePropertyCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, id);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PropertyDto>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPropertyByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }
}
```

### Domain Entities — Rich, Not Anaemic

Entities own their invariants. No public setters. State changes through methods that enforce rules.

```csharp
public class Property : TenantOwnedEntity
{
    private Property() { } // EF Core

    public string AddressLine1 { get; private set; } = null!;
    public string Postcode { get; private set; } = null!;
    public string? PortfolioName { get; private set; }

    private readonly List<ComplianceRecord> _complianceRecords = new();
    public IReadOnlyCollection<ComplianceRecord> ComplianceRecords => _complianceRecords.AsReadOnly();

    public static Property Create(Guid organisationId, string address, string postcode, string? portfolio)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new DomainException("Address is required");

        return new Property
        {
            Id = Guid.NewGuid(),
            OrganisationId = organisationId,
            AddressLine1 = address.Trim(),
            Postcode = postcode.Trim().ToUpperInvariant(),
            PortfolioName = portfolio?.Trim()
        };
    }

    public void AssignCompliance(ComplianceType type, DateOnly? lastCompletedDate)
    {
        if (_complianceRecords.Any(c => c.ComplianceTypeId == type.Id))
            throw new DomainException($"Property already has {type.Name} compliance assigned");

        _complianceRecords.Add(ComplianceRecord.Create(Id, type, lastCompletedDate));
    }
}
```

### MediatR Pipeline Behaviours

Cross-cutting concerns go here, not in handlers:

- `ValidationBehaviour` — runs FluentValidation before handler
- `LoggingBehaviour` — logs request/response at Info, errors at Error
- `PerformanceBehaviour` — logs warning if handler > 500ms
- `TransactionBehaviour` — wraps commands in a DB transaction (queries skipped)

Register in order: Logging → Validation → Performance → Transaction → Handler.

### Multi-Tenancy

- `TenantOwnedEntity` base class has `OrganisationId`
- Global EF Core query filter applied to every `ITenantOwned` type
- `ICurrentUserService` resolves tenant from JWT claim
- Infrastructure sets tenant on `SaveChangesAsync` for new entities automatically

---

## Backend Testing

### Test Project Layout

```
tests/
├── ComplianceApp.Domain.Tests/           # pure unit tests, no mocks needed
├── ComplianceApp.Application.Tests/      # handler tests with mocked dependencies
└── ComplianceApp.Api.IntegrationTests/   # full stack against Testcontainers Postgres
```

### Test Stack

| Concern | Library |
|---------|---------|
| Test runner | xUnit |
| Assertions | FluentAssertions |
| Mocking | NSubstitute |
| Test data | Bogus |
| Integration DB | Testcontainers.PostgreSql |
| Web host | `WebApplicationFactory<Program>` |

### Naming Convention

`MethodName_Scenario_ExpectedOutcome`

```csharp
[Fact]
public async Task Handle_WithValidCommand_ReturnsNewPropertyId() { }

[Fact]
public async Task Handle_WhenAddressIsEmpty_ThrowsValidationException() { }

[Fact]
public async Task Handle_WhenPropertyExists_UpdatesComplianceRecords() { }
```

### Domain Tests — No Mocks

Domain is pure logic. Test entity behaviour directly.

```csharp
public class PropertyTests
{
    [Fact]
    public void Create_WithValidInputs_ReturnsProperty()
    {
        var orgId = Guid.NewGuid();

        var property = Property.Create(orgId, "1 High Street", "SW1A 1AA", "London Portfolio");

        property.OrganisationId.Should().Be(orgId);
        property.AddressLine1.Should().Be("1 High Street");
        property.Postcode.Should().Be("SW1A 1AA");
    }

    [Fact]
    public void Create_WithEmptyAddress_ThrowsDomainException()
    {
        var act = () => Property.Create(Guid.NewGuid(), "", "SW1A 1AA", null);

        act.Should().Throw<DomainException>()
           .WithMessage("Address is required");
    }

    [Fact]
    public void AssignCompliance_WhenTypeAlreadyExists_ThrowsDomainException()
    {
        var property = Property.Create(Guid.NewGuid(), "1 High St", "SW1A 1AA", null);
        var eicrType = ComplianceTypeBuilder.Eicr();
        property.AssignCompliance(eicrType, null);

        var act = () => property.AssignCompliance(eicrType, null);

        act.Should().Throw<DomainException>();
    }
}
```

### Application Tests — Mock Dependencies

Use NSubstitute for `IApplicationDbContext`, `ICurrentUserService`, and any infrastructure interfaces.

```csharp
public class CreatePropertyCommandHandlerTests
{
    private readonly IApplicationDbContext _db = Substitute.For<IApplicationDbContext>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly CreatePropertyCommandHandler _sut;

    public CreatePropertyCommandHandlerTests()
    {
        _currentUser.OrganisationId.Returns(Guid.NewGuid());
        _sut = new CreatePropertyCommandHandler(_db, _currentUser);
    }

    [Fact]
    public async Task Handle_WithValidCommand_AddsPropertyToDbContext()
    {
        var command = new CreatePropertyCommand("1 High St", "SW1A 1AA", null);

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Should().NotBeEmpty();
        _db.Properties.Received(1).Add(Arg.Is<Property>(p => p.AddressLine1 == "1 High St"));
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
```

### Validator Tests — Exhaustive on Rules

```csharp
public class CreatePropertyCommandValidatorTests
{
    private readonly CreatePropertyCommandValidator _sut = new();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void AddressLine1_WhenMissing_HasError(string? address)
    {
        var result = _sut.TestValidate(new CreatePropertyCommand(address!, "SW1A 1AA", null));

        result.ShouldHaveValidationErrorFor(x => x.AddressLine1);
    }

    [Theory]
    [InlineData("SW1A 1AA")]
    [InlineData("M1 1AE")]
    [InlineData("B33 8TH")]
    public void Postcode_WhenValid_PassesValidation(string postcode)
    {
        var result = _sut.TestValidate(new CreatePropertyCommand("1 High St", postcode, null));

        result.ShouldNotHaveValidationErrorFor(x => x.Postcode);
    }
}
```

### Integration Tests — Real Database via Testcontainers

Spin up a real Postgres for each test class using Testcontainers. Tests run full stack from HTTP in to DB and back.

```csharp
public class PropertiesApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.AddDbContext<ApplicationDbContext>(opts =>
                        opts.UseNpgsql(_postgres.GetConnectionString()));
                });
            });

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.ForOrg(TestIds.OrgA));
    }

    [Fact]
    public async Task Post_CreatesProperty_ReturnsCreatedWithId()
    {
        var response = await _client.PostAsJsonAsync("/api/properties",
            new { addressLine1 = "1 High St", postcode = "SW1A 1AA" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await response.Content.ReadFromJsonAsync<Guid>();
        id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Get_PropertyFromDifferentOrg_Returns404()
    {
        // seed property under OrgB
        var propertyId = await SeedPropertyUnderOrg(TestIds.OrgB);

        // client is authenticated as OrgA
        var response = await _client.GetAsync($"/api/properties/{propertyId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}
```

**Tenant isolation tests are mandatory.** Every controller gets at least one test proving org A cannot see org B's data.

### Coverage Expectations

- **Domain:** 100% — it's pure logic with no excuses
- **Application handlers:** 90%+
- **Validators:** 100% of rules
- **Integration tests:** happy path per endpoint + tenant isolation + key failure modes

Don't chase coverage for its own sake in controllers (they're thin) or infrastructure (integration tested).

---

## Frontend — React Web App

### Component Structure

```typescript
// src/features/properties/components/PropertyCard.tsx
type PropertyCardProps = {
  property: Property;
  onSelect?: (id: string) => void;
};

export function PropertyCard({ property, onSelect }: PropertyCardProps) {
  return (
    <article
      data-testid="property-card"
      data-property-id={property.id}
      onClick={() => onSelect?.(property.id)}
    >
      <h3>{property.addressLine1}</h3>
      <ComplianceBadge status={property.worstComplianceStatus} />
    </article>
  );
}
```

### Conventions

- **Named exports only** — no default exports (better refactors, better grep)
- **Props typed inline or with local type aliases** — avoid shared prop interfaces unless genuinely reused
- **Feature folders** — co-locate component, hook, tests, styles
- **Server state in TanStack Query, client state in Zustand** — never mix them
- **No prop drilling beyond 2 levels** — lift to a context or a store
- **Every interactive element gets a stable `data-testid`** — used by both unit and E2E tests

### Test ID Convention

Use `data-testid` attributes on anything a test might need to target. Format:

```
{feature}-{element}[-{modifier}]

property-card
property-card-address
property-list-empty-state
form-builder-add-field-button
form-builder-field-row
compliance-badge-red
```

Don't rely on text content, CSS classes, or DOM structure. Test IDs are the public API of components for tests.

### Accessibility

- Semantic HTML first (`<button>`, `<nav>`, `<article>`, `<label>`, not `<div onClick>`)
- All inputs have associated labels
- Colour is never the only indicator — RAG status has an icon or text too
- Tested with `@axe-core/react` in dev, Playwright axe integration in E2E

---

## Frontend Testing

### Three Layers

```
┌─────────────────────────────┐
│  E2E tests (Playwright)     │  Few, critical user journeys
├─────────────────────────────┤
│  Component tests (Vitest + RTL)  │  Many, per component behaviour
├─────────────────────────────┤
│  Unit tests (Vitest)        │  Pure functions, hooks, utils
└─────────────────────────────┘
```

### Unit & Component — Vitest + React Testing Library

```typescript
// src/features/properties/components/PropertyCard.test.tsx
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PropertyCard } from './PropertyCard';

describe('PropertyCard', () => {
  const property = {
    id: 'abc',
    addressLine1: '1 High Street',
    postcode: 'SW1A 1AA',
    worstComplianceStatus: 'red' as const,
  };

  it('renders the property address', () => {
    render(<PropertyCard property={property} />);
    expect(screen.getByText('1 High Street')).toBeInTheDocument();
  });

  it('calls onSelect with property id when clicked', async () => {
    const onSelect = vi.fn();
    render(<PropertyCard property={property} onSelect={onSelect} />);

    await userEvent.click(screen.getByTestId('property-card'));

    expect(onSelect).toHaveBeenCalledWith('abc');
  });

  it('shows red compliance badge when any record is overdue', () => {
    render(<PropertyCard property={property} />);
    expect(screen.getByTestId('compliance-badge-red')).toBeInTheDocument();
  });
});
```

**Rules:**
- Query by accessible role first (`getByRole`), fall back to `getByTestId` only when role isn't viable
- Use `userEvent` not `fireEvent` — it simulates real user interactions
- One behaviour per test
- Mock at the network boundary with MSW, not at the hook/component level

### Hook Tests

```typescript
// src/features/properties/hooks/useProperties.test.ts
import { renderHook, waitFor } from '@testing-library/react';
import { useProperties } from './useProperties';
import { createWrapper } from '@/test/queryWrapper';

describe('useProperties', () => {
  it('returns properties from the API', async () => {
    const { result } = renderHook(() => useProperties(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(3);
  });
});
```

### E2E — Playwright (Recommended Over Cypress)

Playwright is the default. It's faster, handles multiple browsers natively, has better debugging tools, and works well in CI. Cypress is fine if preferred, but the test ID conventions and Page Object Model below work for either.

**Structure:**

```
e2e/
├── fixtures/              # test data, seeded orgs and users
├── pages/                 # Page Object Models
│   ├── LoginPage.ts
│   ├── DashboardPage.ts
│   ├── PropertyDetailPage.ts
│   └── FormBuilderPage.ts
├── tests/
│   ├── onboarding.spec.ts
│   ├── property-management.spec.ts
│   ├── form-builder.spec.ts
│   └── inspection-flow.spec.ts
└── playwright.config.ts
```

**Page Object example:**

```typescript
// e2e/pages/DashboardPage.ts
import { Page, Locator } from '@playwright/test';

export class DashboardPage {
  readonly page: Page;
  readonly propertyCards: Locator;
  readonly addPropertyButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.propertyCards = page.getByTestId('property-card');
    this.addPropertyButton = page.getByTestId('dashboard-add-property');
  }

  async goto() {
    await this.page.goto('/dashboard');
  }

  async openProperty(addressLine1: string) {
    await this.propertyCards.filter({ hasText: addressLine1 }).click();
  }
}
```

**Test example — critical user journey:**

```typescript
// e2e/tests/onboarding.spec.ts
import { test, expect } from '@playwright/test';
import { LoginPage } from '../pages/LoginPage';
import { OnboardingPage } from '../pages/OnboardingPage';
import { DashboardPage } from '../pages/DashboardPage';

test('new user can sign up, onboard, and see their first property', async ({ page }) => {
  const login = new LoginPage(page);
  const onboarding = new OnboardingPage(page);
  const dashboard = new DashboardPage(page);

  await login.signUp({
    email: `test-${Date.now()}@example.com`,
    password: 'SecurePass123!',
  });

  await onboarding.completeStep1({ organisationName: 'Acme Lettings' });
  await onboarding.completeStep2({
    addressLine1: '1 High Street',
    postcode: 'SW1A 1AA',
  });
  await onboarding.completeStep3({ complianceTypes: ['EICR', 'Gas Safety'] });

  await expect(dashboard.propertyCards).toHaveCount(1);
  await expect(page.getByText('1 High Street')).toBeVisible();
});
```

### Playwright Config Essentials

- Run against a dedicated test environment, never production or staging
- Seeded tenant per test run (cleaned up after)
- Parallel execution enabled
- Screenshots and videos on failure
- Traces on first retry

### What to Cover with E2E

Only the user journeys that matter for the business:

1. Sign up → onboarding → dashboard
2. Add property → assign compliance → view RAG
3. Build form template → publish → verify it appears in mobile
4. Complete form (via API to simulate mobile) → verify compliance auto-updates on web
5. Overdue property triggers red RAG + email

Don't E2E test every edge case. Those belong in component or API tests.

---

## Mobile — React Native Testing

### Stack

- **Unit/component:** Jest + React Native Testing Library
- **E2E:** Maestro (preferred) or Detox
- Same `data-testid` convention as web

### Component Test

```typescript
// src/features/inspection/components/PhotoField.test.tsx
import { render, fireEvent } from '@testing-library/react-native';
import { PhotoField } from './PhotoField';

describe('PhotoField', () => {
  it('prompts for camera permission when tapped', async () => {
    const onCapture = jest.fn();
    const { getByTestId } = render(
      <PhotoField fieldId="photo-1" label="Consumer unit" onCapture={onCapture} />
    );

    fireEvent.press(getByTestId('photo-field-photo-1-capture'));

    // assert permission prompt flow...
  });
});
```

### E2E — Maestro Flow

Maestro is simpler than Detox for MVP. Flows are YAML, runs cleanly in CI.

```yaml
# .maestro/complete-inspection.yaml
appId: com.complianceapp.mobile
---
- launchApp
- tapOn: "Log in"
- inputText: "inspector@example.com"
- tapOn: "Password"
- inputText: "password123"
- tapOn: "Sign in"
- tapOn: "1 High Street"
- tapOn: "Start EICR inspection"
- tapOn: "Next"
# ... complete form
- tapOn: "Submit"
- assertVisible: "Compliance updated"
```

---

## API Contract & Type Sharing

- Backend generates OpenAPI spec on build
- Frontend and mobile generate TypeScript types from the spec via `openapi-typescript`
- Types are regenerated on every backend change, checked in, caught by CI
- **Never hand-write API types** — drift is inevitable

```bash
# scripts/generate-api-types.sh
npx openapi-typescript http://localhost:5000/swagger/v1/swagger.json -o src/api/types.generated.ts
```

---

## Git Conventions

### Branch Names

- `feat/short-description`
- `fix/short-description`
- `chore/short-description`
- `test/short-description`

### Commit Messages

Conventional commits format:

```
feat(properties): add address validation to create command
fix(compliance): correct RAG calculation for overdue records
test(form-builder): cover field reordering edge cases
chore(deps): bump MediatR to 12.4
```

### PRs

- One feature per PR
- PR description includes: what, why, how tested
- All tests pass in CI before review
- No merge commits — rebase onto main

---

## CI Pipeline Expectations

Every push runs:

1. Lint (backend: `dotnet format --verify-no-changes`, frontend: ESLint + Prettier)
2. Build all projects
3. Unit + component tests (backend + web + mobile)
4. Integration tests (API with Testcontainers)
5. E2E smoke tests (Playwright against ephemeral env) — PR only, not every push

Fail loudly. Flaky tests get fixed or deleted, never retried into the green.

---

## Summary Checklist for Every Change

Before opening a PR, Claude Code should verify:

- [ ] New backend logic has at least one unit test (handler level)
- [ ] Validators have tests covering every rule
- [ ] New controllers have at least one integration test
- [ ] Tenant-scoped endpoints have an isolation test
- [ ] New React components have at least one RTL test
- [ ] New interactive elements have stable `data-testid` attributes
- [ ] Critical user journeys have E2E coverage
- [ ] No hand-written API types — generated from OpenAPI
- [ ] No default exports in new TS/TSX files
- [ ] No new domain entities with public setters
- [ ] Commit message follows conventional format
