# Feature Specification: Account Operating Number and Chain Code

**Feature Branch**: `004-account-chain-code`
**Created**: 2026-10-26
**Status**: Ready for planning
**Input**: Synchronize Entra EmployeeId as a read-only Operating Number and add an optional fictional Chain Code profile to account settings.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Directory Identity (Priority: P1)

As a signed-in user, I can see my military Operating Number supplied by my organization without editing it in GISR.

**Why this priority**: Organizational identity must stay authoritative and separate from fictional biography.
**Independent Test**: Sign in with an EmployeeId, change it in the directory, and sign in again.
**Acceptance Scenarios**:
1. **Given** a directory EmployeeId, **When** I sign in, **Then** it is saved and displayed as Operating Number.
2. **Given** a changed or cleared EmployeeId, **When** the directory is successfully read again at sign-in, **Then** the saved value is replaced or cleared.
3. **Given** a directory outage, **When** I access my account, **Then** my existing identity is retained and account access remains available.
4. **Given** account settings, **When** I edit preferences, **Then** no Operating Number editing control is available and the value is unchanged.

### User Story 2 - Fictional Chain Code (Priority: P2)

As a user, I can optionally save a fictional personal profile and receive a made-up Chain Code derived from that information.

**Why this priority**: Personal world-building should not be required to use service records.
**Independent Test**: Create, reload, and change my profile without affecting preferences or another account.
**Acceptance Scenarios**:
1. **Given** no profile, **When** I open account settings, **Then** Account Information and Preferences share the first row, and Chain Code occupies the full second row.
2. **Given** fictional details, **When** I save, **Then** the details persist and a generated Chain Code appears.
3. **Given** an unchanged profile, **When** I save again, **Then** the code stays the same; changing any meaningful field changes it.
4. **Given** a saved profile, **When** I view account settings, **Then** no delete action is offered; profile removal is not performed in the application.
5. **Given** another user's profile, **When** I use my account operations, **Then** I cannot read or change their profile.

### Edge Cases

- Missing EmployeeId displays as not provided; a failed lookup does not erase the last known value.
- An entirely blank profile cannot generate a code; individual profile fields are otherwise optional.
- Leading/trailing whitespace and empty versus missing text do not change the generated code.
- Unsupported colors and oversized text fail validation without losing saved details.
- Failed saves keep entered values available for correction.
- Mobile layout stacks identity, preferences, and Chain Code without horizontal overflow.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Retrieve EmployeeId from the authenticated user's Entra record on sign-in and persist it as Operating Number.
- **FR-002**: Operating Number MUST be read-only to users and updated only from a successful authenticated directory lookup.
- **FR-003**: Missing directory data MUST be distinct from a failed lookup; failures preserve the saved value.
- **FR-004**: Account Information and Preferences MUST occupy the first row's left and right columns on large screens; Chain Code MUST span the full second row. Cards MUST stack on mobile.
- **FR-005**: Each account MAY have one private fictional profile with name, birth date text, homeworld, species, height text, eye/skin/hair colors, distinguishing marks, family history, criminal history, and biography.
- **FR-006**: World, species, and descriptive fields MUST accept free text; eye, skin, and hair colors MUST support zero or more distinct selections from inclusive fictional options, displayed as removable tags. Existing single-color profiles MUST remain readable.
- **FR-007**: Saving a nonblank profile MUST generate a deterministic, readable, fictional code from all normalized profile fields; meaningful edits MUST regenerate it.
- **FR-008**: Users MUST be able to reload and edit only their own profile; the application offers no delete action. No profile is required for other workflows.
- **FR-009**: Generated codes and Operating Numbers MUST NOT be accepted as authorization credentials or edited through profile requests.
- **FR-010**: Inputs MUST be bounded and validated, with accessible field errors and independent profile/preference save states.
- **FR-011**: Cards MUST have headings in headers. Account Information MUST have no footer and MUST show its existing read-only explanation in an icon-bearing informational alert at the top of its body. Preferences and Chain Code MUST keep save and retry actions in footers, preserving form submission, validation, and busy-state disabling. Preferences' submit button MUST read Save with a preceding icon.
- **FR-012**: The Chain Code header MUST omit the optional suffix. Identity and biometric controls MUST use the fieldsets specified in FR-016. The remaining four multiline fields MUST display two per row on large screens and stack on mobile.
- **FR-013**: The generated Chain Code MUST be displayed at the top of the card body in a compact read-only text field labeled Your Chain Code. A decorative chain-link icon MUST appear in a leading input-group addon, not in the label. Its wrapper MUST use col-12 col-lg-6 to occupy half the row on large screens and the full row below that breakpoint. The card header MUST read Chain Code Components. Adding or removing a selected color MUST affect generation; reordering or repeating the same selections MUST NOT. Existing single-color profiles MUST retain their code when saved unchanged.
- **FR-014**: Account Information MUST display User Name, Operating Number, and Access level in that order. The existing sign-in email remains the User Name value. Access level MUST display a text-bearing status badge reading Officer or Citizen. The contact-email preference MUST be labeled Notification Account without changing its optional email semantics.
- **FR-015**: Successful Chain Code and Preferences saves MUST show accessible success alerts in their own card footers, after the action buttons, with decorative success icons and the exact messages "Chain code updated..." and "Preferences saved..." respectively. Both alerts MUST disappear after five seconds, fit narrow/mobile and large screens, and give each new successful save its own five-second display period. Chain Code feedback MUST NOT use a toast. Validation and failure messages MUST remain inline in the card body. Timers MUST be canceled on component disposal.
- **FR-016**: The Account Information alert MUST read "These details come from the system's access control component and cannot be altered here." Birth date / era MUST no longer be editable or visible; existing stored values MUST survive saves. Beneath Your Chain Code, side-by-side fieldsets MUST group Full name/Homeworld/Species as Identity/Biographical Data and Height/Eye colors/Skin colors/Hair colors as Biometric Data. Both fieldsets MUST have subtle decorative borders and inset legends. Identity fields remain stacked; biometric fields MUST pair Height/Eye colors on the first row and Skin colors/Hair colors on the second at small-screen widths and above. Fields and groups MUST stack on narrow mobile screens. The four multiline fields MUST remain below in two desktop columns.

### Key Entities *(include if feature involves data)*

- **Account**: Existing authenticated identity, with optional authoritative Operating Number.
- **Chain Code Profile**: One optional owner-specific fictional biography, generated code, and last-updated instant.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every successful identity refresh reflects the current directory EmployeeId; failed refreshes preserve the previous value.
- **SC-002**: All profile fields survive save and reload; unchanged normalized profiles yield identical codes.
- **SC-003**: Unauthorized and cross-account attempts cannot disclose or change a profile.
- **SC-004**: Account settings are usable at 375px and desktop widths without overlapping controls or horizontal scrolling.
- **SC-005**: Accounts without profiles retain all existing functionality.

## Assumptions

- EmployeeId is not assumed to be present in standard identity tokens; an authenticated directory profile lookup is required.
- Birth date is fictional free text, allowing BBY/ABY and unknown dates rather than requiring a terrestrial date.
- Codes are fictional hashes, not canon biometric measurements, globally unique guarantees, military callsigns, or real credentials. The age-suffix lore in the reference is not enforced because fictional dates need not encode a computable age.
- A code is scoped to its owner and changes with profile details. Profile deletion is not offered in the application. World/species suggestions are deferred; free text is explicitly acceptable.
- Existing authentication, Bootstrap presentation, repositories, migrations, and test infrastructure are reused; no new packages are required.