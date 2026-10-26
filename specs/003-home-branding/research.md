# Research: GISR Home and Identity

## Landing-page content

- **Decision**: Static archival introduction, existing feature shortcuts, identity/service/evidence overview.
- **Rationale**: User explicitly chose a landing page; existing account state and record APIs need not be loaded.
- **Alternatives considered**: Live personal dashboard, rejected for this scope; invented metrics, rejected as misleading.

## Brand

- **Decision**: Original geometric SVG combining an orbital ring, an identity dossier, and record lines.
- **Rationale**: Connects galactic context with record-keeping without copying an existing franchise insignia. One asset can serve header, hero, and favicon.
- **Alternatives considered**: External image-generation services, unnecessary; text-only brand, insufficient for requested logo.

## Navigation

- **Decision**: Root becomes actual home; header, sidebar, and Home breadcrumbs point to it.
- **Rationale**: Current root only redirects; Home breadcrumbs currently mean records and would be misleading.
- **Alternatives considered**: Keep brand linked to records, rejected as inconsistent.

## Layout and access

- **Decision**: Existing Bootstrap responsive grid and cards, dark teal palette, semantic headings, decorative images with empty alt text beside named content.
- **Rationale**: Preserves conventions and avoids packages, motion, or new authorization paths.
- **Alternatives considered**: New UI framework or animation, rejected.

No unresolved technical questions. Existing repository patterns provide all required evidence.
