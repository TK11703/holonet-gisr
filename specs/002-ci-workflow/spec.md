# Feature Specification: Continuous Integration Checks

**Feature Branch**: `002-ci-workflow`

**Created**: 2026-10-26

**Status**: Draft

**Input**: User description: "Add a CI workflow so the constitution's merge gates run automatically on every pull request."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pull requests are verified automatically (Priority: P1)

A contributor opens or updates a pull request. Without any manual step, the repository builds the whole solution with warnings treated as errors and runs every automated test, including the tests that need a real database and file-storage emulator. The result appears on the pull request as a single pass or fail status that a reviewer can rely on before merging.

**Why this priority**: The constitution's merge gates (zero warnings, all tests passing, migrations applying to an empty database) are currently checked only on a developer's machine. Making them automatic is the whole point of the feature.

**Independent Test**: Open a pull request with a deliberately introduced compiler warning and confirm the status is failed; remove the warning and confirm the status turns passed.

**Acceptance Scenarios**:

1. **Given** a pull request whose code builds cleanly and whose tests pass, **When** it is opened or updated, **Then** a passing status is shown on the pull request.
2. **Given** a pull request that introduces any build warning, **When** the checks run, **Then** the status is failed and the warning is visible in the run output.
3. **Given** a pull request that makes any test fail, **When** the checks run, **Then** the status is failed and the failing test names are visible without downloading anything.
4. **Given** a pull request that edits an already-applied database migration, **When** the checks run, **Then** the status is failed.
5. **Given** a new commit is pushed to a pull request while an earlier run is still going, **When** the new run starts, **Then** the superseded run is cancelled so the status always reflects the latest commit.

---

### User Story 2 - The main branch stays verified (Priority: P2)

Every change that lands on the main branch is verified the same way, so the team can trust that main always builds and passes its tests, and a broken merge is noticed within minutes.

**Why this priority**: Pull request checks run against the branch as it was when opened; combining several merged pull requests can still break main.

**Independent Test**: Merge a pull request and confirm a run for the merge commit starts and reports a status for main.

**Acceptance Scenarios**:

1. **Given** a pull request is merged, **When** the merge commit lands on main, **Then** the same checks run against it and report a status.
2. **Given** a run on main fails, **When** a maintainer looks at the repository, **Then** the failing commit and the reason are identifiable from the run.

---

### User Story 3 - Slow checks run on demand and on a schedule (Priority: P3)

The scale check (thousands of records and tens of thousands of files, asserting that searches and listings stay fast) takes about a minute longer than everything else and is not needed on every pull request. A maintainer can start it on demand, and it also runs on a regular schedule so a performance regression is noticed within a week.

**Why this priority**: It protects a documented performance expectation but is not needed to merge safely.

**Independent Test**: Start the scale check manually and confirm it runs the scale tests and reports a status.

**Acceptance Scenarios**:

1. **Given** a maintainer starts the scale check manually, **When** it completes, **Then** a status shows whether the scale tests passed.
2. **Given** the weekly schedule fires, **When** the scale check completes, **Then** a failure is visible in the repository's run history.
3. **Given** an ordinary pull request, **When** the standard checks run, **Then** the scale tests are skipped and reported as skipped, not silently missing.

---

### User Story 4 - Failures are quick to diagnose (Priority: P3)

When a run fails, the reviewer can see which step failed and which tests failed, and can download the detailed test results to investigate. Repeated runs reuse previously downloaded dependencies so feedback stays fast.

**Why this priority**: A failing check that is hard to read wastes the time the check was meant to save.

**Independent Test**: Break one test, open a pull request, and confirm the failed test appears in the run summary and the detailed results are downloadable.

**Acceptance Scenarios**:

1. **Given** a run in which tests fail, **When** the run finishes, **Then** a summary lists the failed tests and the detailed results are available to download for at least two weeks.
2. **Given** two consecutive runs with unchanged dependencies, **When** the second starts, **Then** it reuses the first run's downloaded dependencies instead of fetching them again.

---

### Edge Cases

- A pull request changes only documentation: the checks still run and report a status, so a required status never stays "pending" forever.
- A pull request comes from a fork: the checks run without access to any secret, and nothing in the checks needs one.
- The container service needed by the database tests is unavailable or slow to start: the run fails with a message that names the cause rather than a vague test failure.
- A dependency has a newly published vulnerability: the build fails through the existing zero-warning rule, and the output names the package.
- A flaky run: re-running the failed run is possible from the pull request without pushing a new commit.
- Two runs start for the same pull request: only the latest is kept.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repository MUST automatically run the standard checks on every pull request that targets the main branch, on opening and on every new commit.
- **FR-002**: The standard checks MUST build the entire solution with warnings treated as errors and fail on any warning.
- **FR-003**: The standard checks MUST run all automated tests of all test projects, including those that need a real database and a file-storage emulator, and MUST fail if any test fails.
- **FR-004**: The standard checks MUST apply the database migrations to an empty database and fail if the migration history was altered (this is covered by the existing tests and must not be skipped).
- **FR-005**: The standard checks MUST report one overall status per pull request that can be marked as required for merging.
- **FR-006**: The same checks MUST run for every commit pushed to the main branch.
- **FR-007**: A newer run for the same pull request MUST cancel the older run still in progress.
- **FR-008**: The scale tests MUST be excluded from the standard checks, MUST be reported as skipped there, and MUST run on demand and on a weekly schedule.
- **FR-009**: Failed tests MUST be listed in a run summary, and detailed test results MUST be kept as downloadable files for at least 14 days.
- **FR-010**: Downloaded dependencies MUST be reused between runs when the dependency definitions are unchanged.
- **FR-011**: The checks MUST NOT require any secret or credential, and MUST run with the minimum repository permissions needed to read the code and publish results.
- **FR-012**: A run MUST end within a fixed time limit so a hung job cannot occupy the runner indefinitely.
- **FR-013**: Third-party automation used by the workflow MUST be pinned to a specific version so a changed upstream release cannot silently alter the checks.
- **FR-014**: The repository documentation MUST explain what the checks do, how to run the same checks locally, how to start the scale check, and which repository setting makes the status required for merging.

### Key Entities

- **Check run**: One execution of the standard checks for a commit; has a result (passed, failed, cancelled), a log, a summary of failed tests, and downloadable test results.
- **Required status**: The overall pass/fail result that repository settings can require before a pull request may merge.
- **Scale check**: The separately triggered run of the long performance tests.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A pull request with a build warning or a failing test never shows a passing status (verified by deliberately introducing each and observing a failed status).
- **SC-002**: The standard checks finish in under 20 minutes for the current solution on a cold start, and in under 15 minutes when dependencies are reused.
- **SC-003**: After a push to a pull request, the status appears within 2 minutes of the run starting and replaces any earlier status within 1 minute of cancellation.
- **SC-004**: A reviewer can identify the failing step and the failing test names from the pull request page without opening any downloaded file.
- **SC-005**: Every commit on the main branch has an associated check result after this feature ships.
- **SC-006**: The scale check can be started and completes with a visible result in under 20 minutes.

## Assumptions

- The repository is hosted on GitHub and the checks use the hosting provider's built-in automation; no external service is introduced.
- Hosted Linux runners with container support are available; the database and file-storage emulator tests start their own containers.
- Enabling "required status" and any branch protection is a repository setting changed by a repository administrator; this feature documents it but does not change settings.
- Tests already fake the paid cloud services (embeddings and text recognition), so no cloud credentials are needed.
- Code coverage reporting, static-analysis services, deployment, and release publishing are out of scope for this feature.
- The standard checks run for every pull request regardless of which files changed, because a required status must always report.
