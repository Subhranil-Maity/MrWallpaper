# Publishes MrWallpaper (Release, x64, self-contained) and packages it into an MSI installer.
# Output: artifacts\installer\MrWallpaper-Setup.msi
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publish = Join-Path $root 'artifacts\publish'
$installer = Join-Path $root 'artifacts\installer'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish (Join-Path $root 'MrWallpaper\MrWallpaper.csproj') -c Release -p:Platform=x64 -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

dotnet build (Join-Path $root 'Installer\MrWallpaper.Installer.wixproj') -c Release -p:PublishDir="$publish\" -o $installer
if ($LASTEXITCODE) { throw 'Installer build failed' }

Get-Item (Join-Path $installer 'MrWallpaper-Setup.msi')
