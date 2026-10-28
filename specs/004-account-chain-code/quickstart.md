# Validation Guide

## Prerequisites
- .NET 10, Docker, existing AppHost configuration.
- API Entra app: add/consent Graph delegated `User.Read`; keep `User.ReadBasic.All` for Officer search. Configure existing confidential-client credentials securely. No application-wide directory permission needed.
- Reference: https://learn.microsoft.com/en-us/graph/api/user-get?view=graph-rest-1.0

## Automated Validation
1. `dotnet test tests/Gisr.Api.Tests/Gisr.Api.Tests.csproj --filter FullyQualifiedName~Accounts --verbosity minimal`
2. `dotnet test tests/Gisr.Database.Tests/Gisr.Database.Tests.csproj --filter "FullyQualifiedName~Accounts|FullyQualifiedName~Migrations" --verbosity minimal`
3. `dotnet test tests/Gisr.Web.Tests/Gisr.Web.Tests.csproj --filter FullyQualifiedName~Accounts --verbosity minimal`
4. `dotnet build Gisr.slnx --verbosity minimal` and `dotnet test Gisr.slnx --no-build --verbosity minimal`.

## Manual Scenarios
1. Run AppHost. Sign in with EmployeeId: Operating Number read-only. Change Entra value, sign out/in: updates. Clear: not provided. Failure preserves saved number.
2. Account Information and Preferences share the first desktop row; Chain Code spans the second row. Verify 375px stacking, informational alert without an identity footer, and icon-bearing Save button.
3. Save fictional fields including `41 BBY`, reload: fields/code persist. Unchanged save same code; each changed field new code.
4. Reject blank profile, excessive text, unsupported colors. Errors retain inputs and link to fields; failed requests retryable.
5. Cancel deletion preserves profile; confirm deletes only profile. Other accounts, even Officers, cannot access it.
6. Add several eye, skin, and hair colors; remove tags with keyboard and pointer controls. Save/reload preserves selections; selection order and duplicates do not change the generated code. Single-color legacy profiles retain their code when saved unchanged.
7. Verify Identity/Biographical Data and Biometric Data appear as side-by-side bordered fieldsets with inset legends below Your Chain Code on desktop and stack on mobile. Identity contains vertically stacked Full name, Homeworld, and Species. Biometric pairs Height/Eye colors then Skin colors/Hair colors at widths of 576px and above, stacking fields on narrower phones. The four multiline fields remain below in two desktop columns. Birth date / era is absent; saving an existing profile retains its stored birth-date value. Your Chain Code remains a compact read-only input above the fields under Chain Code Components.
8. Verify Account Information lists User Name, Operating Number, and Access level in that order, with a text-bearing badge reading Officer or Citizen. Notification Account remains an optional email input. Save the Chain Code and Preferences separately: each footer displays its own success icon and "Chain code updated..." or "Preferences saved..." after its buttons, disappearing after five seconds. Verify at desktop and phone widths that alerts fit without horizontal overflow and no toast appears. Errors remain inline in the card body.
9. Your Chain Code has a plain-text label and a decorative chain-link icon in a leading input-group addon. The field occupies half the row at large-screen widths (992px and above), otherwise the full row. The input remains read-only.

## Verification Record
- `dotnet build Gisr.slnx --artifacts-path artifacts/chain-code --verbosity minimal`: passed, zero warnings/errors.
- API suite: 670 passed. PostgreSQL suite: 206 passed, one opt-in scale test skipped. Web suite: 334 passed. Total: 1,210 passed, one skipped.
- Full-suite execution initially exposed missing cumulative OpenAPI documentation, which was updated in `specs/001-gisr-core-records/contracts/api.openapi.yaml`; all four conformance checks then passed.
- Two existing polling tests failed during simultaneous AppHost startup. The entire web suite passed on an isolated rerun without source changes. API/database suites also passed in that full run.
- All ten migrations applied to empty PostgreSQL databases; checksum/idempotency tests passed. No existing migration was edited.
- Authenticated browser at `https://localhost:7200/account`: desktop 1440x1000 and mobile 375x812 inspected with screenshots. Preferences is below identity; Chain Code is right on desktop and stacked on mobile. No horizontal overflow; all twelve inputs and the wrapped code fit.
- Live fictional-profile lifecycle passed: save all fields, reload text/colors/multiline values, stable unchanged save, changed homeworld changes code, cancel deletion, confirm deletion, reload absent profile, reject blank save. Temporary fictional data was deleted; account and preferences were left intact.
- One browser action ran before the reloaded page's Blazor circuit was interactive; retry after connection confirmed validation. Wait for interactivity before driving controls after navigation.
- Operating Number is read-only in the live UI. Directory EmployeeId change/clear/re-login was not tested live: no tenant identity or application permissions were modified. Sync/update/clear/outage behavior, identity verification, delegated request path, invalid responses, and cancellation are covered by automated tests. The `User.Read` consent/confidential-client prerequisite above remains necessary.
- AppHost is left running for review at `https://localhost:7200/account`.

## Account Card Follow-Up Verification
- Added headers and footers to all three cards, with headings in headers and all action buttons in associated footers, including retry and delete confirmation.
- Observed three targeted tests fail before the markup changes; all 13 focused account page/form tests then passed.
- Solution build passed with zero warnings/errors. Full suite: API 670 passed; PostgreSQL 206 passed and one opt-in scale test skipped; Web 335 passed. Total: 1,211 passed, one skipped.
- Authenticated browser confirmed both footer Save buttons submit their associated forms, and footer delete confirmation cancels correctly. Existing preference/profile values were retained.
- Desktop 1440px and mobile 375px layouts inspected with screenshots and overflow checks. The integrated browser's desktop viewport override required direct browser device metrics for a stable desktop capture; both layouts fit without horizontal overflow.
- Restarted AppHost with the updated markup and left it running at `https://localhost:7200/account`.

## Refined Layout and Color Tag Verification
- Test-first checks initially failed for revised card layout, tag behavior, multi-color normalization, and validation; all 18 focused UI checks and 36 API/OpenAPI checks subsequently passed.
- Full solution build passed with zero warnings/errors. API: 681 passed; PostgreSQL: 206 passed and one opt-in scale test skipped; Web: 340 passed. Total: 1,227 passed, one skipped. An existing record-status polling test failed during simultaneous AppHost startup, then the entire Web suite passed twice in isolation without changes to that record feature.
- PostgreSQL-backed tests cover every color collection's save/reload, distinct sorted normalization, invalid values, and unchanged legacy single-color codes. No schema migration was needed; empty-database migration tests passed.
- Authenticated browser confirmed Account Information/Preferences/Chain Code headings, no Account Information footer, Save text, and a read-only generated-code input. All three color pickers added and removed tags by keyboard; no save was triggered and existing saved profile values were retained.
- Browser layout checks at 1440px confirmed first-row account/preferences, full-width second-row Chain Code, field rows of 4/4/2/2, and no horizontal overflow. At 375px, cards/fields stack, buttons fit, and there is no horizontal overflow. Mobile screenshot inspected. The integrated browser's desktop screenshot compositor produced tiled captures despite correct DOM geometry, so desktop pixel-level inspection is limited; pointer automation also required keyboard activation for tag removal.
- Mobile screenshot identified a shrinking alert icon; wrapping it with Bootstrap flex-shrink-0 fixed its sizing, and all 340 Web tests passed after the adjustment. AppHost remains available at `https://localhost:7200/account`.

## Account Labels and Toast Verification
- Five new presentation expectations failed before implementation. All 21 focused account UI tests now pass, including both credential badge states, label/order changes, top-positioned read-only code, toast dismissal, and five-second auto-hide.
- Full solution build: zero warnings/errors. Full suite: API 681 passed; PostgreSQL 206 passed and one opt-in scale test skipped; Web 343 passed. Total: 1,230 passed, one skipped. Empty-database migration and checksum/idempotency tests passed; no database or API changes were needed for this follow-up.
- Restarted AppHost and confirmed the Web resource healthy. Authenticated browser verified the new labels/order/badge, Chain Code Components heading, and top-positioned read-only Your Chain Code. Desktop 1440px and mobile 375px geometry checks found no horizontal overflow; the toast fits within the mobile viewport and is fixed 16px from its top.
- Two unchanged-profile saves exercised live toast dismissal and auto-hide. No profile fields were edited; the generated code stayed unchanged. Inspected the mobile screenshot at artifacts/chain-code/account-labels-mobile.png; desktop screenshot-compositor limitations noted above remain. AppHost is left running at `https://localhost:7200/account`.

## Profile Fieldset Verification
- Observed five failing expectations before implementation; all 23 focused account UI checks now pass, including the exact access-control alert, fieldset membership, removed birth-date control, and preservation of stored birth-date values during saves.
- Solution and runtime builds passed with zero warnings/errors. API: 681 passed; PostgreSQL: 206 passed and one opt-in scale test skipped; Web: all 345 passed on an isolated rerun. Total passing across suites: 1,232. The concurrent solution run had two Web failures, including a notification polling timeout; no unrelated production/test code was changed to obtain the passing isolated rerun. Empty-database migration checks passed.
- Refreshed the authenticated page after AppHost became healthy. Browser DOM geometry at 1440px confirms both fieldsets share the same top position in separate columns; at 375px they share the same left position and stack vertically. No horizontal overflow at either size. Confirmed exact alert wording and no birth-date control. No profile save or data edit was performed during this browser check.
- Mobile capture showed the new form controls fitting, but navigation transition artifacts and a tiled recapture make final full-page pixel verification unreliable in the integrated browser. Geometry and automated grouping checks passed; no app changes were made for screenshot-compositor behavior. AppHost remains running at `https://localhost:7200/account`.

## Biometric Pairing and Decoration Verification
- Observed the updated fieldset test fail before implementation. All 23 focused account tests and all 345 Web tests pass. Solution and runtime builds have zero warnings/errors; edited files have no diagnostics. This markup-only refinement changes no API or database behavior; those suites passed in the preceding verification.
- Both named fieldsets use Bootstrap border/rounded/padding utilities with float-none/w-auto inset legends. Biometric controls use responsive half-width columns in Height/Eye colors then Skin colors/Hair colors order; the unframed multiline group remains unchanged.
- Refreshed the authenticated page after AppHost became healthy. Browser geometry at 1440px and 768px confirms Height/Eye colors have matching row positions, followed by Skin colors/Hair colors on a shared second row. At 375px, controls stack. Both fieldsets have computed 1px solid borders, 6px corner radii, and non-floating legends. No horizontal overflow at any width. No profile edits or saves were performed. Existing screenshot-compositor limitations still apply; verification uses live geometry/computed styles. AppHost remains running at `https://localhost:7200/account`.

## Chain Code Icon and Width Verification
- Observed the generated-code layout test fail before implementation. All 23 focused account tests and all 345 Web tests pass. Solution and runtime builds have zero warnings/errors; edited Razor components have no diagnostics.
- Added a chain-link path to the existing Icon component, rendered decoratively before Your Chain Code. The label and read-only input share a col-12 col-lg-6 wrapper inside a Bootstrap row. No data, API, database, dependency, or custom CSS changes.
- Opened the refreshed authenticated account page after the previous browser tab was closed. Live geometry at 1440px confirms the generated-code column is exactly half its row width (567.5/1135px); at 375px it fills the row (302/302px). The link SVG has a nonempty path, renders at 16px, and is aria-hidden. The input remains read-only and neither viewport has horizontal overflow. No profile data was edited or saved. AppHost remains running at `https://localhost:7200/account`.

## Chain Code Input Addon Verification
- Updated the existing test and observed it fail on the label icon before implementation. Moved the decorative icon into a leading input-group-text span within input-group input-group-sm; the label is text-only and the responsive wrapper is unchanged.
- All 23 focused account tests and all 345 Web tests pass. The full solution build has zero warnings/errors; edited files have no diagnostics. This correction changes only presentation and preserves read-only semantics.
- Refreshed the authenticated page and verified zero label icons, one decorative addon icon immediately before the input, and matching 31px addon/input heights. At 1440px the column remains half-width (567.5/1135px); at 375px it remains full-width (302/302px). Neither viewport overflows and the input stays read-only. No profile data was edited or saved. AppHost remains running at `https://localhost:7200/account`.

## Timed Footer Feedback Verification
- Observed three failing tests before implementation. All 24 focused account tests and all 346 Web tests pass, including footer placement, decorative success icons, exact messages, no toast/body success, five-second removal, and clearing previous Preferences success on a failed save.
- The full solution builds with zero warnings/errors, edited components/tests have no diagnostics, and git diff --check passes. No API, database, migration, dependency, or custom CSS changes were needed for this follow-up; earlier database/API verification remains unchanged.
- Restarted AppHost and confirmed the Web resource healthy. Authenticated browser checks at 1440px and 375px verified both success alerts below their own buttons, polite live containers, visible decorative icons, exact text, no toast, and no horizontal overflow. Both alerts expired; Preferences elapsed measurements were 5,074ms and 4,993ms after becoming visible.
- Saved unchanged Preferences and Chain Code values at each width solely to exercise feedback; no fields were edited and the generated code stayed unchanged. Layout evidence uses live DOM geometry because of the previously documented screenshot-compositor limitations. AppHost remains running at `https://localhost:7200/account`.