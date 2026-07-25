[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDir,
    [switch]$SkipSmoke
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PublishDir)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Publish directory does not exist: $root" }

$required = @(
    'AbioticEditor.Web.exe',
    'AbioticEditor.Web.dll',
    'Photino.Native.dll',
    'WebView2Loader.dll',
    'THIRD-PARTY-NOTICES.txt',
    'Mappings.usmap',
    'wwwroot',
    'Templates\blank-world-template.sav',
    'Templates\blank-player-template.sav'
)
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relative))) { throw "Published Windows host is missing '$relative'." }
}

if ($SkipSmoke) { Write-Host "Windows host publish layout verified: $root"; return }

$port = 37261
$url = "http://127.0.0.1:$port"
$oldUrl = $env:ABIOTIC_EDITOR_URL
$oldNoDesktop = $env:ABIOTIC_EDITOR_NO_DESKTOP
$log = Join-Path ([IO.Path]::GetTempPath()) "abiotic-editor-web-smoke-$PID.log"
$errorLog = "$log.err"
$unsafeLog = "$log.unsafe"
$desktopLog = "$log.desktop"
$desktopErrorLog = "$desktopLog.err"
$process = $null
$desktopProcess = $null
try {
    $env:ABIOTIC_EDITOR_URL = 'http://0.0.0.0:37246'
    $env:ABIOTIC_EDITOR_NO_DESKTOP = '1'
    $unsafeProcess = Start-Process -FilePath (Join-Path $root 'AbioticEditor.Web.exe') -WorkingDirectory $root `
        -RedirectStandardOutput $unsafeLog -RedirectStandardError "$unsafeLog.err" -PassThru -Wait
    $unsafeOutput = Get-Content -LiteralPath $unsafeLog,"$unsafeLog.err" -Raw -ErrorAction SilentlyContinue
    if ($unsafeProcess.ExitCode -eq 0 -or $unsafeOutput -notmatch 'loopback URL') {
        throw "Published Windows host did not reject a non-loopback endpoint.`n$unsafeOutput"
    }

    $env:ABIOTIC_EDITOR_URL = $url
    $env:ABIOTIC_EDITOR_NO_DESKTOP = '1'
    # Launch from outside the publish directory, matching shortcuts and app launchers.
    # Static assets must resolve beside the executable, not from the caller's cwd.
    $process = Start-Process -FilePath (Join-Path $root 'AbioticEditor.Web.exe') -WorkingDirectory ([IO.Path]::GetTempPath()) `
        -RedirectStandardOutput $log -RedirectStandardError $errorLog -PassThru
    $healthy = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($process.HasExited) { throw "Published Windows host exited with code $($process.ExitCode).`n$(Get-Content -LiteralPath $log,$errorLog -Raw -ErrorAction SilentlyContinue)" }
        try {
            $reply = Invoke-RestMethod -Uri "$url/healthz" -TimeoutSec 1
            if ($reply.status -eq 'ok') { $healthy = $true; break }
        }
        catch { Start-Sleep -Milliseconds 100 }
    }
    if (-not $healthy) { throw "Published Windows host did not pass /healthz within 10 seconds.`n$(Get-Content -LiteralPath $log,$errorLog -Raw -ErrorAction SilentlyContinue)" }
    $assets = @(
        '/',
        '/parity.css',
        '/AbioticEditor.Web.styles.css',
        '/fonts/Digital7.ttf',
        '/fonts/MaterialSymbolsOutlined.ttf',
        '/fonts/OpenSans-Regular.ttf',
        '/fonts/OpenSans-Semibold.ttf',
        '/images/abiotic-factor.png',
        '/_framework/blazor.web.js'
    )
    foreach ($asset in $assets) {
        # The first framework/static-asset request can trigger endpoint-manifest setup,
        # and the bundled Material Symbols font is intentionally large. Keep this
        # bounded without making healthy local release packages fail on slower CI VMs.
        $response = Invoke-WebRequest -Uri "$url$asset" -TimeoutSec 15
        if ($response.StatusCode -ne 200 -or $response.RawContentLength -le 0) {
            throw "Published Windows host returned an empty or non-200 response for '$asset'."
        }
    }

    Stop-Process -Id $process.Id -Force
    $process.WaitForExit()
    $process = $null

    $desktopUrl = 'http://127.0.0.1:37264'
    $env:ABIOTIC_EDITOR_URL = $desktopUrl
    Remove-Item Env:ABIOTIC_EDITOR_NO_DESKTOP -ErrorAction SilentlyContinue
    $desktopProcess = Start-Process -FilePath (Join-Path $root 'AbioticEditor.Web.exe') -WorkingDirectory ([IO.Path]::GetTempPath()) `
        -RedirectStandardOutput $desktopLog -RedirectStandardError $desktopErrorLog -PassThru
    $windowReady = $false
    for ($attempt = 0; $attempt -lt 150; $attempt++) {
        if ($desktopProcess.HasExited) {
            throw "Published Windows desktop exited with code $($desktopProcess.ExitCode).`n$(Get-Content -LiteralPath $desktopLog,$desktopErrorLog -Raw -ErrorAction SilentlyContinue)"
        }
        $desktopProcess.Refresh()
        try {
            $desktopHealth = Invoke-RestMethod -Uri "$desktopUrl/healthz" -TimeoutSec 1
            $windowReady = $desktopHealth.status -eq 'ok' -and
                $desktopProcess.MainWindowHandle -ne [IntPtr]::Zero -and
                $desktopProcess.MainWindowTitle -eq 'Abiotic Editor'
            if ($windowReady) { break }
        }
        catch { }
        Start-Sleep -Milliseconds 100
    }
    if (-not $windowReady) {
        throw "Published Windows desktop did not map an 'Abiotic Editor' window within 15 seconds.`n$(Get-Content -LiteralPath $desktopLog,$desktopErrorLog -Raw -ErrorAction SilentlyContinue)"
    }
    Write-Host "Windows host publish layout, health, UI assets, and native window smoke tests passed: $root"
}
finally {
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    if ($null -ne $desktopProcess -and -not $desktopProcess.HasExited) { Stop-Process -Id $desktopProcess.Id -Force }
    if ($null -eq $oldUrl) { Remove-Item Env:ABIOTIC_EDITOR_URL -ErrorAction SilentlyContinue } else { $env:ABIOTIC_EDITOR_URL = $oldUrl }
    if ($null -eq $oldNoDesktop) { Remove-Item Env:ABIOTIC_EDITOR_NO_DESKTOP -ErrorAction SilentlyContinue } else { $env:ABIOTIC_EDITOR_NO_DESKTOP = $oldNoDesktop }
    Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $errorLog -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $unsafeLog -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$unsafeLog.err" -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $desktopLog -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $desktopErrorLog -Force -ErrorAction SilentlyContinue
}
