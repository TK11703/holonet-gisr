# HoloNet GISR – Copilot Instructions

GISR (Galactic Identity and Service Records) archives authoritative records for citizens,
military personnel, Jedi, bounty hunters, and others. The project constitution at
`.specify/memory/constitution.md` is authoritative; if anything here conflicts with it, the constitution wins.

## Non-Negotiable Rules

- Preserve the existing multi-project architecture and dependency direction.
- Follow existing project patterns before introducing new abstractions, dependencies, or architectural approaches.
- When implementing a feature, follow patterns used by adjacent or similar features unless there is a documented reason to do otherwise.
- Keep endpoint handlers thin and move business or persistence logic into focused services.
- Derive user identity and ownership from validated authentication claims; never trust user or owner identifiers supplied in request data for authorization.
- Enforce authorization server-side and scope owner-specific queries by owner ID in SQL.
- Use parameterized Dapper queries exclusively.
- Preserve nullable-reference-type safety.
- Use async APIs and propagate `CancellationToken` through all layers.
- Never expose database entities as API contracts.
- Never commit secrets or log sensitive data.

## Stack

- Target C# 14 and .NET 10.
- UI: Blazor Web App (Interactive Server), Bootstrap 5.3.x only.
- API: ASP.NET Core minimal APIs, FluentValidation, OpenAPI, RFC 9457 `ProblemDetails`.
- Data: PostgreSQL via Npgsql + Dapper (no other ORM), plain `.sql` files, custom migration runner.
- Auth: Microsoft Entra ID (OIDC for web, JWT bearer for API).
- Keep HTTP, authentication, authorization, and orchestration concerns in the application's API layer.
- Keep persistence, migrations, and database access in the data-access layer.
- Keep UI behavior and presentation concerns in the UI layer.
- Tests: xUnit, Testcontainers PostgreSQL (no mocked database).
- Logging/telemetry: `ILogger`/Serilog + OpenTelemetry.

## Solution layout and dependencies
- Source projects will be organized within a src folder, tests projects within a tests folder. Infrastructure will be organized within an infra folder.
- `Gisr.Web` -> references only `Gisr.Shared`; reaches data only via HTTP to `Gisr.Api`.
- `Gisr.Api` -> references `Gisr.Shared` and `Gisr.Database`; never runs SQL directly.
- `Gisr.Database` -> references no other Gisr project; holds repositories, SQL scripts, migrations.
- `Gisr.Shared` -> DTOs and contracts only.
- Test projects mirror the feature folders of the project under test.

## Coding rules

- Organize every project by feature folder (e.g. `Citizens/`), not by technical layer. Keep a
  feature's endpoints, DTOs, validators, components, repository, and SQL together. Cross-feature
  code goes in `Common/`.
- Never expose data models across the API boundary. Endpoints accept and return DTOs from
  `Gisr.Shared` only; map explicitly between data models and DTOs.
- Every endpoint: validated with a FluentValidation validator, grouped per feature with
  `MapGroup`, declares request/response types and status codes, requires authorization unless the spec justifies otherwise, and returns `ProblemDetails` for errors.
- Use dependency injection and small focused services over static mutable state.
- Validate untrusted input at system boundaries before database or external-service operations.
- Store system timestamps and absolute instants as UTC using DateTimeOffset and PostgreSQL timestamptz. For user-entered zoned times, persist the local wall-clock value, the IANA time-zone ID, and the derived UTC instant. Use DateOnly and PostgreSQL date for date-only values.
- SQL lives in individual `.sql` files in the feature folder of `Gisr.Database`, loaded and
  cached at runtime and executed through Dapper. Always parameterize; never concatenate or
  interpolate values into SQL. No SQL string literals in C#.
- Schema changes only via new numbered, forward-only migration scripts recorded in the migration
  ledger with checksums. Never edit an applied migration.
- All data access is async and takes a `CancellationToken`.
- Do not log secrets or sensitive personal record data. Do not commit secrets.
- Do not add packages, an alternative ORM, or another CSS/UI framework without justification in
  the plan and a constitution amendment where required.

## UI Standards

- Prefer Bootstrap 5.3 utilities and components over custom CSS or JavaScript.
- Do not introduce jQuery or another UI framework.
- Use semantic HTML and maintain accessibility, keyboard navigation, and focus behavior.
- Provide accessible names for icon-only controls.
- Keep JavaScript modular and narrowly scoped.
- Use module-based Blazor interop and dispose modules and event handlers.
- Do not manipulate Blazor-owned DOM unless interop requires it.
- Safely render user-provided content and respect `prefers-reduced-motion`.

## Workflow

- Work is spec-driven with Spec Kit: `/speckit-specify` -> `/speckit-plan` -> `/speckit-tasks` -> `/speckit-implement`.
- Each spec is built on its own sequentially numbered branch (e.g. `001-citizen-records`).
  Never commit directly to `main`; merge via pull request only.
- Write tests first and confirm they fail before implementing. Every endpoint needs success, validation-failure, and not-found/error tests.
- Before finishing: solution builds with zero warnings, all tests pass, and migrations apply cleanly to an empty database.

## Commit Messages

Use Conventional Commits (`feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`) with concise imperative subjects and no trailing period.