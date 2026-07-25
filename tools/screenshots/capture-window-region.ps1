# Reliably captures a specific window's real rendered pixels (works for WinUI3/DirectComposition
# apps where PrintWindow returns black) without exposing the rest of the desktop: it only ever
# writes the cropped window region to disk, never the full-screen intermediate bitmap.
#
# Foregrounding a window from a non-interactive process is normally blocked by Windows' focus-
# stealing prevention, which would otherwise leave whatever window was already on top in the
# captured region. This nudges past that with the standard harmless-Alt-keypress workaround before
# calling SetForegroundWindow.
param(
    [Parameter(Mandatory = $true)][string]$ProcessName,
    [Parameter(Mandatory = $true)][string]$OutPath
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class RegCap {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public const byte VK_MENU = 0x12;
    public const uint KEYEVENTF_KEYUP = 0x0002;
}
"@

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Error "No window for process '$ProcessName'"; exit 1 }
$h = $proc.MainWindowHandle

# Harmless Alt tap so Windows grants the next SetForegroundWindow call.
[RegCap]::keybd_event([RegCap]::VK_MENU, 0, 0, [UIntPtr]::Zero)
[RegCap]::keybd_event([RegCap]::VK_MENU, 0, [RegCap]::KEYEVENTF_KEYUP, [UIntPtr]::Zero)

[RegCap]::ShowWindow($h, 9) | Out-Null   # SW_RESTORE
[RegCap]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500

$r = New-Object RegCap+RECT
[RegCap]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$hgt = $r.Bottom - $r.Top
if ($w -le 0 -or $hgt -le 0) { Write-Error "Zero-size window"; exit 1 }

$crop = New-Object System.Drawing.Bitmap($w, $hgt)
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $crop.Size)
$g.Dispose()
$crop.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$savedW = $crop.Width
$savedH = $crop.Height
$crop.Dispose()
Write-Output "saved $OutPath ($savedW x $savedH)"
