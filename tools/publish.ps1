# Builds and verifies a distributable self-contained Razor host.
# Output: publish/AbioticEditor-desktop-<rid>/ and its versioned zip.
[CmdletBinding()]
param(
    [string]$Version = "0.5.0",
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$SkipSmoke
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/AbioticEditor.Web/AbioticEditor.Web.csproj'
$publishRoot = Join-Path $root 'publish'
$outDir = Join-Path $publishRoot "AbioticEditor-desktop-$RuntimeIdentifier"
$zip = Join-Path $publishRoot "AbioticEditor-desktop-$RuntimeIdentifier-v$Version.zip"

$resolvedPublishRoot = [IO.Path]::GetFullPath($publishRoot)
$resolvedOutDir = [IO.Path]::GetFullPath($outDir)
if (-not $resolvedOutDir.StartsWith($resolvedPublishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to publish outside $resolvedPublishRoot"
}

Write-Host "Publishing $project to $outDir"
if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

dotnet publish $project `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if ($RuntimeIdentifier -eq 'win-x64') {
    & (Join-Path $PSScriptRoot 'verify-web-host.ps1') -PublishDir $outDir -SkipSmoke:$SkipSmoke
    if (-not $?) { throw "Windows host verification failed" }
} else {
    if (-not (Get-Command bash -ErrorAction SilentlyContinue)) {
        throw "bash is required to verify the Linux desktop package"
    }
    bash (Join-Path $PSScriptRoot 'verify-web-host-linux.sh') $outDir
    if ($LASTEXITCODE -ne 0) { throw "Linux host verification failed" }
}

if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
if ($RuntimeIdentifier -eq 'win-x64') {
    Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $zip
} else {
    if (-not (Get-Command zip -ErrorAction SilentlyContinue)) {
        throw "zip is required to preserve Linux launcher permissions"
    }
    Push-Location $outDir
    try {
        zip -r $zip .
        if ($LASTEXITCODE -ne 0) { throw "zip failed" }
    }
    finally {
        Pop-Location
    }
}
Write-Host "Created $zip ($([Math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)) MB)"
