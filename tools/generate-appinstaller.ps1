<#
.SYNOPSIS
    Generates the .appinstaller file for a release (Phase 10 / ADR-0010's update mechanism).

.DESCRIPTION
    ADR-0010 chose MSIX App Installer (.appinstaller), pointed at this repository's GitHub
    Releases, as the update mechanism - no Microsoft Store account is confirmed to exist for this
    project, and App Installer needs only a stable download URL, which GitHub Releases provides.

    An .appinstaller file must carry the exact Identity Name/Publisher/Version of the package it
    describes, or Windows will refuse to install/update through it. Those come from
    Package.appxmanifest here rather than being retyped, so they can never drift out of sync.

    Intended to be run by .github/workflows/release.yml after packaging, once the release's tag
    (and therefore its asset URLs) are known.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$MsixUri,

    [Parameter(Mandatory)]
    [string]$AppInstallerUri,

    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\src\PlaySphere.App\Package.appxmanifest'),

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [int]$HoursBetweenUpdateChecks = 24
)

$ErrorActionPreference = 'Stop'

[xml]$manifest = Get-Content -Path $ManifestPath -Raw
$identity = $manifest.Package.Identity
$name = $identity.Name
$publisher = $identity.Publisher
$version = $identity.Version

if (-not $name -or -not $publisher -or -not $version) {
    throw "Could not read Identity Name/Publisher/Version from $ManifestPath"
}

# Single-RID (win-x64) app - hardcoded, matching build-release.ps1's -r win-x64.
$processorArchitecture = 'x64'

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
    xmlns="http://schemas.microsoft.com/appx/appinstaller/2018"
    Version="$version"
    Uri="$AppInstallerUri">
  <MainPackage
      Name="$name"
      Publisher="$publisher"
      Version="$version"
      Uri="$MsixUri"
      ProcessorArchitecture="$processorArchitecture" />
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="$HoursBetweenUpdateChecks" />
  </UpdateSettings>
</AppInstaller>
"@

$outputDir = Split-Path -Parent $OutputPath
if ($outputDir -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}
Set-Content -Path $OutputPath -Value $xml -Encoding utf8
Write-Output "Generated: $OutputPath (Name=$name, Version=$version)"
