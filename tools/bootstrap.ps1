# Restore dependencies and prepare the repository for a local build.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Write-Host '== PlaySphere bootstrap ==' -ForegroundColor Cyan

& (Join-Path $PSScriptRoot 'verify-environment.ps1')

& dotnet restore
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed (exit $LASTEXITCODE)." }

Write-Host 'Bootstrap complete. Next: tools\build-release.ps1' -ForegroundColor Green