# Builds a distributable (unpackaged) Release build of the Abiotic Factor save editor
# and zips it. Output: dotnet/publish/AbioticEditor/ + dotnet/publish/AbioticEditor-vX.zip
#
# Usage:  pwsh tools/publish.ps1 [-Version 0.3.0]
param(
    [string]$Version = "0.5.0"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/AbioticEditor.App/AbioticEditor.App.csproj'
$outDir = Join-Path $root 'publish/AbioticEditor'
$zip = Join-Path $root "publish/AbioticEditor-v$Version-win-x64.zip"

Write-Host "Publishing $project → $outDir"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

dotnet publish $project `
    -f net10.0-windows10.0.19041.0 `
    -c Release `
    -p:WindowsPackageType=None `
    -p:RuntimeIdentifierOverride=win-x64 `
    -p:WindowsAppSDKSelfContained=true `
    -p:SelfContained=true `
    -o $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Ship a README next to the exe.
@"
Abiotic Factor Save Editor v$Version
====================================

Run AbioticEditor.App.exe and open your save folder, e.g.
  %LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<steamid>\Worlds\<WorldName>

Item/skill/recipe names, icons and lore are read from your local Abiotic
Factor install (Steam). For full data, place a .usmap mappings file at:
  %LOCALAPPDATA%\AbioticEditor\mappings\Mappings.usmap

Every save write keeps a .bak backup next to the file.
"@ | Set-Content (Join-Path $outDir 'README.txt')

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$outDir/*" -DestinationPath $zip
Write-Host "Created $zip ($([Math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
