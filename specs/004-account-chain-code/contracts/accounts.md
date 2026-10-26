# Accounts Contracts

Existing GET `/api/me` and PUT `/api/me/settings` return nullable `operatingNumber`. Settings request unchanged and cannot set this value.

All profile routes require existing authentication/API scope. No owner ID, code, or Operating Number accepted in requests.

| Route | Request | Success | Errors |
| --- | --- | --- | --- |
| GET /api/me/chain-code | None | 200 ChainCodeProfileDto | 401/403/404 ProblemDetails |
| PUT /api/me/chain-code | UpdateChainCodeRequest | 200 ChainCodeProfileDto | 400 validation; 401/403 ProblemDetails |
| DELETE /api/me/chain-code | None | 204 | 401/403/404 ProblemDetails |

Request contains the twelve fictional fields in data-model.md. Response: `profile` (normalized request fields), `chainCode`, UTC `updatedAt`. Errors use camelCase fields; blank profile uses `profile`.

Color selections use optional `eyeColors`, `skinColors`, and `hairColors` arrays, each bounded to 20 defined ProfileColor values. Empty/null arrays mean no selections. Legacy nullable `eyeColor`, `skinColor`, and `hairColor` remain supported; when both forms are supplied, their union is used. Normalization sorts and deduplicates selections, storing a single selection in the legacy scalar and multiple selections in the array. Null arrays are omitted. Unchanged single-color profiles retain their existing generated code. Collection validation errors use the plural property name.

API registration needs delegated Graph `User.Read` consent alongside existing `User.ReadBasic.All` for Officer search. Reuse configured confidential credentials. Do not change tenant registrations autonomously.