[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern("^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$")]
    [string]$Version = "0.3.0",

    [Parameter()]
    [switch]$SkipPreviousRelease
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $ProjectRoot "TOST.csproj"
$PublishDir = Join-Path $ProjectRoot "artifacts\publish\win-x64"
$ReleaseDir = Join-Path $ProjectRoot "Releases"
$RepositoryUrl = "https://github.com/sadabx/OST"

function Assert-LastExitCode([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

Remove-Item -Recurse -Force $PublishDir, $ReleaseDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $PublishDir, $ReleaseDir | Out-Null

dotnet tool restore
Assert-LastExitCode "Velopack tool restore"

if (-not $SkipPreviousRelease) {
    dotnet vpk download github --repoUrl $RepositoryUrl --outputDir $ReleaseDir
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "No previous Velopack release was downloaded. Continuing without a delta package."
        Remove-Item -Recurse -Force $ReleaseDir
        New-Item -ItemType Directory -Force $ReleaseDir | Out-Null
    }
}

dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $PublishDir
Assert-LastExitCode "TOST publish"

Copy-Item (Join-Path $ProjectRoot "README.md") $PublishDir
Copy-Item (Join-Path $ProjectRoot "LICENSE") $PublishDir

dotnet vpk pack `
    --packId TOST `
    --packVersion $Version `
    --packDir $PublishDir `
    --mainExe TOST.exe `
    --packTitle "TOST" `
    --packAuthors "Trionine" `
    --icon (Join-Path $ProjectRoot "Assets\opensteamtool.ico") `
    --releaseNotes (Join-Path $ProjectRoot "release-notes.md") `
    --instLicense (Join-Path $ProjectRoot "LICENSE") `
    --instReadme (Join-Path $ProjectRoot "README.md") `
    --runtime win-x64 `
    --outputDir $ReleaseDir
Assert-LastExitCode "Velopack packaging"

Write-Host ""
Write-Host "TOST $Version release assets:"
Get-ChildItem $ReleaseDir | Select-Object Name, Length
