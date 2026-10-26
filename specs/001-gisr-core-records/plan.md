# Implementation Plan: GISR Core Records

**Branch**: `001-gisr-core-records` | **Date**: 2026-10-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-gisr-core-records/spec.md`

## Summary

Build the first slice of GISR as a greenfield four-project .NET 10 solution: a Blazor Web App
(Interactive Server, dark-only Bootstrap 5.3 shell) that signs users in with Microsoft Entra ID and
calls a minimal API secured by Entra JWT bearer tokens. The API treats the Entra app role `Officer`
as the credential for viewing/editing other citizens' records and creating service records. Data
lives in PostgreSQL (Npgsql + Dapper, plain `.sql` files, custom forward-only migrations). A citizen
record is a title, an affiliation, affiliation-specific engagement details, incidents, awards, and
artifacts (files and photos stored in Azure Blob Storage). Search is semantic: record text and
text from PDF, Word, and plain-text artifacts (extracted locally with PdfPig and the Open XML SDK, with
Azure Document Intelligence OCR only as a budget-capped fallback for scanned PDFs) are
chunked, embedded with Azure OpenAI, stored with pgvector, and queried by cosine similarity with access
scoping applied in SQL. Officers can seal service records and must set an override code when sealing; a
locked sealed record is redacted in the subject's list and withheld from every other channel. Entering the
code opens a single viewing (record and artifacts, held server-side and revoked when the page is left); sealed
images never appear in the gallery. Details: [research.md](research.md).

## Technical Context

**Language/Version**: C# 14, .NET 10 (nullable enabled, warnings as errors)

**Primary Dependencies**: ASP.NET Core minimal APIs, Blazor Web App (Interactive Server), Bootstrap 5.3.x (vendored, pinned), FluentValidation, Microsoft.Identity.Web, Npgsql, Dapper, Pgvector/Pgvector.Dapper, Azure.Storage.Blobs, Azure.Identity, Azure.AI.OpenAI + Microsoft.Extensions.AI.OpenAI, UglyToad.PdfPig, DocumentFormat.OpenXml, Azure.AI.DocumentIntelligence (scanned-PDF fallback only), SkiaSharp, Serilog, OpenTelemetry (full justification below)

**Storage**: PostgreSQL 16+ with the `vector` (pgvector) extension; Azure Blob Storage for artifact bytes and thumbnails

**Local orchestration**: .NET Aspire AppHost (`Gisr.AppHost`) starts PostgreSQL (pgvector image), the Azurite blob emulator, `Gisr.Api`, and `Gisr.Web`, injects their configuration, and provides the Aspire dashboard. Automated tests still use Testcontainers.

**Testing**: xUnit; Testcontainers (PostgreSQL with pgvector image, Azurite); `WebApplicationFactory` for API tests with a test authentication scheme; bUnit for Blazor components. External AI services (embeddings, text extraction) are faked at the interface boundary only; the database is never mocked.

**Target Platform**: Linux containers on Azure (App Service or Container Apps); modern evergreen browsers

**Project Type**: Web application (Blazor UI + HTTP API + data-access library + shared contracts)

**Performance Goals**: Search, gallery, and record list pages respond in under 2 s at 10,000 records / 50,000 artifacts; account settings in under 3 s after sign-in; new or changed content searchable within 2 min

**Constraints**: Dark theme only; no custom JavaScript beyond small ES modules for focus management and the Ctrl+K shortcut; every SQL statement is a parameterized `.sql` file; authorization enforced server-side and in SQL; per-file upload limit 25 MB

**Scale/Scope**: Up to ~10k records, ~50k artifacts, 4 initial affiliations; ~9 UI pages; ~30 endpoints

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | How the plan complies |
|-----------|--------|-----------------------|
| I. .NET 10 / C# only; nullable; warnings as errors; async + `CancellationToken` | PASS | `Directory.Build.props` sets `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`; all repository, storage, and AI calls are async with tokens. |
| II. Feature-folder organization | PASS | `Accounts/`, `Affiliations/`, `Records/`, `Artifacts/`, `Gallery/`, `Search/`, `Notifications/` in every project; shared code in `Common/`; web shell, sign-in plumbing and app root in `Layout/`, `Auth/` and `Components/` (see Complexity Tracking). |
| III. Solution structure and dependency direction | PASS | `Gisr.Web` -> `Gisr.Shared` only; `Gisr.Api` -> `Shared` + `Database`; `Gisr.Database` references nothing from Gisr. The Web media proxy streams artifacts over HTTP from the API. |
| III (addition). Orchestration project | PASS | `Gisr.AppHost` is outside the four application projects; it only launches Api and Web and nothing references it, so the dependency direction in principle III is unchanged. |
| IV. DTO boundary | PASS | Database models (`*Row`/`*Model`) stay in `Gisr.Database` and API mappers; endpoints use `Gisr.Shared` DTOs; mapper tests required. |
| V. Validated minimal APIs | PASS | FluentValidation endpoint filter on every body/query DTO, `MapGroup` per feature, `ProblemDetails` for every error, OpenAPI 3.1 metadata for all endpoints (see [contracts](contracts/)). |
| VI. Dapper, PostgreSQL, plain SQL | PASS | Repositories per feature; embedded `.sql` files loaded and cached by `SqlScriptLoader`; no SQL in C#. pgvector accessed through the same Dapper path. |
| VII. Versioned migrations | PASS | `Scripts/Migrations/0001_*.sql` ... with `schema_migration` ledger (id, checksum, applied_at) and checksum-mismatch failure. `CREATE EXTENSION vector` is itself a migration. |
| VIII. Test-first, real dependencies | PASS | Red-green per task; Testcontainers PostgreSQL (pgvector) and Azurite; every endpoint has success, validation-failure, and not-found/forbidden tests; bUnit for components with logic. |
| Tech: Blazor Interactive Server + Bootstrap 5.3.x only | PASS | Dark theme via `data-bs-theme="dark"` plus CSS variable overrides; icons are inline SVG in an `Icon` component (no icon font/CSS framework added). |
| Tech: Entra OIDC (Web) + JWT bearer (Api); authorize every endpoint | PASS (see Complexity Tracking) | Every Api endpoint requires authorization through the fallback policy, and so does every Web page. The only anonymous Web endpoints are the sign-in challenge `/auth/login` and the framework's static assets. Officer is an app role on the API app registration, read from the `roles` claim. |
| Tech: Observability without sensitive data | PASS | Serilog + OpenTelemetry in Web and Api; log record ids and counts only, never titles, details, search text, or file names. |
| Tech: Data protection and attribution | PASS | Immutable `record_revision` history plus `record_access_log` for cross-citizen reads; sealed-record view grants and override attempts are audited (`record_view_grant`, `record_override_attempt`); override codes are stored only as PBKDF2 hashes and never logged; view tokens are held only on the Web server and stored hashed. |
| Tech: Dependencies justified | PASS | See "Dependency Justification" below. |

No violations of a MUST. Three deviations from the plain reading of the constitution are recorded here for review:

| Deviation | Why it is needed | Simpler alternative rejected because |
|-----------|------------------|--------------------------------------|
| In `Gisr.Database` the SQL scripts live in `Scripts/<Feature>/` (including `Scripts/Migrations/`) instead of beside each feature's repository. | The product owner wants every database script tracked in one place, which also makes the embedded-resource rule a single glob and lets a test assert that no SQL sits elsewhere. The feature subfolders keep each feature's scripts together, so a feature is still one folder plus its repository. | Keeping the scripts next to the repositories is the literal reading of principles II and VI; it scatters the scripts across seven folders. |
| `Gisr.Web` has anonymous endpoints: `GET /auth/login` and the static assets (`MapStaticAssets().AllowAnonymous()`). The Api has none. | `/auth/login` is the Entra challenge a signed-out user is sent to, and the framework assets are what the Blazor shell and error pages need to render. Neither returns or accepts record data. | With the fallback policy alone, `/auth/login` would challenge twice: once for the policy and again in the handler after sign-in. |
| `Gisr.Web` has `Auth/`, `Layout/` and `Components/` folders that are not features and not named `Common/`. | They hold the sign-in plumbing, the page shell (top bar, side navigation, page header) and the app root, none of which belongs to one feature. They are explicitly named and contain no feature code. | Moving them under `Common/` renames folders without making any feature easier to find or remove. |

## Project Structure

### Documentation (this feature)

```text
specs/001-gisr-core-records/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── api.openapi.yaml
│   └── ui-contract.md
└── tasks.md              # produced later by /speckit-tasks
```

### Source Code (repository root)

```text
Directory.Build.props
Directory.Packages.props
Gisr.slnx

src/
├── Gisr.AppHost/                    # Aspire orchestration (dev-time only; nothing references it)
├── Gisr.Shared/                     # DTOs and contracts only
│   ├── Common/                      # PagedResult<T>, paging requests
│   ├── Accounts/
│   ├── Affiliations/
│   ├── Records/                     # record, incident, award, history DTOs
│   ├── Artifacts/
│   ├── Gallery/
│   ├── Search/
│   └── Notifications/
├── Gisr.Database/                   # no Gisr project references
│   ├── Common/                      # SqlScriptLoader, connection factory, Pgvector setup
│   ├── Migrations/                  # MigrationRunner, migration source, ledger (the scripts are in Scripts/Migrations)
│   ├── Accounts/                    # AccountRepository
│   ├── Records/                     # RecordRepository, RevisionRepository, AccessLog, ViewGrant/OverrideAttempt (shared "locked for caller" predicate)
│   ├── Artifacts/                   # ArtifactRepository, IArtifactStore (Blob)
│   ├── Gallery/                     # GalleryRepository
│   ├── Search/                      # SearchRepository, IndexJobRepository
│   ├── Notifications/               # NotificationRepository
│   └── Scripts/                     # every .sql file, in one subfolder per feature: Accounts, Artifacts, Gallery, Migrations (0001_*.sql ...), Notifications, Records, Search
├── Gisr.Api/
│   ├── Common/                      # auth policies, current-user, ProblemDetails, validation filter, OpenAPI
│   ├── Accounts/                    # endpoints, validators, mappers, AccountService
│   ├── Affiliations/                # AffiliationCatalog (field definitions) + endpoint
│   ├── Records/                     # endpoints, validators (affiliation-aware), RecordService, OverrideCodeHasher, OverrideService (lockout, view grants), ViewGrantPurgeService, mappers
│   ├── Artifacts/                   # upload endpoint, file validation, thumbnailing
│   ├── Gallery/
│   ├── Search/                      # SearchService, chunker, SearchIndexingWorker, embedding + extraction clients
│   └── Notifications/
└── Gisr.Web/
    ├── Common/                      # ApiClient (token-attached HttpClient), CurrentUserState
    ├── Auth/                        # OIDC setup, login/logout endpoints
    ├── Layout/                      # MainLayout, TopBar, SideNav, PageHeader, Icon
    ├── Accounts/                    # AccountSettings page
    ├── Records/                     # RecordList, RecordView, RecordEdit, AffiliationFields components
    ├── Artifacts/                   # ArtifactUploader, ArtifactList, media proxy endpoints
    ├── Gallery/                     # GalleryIndex, GalleryRecord, Lightbox
    ├── Search/                      # SearchResults, TopBar search box
    ├── Notifications/               # NotificationMenu
    └── wwwroot/                     # vendored Bootstrap 5.3.x, theme.css, ES modules

tests/
├── Gisr.Database.Tests/             # mirrors Database feature folders (Testcontainers)
├── Gisr.Api.Tests/                  # mirrors Api feature folders (WebApplicationFactory)
└── Gisr.Web.Tests/                  # mirrors Web feature folders (bUnit)
```

**Structure Decision**: The constitution mandates the four-project layout with mirrored test
projects; `Gisr.Web.Tests` is added because the constitution requires tests for UI components with
logic. `Gisr.AppHost` (Aspire) is an additional orchestration project: it references `Gisr.Api` and
`Gisr.Web` only to launch them, no application project references it, and it adds no runtime
dependency to them. It replaces a hand-written compose file for local PostgreSQL and Azurite.

## Dependency Justification

| Package | Needed for | Why not alternatives |
|---------|-----------|----------------------|
| `Microsoft.Identity.Web` | Entra OIDC sign-in, token acquisition for the API, JWT validation | First-party; hand-rolling OIDC/JWT is a security risk. |
| `FluentValidation` | Required by constitution V | n/a |
| `Npgsql`, `Dapper` | Required by constitution VI | n/a |
| `Pgvector`, `Pgvector.Dapper` | Vector column mapping for semantic search | Storing embeddings as `float8[]` forfeits indexed similarity search. |
| `Azure.Storage.Blobs`, `Azure.Identity` | Artifact bytes/thumbnails; managed-identity auth | Large binaries in PostgreSQL bloat backups; see research D-7. |
| `Azure.AI.OpenAI`, `Microsoft.Extensions.AI.OpenAI` | Embedding generation behind `IEmbeddingGenerator` (swappable/fakeable) | Local embedding models add hosting and GPU burden. |
| `PdfPig` (UglyToad's package; the `UglyToad.PdfPig` id on the feed is an unrelated custom build) | Free local text extraction from text-based PDFs (Apache-2.0) | Sending every PDF to Azure Document Intelligence Read costs about $1.50 per 1,000 pages (roughly $750 for 500,000 pages); see research D-21. |
| `DocumentFormat.OpenXml` | Free local text extraction from DOCX (Microsoft, MIT) | Document Intelligence for DOCX adds cost with no benefit. |
| `Azure.AI.DocumentIntelligence` | OCR fallback only for scanned PDFs that PdfPig cannot read, behind `IOcrClient`, managed-identity auth, monthly page budget (default 2,000 pages, about $3) and 100-page per-document cap | Skipping scanned PDFs would leave them unsearchable (product owner chose to include them); local OCR engines are heavy native dependencies. |
| `SkiaSharp` (+ Linux native assets) | Thumbnail generation | MIT licensed; ImageSharp's split license is a compliance risk. |
| `Serilog.AspNetCore`, `OpenTelemetry.*` | Required by constitution observability | n/a |
| `Npgsql.OpenTelemetry` | Database spans for the required OpenTelemetry tracing (Npgsql ships its own instrumentation source) | First-party; without it database calls would be invisible in traces. |
| `Microsoft.AspNetCore.OpenApi` | Generates the OpenAPI document the constitution requires and the conformance test compares with the contract | First-party and built into the framework; a third-party generator adds a dependency for the same output. |
| `YamlDotNet` (test project only) | Reads `contracts/api.openapi.yaml` in the OpenAPI conformance test | The contract is YAML, and a hand-written parser for flow-style YAML would be fragile. Not referenced by any runtime project. |
| `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector` (test projects only) | The xUnit test stack required by constitution VIII | n/a |
| Version pins in `Directory.Packages.props` (`Microsoft.Extensions.*`, `System.Security.Cryptography.*`, `Microsoft.AspNetCore.TestHost`, and similar) | Raise transitive packages that carry known vulnerabilities; they add no new dependency | Leaving the vulnerable versions would fail the zero-warning build through NuGet audit. |
| `Aspire.AppHost.Sdk`, `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Azure.Storage` (AppHost project only) | Local orchestration of PostgreSQL (pgvector image), the Azurite emulator, Api, and Web with injected configuration and a dashboard | First-party; a hand-written compose file does not wire Api/Web configuration or start the projects. Not referenced by any runtime project. |
| `Testcontainers.PostgreSql`, `Testcontainers.Azurite`, `Microsoft.AspNetCore.Mvc.Testing`, `bunit` | Required real-dependency, API, and component tests | n/a |

Bootstrap 5.3.x is vendored into `wwwroot/lib/bootstrap` at a pinned patch version (no npm build chain).

## Phase 1 Design Artifacts

- [research.md](research.md) - decisions and rejected alternatives
- [data-model.md](data-model.md) - entities, constraints, state transitions, access rules
- [contracts/api.openapi.yaml](contracts/api.openapi.yaml) - HTTP contract between Web and Api
- [contracts/ui-contract.md](contracts/ui-contract.md) - shell, routes, and page contracts
- [quickstart.md](quickstart.md) - environment setup and end-to-end validation scenarios

## Post-Design Constitution Re-check

All gates remain PASS. The two design points that could have strained the constitution were
resolved in-bounds: artifact bytes reach the browser through a Web proxy that calls the Api over
HTTP (Principle III), and affiliation field definitions live in `Gisr.Api` as code, not in
`Gisr.Shared` or SQL, so `Gisr.Shared` stays contracts-only (Principle III/IV).
