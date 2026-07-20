# SP-014 headed Windows smoke - stable-ID quick-toggle dispatch evidence.
# Re-verifies SP-007 claims on the CHANGED code (right-click toggle, ring, persistence),
# then the cross-proof: right-click toggle WHILE the SP-013 modeless popup is open
# (popup raised topmost first - a topmost dashboard buries its owned popup, observed;
# popup dragged aside by its title bar - at 520 DIP it covers the 488 DIP card).
# Negatives: no context menu on plain right-click; title-region click == body-region.
# Exceptions (locked/help/Visuals/System): contract-only, recorded in record.md - no fake cards.
# Harness pattern from SP-007/SP-013 (app layout-probe + popup-probe locators; UIA; SendInput).
# ASCII check strings only (PS 5.1 mangles BOM-less UTF-8 - SP-006 lesson).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

$taskDir = $PSScriptRoot
$shots = Join-Path $taskDir 'artifacts'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
$exe = Join-Path $taskDir '..\..\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe'
$settingsFile = Join-Path $env:APPDATA 'CcpClient\settings.json'

$mouse = @'
using System;
using System.Runtime.InteropServices;
public class MouseNative {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, int data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010, MOUSEMOVE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
}
'@
Add-Type -TypeDefinition $mouse

$script:gateFailures = 0
function Gate([bool]$ok, [string]$name, [string]$detail) {
    if ($ok) { Write-Output "GATE PASS: $name - $detail" }
    else { $script:gateFailures++; Write-Output "GATE FAIL: $name - $detail" }
}
function Fail([string]$msg) { Write-Output "FAIL: $msg"; if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill() }; exit 1 }

function Get-Windows([int]$processId) {
    # Owned windows nest UNDER the owner in the UIA tree (SP-013 lesson): search descendants.
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)))
    return @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond))
}
function Get-Texts($window) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $els = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $lines = @(); foreach ($t in $els) { $lines += $t.Current.Name }
    return $lines
}
function Get-Dashboard { return @(Get-Windows $script:proc.Id | Where-Object { (Get-Texts $_) -match 'layout-probe:' })[0] }
function Get-Popup { return @(Get-Windows $script:proc.Id | Where-Object { $t = Get-Texts $_; ($t -match 'popup-probe:') -and -not ($t -match 'layout-probe:') })[0] }

function Get-TickNumber($window) {
    foreach ($line in (Get-Texts $window)) {
        if ($line -match '^demo\.status-ticker: tick (\d+)$') { return [int]$Matches[1] }
    }
    return $null
}
function Get-CardRect($window) {
    $probe = (Get-Texts $window) | Where-Object { $_ -like 'layout-probe:*' }
    if ($probe -notmatch 'card ([\d.]+)x([\d.]+) DIP @ scale ([\d.]+) @ screen (-?\d+),(-?\d+)') { Fail "layout probe unreadable: $probe" }
    $scale = [double]$Matches[3]
    return @{
        X = [int]$Matches[4]; Y = [int]$Matches[5]
        W = [int]([double]$Matches[1] * $scale); H = [int]([double]$Matches[2] * $scale)
        DipW = [double]$Matches[1]; DipH = [double]$Matches[2]; Scale = $scale; Raw = $probe
    }
}
function Read-PopupProbe {
    $popup = Get-Popup
    if ($null -eq $popup) { return $null }
    $line = (Get-Texts $popup) | Where-Object { $_ -like 'popup-probe:*' } | Select-Object -First 1
    if ($line -notmatch 'pos (-?\d+),(-?\d+) size (\d+)x(\d+) scale ([\d.]+)') { return $null }
    return @{ X = [int]$Matches[1]; Y = [int]$Matches[2]; W = [int]$Matches[3]; H = [int]$Matches[4]; Scale = [double]$Matches[5]; Raw = $line }
}
function Raise-Topmost([IntPtr]$hwnd) {
    [MouseNative]::SetWindowPos($hwnd, [MouseNative]::HWND_TOPMOST, 0, 0, 0, 0,
        [MouseNative]::SWP_NOMOVE -bor [MouseNative]::SWP_NOSIZE -bor [MouseNative]::SWP_SHOWWINDOW) | Out-Null
    Start-Sleep -Milliseconds 300
}
function Shot([int]$x, [int]$y, [int]$w, [int]$h, [string]$file) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
function Count-BorderPixels([string]$file, [int]$targetR, [int]$targetG, [int]$targetB, [int]$tol) {
    $bmp = New-Object System.Drawing.Bitmap($file)
    $count = 0
    for ($x = 0; $x -lt $bmp.Width; $x++) {
        for ($y = 0; $y -lt 3; $y++) {
            $p = $bmp.GetPixel($x, $y)
            if ([Math]::Abs($p.R - $targetR) -le $tol -and [Math]::Abs($p.G - $targetG) -le $tol -and [Math]::Abs($p.B - $targetB) -le $tol) { $count++ }
        }
    }
    $bmp.Dispose()
    return $count
}
function Right-Click([int]$x, [int]$y) {
    [MouseNative]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 200
    [MouseNative]::mouse_event([MouseNative]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
    [MouseNative]::mouse_event([MouseNative]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
}
function Park-Mouse($rect) { [MouseNative]::SetCursorPos($rect.X + $rect.W + 200, $rect.Y + $rect.H + 200) | Out-Null; Start-Sleep -Milliseconds 300 }

if (Test-Path $settingsFile) { Remove-Item $settingsFile -Force }

# ---- Phase A: re-verify SP-007 toggle claims on the changed dispatch code ----
$script:proc = [System.Diagnostics.Process]::Start($exe)
Write-Output "launched pid=$($script:proc.Id)"
Start-Sleep -Seconds 4
$window = Get-Dashboard
if ($null -eq $window) { Fail 'dashboard window not found' }
Raise-Topmost $script:proc.MainWindowHandle

$all = (Get-Texts $window) -join "`n"
if ($all -notlike '*Demo: Status Ticker*') { Fail 'card title (CardTitle binding) not rendered' }
Write-Output 'card title renders from the CardTitle binding'

$rect = Get-CardRect $window
Write-Output "probe: $($rect.Raw)"
if ($rect.Scale -ne 1) { Fail "expected scale 1 on this machine, got $($rect.Scale)" }
$cx = [int]($rect.X + $rect.W / 2); $cy = [int]($rect.Y + $rect.H / 2)

if ($null -ne (Get-TickNumber $window)) { Fail 'tick text present before first toggle' }
Park-Mouse $rect
$unlitShot = Join-Path $shots 'sp014-unlit.png'
Shot $rect.X $rect.Y $rect.W $rect.H $unlitShot
$unlitBorder = Count-BorderPixels $unlitShot 0x3A 0x2F 0x3E 24
Gate ($unlitBorder -ge 50) 'unlit-ring' "unlit border pixels (#3A2F3E): $unlitBorder"

# A1: right-click body center -> immediate toggle, tick advances (live-start)
Right-Click $cx $cy
$tick1 = Get-TickNumber $window
if ($null -eq $tick1) { Fail 'tick text did not appear after right-click (dispatch broken)' }
Start-Sleep -Seconds 2
$tick2 = Get-TickNumber $window
Gate ($tick2 -gt $tick1) 'rightclick-live-start' "tick advanced: $tick1 -> $tick2"

# ring update (lit)
$rectLit = Get-CardRect $window
Park-Mouse $rectLit
$litShot = Join-Path $shots 'sp014-lit.png'
Shot $rectLit.X $rectLit.Y $rectLit.W $rectLit.H $litShot
$litBorder = Count-BorderPixels $litShot 0xE0 0x66 0xFF 32
Gate ($litBorder -ge 50) 'lit-ring' "lit border pixels (#E066FF): $litBorder"

# A2: mid-run persistence file-content proof (SP-005; no debounce in the store)
Start-Sleep -Seconds 1
Gate (Select-String -Path $settingsFile -Pattern '"statusTickerEnabled": true' -Quiet) 'persistence-mid-run' 'settings file contains statusTickerEnabled true while running'

# A3: title-region right-click == body-region (click directly over the displayed title text)
$titleEl = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Demo: Status Ticker')))
if ($null -eq $titleEl) { Fail 'title text element not found via UIA' }
$tr = $titleEl.Current.BoundingRectangle
Write-Output "title rect: $tr"
Right-Click ([int]($tr.X + $tr.Width / 2)) ([int]($tr.Y + $tr.Height / 2))
Gate ($null -eq (Get-TickNumber $window)) 'title-region-toggles' 'right-click over the title text toggled OFF (title region == body region, dispatch by ID)'

# back ON for the cross-proof
$rect2 = Get-CardRect (Get-Dashboard)
Right-Click ([int]($rect2.X + $rect2.W / 2)) ([int]($rect2.Y + $rect2.H / 2))
$tick3 = Get-TickNumber (Get-Dashboard)
Gate ($null -ne $tick3) 'toggle-back-on' "toggled on again, tick $tick3"

# ---- Phase B: cross-proof - right-click toggle WHILE the SP-013 popup is open ----
# Left-click opens the modeless popup (SP-013 wiring).
$rect3 = Get-CardRect (Get-Dashboard)
[MouseNative]::SetCursorPos([int]($rect3.X + $rect3.W / 2), [int]($rect3.Y + $rect3.H / 2)) | Out-Null
Start-Sleep -Milliseconds 200
[MouseNative]::mouse_event([MouseNative]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
[MouseNative]::mouse_event([MouseNative]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Seconds 1
$pp = Read-PopupProbe
Gate ($null -ne $pp) 'popup-open' "SP-013 modeless popup opened via left-click: $($pp.Raw)"
if ($null -eq $pp) { Fail 'popup did not open' }

# A topmost dashboard buries its owned popup (observed): raise the popup itself (SP-013 pattern).
Raise-Topmost ([IntPtr](Get-Popup).Current.NativeWindowHandle)

# The popup (520 DIP) covers the card (488 DIP): drag it aside by its title bar (+140,+340).
$beforeX = $pp.X; $beforeY = $pp.Y
$dragFromX = [int]($pp.X + $pp.W / 2); $dragFromY = [int]($pp.Y + 24)
[MouseNative]::SetCursorPos($dragFromX, $dragFromY) | Out-Null
Start-Sleep -Milliseconds 200
[MouseNative]::mouse_event([MouseNative]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
for ($i = 1; $i -le 12; $i++) {
    [MouseNative]::SetCursorPos($dragFromX + [int](140 * $i / 12), $dragFromY + [int](340 * $i / 12)) | Out-Null
    [MouseNative]::mouse_event([MouseNative]::MOUSEMOVE, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 30
}
[MouseNative]::mouse_event([MouseNative]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 600
$ppAfter = Read-PopupProbe
if ($null -eq $ppAfter) { Fail 'popup probe lost after drag' }
$dx = $ppAfter.X - $beforeX; $dy = $ppAfter.Y - $beforeY
Gate ([Math]::Abs($dx - 140) -le 8 -and [Math]::Abs($dy - 340) -le 8) 'popup-dragged-aside' "popup moved ($dx,$dy) ~= drag (140,340)"

# The card must be exposed now (click point outside the popup rect; both rects app-reported).
$rectB = Get-CardRect (Get-Dashboard)
$cxB = [int]($rectB.X + $rectB.W / 2); $cyB = [int]($rectB.Y + $rectB.H / 2)
$inside = ($cxB -ge $ppAfter.X -and $cxB -le ($ppAfter.X + $ppAfter.W) -and $cyB -ge $ppAfter.Y -and $cyB -le ($ppAfter.Y + $ppAfter.H))
if ($inside) { $cxB = [int]($rectB.X + 20) } # fall back to the exposed left strip
$inside = ($cxB -ge $ppAfter.X -and $cxB -le ($ppAfter.X + $ppAfter.W) -and $cyB -ge $ppAfter.Y -and $cyB -le ($ppAfter.Y + $ppAfter.H))
Gate (-not $inside) 'card-exposed' "click point ($cxB,$cyB) outside popup rect ($($ppAfter.X),$($ppAfter.Y),$($ppAfter.W)x$($ppAfter.H))"
if ($inside) { Fail 'cannot expose the card from under the popup' }

# B1: right-click the card WHILE the popup is open -> toggle OFF live; popup survives.
$tickBefore = Get-TickNumber (Get-Dashboard)
Right-Click $cxB $cyB
$dash = Get-Dashboard
Gate ($null -eq (Get-TickNumber $dash)) 'toggle-off-while-popup-open' "tick $tickBefore -> absent (right-click dispatched with popup open)"
Gate ($null -ne (Read-PopupProbe)) 'popup-survives-toggle' 'popup still open after the dashboard right-click'

# B2: right-click again WHILE the popup is open -> toggle ON live; tick advances.
Right-Click $cxB $cyB
$dash = Get-Dashboard
$tickB1 = Get-TickNumber $dash
if ($null -eq $tickB1) { Fail 'toggle-on while popup open failed (tick absent)' }
Start-Sleep -Seconds 2
$tickB2 = Get-TickNumber (Get-Dashboard)
Gate ($tickB2 -gt $tickB1) 'toggle-on-while-popup-open' "tick advancing $tickB1 -> $tickB2 with popup open"
Gate ($null -ne (Read-PopupProbe)) 'popup-still-open' 'popup open throughout the cross-proof'

# Close the popup: click its title bar (focus) then Escape (real key path, SP-013 pattern).
$ppNow = Read-PopupProbe
[MouseNative]::SetCursorPos([int]($ppNow.X + $ppNow.W / 2), [int]($ppNow.Y + 24)) | Out-Null
Start-Sleep -Milliseconds 200
[MouseNative]::mouse_event([MouseNative]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
[MouseNative]::mouse_event([MouseNative]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Milliseconds 700
Gate ($null -eq (Read-PopupProbe)) 'popup-closed' 'popup closed via Escape after title-bar click'

# ---- Phase C: negatives + teardown ----
# No-context-menu negative: one more plain right-click; window count stays 1; no Menu/MenuItem.
$rectC = Get-CardRect (Get-Dashboard)
Right-Click ([int]($rectC.X + $rectC.W / 2)) ([int]($rectC.Y + $rectC.H / 2))
$winCount = @(Get-Windows $script:proc.Id).Count
Gate ($winCount -eq 1) 'no-extra-window' "process windows after right-clicks: $winCount (dashboard only)"
$menuCond = New-Object System.Windows.Automation.OrCondition(
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Menu)),
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))
$menus = (Get-Dashboard).FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuCond)
if ($menus.Count -gt 0) {
    foreach ($m in $menus) {
        try { $c = $m.Current; Write-Output "  menu-ish: type=$($c.ControlType.ProgrammaticName) name='$($c.Name)' class='$($c.ClassName)' rect=$($c.BoundingRectangle) offscreen=$($c.IsOffscreen)" }
        catch { Write-Output '  menu-ish: (stale element - UIA teardown residue)' }
    }
}
# UIA exposes the window-chrome system-menu icon as ControlType.MenuItem name='System'
# (non-client area, appears once the window is activated) - observed; it is NOT an app
# context menu. An Avalonia ContextMenu would materialize as a separate popup WINDOW
# element (like the SP-013 popup), which the no-extra-window gate above already forbids.
$appMenus = @($menus | Where-Object { -not ($_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem -and $_.Current.Name -eq 'System') })
Gate ($appMenus.Count -eq 0) 'no-context-menu' "app Menu/MenuItem elements: $($appMenus.Count) (window-chrome System item excluded)"

# that right-click toggled OFF; teardown; final flush must carry the OFF state
$null = $script:proc.CloseMainWindow()
if (-not $script:proc.WaitForExit(10000)) { Fail 'process did not exit within 10s' }
Gate ($script:proc.ExitCode -eq 0) 'exit-0' "exit code: $($script:proc.ExitCode)"
Gate (Select-String -Path $settingsFile -Pattern '"statusTickerEnabled": false' -Quiet) 'persistence-final' 'settings file flushed with the final toggled-off state'

if ($script:gateFailures -gt 0) { Write-Output "SMOKE FAIL ($script:gateFailures gates)"; exit 1 }
Write-Output 'SMOKE PASS'
