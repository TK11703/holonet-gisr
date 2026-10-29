#!/usr/bin/env bash
# Idempotent. Creates the Api identity's database role, applies the extension migration as an administrator, and grants what the Api needs to run its other migrations.
# Run as a PostgreSQL Entra administrator (the deployer) after az login; needs psql and network access to the server.
set -euo pipefail

: "${PG_SERVER:?}" "${PG_ADMIN_NAME:?}" "${PG_DATABASE:?}" "${API_IDENTITY_NAME:?}" "${API_IDENTITY_PRINCIPAL_ID:?}"

MIGRATIONS_DIR="$(cd "$(dirname "$0")/../../src/Gisr.Database/Scripts/Migrations" && pwd)"
EXTENSION_SCRIPT_ID="0001_create_extensions"

export PGHOST="${PG_SERVER}.postgres.database.azure.com"
export PGUSER="${PG_ADMIN_NAME}"
export PGSSLMODE=require
PGPASSWORD="$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)"
export PGPASSWORD
echo "::add-mask::${PGPASSWORD}"

psql --no-psqlrc -v ON_ERROR_STOP=1 -d postgres \
  -v role="${API_IDENTITY_NAME}" -v oid="${API_IDENTITY_PRINCIPAL_ID}" <<'SQL'
SELECT pgaadauth_create_principal_with_oid(:'role', :'oid', 'service', false, false)
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'role');
SQL

# Azure only lets azure_pg_admin members run CREATE EXTENSION, even when the extension exists, so the Api cannot run migration 0001.
# Apply it here and record it in the ledger with the checksum the Api computes (CRLF normalised to LF); the Api then skips it.
CHECKSUM="$(tr -d '\r' < "${MIGRATIONS_DIR}/${EXTENSION_SCRIPT_ID}.sql" | sha256sum | cut -d' ' -f1)"

psql --no-psqlrc -v ON_ERROR_STOP=1 -d "${PG_DATABASE}" \
  -v role="${API_IDENTITY_NAME}" -v db="${PG_DATABASE}" -v script_id="${EXTENSION_SCRIPT_ID}" -v checksum="${CHECKSUM}" \
  -f "${MIGRATIONS_DIR}/${EXTENSION_SCRIPT_ID}.sql" \
  -f "${MIGRATIONS_DIR}/bootstrap-ledger.sql" -f - <<'SQL'
INSERT INTO schema_migration (script_id, checksum_sha256, applied_at)
VALUES (:'script_id', :'checksum', now())
ON CONFLICT (script_id) DO NOTHING;
GRANT SELECT, INSERT ON schema_migration TO :"role";
GRANT CONNECT, TEMPORARY, CREATE ON DATABASE :"db" TO :"role";
GRANT USAGE, CREATE ON SCHEMA public TO :"role";
SQL

echo "Database ${PG_DATABASE} is ready for ${API_IDENTITY_NAME}."
