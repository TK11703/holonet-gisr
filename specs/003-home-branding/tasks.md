# Tasks: GISR Home and Identity

**Input**: [Plan](plan.md), [specification](spec.md), research, data model, and UI contract
**Tests**: Required by the constitution; observe red before implementation.

## Phase 1: Setup

- [x] T001 Create feature branch and validated specification in specs/003-home-branding/spec.md.

## Phase 2: Foundation

- [x] T002 Confirm architecture, existing routes, styles, and dirty changes; record design in specs/003-home-branding/plan.md.

## Phase 3: User Story 1 - Enter the archive (P1, MVP)

**Independent test**: Root remains home and renders full name, hierarchy, and overview without API services.

- [x] T003 [US1] Add failing home rendering and no-redirect tests in tests/Gisr.Web.Tests/Home/HomeTests.cs.
- [x] T004 [US1] Replace src/Gisr.Web/Accounts/Home.razor with src/Gisr.Web/Home/Home.razor, using Bootstrap sections and semantic headings (FR-001, FR-002, FR-004, FR-008).

## Phase 4: User Story 2 - Start a record-keeping task (P1)

**Independent test**: All four home task links work; brand/Home/breadcrumb links return to root; nested routes keep the right active link.

- [x] T005 [US2] Add failing navigation tests in tests/Gisr.Web.Tests/Home/HomeTests.cs and tests/Gisr.Web.Tests/Layout/SideNavTests.cs.
- [x] T006 [US2] Wire shortcuts in src/Gisr.Web/Home/Home.razor and Home navigation in src/Gisr.Web/Layout/SideNav.razor; correct Home crumbs in existing feature pages (FR-003, FR-006).

## Phase 5: User Story 3 - Recognize the system (P2)

**Independent test**: The same original seal appears on home, brand, and favicon; the brand has an accessible home name.

- [x] T007 [P] [US3] Update brand tests in tests/Gisr.Web.Tests/Layout/TopBarTests.cs and home accessibility coverage in tests/Gisr.Web.Tests/Layout/AccessibilityTests.cs.
- [x] T008 [US3] Create src/Gisr.Web/wwwroot/gisr-mark.svg and wire src/Gisr.Web/Layout/TopBar.razor and src/Gisr.Web/Components/App.razor (FR-005).
- [x] T009 [US3] Add restrained responsive identity styles in src/Gisr.Web/wwwroot/theme.css and verify the home page at mobile/desktop widths (FR-007).

## Phase 6: Validation and Documentation

- [x] T010 Run focused/web tests, zero-warning solution build, and full solution tests using specs/003-home-branding/quickstart.md; document any environment blockers here.
- [x] T011 Update README.md with home/branding behavior and record final validation in specs/003-home-branding/tasks.md.

## Dependencies and Parallel Opportunities

- T001 -> T002 -> story tests; each story implementation depends on its failing tests.
- US1 and US2 share the home component, so implement together after both test groups are red.
- T007 is independent of home tests. Brand asset work can run independently of breadcrumb updates after tests fail.
- T009 depends on the final home markup and asset; final validation depends on all stories.

## Implementation Strategy

First deliver the static root (US1), then navigation consistency (US2), then brand/responsive polish (US3). Run all failing tests before the cohesive implementation; retain existing shell behavior. No data-layer changes.

## Validation Evidence (2026-10-26)

- Red: focused tests compiled and ran; 16 failed and 15 passed before implementation. Failures covered root redirect, missing content/links/logo/Home navigation, and incorrect Home breadcrumbs.
- Final build: `dotnet build Gisr.slnx --no-restore -warnaserror --verbosity minimal`, exit 0; zero warnings/errors.
- Final suite: `dotnet test Gisr.slnx --no-build --no-restore --verbosity minimal -- xUnit.ParallelizeTestCollections=false`, exit 0.
  - Web: 328 passed, 0 failed, 0 skipped.
  - API: 633 passed, 0 failed, 0 skipped.
  - Database: 204 passed, 0 failed, 1 skipped (opt-in scale test). Includes real PostgreSQL/migration tests.
- Default parallel runs exposed a timing-sensitive existing test: `SealedRecordViewTests.PollingStops_WhenTheComponentIsDisposed`. Its implementation and test are unchanged from HEAD. It passed in isolation and with sequential test collections; no unrelated polling changes were made.
- Runtime: Docker 29.9.2 and Node 22.23.4 available. Existing Aspire AppHost started successfully; unauthenticated root returned 302 to Entra, and the user signed in before browser checks. No authentication bypass was introduced.
- Real authenticated browser checks (Playwright through integrated browser):
  - At viewport widths 375, 768, and 1440, document scroll width equaled content width (360, 753, 1425 respectively; scrollbar consumes 15px).
  - Header brand and search do not overlap; both logo images loaded. Favicon resolved to the fingerprinted SVG asset.
  - Followed all four primary destinations and returned home via brand or Home breadcrumb. Nested creation kept Records active; search/gallery retained their active entries.
  - Mobile drawer opened, Escape closed it and restored focus. Keyboard navigation reached search; primary link displayed a visible Bootstrap focus ring.
  - Desktop/mobile screenshots inspected. Mobile header crowding found during validation was corrected and rechecked.
- All 11 tasks complete. No new packages, backend behavior, schema changes, fake statistics, or parent-service links.
