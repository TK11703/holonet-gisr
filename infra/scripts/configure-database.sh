#!/usr/bin/env bash
# Idempotent. Creates the Api identity's database role, the vector extension, and the grants the Api needs to run its own migrations.
# Run as a PostgreSQL Entra administrator (the deployer) after az login; needs psql and network access to the server.
set -euo pipefail

: "${PG_SERVER:?}" "${PG_ADMIN_NAME:?}" "${PG_DATABASE:?}" "${API_IDENTITY_NAME:?}" "${API_IDENTITY_PRINCIPAL_ID:?}"

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

# The extension is created here because only an administrator may create it; migration 0001 then no-ops.
psql --no-psqlrc -v ON_ERROR_STOP=1 -d "${PG_DATABASE}" -v role="${API_IDENTITY_NAME}" -v db="${PG_DATABASE}" <<'SQL'
CREATE EXTENSION IF NOT EXISTS vector;
GRANT CONNECT, TEMPORARY, CREATE ON DATABASE :"db" TO :"role";
GRANT USAGE, CREATE ON SCHEMA public TO :"role";
SQL

echo "Database ${PG_DATABASE} is ready for ${API_IDENTITY_NAME}."
