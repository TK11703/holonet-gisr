---

description: "Task list for GISR Core Records (001-gisr-core-records)"
---

# Tasks: GISR Core Records

**Input**: Design documents from `/specs/001-gisr-core-records/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/api.openapi.yaml](contracts/api.openapi.yaml), [contracts/ui-contract.md](contracts/ui-contract.md), [quickstart.md](quickstart.md)

**Tests**: REQUIRED. Constitution principle VIII mandates test-first with real dependencies (xUnit, Testcontainers PostgreSQL with pgvector, Azurite, `WebApplicationFactory`, bUnit). In every phase, write the test tasks first and confirm they FAIL before starting the implementation tasks. External AI services are faked only at `IEmbeddingGenerator` and `IOcrClient`; the database and blob store are never mocked.

**Organization**: Tasks are grouped by user story so each can be implemented and tested independently (see Dependencies for the few cross-story couplings).

## Format: `- [ ] T### [P?] [Story?] Description with file path`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[USn]**: user story from [spec.md](spec.md) (US4A is the sealed-records story)
- Paths: `src/Gisr.Shared`, `src/Gisr.Database`, `src/Gisr.Api`, `src/Gisr.Web`, `tests/Gisr.Database.Tests`, `tests/Gisr.Api.Tests`, `tests/Gisr.Web.Tests` (feature folders mirror each other: `Accounts/`, `Affiliations/`, `Records/`, `Artifacts/`, `Gallery/`, `Search/`, `Notifications/`, shared code in `Common/`)

- Task IDs are stable. Tasks added after the consistency analysis use T202-T209 and sit in their phase's test or implementation block; execute by position within the phase, not by number.

## Global rules every task must follow

- C# 14 / .NET 10, nullable enabled, warnings are errors; all I/O async and takes a `CancellationToken`.
- SQL only in individual `.sql` files (embedded, loaded by `SqlScriptLoader`), always parameterized; no SQL string literals in C#.
- Endpoints accept/return `Gisr.Shared` DTOs only; validate with FluentValidation; return `ProblemDetails`; require authorization; `MapGroup` per feature; declare request/response types and status codes.
- Identity and Officer status come only from validated token claims; owner/subject scoping is done in SQL.
- Never log or return record content, search text, file names, override codes, or view tokens.
- Dates: `DateOnly`/`date` for `*_on` and engagement dates; `DateTimeOffset`/`timestamptz` (UTC) for `*_at`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution skeleton and tooling. No user-story work yet.

- [X] T001 Create solution `Gisr.slnx` with projects `src/Gisr.Shared` (classlib), `src/Gisr.Database` (classlib), `src/Gisr.Api` (ASP.NET Core web), `src/Gisr.Web` (Blazor Web App, Interactive Server, global render mode), `tests/Gisr.Database.Tests`, `tests/Gisr.Api.Tests`, `tests/Gisr.Web.Tests` (xUnit); references exactly: Web -> Shared; Api -> Shared, Database; Database -> none; Database.Tests -> Database; Api.Tests -> Api, Shared, Database; Web.Tests -> Web, Shared
- [X] T002 [P] Create `Directory.Build.props` setting `net10.0`, `LangVersion` 14, `Nullable` enable, `ImplicitUsings` enable, `TreatWarningsAsErrors` true, `AnalysisLevel` latest
- [X] T003 Create `Directory.Packages.props` (central package management, stable versions only) with the packages from plan.md Dependency Justification: Microsoft.Identity.Web, FluentValidation, Npgsql, Dapper, Pgvector, Pgvector.Dapper, Azure.Storage.Blobs, Azure.Identity, Azure.AI.OpenAI, Microsoft.Extensions.AI.OpenAI, UglyToad.PdfPig, DocumentFormat.OpenXml, Azure.AI.DocumentIntelligence, SkiaSharp (+ NativeAssets.Linux.NoDependencies), Serilog.AspNetCore, OpenTelemetry.* (Extensions.Hosting, Instrumentation.AspNetCore/Http/Runtime, Exporter.OpenTelemetryProtocol), Microsoft.AspNetCore.Mvc.Testing, Testcontainers.PostgreSql, Testcontainers.Azurite, bunit, xunit; add package references to the matching projects
- [X] T004 [P] Superseded by T209: local PostgreSQL (pgvector) and Azurite are started by the Aspire AppHost, so no compose file is kept
- [X] T005 [P] Vendor Bootstrap 5.3.x CSS only (no JS bundle, no jQuery) into `src/Gisr.Web/wwwroot/lib/bootstrap/` at a pinned patch version and record the version in `src/Gisr.Web/wwwroot/lib/bootstrap/VERSION.txt`
- [X] T006 [P] Add configuration skeletons without secrets: `src/Gisr.Api/appsettings.json` (sections `AzureAd`, `ConnectionStrings:Gisr`, `Storage:BlobEndpoint`, `Search:OpenAiEndpoint`, `Search:EmbeddingDeployment`, `Search:OcrFallback:Enabled` = true, `Search:OcrFallback:Endpoint`, `Search:OcrFallback:MonthlyPageBudget` = 2000) and `src/Gisr.Web/appsettings.json` (sections `AzureAd`, `Api:BaseUrl`, `Api:Scope`); enable user-secrets in both projects
- [X] T209 [P] Create `src/Gisr.AppHost` (Aspire AppHost, `Aspire.AppHost.Sdk` 13.6.x) in `src/Gisr.AppHost/AppHost.cs`: PostgreSQL using image `pgvector/pgvector` tag `pg17` with a data volume and database `Gisr`; Azure Storage emulator (Azurite) with blobs resource `blobs`; `Gisr.Api` (references the database and blobs, `AzureAd__TenantId`/`AzureAd__ClientId`) and `Gisr.Web` (external endpoints, `Api__BaseUrl`, `Api__Scope`, `AzureAd__*` including the client secret as a secret parameter); Entra values are AppHost parameters supplied by user-secrets; add the project to `Gisr.slnx`; only the AppHost references Api and Web

**Checkpoint**: `dotnet build` of the empty solution succeeds with zero warnings.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting infrastructure every story needs. No user story may start until this phase is complete.

### Shared

- [X] T007 [P] Create `PagedResult<T>` (`Page`, `PageSize`, `TotalCount`, `Items`) and `PagedRequest` (`Page` >= 1, `PageSize`) in `src/Gisr.Shared/Common/PagedResult.cs` and `src/Gisr.Shared/Common/PagedRequest.cs`

### Database foundation (tests first)

- [X] T008 [P] Write tests for `SqlScriptLoader` (loads embedded `.sql` by feature path, caches, throws for unknown script) in `tests/Gisr.Database.Tests/Common/SqlScriptLoaderTests.cs`
- [X] T009 [P] Create the PostgreSQL+pgvector Testcontainers collection fixture (image `pgvector/pgvector:pg17`, applies migrations, per-test database reset helper) in `tests/Gisr.Database.Tests/Common/PostgresFixture.cs`
- [X] T010 [P] Write `MigrationRunner` tests in `tests/Gisr.Database.Tests/Migrations/MigrationRunnerTests.cs`: applies all scripts to an empty database in numeric order; records `(script_id, checksum_sha256, applied_at)` in `schema_migration`; re-run is a no-op; a changed checksum of an applied script makes the runner fail; a failing script rolls back and is not recorded; two runners started concurrently apply each script exactly once
- [X] T011 Implement `SqlScriptLoader` (embedded-resource `.sql` files per feature folder, loaded once and cached) in `src/Gisr.Database/Common/SqlScriptLoader.cs`; configure `<EmbeddedResource Include="Scripts/**/*.sql" />` in `src/Gisr.Database/Gisr.Database.csproj`
- [X] T012 Implement data access plumbing in `src/Gisr.Database/Common/`: `NpgsqlDataSource` factory with `UseVector()`, Dapper type handlers for `DateOnly`, `Pgvector.Vector`, snake_case column mapping, `AddGisrDatabase(...)` in `ServiceCollectionExtensions.cs`
- [X] T013 Implement `MigrationRunner` (numbered forward-only scripts embedded in `src/Gisr.Database/Migrations/`, `schema_migration` ledger `script_id text PK, checksum_sha256 text NOT NULL, applied_at timestamptz NOT NULL`, fail on checksum mismatch, each script in a transaction, whole run guarded by a PostgreSQL advisory lock so concurrently starting instances apply each script once) in `src/Gisr.Database/Migrations/MigrationRunner.cs` and add `src/Gisr.Database/Scripts/Migrations/0001_create_extensions.sql` (`CREATE EXTENSION IF NOT EXISTS vector;`)

### Api foundation (tests first)

- [X] T014 [P] Write `CurrentUser` tests (reads `oid` and `tid` as Guids, `isOfficer` true only when the `roles` claim contains `Officer`, throws when `oid` is missing) in `tests/Gisr.Api.Tests/Common/CurrentUserTests.cs`
- [X] T015 [P] Write `ValidationEndpointFilter` tests (invalid DTO -> 400 `ValidationProblemDetails` with `errors`; valid DTO passes through; runs before the handler; also validates query DTOs bound with `[AsParameters]`) in `tests/Gisr.Api.Tests/Common/ValidationEndpointFilterTests.cs`
- [X] T016 [P] Write ProblemDetails behavior tests (unhandled exception -> 500 `application/problem+json` with no stack trace or message content; unauthenticated -> 401 ProblemDetails; forbidden -> 403 ProblemDetails) in `tests/Gisr.Api.Tests/Common/ProblemDetailsTests.cs`
- [X] T017 [P] Create API test infrastructure: `ApiFactory` (`WebApplicationFactory<Program>` using the PostgreSQL fixture, a test authentication scheme overriding JWT bearer, fake `IEmbeddingGenerator`/`IOcrClient` registrations added when those exist) in `tests/Gisr.Api.Tests/Common/ApiFactory.cs`, and `TestAuthHandler` (headers `X-Test-Oid`, `X-Test-Tid`, `X-Test-Roles`) in `tests/Gisr.Api.Tests/Common/TestAuthHandler.cs`, plus a `TestClients` helper creating a regular user client and an Officer client
- [X] T018 Implement `CurrentUser` (`ICurrentUser`: `AccountKey` (tid, oid), `IsOfficer`, display name/email from claims) in `src/Gisr.Api/Common/CurrentUser.cs`
- [X] T019 Implement `ValidationEndpointFilter<T>` (FluentValidation for body and `[AsParameters]` query DTOs -> RFC 9457 `ValidationProblemDetails`, 400) in `src/Gisr.Api/Common/ValidationEndpointFilter.cs`
- [X] T020 Wire `src/Gisr.Api/Program.cs` and `src/Gisr.Api/Common/`: Serilog, OpenTelemetry traces/metrics (ASP.NET Core, HttpClient, Npgsql, runtime), Microsoft.Identity.Web JWT bearer validation for scope `access_as_user`, fallback authorization policy requiring an authenticated user (no anonymous endpoints), `AddProblemDetails` + global exception handler, built-in OpenAPI 3.1 document, `AddGisrDatabase`, run `MigrationRunner` on startup, `public partial class Program` for tests

### Web foundation (tests first)

- [X] T021 [P] Write `ApiClient` tests (attaches bearer token, maps `ProblemDetails` to `ApiException` with status and field errors, supports optional extra headers) in `tests/Gisr.Web.Tests/Common/ApiClientTests.cs`
- [X] T022 [P] Write `TopBar` bUnit tests (site name "GISR" links to `/records`; search input with accessible name "Search" navigates to `/search?q=...` on Enter; "New record" button links to `/records/new`; hamburger button visible for narrow screens; no theme toggle) in `tests/Gisr.Web.Tests/Layout/TopBarTests.cs`
- [X] T023 [P] Write `SideNav` bUnit tests (sections Records [Citizen Records `/records`, Gallery `/gallery`], Find [Search `/search`], Account [Account Settings `/account`]; active item has `aria-current="page"`; off-canvas toggle with focus trap hook) in `tests/Gisr.Web.Tests/Layout/SideNavTests.cs`
- [X] T024 [P] Write `PageHeader` bUnit tests (renders `h1` title followed by `nav aria-label="breadcrumb"` on the same row; last crumb is current) in `tests/Gisr.Web.Tests/Layout/PageHeaderTests.cs`
- [X] T025 [P] Write `MainLayout`/`App` tests (root `html` has `data-bs-theme="dark"`; landmarks `header`, `nav`, `main`; skip-to-content link is first; no light/dark switch anywhere) in `tests/Gisr.Web.Tests/Layout/MainLayoutTests.cs`
- [X] T026 Implement `ApiClient` (typed `HttpClient` with Microsoft.Identity.Web downstream token, `ApiException`, optional per-request headers) in `src/Gisr.Web/Common/ApiClient.cs` and `src/Gisr.Web/Common/ApiException.cs`
- [X] T027 Wire `src/Gisr.Web/Program.cs` and `src/Gisr.Web/Auth/AuthEndpoints.cs`: Blazor Interactive Server, Entra OIDC (auth code + PKCE, cookie session) with token acquisition for `Api:Scope`, `/auth/login` challenge and `/auth/logout` sign-out (POST, antiforgery), authorization required for all routes except login, Serilog + OpenTelemetry, `public partial class Program`
- [X] T028 Create the shell: `src/Gisr.Web/Components/App.razor` (`<html lang="en" data-bs-theme="dark">`, vendored Bootstrap link, `theme.css`), `src/Gisr.Web/Layout/MainLayout.razor` (skip link, `header` + `nav` + `main`), and `src/Gisr.Web/wwwroot/theme.css` (Bootstrap CSS-variable overrides for the deep teal-green dark palette and teal accent from the reference screenshot, 3rem full-width fixed top bar, full-height left nav under it, scrollable content region, WCAG AA contrast, `prefers-reduced-motion` disables transitions)
- [X] T029 [P] Implement the `Icon` component (inline SVG paths for bell, plus, search, lock, records, gallery, user, logout, settings, chevron, menu, close, file, image; accessible-name aware, decorative by default) in `src/Gisr.Web/Layout/Icon.razor`
- [X] T030 [P] Implement `PageHeader` (`Title`, `Crumbs`) in `src/Gisr.Web/Layout/PageHeader.razor`
- [X] T031 Implement `SideNav` (grouped sections, active highlighting, off-canvas below `lg` driven by Blazor state) in `src/Gisr.Web/Layout/SideNav.razor`
- [X] T032 Implement `TopBar` (site name, pill search box, "New record" primary button, slots for notification menu and profile menu) in `src/Gisr.Web/Layout/TopBar.razor`
- [X] T033 Create ES modules `src/Gisr.Web/wwwroot/js/focus-trap.js` (trap, Escape, restore focus) and `src/Gisr.Web/wwwroot/js/shortcuts.js` (Ctrl+K focuses search) with a disposable module-interop wrapper `src/Gisr.Web/Layout/ShellInterop.cs`; wire into `SideNav` and `TopBar`

**Checkpoint**: Foundation ready. Tests T008-T010, T014-T016, T021-T025 pass; empty app runs with the dark shell and Entra sign-in.

---

## Phase 3: User Story 1 - Sign In and View Account Settings (Priority: P1) MVP

**Goal**: A user signs in with Entra, an account is created on first sign-in, and the user views/edits their own account settings. No citizen records exist yet.

**Independent Test**: Sign in as a new user, see the "no records yet" invitation, open Account settings from the profile menu, change the display name, sign out and back in, and confirm the change persisted and no other user's data is reachable (quickstart scenarios 1 and 2).

### Tests for User Story 1 (write first; must fail)

- [X] T034 [P] [US1] Write `AccountRepository` tests (upsert creates exactly one row per `(tenant_id, entra_object_id)`, second call updates `last_sign_in_at` only, update settings, get by id) in `tests/Gisr.Database.Tests/Accounts/AccountRepositoryTests.cs`
- [X] T035 [P] [US1] Write endpoint tests for `GET /api/me` (401 anonymous; 200 creates the account on first call; `isOfficer` true only with `Officer` role; `notifyOnServiceRecord` defaults true) and `PUT /api/me/settings` (200 persisted; 400 on invalid input; 401) in `tests/Gisr.Api.Tests/Accounts/AccountEndpointsTests.cs`
- [X] T036 [P] [US1] Write `AccountMapper` tests (row -> `AccountDto`, never exposes tenant/object ids) in `tests/Gisr.Api.Tests/Accounts/AccountMapperTests.cs`
- [X] T037 [P] [US1] Write `UpdateAccountSettingsRequestValidator` tests (display name "1-100 chars", contact email "valid email, max 254", both boundary values) in `tests/Gisr.Api.Tests/Accounts/UpdateAccountSettingsRequestValidatorTests.cs`
- [X] T038 [P] [US1] Write `ProfileMenu` bUnit tests (avatar initials button with display name; items "Account settings" -> `/account` and "Sign out" posting to `/auth/logout`; click/Escape/arrow-key behavior; focus returns to trigger) in `tests/Gisr.Web.Tests/Accounts/ProfileMenuTests.cs`
- [X] T039 [P] [US1] Write `AccountSettings` page bUnit tests (read-only sign-in identity and Officer credential state; editable display name, contact email, notify toggle; inline validation errors; success toast) in `tests/Gisr.Web.Tests/Accounts/AccountSettingsPageTests.cs`
- [X] T040 [P] [US1] Write `Home` bUnit tests (new user with no records sees the invitation to create one with a "New record" link) in `tests/Gisr.Web.Tests/Accounts/HomePageTests.cs`

### Implementation for User Story 1

- [X] T041 [US1] Add migration `src/Gisr.Database/Scripts/Migrations/0002_accounts.sql` creating `account`: `id uuid PK`; `tenant_id uuid NOT NULL`; `entra_object_id uuid NOT NULL` with UNIQUE `(tenant_id, entra_object_id)`; `display_name text NOT NULL` ("1-100 chars"; add `CHECK (char_length(display_name) BETWEEN 1 AND 100)`); `sign_in_email text NULL`; `contact_email text NULL` ("valid email, max 254"; add `CHECK (char_length(contact_email) <= 254)`); `notify_on_service_record boolean NOT NULL DEFAULT true`; `created_at timestamptz NOT NULL`; `last_sign_in_at timestamptz NOT NULL`
- [X] T042 [P] [US1] Create `AccountRow` and SQL files `upsert-account.sql`, `get-account-by-id.sql`, `update-account-settings.sql` in `src/Gisr.Database/Accounts/`
- [X] T043 [US1] Implement `AccountRepository` in `src/Gisr.Database/Accounts/AccountRepository.cs`
- [X] T044 [P] [US1] Create DTOs `AccountDto`, `UpdateAccountSettingsRequest` in `src/Gisr.Shared/Accounts/` per contracts/api.openapi.yaml
- [X] T045 [P] [US1] Implement `UpdateAccountSettingsRequestValidator` in `src/Gisr.Api/Accounts/UpdateAccountSettingsRequestValidator.cs` (display name 1-100 chars; contact email valid email, max 254, nullable)
- [X] T046 [US1] Implement `AccountMapper` and `AccountService` (`EnsureAccountAsync` from claims, `UpdateSettingsAsync`) in `src/Gisr.Api/Accounts/`
- [X] T047 [US1] Implement `AccountEndpoints` (`MapGroup("/api/me")`: `GET /`, `PUT /settings`, with OpenAPI metadata, validation filter, ProblemDetails) in `src/Gisr.Api/Accounts/AccountEndpoints.cs` and register in `Program.cs`
- [X] T048 [US1] Implement `CurrentUserState` (scoped; loads `GET /api/me` once per circuit; exposes display name, `IsOfficer`) in `src/Gisr.Web/Common/CurrentUserState.cs`
- [X] T049 [US1] Implement `ProfileMenu.razor` and place it in `TopBar` in `src/Gisr.Web/Accounts/ProfileMenu.razor` and `src/Gisr.Web/Layout/TopBar.razor`
- [X] T050 [US1] Implement `AccountSettings.razor` (`/account`, PageHeader "Account settings") in `src/Gisr.Web/Accounts/AccountSettings.razor`
- [X] T051 [US1] Implement `Home.razor` (`/`) showing the "no citizen records yet" invitation with a "New record" link in `src/Gisr.Web/Accounts/Home.razor`

**Checkpoint**: US1 is fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 - Create and Manage My Own Citizen Records (Priority: P2)

**Goal**: A signed-in user creates any number of citizen records for themselves, with title, affiliation, affiliation-specific engagement details, incidents, and awards; views, lists, and edits them; history and concurrency protection included.

**Independent Test**: As a new user with no credentials, create a Military record (see inline errors when Rank is missing), then fix and save; create a Jedi record; confirm both are listed; edit one in two tabs and confirm the second save gets a conflict (quickstart scenarios 3 and 4). Artifacts are not part of this story.

### Tests for User Story 2 (write first; must fail)

- [X] T052 [P] [US2] Write migration constraint tests (title length, `engagement_end >= engagement_start` check, `revision (record_id, version)` unique, FK integrity) in `tests/Gisr.Database.Tests/Records/RecordMigrationTests.cs`
- [X] T053 [P] [US2] Write `RecordRepository` tests (create with incidents and awards in one transaction; update with matching `expectedVersion` increments `version`; stale `expectedVersion` affects 0 rows; replace-collections semantics; list `scope=mine` ordered by `updated_at DESC`, affiliation filter, sort options, paging; unknown keys preserved in `engagement_detail`) in `tests/Gisr.Database.Tests/Records/RecordRepositoryTests.cs`
- [X] T054 [P] [US2] Write `RevisionRepository` tests (one revision per create/update with full snapshot of record + incidents + awards; history paging newest first) in `tests/Gisr.Database.Tests/Records/RevisionRepositoryTests.cs`
- [X] T055 [P] [US2] Write `AffiliationCatalog` tests asserting every affiliation/field in data-model.md (civilian, military, jedi, bounty_hunter; keys, types, required flags, select options, `summary` required max 4000) in `tests/Gisr.Api.Tests/Affiliations/AffiliationCatalogTests.cs`
- [X] T056 [P] [US2] Write validator tests for `CreateRecordRequestValidator`/`UpdateRecordRequestValidator` (title 1-200; affiliation in catalog; engagement_detail per field definitions incl. required/select options/max lengths; `summary` required; end date >= start; incident title 1-200, description max 4000, occurred_on required; award name 1-200, issuer max 200, description max 2000, awarded_on required; at most 100 incidents and 100 awards; `expectedVersion` required and >= 1 on update) in `tests/Gisr.Api.Tests/Records/RecordValidatorTests.cs`
- [X] T057 [P] [US2] Write `RecordMapper` tests (row <-> DTO, `isServiceRecord` derived, `canEdit` rule, engagement_detail round trip) in `tests/Gisr.Api.Tests/Records/RecordMapperTests.cs`
- [X] T058 [P] [US2] Write endpoint tests for self-only record flows in `tests/Gisr.Api.Tests/Records/RecordEndpointsTests.cs`: `POST /api/records` 201 + Location, 400 validation, 401; `GET /api/records/{id}` own 200, another user's 404; `PUT` 200 bumps version, 409 on stale version, 400 invalid; `GET /api/records` paging/filter/sort; `GET /api/records/{id}/history`; a user can create several records; a new user starts with none; `GET /api/records` with an invalid `scope`, `sort`, `page` or `pageSize` -> 400; history with `pageSize` out of range -> 400
- [X] T059 [P] [US2] Write `GET /api/affiliations` endpoint tests (200, requires auth, shape matches FieldDefinitionDto) in `tests/Gisr.Api.Tests/Affiliations/AffiliationEndpointsTests.cs`
- [X] T060 [P] [US2] Write bUnit tests for `RecordForm` and field components (affiliation select renders its fields from the affiliation DTO; required markers and inline errors with `aria-describedby`; incident/award add/remove; keeps entered values on 400; shows version-conflict alert preserving input on 409) in `tests/Gisr.Web.Tests/Records/RecordFormTests.cs`
- [X] T061 [P] [US2] Write bUnit tests for `RecordList` (table columns, affiliation filter, sort, paging, empty state "Create your first record") in `tests/Gisr.Web.Tests/Records/RecordListTests.cs`
- [X] T062 [P] [US2] Write bUnit tests for `RecordView` (header, Engagement details, Incidents, Awards, History sections; Edit button only when `canEdit`) in `tests/Gisr.Web.Tests/Records/RecordViewTests.cs`
- [X] T202 [P] [US2] Write query validator tests (`RecordListQueryValidator`: `scope` in {mine, all}, `affiliation` in the catalog codes, `sort` in {updatedDesc, updatedAsc, titleAsc, titleDesc}, `page` >= 1, `pageSize` 1-100; `HistoryQueryValidator`: `page` >= 1, `pageSize` 1-100) in `tests/Gisr.Api.Tests/Records/RecordQueryValidatorTests.cs`

### Implementation for User Story 2

- [X] T063 [US2] Add migration `src/Gisr.Database/Scripts/Migrations/0003_records.sql` creating: `record` (`id uuid PK`; `subject_account_id uuid NOT NULL FK account`; `author_account_id uuid NOT NULL FK account`; `title text NOT NULL` with "1-200 chars"; `affiliation text NOT NULL`; `engagement_start date NULL`; `engagement_end date NULL` "must be >= start when both present"; `engagement_detail jsonb NOT NULL DEFAULT '{}'`; `version integer NOT NULL DEFAULT 1`; `created_at, updated_at timestamptz NOT NULL`; `updated_by_account_id uuid NOT NULL FK account`; indexes `(subject_account_id, updated_at DESC)` and `(affiliation)`), `record_incident` (`id`, `record_id FK`, `occurred_on date NOT NULL`, `title text NOT NULL` "1-200", `description text NULL` "max 4000", `sort_order integer NOT NULL`), `record_award` (`id`, `record_id FK`, `name text NOT NULL` "1-200", `awarded_on date NOT NULL`, `issuer text NULL` "max 200", `description text NULL` "max 2000", `sort_order integer NOT NULL`), `record_revision` (`id`, `record_id FK`, `version integer NOT NULL` UNIQUE with record_id, `changed_by_account_id FK`, `changed_at timestamptz NOT NULL`, `snapshot jsonb NOT NULL`); no cascade deletes (records are never deleted)
- [X] T064 [P] [US2] Create row models (`RecordRow`, `IncidentRow`, `AwardRow`, `RevisionRow`) and SQL files `insert-record.sql`, `update-record.sql` (`WHERE id=@id AND version=@expectedVersion`), `get-record.sql`, `list-records.sql` (`subject_account_id = @callerId`, affiliation filter, sort, paging), `count-records.sql`, `insert-incident.sql`, `insert-award.sql`, `delete-incidents.sql`, `delete-awards.sql`, `list-incidents.sql`, `list-awards.sql`, `insert-revision.sql`, `list-revisions.sql` in `src/Gisr.Database/Records/`
- [X] T065 [US2] Implement `RecordRepository` (transactional create/update including collections and revision insert, conflict signaled by a typed result) in `src/Gisr.Database/Records/RecordRepository.cs`
- [X] T066 [US2] Implement `RevisionRepository` in `src/Gisr.Database/Records/RevisionRepository.cs`
- [X] T067 [P] [US2] Create DTOs `RecordDto`, `RecordSummaryDto`, `CreateRecordRequest`, `UpdateRecordRequest`, `IncidentDto`, `AwardDto`, `RevisionDto`, `AffiliationDto`, `FieldDefinitionDto` in `src/Gisr.Shared/Records/` and `src/Gisr.Shared/Affiliations/` per contracts/api.openapi.yaml
- [X] T068 [US2] Implement `AffiliationCatalog` (code-defined field definitions exactly as in data-model.md) and `AffiliationEndpoints` (`GET /api/affiliations`) in `src/Gisr.Api/Affiliations/`
- [X] T069 [P] [US2] Implement validators (`CreateRecordRequestValidator`, `UpdateRecordRequestValidator`, `IncidentDtoValidator`, `AwardDtoValidator`, `EngagementDetailValidator`) in `src/Gisr.Api/Records/` enforcing the rules in data-model.md "Validation rules"; also `RecordListQueryValidator` and `HistoryQueryValidator` (rules in T202) bound with `[AsParameters]` so list and history endpoints validate query input
- [X] T070 [US2] Implement `RecordMapper` in `src/Gisr.Api/Records/RecordMapper.cs`
- [X] T071 [US2] Implement `RecordService` (create for self with author = subject = caller, get own, update with `expectedVersion` -> `ConflictException` -> 409, list `scope=mine`, history; edit allowed only when caller is subject and author) in `src/Gisr.Api/Records/RecordService.cs`
- [X] T072 [US2] Implement `RecordEndpoints` (`MapGroup("/api/records")`: `GET /`, `POST /`, `GET /{recordId}`, `PUT /{recordId}`, `GET /{recordId}/history`; request/response types, status codes 200/201/400/403/404/409, ProblemDetails) in `src/Gisr.Api/Records/RecordEndpoints.cs` and register in `Program.cs`
- [X] T073 [US2] Implement form components `AffiliationFields.razor`, `IncidentsEditor.razor`, `AwardsEditor.razor`, and `RecordForm.razor` in `src/Gisr.Web/Records/`
- [X] T074 [US2] Implement `RecordList.razor` (`/records`, filter/sort/paging/empty state) in `src/Gisr.Web/Records/RecordList.razor`
- [X] T075 [US2] Implement `RecordNew.razor` (`/records/new`) in `src/Gisr.Web/Records/RecordNew.razor`
- [X] T076 [US2] Implement `RecordView.razor` (`/records/{id}`) with sections and history in `src/Gisr.Web/Records/RecordView.razor`
- [X] T077 [US2] Implement `RecordEdit.razor` (`/records/{id}/edit`, 409 conflict alert that preserves input) in `src/Gisr.Web/Records/RecordEdit.razor`
- [X] T078 [US2] Change `Home.razor` to redirect to `/records` and rely on the list empty state for the invitation in `src/Gisr.Web/Accounts/Home.razor` (update `HomePageTests` accordingly)

**Checkpoint**: US1 and US2 both work independently.

---

## Phase 5: User Story 3 - View Other Citizens' Records With a Credential (Priority: P3)

**Goal**: A user with the Officer role can list, view, and edit other citizens' records; everyone else cannot and learns nothing about them. Cross-citizen reads are logged.

**Independent Test**: With a regular user and an Officer, the regular user gets 404 for the other's record and 403 for `scope=all`; the Officer lists all records, opens and edits the other's record, and an access-log row is written (quickstart scenarios 5, 6, 12).

### Tests for User Story 3 (write first; must fail)

- [X] T079 [P] [US3] Write `AccessLogRepository` tests in `tests/Gisr.Database.Tests/Records/AccessLogRepositoryTests.cs` (insert `kind` of `record` or `artifact`, query by record)
- [X] T080 [P] [US3] Write `RecordAccessPolicy` tests covering the full access matrix in data-model.md (view/create/edit/list for subject, Officer, other user) in `tests/Gisr.Api.Tests/Records/RecordAccessPolicyTests.cs`
- [X] T081 [P] [US3] Write Officer endpoint tests in `tests/Gisr.Api.Tests/Records/RecordEndpointsOfficerTests.cs`: other user `GET` record 404; Officer `GET` 200 and one `record_access_log` row (none when viewing own); `scope=all` 403 for non-Officer and 200 for Officer; `subjectAccountId` filter only with `scope=all`; Officer `PUT` on another's record 200 with new revision attributed to the Officer; role removed from token -> next request denied
- [X] T082 [P] [US3] Write bUnit tests for the Officer-only scope switch ("My records"/"All records", hidden for non-Officers) and subject filter on `RecordList` in `tests/Gisr.Web.Tests/Records/RecordListScopeTests.cs`

### Implementation for User Story 3

- [X] T083 [US3] Add migration `src/Gisr.Database/Scripts/Migrations/0004_record_access_log.sql` creating `record_access_log`: `id uuid PK`; `record_id uuid NOT NULL FK record`; `accessed_by_account_id uuid NOT NULL FK account`; `accessed_at timestamptz NOT NULL`; `kind text NOT NULL` (`record` or `artifact`, add `CHECK (kind IN ('record','artifact'))`)
- [X] T084 [P] [US3] Create `insert-access-log.sql` and `AccessLogRepository` in `src/Gisr.Database/Records/`
- [X] T085 [US3] Implement `RecordAccessPolicy` (central rules from research D-6: view/create/edit/list; 404-vs-403 semantics) in `src/Gisr.Api/Records/RecordAccessPolicy.cs`
- [X] T086 [US3] Extend `list-records.sql`/`get-record.sql` and `RecordRepository` for `scope=all` (`@isOfficer` parameter), `subjectAccountId` filter, and subject/author display names in `src/Gisr.Database/Records/`
- [X] T087 [US3] Update `RecordService` and `RecordEndpoints` to use `RecordAccessPolicy`, write an access-log row when a non-subject reads a record, and reject `scope=all` for non-Officers with 403 ProblemDetails in `src/Gisr.Api/Records/`
- [X] T088 [US3] Add the Officer scope switch and subject filter to `RecordList.razor`, show subject/author and an Edit action when `canEdit` on `RecordView.razor` in `src/Gisr.Web/Records/`

**Checkpoint**: US1-US3 work independently.

---

## Phase 6: User Story 4 - Create a Service Record for Another Citizen (Priority: P4)

**Goal**: An Officer creates a record for another known account holder (a service record); the citizen is notified in-app and can read it (read-only) unless sealed (US4A).

**Independent Test**: Officer picks a second user in the Citizen picker and saves; the second user sees a bell badge within 60 s and the record read-only in their list; a non-Officer cannot create for others (quickstart scenario 7).

### Tests for User Story 4 (write first; must fail)

- [X] T089 [P] [US4] Write `NotificationRepository` tests (insert, list newest first with unread count, mark one read only for the recipient, mark all read, index use `(recipient_account_id, read_at, created_at DESC)`) in `tests/Gisr.Database.Tests/Notifications/NotificationRepositoryTests.cs`
- [X] T090 [P] [US4] Write account search tests (`search-accounts.sql` by display name, case-insensitive, limit, excludes nothing sensitive) in `tests/Gisr.Database.Tests/Accounts/AccountSearchTests.cs`
- [X] T091 [P] [US4] Write service-record endpoint tests in `tests/Gisr.Api.Tests/Records/ServiceRecordEndpointsTests.cs`: non-Officer with another `subjectAccountId` -> 403; Officer -> 201 with author = Officer and subject = target and `isServiceRecord` true; unknown subject -> 404; `subjectAccountId` null = self; subject can read it; subject cannot edit it (403); a citizen with no records can be the subject
- [X] T092 [P] [US4] Write `GET /api/accounts` endpoint tests (Officer only, `query` 2-100 chars, `pageSize` 1-25) in `tests/Gisr.Api.Tests/Accounts/AccountDirectoryEndpointsTests.cs`
- [X] T093 [P] [US4] Write notification endpoint tests (`GET /api/notifications` list + `unreadCount` + `unreadOnly`, `POST /{id}/read` 204/404 for others' notifications, `POST /read-all`; `pageSize` outside 1-50 -> 400) in `tests/Gisr.Api.Tests/Notifications/NotificationEndpointsTests.cs`
- [X] T094 [P] [US4] Write `NotificationService` tests (service record creates one notification for the subject in the same transaction as the record; none when `notify_on_service_record` is false; none for self-authored records; message contains no record content) in `tests/Gisr.Api.Tests/Notifications/NotificationServiceTests.cs`
- [X] T095 [P] [US4] Write bUnit tests for the Officer-only "Citizen" picker (defaults to myself; searches `/api/accounts`; hidden for non-Officers) in `tests/Gisr.Web.Tests/Records/CitizenPickerTests.cs`
- [X] T096 [P] [US4] Write bUnit tests for `NotificationMenu` (accessible-name "Notifications" bell, unread badge, dropdown list, "Mark all read", item click navigates and marks read, 60 s polling timer started/stopped with the component, empty state) in `tests/Gisr.Web.Tests/Notifications/NotificationMenuTests.cs`
- [X] T203 [P] [US4] Write tests for `NotificationMapper` (row -> `NotificationDto`, `isRead` from `read_at`, no record content) in `tests/Gisr.Api.Tests/Notifications/NotificationMapperTests.cs` and for `AccountSearchQueryValidator` (`query` 2-100 chars, `pageSize` 1-25) and `NotificationListQueryValidator` (`pageSize` 1-50) in `tests/Gisr.Api.Tests/Notifications/NotificationQueryValidatorTests.cs`

### Implementation for User Story 4

- [X] T097 [US4] Add migration `src/Gisr.Database/Scripts/Migrations/0005_notifications.sql` creating `notification`: `id uuid PK`; `recipient_account_id uuid NOT NULL FK account`; `kind text NOT NULL` (`service_record_created`); `record_id uuid NULL FK record`; `message text NOT NULL` ("no sensitive record content"); `created_at timestamptz NOT NULL`; `read_at timestamptz NULL`; index `(recipient_account_id, read_at, created_at DESC)`
- [X] T098 [P] [US4] Create `NotificationRow`, SQL files (`insert-notification.sql`, `list-notifications.sql`, `mark-notification-read.sql`, `mark-all-read.sql`) and `NotificationRepository` in `src/Gisr.Database/Notifications/`
- [X] T099 [P] [US4] Add `search-accounts.sql` and `AccountRepository.SearchAsync` in `src/Gisr.Database/Accounts/`
- [X] T100 [P] [US4] Create DTOs `AccountSummaryDto`, `NotificationDto`, `NotificationListDto` in `src/Gisr.Shared/Accounts/` and `src/Gisr.Shared/Notifications/`
- [X] T101 [US4] Implement `NotificationService` (creates a `service_record_created` notification inside the record-creation transaction when subject != author and the subject's `notify_on_service_record` is true) and `NotificationMapper` in `src/Gisr.Api/Notifications/`
- [X] T102 [US4] Extend `RecordService`/`RecordAccessPolicy` for Officer creation with `subjectAccountId` (validates the subject exists -> 404 otherwise; non-Officer -> 403; edit of service records by non-Officers denied) and call `NotificationService` in `src/Gisr.Api/Records/`
- [X] T103 [US4] Implement `GET /api/accounts` (Officer only) in `src/Gisr.Api/Accounts/AccountEndpoints.cs` and `NotificationEndpoints` (`MapGroup("/api/notifications")`) in `src/Gisr.Api/Notifications/NotificationEndpoints.cs`; add `AccountSearchQueryValidator` and `NotificationListQueryValidator` (rules in T203) bound with `[AsParameters]`; register in `Program.cs`
- [X] T104 [US4] Add the Officer-only `CitizenPicker.razor` to `RecordForm` in `src/Gisr.Web/Records/CitizenPicker.razor` and `RecordForm.razor`
- [X] T105 [US4] Implement `NotificationMenu.razor` (bell with badge, 60 s polling timer disposed with the component) and place it in `TopBar` in `src/Gisr.Web/Notifications/NotificationMenu.razor` and `src/Gisr.Web/Layout/TopBar.razor`
- [X] T106 [US4] Show the "Service record" badge and author name in `RecordList.razor` and `RecordView.razor` in `src/Gisr.Web/Records/`
- [X] T210 [US4] Switch the Officer citizen picker from local accounts to the Entra tenant directory: Graph-backed `ITenantDirectory` (delegated `User.ReadBasic.All` via on-behalf-of), `DirectoryUserDto`, `CreateRecordRequest.SubjectObjectId`, placeholder account on demand (`0006_account_placeholders.sql`, `EnsurePlaceholderAsync`), 503 when the directory is unavailable; replaces `AccountSearchTests`/`search-accounts.sql` (T090, T099) and updates T091-T092, T095, T100, T102-T104

**Checkpoint**: US1-US4 work independently.

---

## Phase 7: User Story 4A - Seal a Service Record and Unlock It With an Override Code (Priority: P4)

**Goal**: An Officer can seal a service record (code required). The subject sees a redacted entry, an access-denied view with an override form, and can view the record once per correct code entry; sealed content is hidden everywhere else.

**Independent Test**: Officer creates a sealed service record with a code; the subject sees only "Sealed record" in their list, is denied on open, gets refused with a wrong code, and sees the record with the correct code; reload or revisit asks for the code again; 5 wrong codes block; Officer changing the code or unsealing ends an open viewing (quickstart scenarios 7a-7c). Artifact behavior for sealed records is completed in US5 (T143, T150, T154, T155) and gallery behavior in US6 (T161, T166).

### Tests for User Story 4A (write first; must fail)

- [X] T107 [P] [US4A] Write migration constraint tests (`CHECK (NOT is_sealed OR author_account_id <> subject_account_id)`; `CHECK (is_sealed = (override_code_hash IS NOT NULL))`; `token_hash` UNIQUE) in `tests/Gisr.Database.Tests/Records/SealingMigrationTests.cs`
- [X] T108 [P] [US4A] Write `ViewGrantRepository` tests (create stores only the SHA-256 hash; lookup valid / expired (15 minutes) / revoked; revoke by token; delete all grants for a record; purge expired and revoked) in `tests/Gisr.Database.Tests/Records/ViewGrantRepositoryTests.cs`
- [X] T109 [P] [US4A] Write `OverrideAttemptRepository` tests (insert; count failures for `(record, account)` within 15 minutes; lockout until the oldest of the 5 failures is 15 minutes old) in `tests/Gisr.Database.Tests/Records/OverrideAttemptRepositoryTests.cs`
- [X] T110 [P] [US4A] Write `OverrideCodeHasher` tests (format `pbkdf2-sha256$iterations$salt$hash` with 600,000 iterations, 16-byte salt, 32-byte hash; verify correct/incorrect; different salt each time; constant-time compare used) in `tests/Gisr.Api.Tests/Records/OverrideCodeHasherTests.cs`
- [X] T111 [P] [US4A] Write seal validation tests ("`isSealed = true` requires the caller to hold Officer and the record to be a service record, and a non-empty `overrideCode` (8-64 chars) in the same request. When already sealed, `overrideCode` is optional (omitted = unchanged; provided = replaced). Unsealing clears the code. Override entry: non-empty, max 64 chars.") in `tests/Gisr.Api.Tests/Records/SealValidationTests.cs`
- [X] T112 [P] [US4A] Write sealed record endpoint tests in `tests/Gisr.Api.Tests/Records/SealedRecordEndpointsTests.cs`: subject list shows a redacted summary (`isLocked` true; title, affiliation, author, dates null); subject `GET` -> 403 ProblemDetails with `sealed: true`; other non-Officer -> 404; Officer sees full content; self-authored record cannot be sealed (400); sealing without a code (400); history for a locked record -> 404; locked record excluded from the subject's `artifact` and `history` access
- [X] T113 [P] [US4A] Write override endpoint tests in `tests/Gisr.Api.Tests/Records/OverrideEndpointsTests.cs`: correct code -> 200 `UnlockedRecordDto` (record + `viewToken` + `expiresAt` 15 minutes out); wrong code -> 403 with `attemptsRemaining`; 6th attempt within 15 minutes -> 429 with `Retry-After` and the code is not evaluated; `DELETE` with `X-View-Token` revokes (idempotent); changing the code or unsealing deletes all grants; non-subject -> 404; each attempt is stored without the code; `GET /api/records/{id}/override/status` with a valid `X-View-Token` -> 204, and 404 once the grant is revoked, expired, for another record, or deleted by a seal/code change
- [X] T114 [P] [US4A] Write privacy tests in `tests/Gisr.Api.Tests/Records/SealedPrivacyTests.cs`: `record_revision` snapshots contain `is_sealed` and code existence but never the hash; captured logs and error responses never contain the override code or view token; the notification message for a sealed record is the fixed text "A sealed record was added to your file"
- [X] T115 [P] [US4A] Write `ViewGrantStore` tests (entries keyed by (user, `viewingId`) holding record ID and token; two viewings of the same record are independent and disposing one leaves the other working; lookup with another user's `viewingId` fails; tokens never exposed through any DTO) in `tests/Gisr.Web.Tests/Records/ViewGrantStoreTests.cs`
- [X] T116 [P] [US4A] Write bUnit tests for the sealed `RecordView` states in `tests/Gisr.Web.Tests/Records/SealedRecordViewTests.cs`: 403 `sealed` shows "Access denied. This record is sealed." and a masked override form with an accessible label and `autocomplete="off"`; wrong code shows attempts remaining; 429 shows the retry message and disables the form; success shows the record, the "Viewing under override. Access ends when you leave this page." banner, hides History and Edit; disposing the component calls `DELETE /api/records/{id}/override` and clears the store; an artifact 404 mid-viewing returns to the access-denied state; a status poll every 20 s (fake timer) that returns 404 clears the record from memory and shows the access-denied state within 30 s
- [X] T117 [P] [US4A] Write bUnit tests for the seal controls on `RecordForm` (switch only for Officers creating for another citizen; code field required when sealing, 8-64 chars, masked; edit shows "An override code is set" with "Change code" and no remove action) and the redacted locked row on `RecordList` in `tests/Gisr.Web.Tests/Records/SealedRecordFormTests.cs`
- [X] T204 [P] [US4A] Write tests for `OverrideCodeRequestValidator` (`code` non-empty, max 64) in `tests/Gisr.Api.Tests/Records/OverrideCodeRequestValidatorTests.cs` and mapper tests for the redacted locked summary (title, affiliation, author, dates, artifact count null; `isLocked` true) and `UnlockedRecordDto` (record + token + expiry, `canEdit` false) in `tests/Gisr.Api.Tests/Records/SealedRecordMapperTests.cs`

### Implementation for User Story 4A

- [X] T118 [US4A] Add migration `src/Gisr.Database/Scripts/Migrations/0007_record_sealing.sql`: `ALTER TABLE record ADD is_sealed boolean NOT NULL DEFAULT false, ADD override_code_hash text NULL` (`pbkdf2-sha256$iterations$salt$hash`) with `CHECK (NOT is_sealed OR author_account_id <> subject_account_id)` and `CHECK (is_sealed = (override_code_hash IS NOT NULL))`; create `record_view_grant` (`id uuid PK`, `record_id FK`, `account_id FK`, `token_hash bytea NOT NULL UNIQUE`, `created_at`, `expires_at` = created_at + 15 minutes, `revoked_at timestamptz NULL`) and `record_override_attempt` (`id`, `record_id FK`, `account_id FK`, `attempted_at timestamptz NOT NULL`, `succeeded boolean NOT NULL`) with index `(record_id, account_id, attempted_at DESC)`
- [X] T119 [P] [US4A] Create rows and SQL files (`insert-view-grant.sql`, `get-valid-view-grant.sql`, `revoke-view-grant.sql`, `delete-view-grants-for-record.sql`, `purge-view-grants.sql`, `insert-override-attempt.sql`, `count-recent-failures.sql`) in `src/Gisr.Database/Records/`
- [X] T120 [US4A] Implement `ViewGrantRepository` and `OverrideAttemptRepository` in `src/Gisr.Database/Records/`
- [X] T121 [US4A] Implement `OverrideCodeHasher` (PBKDF2-SHA256 via `Rfc2898DeriveBytes.Pbkdf2`, 600,000 iterations, `CryptographicOperations.FixedTimeEquals`) in `src/Gisr.Api/Records/OverrideCodeHasher.cs`
- [X] T122 [P] [US4A] Extend DTOs: `isSealed`, `isLocked` on `RecordSummaryDto`; `isSealed` on `RecordDto`; `isSealed`, `overrideCode` on create/update requests; new `OverrideCodeRequest`, `UnlockedRecordDto` in `src/Gisr.Shared/Records/`
- [X] T123 [P] [US4A] Add seal validation rules (listed in T111) and `OverrideCodeRequestValidator` (`code` non-empty, max 64; bound to the override endpoint) to the record validators in `src/Gisr.Api/Records/`
- [X] T124 [US4A] Extend `get-record.sql`, `list-records.sql` and `RecordRepository` with the shared "locked for caller" predicate and NULL redaction of locked rows; extend `update-record.sql` for seal/code changes and delete all view grants for the record in the same transaction; snapshots exclude the hash and include `is_sealed` in `src/Gisr.Database/Records/`
- [X] T125 [US4A] Update `RecordService`/`RecordAccessPolicy`/`RecordMapper` for seal rules (Officer-only, service records only, code required), 403 `sealed` ProblemDetails for locked subjects, 404 for others, and redacted summaries in `src/Gisr.Api/Records/`
- [X] T126 [US4A] Implement `OverrideService` (verify code, lockout via `OverrideAttemptRepository`, create grant with a random 32-byte token returning only its SHA-256 hash to storage, return record + token + expiry, revoke by token, check grant status) in `src/Gisr.Api/Records/OverrideService.cs`
- [X] T127 [US4A] Add `POST /api/records/{recordId}/override` (200/400/403/404/429), `DELETE /api/records/{recordId}/override` (204, header `X-View-Token`), and `GET /api/records/{recordId}/override/status` (204 while the grant is valid, else 404) to `src/Gisr.Api/Records/RecordEndpoints.cs`; set the fixed sealed notification text in `NotificationService`
- [X] T128 [US4A] Implement the singleton `ViewGrantStore` (keyed by user and a per-viewing `viewingId` GUID, holding the record ID and view token; server-side only, never serialized to the browser) in `src/Gisr.Web/Records/ViewGrantStore.cs`
- [X] T129 [US4A] Implement the sealed states in `RecordView.razor` (access-denied alert, override form, error/429 handling, override banner, a `viewingId` generated per override viewing, a 20 s poll of `/override/status` that clears the record from memory and returns to the access-denied state on 404, and an `IAsyncDisposable` that revokes only its own grant and removes only its own store entry) in `src/Gisr.Web/Records/RecordView.razor`
- [X] T130 [US4A] Add the seal switch, required masked override code field, and "Change code" action to `RecordForm.razor`/`RecordEdit.razor`, and the redacted locked row (lock icon, "Sealed record", created date) to `RecordList.razor` in `src/Gisr.Web/Records/`

- [X] T208 [US4A] Implement `ViewGrantPurgeService` (hosted service that periodically calls `purge-view-grants.sql` for expired and revoked grants; register in `Program.cs`) in `src/Gisr.Api/Records/ViewGrantPurgeService.cs`

**Checkpoint**: US1-US4A work independently (artifacts, gallery, search not yet built).

---

## Phase 8: User Story 5 - Attach Artifacts to Records (Priority: P5)

**Goal**: Users attach, list, open/download, describe, and remove files and images on records they may edit; access follows the record, including sealed-record rules.

**Independent Test**: Attach a PNG with a description and a PDF; reject a renamed `.exe`, a 30 MB file, and an image without a description; open and remove artifacts; confirm access follows the record (quickstart scenario 8, 7a artifact part).

### Tests for User Story 5 (write first; must fail)

- [X] T131 [P] [US5] Create the Azurite Testcontainers fixture (blob container per test run) in `tests/Gisr.Api.Tests/Common/AzuriteFixture.cs` and `tests/Gisr.Database.Tests/Common/AzuriteFixture.cs`; register it in `ApiFactory`
- [X] T132 [P] [US5] Write migration constraint tests for `artifact` (size range, `is_image`/description rule, `index_status` default `pending`) in `tests/Gisr.Database.Tests/Artifacts/ArtifactMigrationTests.cs`
- [X] T133 [P] [US5] Write `ArtifactRepository` tests (insert, list active only with paging and `kind=image`, soft delete sets `removed_at` and `removed_by_account_id`, count active <= 50 per record, update description) in `tests/Gisr.Database.Tests/Artifacts/ArtifactRepositoryTests.cs`
- [X] T134 [P] [US5] Write `BlobArtifactStore` tests against Azurite (save, open stream, delete, thumbnail save) in `tests/Gisr.Database.Tests/Artifacts/BlobArtifactStoreTests.cs`
- [X] T135 [P] [US5] Write `FileTypeSniffer` tests (magic bytes for JPEG, PNG, WebP, GIF, PDF, DOCX [zip containing `word/document.xml`], plain text, markdown; rejects a renamed executable, a zip that is not DOCX, empty files, and extension/content mismatches) in `tests/Gisr.Api.Tests/Artifacts/FileTypeSnifferTests.cs`
- [X] T136 [P] [US5] Write `ThumbnailGenerator` tests (320 px longest edge JPEG for PNG/JPEG/WebP/GIF; corrupt image rejected) in `tests/Gisr.Api.Tests/Artifacts/ThumbnailGeneratorTests.cs`
- [X] T137 [P] [US5] Write artifact validator tests (file name "sanitized, max 255"; size "1 .. 26,214,400"; description "required when is_image; max 500"; "Max 50 active artifacts per record") in `tests/Gisr.Api.Tests/Artifacts/ArtifactValidatorTests.cs`
- [X] T138 [P] [US5] Write artifact endpoint tests in `tests/Gisr.Api.Tests/Artifacts/ArtifactEndpointsTests.cs`: upload 201; 400 for empty, missing image description, 51st artifact; 413 over 25 MB; 415 for disallowed or mismatched type; list/paging; content and thumbnail streams; `PATCH` description; `DELETE` 204 and records who removed it; invalid `kind`, `page` or `pageSize` on the list -> 400; non-viewer 404; viewer-not-editor 403 on upload/delete; Officer reading another citizen's artifact writes an access-log row with `kind = artifact`
- [X] T139 [P] [US5] Write sealed artifact access tests in `tests/Gisr.Api.Tests/Artifacts/ArtifactSealedAccessTests.cs`: locked subject gets 404 for list/content/thumbnail; with a valid `X-View-Token` for that record they succeed; a token for another record, an expired token, a revoked token, and a token after seal/code change all give 404; Officers need no token
- [X] T140 [P] [US5] Write bUnit tests for `ArtifactUploader` (25 MB `MaxAllowedSize`, per-file progress and errors, description required for images, drag/drop or picker) and `ArtifactList` (thumbnails with alt text from description, download link, remove with confirmation, "Not searchable" placeholder slot) in `tests/Gisr.Web.Tests/Artifacts/ArtifactUploaderTests.cs` and `tests/Gisr.Web.Tests/Artifacts/ArtifactListTests.cs`
- [X] T141 [P] [US5] Write media proxy endpoint tests (requires cookie auth; streams from the API; for sealed records requires `?v={viewingId}` and adds the `X-View-Token` stored for the signed-in user and that `viewingId`, with a missing, unknown, another user's, or other-record `v` giving 404; `X-Content-Type-Options: nosniff`; non-images `Content-Disposition: attachment`; `Cache-Control: private` and `private, no-store` for override viewings) in `tests/Gisr.Web.Tests/Artifacts/MediaEndpointsTests.cs`
- [X] T205 [P] [US5] Write `ArtifactMapper` tests (row -> `ArtifactDto` incl. `indexStatus`, `indexDetail`, uploader display name; never exposes blob names or hashes) in `tests/Gisr.Api.Tests/Artifacts/ArtifactMapperTests.cs` and `ArtifactListQueryValidator` tests (`kind` in {all, image}, `page` >= 1, `pageSize` 1-100) in `tests/Gisr.Api.Tests/Artifacts/ArtifactQueryValidatorTests.cs`

### Implementation for User Story 5

- [X] T142 [US5] Add migration `src/Gisr.Database/Scripts/Migrations/0008_artifacts.sql` creating `artifact`: `id uuid PK`; `record_id uuid NOT NULL FK record`; `file_name text NOT NULL` ("sanitized, max 255"); `content_type text NOT NULL` ("server-determined from sniffed bytes"); `size_bytes bigint NOT NULL` ("1 .. 26,214,400", add `CHECK (size_bytes BETWEEN 1 AND 26214400)`); `sha256 text NOT NULL`; `blob_name text NOT NULL`; `thumbnail_blob_name text NULL` ("images only"); `is_image boolean NOT NULL`; `description text NULL` ("required when is_image; max 500; alt text"; add `CHECK (NOT is_image OR (description IS NOT NULL AND char_length(description) BETWEEN 1 AND 500))`); `uploaded_by_account_id uuid NOT NULL FK account`; `uploaded_at timestamptz NOT NULL`; `removed_at timestamptz NULL` ("soft delete; removed artifacts leave gallery/search"); `removed_by_account_id uuid NULL FK account` (set together with `removed_at`); `index_status text NOT NULL DEFAULT 'pending'`; `index_detail text NULL` (reason codes `unsupported_type`, `no_text`, `ocr_disabled`, `ocr_budget_exhausted`, `ocr_page_limit`, `parse_error`, `too_large`; never content); index `(record_id) WHERE removed_at IS NULL`
- [X] T143 [P] [US5] Create `ArtifactRow` and SQL files (`insert-artifact.sql`, `list-artifacts.sql` including the "locked for caller" predicate and `@viewToken` hash check, `get-artifact.sql`, `update-artifact-description.sql`, `soft-delete-artifact.sql`, `count-active-artifacts.sql`) in `src/Gisr.Database/Artifacts/`
- [X] T144 [US5] Implement `ArtifactRepository` in `src/Gisr.Database/Artifacts/ArtifactRepository.cs`
- [X] T145 [US5] Implement `IArtifactStore` and `BlobArtifactStore` (private container; uses `ConnectionStrings:blobs` when present (Aspire/Azurite) and otherwise `Storage:BlobEndpoint` with `DefaultAzureCredential`) in `src/Gisr.Database/Artifacts/`
- [X] T146 [P] [US5] Create DTOs `ArtifactDto` (including `indexStatus` and `indexDetail`), `UpdateArtifactRequest` in `src/Gisr.Shared/Artifacts/`
- [X] T147 [P] [US5] Implement `FileTypeSniffer` (allowlist: JPEG, PNG, WebP, GIF, PDF, DOCX, plain text, markdown) in `src/Gisr.Api/Artifacts/FileTypeSniffer.cs`
- [X] T148 [P] [US5] Implement `ThumbnailGenerator` (SkiaSharp, 320 px JPEG) in `src/Gisr.Api/Artifacts/ThumbnailGenerator.cs`
- [X] T149 [P] [US5] Implement `ArtifactValidator`/`UpdateArtifactRequestValidator` and `ArtifactListQueryValidator` (rules in T205) in `src/Gisr.Api/Artifacts/`
- [X] T150 [US5] Implement `ArtifactService` (upload with sniffing, SHA-256, size cap 25 MB, thumbnail for images, edit permission via `RecordAccessPolicy`, locked-sealed -> 404 unless valid view token, access-log row for non-subject reads, records `removed_by_account_id` on removal, never logs file names) and `ArtifactMapper` in `src/Gisr.Api/Artifacts/`
- [X] T151 [US5] Implement `ArtifactEndpoints` (`GET/POST /api/records/{recordId}/artifacts`, `PATCH/DELETE /api/artifacts/{artifactId}`, `GET .../content`, `GET .../thumbnail`; multipart limit 25 MB; `X-View-Token` header; status codes 200/201/204/400/403/404/413/415) in `src/Gisr.Api/Artifacts/ArtifactEndpoints.cs`; register in `Program.cs`
- [X] T152 [US5] Add the artifact count to record summaries/`RecordDto` and include the "locked for caller" rule for artifact access in `src/Gisr.Database/Records/` and `src/Gisr.Api/Records/RecordMapper.cs`
- [X] T153 [US5] Implement `ArtifactUploader.razor` (`InputFile`, `MaxAllowedSize` 26,214,400, progress, errors) and `ArtifactList.razor` in `src/Gisr.Web/Artifacts/`
- [X] T154 [US5] Implement the media proxy `src/Gisr.Web/Artifacts/MediaEndpoints.cs` (`/media/artifacts/{id}` and `/thumbnail`) resolving the server-held view token from `ViewGrantStore` by (signed-in user, `v` = `viewingId` query parameter) and returning 404 for a sealed record when it is missing, unknown, another user's, or for a different record, with the security headers from the UI contract; allow `ApiClient` per-request headers
- [X] T155 [US5] Integrate artifacts into `RecordView.razor` (list; during override viewings build artifact URLs with the viewing's `?v={viewingId}` and fall back to the access-denied state on 404) and `RecordEdit.razor` (uploader) in `src/Gisr.Web/Records/`

**Checkpoint**: US1-US5 work independently.

---

## Phase 9: User Story 6 - Browse the Image Gallery Grouped by Record (Priority: P6)

**Goal**: A gallery page with one card per record that has images, a per-record photo grid, and a lightbox; sealed records never appear.

**Independent Test**: Add images to two records and one sealed record; `/gallery` shows two cards with counts; opening one shows its photos; the lightbox navigates and closes with Esc; the sealed record's images appear nowhere in the gallery, even for the Officer (quickstart scenario 9).

### Tests for User Story 6 (write first; must fail)

- [X] T156 [P] [US6] Write `GalleryRepository` tests (group per record, image count, up to 4 cover artifact ids, excludes `removed_at` artifacts, excludes sealed records for every caller including Officers, `scope=mine` vs Officer `scope=all`, paging) in `tests/Gisr.Database.Tests/Gallery/GalleryRepositoryTests.cs`
- [X] T157 [P] [US6] Write `GET /api/gallery` endpoint tests (200 paged groups, `scope=all` 403 for non-Officers, sealed records absent for subject and Officer, locked/other users' records absent; `pageSize` 0 or 51, `page` 0, and an invalid `scope` -> 400) in `tests/Gisr.Api.Tests/Gallery/GalleryEndpointsTests.cs`
- [X] T158 [P] [US6] Write bUnit tests for `GalleryIndex` (card grid, counts, empty-state message, Officer scope switch) in `tests/Gisr.Web.Tests/Gallery/GalleryIndexTests.cs`
- [X] T159 [P] [US6] Write bUnit tests for `GalleryRecord` (thumbnail grid paged; a sealed record redirects to its record page) in `tests/Gisr.Web.Tests/Gallery/GalleryRecordTests.cs`
- [X] T160 [P] [US6] Write bUnit tests for `Lightbox` (large image with description as `alt`, previous/next, Esc closes, focus trapped then restored, "Open record" link, respects reduced motion) in `tests/Gisr.Web.Tests/Gallery/LightboxTests.cs`
- [X] T206 [P] [US6] Write `GalleryMapper` tests (cover artifact ids capped at 4, image count, no blob names) in `tests/Gisr.Api.Tests/Gallery/GalleryMapperTests.cs` and `GalleryQueryValidator` tests (`scope` in {mine, all}, `page` >= 1, `pageSize` 1-50) in `tests/Gisr.Api.Tests/Gallery/GalleryQueryValidatorTests.cs`

### Implementation for User Story 6

- [X] T161 [P] [US6] Create `list-gallery-groups.sql` (excludes `is_sealed` records and removed artifacts; owner scoping in SQL) and `GalleryRepository` in `src/Gisr.Database/Gallery/`
- [X] T162 [P] [US6] Create DTO `GalleryGroupDto` in `src/Gisr.Shared/Gallery/`
- [X] T163 [US6] Implement `GalleryService`, `GalleryMapper`, and `GalleryEndpoints` (`GET /api/gallery`, `scope`, `page`, `pageSize` 1-50, validated by `GalleryQueryValidator` from T206) in `src/Gisr.Api/Gallery/`; register in `Program.cs`
- [X] T164 [US6] Implement `Lightbox.razor` using `focus-trap.js` in `src/Gisr.Web/Gallery/Lightbox.razor`
- [X] T165 [US6] Implement `GalleryIndex.razor` (`/gallery`) in `src/Gisr.Web/Gallery/GalleryIndex.razor`
- [X] T166 [US6] Implement `GalleryRecord.razor` (`/gallery/{recordId}`, image artifacts via `GET /api/records/{id}/artifacts?kind=image`, redirect to `/records/{id}` when the record is sealed) in `src/Gisr.Web/Gallery/GalleryRecord.razor`

**Checkpoint**: US1-US6 work independently.

---

## Phase 10: User Story 7 - Search for Information (Priority: P7)

**Goal**: Semantic search across record text and the text of PDF, Word (.docx), and plain-text artifacts; scanned PDFs use PdfPig first then budget-capped Document Intelligence OCR; access scoping in SQL; Officers can find sealed records.

**Independent Test**: Save records and upload text-based and scanned PDFs, a `.docx`, a `.txt`, and a PNG; after indexing, a paraphrase search finds the records; the PNG shows "Not searchable"; restricted and locked-sealed records never appear (or count) for the wrong user; an Officer finds a sealed record and opens it on the record page (quickstart scenarios 10, 10a, 10b).

### Tests for User Story 7 (write first; must fail)

- [X] T167 [P] [US7] Write migration/constraint tests (`vector(1536)` column, HNSW `vector_cosine_ops` index exists, `ocr_usage` primary key) in `tests/Gisr.Database.Tests/Search/SearchMigrationTests.cs`
- [X] T168 [P] [US7] Write `Chunker` tests (about 500 tokens per chunk, 50-token overlap, record text built from title, affiliation, engagement details, incidents, awards) in `tests/Gisr.Api.Tests/Search/ChunkerTests.cs`
- [X] T169 [P] [US7] Write extractor tests with small fixture files: plain text/markdown UTF-8 in `tests/Gisr.Api.Tests/Search/PlainTextExtractorTests.cs`; DOCX body paragraphs and tables, decompressed size cap 100 MB, no external relationships followed in `tests/Gisr.Api.Tests/Search/DocxExtractorTests.cs`; text PDF via PdfPig and "fewer than 25 non-whitespace characters per page on average" scanned detection, 500-page cap, 60 s timeout in `tests/Gisr.Api.Tests/Search/PdfExtractorTests.cs`; 200,000-character text cap for all
- [X] T170 [P] [US7] Write `OcrFallback` tests with a fake `IOcrClient` in `tests/Gisr.Api.Tests/Search/OcrFallbackTests.cs`: text PDF/DOCX/TXT never call OCR; scanned PDF calls OCR for at most the first 100 pages (`index_detail = ocr_page_limit` when truncated); disabled -> `skipped`/`ocr_disabled`; budget exhausted -> `skipped`/`ocr_budget_exhausted`; OCR failure -> retry/`failed`
- [X] T171 [P] [US7] Write `OcrUsageRepository` tests (atomic reservation `UPDATE ... SET pages_used = pages_used + @pages WHERE month = @month AND pages_used + @pages <= @budget`; month row inserted first; concurrent reservations never exceed the budget; unused reservations not refunded) in `tests/Gisr.Database.Tests/Search/OcrUsageRepositoryTests.cs`
- [X] T172 [P] [US7] Write `SearchRepository` tests with deterministic vectors in `tests/Gisr.Database.Tests/Search/SearchRepositoryTests.cs`: subject sees own non-sealed results only; another user's chunks never returned or counted; Officer sees all including sealed; a locked sealed record never matches for its subject; affiliation filter; `distance < @maxDistance`; best chunk per record/artifact; paging and total count; removed artifacts excluded
- [X] T173 [P] [US7] Write `IndexJobRepository` tests (claim with `FOR UPDATE SKIP LOCKED` by two concurrent workers gets disjoint jobs; exponential backoff via `available_at`; fails after 5 attempts; transitions `pending` -> `running` -> `succeeded`) in `tests/Gisr.Database.Tests/Search/IndexJobRepositoryTests.cs`
- [X] T174 [P] [US7] Write `SearchIndexingWorker` tests (indexes a record and an artifact end to end with fake embeddings; replaces prior chunks; sets `index_status` indexed/skipped/failed with `index_detail`; images -> `skipped`/`unsupported_type`; logs only ids) in `tests/Gisr.Api.Tests/Search/SearchIndexingWorkerTests.cs`
- [X] T175 [P] [US7] Write enqueue tests (creating/updating a record and finishing an upload enqueue a job in the same transaction; a rolled-back save leaves no job) in `tests/Gisr.Api.Tests/Search/IndexEnqueueTests.cs`
- [X] T176 [P] [US7] Write `MonthlyRequeueService` tests (artifacts skipped with `ocr_budget_exhausted` are re-queued on the first run of a new month only) in `tests/Gisr.Api.Tests/Search/MonthlyRequeueTests.cs`
- [X] T177 [P] [US7] Write search endpoint tests in `tests/Gisr.Api.Tests/Search/SearchEndpointsTests.cs`: 200 paged results with snippet/score; `q` "2-500 characters after trimming" and `pageSize` "1-50" validation (400); `scope=all` 403 for non-Officers; embedding failure -> 503 ProblemDetails; subject never gets a locked sealed record (nor counts); Officer result has `isSealed` true; search text is never logged
- [X] T178 [P] [US7] Write bUnit tests for `SearchResults` (query box, separate affiliation/scope filter row above unframed results, Officer scope dropdown defaulting to All records, switching both directions and resetting pagination while retaining affiliation, Sealed badge for Officers, results open `/records/{id}`, paging, no-results and "search temporarily unavailable" states) in `tests/Gisr.Web.Tests/Search/SearchResultsTests.cs`
- [X] T179 [P] [US7] Write bUnit tests for the artifact "Not searchable" badge with reason text for each `index_detail` in `tests/Gisr.Web.Tests/Artifacts/NotSearchableBadgeTests.cs`
- [X] T207 [P] [US7] Write `SearchResultMapper` tests (snippet truncated to at most 500 characters, score within 0..1, `isSealed` true only for Officers, artifact file name only for artifact hits) in `tests/Gisr.Api.Tests/Search/SearchResultMapperTests.cs`

### Implementation for User Story 7

- [X] T180 [US7] Add migration `src/Gisr.Database/Scripts/Migrations/0009_search.sql` creating `search_chunk` (`id uuid PK`; `record_id uuid NOT NULL FK record`; `source_type text NOT NULL` (`record` or `artifact`); `artifact_id uuid NULL FK artifact`; `chunk_index integer NOT NULL`; `content text NOT NULL`; `embedding vector(1536) NOT NULL` with HNSW index `vector_cosine_ops`; `created_at timestamptz NOT NULL`), `search_index_job` (`id`; `kind text NOT NULL` (`record` or `artifact`); `record_id uuid NOT NULL`; `artifact_id uuid NULL`; `status text NOT NULL` (`pending`, `running`, `succeeded`, `failed`); `attempts integer NOT NULL DEFAULT 0`; `available_at timestamptz NOT NULL`; `last_error text NULL` "error type only; no content"; `created_at, updated_at timestamptz NOT NULL`), and `ocr_usage` (`month date PK` first day of the UTC month; `pages_used integer NOT NULL`; plus `requeued_at timestamptz NULL` marking the month whose budget-skipped files were re-queued)
- [X] T181 [P] [US7] Create rows and SQL files (`search-chunks.sql` with access scoping `(subject_account_id = @callerId AND NOT is_sealed) OR @isOfficer`, affiliation filter, distance cut-off, best chunk per source, `SET LOCAL hnsw.iterative_scan = relaxed_order`; `replace-chunks.sql`; `claim-index-job.sql`; `complete-index-job.sql`; `enqueue-index-job.sql`; `reserve-ocr-pages.sql`; `requeue-budget-skipped.sql`) in `src/Gisr.Database/Search/`
- [X] T182 [US7] Implement `SearchRepository`, `IndexJobRepository`, and `OcrUsageRepository` in `src/Gisr.Database/Search/`
- [X] T183 [P] [US7] Create DTO `SearchResultDto` (includes `isSealed`) in `src/Gisr.Shared/Search/`
- [X] T184 [P] [US7] Implement `Chunker` in `src/Gisr.Api/Search/Chunker.cs`
- [X] T185 [P] [US7] Implement `PlainTextExtractor`, `DocxExtractor` (Open XML SDK), and `PdfExtractor` (PdfPig, scanned detection) behind `ITextExtractor` in `src/Gisr.Api/Search/Extraction/`
- [X] T186 [US7] Implement `IOcrClient`, `DocumentIntelligenceOcrClient` (`prebuilt-read`, page range 1-100, managed identity) and `OcrFallbackExtractor` (config `Search:OcrFallback`, budget reservation, reason codes) in `src/Gisr.Api/Search/Ocr/`
- [X] T187 [US7] Register the Azure OpenAI `IEmbeddingGenerator<string, Embedding<float>>` (`text-embedding-3-large` requested at 1536 dimensions, managed identity) and the extractor/OCR/search services in `src/Gisr.Api/Search/SearchServiceCollectionExtensions.cs` and `Program.cs`
- [X] T188 [US7] Implement `SearchIndexingWorker` (hosted service claiming jobs with `SKIP LOCKED`, extract -> chunk -> embed -> replace chunks, backoff up to 5 attempts, status/detail updates) in `src/Gisr.Api/Search/SearchIndexingWorker.cs`
- [X] T189 [US7] Implement `SearchService` (embed query, call `SearchRepository`, map to DTOs with `SearchResultMapper`, 503 on embedding outage) and `SearchEndpoints` (`GET /api/search`, validation `q` 2-500 chars trimmed, `pageSize` 1-50, `scope`) in `src/Gisr.Api/Search/`; register in `Program.cs`
- [X] T190 [US7] Enqueue index jobs in the same transaction from `RecordService` (create/update) and `ArtifactService` (upload) in `src/Gisr.Api/Records/RecordService.cs` and `src/Gisr.Api/Artifacts/ArtifactService.cs`
- [X] T191 [P] [US7] Implement `MonthlyRequeueService` (hosted service) in `src/Gisr.Api/Search/MonthlyRequeueService.cs`
- [X] T192 [US7] Implement `SearchResults.razor` (`/search?q=`), wiring the existing `TopBar` search box and Ctrl+K shortcut, in `src/Gisr.Web/Search/SearchResults.razor`; refine with a centered search bar, separate responsive result-count/filter row, Officer records dropdown defaulting to All records, and border-separated result rows (2026-10-28)
- [X] T193 [US7] Add the "Not searchable" badge with reason text to `ArtifactList.razor` in `src/Gisr.Web/Artifacts/ArtifactList.razor`

**Checkpoint**: All user stories are independently functional.

---

## Phase 11: Polish & Cross-Cutting Concerns

- [X] T194 [P] Add an OpenAPI conformance test that compares the generated Api OpenAPI document with `specs/001-gisr-core-records/contracts/api.openapi.yaml` (paths, operations, status codes, schemas) in `tests/Gisr.Api.Tests/Common/OpenApiConformanceTests.cs`
- [X] T195 [P] Add a cross-cutting access-matrix test that exercises every endpoint as anonymous, regular user, other user, Officer, and sealed-locked subject and asserts the data-model.md access rules in `tests/Gisr.Api.Tests/Common/AccessMatrixTests.cs`
- [X] T196 [P] Add a log-scrubbing test (captured Serilog output for override, search, upload, and record flows contains no titles, details, search text, file names, override codes, or view tokens) in `tests/Gisr.Api.Tests/Common/LogPrivacyTests.cs`
- [X] T197 [P] Add an accessibility pass: review all pages against `contracts/ui-contract.md` (landmarks, skip link, accessible names on icon-only controls, `aria-describedby` errors, focus visible, contrast AA, `prefers-reduced-motion`) and add bUnit assertions where missing in `tests/Gisr.Web.Tests/Layout/AccessibilityTests.cs`
- [X] T198 Add a performance check that seeds 10,000 records and 50,000 artifacts (with synthetic chunks) and asserts search, gallery, and record list queries complete in under 2 s in `tests/Gisr.Database.Tests/Common/ScaleSmokeTests.cs` (tagged `Scale` so it is excluded from the default run but always executed in T201)
- [X] T199 [P] Verify the migrations apply cleanly to an empty database and the checksum guard holds (already covered by T010; extended to all nine migrations) in `tests/Gisr.Database.Tests/Migrations/AllMigrationsTests.cs`
- [X] T200 [P] Update `README.md` with solution layout, local setup from `quickstart.md`, and the constitution workflow notes
- [X] T201 Run the full pre-merge gates: `dotnet build -warnaserror` (zero warnings), `dotnet test` plus the `Scale` tests (T198), and every scenario in `specs/001-gisr-core-records/quickstart.md` (1-12 including 7a-7c, 10a, 10b); record any gaps as new tasks (automated gates passed: 0 warnings, 1,134 tests including `Scale`; the interactive scenarios are T211)
- [X] T211 Walk through quickstart scenarios by hand with real Entra sign-in and Azure OpenAI as the Officer user (done: 1, 2, 3, 4, 7 and 7a Officer side, 8, 9, 10, 10a with OCR off, 10b Officer side, 11). Found and fixed: media proxy 500 outside a circuit (`EntraAccessTokenProvider`), local Azure credential selection (`AZURE_TOKEN_CREDENTIALS=AzureCliCredential` in the AppHost), expected 403/404/409/503 problems logged as 500 errors, and credential/HTTP-status detail missing from embedding failure logs
- [X] T212 Walk through the scenarios that need a second signed-in non-Officer user, as Anakin Skywalker (done: 5; 6 access log written; 7 receiving side with a content-free notification; 7a-7c subject side: locked view, wrong code with attempts remaining, unlock with image, reload and navigate-away re-lock with old image links returning 404, lockout after 5 failures, viewing cleared and old code refused after the Officer changed the code; 10b subject side: no result and an empty gallery)
- [X] T213 Lockout expiry (verified by ageing the attempts 16 minutes: the form re-enables with 4 attempts remaining) and the index-attempt decision (embedding outages now defer the job for a minute and hand the attempt back instead of failing it; tests in `SearchIndexingWorkerTests`, `EmbeddingOutageTests`, `IndexJobRepositoryTests`)
- [X] T214 Scenario 12 (Officer role removed in Entra; after a fresh sign-in the account shows "Officer credential: Not held", the My/All records switches and the citizen picker are gone, only own records are listed, and sealed records stay out of search) and scenario 10a with Document Intelligence at the AppHost-configured endpoint (an image-only PDF was recognised once, used 1 page of the budget, and was found at 99%)
- [X] T215 Constitution review follow-ups: record the anonymous `/auth/login` and static-asset endpoints, the `Auth/`, `Layout/` and `Components/` folders, and the additional packages in the plan; drop the unused `Microsoft.Identity.Web.DownstreamApi` package; add `ValidationCoverageTests` so an endpoint request type without a validator fails the build; remove the leftover Sync Impact Report comment from the constitution
- [X] T216 Move every SQL script in `Gisr.Database` into `Scripts/<Feature>/` (Accounts, Artifacts, Gallery, Migrations, Notifications, Records, Search), embed `Scripts/**/*.sql`, point `SqlScriptLoader` and `EmbeddedMigrationSource` at the `Scripts.` resource prefix, and add tests that every script is embedded under `Scripts` and that no SQL file exists outside it. Script paths named in earlier tasks now live under `src/Gisr.Database/Scripts/<Feature>/`; migration ids and checksums are unchanged; constitution 1.0.1 clarifies principles II and VI to match

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)** -> **Foundational (Phase 2)** -> user-story phases -> **Polish (Phase 11)**
- Foundational blocks everything. After it, stories should be done in priority order because later stories extend earlier code:

```text
US1 (P1)  ->  US2 (P2)  ->  US3 (P3)  ->  US4 (P4)  ->  US4A (P4)
                                                          |
                                              US5 (P5) <--+
                                                |
                       US6 (P6) <---------------+
                       US7 (P7) <--- US5 (artifacts) and US4A (sealed rules in search SQL)
```

- **US1**: needs only Foundational.
- **US2**: needs US1 (accounts). Independent otherwise.
- **US3**: extends US2 services and list/get SQL.
- **US4**: extends US2/US3 (`RecordService`, `RecordAccessPolicy`) and adds notifications.
- **US4A**: extends US2-US4 record SQL/service/UI. Its artifact and gallery behaviors are completed in US5 (T143, T150, T154, T155) and US6 (T161, T166) because those features do not exist yet.
- **US5**: needs US2 (records) and uses the US4A grant lookup for sealed records.
- **US6**: needs US5 (artifacts) and the sealed flag from US4A.
- **US7**: needs US2 (records) for record indexing; artifact indexing needs US5; sealed-record rules need US4A.

### Within each story

1. Tests first (all marked [P] can be written in parallel) and confirmed failing
2. Migration, then rows/SQL, then repositories
3. Shared DTOs and validators, then mappers/services, then endpoints
4. Web components after the API contract exists
5. Story checkpoint: run that story's tests and its quickstart scenario

### Parallel opportunities

- Phase 1: T002, T004, T005, T006 together after T001.
- Phase 2: T008-T010 together; T014-T017 together; T021-T025 together; T029, T030, T033 together.
- Each story: all test tasks marked [P] run in parallel (different files); after the migration, row/SQL tasks and DTO/validator tasks marked [P] run in parallel.
- Different developers can take the API side and the Web side of a story once DTOs (the contract) exist.
- US6 and US7 can proceed in parallel after US5.

### Parallel example: User Story 2

```text
Tests in parallel:   T052 T053 T054 T055 T056 T057 T058 T059 T060 T061 T062
Then in parallel:    T064 (SQL + rows)  T067 (DTOs)  T069 (validators)
Then sequential:     T063 -> T065/T066 -> T068 -> T070 -> T071 -> T072 -> Web T073-T078
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 Setup, Phase 2 Foundational
2. Phase 3 (US1): sign in, account created on first sign-in, account settings, dark shell
3. **Stop and validate** with quickstart scenarios 1-2, then demo

### Incremental delivery

1. Foundation + US1 -> demo (MVP)
2. + US2 -> users keep their own records (first real value)
3. + US3 and US4 -> Officer workflows and notifications (authoritative records)
4. + US4A -> sealed records and override codes (do this before releasing US4 to real users, since without it every service record is immediately readable by its subject)
5. + US5 -> artifacts; + US6 -> gallery; + US7 -> semantic search with OCR fallback
6. Polish, then the pre-merge gates in T201

### Notes

- Do not commit to `main`; all work is on branch `001-gisr-core-records` and merges through a pull request.
- Commit messages follow Conventional Commits.
- Mark tasks `[X]` as they complete; keep tests red-then-green per task.
