# Build and test the Release configuration. Runs the Phase 0 gate commands.
[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Write-Host '== PlaySphere Release build ==' -ForegroundColor Cyan

& (Join-Path $PSScriptRoot 'verify-environment.ps1')

& dotnet restore
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed (exit $LASTEXITCODE)." }

& dotnet build -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }

if (-not $SkipTests) {
    & dotnet test -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed (exit $LASTEXITCODE)." }
}
else {
    Write-Warning 'Tests skipped (-SkipTests).'
}

Write-Host 'Release build complete.' -ForegroundColor Green