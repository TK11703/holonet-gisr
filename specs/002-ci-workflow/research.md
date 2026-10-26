# Research: Continuous Integration Checks

## D-1: One standard workflow, one separate scale workflow

- **Decision**: `ci.yml` runs on `pull_request` (base `main`), `push` (`main`) and `workflow_dispatch`; `scale.yml` runs on a weekly `schedule` and `workflow_dispatch`.
- **Rationale**: The standard check needs a single stable job name that branch protection can require (FR-005). The scale test must never be able to delay or fail a pull request (FR-008).
- **Alternatives considered**: One workflow with a conditional scale job (a skipped job complicates required-status naming); running the scale test on every pull request (adds about a minute and ties merging to a performance test).

## D-2: Runner and tooling

- **Decision**: `ubuntu-24.04` (pinned, not `ubuntu-latest`), .NET SDK `10.0.x` through `actions/setup-dotnet` with `cache: true` keyed on `Directory.Packages.props` and the project files.
- **Rationale**: Linux runners include Docker, which the Testcontainers fixtures need. The solution already carries the SkiaSharp Linux native assets. Pinning the image stops a runner image change from altering results.
- **Alternatives considered**: Windows runners (slower, Docker Linux containers unsupported); a self-hosted runner (infrastructure to maintain).

## D-3: Build and test commands

- **Decision**: `dotnet restore`, `dotnet build Gisr.slnx --no-restore -warnaserror`, then `dotnet test Gisr.slnx --no-build --logger "trx" --results-directory TestResults`.
- **Rationale**: Identical to the local gate commands in the README, so a green local run predicts a green CI run. Warnings are already errors through `Directory.Build.props`; `-warnaserror` also covers NuGet audit warnings (FR-002).
- **Alternatives considered**: `dotnet test` without a separate build (fewer log lines, but a build failure and a test failure become harder to tell apart).

## D-4: Scale tests are skipped in the standard run

- **Decision**: Nothing to add. `ScaleFact` already skips unless `GISR_RUN_SCALE_TESTS=1`, and the TRX records it as skipped, so the summary can report "1 skipped" (FR-008). `scale.yml` sets the variable and filters on `Category=Scale`.
- **Rationale**: Reuses the existing mechanism instead of inventing a filter.

## D-5: Test summary without a third-party action

- **Decision**: A short `pwsh` step reads the `.trx` files and appends totals and the names of failed tests (with the first lines of each message) to `$GITHUB_STEP_SUMMARY`, then the step fails the job if the test step failed. It runs with `if: always()`.
- **Rationale**: Meets SC-004 with no extra dependency and no `checks: write` permission. Test names and messages do not contain secrets because the suites use no credentials.
- **Alternatives considered**: A marketplace test-report action (non-GitHub supply chain, needs a write permission); the console log alone (failed names are buried in thousands of lines).

## D-6: Artifacts and cache

- **Decision**: `actions/upload-artifact` with `if: always()`, `retention-days: 14`, path `TestResults/`. NuGet cache through `setup-dotnet` (FR-010).
- **Rationale**: Results survive for investigation; the cache key changes only when package definitions change.

## D-7: Concurrency, timeouts, permissions

- **Decision**: `concurrency: group: ci-${{ github.workflow }}-${{ github.ref }}` with `cancel-in-progress: ${{ github.event_name == 'pull_request' }}`; `timeout-minutes: 30`; top-level `permissions: contents: read`.
- **Rationale**: Newer pull request runs cancel older ones (FR-007) while runs on `main` are never cancelled, so every main commit keeps its result (SC-005). The timeout bounds a hung container (FR-012). Read-only permissions and no secrets satisfy FR-011.
- **Alternatives considered**: Cancelling main runs too (would drop results for rapid merges).

## D-8: Action pinning

- **Decision**: Pin each action to a full commit SHA with the release tag in a comment: `actions/checkout` v7.0.1, `actions/setup-dotnet` v6.0.0, `actions/upload-artifact` v7.0.2 (SHAs recorded in the workflow). Updating them is a deliberate pull request.
- **Rationale**: FR-013; a moved tag cannot change what the checks run.

## D-9: Container pulls and failure messages

- **Decision**: No pre-pull step. A short preliminary `docker version` step makes a missing or broken Docker engine fail with a clear message (edge case in the spec).
- **Rationale**: Testcontainers pulls `pgvector/pgvector:pg17` and `azurite:3.35.0` on demand; a clear engine check is cheaper than an image-caching layer.

## D-10: Required status and branch protection

- **Decision**: Documented, not automated. The README states the job name to require under Settings, Rules, for `main`.
- **Rationale**: Changing repository rules is an administrator action on shared infrastructure; the spec lists it as an assumption.

## D-11: Verifying the workflow itself

- **Decision**: Lint with `actionlint` locally (container image) before pushing, then prove SC-001 with a throwaway branch carrying (a) a compiler warning and (b) a failing test, each opened as a draft pull request and closed unmerged.
- **Rationale**: A workflow cannot be unit tested; observing red then green on GitHub is the real test.
