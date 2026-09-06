<#
.SYNOPSIS
    Generates a dependency license audit and a lightweight SBOM for the shipped app (Phase 10,
    PICKLE.md Phase 10 scope).

.DESCRIPTION
    Runs entirely offline against the local NuGet cache (no network calls) - `dotnet list package`
    only needs the already-restored assets, and each package's license metadata is read from its
    cached .nuspec rather than queried online. Run `dotnet restore` first if the cache is stale.

    Walks the full transitive dependency graph starting from PlaySphere.App.csproj. A single
    project reference graph is enough: `dotnet list package --include-transitive` resolves
    packages pulled in through ProjectReferences too, not just the entry project's own direct
    PackageReferences - verified by inspection (e.g. EF Core/SQLite appear despite being
    referenced only by PlaySphere.Infrastructure/PlaySphere.Library, not PlaySphere.App itself).

    Flags packages whose license is GPL/AGPL/LGPL (copyleft, needs compliance review - LGPL in
    particular has dynamic-linking/relinking obligations relevant to how libvlc is loaded) or
    whose license could not be determined (needs manual lookup) for human review; this script
    does not make a legal determination, only surfaces what needs one.
#>
[CmdletBinding()]
param(
    [string]$Project = (Join-Path $PSScriptRoot '..\src\PlaySphere.App\PlaySphere.App.csproj'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\docs\security')
)

$ErrorActionPreference = 'Stop'

$nugetCache = ((& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
if (-not (Test-Path $nugetCache)) { throw "NuGet global-packages cache not found at $nugetCache" }

$listJson = & dotnet list $Project package --include-transitive --format json
if ($LASTEXITCODE -ne 0) { throw "dotnet list package failed (exit $LASTEXITCODE) - run dotnet restore first." }
$list = $listJson | ConvertFrom-Json

$packages = @{}
foreach ($proj in $list.projects) {
    foreach ($fw in $proj.frameworks) {
        foreach ($pkg in @($fw.topLevelPackages) + @($fw.transitivePackages)) {
            if (-not $pkg) { continue }
            $key = "$($pkg.id)/$($pkg.resolvedVersion)"
            $packages[$key] = [PSCustomObject]@{ Id = $pkg.id; Version = $pkg.resolvedVersion }
        }
    }
}

$results = foreach ($key in ($packages.Keys | Sort-Object)) {
    $pkg = $packages[$key]
    $idLower = $pkg.Id.ToLowerInvariant()
    $nuspecPath = Join-Path $nugetCache "$idLower\$($pkg.Version)\$idLower.nuspec"

    $license = $null
    $authors = $null
    $projectUrl = $null

    if (Test-Path $nuspecPath) {
        [xml]$nuspec = Get-Content -Path $nuspecPath -Raw
        $metadata = $nuspec.package.metadata
        $authors = $metadata.authors
        $projectUrl = if ($metadata.projectUrl) { $metadata.projectUrl } elseif ($metadata.repository) { $metadata.repository.url } else { $null }
        $license = if ($metadata.license -and $metadata.license.'#text') { $metadata.license.'#text' }
                   elseif ($metadata.license.InnerText) { $metadata.license.InnerText }
                   elseif ($metadata.licenseUrl) { $metadata.licenseUrl }
                   else { $null }
    }

    $recognizedPermissive = '^(MIT|Apache-2\.0|BSD-2-Clause|BSD-3-Clause|ISC|MS-PL|0BSD|Unlicense)$'
    $flag = if (-not $license) { 'UNKNOWN - manual review needed' }
            elseif ($license -match 'AGPL') { 'REVIEW - AGPL (strong copyleft)' }
            elseif ($license -match '(?<!L)GPL') { 'REVIEW - GPL (strong copyleft)' }
            elseif ($license -match 'LGPL') { 'REVIEW - LGPL (dynamic-linking/relinking obligations)' }
            elseif ($license -match $recognizedPermissive) { 'OK' }
            else { 'VERIFY - not a recognized SPDX expression (bare URL/EULA/custom text - open the link and check)' }

    [PSCustomObject]@{
        Id = $pkg.Id
        Version = $pkg.Version
        License = if ($license) { $license } else { '(none found)' }
        Authors = $authors
        ProjectUrl = $projectUrl
        Flag = $flag
    }
}

if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

# SBOM: simple custom JSON, not a full CycloneDX/SPDX document - this environment has no network
# access to install those generators. Sufficient to answer "what's in the shipped package and
# under what license" until a proper SBOM toolchain is wired up.
$sbomPath = Join-Path $OutputDir 'sbom.json'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceProjectRelative = (Resolve-Path $Project).Path.Substring($repoRoot.Length + 1) -replace '\\', '/'

[PSCustomObject]@{
    generatedAt = (Get-Date).ToString('o')
    component = 'PlaySphere.App'
    sourceProject = $sourceProjectRelative
    packages = $results
} | ConvertTo-Json -Depth 5 | Set-Content -Path $sbomPath -Encoding utf8
Write-Output "Wrote: $sbomPath ($($results.Count) packages)"

$reviewCount = ($results | Where-Object { $_.Flag -ne 'OK' } | Measure-Object).Count

$md = @()
$md += '# Dependency license audit (Phase 10)'
$md += ''
$md += "Generated by ``tools/generate-license-audit.ps1`` from the NuGet packages actually resolved"
$md += "for ``PlaySphere.App`` (includes packages pulled in transitively through its project"
$md += "references, e.g. EF Core via PlaySphere.Infrastructure) - this is what actually ships in"
$md += "the MSIX package, not just PlaySphere.App's own direct references. Re-run after any"
$md += "dependency change; this file is a snapshot, not something to hand-edit."
$md += ''
$md += "**$reviewCount of $($results.Count) packages flagged for review** (copyleft license or"
$md += "license not determinable from the cached .nuspec - see the Flag column). A flag is not"
$md += "itself a problem; it means a human needs to look, which this script cannot do."
$md += ''
$md += '| Package | Version | License | Flag |'
$md += '|---|---|---|---|'
foreach ($r in $results) {
    $md += "| $($r.Id) | $($r.Version) | $($r.License) | $($r.Flag) |"
}
$md += ''
$md += '## Packages needing review'
$md += ''
$toReview = $results | Where-Object { $_.Flag -ne 'OK' }
if ($toReview.Count -eq 0) {
    $md += 'None.'
} else {
    foreach ($r in $toReview) {
        $md += "- **$($r.Id) $($r.Version)** - $($r.Flag)$(if ($r.ProjectUrl) { " - $($r.ProjectUrl)" })"
    }
}
$mdPath = Join-Path $OutputDir 'dependency-licenses.md'
($md -join "`n") | Set-Content -Path $mdPath -Encoding utf8
Write-Output "Wrote: $mdPath"
