[CmdletBinding()]
param([Parameter(Mandatory)][string[]]$Targets,
    [string]$ResultPath = 'defender-scan/RESULT.txt')
$ErrorActionPreference = 'Stop'
$scanner = Join-Path $env:ProgramFiles 'Windows Defender/MpCmdRun.exe'
if (-not (Test-Path -LiteralPath $scanner)) { throw 'Windows Defender scanner was not found.' }
# The update can fail transiently on hosted runners (0x80070652: another update is already running), so retry
# before falling back to the signatures already installed on the runner image, and say so in the result.
$signaturesUpdated = $false
foreach ($attempt in 1..5) {
    & $scanner -SignatureUpdate
    if ($LASTEXITCODE -eq 0) { $signaturesUpdated = $true; break }
    Write-Host "Windows Defender signature update failed (attempt $attempt of 5, exit $LASTEXITCODE)."
    if ($attempt -lt 5) { Start-Sleep -Seconds (15 * $attempt) }
}
$signatureNote = if ($signaturesUpdated) { 'signatures updated in this run' } else { 'signature update failed, scanned with the runner image signatures' }
$results = @()
foreach ($target in $Targets) {
    $resolved = (Resolve-Path -LiteralPath $target).Path
    $output = & $scanner -Scan -ScanType 3 -File $resolved -DisableRemediation 2>&1
    $code = $LASTEXITCODE
    $summary = $output -join "`n"
    Write-Host $summary
    if ($code -ne 0 -or ($summary -match '(?im)^\s*Threats:\s*(\d+)\s*$' -and [int]$Matches[1] -ne 0)) {
        throw "Windows Defender did not confirm a clean scan of '$target' (exit $code)."
    }
    $results += "Windows Defender: 0 threats detected in $target ($signatureNote)."
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ResultPath) | Out-Null
$results | Set-Content -LiteralPath $ResultPath -Encoding utf8NoBOM
