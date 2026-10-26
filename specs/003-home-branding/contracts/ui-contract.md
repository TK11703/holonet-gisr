# UI Contract: Home and Identity

- `/` renders home in the existing authenticated shell; it does not redirect.
- One `h1`: Galactic Identity and Service Records.
- Visible hierarchy: HoloNet / Databank / GISR, with no parent-system hyperlinks.
- Primary links: `/records`, `/records/new`, `/search`, `/gallery`.
- Overview covers identity, incidents/awards/service history, attachments, gallery, and revisions without claims of new capabilities.
- The site-brand link targets `/`, contains GISR and the original seal, and is named "GISR home".
- Sidebar Home targets `/` and is active only at the root.
- All breadcrumbs labeled Home target `/`.
- The same SVG asset supplies header, hero, and browser favicon.
- Decorative logo images have empty alt text because adjacent text names the application.
- No dashboard statistics, activity feed, live-service badges, or record requests.
- Responsive layout at 375px and 1440px must not overflow horizontally.
