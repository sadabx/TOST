[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern("^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$")]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ReleaseDir = Join-Path $ProjectRoot "Releases"
$RepositoryUrl = "https://github.com/sadabx/OST"

if (-not $env:GITHUB_TOKEN) {
    throw "Set GITHUB_TOKEN to a token with Releases write access before publishing."
}

if (-not (Test-Path $ReleaseDir)) {
    throw "Releases directory not found. Run build-release.ps1 first."
}

dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    throw "Velopack tool restore failed."
}

dotnet vpk upload github `
    --repoUrl $RepositoryUrl `
    --token $env:GITHUB_TOKEN `
    --outputDir $ReleaseDir `
    --tag "v$Version" `
    --releaseName "TOST v$Version" `
    --publish true

if ($LASTEXITCODE -ne 0) {
    throw "GitHub release upload failed."
}

Write-Host "Published TOST v$Version to $RepositoryUrl/releases"
