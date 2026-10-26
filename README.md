# holonet-gisr
The Galactic Identity and Service Records (GISR) system is a fictitious system that archives authoritative records for citizens, military personnel, jedi, bounty hunters, and more.

Citizens and Officers keep records with incidents, awards, revision history, file attachments, an image gallery, and semantic search. Officers can write service records about others and seal them behind an override code.

## Solution layout

| Project | Role | References |
|---------|------|-----------|
| `src/Gisr.Web` | Blazor Web App (Interactive Server), Bootstrap 5.3 only; reaches data only through HTTP to the Api | `Gisr.Shared` |
| `src/Gisr.Api` | ASP.NET Core minimal APIs, FluentValidation, ProblemDetails; authentication, authorization, orchestration | `Gisr.Shared`, `Gisr.Database` |
| `src/Gisr.Database` | PostgreSQL (Npgsql + Dapper), plain `.sql` files, forward-only numbered migrations with a checksum ledger | none |
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

## Workflow

Work is spec-driven with Spec Kit: `/speckit-specify`, `/speckit-plan`, `/speckit-tasks`, `/speckit-implement`. Each spec lives on its own sequentially numbered branch (for example `001-gisr-core-records`); never commit to `main`, merge by pull request. Write tests first and watch them fail. The constitution in `.specify/memory/constitution.md` is authoritative, and [.github/copilot-instructions.md](.github/copilot-instructions.md) summarises the rules. Commits use Conventional Commits.
