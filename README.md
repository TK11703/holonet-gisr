# holonet-gisr
The Galactic Identity and Service Records (GISR) system is a fictitious system that archives authoritative records for citizens, military personnel, jedi, bounty hunters, and more.

Citizens and Officers keep records with incidents, awards, revision history, file attachments, an image gallery, and semantic search. Officers can write service records about others and seal them behind an override code.

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

## Solution layout

| Project | Role | References |
|---------|------|-----------|
| `src/Gisr.Web` | Blazor Web App (Interactive Server), Bootstrap 5.3 only; reaches data only through HTTP to the Api | `Gisr.Shared` |
| `src/Gisr.Api` | ASP.NET Core minimal APIs, FluentValidation, ProblemDetails; authentication, authorization, orchestration | `Gisr.Shared`, `Gisr.Database` |
| `src/Gisr.Database` | PostgreSQL (Npgsql + Dapper), plain `.sql` files under `Scripts/<Feature>/`, forward-only numbered migrations (`Scripts/Migrations/`) with a checksum ledger | none |
| `src/Gisr.Shared` | DTOs and contracts only | none |
| `src/Gisr.AppHost` | .NET Aspire host for local runs (PostgreSQL with pgvector, Azurite, Api, Web) | all |
| `tests/*` | xUnit; real PostgreSQL (and Azurite) through Testcontainers, no mocked database | mirrors `src` |
| `specs/001-gisr-core-records` | Spec Kit artifacts: spec, plan, tasks, contracts, quickstart | |

Every project is organised by feature folder (`Records/`, `Artifacts/`, `Search/`, ...), with cross-feature code in `Common/`.

## Local setup

Prerequisites: .NET 10 SDK, Docker, a Microsoft Entra tenant. The full walk-through, including the Entra app registrations and the manual validation scenarios, is in [specs/001-gisr-core-records/quickstart.md](specs/001-gisr-core-records/quickstart.md).

```powershell
dotnet user-secrets set "Parameters:azure-ad-tenant-id" "<tenant id>" --project src/Gisr.AppHost   # plus the other Parameters:* values from the quickstart
dotnet run --project src/Gisr.AppHost --launch-profile https
```

The AppHost starts the containers and applies database migrations when the Api starts. Semantic search indexing needs an Azure OpenAI embeddings deployment (`Search:OpenAiEndpoint`, `Search:EmbeddingDeployment`); without one, search returns 503 and nothing is indexed.

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
