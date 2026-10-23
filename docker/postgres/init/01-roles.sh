#!/bin/sh
# Creates the application's database roles (TSK-06, refinement R3).
# Runs ONCE, on the first initialisation of the data volume, as the bootstrap superuser. The superuser is
# used for nothing else. Passwords come from the environment (.env locally, the secret store in production),
# never from the repository (ADR-0011). Grants on tables and views are in the EF migrations, not here.
set -eu
: "${LINKS_MIGRATOR_PASSWORD:?missing}" "${LINKS_API_PASSWORD:?missing}" "${LINKS_REDIRECT_PASSWORD:?missing}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v db="$POSTGRES_DB" \
  -v migrator_pw="$LINKS_MIGRATOR_PASSWORD" \
  -v api_pw="$LINKS_API_PASSWORD" \
  -v redirect_pw="$LINKS_REDIRECT_PASSWORD" <<'SQL'
CREATE ROLE urlshortener_migrator LOGIN PASSWORD :'migrator_pw';  -- DDL; used only by the migrate job
CREATE ROLE urlshortener_api      LOGIN PASSWORD :'api_pw';       -- data read/write; no DDL
CREATE ROLE urlshortener_redirect LOGIN PASSWORD :'redirect_pw';  -- SELECT on redirect_lookup only (granted by the migration)

REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"db" TO urlshortener_migrator, urlshortener_api, urlshortener_redirect;

-- PostgreSQL 15+ already denies CREATE on schema public to PUBLIC. Only the migrator may create objects.
GRANT USAGE, CREATE ON SCHEMA public TO urlshortener_migrator;
GRANT USAGE ON SCHEMA public TO urlshortener_api, urlshortener_redirect;
SQL
