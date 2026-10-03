[CmdletBinding()]
param([Parameter(Mandatory)][string[]]$Targets,
    [string]$ResultPath = 'defender-scan/RESULT.txt')
$ErrorActionPreference = 'Stop'
$scanner = Join-Path $env:ProgramFiles 'Windows Defender/MpCmdRun.exe'
if (-not (Test-Path -LiteralPath $scanner)) { throw 'Windows Defender scanner was not found.' }
& $scanner -SignatureUpdate
if ($LASTEXITCODE -ne 0) { throw 'Windows Defender signature update failed.' }
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
    $results += "Windows Defender: 0 threats detected in $target (signatures updated in this run)."
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ResultPath) | Out-Null
$results | Set-Content -LiteralPath $ResultPath -Encoding utf8NoBOM
