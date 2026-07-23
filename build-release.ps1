$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $ProjectRoot "OpenSteamTool.FloatingInstaller.csproj"
$PublishDir = Join-Path $ProjectRoot "publish\win-x64"
$PackageDir = Join-Path $ProjectRoot "package"
$ZipPath = Join-Path $ProjectRoot "OST-Floating-Installer.zip"

Remove-Item -Recurse -Force $PublishDir, $PackageDir, $ZipPath -ErrorAction SilentlyContinue

dotnet publish $Project -c Release -r win-x64 --self-contained true -o $PublishDir

New-Item -ItemType Directory -Force $PackageDir | Out-Null
Copy-Item -Recurse -Force (Join-Path $PublishDir "*") $PackageDir

$FilesDir = Join-Path $PackageDir "files"
New-Item -ItemType Directory -Force $FilesDir | Out-Null

if (Test-Path (Join-Path $ProjectRoot "files")) {
    Copy-Item -Recurse -Force (Join-Path $ProjectRoot "files\*") $FilesDir
}

Compress-Archive -Force -Path (Join-Path $PackageDir "*") -DestinationPath $ZipPath
Write-Host "Created $ZipPath"
