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

## Verification notes

Results recorded while validating the pull request that introduced the workflows.

| Step | Result |
|------|--------|
| 1 Lint | actionlint v1.7.12: no findings for both workflow files. |
| 2 Clean pull request | Green in about 3 minutes; summary shows 1152 passed, 0 failed, 1 skipped; `test-results` artifact present. |
| 3 Build warning | Throwaway pull request failed at "Build with warnings as errors". |
| 4 Failing test | Throwaway pull request failed at "Test"; summary listed the failing test with its message; artifact downloadable. |
| 5 Cancellation | Two pushes in quick succession; see the result noted on the pull request. |
| 6 Run on main | Can only be confirmed after the pull request is merged. |
| 7 Scale check | `gh workflow run` needs the workflow on the default branch, so the steps were proven on a throwaway pull request that also triggered the workflow: 1 scale test passed, 0 skipped, in about 1.3 minutes. A manual dispatch is confirmed after merge. |
| 8 Dependency reuse | Re-run reported "Cache hit" and finished faster (about 3 minutes against 3.7). |
| 9 Read-only, no secrets | `permissions: contents: read`, no `secrets.` reference, every `uses:` pinned to a full SHA. |