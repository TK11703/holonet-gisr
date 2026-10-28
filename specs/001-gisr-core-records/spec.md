# Feature Specification: GISR Core Records

**Feature Branch**: `001-gisr-core-records`

**Created**: 2026-10-23

**Status**: Draft

**Input**: User description: "We are going to build a system called Galactic Identity and Service Records (GISR). It will authenticate a user and retrieve user specific data. A user can view their account settings, which are tied to their system login account. The system will contain Citizen Records, and a user can create records for themselves. A user can view other citizen records if they possess a credential. A user can create a service record for another citizen if the user possesses a credential allowing that action. Each citizen record will contain various fields about an engagement, and the record will be customized for the user's current affiliation. The record can consist of artifacts like file uploads or images. There would be an image gallery to view a user's images, but the gallery would be grouped by record. There should be a search to find information."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign In and View Account Settings (Priority: P1)

A person signs in with their system login account and sees their own account settings (the profile and preference information tied to that login). The login account and its settings are separate from citizen records: a user has an account and settings from their first sign-in, but starts with no citizen records. Everything the system shows afterward is specific to the signed-in user.

**Why this priority**: Identity is the foundation. Every other capability depends on knowing who the user is and what they are allowed to do.

**Independent Test**: Sign in as a user, open account settings, and confirm the displayed information matches that user's login account and that no other user's data is shown. Delivers a working authenticated experience on its own.

**Acceptance Scenarios**:

1. **Given** a person who is not signed in, **When** they try to open any part of the system, **Then** they are required to sign in first.
2. **Given** a signed-in user, **When** they open account settings, **Then** they see the settings tied to their own login account.
3. **Given** a signed-in user, **When** they update an editable account setting, **Then** the change is saved and shown on their next visit.
4. **Given** a signed-in user, **When** they attempt to view another user's account settings, **Then** access is denied.
5. **Given** a person signing in for the first time, **When** they open the system, **Then** they have a login account and account settings but no citizen records, and are invited to create one.

---

### User Story 2 - Create and Manage My Own Citizen Records (Priority: P2)

A signed-in user creates citizen records for themselves, and then views and edits them. Each record captures information about an engagement, and the set of fields presented is customized for the user's current affiliation (for example civilian, military, Jedi, or bounty hunter). A user may create as many records for themselves as they need.

**Why this priority**: The citizen record is the core data of the system. A user managing their own record is the smallest useful slice of record-keeping and requires no special credentials.

**Independent Test**: As a newly signed-in user with no credentials and no records, create a citizen record for oneself, choose an affiliation, fill in the affiliation-specific fields, save, reopen, and edit it; then create a second record and confirm both are listed.

**Acceptance Scenarios**:

1. **Given** a signed-in user with no citizen records, **When** they create a citizen record for themselves and select an affiliation, **Then** the form presents the fields defined for that affiliation.
2. **Given** a user completing the form, **When** required fields are missing or invalid, **Then** the system explains each problem and does not save the record.
3. **Given** a saved record, **When** the user changes its affiliation, **Then** the record shows the fields for the new affiliation and previously entered values remain available as history.
4. **Given** a user who owns a record, **When** they edit it, **Then** the change is attributed to them with a timestamp.
5. **Given** a user who already has one or more citizen records, **When** they create another record for themselves, **Then** it is saved as a separate record and all of their records are listed together.

---

### User Story 3 - View Other Citizens' Records With a Credential (Priority: P3)

A user who holds the Officer credential can look up and open citizen records belonging to other citizens. A user without that credential cannot see them.

**Why this priority**: Shared access is the value of an authoritative archive, but it depends on records existing (P2) and identity being established (P1).

**Independent Test**: With two users and one record owned by the second, confirm the first user cannot view it without the credential and can view it once the credential is held.

**Acceptance Scenarios**:

1. **Given** a user without the Officer credential, **When** they request another citizen's record, **Then** access is denied and the record's contents are not revealed.
2. **Given** a user with the Officer credential, **When** they open another citizen's record, **Then** they see the record's content.
3. **Given** a user whose credential is removed or expires, **When** they next request another citizen's record, **Then** access is denied.
4. **Given** a viewing event of another citizen's record, **When** it completes, **Then** the access is recorded (who viewed which record and when).

---

### User Story 4 - Create a Service Record for Another Citizen (Priority: P4)

A user who holds the Officer credential can add a service record for another citizen, documenting an engagement (for example a military posting, a Jedi mission, or a bounty contract). The service record is a citizen record belonging to that citizen but authored by someone else, and its fields follow the affiliation it is recorded under. The citizen does not need to have any existing records.

**Why this priority**: Third-party authoring is the authoritative-record function that distinguishes GISR from a personal profile, but it builds on viewing and record structure.

**Independent Test**: With a user holding the service-record credential and a second citizen who has no records, add a service record for that citizen and confirm it appears among their records attributed to the author.

**Acceptance Scenarios**:

1. **Given** a user without the Officer credential, **When** they try to add a service record to another citizen, **Then** the action is unavailable and any attempt is denied.
2. **Given** a user with the Officer credential, **When** they add a service record for another citizen, **Then** the record is saved as that citizen's record and attributed to the author with a timestamp.
3. **Given** a service record being created, **When** the author selects an affiliation, **Then** the form shows the fields for that affiliation and validates them.
4. **Given** a citizen for whom someone else created a service record that is not sealed, **When** the citizen views their own records, **Then** they can see and open the new service record.

---

### User Story 4A - Seal a Service Record and Unlock It With an Override Code (Priority: P4)

When an Officer creates (or later edits) a service record for another citizen, they can mark it sealed and, at that moment, must set an override code. A sealed record stays on the citizen's list so they know it exists, but its contents are hidden: opening it shows an access-denied message with a field for the override code. Entering the correct code lets the citizen view the record once; leaving the page ends the view, and the next visit asks for the code again. Artifacts and images of a sealed record are visible only on the record page itself, never in the gallery.

**Why this priority**: Without sealing, every service record is immediately readable by its subject, which defeats authoritative records that must be withheld. It extends service record creation, so it ships alongside it.

**Independent Test**: As an Officer, create a sealed service record with an override code for a citizen. As that citizen, confirm the record is listed without details, opening it is denied, a wrong code is refused, and the right code reveals it.

**Acceptance Scenarios**:

1. **Given** an Officer creating a service record for another citizen, **When** they mark it sealed, **Then** they must enter an override code, and without one the record is not saved.
2. **Given** a sealed service record, **When** its subject views their record list, **Then** the record appears as a sealed entry showing no title, affiliation, engagement details, or author, and they are told a record exists.
3. **Given** a sealed record the subject has not unlocked in the current view, **When** the subject opens it (or any of its artifacts), **Then** they see an access-denied message and no record content.
4. **Given** the access-denied message for a sealed record, **When** it is shown, **Then** it includes a field to enter the override code.
5. **Given** the access-denied message, **When** the citizen enters the correct code, **Then** the record, including its artifacts, is shown for that viewing only, and the unlock is recorded.
6. **Given** a citizen viewing a sealed record through an override, **When** they leave the page, reload it, or return later, **Then** the record is locked again and they must re-enter the code.
7. **Given** the access-denied message with an override field, **When** the citizen enters an incorrect code, **Then** they are told it was incorrect, the attempt is recorded, and repeated failures temporarily block further attempts.
8. **Given** an Officer, **When** they change a record's sealed state or override code, **Then** any viewing in progress under the old code ends within 30 seconds.
9. **Given** a record that is not a service record (the citizen authored it), **When** anyone tries to seal it, **Then** the request is rejected.
10. **Given** a user who is not the record's subject and not an Officer, **When** they request a sealed record, **Then** they receive the same not-found response as for any record they may not view.
11. **Given** a sealed record with images, **When** anyone (including an Officer) opens the gallery, **Then** those images do not appear; they are viewable only on the record page.

---

### User Story 5 - Attach Artifacts to Records (Priority: P5)

A user adds artifacts such as documents and images to a record they are permitted to edit. Artifacts are shown with the record and can be opened or downloaded by anyone allowed to view that record.

**Why this priority**: Artifacts enrich records but records must work without them.

**Independent Test**: Attach a document and an image to a record, reopen the record, and confirm both are listed and retrievable; attempt an unsupported or oversized file and confirm it is rejected with a clear message.

**Acceptance Scenarios**:

1. **Given** a record the user may edit, **When** they upload a permitted file, **Then** the file is attached and listed with its name, type, size, and upload date.
2. **Given** an upload that is of a disallowed type or exceeds the size limit, **When** the user submits it, **Then** it is rejected with an explanation and nothing is attached.
3. **Given** a user who may not view a record, **When** they try to retrieve one of its artifacts, **Then** access is denied.
4. **Given** a user who may edit a record, **When** they remove an artifact, **Then** it no longer appears on the record.

---

### User Story 6 - Browse the Image Gallery Grouped by Record (Priority: P6)

A user opens an image gallery that shows the images they are allowed to see, grouped by the record each image belongs to. Selecting an image shows it larger along with its record context.

**Why this priority**: The gallery is a convenient view over existing artifacts and depends on artifact support.

**Independent Test**: Attach images to two different records, open the gallery, and confirm images appear under their respective record groups and only images the user may view are shown.

**Acceptance Scenarios**:

1. **Given** a user with images on multiple records, **When** they open the gallery, **Then** images are grouped under a heading for each record.
2. **Given** a gallery group, **When** the user selects an image, **Then** a larger view opens with a link back to its record.
3. **Given** images on records the user cannot view, or on sealed records, **When** the user opens the gallery, **Then** those images and their groups do not appear.
4. **Given** a user with no images, **When** they open the gallery, **Then** an empty-state message explains how to add images.

---

### User Story 7 - Search for Information (Priority: P7)

A user searches in natural language, by meaning rather than exact wording, across the records and file artifacts they are permitted to see and gets a list of matching results they can open.

**Why this priority**: Search improves discoverability once data exists; it is not required for the core record workflows.

**Independent Test**: Create records with distinguishing text, search for a term, and confirm only matching records the user may view are returned.

**Acceptance Scenarios**:

1. **Given** records containing a search term, **When** the user searches for it, **Then** matching records they may view are listed with the matching context.
2. **Given** records the user cannot view that contain the term, **When** the user searches, **Then** those records are never returned or counted.
3. **Given** a search with no matches, **When** results display, **Then** the user sees a clear no-results message.
4. **Given** many matches, **When** results display, **Then** they are paged and can be filtered by affiliation.
5. **Given** an Officer and a sealed record that matches, **When** the Officer searches, **Then** the sealed record is listed (marked as sealed) and opens on the record page; **and** the same record is never listed for its subject while locked.
6. **Given** a PDF, Word, or text file attached to a record, **When** the user searches for something said only inside the file, **Then** the record is found through that file; **and** a file that cannot be searched (for example an image, or a scanned PDF after the monthly limit) is labeled "Not searchable" with the reason.

---

### Edge Cases

- A user signs in for the first time and has account settings but no citizen records or credentials yet.
- A signed-in session expires while a user is filling in or saving a record.
- Two users edit the same record at the same time.
- A user changes affiliation after records exist under the previous affiliation.
- A credential is revoked while the holder is viewing or editing a record.
- A citizen's record is requested by a user who knows the record exists but lacks permission (the response must not disclose its contents).
- An uploaded file is empty, corrupt, has a misleading extension, or contains harmful content.
- An image is too large to display in the gallery without a smaller preview.
- A search term contains special characters or is extremely long.
- A user attempts to add a service record for a citizen who has no citizen records yet (allowed) or who is not a member of the organization's Microsoft Entra tenant (rejected). A tenant member who has never signed in is allowed.
- A citizen is viewing a sealed record through an override when the Officer changes the seal or code, or the citizen keeps the page open for a long time.
- A citizen guesses override codes repeatedly.
- A sealed record has images or files; the subject must not see them in the gallery, search, or direct links while it is locked.
- A notification is sent for a sealed record; it must not reveal any of the record's content.

## Requirements *(mandatory)*

### Functional Requirements

**Authentication and account**

- **FR-001**: System MUST require users to sign in with their system login account before accessing any record, artifact, gallery, search, or account page.
- **FR-002**: System MUST determine user identity solely from the authenticated login and MUST NOT accept user or owner identity supplied by the user as proof of identity or ownership.
- **FR-003**: Users MUST be able to view and edit their own account settings, which are tied to their login account.
- **FR-004**: System MUST NOT allow a user to view or change another user's account settings.

**Credentials and access**

- **FR-005**: System MUST support credentials held by users that grant specific permissions. The "Officer" credential allows a user to view and edit other citizens' records and to create service records for other citizens.
- **FR-006**: System MUST allow every signed-in user to create, view, and edit their own citizen records without any credential.
- **FR-007**: System MUST deny access to another citizen's records, artifacts, gallery images, and search results unless the user holds a credential permitting it.
- **FR-008**: System MUST apply credential changes (grant, expiry, revocation) no later than the end of the holder's current access-token lifetime (60 to 90 minutes by default in the identity provider) and MUST apply them on every sign-in.
- **FR-009**: System MUST record each access to another citizen's record, including who accessed it and when.

**Citizen records**

- **FR-010**: Users MUST be able to create any number of citizen records for themselves and view and edit them. A new user MUST start with no citizen records.
- **FR-010a**: System MUST treat the login account and account settings as separate from citizen records; creating, editing, or having no citizen records MUST NOT affect the account or its settings.
- **FR-011**: Each citizen record MUST consist of a title, the affiliation it is recorded under, engagement details specific to that affiliation, artifacts (files and photos), incidents, and awards.
- **FR-012**: System MUST present and validate the fields appropriate to the selected affiliation, and MUST explain validation failures to the user without saving invalid data.
- **FR-013**: System MUST retain information entered under a prior affiliation when a user changes affiliation.
- **FR-014**: System MUST attribute every creation and change to an authenticated user with a timestamp and retain a change history for each record.
- **FR-015**: System MUST prevent silent overwrites when two users change the same record at the same time.

**Service records**

- **FR-016**: Users holding the appropriate credential MUST be able to create a service record for another citizen.
- **FR-017**: A service record MUST identify the citizen it belongs to, the author, the affiliation it is recorded under, the engagement details for that affiliation, and the creation date.
- **FR-018**: A citizen MUST be able to view service records that others have created for them, except those that are sealed and locked (see FR-036 to FR-043).
- **FR-019**: System MUST allow a service record to be created for a citizen who has no other citizen records, and MUST reject service records for anyone who is not a member of the organization's Microsoft Entra tenant. A tenant member who has not yet signed in gets an account on demand, which their first sign-in adopts.

**Artifacts**

- **FR-020**: Users MUST be able to attach files, including images, to records they are permitted to edit.
- **FR-021**: System MUST accept only permitted file types within a defined size limit and MUST reject others with a clear message.
- **FR-022**: System MUST apply the same access rules to an artifact as to the record it belongs to.
- **FR-023**: Users MUST be able to open or download artifacts for records they are permitted to view, and remove artifacts from records they are permitted to edit.

**Image gallery**

- **FR-024**: System MUST provide a gallery showing the images a user may view, grouped by the record each belongs to.
- **FR-025**: Selecting a gallery image MUST show a larger view and a way to reach its record.
- **FR-026**: System MUST exclude from the gallery any image on a record the user may not view.

**Search**

- **FR-027**: Users MUST be able to search in natural language, matching by meaning rather than exact wording, across record content (title, engagement details, incidents, awards) and the text content of PDF, Word (.docx), and plain-text file artifacts. Scanned PDFs MUST be searchable too, subject to a monthly limit on how many scanned pages are processed. Other file types (such as images) are not searchable by content, and when a file is not searchable the user MUST be told so and why. Newly saved or uploaded content MUST become searchable within 2 minutes.
- **FR-028**: Search MUST return only results the user is permitted to view and MUST NOT reveal the existence or count of restricted results.
- **FR-029**: Search results MUST be paged and filterable by affiliation, and MUST show a clear message when nothing matches.
	The search bar MUST be centered above a separate filter row, with the result count on the left and filters on the right at desktop widths; controls MUST stack without overflow on narrow screens. The filter row MUST contain the affiliation dropdown and, for Officers, a records dropdown offering "All records" (default) and "My Records". Changing either filter MUST reset pagination while preserving the other filter. Results MUST appear below the filters as unframed rows separated by borders. Non-Officers MUST remain restricted to their own records without an all-records control.

**General**

- **FR-030**: System MUST be usable with a keyboard and assistive technologies, and provide text alternatives for images.
- **FR-031**: System MUST NOT expose sensitive personal record data in logs or error messages.
- **FR-032**: System MUST notify a citizen, within the application, when another user creates a service record for them, and MUST let users see and dismiss their notifications.
- **FR-033**: System MUST present a single dark appearance with no option to switch to a light appearance.
- **FR-034**: Every page MUST share one layout: a full-width top bar with the site name, search, notifications, a new-record button, and a profile menu (account settings and sign out); a full-height navigation menu on the left for the primary areas (citizen records, gallery, search, account settings); and a content area that begins with the page title followed by a breadcrumb.
- **FR-035**: System MUST provide a citizen record listing and a record view and edit experience, and a gallery page that groups images by record and lets the user open a record's gallery to view its photos.

**Sealed records**

- **FR-036**: Officers MUST be able to mark a service record as sealed when creating it and to seal or unseal it when editing it, and MUST set an override code whenever they seal a record. System MUST reject sealing a record the citizen authored for themselves, sealing without an override code, and sealing by anyone who lacks the Officer credential.
- **FR-037**: A sealed record is locked for its subject citizen except during a viewing unlocked by the override code. Officers are never locked out. Users who are neither the subject nor an Officer receive the same response as for any record they may not view.
- **FR-038**: System MUST show a locked sealed record in its subject's record list as a sealed entry that reveals only that a sealed record exists and when it was created; the title, affiliation, author, engagement dates, and all content MUST be withheld.
- **FR-039**: When a subject opens a locked sealed record, System MUST show an access-denied message with no record content and a field to enter the override code.
- **FR-040**: Officers MAY change the override code of a sealed record. System MUST store override codes only in a non-recoverable form, MUST NOT display them to anyone after they are set, and MUST NOT write them to logs or history. A sealed record always has an override code; unsealing removes it.
- **FR-041**: Entering the correct override code MUST show the record, including its artifacts, to the subject for that single viewing only (view-only). Leaving or reloading the page, or 15 minutes elapsing, MUST end the viewing, after which the code MUST be entered again. Changing the record's sealed state or override code MUST end any viewing in progress within 30 seconds.
- **FR-042**: System MUST record every override attempt and unlock (who, which record, when, success or failure) and MUST block further attempts for that user and record after 5 failed attempts within 15 minutes, until 15 minutes have passed since the oldest of those failures.
- **FR-043**: System MUST show the artifacts and images of a sealed record only on that record's page and only to those permitted to view it at that moment (Officers, or the subject during an override viewing). They MUST NOT appear in the gallery for any user. System MUST exclude a locked sealed record's content from its subject's search results and MUST NOT reveal it through counts. Officers MUST be able to find sealed records through search and open them from the record page. Notifications about a sealed record MUST NOT reveal its content.

### Key Entities

- **User Account**: A person's system login identity and their editable settings. Exists from first sign-in and is independent of citizen records; an account may have zero or many citizen records.
- **Credential**: A permission held by a user that allows an action beyond their own data. The "Officer" credential allows viewing and editing other citizens' records and creating service records for others.
- **Citizen Record**: A record belonging to a citizen (an account holder) documenting an engagement under one affiliation, with a title, affiliation-specific engagement details, artifacts, incidents, and awards. Has a subject citizen, an author, and a change history.
- **Incident**: A dated event of note recorded on a citizen record, with a title and description.
- **Award**: A recognition recorded on a citizen record, with a name, date, and issuer.
- **Notification**: A message to a user that something relevant happened, such as a service record being created for them.
- **Affiliation**: A category (for example civilian, military, Jedi, bounty hunter) that determines which engagement fields a record contains.
- **Service Record**: A citizen record about a citizen that was authored by a different, credentialed user rather than by the citizen. It may be sealed.
- **Seal and Override Code**: A sealed service record is hidden from its subject citizen while locked. The Officer sets the override code when sealing. Entering it opens a single viewing of the record for the subject; each later visit requires the code again.
- **Override Attempt**: A record of an override code entry (who, which record, when, success or failure).
- **Artifact**: A file or image attached to a record, with name, type, size, upload date, and uploader.
- **Access Log Entry**: A record of who accessed another citizen's record and when.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time user can sign in and create their own citizen record in under 5 minutes.
- **SC-002**: A user can view their account settings within 3 seconds of signing in.
- **SC-003**: 100% of attempts to view or search another citizen's data without the required credential are denied, and none reveal record contents.
- **SC-004**: 95% of users locate a specific record using search in under 30 seconds.
- **SC-005**: Search results and gallery pages display within 2 seconds for libraries of up to 10,000 records and 50,000 artifacts.
- **SC-006**: 100% of created and changed records show who made the change and when.
- **SC-007**: 90% of users attach an artifact to a record on their first attempt without assistance.
- **SC-008**: Every credential grant or revocation takes effect for the holder within 90 minutes and always at their next sign-in.
- **SC-009**: 100% of locked sealed records reveal no content (including through lists, gallery, search, notifications, and direct links) to their subject; their images never appear in the gallery for any user; a correct override code shows a record in under 30 seconds; and every new visit to a sealed record requires the code.

## Assumptions

- Users sign in through an existing organizational identity provider; account creation and passwords are managed there, not in GISR.
- Account settings are limited to profile and preference information (such as display name, contact preference, and display options) and do not duplicate identity-provider data that GISR cannot change.
- The login account and settings are created automatically on a user's first sign-in; citizen records are never created automatically.
- Credentials are assigned and revoked by administrators in the organization's identity provider (the "Officer" role); this feature only honors credentials already held. Credential administration screens are out of scope.
- A citizen cannot edit a record authored by another user about them; only Officer credential holders can edit such service records.
- Only service records can be sealed. Unlocking is view-only and covers one viewing; override codes are set by the Officer when sealing (minimum 8 characters) and are shared with the citizen outside this system.
- PDF, Word (.docx), and plain-text files are searchable by content. Text-based documents are read at no cost; scanned PDFs are read with a paid recognition service, only when no text can be read directly and only up to a monthly page limit (and a per-document page limit) to keep running costs low. When the limit is reached, scanned PDFs stay attachable and viewable but not searchable until the next month. Images and other file types are never searchable by content. Record text sent for search indexing to the organization's cloud AI service is acceptable.
- A locked sealed entry shows no title or affiliation because those could themselves be sensitive; the citizen sees only that a sealed record exists and when it was created.
- A user may create any number of citizen records for themselves, each under a single affiliation; records about other citizens arise only as service records created by credentialed users, and the subject citizen must be a member of the organization's Microsoft Entra tenant (they need not have signed in before).
- The initial affiliations are civilian, military, Jedi, and bounty hunter; the set of fields for each will be defined during planning and can be extended later.
- Permitted artifact types are common documents and images with a reasonable per-file size limit, to be fixed during planning.
- Viewing another citizen's record is all-or-nothing for the record's content in this version; per-field restrictions are out of scope.
- Deleting citizen records is out of scope; records are retained as authoritative history, and only artifacts can be removed.
- Mobile-native applications, offline use, and bulk import of existing records are out of scope.
