# Smoke test for the local stack (TSK-03). PowerShell equivalent of compose-smoke.sh.
# Precondition: the stack has been started with `docker compose up -d --build --wait`.
# Exits non-zero if any container is not healthy or any health endpoint doesn't return 200.
$ErrorActionPreference = 'Stop'

$timeoutSeconds = if ($env:SMOKE_TIMEOUT_SECONDS) { [int]$env:SMOKE_TIMEOUT_SECONDS } else { 120 }
$endpoints = @(
    'http://localhost:5080/health/live',
    'http://localhost:5080/health/ready',
    'http://localhost:5081/health/live',
    'http://localhost:5081/health/ready',
    'http://localhost:5082/health/live',
    'http://localhost:5082/health/ready'
)

function Fail([string]$message) {
    [Console]::Error.WriteLine("SMOKE FAILED: $message")
    exit 1
}

Write-Host "Waiting up to ${timeoutSeconds}s for all containers to be healthy..."
$deadline = (Get-Date).AddSeconds($timeoutSeconds)
while ($true) {
    $ids = @(docker compose ps -a -q)
    if ($ids.Count -eq 0) { Fail 'no containers found; start the stack first' }
    $unhealthy = @()
    foreach ($id in $ids) {
        $name = (docker inspect --format '{{.Name}}' $id).TrimStart('/')
        $state = docker inspect --format '{{.State.Status}}' $id
        $health = docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' $id
        $exitCode = docker inspect --format '{{.State.ExitCode}}' $id
        # One-off jobs (e.g. migrate) are fine once they have exited successfully.
        if ($state -eq 'exited' -and $exitCode -eq '0' -and $health -eq 'none') { continue }
        if ($state -ne 'running' -or $health -ne 'healthy') { $unhealthy += "$name($state/$health)" }
    }
    if ($unhealthy.Count -eq 0) { break }
    if ((Get-Date) -ge $deadline) { Fail ("not healthy: " + ($unhealthy -join ' ')) }
    Start-Sleep -Seconds 3
}
Write-Host 'All containers healthy.'

foreach ($url in $endpoints) {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 5
        $code = [int]$response.StatusCode
    }
    catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    }
    if ($code -ne 200) { Fail "$url returned $code" }
    Write-Host "OK  $url -> $code"
}

Write-Host 'SMOKE PASSED'
