# UI Contract: GISR Web

Blazor Web App, Interactive Server render mode, Bootstrap 5.3.x, dark appearance only (`data-bs-theme="dark"`,
no theme switch anywhere). Visual reference: the supplied "Super Admin" dashboard screenshot (deep
teal-green surfaces, teal accent, rounded cards, compact top bar, grouped left navigation).

## Shell (all authenticated pages)

```text
+--------------------------------------------------------------------------------+
| GISR (site name) |      [ Search...        Ctrl+K ]      [bell] [+ New record] [avatar v] |  <- top bar, 100% wide, compact (3rem)
+---------+----------------------------------------------------------------------+
| Records |  Page title   Home > Section > Page                                  |
|  Citizen|  ------------------------------------------------------------------  |
|  Gallery|  page content (cards, tables, forms)                                 |
| Find    |                                                                      |
|  Search |                                                                      |
| Account |                                                                      |
|  Settings                                                                      |
+---------+----------------------------------------------------------------------+
   ^ left nav: full height under top bar, grouped with small section headings
```

### Top bar
| Element | Behavior |
|---------|----------|
| Site name | "GISR"; links to `/records`. |
| Search box | Pill input, placeholder "Search records and files...", submit (Enter) navigates to `/search?q=...`. `Ctrl+K` focuses it. Accessible name "Search". |
| Notifications | Bell icon button (accessible name "Notifications") with unread badge; opens dropdown with latest notifications, "Mark all read", each item links to its record and marks read. Polls every 60 s. Empty state "No notifications". |
| New record | Primary button "New record" -> `/records/new`. |
| Profile menu | Avatar (initials) dropdown button showing display name; items: **Account settings** (`/account`), **Sign out** (POST `/auth/logout`). Opens/closes by click, Escape closes, arrow-key navigation, focus returns to the trigger. |

### Left navigation
Section **Records**: Citizen Records (`/records`), Gallery (`/gallery`). Section **Find**: Search (`/search`). Section **Account**: Account Settings (`/account`). Active item highlighted and `aria-current="page"`. Below `lg`, the nav collapses behind a hamburger button in the top bar into an off-canvas panel with focus trap.

### Content area
Starts with a **page title** (`h1`) immediately followed by a **breadcrumb** (`nav aria-label="breadcrumb"`) on the same row (as in the reference), then a bordered "card" content surface. Implemented once as `PageHeader` (parameters: `Title`, `Crumbs`).

## Routes and pages

| Route | Page | Access | Contents |
|-------|------|--------|----------|
| `/` | Redirect to `/records` | authenticated | |
| `/records` | Citizen Records | all | Table of records (title, affiliation badge, subject, "Service record" badge, "Sealed" badge for Officers/unlocked users, engagement dates, artifacts count, updated). Filters: affiliation. Sort. Paging. Officers get a scope switch (My records / All records, default My) and subject filter. A **locked** sealed record renders as a redacted row: lock icon, "Sealed record", created date only; selecting it opens the record page's access-denied state. Empty state with "Create your first record" call to action. |
| `/records/new` | New record | all | Form in a card (header "Citizen Record"; footer with **Save Record** (save icon), **Delete record** (delete icon, disabled until saved), and Cancel). Tabs: **Record Details** (Title, Affiliation select, engagement start/end, Summary), **Incidents**, **Awards**, **Artifacts**, **History**. Affiliation options come only from the signed-in user's account affiliations (`GET /api/me/affiliations`, shown as "Primary › Secondary › Special Unit"). Incidents, Awards, and Artifacts are listed but disabled until the record has been saved once; after creating, the page continues on `/records/{id}/edit`. In each of those tabs the "new item" form is on top and the added items are listed below it. Officers also get a "Citizen" picker (default: myself). When the chosen citizen is not the Officer, a **Seal this record** switch appears; turning it on reveals a required **Override code** field (masked, 8-64 chars, with helper text that the code is shown to no one afterwards and must be shared with the citizen out of band; saving without it is blocked). |
| `/records/{id}` | Record view | subject or Officer | Header (title, affiliation, subject, author, version/updated, Sealed badge); sections: Engagement details, Incidents, Awards, Artifacts (thumbnails for images, "Not searchable" badge on files that are not indexed), History. "Edit" button when `canEdit`. **Access-denied state** (API 403 `sealed`): full-width alert "Access denied. This record is sealed." with no record content and always an **Override code** form (masked input, accessible label, "Unlock" button, `autocomplete="off"`). Incorrect code shows an error with attempts remaining; a 429 shows "Too many attempts. Try again in {n} minutes." and disables the form. **Override viewing**: on success the record renders from the override response, with a banner "Viewing under override. Access ends when you leave this page."; History is hidden and Edit is unavailable. The code, response, and view token live only in the component's server-side state; the component revokes the grant when disposed (navigation, reload, circuit end), so every later visit shows the access-denied state again. If an artifact request fails with 404, or the status poll returns 404 (`GET /api/records/{id}/override/status`, polled every 20 seconds; seal or code changed, or 15 minutes elapsed), the page clears the record from memory and returns to the access-denied state, so a viewing ends within 30 seconds. Artifact URLs on this page carry `?v={viewingId}` (see the media proxy row). |
| `/records/{id}/edit` | Record edit | `canEdit` | Same card and tabs as new, all tabs enabled (History lists revisions); includes seal switch and override code controls for Officers on service records; existing code is shown only as "An override code is set" with a **Change code** action (the code cannot be removed while sealed; unsealing clears it); submits with `expectedVersion`; 409 shows a "This record changed; reload" alert preserving the user's input. **Delete record** asks for confirmation, then soft-deletes (`DELETE /api/records/{id}`) and returns to `/records`. Artifacts tab: uploader on top (drag/drop or file picker), description field required for images, per-file progress and errors, then the added artifacts with remove and confirmation. |
| `/gallery` | Gallery | all | Card grid, one card per record with images: title, affiliation, image count, up to four thumbnails. Records that are sealed never appear here, for any user (including Officers); their images are viewable only on the record page. Officers get the same scope switch. Click -> `/gallery/{recordId}`. Empty state explains how to add images. |
| `/gallery/{recordId}` | Record gallery | subject or Officer | Photo grid of thumbnails (paged). Selecting a photo opens the lightbox: large image, description, previous/next, close (Esc), "Open record" link. |
| `/search?q=` | Search results | all | Query box, affiliation filter, Officer scope switch (default All records for Officers). Result list: record title, affiliation, **Sealed badge for sealed records (Officers only)**, source (record/file name), highlighted-context snippet, similarity. Selecting a result opens the record page (`/records/{id}`), including sealed records for Officers. Paged. Clear "no results" and "search temporarily unavailable" states. |
| `/account` | Account settings | self | Read-only sign-in identity (tenant, sign-in email) and whether the Officer credential is held; editable display name, contact email, notify-on-service-record toggle. Success/failed toast. |
| `/auth/login`, `/auth/logout` | Sign in/out | anonymous / authenticated | Entra OIDC challenge and sign-out. All other routes require authentication. |
| `/media/artifacts/{id}`, `/media/artifacts/{id}/thumbnail` | Artifact proxy | authenticated (cookie) | Streams from the API; for sealed records it requires `?v={viewingId}` and attaches the server-held view token stored for the signed-in user and that `viewingId` (404 if missing, unknown, another user's, or for a different record). `Cache-Control: private, no-store` for sealed records, `private` otherwise; `X-Content-Type-Options: nosniff`; non-image types served as attachment. |

## Cross-cutting UI rules

- Bootstrap utilities and components only; theme variables in `wwwroot/theme.css`; no jQuery, no extra UI framework, no Bootstrap JS bundle.
- Semantic landmarks: `header` (top bar), `nav` (left nav), `main` (content). Skip-to-content link first in the tab order.
- Icon-only buttons have accessible names; all images use their artifact description as `alt`.
- Contrast meets WCAG AA on the dark theme; focus indicators always visible.
- `prefers-reduced-motion` disables transitions/animations.
- User-provided text is rendered as text (Blazor encoding); never as markup.
- JS modules: `focus-trap.js`, `shortcuts.js`; both imported via module interop and disposed with the component.
- Validation errors display inline next to fields with `aria-describedby`; a summary appears at the top of the form.
- ProblemDetails from the API map to: field errors (400), "not found" page (404), "not permitted" message (403), sealed access-denied state with override form (403 with `sealed`), conflict alert (409), "too many attempts" message (429), retry banner (503).
- Override code inputs are masked, never echoed, never stored in component state longer than the submit, and never written to logs, URLs, or browser storage.
