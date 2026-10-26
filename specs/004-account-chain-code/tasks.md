# Tasks: Account Operating Number and Chain Code

## Phase 1: Setup
- [x] T001 Write and validate specification/design in specs/004-account-chain-code/.

## Phase 2: Foundation
- [x] T002 Write failing account/profile endpoint tests in tests/Gisr.Api.Tests/Accounts/AccountEndpointsTests.cs and ChainCodeEndpointsTests.cs.
- [x] T003 Add forward-only migration src/Gisr.Database/Scripts/Migrations/0010_account_chain_code.sql: nullable operating_number varchar(64); owner FK profile JSONB, code varchar(47), UTC updated_at.

## Phase 3: US1 Directory Identity
- [x] T004 [US1] Implement/test current-user Graph lookup in src/Gisr.Api/Accounts/GraphAccountIdentity.cs with delegated User.Read, id verification, 64-character EmployeeId limit, cancellation, failure preservation.
- [x] T005 [US1] Add account DTO/row mapping and directory-only update SQL in src/Gisr.Shared/Accounts/AccountDto.cs, src/Gisr.Database/Accounts/AccountRepository.cs, src/Gisr.Api/Accounts/AccountService.cs.
- [x] T006 [US1] Test sync/create/change/clear/outage and preference non-editability in tests/Gisr.Api.Tests/Accounts/AccountEndpointsTests.cs.

## Phase 4: US2 Optional Fictional Profile
- [x] T007 [US2] Add contracts in src/Gisr.Shared/Accounts/ChainCodeProfileDto.cs: twelve optional fields, shared color enum; no owner/code in request.
- [x] T008 [US2] Implement profile repository/SQL in src/Gisr.Database/Accounts/ChainCodeRepository.cs and Scripts/Accounts/: owner-only reads/upserts/deletes.
- [x] T009 [US2] Implement service/validator/routes in src/Gisr.Api/Accounts/: normalize all fields, hash version+owner+profile, lengths 200/80/1000/4000 as data-model.md, valid colors, reject blank profile, ProblemDetails/OpenAPI.
- [x] T010 [US2] Write failing UI tests in tests/Gisr.Web.Tests/Accounts/AccountSettingsPageTests.cs and ChainCodeFormTests.cs for layout/read-only identity, prefill/save/errors/retry/delete.
- [x] T011 [US2] Move preferences below identity and add src/Gisr.Web/Accounts/ChainCodeForm.razor with free-text fields, color selects, code display, independent state, accessible errors, confirmed deletion, cancellation.
- [x] T012 [US2] Validate hash every-field sensitivity/normalization and account isolation in tests/Gisr.Api.Tests/Accounts/; validate repository/migrations against PostgreSQL in tests/Gisr.Database.Tests/Accounts/.

## Phase 5: Verification
- [x] T013 Build Gisr.slnx with zero warnings; run all tests and clean-database migrations.
- [x] T014 Start application, inspect desktop/mobile account page if authenticated session available, and record evidence/limits in specs/004-account-chain-code/quickstart.md.

## Account Card Presentation Follow-Up
- [x] T015 Write and observe failing card header/footer, form ownership, retry, and delete-confirmation placement assertions in the existing account UI tests.
- [x] T016 Add Bootstrap headers and footers to all account cards; move associated buttons into footers while preserving form ownership and busy states (FR-011).
- [x] T017 Verify focused UI tests, zero-warning solution build, full regression suite, native footer submission, and desktop/mobile rendering; record results in quickstart.md.

## Account Layout and Color Tags
- [x] T018 Update account layout and multi-color requirements, design, contracts, and regression tests; observe failing tests.
- [x] T019 Extend profile color contracts, validation, and deterministic normalization without breaking saved single-color profiles.
- [x] T020 Implement requested card layout, icons, removable color tags, field grid, and compact read-only generated code.
- [x] T021 Validate focused tests, full zero-warning build/regression/migrations, and desktop/mobile browser workflows without altering saved user details; record desktop screenshot/pointer automation limitations in quickstart.md.

## Account Labels and Toast Feedback
- [x] T022 Update label/field-order/toast requirements and tests; observe the new expectations fail.
- [x] T023 Implement identity labels/badges, Notification Account, top-positioned Your Chain Code, and dismissible auto-hiding success toast.
- [x] T024 Validate account UI and regression gates; refresh the running app and inspect the updated account page.

## Profile Fieldset Refinement
- [x] T025 Test and implement the revised access-control alert and responsive identity/biometric fieldsets, removing the birth-date control without erasing stored values.
- [x] T026 Validate focused and full regression checks, zero-warning build, and refreshed desktop/mobile layout.

## Biometric Pairing and Decoration
- [x] T027 Test and implement paired biometric rows and decorative fieldset borders/legends; validate focused/full Web tests, zero-warning solution build, and refreshed responsive layout.

## Chain Code Icon and Width
- [x] T029 Move the chain-link icon from the label into a leading compact Bootstrap input addon; verify tests, build, and refreshed browser rendering without changing responsive width.
- [x] T028 Test and implement the decorative chain-link label icon and col-12 col-lg-6 generated-code wrapper; validate focused/Web regression checks, zero-warning build, and live responsive layout.

## Timed Footer Feedback
- [x] T030 Replace Chain Code save toast and Preferences body success with icon-bearing five-second footer alerts after action buttons; verify test-first behavior, regressions, build, and responsive rendering.

## Dependencies
T001 -> T002 -> T003; US1 T004-T006; US2 T007-T012; both -> T013-T014. Tests run before corresponding implementation. US1 is independently deployable MVP; US2 leaves non-profile accounts unchanged.

## Parallel Opportunities
US1 Graph tests and US2 contract tests touch separate files; UI form tests and repository tests are independent once contracts exist. Execution remains sequential in this session.

## Strategy
Deliver directory identity first, then optional profile lifecycle; focused red/green checks after each slice; full verification last. No extensions.yml exists, so no hooks apply.