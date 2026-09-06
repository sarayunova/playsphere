# Build and test the Release configuration. Runs the Phase 0 gate commands.
# -Package additionally produces a signed MSIX (Phase 10 / ADR-0010).
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$Package,
    [switch]$SkipSign,
    [string]$PfxPath = (Join-Path $PSScriptRoot 'dev-signing-cert.pfx'),
    [string]$CerPath = (Join-Path $PSScriptRoot 'dev-signing-cert.cer'),
    [System.Security.SecureString]$PfxPassword
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

if ($Package) {
    Write-Host '== Packaging MSIX ==' -ForegroundColor Cyan
    $appProject = Join-Path $PSScriptRoot '..\src\PlaySphere.App\PlaySphere.App.csproj'
    $packageDir = Join-Path $PSScriptRoot '..\src\PlaySphere.App\bin\Release\AppPackages\'

    # The MSIX packaging target deletes and recreates its localized Add-AppDevPackage.resources
    # output on every run. Real-time antivirus scanning of the freshly-written .psd1 resource
    # files can hold a sub-second lock on the first one (alphabetically cs-CZ) at exactly the
    # moment the target tries to remove it, failing with MSB3231 "Access is denied". This is a
    # known transient OS-level race, not a build correctness issue - retry a few times.
    $maxAttempts = 3
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        & dotnet publish $appProject -c Release -r win-x64 `
            -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageDir=$packageDir
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -eq $maxAttempts) { throw "MSIX packaging failed after $maxAttempts attempts (exit $LASTEXITCODE)." }
        Write-Warning "MSIX packaging failed (exit $LASTEXITCODE), attempt $attempt/$maxAttempts - retrying (likely a transient antivirus file lock)."
        Start-Sleep -Seconds 3
    }

    $msix = Get-ChildItem -Path $packageDir -Filter '*.msix' -Recurse | Select-Object -First 1
    if (-not $msix) { throw "No .msix produced under $packageDir" }
    Write-Host "Produced: $($msix.FullName)" -ForegroundColor Green

    if ($SkipSign) {
        Write-Warning 'Signing skipped (-SkipSign). Package is UNSIGNED and will not install.'
    }
    elseif (-not (Test-Path $PfxPath)) {
        Write-Warning "No signing certificate at $PfxPath - run tools/create-dev-signing-cert.ps1 first. Package is UNSIGNED."
    }
    else {
        # signtool.exe may come from a standalone Windows SDK install, or (as when only the
        # Windows App SDK / MSIX build tools are present) from the microsoft.windows.sdk.buildtools
        # NuGet package already restored for packaging - check both.
        $nugetGlobalPackages = (& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
        $signtoolRoots = @(
            'C:\Program Files (x86)\Windows Kits\10\bin',
            'C:\Program Files\Windows Kits\10\bin',
            (Join-Path $nugetGlobalPackages.Trim() 'microsoft.windows.sdk.buildtools'),
            'C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools'
        ) | Where-Object { $_ -and (Test-Path $_) }

        $signtool = $signtoolRoots |
            ForEach-Object { Get-ChildItem -Path $_ -Filter 'signtool.exe' -Recurse -ErrorAction SilentlyContinue } |
            Where-Object { $_.FullName -like '*\x64\*' } | Select-Object -First 1
        if (-not $signtool) { throw "signtool.exe not found under the Windows SDK or microsoft.windows.sdk.buildtools NuGet package - install the Windows SDK." }

        if (-not $PfxPassword) { $PfxPassword = ConvertTo-SecureString -String 'devcert' -Force -AsPlainText }
        $plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword))
        & $signtool.FullName sign /fd SHA256 /a /f $PfxPath /p $plainPassword $msix.FullName
        if ($LASTEXITCODE -ne 0) { throw "signtool sign failed (exit $LASTEXITCODE)." }
        Write-Host "Signed: $($msix.FullName)" -ForegroundColor Green
        Write-Host 'This is a self-signed DEV certificate - see docs/security/code-signing.md.' -ForegroundColor Yellow

        # Add-AppDevPackage.ps1 (generated alongside the .msix) needs exactly one .cer next to
        # it to auto-trust the certificate on install - copy ours there so it's found.
        if (Test-Path $CerPath) {
            Copy-Item -Path $CerPath -Destination $msix.DirectoryName -Force
            Write-Host "Copied dev certificate to: $(Join-Path $msix.DirectoryName (Split-Path $CerPath -Leaf))" -ForegroundColor Green
        }
        else {
            Write-Warning "No .cer at $CerPath - Add-AppDevPackage.ps1 won't be able to auto-trust this package. Run tools/create-dev-signing-cert.ps1 to generate one."
        }
    }
}

Write-Host 'Release build complete.' -ForegroundColor Green