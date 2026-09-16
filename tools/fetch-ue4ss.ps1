# Downloads the pinned UE4SS package described by live-agent/ue4ss/runtime.json into
# live-agent/ue4ss/UE4SS.zip, verifying its size and SHA-256 against that manifest.
#
# The editor never downloads UE4SS at runtime - the release build bundles whatever this script
# fetches (see AbioticEditor.Web.csproj's live-agent ItemGroup and Ue4ssBundledRuntime). This
# script is a maintainer/CI step, not something a player ever runs.
#
# The runtime.json `url` points at the "experimental-latest" tag, which upstream moves as new
# builds land. That means a re-run of this script CAN legitimately start failing the hash check
# once upstream replaces the asset behind that tag - that is not a bug in this script. When it
# happens, a maintainer needs to: download the new build, verify it works in game, update
# runtime.json's version/asset/url/sha256/size fields, and commit that manifest. UE4SS.zip itself
# is gitignored; CI re-fetches it (see docs/reference/maintainer-commands.md, "Bumping the
# bundled UE4SS").
[CmdletBinding()]
param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\live-agent\ue4ss\runtime.json')
)

$ErrorActionPreference = 'Stop'

$manifestPath = [IO.Path]::GetFullPath($ManifestPath)
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "UE4SS pin manifest not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($field in 'url', 'sha256', 'size', 'version') {
    if (-not $manifest.$field) { throw "runtime.json is missing required field '$field'." }
}

$ue4ssDir = Split-Path -Parent $manifestPath
$packagePath = Join-Path $ue4ssDir 'UE4SS.zip'
$expectedSize = [int64]$manifest.size
$expectedSha256 = [string]$manifest.sha256

function Test-PinnedPackage {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $actualSize = (Get-Item -LiteralPath $Path).Length
    if ($actualSize -ne $expectedSize) { return $false }
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    return $actualHash.Equals($expectedSha256, [StringComparison]::OrdinalIgnoreCase)
}

if (Test-PinnedPackage -Path $packagePath) {
    Write-Host "UE4SS $($manifest.version) already present and verified: $packagePath"
    return
}

Write-Host "Fetching UE4SS $($manifest.version) from $($manifest.url) ..."
$downloadPath = "$packagePath.download"
if (Test-Path -LiteralPath $downloadPath) { Remove-Item -LiteralPath $downloadPath -Force }
try {
    Invoke-WebRequest -Uri $manifest.url -OutFile $downloadPath -UseBasicParsing
}
catch {
    if (Test-Path -LiteralPath $downloadPath) { Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue }
    throw "Could not download the pinned UE4SS package from $($manifest.url): $_"
}

if (-not (Test-PinnedPackage -Path $downloadPath)) {
    $actualSize = if (Test-Path -LiteralPath $downloadPath) { (Get-Item -LiteralPath $downloadPath).Length } else { -1 }
    $actualHash = if (Test-Path -LiteralPath $downloadPath) { (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash } else { '(no file)' }
    Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
    throw @"
Downloaded UE4SS package did not match live-agent/ue4ss/runtime.json.
  expected size:   $expectedSize
  actual size:     $actualSize
  expected sha256: $expectedSha256
  actual sha256:   $actualHash
The 'experimental-latest' tag this manifest points at is rolling: upstream may have replaced the
asset behind it. A maintainer must download the new build, verify it works in game, and update
runtime.json (version/asset/url/sha256/size) before this will pass again.
"@
}

Move-Item -LiteralPath $downloadPath -Destination $packagePath -Force
Write-Host "UE4SS $($manifest.version) fetched and verified: $packagePath"
