# Research: GISR Core Records

All Technical Context unknowns are resolved below. Format: Decision / Rationale / Alternatives.

## D-1 Authentication and the Officer credential

- **Decision**: Two Entra app registrations. `Gisr.Web` is a confidential client (OIDC auth code + PKCE, cookie session) that acquires an access token for `Gisr.Api`. `Gisr.Api` exposes the delegated scope `access_as_user` and defines the app role `Officer` (`allowedMemberTypes: ["User"]`). Officers are assigned in the Gisr.Api enterprise application. The API reads `roles` from the validated access token. The Web UI never reads roles itself; it calls `GET /api/me`, whose `isOfficer` flag is derived by the API.
- **Rationale**: Single source of truth for authorization is the API (constitution: server-side enforcement). Defining the role once avoids drift between registrations.
- **Alternatives**: Role on the Web registration (ID-token roles) - duplicates the role and the API cannot trust UI claims. Entra security groups - group-overage handling and Graph calls for no benefit.
- **Consequence**: Role changes reach the API only when the access token is refreshed (default lifetime 60 to 90 minutes) or on next sign-in. The spec states this (FR-008, SC-008); an Entra token lifetime policy can shorten it if required. Continuous Access Evaluation can be evaluated later.

## D-2 User identity and "known account holder"

- **Decision**: An `account` row is upserted on every `GET /api/me` (called when a circuit starts), keyed by the unique pair (`tid`, `oid`) from the token. Ownership and authorship always come from these claims, never from request bodies. The Officer's citizen picker does not search this table; it searches the Entra tenant directory (see below).
- **Rationale**: Satisfies FR-002. Service records may target any member of the Officer's tenant: the Api searches the directory through Microsoft Graph on behalf of the Officer (delegated `User.ReadBasic.All` via on-behalf-of) and identifies the subject by Entra object id. The tenant is always the Officer's token `tid`, never a request value.
- **Placeholder accounts**: choosing a tenant member with no `account` row creates one from the Graph id, name, and email with `last_sign_in_at` null; the first sign-in upsert adopts that row (same id, existing display name kept), so the member sees their records and notifications. Graph failures return 503 and create nothing.
- **Alternatives**: Searching only the `account` table (simpler, no extra permission) - rejected because it hides people who have not signed in yet.

## D-3 Record shape and affiliation-specific fields

- **Decision**: A record has fixed columns (`title`, `affiliation`, `engagement_start`, `engagement_end`) and an `engagement_detail` JSONB object of affiliation-specific fields. Incidents and awards are child tables. Field definitions (key, label, input type, required, options) are code-defined in `Gisr.Api/Affiliations/AffiliationCatalog` and published through `GET /api/affiliations`; the Web renders forms from that DTO and the API validates against the same catalog. Initial affiliations and fields are in [data-model.md](data-model.md).
- **Rationale**: One definition drives UI and validation, new affiliations need no schema change, and `Gisr.Shared` stays contracts-only. Changing a record's affiliation keeps old values: the JSONB keeps unknown keys and every revision snapshot preserves prior values (FR-013).
- **Alternatives**: Table-per-affiliation (schema churn, many repositories). DB-stored field definitions (more tables and admin UX with no current need). EAV (poor queries and validation).

## D-4 Incidents and awards editing model

- **Decision**: Incidents and awards are embedded collections on the record DTO. `PUT /api/records/{id}` replaces both collections in one transaction, bumps `version`, and writes one revision.
- **Rationale**: The edit page saves as one unit, giving one attributed history entry and one concurrency check (FR-014, FR-015).
- **Alternatives**: Sub-resource CRUD endpoints (more endpoints and per-item concurrency/versioning with no UX gain).

## D-5 Concurrency and history

- **Decision**: Integer `version` on `record`; `UPDATE ... WHERE id=@id AND version=@expectedVersion` and a zero-row result maps to HTTP 409 `ProblemDetails`. Each successful create/update inserts a full JSON snapshot into `record_revision` in the same transaction.
- **Rationale**: Simple, portable optimistic concurrency; snapshots make history and affiliation-change recovery trivial.
- **Alternatives**: `xmin` (opaque, not stable across dump/restore); event sourcing (disproportionate).

## D-6 Authorization rules

- **Decision**: Single rule set enforced in services and SQL:
  - View record: caller is the subject, or has Officer. A sealed record that is *locked* for the subject returns 403 (see D-20).
  - Create record: for self always; for another citizen only Officer (subject must exist in `account`).
  - Edit record / manage artifacts: Officer, or caller is both subject and author (a citizen cannot edit a service record authored by someone else about them).
  - List/gallery/search: `scope=mine` (default) restricts SQL to `subject_account_id = @callerId`; `scope=all` is rejected with 403 for non-Officers.
  - A "not permitted" record returns 404 for non-Officers who are not the subject, so existence is not disclosed (spec edge case).
- **Rationale**: Matches the spec and constitution (owner scoping in SQL; no trust in client-supplied identity).

## D-7 Artifact storage

- **Decision**: Azure Blob Storage (private container, managed-identity access via `Azure.Identity`; Azurite locally and in tests). The DB stores metadata and blob names. `IArtifactStore` lives in `Gisr.Database/Artifacts`.
- **Rationale**: Keeps the relational store small, supports direct streaming, and fits 50k artifacts at 25 MB max each.
- **Alternatives**: PostgreSQL `bytea`/large objects (simplest transactionally, but backup/restore and replica size cost); filesystem (breaks scale-out).
- **Upload validation**: Allowlist by extension *and* magic-byte sniffing (JPEG, PNG, WebP, GIF, PDF, DOCX, TXT/MD); reject empty files, 25 MB overflow, and mismatches. Malware scanning is an infrastructure control (Defender for Storage) outside this code.
- **Image accessibility**: Every image artifact requires a description (used as alt text) - satisfies FR-030.
- **Thumbnails**: 320 px JPEG generated at upload with SkiaSharp and stored next to the original; gallery pages use only thumbnails.

## D-8 Delivering artifacts to the browser

- **Decision**: `Gisr.Web` exposes cookie-authenticated GET endpoints `/media/artifacts/{id}` and `/media/artifacts/{id}/thumbnail` that call the API with the user's token and stream the response with `Cache-Control: private`. Content is served with `X-Content-Type-Options: nosniff`; non-image types use `Content-Disposition: attachment`.
- **Rationale**: `<img src>` cannot attach bearer tokens, and the Web may not reach storage or the DB directly (Principle III). Authorization still happens in the API. For sealed-record override viewings the proxy adds the server-held `X-View-Token` (D-20).
- **Alternatives**: SAS URLs (bypass API authorization/audit logging and leak a bearer-style link).

## D-9 Semantic search architecture

- **Decision**:
  - Embeddings from Azure OpenAI `text-embedding-3-large` requested at 1536 dimensions (its native 3072 exceeds the pgvector HNSW limit of 2000) behind `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>`.
  - Storage in `search_chunk.embedding vector(1536)` with an HNSW cosine index (pgvector). `hnsw.iterative_scan = relaxed_order` is set for filtered queries.
  - Chunks: one set for record text (title, affiliation, engagement details, incidents, awards, about 500 tokens each, 50-token overlap) and one set per indexed artifact from extracted text. Only PDF, DOCX, and plain-text/markdown artifacts are indexed; images are marked `skipped` (`unsupported_type`, not searchable).
  - Extraction (see D-21): plain text/markdown read directly (UTF-8, size-capped); DOCX through the Open XML SDK (local); PDFs through PdfPig first, then Azure AI Document Intelligence `prebuilt-read` only when PdfPig finds no usable text (scanned PDFs) and the OCR budget allows. Extracted text is capped at 200,000 characters per artifact.
  - Query: embed the query, then one SQL statement joins `search_chunk` to `record`, applies `(subject_account_id = @callerId AND NOT is_sealed) OR @isOfficer`, optional affiliation filter, `distance < @maxDistance`, groups to best chunk per record/artifact, orders by distance, pages. A locked sealed record is therefore never matched for its subject; Officers (confirmed by the product owner) match sealed records and open them from the record page, where the Officer's own access allows the artifacts.
  - Hits show record title, affiliation, source (record or artifact name), and the best-matching snippet.
- **Rationale**: Meaning-based matching (FR-027) with authorization applied before ranking, so restricted data is never returned or counted (FR-028).
- **Alternatives**: Hybrid keyword+vector (better exact-token recall; deferred, can be added with a `tsvector` column). Azure AI Search (second data store to keep in sync and secure). Client-side embedding (exposes keys).
- **Operational note**: pgvector must be allow-listed on Azure Database for PostgreSQL Flexible Server before migration 0001 runs.

## D-10 Asynchronous indexing

- **Decision**: A `search_index_job` table acts as a durable queue. Saving a record or finishing an upload enqueues a job in the same transaction. `SearchIndexingWorker` (hosted service in `Gisr.Api`) claims jobs with `FOR UPDATE SKIP LOCKED`, extracts text, embeds, and replaces the item's chunks. Retries use exponential backoff up to 5 attempts, after which the job is `failed` and the artifact shows "not searchable". Target: indexed within 2 minutes (FR-027).
- **Rationale**: Embedding and PDF parsing are slow and fallible; saves must not depend on them. Transactional enqueue prevents lost updates.
- **Alternatives**: Inline indexing (slow, failure-prone saves); Service Bus/Functions (extra infrastructure for current scale).

## D-11 UI shell, theme, and navigation

- **Decision**: Bootstrap 5.3.x with `<html data-bs-theme="dark">` and no toggle. A small `theme.css` overrides Bootstrap CSS variables (deep teal-green surfaces and teal accent matching the reference screenshot); layout only needs utilities plus a few shell rules (fixed 3rem top bar, full-height sidebar below it, content scroll region). Responsive: below `lg` the sidebar becomes a Blazor-state-driven off-canvas panel. Icons are inline SVG in an `Icon` component.
- **Rationale**: Constitution prefers Bootstrap utilities and allows no other CSS framework; inline SVG avoids a new asset dependency.
- **Interpretation**: The "100% high" left menu fills the viewport height beneath the 100%-wide top bar, as in the reference image.
- **Alternatives**: Bootstrap Icons package (extra dependency); theme toggle (explicitly out of scope per FR-033).

## D-12 Interactivity and JavaScript

- **Decision**: No jQuery. Bootstrap JS is not loaded; dropdowns, off-canvas, and the lightbox are Blazor-state-driven using Bootstrap classes, with two small ES modules via module interop: `focus-trap.js` (modal/off-canvas focus management, Escape handling) and `shortcuts.js` (Ctrl+K focuses search). Modules and handlers are disposed with the component.
- **Rationale**: Avoids Bootstrap JS mutating Blazor-owned DOM; keeps keyboard/focus behavior accessible.

## D-13 Gallery behavior

- **Decision**: `/gallery` lists one card per record that has images (title, affiliation, count, up to four thumbnails). `/gallery/{recordId}` shows that record's photo grid; selecting a photo opens a lightbox (Blazor modal) with previous/next, description, and a link back to the record. Gallery endpoints filter on the same access rules and always exclude sealed records (D-20); Officers can switch `scope` to all.
- **Rationale**: Directly matches "grouped by record, then clicking a gallery permits viewing the photos".

## D-14 Notifications

- **Decision**: The `notification` table holds in-app notifications; a service record created for another citizen inserts one for the subject in the creating transaction (FR-032). For a sealed record the message is the fixed text "A sealed record was added to your file" (no title or affiliation). The top-bar bell polls `GET /api/notifications` every 60 s while the circuit is active, shows an unread badge, and offers mark-read / mark-all-read.
- **Alternatives**: SignalR/server push (extra moving parts for a low-frequency signal); email (not requested).

## D-15 Account settings

- **Decision**: Editable: display name, contact email (optional), and "notify me of new service records". Sign-in identity fields (object id, tenant, sign-in email) are read-only. No theme setting (dark only).

## D-16 Time and dates

- **Decision**: System instants use `DateTimeOffset`/`timestamptz` in UTC. Engagement dates, incident dates, and award dates are `DateOnly`/`date`, since users enter calendar dates without a time or zone.

## D-17 Testing approach

- **Decision**: Red-green per task. `Gisr.Database.Tests`: migrations on an empty database, checksum-tamper failure, repository SQL against real PostgreSQL+pgvector, access scoping. `Gisr.Api.Tests`: `WebApplicationFactory` with a test auth scheme injecting `oid`/`roles`; fake `IEmbeddingGenerator` (deterministic vectors); real PDF/DOCX/text extraction on small fixture files; fake OCR client (`IOcrClient`); Azurite for blobs; every endpoint covers success, validation failure, not-found/forbidden. `Gisr.Web.Tests`: bUnit for TopBar, SideNav, PageHeader, record forms, uploader, lightbox, notifications.
- **Rationale**: The embedding service costs money and is non-deterministic, so it is faked at the interface; the database, blob store, and PDF extractor are real.

## D-18 Observability and privacy

- **Decision**: Serilog structured logging and OpenTelemetry traces/metrics (ASP.NET Core, HttpClient, Npgsql, runtime) in Web and Api; exporter configured by environment. Log only identifiers and counts; never record content, search text, or file names. Embedding/extraction calls carry only the minimum text required.
- **Decision (confirmed by the product owner)**: Sending record text to Azure OpenAI for embedding is acceptable. Deployments should still use the organization's own tenant and region. Text from scanned PDFs goes to Azure Document Intelligence only as a last resort (D-21); all other extraction happens inside the Api process.

## D-19 Migrations and SQL loading

- **Decision**: Numbered scripts (`0001_create_extensions.sql`, `0002_accounts.sql`, ...) embedded as resources in `Gisr.Database/Migrations`; `MigrationRunner` applies pending scripts in order inside transactions, stores `(script_id, checksum_sha256, applied_at)` in `schema_migration`, and fails startup if an applied checksum differs. The whole run holds a PostgreSQL advisory lock so concurrently starting instances apply each script exactly once. Feature SQL is embedded under each feature folder and loaded once through `SqlScriptLoader` into a cache.
- **Rationale**: Directly implements constitution VI and VII.

## D-20 Sealed records and override codes

- **Decision**:
  - `record.is_sealed` plus `override_code_hash`; the two are required together (DB `CHECK`). Only Officers set them, only on service records, and the Officer must supply the code when sealing. The code is 8-64 chars, shared with the citizen out of band, and never shown again.
  - Code storage: PBKDF2-SHA256 (`Rfc2898DeriveBytes.Pbkdf2`, 600,000 iterations, 16-byte random salt, 32-byte hash) encoded in one text column; verification uses `CryptographicOperations.FixedTimeEquals`. No new package is needed. The code is never logged, put in a snapshot, DTO response, or exception message.
  - A subject on a locked sealed record gets `403` ProblemDetails with extension member `sealed: true`; the UI always offers the override form. Non-subject, non-Officer callers still get 404, so existence stays hidden from them. Artifact list/content/thumbnail and history endpoints return 404 for locked records unless a valid view token is presented (history never accepts one).
  - **Single-view unlock**: `POST /api/records/{id}/override` verifies the code and, on success, creates a `record_view_grant` (random 32-byte token, only its SHA-256 stored, 15-minute backstop expiry) and returns `200 { record, viewToken, expiresAt }`. The record content travels only in that response. Failure returns 403 with `attemptsRemaining`; every attempt writes `record_override_attempt` (no code stored). After 5 failures in 15 minutes for the same (record, account), the endpoint returns 429 with `Retry-After` and does not evaluate the code.
  - **Where the token lives**: only on the Web server. The record page component keeps the record and token in circuit memory and registers the token in a singleton `ViewGrantStore` keyed by (user, `viewingId`), where `viewingId` is a random GUID generated per override viewing (not a secret; useless without the same signed-in user), and the entry holds the record ID and token. Artifact URLs on the record page carry `?v={viewingId}`, so the media proxy can look up the token for (signed-in user, `v`) and send it to the API as `X-View-Token`; a missing, unknown, or another user's `v`, or an artifact of a different record, gives 404. Two tabs on the same record are independent viewings. The browser never sees the token. When the component is disposed (navigation, reload, circuit end) it calls `DELETE /api/records/{id}/override` with its own token (revoking only that grant) and removes only its own entry. Reloading or revisiting therefore always asks for the code again.
  - Any change to `is_sealed` or the code deletes all view grants for the record in the same transaction, which ends a viewing in progress: the record page polls `GET /api/records/{id}/override/status` (header `X-View-Token`, 204 while the grant is valid, else 404) every 20 seconds, and on 404 (or on any artifact 404) clears the record from memory and returns to the locked state, so a viewing ends within 30 seconds.
  - **Gallery**: gallery queries exclude every sealed record for all callers, including Officers; sealed images appear only on the record page.
  - One shared SQL predicate ("locked for caller") is included in the record read, list (redaction), artifact, and search queries so no path can bypass it. The list query returns `title`/`affiliation`/author as NULL for locked rows and the API maps them to a redacted summary DTO.
- **Rationale**: Keeps withheld information out of every channel (list, gallery, search, artifacts, notifications) while still letting the subject know a record exists (spec FR-036 to FR-043). Server-side lockout and constant-time comparison protect a low-entropy shared secret from guessing.
- **Alternatives**: Storing the code in plain text or reversibly (rejected: recoverable secret). A persistent unlock row per (record, user) with a long expiry (rejected: the product owner wants each visit to require the code). Unlock by session cookie flag (rejected: not auditable, not revocable when the seal changes). Putting the token in a browser-visible URL or cookie (rejected: leakable). Showing the title in the redacted list entry (rejected: the title can itself be sensitive). Letting the system generate the code (possible later; Officer-chosen codes match the stated workflow).
- **Risk noted**: The override code is a shared secret entered by the subject, so its strength depends on Officer practice; the 8-character minimum, lockout, and audit trail mitigate but do not remove this. A viewer can still save or screenshot content shown during a viewing; that cannot be prevented.

## D-21 Indexing cost and text extraction

- **Decision (updated by the product owner)**: A tiered extractor chooses the cheapest method that works. Indexed types are PDF, DOCX, TXT, and MD; images are not indexed.
  1. TXT/MD: read directly as UTF-8.
  2. DOCX: Open XML SDK (`DocumentFormat.OpenXml`, MIT, in-process), body paragraphs and tables only; no external relationships are followed, decompressed size is capped at 100 MB.
  3. PDF: **PdfPig** (`UglyToad.PdfPig`, Apache-2.0, in-process) first. If it extracts fewer than 25 non-whitespace characters per page on average (treated as a scanned PDF), fall back to **Azure AI Document Intelligence `prebuilt-read`** (OCR), reading at most the first 100 pages (`index_detail = ocr_page_limit` when truncated).
- **Cost basis** (Azure public pricing pages, checked 2026-10-23): Document Intelligence Read is $1.50 per 1,000 pages (first 1M pages), with a 500-page monthly free tier (F0). If every PDF went through it, 50,000 PDFs averaging 10 pages (about 500,000 pages) would cost roughly $750 plus a charge for every new document. With the tiered approach only scanned PDFs pay. `text-embedding-3-large` costs about $0.13 per 1M tokens (about $65 for 500M tokens), so embedding is negligible.
- **Cost guard**: OCR is gated by `Search:OcrFallback:Enabled` (default on) and a monthly page budget `Search:OcrFallback:MonthlyPageBudget` (default 2,000 pages, about $3 per month at $1.50 per 1,000 pages) tracked in `ocr_usage` with atomic page reservation before each call. When the budget is exhausted or OCR is disabled, the artifact is `skipped` with `ocr_budget_exhausted` / `ocr_disabled`, shows "Not searchable" with the reason, and budget-skipped artifacts are re-queued at the start of the next month. The worst-case OCR spend is therefore bounded and configurable.
- **Rationale**: Keeps running cost as low as possible while still covering scanned documents: PdfPig and the Open XML SDK are free and local, and the paid OCR path runs only for scanned PDFs, within a hard budget. Document Intelligence is called with the Api's managed identity (no keys) and is behind `IOcrClient`, so tests and local development fake it.
- **Limits and safeguards**: The scanned-PDF decision is per document (a PDF mixing text pages and scanned pages is treated as text-based and its scanned pages are missed). Untrusted files are parsed in the background worker only, with a 500-page cap for PdfPig, a 200,000-character text cap, and a per-file timeout (60 s); parse failures mark the artifact `failed` (`parse_error`) without affecting uploads. Document Intelligence is never called for images, DOCX, or text-based PDFs.
- **Alternatives**: Document Intelligence for everything (accurate, but about $1.50 per 1,000 pages on every document). PdfPig only (free, but scanned PDFs unsearchable; the original proposal). Local OCR such as Tesseract (heavy native dependency, slow, uneven accuracy; deferred). Document Intelligence for DOCX (unnecessary cost; the Open XML SDK reads DOCX for free). Indexing image descriptions (nearly free; can be added later if wanted).

## D-22 Local orchestration with .NET Aspire

- **Decision**: Add a `Gisr.AppHost` project (`Aspire.AppHost.Sdk` 13.6.x) that runs PostgreSQL with the `pgvector/pgvector:pg17` image, the Azurite blob emulator, `Gisr.Api`, and `Gisr.Web`. It injects `ConnectionStrings__Gisr`, `ConnectionStrings__blobs`, `Api__BaseUrl`, and the Entra `AzureAd__*` settings (from AppHost parameters held in user-secrets) so no per-project connection strings are needed locally. It replaces a compose file. The Api and Web read the same configuration keys with or without the AppHost; the Api's `BlobArtifactStore` uses `ConnectionStrings:blobs` when present (Azurite) and `Storage:BlobEndpoint` + `DefaultAzureCredential` otherwise. The existing `OTEL_EXPORTER_OTLP_ENDPOINT` handling means the Aspire dashboard receives traces and metrics with no extra service-defaults project.
- **Rationale**: One command starts the full local stack with fixed ports (Api 7100, Web 7200 from launch settings, matching the Entra redirect URI) and a dashboard for logs and traces. Application projects gain no Aspire dependency.
- **Constitution fit**: `Gisr.AppHost` is an additional project outside the four application projects; principle III's dependency rules are untouched. Aspire packages are justified in plan.md.
- **Alternatives**: Docker Compose (no project launch or config injection; removed). A `ServiceDefaults` project (extra shared project for features already covered by `Common/ObservabilitySetup.cs`; can be added later). Aspire-based integration tests (`Aspire.Hosting.Testing`) instead of Testcontainers (kept Testcontainers; constitution VIII requires real containers either way and the current tests are already fast).
- **Not covered**: Azure OpenAI and Document Intelligence are not emulated, so semantic search indexing needs real Azure settings or is skipped locally.
