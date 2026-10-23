<#
.SYNOPSIS
    Checks that generate-appinstaller.ps1 emits a valid .appinstaller pointed at
    GitHub's public latest-download URLs, using the asset names it was given.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$generator = Join-Path $PSScriptRoot 'generate-appinstaller.ps1'
$manifestPath = Join-Path $PSScriptRoot '..' 'src' 'PlaySphere.App' 'Package.appxmanifest'
$repo = 'sarayunova/playsphere'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("playsphere-appinstaller-" + [guid]::NewGuid().ToString('n'))

function Assert-Equal {
    param($Actual, $Expected, [string]$Message)
    if ($Actual -ne $Expected) {
        throw "$Message`n  expected: $Expected`n  actual:   $Actual"
    }
}

function Assert-Throws {
    param([scriptblock]$Action, [string]$Because)
    $threw = $false
    try {
        & $Action | Out-Null
    }
    catch {
        $threw = $true
        if ($Because -and ($_.Exception.Message -notmatch [regex]::Escape($Because))) {
            throw "Expected failure mentioning '$Because' but got: $($_.Exception.Message)"
        }
    }
    if (-not $threw) {
        throw "Expected failure mentioning '$Because', but the generator succeeded."
    }
}

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    $identity = $manifest.Package.Identity
    $msixName = 'PlaySphere.App_0.10.0.0_x64.msix'
    $out = Join-Path $tempRoot 'PlaySphere.App.appinstaller'

    & $generator -Repository $repo -MsixFileName $msixName -OutputPath $out -ManifestPath $manifestPath -HoursBetweenUpdateChecks 24 | Out-Null

    $bytes = [System.IO.File]::ReadAllBytes($out)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw "App Installer file must be UTF-8 without a BOM."
    }

    $text = [System.IO.File]::ReadAllText($out)
    if ($text -match '/releases/download/') {
        throw "App Installer file still contains a version-tagged /releases/download/ URI:`n$text"
    }

    [xml]$doc = Get-Content -LiteralPath $out -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
    $ns.AddNamespace('ai', 'http://schemas.microsoft.com/appx/appinstaller/2018')
    $root = $doc.SelectSingleNode('/ai:AppInstaller', $ns)
    $main = $doc.SelectSingleNode('/ai:AppInstaller/ai:MainPackage', $ns)
    $onLaunch = $doc.SelectSingleNode('/ai:AppInstaller/ai:UpdateSettings/ai:OnLaunch', $ns)
    if (-not $root -or -not $main -or -not $onLaunch) {
        throw "Generated XML is missing AppInstaller, MainPackage, or OnLaunch."
    }

    $expectedAppInstaller = "https://github.com/$repo/releases/latest/download/PlaySphere.App.appinstaller"
    $expectedMsix = "https://github.com/$repo/releases/latest/download/$msixName"
    Assert-Equal $root.Uri $expectedAppInstaller 'AppInstaller Uri'
    Assert-Equal $main.Uri $expectedMsix 'MainPackage Uri'
    Assert-Equal $root.Version ([string]$identity.Version) 'AppInstaller Version'
    Assert-Equal $main.Name ([string]$identity.Name) 'MainPackage Name'
    Assert-Equal $main.Publisher ([string]$identity.Publisher) 'MainPackage Publisher'
    Assert-Equal $main.Version ([string]$identity.Version) 'MainPackage Version'
    Assert-Equal $main.ProcessorArchitecture 'x64' 'ProcessorArchitecture'
    Assert-Equal $onLaunch.HoursBetweenUpdateChecks '24' 'HoursBetweenUpdateChecks'

    # The package URL must use the uploaded file name, even when that name's version
    # differs from whatever the manifest happens to say. Callers pass the real asset.
    $renamed = 'PlaySphere.App_9.8.7.6_x64.msix'
    $renamedOut = Join-Path $tempRoot 'custom' 'PlaySphere.App.appinstaller'
    & $generator -Repository $repo -MsixFileName $renamed -OutputPath $renamedOut -HoursBetweenUpdateChecks 0 | Out-Null
    [xml]$renamedDoc = Get-Content -LiteralPath $renamedOut -Raw
    $renamedNs = New-Object System.Xml.XmlNamespaceManager($renamedDoc.NameTable)
    $renamedNs.AddNamespace('ai', 'http://schemas.microsoft.com/appx/appinstaller/2018')
    $renamedMain = $renamedDoc.SelectSingleNode('/ai:AppInstaller/ai:MainPackage', $renamedNs)
    $renamedLaunch = $renamedDoc.SelectSingleNode('/ai:AppInstaller/ai:UpdateSettings/ai:OnLaunch', $renamedNs)
    Assert-Equal $renamedMain.Uri "https://github.com/$repo/releases/latest/download/$renamed" 'Renamed MainPackage Uri'
    Assert-Equal $renamedLaunch.HoursBetweenUpdateChecks '0' 'Zero HoursBetweenUpdateChecks means check on every launch'

    Assert-Throws { & $generator -Repository 'sarayunova/playsphere/releases/download/v1.2.3' -MsixFileName $msixName -OutputPath $out } 'owner/name'
    Assert-Throws { & $generator -Repository 'https://github.com/sarayunova/playsphere' -MsixFileName $msixName -OutputPath $out } 'owner/name'
    Assert-Throws { & $generator -Repository $repo -MsixFileName '../PlaySphere.App_0.10.0.0_x64.msix' -OutputPath $out } 'single file name'
    Assert-Throws { & $generator -Repository $repo -MsixFileName 'PlaySphere.App_0.10.0.0_x64.msix?raw=1' -OutputPath $out } 'single file name'
    Assert-Throws { & $generator -Repository $repo -MsixFileName 'PlaySphere.App.appinstaller' -OutputPath $out } '.msix'
    Assert-Throws { & $generator -Repository $repo -MsixFileName $msixName -OutputPath (Join-Path $tempRoot 'Other.appinstaller') } 'PlaySphere.App.appinstaller'
    Assert-Throws { & $generator -Repository $repo -MsixFileName $msixName -OutputPath $out -HoursBetweenUpdateChecks -1 } 'zero or greater'

    Write-Output "App Installer generation checks passed."
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
