# Quickstart: GISR Core Records

Validation guide for running GISR locally and proving the feature end to end. Contracts:
[api.openapi.yaml](contracts/api.openapi.yaml), [ui-contract.md](contracts/ui-contract.md). Data details:
[data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK and Docker (the Aspire AppHost runs PostgreSQL with pgvector and the Azurite emulator as containers; Testcontainers uses Docker too)
- A Microsoft Entra tenant where you can create app registrations and assign users
- Azure OpenAI resource with an embeddings deployment (`text-embedding-3-large`) for real indexing; automated tests use a fake embedding generator.
- Optional: Azure AI Document Intelligence resource (F0 free tier gives 500 pages/month) with the Api identity granted `Cognitive Services User`, only for scanned-PDF OCR. Without it (or with `Search:OcrFallback:Enabled=false`) scanned PDFs show "Not searchable". PDF, DOCX, and text extraction are local.

## 1. Entra setup (once)

1. Register **Gisr.Api**: expose an API with scope `access_as_user`; add an app role `Officer` (allowed member type: Users); set access token version 2.
2. Register **Gisr.Web**: web platform, redirect URI `https://localhost:7200/signin-oidc`, client secret or certificate; grant permission to the `access_as_user` scope.
3. In the Gisr.Api enterprise application, assign the `Officer` role to at least one test user. Keep a second test user with no role.
4. Citizen picker (tenant directory): on Gisr.Api add a client secret (dev) and the Microsoft Graph delegated permission `User.ReadBasic.All`, then grant tenant admin consent. Without them the picker reports "unavailable" (503) and everything else still works. Example: `az ad app credential reset --id <Gisr.Api client id> --append`, `az ad app permission add --id <Gisr.Api client id> --api 00000003-0000-0000-c000-000000000000 --api-permissions b340eb25-3456-403f-be2f-af7a0d370277=Scope`, `az ad app permission admin-consent --id <Gisr.Api client id>`.

## 2. Local configuration

Run the Aspire AppHost, which owns local configuration (never commit secrets). Store the Entra values as AppHost user-secrets (or enter them in the dashboard when prompted):

```powershell
dotnet user-secrets set "Parameters:azure-ad-tenant-id" "<tenant id>" --project src/Gisr.AppHost
dotnet user-secrets set "Parameters:azure-ad-api-client-id" "<Gisr.Api client id>" --project src/Gisr.AppHost
dotnet user-secrets set "Parameters:azure-ad-web-client-id" "<Gisr.Web client id>" --project src/Gisr.AppHost
dotnet user-secrets set "Parameters:azure-ad-web-client-secret" "<secret>" --project src/Gisr.AppHost
dotnet user-secrets set "Parameters:azure-ad-api-scope" "api://<Gisr.Api client id>/access_as_user" --project src/Gisr.AppHost
dotnet user-secrets set "Parameters:azure-ad-api-client-secret" "<Gisr.Api secret>" --project src/Gisr.AppHost   # optional: enables the tenant directory picker
```

The AppHost injects `ConnectionStrings:Gisr` (PostgreSQL), `ConnectionStrings:blobs` (Azurite), `Api:BaseUrl`, the `AzureAd` settings, and the Azure OpenAI endpoint and embeddings deployment (`Search:OpenAiEndpoint`, `Search:EmbeddingDeployment`; edit them in `src/Gisr.AppHost/AppHost.cs`) into the Api and Web. The Api signs in to Azure OpenAI with `DefaultAzureCredential`, so your identity needs `Cognitive Services OpenAI User` on that resource. Other search settings are optional per project (`dotnet user-secrets`):

- `Gisr.Api`: `Search:OpenAiEndpoint` and `Search:EmbeddingDeployment` when running without the AppHost (the AppHost also sets `Search:OcrFallback:Endpoint`; grant your identity `Cognitive Services User` on that resource), and optionally `Search:OcrFallback:Enabled`, `Search:OcrFallback:Endpoint`, `Search:OcrFallback:MonthlyPageBudget` (default 2000), `Search:MaxDistance` (default 0.7; lower is stricter), and `Search:Indexing:Enabled` / `Search:Indexing:PollIntervalSeconds` (default 5) (indexing runs only when `Search:OpenAiEndpoint` is set unless forced)

Without the AppHost, set the same values per project: `Gisr.Api` needs `AzureAd:TenantId`, `AzureAd:ClientId`, `ConnectionStrings:Gisr`, and `Storage:BlobEndpoint` (or `ConnectionStrings:blobs`); `Gisr.Web` needs `AzureAd:TenantId`, `AzureAd:ClientId`, `AzureAd:ClientCredentials`, `Api:BaseUrl`, and `Api:Scope`.

## 3. Run

```powershell
dotnet run --project src/Gisr.AppHost   # starts PostgreSQL (pgvector), Azurite, Api (applies migrations), and Web
```

The Aspire dashboard URL is printed in the console. Open `https://localhost:7200` and sign in.

## 4. Automated validation

```powershell
dotnet build -warnaserror
dotnet test
$env:GISR_RUN_SCALE_TESTS = "1"; dotnet test tests/Gisr.Database.Tests --filter "Category=Scale"
```

Expected: zero warnings; all tests pass, including migrations applying to an empty PostgreSQL container, the tamper test failing on a changed checksum, the OpenAPI conformance test, and the access matrix. The scale test seeds 10,000 records and 50,000 artifacts and asserts search, gallery, and list queries finish in under 2 seconds (it is skipped unless `GISR_RUN_SCALE_TESTS=1`).

## 5. Manual validation scenarios

| # | Scenario (spec story) | Steps | Expected |
|---|----------------------|-------|----------|
| 1 | First sign-in (US1) | Sign in as the non-Officer user. | Lands on `/records` with an empty state; **Account settings** from the profile menu shows the user's settings; no records exist. |
| 2 | Account settings (US1) | Change display name and save; sign out and back in. | New name persists; sign-in identity fields are read-only. |
| 3 | Own records (US2) | Click **New record**, choose *Military*, leave Rank empty, save; then fill and save; create a second record as *Jedi*. | Inline errors on first save; both records listed; Jedi form shows its own fields. |
| 4 | Concurrency (US2) | Open the same record in two tabs, save in tab A, then tab B. | Tab B shows the conflict alert; nothing is overwritten; history shows both versions attributed. |
| 5 | Access denial (US3) | As the non-Officer, open another user's record URL, or call `GET /api/records?scope=all`. | 404 for the record, 403 for `scope=all`; no contents leaked. |
| 6 | Officer view (US3) | As the Officer, switch records list to **All records** and open another user's record. | Visible; an access-log row is written. |
| 7 | Service record (US4) | As the Officer, **New record**, pick the other user in **Citizen**, save. | Record attributed to the Officer; the other user sees a notification (bell badge within 60 s) and the record read-only in their list. |
| 7a | Sealed record (US4A) | As the Officer, create a service record for the other user with **Seal this record** on (try saving without a code first, then enter one of 8+ chars) and attach an image. As the other user: open `/records`, open the sealed entry, enter a wrong code, then the right code. | Save without a code is blocked. Entry shows only "Sealed record" and a created date; open shows access denied with the override form; wrong code is refused with attempts remaining; right code shows the record and its image with the "Viewing under override" banner. The image is absent from the gallery (for the Officer too) and the record's text never appears in the subject's search results. |
| 7b | Single-view unlock (US4A) | As the other user after unlocking: reload, navigate away and back, or open the record in a new tab. Also unlock the same record in two tabs and close one. | Every new visit shows access denied and asks for the code again; the old viewing's image links stop working. The two tabs are independent: closing one does not affect the other. |
| 7c | Lockout and Officer change (US4A) | As the other user, enter 5 wrong codes. Separately, while a viewing is open, have the Officer change the override code or unseal. | 6th attempt returns the "Too many attempts" message; after the Officer's change the open viewing clears the record and returns to access denied within 30 seconds (or, if unsealed, the record is simply readable after reload). |
| 8 | Artifacts (US5) | On a record you may edit, upload a PNG with a description, a PDF, then try a renamed `.exe`, a 30 MB file, and an image without a description. | First two attach; others rejected with clear messages; thumbnails appear for the PNG. |
| 9 | Gallery (US6) | Add images to two records (and one to a sealed record); open `/gallery`. | One card per non-sealed record with counts; click opens that record's photos; click a photo for the lightbox (Esc closes); records you cannot view and sealed records never appear. |
| 10 | Semantic search (US7) | Wait up to 2 minutes after saving, then search by meaning (e.g. a paraphrase of a record's summary, or text found only inside an uploaded text-based PDF, `.docx`, or `.txt` file). Also upload a PNG. | Matching records/files returned with snippets; results for records you cannot view are never listed or counted. The PNG shows "Not searchable" and never matches by content. |
| 10a | Scanned PDF fallback (US7) | Upload an image-only (scanned) PDF containing a distinctive phrase, then search for it. Repeat with OCR disabled or with `MonthlyPageBudget` set to 1. | With OCR enabled and budget available it is found (OCR runs once for that file; text-based PDFs and DOCX never trigger it). With OCR disabled or the budget reached it shows "Not searchable" with the reason, and is re-queued at the start of the next month. |
| 10b | Officer search of sealed records (US7/US4A) | As the Officer, search for text that exists only in a sealed record (or its PDF). As the sealed record's subject, run the same search. | The Officer sees the record with a Sealed badge and can open it on the record page (images/files viewable there). The subject sees no result and no count. |
| 11 | Shell and theme (FR-033/034) | Navigate all pages; resize below `lg`; tab through the top bar. | Dark appearance only (no theme control); top bar, left nav, and title-then-breadcrumb present on every page; nav becomes off-canvas on narrow screens; keyboard focus visible and Ctrl+K focuses search. |
| 12 | Credential change (FR-008) | Remove the Officer role in Entra; sign out and in (or wait for the access token to expire, 60 to 90 minutes). | Officer-only UI and `scope=all` no longer available. |

## 6. Pre-merge gates (constitution)

Zero build warnings, all tests green, migrations apply to an empty database, PR reviewed against the constitution. Work stays on branch `001-gisr-core-records`.
