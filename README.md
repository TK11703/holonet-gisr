# holonet-gisr
The Galactic Identity and Service Records (GISR) system is a fictitious system that archives authoritative records for citizens, military personnel, jedi, bounty hunters, and more.

Citizens and Officers keep records with incidents, awards, revision history, file attachments, an image gallery, and semantic search. Officers can write service records about others and seal them behind an override code.

## Features

| Area | What it does |
|------|--------------|
| Records | Citizens create, edit, and delete records about themselves; each is filed under one of their affiliations, with a summary, an optional engagement period, incidents, awards, artifacts, and a revision history. Concurrent edits are detected rather than overwritten. Only Officers add or remove incidents. |
| Service and sealed records | Officers write records about any tenant member (found through a Microsoft Graph directory search) and may seal them behind an 8-64 character override code. A sealed record is listed for its subject without details, unlocks for a single viewing, and is excluded from the gallery and from the subject's search. Failed codes are logged and lock out after 5 attempts in 15 minutes. |
| Artifacts and gallery | JPEG, PNG, WebP, GIF, PDF, DOCX, TXT, and Markdown files up to 25 MB each and 50 per record, verified against their real contents. Images need a description for alt text and are grouped by record in the gallery. |
| Search | Semantic search over record content and the text of PDF, DOCX, TXT, and Markdown files, with OCR for scanned PDFs up to a monthly page budget. Filter by affiliation; Officers can search all records or only their own. |
| Account | Preferences (including a site theme (Green / Teal, GISR Hub, Dark Navy, or Dark Grey), applied as soon as you sign in), a read-only Operating Number synced from the Entra `EmployeeId`, a primary and secondary affiliation list, and an optional fictional Chain Code profile. Operating Number and Chain Code are never authorization credentials. |
| Notifications | A bell in the top bar reports service records written about you. Notifications about sealed records reveal nothing about their content. |
| Help | An About page and a searchable FAQ, described below. |

## Home and identity

The signed-in home page at `/` introduces GISR as **HoloNet / Databank / GISR** and
provides shortcuts to records, record creation, search, and the image gallery.
It is an archival landing page, not a live dashboard: it makes no record requests
and displays no invented statistics or activity. Parent-system labels describe
GISR's context rather than linking to services that are not yet integrated.

The original [GISR seal](src/Gisr.Web/wwwroot/gisr-mark.svg) combines an identity
dossier with orbital rings. The same scalable asset appears on home, in the site
brand, and as the favicon. The site brand, sidebar Home entry, and Home breadcrumbs
all return to `/`. Feature requirements and validation are in
[the home and identity spec](specs/003-home-branding/spec.md).

## Account and affiliations

Account settings holds Preferences (display name, notification account, notification switch, and site theme), read-only Account Information (user name, Operating Number, and Officer credential), the optional Chain Code profile, and the affiliation cards.

- **Operating Number** is read from the signed-in user's Entra `EmployeeId` on each sign-in. A failed directory lookup keeps the last known value, and it cannot be edited in GISR.
- **Chain Code** is an optional fictional profile (identity, biometric traits, histories, and biography). Saving it derives a deterministic, made-up code from the normalized details, so unchanged details always produce the same code. See [the Chain Code spec](specs/004-account-chain-code/spec.md).
- **Affiliations** come from a fixed catalog of factions and organizations plus a free-text special unit. A record's affiliation is the display label of one of its subject's affiliations (for example `Primary › Secondary › Unit`), so a person must add an affiliation in account settings before a record can be filed for them.

## Help: About and FAQ

The **Help** group in the side navigation holds two pages, both open to every signed-in user and written as static content in `src/Gisr.Web/Help/`.

- `/about` explains what a record holds, what citizens and Officers can each do, how records are protected, and where to find each feature.
- `/faq` is a searchable set of cards in six topics (getting started, records, service and sealed records, files and gallery, search, and account and access). Questions expand and collapse with `aria-expanded`, typed terms must all appear in a question or its answer, and popular-search chips fill the search box. Add or change an answer in `FaqContent.cs`; the tests in `tests/Gisr.Web.Tests/Help/` check that topics and questions stay unique, links stay rooted, and every popular search still matches something.

Keep FAQ answers in step with shipped behavior: when a limit, role rule, or screen changes, update the matching answer in the same pull request.

## Solution layout

| Project | Role | References |
|---------|------|-----------|
| `src/Gisr.Web` | Blazor Web App (Interactive Server), Bootstrap 5.3 only; reaches data only through HTTP to the Api | `Gisr.Shared` |
| `src/Gisr.Api` | ASP.NET Core minimal APIs, FluentValidation, ProblemDetails; authentication, authorization, orchestration | `Gisr.Shared`, `Gisr.Database` |
| `src/Gisr.Database` | PostgreSQL (Npgsql + Dapper), plain `.sql` files under `Scripts/<Feature>/`, forward-only numbered migrations (`Scripts/Migrations/`) with a checksum ledger | none |
| `src/Gisr.Shared` | DTOs and contracts only | none |
| `src/Gisr.AppHost` | .NET Aspire host for local runs (PostgreSQL with pgvector, Azurite, Api, Web) | all |
| `tests/*` | xUnit; real PostgreSQL (and Azurite) through Testcontainers, no mocked database | mirrors `src` |
| `specs/` | Spec Kit artifacts per feature: `001-gisr-core-records`, `002-ci-workflow`, `003-home-branding`, `004-account-chain-code` | |

Every project is organised by feature folder (`Records/`, `Artifacts/`, `Search/`, ...), with cross-feature code in `Common/`.

## Local setup

Prerequisites: .NET 10 SDK, Docker, the Azure CLI signed in with `az login` (the AppHost has the Api use `AzureCliCredential` for Azure OpenAI and OCR), a Microsoft Entra tenant, and optionally the Aspire CLI for `aspire run`. The full walk-through, including the Entra app registrations and the manual validation scenarios, is in [specs/001-gisr-core-records/quickstart.md](specs/001-gisr-core-records/quickstart.md).

```powershell
dotnet user-secrets set "Parameters:azure-ad-tenant-id" "<tenant id>" --project src/Gisr.AppHost   # plus the other Parameters:* values from the quickstart
dotnet run --project src/Gisr.AppHost --launch-profile https
```

The AppHost starts the containers and applies database migrations when the Api starts. Semantic search indexing needs an Azure OpenAI embeddings deployment (`Search:OpenAiEndpoint`, `Search:EmbeddingDeployment`); without one, search returns 503 and nothing is indexed. The citizen picker for service records needs the optional Api client secret and Microsoft Graph `User.ReadBasic.All` consent from quickstart step 1.4; without them the picker reports itself unavailable and everything else works. Scanned-PDF OCR uses the Document Intelligence endpoint in `Search:OcrFallback`.

## Build and test

```powershell
dotnet build Gisr.slnx --no-incremental      # must finish with zero warnings
dotnet test Gisr.slnx                        # needs Docker
$env:GISR_RUN_SCALE_TESTS = "1"; dotnet test tests/Gisr.Database.Tests --filter "Category=Scale"   # 10k records / 50k artifacts, about a minute
```

Stop the AppHost before building; it locks the output assemblies.

## Continuous integration

Two GitHub Actions workflows live in `.github/workflows/`. They need no secrets, use a read-only token, and pin every action to a commit SHA.

| Workflow | Runs | What it does |
|----------|------|--------------|
| `ci.yml` ("CI", job `build-and-test`) | Every pull request to `main`, every push to `main`, and manually | Restores, builds with `-warnaserror`, and runs every test (PostgreSQL and Azurite start as containers). The run summary lists failed tests, the detailed results are kept as the `test-results` artifact for 14 days, and a newer pull request run cancels an older one. The scale test is reported as skipped. |
| `scale.yml` ("Scale check", job `scale`) | Mondays at 06:00 UTC and manually | Runs only the scale test, as in the command above. |

The local equivalent of the standard check is `dotnet build Gisr.slnx -warnaserror` followed by `dotnet test Gisr.slnx`. Start the scale check with `gh workflow run scale.yml`, and re-run a failed run with `gh run rerun <run-id> --failed`.

To make the standard check mandatory, a repository administrator adds a rule for `main` (Settings, Rules, Rulesets) that requires a pull request and the status check `build-and-test`. The workflows cannot do this themselves.

Update the pinned actions deliberately: look up the new release's commit SHA with `gh api repos/<owner>/<action>/commits/<tag> --jq .sha` and change the SHA and the version comment together.

## Workflow

Work is spec-driven with Spec Kit: `/speckit-specify`, `/speckit-plan`, `/speckit-tasks`, `/speckit-implement`. Each spec lives on its own sequentially numbered branch (for example `001-gisr-core-records`); never commit to `main`, merge by pull request. Write tests first and watch them fail. The constitution in `.specify/memory/constitution.md` is authoritative, and [.github/copilot-instructions.md](.github/copilot-instructions.md) summarises the rules. Commits use Conventional Commits.
