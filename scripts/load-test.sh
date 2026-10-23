#!/usr/bin/env bash
# Runs one k6 load test against the running compose stack (TSK-12). k6 runs in a container on the compose
# network, so it reaches the services by name. Results (k6 JSON summary) go to tests/load/results/.
#
#   bash scripts/load-test.sh redirect            # scripts: redirect, hot-key, scan, create, smoke
#   RATE=1500 DURATION=2m bash scripts/load-test.sh redirect
set -euo pipefail

script="${1:?usage: load-test.sh <redirect|hot-key|scan|create|smoke>}"
root="$(cd "$(dirname "$0")/.." && pwd)"
network="${COMPOSE_NETWORK:-urlshortener_default}"
mkdir -p "$root/tests/load/results"

# MSYS_NO_PATHCONV keeps Git Bash on Windows from rewriting the container paths.
MSYS_NO_PATHCONV=1 docker run --rm --network "$network" \
  -v "$root/tests/load:/scripts:ro" -v "$root/tests/load/results:/results" \
  -e RATE -e DURATION -e SEED -e P99_REDIRECT_MS -e P99_CREATE_MS \
  grafana/k6:2.3.0 run --quiet --summary-export "/results/$script.json" "/scripts/$script.js"
