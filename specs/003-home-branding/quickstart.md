# Validation: GISR Home and Identity

## Automated

Prerequisite: .NET 10 SDK; Docker for database/API tests.

```powershell
dotnet test tests\Gisr.Web.Tests --filter "FullyQualifiedName~HomeTests|FullyQualifiedName~TopBarTests|FullyQualifiedName~SideNavTests|FullyQualifiedName~AccessibilityTests"
dotnet build Gisr.slnx -warnaserror
dotnet test Gisr.slnx --no-build
```

Use a separate artifacts path if the running application locks default outputs. Existing migration tests validate applying the schema to an empty PostgreSQL database.

If the existing polling-disposal test fails under concurrent collections, rerun with
`dotnet test Gisr.slnx --no-build --no-restore -- xUnit.ParallelizeTestCollections=false`.
The final validation used this invocation: 1,165 passed, one opt-in scale test skipped.

## Browser

Start the existing AppHost as documented in the repository README and sign in.

1. Visit `/`: it remains home, displays the full name and hierarchy, and shows no invented activity or counts.
2. Open each of records, creation, search, and gallery via home links.
3. Return using the header brand, Home navigation, and Home breadcrumbs.
4. At 375px and 1440px, verify no horizontal overflow and visible, usable primary links.
5. Tab through home and confirm visible focus, sensible headings, and usable mobile navigation.
6. Confirm the same seal appears in home, header, and tab.
7. Confirm nested records/gallery routes retain their own active sidebar state.

See [UI contract](contracts/ui-contract.md) for exact expectations.
