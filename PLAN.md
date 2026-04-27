# Delivery Plan — Phase 1 (Foundation)

Living document tracking Phase 1 progress. Phase 1 exit criteria: authenticated API calls return tenant-scoped data; all three clients can log in.

See [PROJECT.md](PROJECT.md) for *what* we're building and [CLAUDE.md](CLAUDE.md) for *how*.

---

## Decisions locked in

- **Runtime:** .NET 10 (bumped from spec's .NET 8 — latest LTS, released Nov 2025)
- **Monorepo layout:** `api/`, `web/`, `mobile/`, `shared/`, `infra/` under repo root
- **IaC:** AWS CDK (TypeScript) under `infra/`
- **Workspace manager (TS side):** pnpm workspaces covering `web`, `mobile`, `shared`
- **CI:** GitHub Actions (not Bitbucket Pipelines — spec override)
- **Local auth:** dev JWT issuer (option A) — real Cognito wiring once AWS account exists
- **MediatR:** pinned at `12.4.1` (last MIT-licensed release). v13+ went commercial; upgrade decision deferred.

---

## Progress

### ✅ T1 — Monorepo + API scaffolding *(merged in [#1](https://github.com/DannyHumphrey/compliance-flow/pull/1))*
- `api/` folder with Clean Architecture layout: Domain, Application, Infrastructure, Api + three test projects
- Project references wired per dependency rules
- NuGet packages installed (MediatR, FluentValidation, EF Core 10, Npgsql, Serilog, JwtBearer, xUnit, FluentAssertions, NSubstitute, Testcontainers.PostgreSql, Mvc.Testing)
- `.gitignore` + `Program` exposed as `public partial class` for `WebApplicationFactory<Program>`
- Build clean (0 errors)

### ✅ T3 — Domain base classes *(merged in [#2](https://github.com/DannyHumphrey/compliance-flow/pull/2))*
- `BaseEntity` — `Id`, `CreatedAt`, `UpdatedAt` with `protected set` (infrastructure interceptor will stamp timestamps in T7)
- `ITenantOwned` — marker interface exposing `OrganisationId`
- `TenantOwnedEntity` — `BaseEntity` + `ITenantOwned`
- `DomainException` — message + inner-exception constructors
- 5 tests, all passing

> *T2 was folded into T1; kept the numbering anyway for traceability against earlier plan.*

### ✅ T5 — MediatR pipeline behaviours *(this PR)*
- `ICommand<TResponse>` / `IQuery<TResponse>` markers under `Application/Common/Messaging` — give us a way to route commands through `TransactionBehaviour` while leaving queries untouched
- `IUnitOfWork` + `IUnitOfWorkTransaction` abstraction under `Application/Common/Persistence`; T7 implements on EF Core so Application stays free of EF Core types
- `LoggingBehaviour` — logs request start/completion at Info, failure at Error (outermost in the pipeline)
- `ValidationBehaviour` — runs all registered `IValidator<TRequest>`, aggregates failures, throws `FluentValidation.ValidationException`
- `PerformanceBehaviour` — warns when a handler exceeds 500 ms (threshold exposed as a const for future tuning)
- `TransactionBehaviour` — generic-constrained to `ICommand<TResponse>` so MediatR only constructs it for commands; queries skip it for free
- `Application.DependencyInjection.AddApplication()` registers MediatR + validators-from-assembly + behaviours in order: Logging → Validation → Performance → Transaction → Handler
- 8 tests across `ValidationBehaviour` (no validators / valid / invalid / multi-validator aggregation), `TransactionBehaviour` (commit on success, rollback + dispose on failure), and `LoggingBehaviour` (info on success, error on throw)

---

## Remaining Phase 1 work (in execution order)

### 🔲 T6 — JWT auth + dev token issuer
- `ICurrentUserService` (Application) with `UserId` + `OrganisationId`
- `CurrentUserService` (Infrastructure) reading `sub` + `custom:organisationId` from JWT claims
- JWT bearer middleware in Api, **dev issuer** gated to `Development` (option A — short-lived tokens signed with a dev symmetric key)
- `AuthController` exposing a dev-only `/api/auth/dev-token` endpoint
- Integration test using the dev issuer proves tenant resolution

### 🔲 T7 — EF Core + initial migration
- `IApplicationDbContext` interface in Application
- `ApplicationDbContext` in Infrastructure with global query filter for `ITenantOwned`, wired to `ICurrentUserService`
- `SaveChanges` interceptor stamps `CreatedAt`/`UpdatedAt` and `OrganisationId` on new entities
- First migration creates `ComplianceType` table (root entity for T8 seeding)
- Testcontainers integration test confirms migration applies and tenant filter isolates data

### 🔲 T8 — ComplianceType seed data
- Seeder inserts system types (EICR, Gas Safety, Fire Risk, Damp & Mould, Legionella) with null `OrganisationId`
- Runs on app startup (idempotent)
- Integration test confirms all five present after seed

### 🔲 T9 — Web app scaffolding
- `web/` via Vite + React 18 + TypeScript
- TanStack Query, Zustand, React Router v6, React Hook Form + Zod, shadcn/ui + Tailwind
- Amplify Auth pointing at **dev issuer** for now (swap to Cognito later)
- Login screen exchanges creds for JWT → lands on placeholder dashboard
- Vitest + RTL + MSW configured; one smoke test

### 🔲 T10 — Mobile app scaffolding
- `mobile/` via Expo managed workflow
- React Navigation, TanStack Query, Zustand, React Hook Form + Zod, Amplify Auth for RN
- Login screen authenticates against dev issuer
- Jest + RNTL configured; one smoke test

### 🔲 T11 — OpenAPI type generation
- `scripts/generate-api-types.sh` hits `/openapi/v1.json` and writes `shared/src/api-types.generated.ts`
- Web + mobile consume via `@compliance-flow/shared`
- CI drift-check: regenerate, fail if diff

### 🔲 T4 — AWS CDK (synth-only until account exists)
- `infra/` as its own pnpm workspace package
- Stacks: `AuthStack` (Cognito pool + web/mobile app clients, `custom:organisationId`), `DataStack` (RDS Postgres), `StorageStack` (S3 certs)
- `cdk synth` in CI to catch drift
- `infra/README.md` documenting bootstrap → deploy → copy outputs to `.env`
- `.env.example` at repo root listing required vars

### 🔲 T12 — GitHub Actions CI
- `api.yml` — `dotnet format --verify-no-changes`, build, unit tests, Testcontainers integration tests
- `web.yml` — pnpm lint, build, Vitest
- `mobile.yml` — pnpm lint, build, Jest
- `types-drift.yml` — regenerate OpenAPI types, fail on diff
- `infra.yml` — `cdk synth`
- Path filters so workflows only run on relevant folder changes

---

## Deferred / open questions

- **MediatR licensing** — stay on 12.4.1 or upgrade to commercial v13?
- **Vulnerability warnings** — `System.Security.Cryptography.Xml 9.0.0` NU1903 from EF Core design-time tooling. Suppress with `NoWarn` or leave visible?
- **Seed compliance types** — confirm the final list for v1 (currently: EICR, Gas Safety, Fire Risk, Damp & Mould, Legionella)
- **AWS account** — when provisioned, T4 outputs feed into T6 (swap dev issuer for real Cognito pool) and T9/T10 Amplify config
