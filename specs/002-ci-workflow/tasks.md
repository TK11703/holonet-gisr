# Tasks: Continuous Integration Checks

**Input**: Design documents from `/specs/002-ci-workflow/` (spec.md, plan.md, research.md, quickstart.md)

**Tests**: A workflow cannot be unit tested. Verification is linting plus the deliberate red/green pull requests in quickstart.md (T008, T009), written as tasks before the final documentation is accepted.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependencies)
- **[Story]**: US1 = pull requests verified, US2 = main verified, US3 = scale check, US4 = diagnosable failures

## Phase 1: Setup

- [ ] T001 Create `.github/workflows/` and confirm the pinned action SHAs from research D-8 (`actions/checkout` v7.0.1, `actions/setup-dotnet` v6.0.0, `actions/upload-artifact` v7.0.2) are still the latest releases (`gh api repos/<action>/releases/latest`)

## Phase 2: User Story 1 - Pull requests are verified automatically (Priority: P1)

**Goal**: Every pull request to `main` gets one pass/fail status from a zero-warning build and the full test suite.

**Independent Test**: quickstart steps 2 and 3.

- [ ] T002 [US1] Create `.github/workflows/ci.yml` with the `pull_request` (base `main`) and `workflow_dispatch` triggers, workflow `permissions: contents: read`, a `concurrency` group that cancels superseded pull request runs only (D-7), and one job `build-and-test` on `ubuntu-24.04` with `timeout-minutes: 30`
- [ ] T003 [US1] In `ci.yml` add the steps: checkout, a Docker engine check (`docker version`, D-9), setup-dotnet `10.0.x` with the NuGet cache (D-2, FR-010), `dotnet restore`, `dotnet build Gisr.slnx --no-restore -warnaserror`, and `dotnet test Gisr.slnx --no-build --logger trx --results-directory TestResults` (D-3)

**Checkpoint**: a pull request shows a passing or failing `build-and-test` status.

## Phase 3: User Story 2 - The main branch stays verified (Priority: P2)

- [ ] T004 [US2] In `ci.yml` add the `push` trigger for `main`; confirm `cancel-in-progress` is false for push events so every main commit keeps its result (D-7)

## Phase 4: User Story 4 - Failures are quick to diagnose (Priority: P3)

**Independent Test**: quickstart steps 4 and 8.

- [ ] T005 [US4] In `ci.yml` add an `if: always()` PowerShell step that reads `TestResults/*.trx` and writes totals (passed, failed, skipped) and each failed test's name and first message lines to `$GITHUB_STEP_SUMMARY` (D-5), without any third-party action
- [ ] T006 [US4] In `ci.yml` add an `if: always()` `actions/upload-artifact` step for `TestResults/` named `test-results` with `retention-days: 14` (D-6)

## Phase 5: User Story 3 - Slow checks run on demand and on a schedule (Priority: P3)

**Independent Test**: quickstart step 7.

- [ ] T007 [P] [US3] Create `.github/workflows/scale.yml` with `schedule` (weekly) and `workflow_dispatch` triggers, read-only permissions, the same runner, checkout, Docker check and setup-dotnet steps, then build and `dotnet test tests/Gisr.Database.Tests --no-build --filter "Category=Scale"` with `GISR_RUN_SCALE_TESTS=1`, `timeout-minutes: 30`, and the same summary and artifact steps

## Phase 6: Polish and Verification

- [ ] T008 Lint both workflows with `actionlint` (quickstart step 1) and fix every finding
- [ ] T009 [P] Document in `README.md` a "Continuous integration" section: what each workflow runs, the equivalent local commands, `gh workflow run scale.yml`, re-running a run, and which job (`build-and-test` in workflow "CI") to require in the repository rules for `main`
- [ ] T010 Push the branch, open the pull request, and confirm quickstart steps 2 and 8 (green run, summary shows the skipped scale test, cache hit on a re-run)
- [ ] T011 Prove SC-001 with two throwaway draft pull requests: one adding a compiler warning, one with a failing test (quickstart steps 3 and 4); confirm red status, readable summary and artifact; close both unmerged and delete the branches
- [ ] T012 Confirm quickstart steps 5, 6 and 7 (cancellation of a superseded run, a run on `main` after merge, a manual scale run), recording any step that can only be confirmed after merge as a note on the pull request
- [ ] T013 Record the results in `specs/002-ci-workflow/quickstart.md` notes and mark the tasks done

## Dependencies

- T001 first. T002 then T003 (same file). T004, T005 and T006 follow T003 and edit `ci.yml` in sequence. T007 can start after T003 (it reuses its steps). T008 needs T002-T007. T009 is independent of the workflow files. T010 needs T008 and T009. T011-T013 need T010.
