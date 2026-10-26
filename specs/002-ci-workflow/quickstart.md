# Quickstart: Validating the CI Checks

Validation guide for spec 002. Requirements: [spec.md](spec.md). Decisions: [research.md](research.md).

## Prerequisites

- GitHub CLI signed in as a repository collaborator (`gh auth status`).
- Docker, only if you want to lint the workflow locally.

## 1. Lint the workflow files

```powershell
docker run --rm -v "${PWD}:/repo" -w /repo rhysd/actionlint:latest -color
```

Expected: no output and exit code 0.

## 2. Standard checks pass on a clean pull request

Open a pull request from this branch to `main`.

Expected: the `build-and-test` job (workflow "CI") runs, ends green, and its summary shows the number of tests passed and `1 skipped` (the scale test). The run page offers a `test-results` artifact.

## 3. A build warning fails the check (SC-001, FR-002)

On a throwaway branch, add an unused local variable to any `src` file, push, and open a draft pull request.

Expected: the job fails at the build step and the warning text is in the log. Close the pull request without merging and delete the branch.

## 4. A failing test fails the check (SC-001, SC-004)

On a throwaway branch, make one existing test assert something false, push, and open a draft pull request.

Expected: the job fails at the test step, the run summary lists the failing test by name, and the `test-results` artifact is downloadable.

## 5. A superseded run is cancelled (FR-007)

Push two commits to a pull request in quick succession.

Expected: the first run shows "Cancelled" and only the second run reports a status.

## 6. The main branch is verified (FR-006)

Merge the pull request.

Expected: a run for the merge commit starts on `main` and reports a status.

## 7. The scale check runs on demand (FR-008)

```powershell
gh workflow run scale.yml
gh run watch
```

Expected: the run executes the scale tests (not skipped) and ends green in under 20 minutes.

## 8. Dependencies are reused (FR-010)

Re-run the standard checks with `gh run rerun <run-id>` without changing dependency files.

Expected: the restore step reports a cache hit and the whole run is faster than the first.

## 9. Read-only token and no secrets (FR-011)

Open the workflow files.

Expected: top-level `permissions` is `contents: read`, there is no `secrets.` reference, and every `uses:` line is a full commit SHA.
