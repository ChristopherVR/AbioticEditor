Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Mouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, int e);
  public const uint LEFTDOWN = 0x02, LEFTUP = 0x04;
  public static void Click(int x, int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(150); mouse_event(LEFTDOWN,0,0,0,0); System.Threading.Thread.Sleep(70); mouse_event(LEFTUP,0,0,0,0); }
}
"@
$exe = Join-Path $env:TEMP "gpverify\AbioticEditor.App.exe"
$env:ABIOTIC_EDITOR_FOLDER = $args[0]
$env:ABIOTIC_EDITOR_AUTOSELECT = $args[1]
$env:ABIOTIC_DIAG_DIRTY = Join-Path $env:TEMP "diag_dirty.txt"
$needles = $args[2..($args.Count-1)]

$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 9
$auto = [System.Windows.Automation.AutomationElement]
$root = $auto::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $p.Id)
$win = $null
for ($t=0; $t -lt 20 -and $win -eq $null; $t++){ $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond); if($win -eq $null){Start-Sleep -Milliseconds 500} }
if ($win -eq $null){ Write-Output "NO WINDOW"; if(!$p.HasExited){Stop-Process -Id $p.Id -Force}; return }

$liCond = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$txtC = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)

function ClickNeedle($needle){
  $items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $liCond)
  foreach ($it in $items){
    $childTexts = $it.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtC)
    $label = ""
    foreach ($ct in $childTexts){ $label += "|" + $ct.Current.Name }
    if ($label -like "*$needle*"){
      try { $it.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView() } catch {}
      Start-Sleep -Milliseconds 400
      $r = $it.Current.BoundingRectangle
      $cx = [int]($r.X + 30); $cy = [int]($r.Y + $r.Height/2)
      Write-Output ("CLICK '$needle' at $cx,$cy")
      [Mouse]::Click($cx,$cy)
      return
    }
  }
  Write-Output "NEEDLE NOT FOUND: $needle"
}

foreach ($n in $needles){
  ClickNeedle $n
  Start-Sleep -Seconds 3
  # check for unsaved dialog
  $texts = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtC)
  foreach ($tx in $texts){ $s=$tx.Current.Name; if($s -like "*unsaved changes*" -or $s -like "*staged edits*"){ Write-Output ("  >> DIALOG: " + $s) } }
}
Start-Sleep -Seconds 1
if(!$p.HasExited){ Stop-Process -Id $p.Id -Force }
Write-Output "DONE"
