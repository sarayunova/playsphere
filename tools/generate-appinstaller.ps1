<#
.SYNOPSIS
    Generates the .appinstaller file for a release (Phase 10 / ADR-0010's update mechanism).

.DESCRIPTION
    ADR-0010 chose MSIX App Installer (.appinstaller), pointed at this repository's public
    GitHub Releases. Windows polls the Uri on the AppInstaller element for updates, so that
    Uri has to keep resolving after a newer release exists.

    GitHub's stable public pattern is:

        https://github.com/<owner>/<repo>/releases/latest/download/<asset-name>

    /releases/latest/download/<asset-name> looks up that exact file name on whichever release
    GitHub currently marks latest. A version-tagged /releases/download/vX.Y.Z/<asset> URL keeps
    serving the file from that tag, so an installed client would never observe a later release.

    The asset names written into the file are the names the release workflow uploads:
    PlaySphere.App.appinstaller (stable) and the .msix file name produced by packaging (passed
    in, not invented here). Identity Name/Publisher/Version come from Package.appxmanifest so
    they cannot drift from the package Windows will actually install.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Repository,

    # Exact .msix file name uploaded for this release. GitHub matches it literally.
    [Parameter(Mandatory)]
    [string]$MsixFileName,

    # Exact .appinstaller file name uploaded for this release. The output file's leaf name
    # must match, because that is the asset /releases/latest/download/ will look up.
    [string]$AppInstallerFileName = 'PlaySphere.App.appinstaller',

    [string]$ManifestPath = (Join-Path $PSScriptRoot '..' 'src' 'PlaySphere.App' 'Package.appxmanifest'),

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [int]$HoursBetweenUpdateChecks = 24
)

$ErrorActionPreference = 'Stop'

function Get-GitHubLatestDownloadUri {
    param(
        [Parameter(Mandatory)][string]$RepositoryName,
        [Parameter(Mandatory)][string]$AssetName
    )

    # owner/name only. A tag, scheme, or extra path segment would bake a URL that stops
    # tracking the latest release.
    if ($RepositoryName -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        throw "Repository must be an owner/name slug such as sarayunova/playsphere. Got: $RepositoryName"
    }

    # Single file name. /releases/latest/download/ matches this string against the latest
    # release's assets; a path or a version-tagged URL will not resolve.
    if ($AssetName -notmatch '^[A-Za-z0-9._-]+$') {
        throw "Asset name must be a single file name with no directories. Got: $AssetName"
    }

    "https://github.com/$RepositoryName/releases/latest/download/$AssetName"
}

if ($HoursBetweenUpdateChecks -lt 0) {
    throw "HoursBetweenUpdateChecks must be zero or greater. Got: $HoursBetweenUpdateChecks"
}

# Validate the owner/name and both asset names before the extension checks, so a path,
# query string, or version-tagged URL fails as "not a single file name" rather than as a
# wrong extension.
$msixUri = Get-GitHubLatestDownloadUri -RepositoryName $Repository -AssetName $MsixFileName
$appInstallerUri = Get-GitHubLatestDownloadUri -RepositoryName $Repository -AssetName $AppInstallerFileName

if ($MsixFileName -notmatch '\.msix$') {
    throw "MsixFileName must be the uploaded .msix asset name. Got: $MsixFileName"
}

if ($AppInstallerFileName -notmatch '\.appinstaller$') {
    throw "AppInstallerFileName must be the uploaded .appinstaller asset name. Got: $AppInstallerFileName"
}

if ($MsixFileName -eq $AppInstallerFileName) {
    throw "MsixFileName and AppInstallerFileName must be different assets. Got: $MsixFileName"
}

$outputLeaf = Split-Path -Leaf $OutputPath
if ($outputLeaf -ne $AppInstallerFileName) {
    throw "Output file name must be '$AppInstallerFileName' so it matches the stable latest-download asset. Got: $outputLeaf"
}

[xml]$manifest = Get-Content -Path $ManifestPath -Raw
$identity = $manifest.Package.Identity
$name = [string]$identity.Name
$publisher = [string]$identity.Publisher
$version = [string]$identity.Version

if (-not $name -or -not $publisher -or -not $version) {
    throw "Could not read Identity Name/Publisher/Version from $ManifestPath"
}

if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Package identity Version must be quad-dotted (Major.Minor.Build.Revision). Got: $version"
}

# Single-RID (win-x64) app - hardcoded, matching build-release.ps1's -r win-x64.
$processorArchitecture = 'x64'

function ConvertTo-XmlAttributeValue {
    param([string]$Value)
    $escaped = [System.Security.SecurityElement]::Escape($Value)
    if ($null -eq $escaped) { return '' }
    return $escaped
}

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
    xmlns="http://schemas.microsoft.com/appx/appinstaller/2018"
    Version="$(ConvertTo-XmlAttributeValue $version)"
    Uri="$(ConvertTo-XmlAttributeValue $appInstallerUri)">
  <MainPackage
      Name="$(ConvertTo-XmlAttributeValue $name)"
      Publisher="$(ConvertTo-XmlAttributeValue $publisher)"
      Version="$(ConvertTo-XmlAttributeValue $version)"
      Uri="$(ConvertTo-XmlAttributeValue $msixUri)"
      ProcessorArchitecture="$processorArchitecture" />
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="$HoursBetweenUpdateChecks" />
  </UpdateSettings>
</AppInstaller>
"@

$outputDir = Split-Path -Parent $OutputPath
if (-not $outputDir) { $outputDir = '.' }
if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$writtenPath = Join-Path $outputDir $outputLeaf
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($writtenPath, $xml.TrimStart() + "`n", $utf8NoBom)
[xml]$written = Get-Content -LiteralPath $writtenPath -Raw
$ns = New-Object System.Xml.XmlNamespaceManager($written.NameTable)
$ns.AddNamespace('ai', 'http://schemas.microsoft.com/appx/appinstaller/2018')
$root = $written.SelectSingleNode('/ai:AppInstaller', $ns)
$main = $written.SelectSingleNode('/ai:AppInstaller/ai:MainPackage', $ns)
if (-not $root -or -not $main) {
    throw "Generated App Installer XML is missing AppInstaller or MainPackage: $writtenPath"
}
if ($root.Uri -ne $appInstallerUri -or $main.Uri -ne $msixUri) {
    throw "Generated App Installer URIs do not match the stable latest-download URLs."
}
if ($root.Uri -match '/releases/download/' -or $main.Uri -match '/releases/download/') {
    throw "Generated App Installer URI is pinned to a versioned release download URL."
}

Write-Output "Generated: $writtenPath (Name=$name, Version=$version)"
Write-Output "AppInstaller: $appInstallerUri"
Write-Output "MainPackage: $msixUri"
