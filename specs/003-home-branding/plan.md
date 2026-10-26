# Implementation Plan: GISR Home and Identity

**Branch**: `003-home-branding` | **Date**: 2026-10-26 | **Spec**: [spec.md](spec.md)

## Summary

Replace the root redirect with an archival landing page. Introduce an original SVG seal (orbital ring and identity dossier), reused as an image on home and the header and as the favicon. Add Home navigation and correct existing Home breadcrumb destinations. Preserve dirty-worktree changes to adjacent shell and records components.

## Technical Context

**Language/Version**: C# 14 / .NET 10, Razor, CSS, SVG
**Primary Dependencies**: Existing Blazor Interactive Server, Bootstrap 5.3, bUnit/xUnit
**Storage**: None added or modified
**Testing**: bUnit rendering/navigation tests, solution build and tests, browser layout checks
**Target Platform**: Existing authenticated Blazor Web App
**Project Type**: Multi-project web application
**Performance Goals**: No new API requests, JavaScript, external assets, or animation
**Constraints**: Preserve dark teal theme, access controls, compact header, existing navigation and dirty changes
**Scale/Scope**: One home screen, one reusable SVG asset, shell/breadcrumb wiring

## Constitution Check

Pre-design and post-design: PASS.

- Web-only changes preserve project references and architecture.
- Home lives in a Home feature folder; cross-feature identity uses a static asset and existing shell.
- No API contracts, SQL, data access, migration, or authentication changes.
- No new dependencies or CSS framework.
- Tests precede implementation and must be observed failing.
- Use existing Bootstrap responsive layouts with minimal identity-specific CSS.
- Work is on its own sequential feature branch. No automatic commit or merge.

## Project Structure

- `src/Gisr.Web/Home/Home.razor`: root page, replacing `Accounts/Home.razor`.
- `src/Gisr.Web/wwwroot/gisr-mark.svg`: original, scalable identity seal.
- `src/Gisr.Web/wwwroot/theme.css`: home identity styles and compact brand layout.
- `src/Gisr.Web/Components/App.razor`: SVG favicon.
- `src/Gisr.Web/Layout/`: brand link, Home entry, home icon.
- Existing feature pages: Home breadcrumb destinations only.
- `tests/Gisr.Web.Tests/Home/HomeTests.cs`: content, root behavior, links, decorative image tests.
- Existing layout tests: brand, Home active-state, accessibility and breadcrumb coverage.

## Implementation Sequence

1. Write failing tests for the existing root component and updated shell behavior.
2. Move root into Home feature and implement semantic sections using Bootstrap.
3. Add the mark and wire header/favicon; add Home navigation and fix breadcrumbs.
4. Run focused tests, all web tests, solution build/tests, and responsive browser validation where available.
5. Update README and record validation results in tasks.

## Complexity Tracking

No deviations. The SVG remains a single static source of truth rather than duplicated inline markup. Hierarchy labels are text, not fabricated parent-system links.
