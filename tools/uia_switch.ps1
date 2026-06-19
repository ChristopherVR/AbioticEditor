Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Mouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, int e);
  public const uint LEFTDOWN = 0x02, LEFTUP = 0x04;
  public static void Click(int x, int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(120); mouse_event(LEFTDOWN,0,0,0,0); System.Threading.Thread.Sleep(60); mouse_event(LEFTUP,0,0,0,0); }
}
"@

$exe = Join-Path $env:TEMP "gpverify\AbioticEditor.App.exe"
$env:ABIOTIC_EDITOR_FOLDER = $args[0]
$env:ABIOTIC_EDITOR_AUTOSELECT = $args[1]
$clickNeedle = $args[2]
$env:ABIOTIC_DIAG_DIRTY = Join-Path $env:TEMP "diag_dirty.txt"

$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 9

$auto = [System.Windows.Automation.AutomationElement]
$root = $auto::RootElement
# find our window
$cond = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $p.Id)
$win = $null
for ($t=0; $t -lt 20 -and $win -eq $null; $t++){
  $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
  if ($win -eq $null){ Start-Sleep -Milliseconds 500 }
}
if ($win -eq $null){ Write-Output "NO WINDOW"; if(!$p.HasExited){Stop-Process -Id $p.Id -Force}; return }
Write-Output ("WINDOW: " + $win.Current.Name)

# Enumerate list items
$liCond = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $liCond)
Write-Output ("LISTITEMS: " + $items.Count)
$txtC = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$target = $null
foreach ($it in $items){
  $childTexts = $it.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtC)
  $label = ""
  foreach ($ct in $childTexts){ $label += "|" + $ct.Current.Name }
  if ($label.Trim('|').Length -gt 0){ Write-Output ("  item: " + $label) }
  if ($label -like "*$clickNeedle*" -and $target -eq $null){ $target = $it }
}
if ($target -eq $null){ Write-Output "NO TARGET"; if(!$p.HasExited){Stop-Process -Id $p.Id -Force}; return }

try { $target.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView() } catch {}
Start-Sleep -Milliseconds 500
$rect = $target.Current.BoundingRectangle
$cx = [int]($rect.X + 30)
$cy = [int]($rect.Y + $rect.Height/2)
Write-Output ("RECT x=$([int]$rect.X) y=$([int]$rect.Y) w=$([int]$rect.Width) h=$([int]$rect.Height) -> CLICK $cx,$cy")
[Mouse]::Click($cx,$cy)
Start-Sleep -Seconds 4

# scan the app window for a dialog / discard text
$txtCond = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$texts = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtCond)
foreach ($tx in $texts){
  $s = $tx.Current.Name
  if ($s -like "*unsaved changes*" -or $s -like "*staged edits*"){ Write-Output ("DIALOGTEXT: " + $s) }
}
Start-Sleep -Seconds 1
if(!$p.HasExited){ Stop-Process -Id $p.Id -Force }
Write-Output "DONE"
