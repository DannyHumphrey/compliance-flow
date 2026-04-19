# Property Compliance SaaS — MVP Specification

## Project Overview

A multi-tenant SaaS platform for UK property compliance management. The product enables property managers, landlords, and housing providers to:

1. Manage a portfolio of properties
2. Track compliance records (EICR, Gas Safety, Damp & Mould, Fire Risk, etc.)
3. Build and deploy custom inspection forms
4. Capture inspection data on mobile with automatic compliance updates
5. Generate PDF certificates and stay ahead of expiring obligations via reminders

The differentiating feature is the **compliance binding layer** — form fields map directly to property record fields at design time, so submissions automatically update compliance dates and statuses. No manual re-entry.

### North Star

> A property manager signs up, adds their first property, runs an EICR inspection on mobile, and sees the compliance record update automatically — in under 10 minutes.

---

## Core Data Model

The system hangs off four core entities:

### Property → Compliance → Event → Action

- **Property** — root entity, owns everything. Address, tenure, owner, portfolio grouping.
- **Compliance** — a living status record attached to a property, one per compliance type. Holds current status, certificate reference, last/next dates, RAG state.
- **Event** — immutable occurrence. Form submissions, reminders, overdue flags, action resolutions all become Events. Appended-only. Blockchain anchor point in v2.
- **Action** — remedial task raised from an Event. Has assignee, due date, status. Resolving an Action creates a new Event.

### Event Types

Events use a discriminator so they can represent different things on a single timeline:

- `ReminderSent` — system generated, no anchor
- `OverdueFlag` — system generated when nextDue passes
- `InspectionCompleted` — form submission, anchored in v2
- `CertificateUploaded` — manual upload, anchored in v2
- `ActionRaised` — created from inspection finding
- `ActionCompleted` — resolution, anchored in v2
- `ManualUpdate` — admin override, anchored in v2 with reason required

### Key Design Principles

- **Multi-tenancy via `OrganisationId`** on base entity classes — baked in, never bolted on
- **Form schemas stored as JSONB** — avoids schema migrations as forms evolve
- **Field-level bindings** on form templates auto-update compliance records on submission
- **Event is immutable** — for audit integrity
- **Compliance is mutable** — reflects current state, updated by Events
- **ComplianceType breaks the standard tenant filter** — to support both system-seeded and org-custom types
- **Rolling reminder evaluation** — not pre-scheduled, re-evaluates daily against current `nextDue`

---

## System Architecture

Three separate deliverables sharing a single API:

```
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│  React Web App  │  │  React Native   │  │  Landing Site   │
│  (admin + ops)  │  │  (mobile forms) │  │  (marketing)    │
└────────┬────────┘  └────────┬────────┘  └─────────────────┘
         │                    │
         └──────────┬─────────┘
                    │ HTTPS / JWT
                    ▼
         ┌──────────────────────┐
         │   .NET 8 Web API     │
         │  (Clean Architecture)│
         └──────────┬───────────┘
                    │
         ┌──────────┼──────────┬──────────┬──────────┐
         ▼          ▼          ▼          ▼          ▼
    PostgreSQL  AWS S3    AWS SES   AWS Cognito  Hangfire
    (+ JSONB)  (files)   (email)    (auth)       (jobs)
```

---

## 1. Backend API (.NET 8)

### Solution Structure — Clean Architecture

```
ComplianceApp.sln
├── src/
│   ├── ComplianceApp.Domain/           # Entities, value objects, domain events
│   ├── ComplianceApp.Application/      # Commands, queries, handlers (MediatR)
│   ├── ComplianceApp.Infrastructure/   # EF Core, AWS services, external integrations
│   └── ComplianceApp.Api/              # Controllers, middleware, startup
└── tests/
    ├── ComplianceApp.Domain.Tests/
    ├── ComplianceApp.Application.Tests/
    └── ComplianceApp.Api.IntegrationTests/
```

### Tech Stack

| Concern | Technology |
|---------|-----------|
| Framework | ASP.NET Core 8 |
| ORM | EF Core 8 |
| Database | PostgreSQL 16 with JSONB |
| Mediator | MediatR |
| Validation | FluentValidation |
| Mapping | Mapster or AutoMapper |
| Auth | AWS Cognito (JWT bearer) |
| Background Jobs | Hangfire + PostgreSQL storage |
| Email | AWS SES |
| File Storage | AWS S3 |
| PDF Generation | QuestPDF |
| Hosting | AWS ECS Fargate |
| Logging | Serilog → CloudWatch |

### Key API Surfaces

```
POST   /api/auth/signup                    # self-serve onboarding
POST   /api/auth/verify
POST   /api/auth/login

GET    /api/properties                     # list, tenant-scoped
POST   /api/properties
GET    /api/properties/{id}
PUT    /api/properties/{id}
DELETE /api/properties/{id}

GET    /api/properties/{id}/compliance     # all compliance records
PUT    /api/properties/{id}/compliance/{complianceId}  # manual override

GET    /api/compliance-types               # system + org-custom types
POST   /api/compliance-types               # post-MVP

GET    /api/form-templates
POST   /api/form-templates
PUT    /api/form-templates/{id}
POST   /api/form-templates/{id}/publish

POST   /api/form-submissions               # creates Event, updates Compliance
GET    /api/form-submissions/{id}
GET    /api/form-submissions/{id}/pdf      # certificate download

GET    /api/properties/{id}/timeline       # all Events for a property
GET    /api/actions                        # outstanding actions
PUT    /api/actions/{id}/complete
```

### Multi-Tenancy Strategy

- `OrganisationId` on every tenant-owned entity via a base class
- Global EF Core query filter applied to all entities implementing `ITenantOwned`
- Current tenant resolved from JWT claim in middleware, injected via `ICurrentUserService`
- `ComplianceType` has a nullable `OrganisationId` — nulls are system defaults visible to all tenants

### Background Jobs (Hangfire)

- **Daily reminder evaluation** — iterates active compliance records, fires Events matching today's trigger date
- **Overdue flag job** — runs nightly, raises `OverdueFlag` events for compliance records past `nextDue`
- **PDF generation** — async after form submission, writes to S3

---

## 2. React Web App (Admin & Ops)

The primary interface for property managers, admins, and office-based users. Mobile-responsive but desktop-first.

### Tech Stack

| Concern | Technology |
|---------|-----------|
| Framework | React 18 + TypeScript |
| Build tool | Vite |
| Routing | React Router v6 |
| State — server | TanStack Query (React Query) |
| State — client | Zustand |
| Forms | React Hook Form + Zod |
| UI library | shadcn/ui + Tailwind CSS |
| Charts | Recharts |
| Date handling | date-fns |
| Auth | AWS Amplify (Cognito) or manual JWT |
| Hosting | AWS S3 + CloudFront |

### Core Screens

- **Sign up / Login** — Cognito-backed, email verification
- **Onboarding wizard** — 3 steps: organisation name → first property → compliance types
- **Dashboard** — property list with RAG indicators, outstanding actions count, upcoming compliance
- **Property detail** — address, compliance records, timeline of Events, raise action, upload cert
- **Form builder** — drag/drop field types, configure compliance bindings, save as template
- **Form template list** — published and draft templates
- **Compliance types** — view system types, configure reminder schedules (post-MVP)
- **Settings** — organisation, users, branding

### Form Builder (MVP Scope)

Ruthlessly constrained to keep scope tight:

**Field types supported:**
- Text (short/long)
- Number
- Date
- Yes/No
- Dropdown (single select)
- Photo (with annotation post-MVP)
- Signature
- Section header (display-only)

**Features:**
- Drag and reorder fields
- Required/optional toggle
- Help text per field
- **Compliance binding** per field — drop-down selects which compliance field to update on submit

**Not in MVP:**
- Conditional logic (show field X if Y = value)
- Multi-page forms
- Repeatable groups
- Calculated fields

### Folder Structure

```
src/
├── api/                  # generated or hand-written API client
├── components/
│   ├── ui/               # shadcn primitives
│   └── domain/           # PropertyCard, ComplianceBadge, etc.
├── features/
│   ├── auth/
│   ├── onboarding/
│   ├── properties/
│   ├── compliance/
│   ├── form-builder/
│   └── submissions/
├── hooks/
├── lib/                  # utils, date helpers, RAG calc
├── routes/
└── types/                # shared type definitions
```

---

## 3. React Native Mobile App

Optimised for on-site field work by inspectors and engineers. Online-only for MVP; offline sync is phase 2.

### Tech Stack

| Concern | Technology |
|---------|-----------|
| Framework | React Native (latest stable) |
| Toolchain | Expo (managed workflow) |
| Navigation | React Navigation |
| State — server | TanStack Query |
| State — client | Zustand |
| Forms | React Hook Form + Zod |
| UI | Tamagui or React Native Paper |
| Storage (v2 offline) | WatermelonDB or MMKV |
| Camera | Expo Camera + Image Manipulator |
| Signature | react-native-signature-canvas |
| Auth | Amplify Auth for React Native |

### Core Screens

- **Login** — email + password, biometric unlock after first login
- **Property picker** — search/filter org's property list
- **Property detail** — compliance records, recent Events, "Start inspection" CTA
- **Form picker** — select from published form templates applicable to this compliance type
- **Form completion** — renders JSONB schema as native UI, photo capture inline, signature at end
- **Submission confirmation** — shows updated compliance status, PDF download link
- **Outstanding actions** — user's assigned actions, mark complete

### Form Rendering Engine

The form schema (same JSONB the web app designed) is rendered dynamically:

```typescript
type FormField =
  | { type: 'text'; id: string; label: string; required: boolean; binding?: string }
  | { type: 'date'; id: string; label: string; required: boolean; binding?: string }
  | { type: 'photo'; id: string; label: string; required: boolean }
  | { type: 'signature'; id: string; label: string }
  | // ... etc
```

The mobile app reads the schema, renders the appropriate native component per field, collects responses into a payload matching the schema shape, and POSTs to `/api/form-submissions`.

### Mobile-Specific Considerations

- Large tap targets (min 44pt)
- Keyboard-aware scrolling on form screens
- Photo compression before upload (max 1920px longest edge, 80% JPEG)
- Upload progress for photo-heavy submissions
- Clear error states when offline (v1) or sync failures (v2)

---

## Shared Concerns

### Authentication

- AWS Cognito user pool, one pool across all tenants
- Custom attribute `custom:organisationId` set at sign-up
- JWT access tokens with 1hr expiry, refresh tokens for 30 days
- All three clients use the same pool and token format

### API Contract

- OpenAPI / Swagger generated from .NET controllers
- TypeScript types generated for React + React Native consumption (e.g. via `openapi-typescript` or NSwag)
- Single source of truth — regenerate on every backend change

### Error Handling

- API returns RFC 7807 Problem Details for errors
- Consistent error shape across all clients
- Correlation IDs in every response header for support

### Versioning

- API versioned via URL path (`/api/v1/...`)
- Backward compatibility maintained within a major version
- Breaking changes require new major version

---

## Delivery Phases

### Phase 1 — Foundation (Weeks 1–3)
- Solution scaffolding (API, Web, Mobile repos)
- PostgreSQL schema + migrations
- Cognito integration + JWT auth
- Multi-tenant middleware
- CI/CD pipeline (Bitbucket → ECS for API, S3/CloudFront for Web, EAS for Mobile)
- Seed data: system compliance types, default reminder rules

**Exit criteria:** Authenticated API calls return tenant-scoped data. All three clients can log in.

### Phase 2 — Property & Compliance Core (Weeks 4–6)
- Property CRUD (API + Web)
- Compliance auto-creation on property add
- RAG calculation logic
- Dashboard with property list + RAG indicators
- Manual compliance override (for onboarding existing records)

**Exit criteria:** User can add a property and see its compliance status on the dashboard.

### Phase 3 — Form Builder & Submission (Weeks 7–10)
- Form template builder UI (Web)
- Compliance binding configuration
- Mobile form rendering engine
- Form submission → Event creation → Compliance update
- PDF certificate generation (QuestPDF)

**Exit criteria:** User builds an EICR template on web, completes it on mobile, compliance record updates, PDF is downloadable.

### Phase 4 — Reminders & Polish (Weeks 11–13)
- Hangfire reminder evaluation job
- Email notifications via AWS SES
- Overdue flag job
- Self-serve sign up + onboarding wizard
- Account settings, user management
- Landing page

**Exit criteria:** End-to-end demo works from sign-up to compliance management. Ready for case studies.

---

## Explicitly Out of Scope for MVP

Listed here so they don't sneak in:

- Offline mobile sync (v2)
- Blockchain anchoring (v2 — architecture already supports it via Event.BlockchainTxHash column)
- Configurable reminder schedules (v2 — ships with fixed 3mo / 1mo / 7d defaults)
- Custom org-specific compliance types (v2)
- Contractor portal / external user access (v2)
- Bulk property import (v2)
- Portfolio-level analytics and reporting (v2)
- Conditional logic in form builder (v2)
- Multi-language support
- White-labelling

---

## Key Architectural Decisions Locked In

- Multi-tenant SaaS via `OrganisationId` base property
- PostgreSQL with JSONB for form schemas and submission payloads
- Immutable Event log, mutable Compliance state
- Rolling daily reminder evaluation (not pre-scheduled)
- Self-serve onboarding (no manual setup)
- Clean Architecture on the API
- Separate React web + React Native mobile (shared API, shared auth)
- AWS-first infrastructure (Cognito, S3, SES, ECS, Fargate)

---

## Repository Layout

Three separate repositories, each independently deployable:

```
compliance-api/         # .NET 8 Web API
compliance-web/         # React admin/ops app
compliance-mobile/      # React Native app (Expo)
```

A shared `compliance-types` package (published privately or monorepo'd) for the generated TypeScript API types consumed by both web and mobile.

---

## Definition of Done — MVP

The MVP is complete when, in a fresh environment, a new user can:

1. Visit the landing page
2. Sign up with email and password
3. Verify their email
4. Complete the onboarding wizard (org, first property, compliance types)
5. See their property and its compliance RAG on the dashboard
6. Build an EICR form template with compliance bindings
7. Open the mobile app, log in, and select the property
8. Complete the EICR form end-to-end including photos and signature
9. See the submission confirmed and the compliance record updated on both mobile and web
10. Download the generated PDF certificate
11. Receive a scheduled reminder email 3 months before the next inspection is due

If all eleven work without developer intervention, ship it and start the case studies.
