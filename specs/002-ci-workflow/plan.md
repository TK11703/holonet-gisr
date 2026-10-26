# Implementation Plan: Continuous Integration Checks

**Branch**: `002-ci-workflow` | **Date**: 2026-10-26 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-ci-workflow/spec.md`

## Summary

Automate the constitution's merge gates. A standard workflow builds the solution with warnings as errors and runs every test (real PostgreSQL and Azurite through Testcontainers) on each pull request and each push to `main`, cancelling superseded pull request runs, summarising failed tests, and keeping the detailed results for 14 days. A second workflow runs the Scale-tagged tests on demand and weekly. Only GitHub-owned actions are used, pinned to commit SHAs, with read-only permissions and no secrets. Branch protection stays an administrator setting and is documented.

## Technical Context

**Language/Version**: GitHub Actions workflow YAML; the workflow builds .NET 10 / C# 14 (`10.0.x` SDK)

**Primary Dependencies**: `actions/checkout`, `actions/setup-dotnet` (with its NuGet cache), `actions/upload-artifact`, all GitHub-owned and pinned to SHAs; the `dotnet` CLI; PowerShell (preinstalled on the runner) for the test summary

**Storage**: N/A (test results are workflow artifacts kept 14 days)

**Testing**: The solution's own xUnit suites. The workflow itself is verified by a throwaway pull request with a deliberate warning and a deliberate failing test (SC-001), and by workflow linting

**Target Platform**: GitHub-hosted `ubuntu-24.04` runners, which have a Docker engine for Testcontainers

**Project Type**: CI configuration for an existing multi-project .NET solution

**Performance Goals**: Standard run under 20 minutes cold and 15 minutes with the NuGet cache; status visible within 2 minutes of the run starting

**Constraints**: No secrets; `permissions: contents: read` (plus nothing else needed because results are published as a summary and artifacts); job `timeout-minutes` set; third-party actions pinned by SHA

**Scale/Scope**: Three test projects (about 1,150 tests), two workflow files, one documentation section

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How the plan complies |
|-----------|--------|-----------------------|
| I-VII (code principles) | N/A | No application code changes; no new C# or SQL. |
| VIII. Test-first, real dependencies | PASS | The suites keep running against real PostgreSQL and Azurite (no mocks introduced). The workflow is verified by a pull request that is deliberately red before it is green. |
| Tech: secrets not committed | PASS | The workflow needs and uses no secrets; fork pull requests run the same checks. |
| Tech: dependencies justified | PASS | Only GitHub-owned actions, pinned to commit SHAs; justified below. |
| Workflow: spec-driven, branch per spec | PASS | Spec 002 on branch `002-ci-workflow`, merged by pull request. |
| Workflow: merge gates | PASS | This feature automates them: zero warnings, all tests, migrations on an empty database (covered by `AllMigrationsTests`), plus the reviewer check that stays human. |

No violations; Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/002-ci-workflow/
├── plan.md
├── research.md
├── quickstart.md
├── checklists/requirements.md
└── tasks.md             # created by /speckit-tasks
```

No `data-model.md` or `contracts/`: the feature has no data and no external interface beyond the workflow files.

### Source Code (repository root)

```text
.github/
└── workflows/
    ├── ci.yml           # standard checks: pull requests to main, pushes to main, manual re-run
    └── scale.yml        # scale tests: weekly schedule and manual start
README.md                # "Continuous integration" section: what runs, local equivalent, scale check, required-status setting
```

**Structure Decision**: Two workflows rather than one with conditional jobs, so the standard check has a single stable status name that branch protection can require, and the slow check can never delay or fail a pull request.

## Dependency Justification

| Dependency | Why it is needed | Simpler alternative rejected because |
|------------|------------------|--------------------------------------|
| `actions/checkout` | Gets the source | n/a, first-party |
| `actions/setup-dotnet` | Installs the .NET 10 SDK and caches the NuGet packages (FR-010) | Installing the SDK by script and writing cache steps by hand duplicates it |
| `actions/upload-artifact` | Keeps the detailed test results for 14 days (FR-009) | No built-in alternative |
| PowerShell step (no action) | Turns the test result files into the run summary (FR-009, SC-004) | A third-party test-reporter action would add a non-GitHub dependency and needs extra token permissions |

All actions are pinned to the commit SHA of the current release (FR-013) with the version in a trailing comment.

## Phase 0 and Phase 1 Outputs

- [research.md](research.md) records the decisions.
- [quickstart.md](quickstart.md) is the validation guide, including the deliberate-failure checks.

## Post-Design Constitution Re-check

All gates remain PASS. Pinning actions to SHAs and denying write permissions keeps the supply-chain surface of the checks small.
