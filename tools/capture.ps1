# Screenshot + scroll helper for driving the AbioticEditor app during visual checks.
# Usage:
#   capture.ps1 -Shot <out.png>                # capture primary screen
#   capture.ps1 -ScrollAt 960,540 -Clicks -6   # wheel-scroll at screen point (negative = down)
#   capture.ps1 -ClickAt 200,300               # left-click at screen point
param(
    [string]$Shot,
    [string]$ScrollAt,
    [int]$Clicks = -3,
    [string]$ClickAt
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Native {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[Native]::SetProcessDPIAware() | Out-Null

if ($ScrollAt) {
    $p = $ScrollAt -split ','
    [Native]::SetCursorPos([int]$p[0], [int]$p[1]) | Out-Null
    Start-Sleep -Milliseconds 150
    for ($i = 0; $i -lt [Math]::Abs($Clicks); $i++) {
        $delta = if ($Clicks -lt 0) { -120 } else { 120 }
        [Native]::mouse_event([Native]::MOUSEEVENTF_WHEEL, 0, 0, $delta, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 80
    }
    Start-Sleep -Milliseconds 400
}

if ($ClickAt) {
    $p = $ClickAt -split ','
    [Native]::SetCursorPos([int]$p[0], [int]$p[1]) | Out-Null
    Start-Sleep -Milliseconds 150
    [Native]::mouse_event([Native]::MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Native]::mouse_event([Native]::MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
}

if ($Shot) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $g.Dispose()
    $bmp.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output "saved $Shot ($($bounds.Width)x$($bounds.Height))"
}
