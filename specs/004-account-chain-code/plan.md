# Implementation Plan: Account Operating Number and Chain Code

**Branch**: `004-account-chain-code` | **Date**: 2026-10-26 | **Spec**: [spec.md](spec.md)
**Input**: `/specs/004-account-chain-code/spec.md`

## Summary
Extend existing Accounts features. Refresh Operating Number through delegated Graph `/me?$select=id,employeeId` during account bootstrap (`GET /api/me`, called once per interactive scope at login). No Graph queries during ordinary record, profile, or preference operations. Add private optional profile CRUD and a responsive form beside identity/preferences.

## Technical Context
**Language/Version**: C# 14 / .NET 10.
**Primary Dependencies**: Existing ASP.NET Core, Microsoft.Identity.Web, FluentValidation, Blazor Interactive Server, Bootstrap 5.3. No new packages.
**Storage**: PostgreSQL, Dapper, embedded SQL, forward-only migration 0010. Atomic bounded JSONB profile; typed relational owner/code/timestamp.
**Testing**: xUnit/VSTest, Testcontainers PostgreSQL, bUnit, browser inspection if authenticated session available.
**Target Platform / Project Type**: Existing multi-project web application.
**Performance Goals**: One additional Graph call per account bootstrap, none for profile/preferences writes.
**Constraints**: Nullable safety, cancellation, parameterized owner queries, no profile/identity data in logs.
**Scale/Scope**: One account page, one nullable account column, one optional profile per account.

## Constitution Check
PASS before research and after design: dependency direction unchanged; Shared contracts only; API owns identity/validation/hashing; Database owns SQL/migrations; Web uses HTTP. Existing packages/Bootstrap patterns reused. Tests first, zero-warning build, full tests, empty database migrations. No violations.

## Project Structure
### Documentation (this feature)
`specs/004-account-chain-code/`: spec, plan, research, data-model, contracts/accounts, quickstart, tasks, checklists/requirements.
### Source Code (repository root)
`src/Gisr.{Shared,Database,Api,Web}/Accounts/`; SQL in `src/Gisr.Database/Scripts/Accounts/` and `Scripts/Migrations/`; mirrored `tests/Gisr.{Api,Database,Web}.Tests/Accounts/`.
**Structure Decision**: Reuse Accounts; Database persists opaque profile JSON without referencing Shared. API explicitly deserializes/maps response contracts.

## Complexity Tracking
None. No new packages or architectural layers.

## Profile Fieldset Refinement
Use Bootstrap half-width desktop columns containing semantic fieldsets with visible Identity/Biographical Data and Biometric Data legends. Use border/rounded/padding utilities and float-none/w-auto legends to decorate only these two fieldsets. Identity controls stack; biometric controls use col-12 col-sm-6 in Height/Eye colors then Skin colors/Hair colors order. Place the four existing multiline fields below in two columns. Stack fieldsets on mobile. Remove only the birth-date control, retaining its loaded value in the outgoing profile so existing data and unchanged hashes are preserved. Update the Account Information alert verbatim. No API, schema, or dependency changes.

## Account Labels and Save Feedback
Use the existing Icon component for a decorative chain-link icon in a leading input-group-text addon within input-group input-group-sm. Keep the Your Chain Code label text-only, with its label/input group in a Bootstrap row's col-12 col-lg-6 wrapper. Retain read-only semantics and existing form behavior; no packages or custom CSS are needed.

Retain existing identity values and DTOs while renaming/reordering presentation labels and rendering Officer credential as a Bootstrap badge. Move the read-only Your Chain Code field before the profile form under Chain Code Components. Show successful saves in each card's footer after its buttons using Bootstrap alert-success blocks, a decorative check-circle from the existing Icon component, and polite live status announcements. Use the exact messages "Chain code updated..." and "Preferences saved...". Keep the icon non-shrinking and allow text to wrap on narrow screens. Reuse ChainCodeForm's five-second dismissal with lifetime cancellation and status versioning, and apply the same pattern to Preferences so older timers cannot clear newer successes. Keep errors inline, clear stale success on new saves, and cancel timers on disposal. No toast, JavaScript, custom CSS, or new dependencies.

## Account Layout and Multi-Color Refinement
Use two col-lg-6 cards followed by a col-12 Chain Code card, Bootstrap informational alert, existing Icon component, and responsive side-by-side fieldsets as detailed in Profile Fieldset Refinement. Color selects add removable Bootstrap badge tags. Display the generated code in a compact read-only input. No new packages or SQL migrations: the existing JSONB profile supports color arrays.

Add optional eyeColors, skinColors, and hairColors arrays while retaining legacy singular color fields for stored profiles and clients. Validate bounded collections and defined enums. Normalize colors as sorted distinct sets, representing zero or one color with the legacy nullable scalar and multiple colors with the new arrays. Omit null arrays from JSON so unchanged legacy single-color hashes remain stable. Test collection normalization, validation, save/reload, tag removal, and the revised layout before full regression/browser verification.

## Account Card Presentation Follow-Up
For FR-011, use existing Bootstrap card headers and action footers in AccountSettings and ChainCodeForm. Keep save, retry, and delete confirmation in footers. Associate external submit buttons with stable form IDs and retain explicit busy-state disabling. Account Information has no footer; its explanation appears in a body alert with a non-shrinking information icon. Tag removal stays with its color field. Verify structure and existing workflows with bUnit, then check responsive layout and keyboard tag interaction in the browser.