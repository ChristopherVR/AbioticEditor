# Fetches whatever UE4SS build the "experimental-latest" upstream tag currently publishes into
# live-agent/ue4ss/UE4SS.zip, and rewrites live-agent/ue4ss/runtime.json to record exactly what
# was fetched (version/asset/url/sha256/size).
#
# The editor never downloads UE4SS at runtime - the release build bundles whatever this script
# fetches (see AbioticEditor.Web.csproj's live-agent ItemGroup and Ue4ssBundledRuntime). This
# script is a maintainer/CI step, not something a player ever runs.
#
# Upstream's "experimental-latest" tag is rolling: it always points at whichever build is newest,
# and the asset file name changes (and the old one disappears) every time a new one lands. This
# script previously pinned an exact file name + SHA-256 in runtime.json and failed loudly once
# upstream moved past it, requiring a maintainer to manually re-verify and bump the pin before CI
# would pass again. By decision, it now always follows upstream instead: it asks the GitHub API
# for whatever asset is live under the tag right now, downloads it, and rewrites runtime.json to
# match. There is no pin to go stale, so a rename upstream can no longer break the build.
# Ue4ssBundledRuntime's own SHA-256 check at install time still guards a player against a
# corrupted or truncated download; it just no longer guards against upstream shipping a build a
# maintainer hasn't personally re-verified in game (see docs/reference/maintainer-commands.md).
[CmdletBinding()]
param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\live-agent\ue4ss\runtime.json'),
    [string]$Repo = 'UE4SS-RE/RE-UE4SS',
    [string]$Tag = 'experimental-latest'
)

$ErrorActionPreference = 'Stop'

$manifestPath = [IO.Path]::GetFullPath($ManifestPath)
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "UE4SS manifest not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$ue4ssDir = Split-Path -Parent $manifestPath
$packagePath = Join-Path $ue4ssDir 'UE4SS.zip'

$headers = @{ 'User-Agent' = 'AbioticEditor-fetch-ue4ss' }
if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)" }

Write-Host "Looking up the current '$Tag' UE4SS release for $Repo ..."
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers $headers -UseBasicParsing

$candidates = @($release.assets | Where-Object { $_.name -match '^UE4SS_v.*\.zip$' })
if ($candidates.Count -ne 1) {
    $seen = ($release.assets | ForEach-Object Name) -join ', '
    throw "Expected exactly one 'UE4SS_v*.zip' asset on the '$Tag' release, found $($candidates.Count): $seen"
}
$asset = $candidates[0]
$version = $asset.name -replace '^UE4SS_', '' -replace '\.zip$', ''

if ((Test-Path -LiteralPath $packagePath -PathType Leaf) -and $manifest.asset -eq $asset.name `
        -and (Get-Item -LiteralPath $packagePath).Length -eq $asset.size) {
    Write-Host "UE4SS $version already present and matches the latest '$Tag' asset: $packagePath"
    return
}

Write-Host "Fetching UE4SS $version from $($asset.browser_download_url) ..."
$downloadPath = "$packagePath.download"
if (Test-Path -LiteralPath $downloadPath) { Remove-Item -LiteralPath $downloadPath -Force }
try {
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $downloadPath -Headers $headers -UseBasicParsing
}
catch {
    if (Test-Path -LiteralPath $downloadPath) { Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue }
    throw "Could not download the UE4SS package from $($asset.browser_download_url): $_"
}

$actualSize = (Get-Item -LiteralPath $downloadPath).Length
if ($actualSize -ne $asset.size) {
    Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
    throw "Downloaded UE4SS package size ($actualSize bytes) did not match GitHub's reported size ($($asset.size) bytes); the download was likely truncated or corrupted."
}
$actualSha256 = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash

Move-Item -LiteralPath $downloadPath -Destination $packagePath -Force
Write-Host "UE4SS $version fetched and verified: $packagePath"

$manifest.version = $version
$manifest.asset = $asset.name
$manifest.url = $asset.browser_download_url
$manifest.sha256 = $actualSha256
$manifest.size = [int64]$actualSize
$manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Host "Updated $manifestPath to record UE4SS $version."
