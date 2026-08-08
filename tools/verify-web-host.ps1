[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDir,
    [switch]$SkipSmoke
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PublishDir)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Publish directory does not exist: $root" }

# The host ships as a single executable: every managed assembly and the native
# Photino/WebView2 libraries live inside it. Only the data the editor reads at run time is
# expected beside it, so this list deliberately no longer names any DLL.
$required = @(
    'AbioticEditor.Web.exe',
    'THIRD-PARTY-NOTICES.txt',
    'Mappings.usmap',
    'registry',
    'wiki',
    'wwwroot',
    # The single-file bundler swallows this unless it is marked ExcludeFromSingleFile, and the
    # window then opens with the blank default icon.
    'appicon.ico',
    # The app will not start without the static-assets manifest, and the build moves it in here
    # to keep it out of the download's top level. Both halves have to stay true.
    'wwwroot\AbioticEditor.Web.staticwebassets.endpoints.json',
    'Templates\blank-world-template.sav',
    'Templates\blank-player-template.sav'
)
# Loose assemblies beside the exe mean single-file publishing silently regressed.
$strayDlls = Get-ChildItem -LiteralPath $root -Filter *.dll -File -ErrorAction SilentlyContinue
if ($strayDlls) { throw "Expected a single-file publish, found $($strayDlls.Count) loose DLL(s), e.g. $($strayDlls[0].Name)." }
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relative))) { throw "Published Windows host is missing '$relative'." }
}
# Windows launchers cannot use the Linux/macOS shell scripts, so they must not be in this package.
$strayScripts = Get-ChildItem -LiteralPath $root -Filter *.sh -File -ErrorAction SilentlyContinue
if ($strayScripts) { throw "Windows package contains $($strayScripts.Count) shell script(s), e.g. $($strayScripts[0].Name)." }
# A console-subsystem executable makes Windows flash a black console window before the editor
# appears. The build rewrites this field after publishing; 2 is the graphical subsystem.
$exe = [IO.File]::OpenRead((Join-Path $root 'AbioticEditor.Web.exe'))
try {
    $reader = New-Object IO.BinaryReader($exe)
    $exe.Position = 0x3C
    $exe.Position = $reader.ReadInt32() + 4 + 20 + 68
    $subsystem = $reader.ReadUInt16()
    if ($subsystem -ne 2) { throw "Published executable would open a console window (PE subsystem $subsystem, expected 2)." }
}
finally { $exe.Dispose() }

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
    # The look-and-feel files live in the shared screen library, so they are served from
    # _content/<library>/ rather than the host's own root; only the scoped-CSS bundle (named
    # after this app) and the Blazor framework files sit at the root. Checking the old root
    # paths passed for as long as those files lived here and then failed the moment they moved,
    # even though the app itself was serving them correctly the whole time.
    $assets = @(
        '/',
        '/_content/AbioticEditor.Web.Shared/parity.css',
        '/AbioticEditor.Web.styles.css',
        '/_content/AbioticEditor.Web.Shared/fonts/Digital7.ttf',
        '/_content/AbioticEditor.Web.Shared/fonts/MaterialSymbolsOutlined.ttf',
        '/_content/AbioticEditor.Web.Shared/fonts/OpenSans-Regular.ttf',
        '/_content/AbioticEditor.Web.Shared/fonts/OpenSans-Semibold.ttf',
        '/_content/AbioticEditor.Web.Shared/images/abiotic-factor.png',
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
