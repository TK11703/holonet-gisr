# HoloNet GISR Constitution

The Galactic Identity and Service Records (GISR) system archives authoritative records for
citizens, military personnel, Jedi, bounty hunters, and others.

## Core Principles

### I. .NET 10 and C# Only

All application projects MUST target .NET 10 and be written in C#. Nullable reference types MUST
be enabled and compiler warnings MUST be treated as errors in every project. Data access and
I/O-bound code MUST be async and MUST accept and forward a `CancellationToken`.

### II. Feature-Folder Organization

Every project MUST be organized by feature (for example `Citizens/`, `Jedi/`, `BountyHunters/`),
not by technical layer. Code, DTOs, validators, endpoints, components, repositories, and SQL
scripts for a feature MUST live together in that feature's folder within each project. Shared
cross-feature code MUST live in an explicitly named `Common/` (or equivalent) folder.
In `Gisr.Database` the SQL scripts of every feature are tracked together under `Scripts/`, in that
feature's subfolder (see VI).
Rationale: a feature can be understood, changed, and removed in one place.

### III. Solution Structure and Dependency Direction

The solution MUST contain these projects, with the following dependency rules:

- `Gisr.Web` — Blazor user interface. MAY reference only `Gisr.Shared`.
- `Gisr.Api` — minimal APIs. MAY reference `Gisr.Shared` and `Gisr.Database`.
- `Gisr.Database` — repositories, SQL scripts, and schema migrations. MUST NOT reference
  `Gisr.Api`, `Gisr.Web`, or `Gisr.Shared`.
- `Gisr.Shared` — request/response DTOs and contracts only. MUST contain no persistence logic.
- Test projects (for example `Gisr.Api.Tests`, `Gisr.Database.Tests`) MUST mirror the feature
  folders of the project under test.

`Gisr.Web` MUST reach data only through HTTP calls to `Gisr.Api`; it MUST NOT access the database.

### IV. DTO Boundary (NON-NEGOTIABLE)

Data models (persistence/entity types) MUST NOT cross the API boundary. Every endpoint MUST accept
and return DTOs only, and data models MUST remain internal to `Gisr.Database` and the API
mapping layer. DTOs MUST live in `Gisr.Shared` and MUST be the only types `Gisr.Web` consumes.
Mapping between data models and DTOs MUST be explicit and covered by tests.

### V. Validated Minimal APIs

The backend MUST be built with ASP.NET Core minimal APIs. Every endpoint MUST validate its input
DTO with FluentValidation before any business or data-access logic runs. Invalid input MUST
produce an RFC 9457 `ProblemDetails` response (validation problems with HTTP 400/422 as
appropriate). All error responses MUST use `ProblemDetails`. Every endpoint MUST declare its
request/response types and status codes and be documented through OpenAPI. Endpoints MUST be
grouped per feature (for example `MapGroup` + one registration extension per feature).

### VI. Dapper, PostgreSQL, and Plain SQL Scripts

Persistence MUST use PostgreSQL accessed through Npgsql and Dapper; no other ORM is permitted.
All SQL MUST be written as individual `.sql` files stored in the feature's subfolder of
`Gisr.Database/Scripts/` (for example `Scripts/Records/`); SQL MUST NOT be embedded as string
literals in C# code. Queries and commands
MUST be loaded and cached at runtime and executed with Dapper. All SQL MUST be parameterized;
string concatenation or interpolation of values into SQL is prohibited. Each feature MUST expose
its data access through a repository in `Gisr.Database` (repository pattern); `Gisr.Api` MUST
call repositories and MUST NOT run SQL directly.

### VII. Versioned Schema Migrations

Schema changes MUST be made only through numbered, forward-only migration scripts stored in
`Gisr.Database`. A custom migration runner MUST apply pending scripts in order and record each
applied script in a migration ledger table (script identifier, checksum, applied timestamp). The
runner MUST fail if an already-applied script's checksum has changed. Applied migration scripts
MUST NOT be edited; corrections MUST be made with a new migration.

### VIII. Test-First with Real Dependencies (NON-NEGOTIABLE)

Development MUST follow test-first practice: tests are written and observed to fail before the
implementation, then made to pass (Red-Green-Refactor). Tests MUST use xUnit. Repository and
migration tests MUST run against a real PostgreSQL instance provisioned with Testcontainers;
mocking the database is not an acceptable substitute. Every endpoint MUST have tests covering
success, validation failure, and not-found/error paths. UI components with logic MUST have tests.

## Technology & Architecture Constraints

- **UI**: Blazor Web App using the Interactive Server render mode, styled with Bootstrap 5.3.x.
  Bootstrap MUST be pinned to a 5.3.x version; other CSS frameworks MUST NOT be introduced.
- **Authentication/Authorization**: Microsoft Entra ID via OpenID Connect for `Gisr.Web` and JWT
  bearer validation for `Gisr.Api`. Every endpoint MUST require authorization unless the spec
  explicitly justifies an anonymous endpoint. Secrets MUST NOT be committed to source control.
- **Observability**: Structured logging (`ILogger`/Serilog) and OpenTelemetry tracing and metrics
  MUST be enabled in `Gisr.Api` and `Gisr.Web`. Logs MUST NOT contain sensitive personal record
  data or credentials.
- **Data protection**: GISR holds authoritative identity records; access to every record type
  MUST be authorized, and changes to authoritative records MUST be attributable to an
  authenticated user.
- **Dependencies**: New third-party packages MUST be justified in the feature plan. An ORM other
  than Dapper, or a UI framework other than Blazor/Bootstrap, is prohibited without a
  constitution amendment.

## Development Workflow & Quality Gates

- **Spec-driven**: Every change MUST originate from a Spec Kit spec (`/speckit-specify`) followed
  by plan and tasks before implementation.
- **Branch per spec**: Each spec MUST be built in its own branch using Spec Kit sequential
  naming (for example `001-citizen-records`). Work MUST NOT be committed directly to `main`.
  Branches MUST be merged to `main` through a pull request only.
- **Merge gates**: A pull request MUST NOT be merged unless the solution builds with zero
  warnings, all tests pass, migrations apply cleanly to an empty database, and a reviewer has
  verified compliance with this constitution.
- **Complexity**: Any deviation from these principles MUST be documented and justified in the
  feature's plan (Complexity Tracking) and approved in review.

## Governance

This constitution supersedes all other practices and guidance for this repository. Amendments
MUST be proposed via a pull request that updates this file, states the rationale, and includes a
migration plan for any existing code that becomes non-compliant. Amendments require reviewer
approval before merge.

Versioning follows semantic versioning: MAJOR for backward-incompatible principle removals or
redefinitions, MINOR for added principles/sections or materially expanded guidance, PATCH for
clarifications and wording fixes. Every pull request and review MUST verify compliance with this
constitution, and Spec Kit plans MUST pass a Constitution Check against it before design begins.

**Version**: 1.0.1 | **Ratified**: 2026-10-23 | **Last Amended**: 2026-10-26
