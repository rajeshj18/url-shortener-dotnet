# Runs one k6 load test against the running compose stack (TSK-12). Windows PowerShell twin of load-test.sh.
#   powershell -ExecutionPolicy Bypass -File scripts\load-test.ps1 redirect
#   $env:RATE = "1500"; powershell -ExecutionPolicy Bypass -File scripts\load-test.ps1 redirect
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("redirect", "hot-key", "scan", "create", "smoke")]
    [string]$Script
)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$network = if ($env:COMPOSE_NETWORK) { $env:COMPOSE_NETWORK } else { "urlshortener_default" }
New-Item -ItemType Directory -Force (Join-Path $root "tests\load\results") | Out-Null

docker run --rm --network $network `
    -v "${root}\tests\load:/scripts:ro" -v "${root}\tests\load\results:/results" `
    -e RATE -e DURATION -e SEED -e P99_REDIRECT_MS -e P99_CREATE_MS `
    grafana/k6:2.3.0 run --quiet --summary-export "/results/$Script.json" "/scripts/$Script.js"
exit $LASTEXITCODE
