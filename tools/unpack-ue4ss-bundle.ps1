[CmdletBinding()]
param([Parameter(Mandatory)][string]$BundleDir)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($BundleDir)
$package = Join-Path $root 'UE4SS.zip'
if (-not (Test-Path -LiteralPath $package)) { return }
$manifestPath = Join-Path $root 'runtime.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ((Get-Item -LiteralPath $package).Length -ne $manifest.size -or
    (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $manifest.sha256) {
    throw 'Bundled UE4SS archive does not match its manifest.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
$required = @('dwmapi.dll', 'ue4ss/UE4SS.dll', 'ue4ss/LICENSE', 'ue4ss/UE4SS-settings.ini',
    'ue4ss/Mods/shared/UEHelpers/UEHelpers.lua')
$hashes = [ordered]@{}
try {
    $files = @($archive.Entries | Where-Object { $_.Name.Length -gt 0 })
    if ($files.Count -gt 1000 -or ($files | Measure-Object Length -Sum).Sum -gt 200MB) {
        throw 'Unexpected UE4SS archive size.'
    }
    foreach ($file in $files) {
        if ($file.FullName -match '(^/|\\|:|(^|/)\.{1,2}(/|$))') { throw 'Unsafe UE4SS archive path.' }
    }
    foreach ($name in $required) {
        if (@($files | Where-Object FullName -CEQ $name).Count -ne 1) { throw "Missing or duplicate UE4SS file: $name" }
    }
    foreach ($file in $files) {
        $name = $file.FullName
        if ($name -cnotin $required -and -not $name.StartsWith('ue4ss/Mods/shared/') -and
            -not $name.StartsWith('ue4ss/UE4SS_SDK_Backends/')) { continue }
        if ($hashes.Contains($name)) { throw "Duplicate UE4SS file: $name" }
        $target = Join-Path (Join-Path $root 'files') $name
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($file, $target, $true)
        $hashes[$name] = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    }
} finally { $archive.Dispose() }
$manifest | Add-Member -NotePropertyName files -NotePropertyValue $hashes -Force
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Remove-Item -LiteralPath $package
Write-Host "Prepared $($hashes.Count) unpacked UE4SS files with checksums."
