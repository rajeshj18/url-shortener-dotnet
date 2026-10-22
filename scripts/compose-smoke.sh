#!/usr/bin/env bash
# Smoke test for the local stack (TSK-03). Reused by CI (TSK-04, TSK-12).
# Precondition: the stack has been started with `docker compose up -d --build --wait`.
# Exits non-zero if any container is not healthy or any health endpoint doesn't return 200.
set -euo pipefail

TIMEOUT_SECONDS="${SMOKE_TIMEOUT_SECONDS:-120}"
ENDPOINTS=(
  "http://localhost:5080/health/live"
  "http://localhost:5080/health/ready"
  "http://localhost:5081/health/live"
  "http://localhost:5081/health/ready"
)

fail() { echo "SMOKE FAILED: $*" >&2; exit 1; }

echo "Waiting up to ${TIMEOUT_SECONDS}s for all containers to be healthy..."
deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
while :; do
  ids=$(docker compose ps -a -q)
  [ -n "$ids" ] || fail "no containers found; start the stack first"
  unhealthy=""
  for id in $ids; do
    name=$(docker inspect --format '{{.Name}}' "$id" | sed 's#^/##')
    state=$(docker inspect --format '{{.State.Status}}' "$id")
    health=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$id")
    if [ "$state" != "running" ] || [ "$health" != "healthy" ]; then
      unhealthy="$unhealthy $name($state/$health)"
    fi
  done
  [ -z "$unhealthy" ] && break
  [ "$(date +%s)" -ge "$deadline" ] && fail "not healthy:$unhealthy"
  sleep 3
done
echo "All containers healthy."

for url in "${ENDPOINTS[@]}"; do
  code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$url" || echo "000")
  [ "$code" = "200" ] || fail "$url returned $code"
  echo "OK  $url -> $code"
done

echo "SMOKE PASSED"
