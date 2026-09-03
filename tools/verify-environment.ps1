# Verify the build environment for LiVi PlaySphere Studio.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Write-Host '== LiVi PlaySphere Studio - environment verification ==' -ForegroundColor Cyan

$repo = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path (Join-Path $repo 'global.json'))) {
    throw 'global.json not found at repository root.'
}
Write-Host 'global.json present.'

$sdkVersion = & dotnet --version 2>$null
if (-not $sdkVersion) {
    throw 'dotnet CLI not found on PATH. Install the .NET 10 SDK.'
}
$major = [int](($sdkVersion -split '\.')[0])
Write-Host "dotnet SDK: $sdkVersion"
if ($major -lt 10) {
    throw ".NET 10 SDK is required (found $sdkVersion)."
}

try {
    $meta = Invoke-RestMethod -Uri 'https://api.nuget.org/v3/index.json' -TimeoutSec 15
    Write-Host "NuGet reachable: $($meta.version)"
}
catch {
    Write-Warning "NuGet unreachable - restore may fail: $($_.Exception.Message)"
}

Write-Host 'Environment OK.' -ForegroundColor Green