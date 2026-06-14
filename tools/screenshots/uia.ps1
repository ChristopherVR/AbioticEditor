# UI Automation helper for driving the MAUI app. Run under Windows PowerShell 5.1.
#   uia.ps1 dump                       -> list invokable/selectable elements (name | type)
#   uia.ps1 invoke "<name substring>"  -> Invoke (or Select/Toggle) the first match
#   uia.ps1 click  "<name substring>"  -> move mouse + click center of first match
param(
    [Parameter(Mandatory = $true)][string]$Action,
    [string]$Target,
    [string]$ProcessName = "AbioticEditor.App"
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Error "No window for '$ProcessName'"; exit 1 }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$cond = [System.Windows.Automation.Condition]::TrueCondition
$all  = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)

function Get-Name($e) {
    $n = $e.Current.Name
    if ([string]::IsNullOrWhiteSpace($n)) { $n = $e.Current.AutomationId }
    return $n
}

if ($Action -eq "dump") {
    foreach ($e in $all) {
        $ct = $e.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        $n  = Get-Name $e
        if (-not [string]::IsNullOrWhiteSpace($n)) {
            "{0,-22} | {1}" -f $ct, $n
        }
    }
    exit 0
}

# Find element by name. Prefer an exact (case-insensitive) match; fall back to substring.
$match = $null
foreach ($e in $all) {
    $n = Get-Name $e
    if ($n -and $n.ToLower() -eq $Target.ToLower()) { $match = $e; break }
}
if (-not $match) {
    foreach ($e in $all) {
        $n = Get-Name $e
        if ($n -and $n.ToLower().Contains($Target.ToLower())) { $match = $e; break }
    }
}
if (-not $match) { Write-Error "No element matching '$Target'"; exit 2 }

if ($Action -eq "click") {
    $r = $match.Current.BoundingRectangle
    $x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2)
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)
    Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,int e);' -Name M -Namespace W
    [W.M]::mouse_event(0x02,0,0,0,0); [W.M]::mouse_event(0x04,0,0,0,0)  # left down/up
    "clicked '$($match.Current.Name)' at $x,$y"
    exit 0
}

if ($Action -eq "selectrow") {
    # Find the ListItem whose descendant Text contains $Target, then Select it.
    $listItems = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)))
    foreach ($li in $listItems) {
        $txt = $li.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)))
        $hit = $false
        foreach ($t in $txt) { if ($t.Current.Name -and $t.Current.Name.ToLower().Contains($Target.ToLower())) { $hit = $true; break } }
        if ($hit) {
            $sel = $null
            if ($li.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) {
                $sel.Select(); "selected row containing '$Target'"; exit 0
            }
            $r = $li.Current.BoundingRectangle
            $x = [int]($r.X + 20); $y = [int]($r.Y + $r.Height / 2)
            [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)
            Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,int e);' -Name M2 -Namespace W
            [W.M2]::mouse_event(0x02,0,0,0,0); [W.M2]::mouse_event(0x04,0,0,0,0)
            "clicked row containing '$Target' at $x,$y"; exit 0
        }
    }
    Write-Error "No ListItem row containing '$Target'"; exit 2
}

if ($Action -eq "invoke") {
    foreach ($patId in @('InvokePatternIdentifiers','SelectionItemPatternIdentifiers','TogglePatternIdentifiers')) {}
    $invoke = [System.Windows.Automation.InvokePattern]::Pattern
    $selitem = [System.Windows.Automation.SelectionItemPattern]::Pattern
    $toggle  = [System.Windows.Automation.TogglePattern]::Pattern
    $p = $null
    if ($match.TryGetCurrentPattern($invoke, [ref]$p)) { $p.Invoke(); "invoked '$($match.Current.Name)'"; exit 0 }
    if ($match.TryGetCurrentPattern($selitem, [ref]$p)) { $p.Select(); "selected '$($match.Current.Name)'"; exit 0 }
    if ($match.TryGetCurrentPattern($toggle,  [ref]$p)) { $p.Toggle(); "toggled '$($match.Current.Name)'"; exit 0 }
    Write-Error "Element '$Target' supports no invoke/select/toggle pattern"; exit 3
}

Write-Error "Unknown action '$Action'"; exit 4
