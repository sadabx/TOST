$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ProjectRoot "..\..")
$Project = Join-Path $ProjectRoot "OpenSteamTool.FloatingInstaller.csproj"
$PublishDir = Join-Path $ProjectRoot "publish\win-x64"
$PackageDir = Join-Path $ProjectRoot "package"
$ZipPath = Join-Path $ProjectRoot "OST-Floating-Installer.zip"

Remove-Item -Recurse -Force $PublishDir, $PackageDir, $ZipPath -ErrorAction SilentlyContinue

dotnet publish $Project -c Release -r win-x64 --self-contained true -o $PublishDir

New-Item -ItemType Directory -Force $PackageDir | Out-Null
Copy-Item -Recurse -Force (Join-Path $PublishDir "*") $PackageDir

$BuiltDllRoot = Join-Path $RepoRoot "build\src\Release"
$FilesDir = Join-Path $PackageDir "files"
New-Item -ItemType Directory -Force $FilesDir | Out-Null

foreach ($Name in @("OpenSteamTool.dll", "dwmapi.dll", "xinput1_4.dll")) {
    $Candidate = Join-Path $BuiltDllRoot $Name
    if (Test-Path $Candidate) {
        Copy-Item -Force $Candidate $FilesDir
    }
}

$ExampleConfig = Join-Path $RepoRoot "opensteamtool.example.toml"
if (Test-Path $ExampleConfig) {
    Copy-Item -Force $ExampleConfig (Join-Path $FilesDir "opensteamtool.toml")
}

Compress-Archive -Force -Path (Join-Path $PackageDir "*") -DestinationPath $ZipPath
Write-Host "Created $ZipPath"
