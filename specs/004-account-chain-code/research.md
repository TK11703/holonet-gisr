# Research

## Current Identity Lookup
- Decision: Separate delegated Graph `/me?$select=id,employeeId` with `User.Read` from existing Officer `User.ReadBasic.All` search.
- Rationale: EmployeeId is not a standard token/basic directory field. Verify returned id against validated oid. Null clears; missing, malformed, oversized, unauthorized, or unavailable responses preserve stored number.
- Alternatives: Custom token claims need tenant policy; User.Read.All unnecessarily broadens permissions.
- Reference: https://learn.microsoft.com/en-us/graph/api/user-get?view=graph-rest-1.0

## Chain Code Generation
- Decision: Normalize all twelve fields (trim, Unicode Form C, CRLF/CR to LF, whitespace-only to null); fixed-order JSON; include version and authenticated account ID; SHA-256; first 128 bits as 40 decimal digits grouped in fives.
- Rationale: Stable owner-specific fictional code, unambiguous field boundaries. Not a credential or uniqueness guarantee.
- Alternatives: Random IDs do not derive from data; canon age suffix undefined for fictional dates; browser hashing trusts client data.

## Profile Lifecycle
- Decision: Atomic bounded JSONB document; owner-only GET/PUT/DELETE; independent save state; confirmed deletion; free-text worlds/species/dates; color dropdowns with Other/None.
- Rationale: No cross-account search requirement; existing Bootstrap/packages suffice.
- Alternatives: Curated worlds/species exceed scope; suggestions deferred because free text is explicitly acceptable.