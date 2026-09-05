<#
.SYNOPSIS
    Creates (or reuses) a self-signed code-signing certificate for local MSIX sideload testing.

.DESCRIPTION
    LOCAL/SIDELOAD TESTING ONLY. This certificate is not issued by a trusted CA, will trigger
    SmartScreen warnings, and cannot be used for Microsoft Store submission. Its Subject must
    exactly match Package.appxmanifest's Identity/Publisher (CN=sarayunova) or the resulting
    package will fail to install. See docs/security/code-signing.md.

    The exported .pfx is written next to this script (tools/) and is gitignored - never commit
    it or its password. Each developer runs this once locally to get their own dev cert.
#>
[CmdletBinding()]
param(
    [string]$Subject = "CN=sarayunova",
    [string]$PfxPath,
    [System.Security.SecureString]$Password
)

$ErrorActionPreference = "Stop"

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $PfxPath) { $PfxPath = Join-Path $scriptRoot "dev-signing-cert.pfx" }
if (-not $Password) { $Password = ConvertTo-SecureString -String "devcert" -Force -AsPlainText }

$existing = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey }
if ($existing) {
    Write-Output "Reusing existing certificate: $($existing.Thumbprint) ($Subject)"
    $cert = $existing | Select-Object -First 1
}
else {
    Write-Output "Creating new self-signed code-signing certificate for $Subject ..."
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Subject `
        -KeyUsage DigitalSignature `
        -FriendlyName "PlaySphere dev signing certificate (sideload testing only)" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}Subject Type:End Entity")
    Write-Output "Created: $($cert.Thumbprint)"
}

Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $Password | Out-Null
Write-Output "Exported PFX to: $PfxPath (default password 'devcert' unless -Password was supplied)"
Write-Output ""
Write-Output "This certificate is for LOCAL SIDELOAD TESTING ONLY - see docs/security/code-signing.md."
Write-Output "To install a package signed with it on a test machine, first trust the certificate:"
Write-Output "  Import-Certificate -FilePath <exported .cer> -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
