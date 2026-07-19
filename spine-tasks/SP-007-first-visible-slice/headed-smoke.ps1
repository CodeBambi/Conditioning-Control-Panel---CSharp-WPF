# SP-007 headed Windows smoke (UIA + real input + pixel observation).
# Observes, never believes: card render, avares asset, right-click quick-toggle advancing
# the tick, lit-ring pixel flip, :pointerover delta, IsVisible bounds delta, keyboard path,
# restart-restore, scaling bounds at 100%, mid-operation teardown exit 0.
# The card's rect comes from the app's own layout probe (Avalonia exposes no UIA peers for
# Border/StackPanel/Grid on this build - observed fact, recorded in record.md).
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
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;
    public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
}
'@
Add-Type -TypeDefinition $mouse

function Fail([string]$msg) { Write-Output "FAIL: $msg"; if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill() }; exit 1 }

function Get-Window([int]$processId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

# Pixel captures read the screen, not the window - the app must be on top or the crop shows
# whatever window covers it (observed: the app opens UNACTIVATED behind other windows; UIA
# reads it fine but pixels belong to the occluder; SetForegroundWindow is foreground-locked;
# a click cannot reach a fully covered window). SetWindowPos(HWND_TOPMOST) raises the real
# HWND (proc.MainWindowHandle - UIA's NativeWindowHandle is bogus on this Avalonia build,
# also observed). The pixel checks below are themselves the occlusion verification.
function Enter-Foreground() {
    $script:proc.Refresh()
    $hwnd = $script:proc.MainWindowHandle
    if ($hwnd -eq [IntPtr]::Zero) { Fail 'no MainWindowHandle' }
    [MouseNative]::SetWindowPos($hwnd, [MouseNative]::HWND_TOPMOST, 0, 0, 0, 0,
        [MouseNative]::SWP_NOMOVE -bor [MouseNative]::SWP_NOSIZE -bor [MouseNative]::SWP_SHOWWINDOW) | Out-Null
    Start-Sleep -Milliseconds 500
}

function Get-Texts($window) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $els = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $lines = @()
    foreach ($t in $els) { $lines += $t.Current.Name }
    return $lines
}

function Get-TickNumber($window) {
    foreach ($line in (Get-Texts $window)) {
        if ($line -match '^demo\.status-ticker: tick (\d+)$') { return [int]$Matches[1] }
    }
    return $null
}

# Returns @{X;Y;W;H;Scale} from the app's layout probe (measured by the app itself).
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

function Park-Mouse($rect) { [MouseNative]::SetCursorPos($rect.X + $rect.W + 200, $rect.Y + $rect.H + 200) | Out-Null }
function Card-Center($rect) { @([int]($rect.X + $rect.W / 2), [int]($rect.Y + $rect.H / 2)) }

# Deterministic start: remove the demonstrator settings file (demo store only - greeting,
# volume, ticker flag; no product data lives there).
if (Test-Path $settingsFile) { Remove-Item $settingsFile -Force }

# ---- Phase A: fresh launch -------------------------------------------------
$script:proc = [System.Diagnostics.Process]::Start($exe)
Write-Output "launched pid=$($script:proc.Id)"
Start-Sleep -Seconds 4
$window = Get-Window $script:proc.Id
if ($null -eq $window) { Fail 'window not found' }
Write-Output "window: $($window.Current.Name)"
Enter-Foreground

$all = (Get-Texts $window) -join "`n"
foreach ($needle in @('CapabilityProbes: ok', 'capability display-session: Available', 'capability atomic-filesystem: Available', 'Demo: Status Ticker', 'layout-probe: card')) {
    if ($all -notlike "*$needle*") { Fail "missing '$needle'" }
}
Write-Output 'capability surface + card + layout probe render (SP-003/004/006 proofs intact)'

$rect = Get-CardRect $window
Write-Output "probe: $($rect.Raw)"
if ($rect.Scale -ne 1) { Fail "expected scale 1 on this machine (all monitors 100%), got $($rect.Scale)" }
Write-Output ("card bounds @100%: {0}x{1} DIP = {2}x{3} px at {4},{5}" -f $rect.DipW, $rect.DipH, $rect.W, $rect.H, $rect.X, $rect.Y)

# avares asset: an Image control with non-empty bounds
$imgCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Image)
$images = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $imgCond)
if ($images.Count -lt 1) { Fail 'no Image control found' }
$imgRect = $images[0].Current.BoundingRectangle
if ($imgRect.Width -lt 20 -or $imgRect.Height -lt 20) { Fail "asset image has empty bounds: $imgRect" }
Write-Output ("avares asset rendered: image bounds {0}x{1}" -f $imgRect.Width, $imgRect.Height)

if ($null -ne (Get-TickNumber $window)) { Fail 'tick text present before first toggle' }
Write-Output 'tick row absent while off'

# unlit screenshot (mouse parked away from the card)
Park-Mouse $rect
Start-Sleep -Milliseconds 400
$unlitShot = Join-Path $shots 'windows-uia-scale1-card-unlit.png'
Shot $rect.X $rect.Y $rect.W $rect.H $unlitShot
$unlitBorder = Count-BorderPixels $unlitShot 0x3A 0x2F 0x3E 24
Write-Output "unlit border pixels (#3A2F3E): $unlitBorder"
if ($unlitBorder -lt 50) { Fail 'unlit border brush not observed' }

# :pointerover delta (unlit): #3A2F3E -> #6B5B73
$center = Card-Center $rect
[MouseNative]::SetCursorPos($center[0], $center[1]) | Out-Null
Start-Sleep -Milliseconds 400
$hoverShot = Join-Path $shots 'windows-uia-scale1-card-pointerover.png'
Shot $rect.X $rect.Y $rect.W $rect.H $hoverShot
$hoverBorder = Count-BorderPixels $hoverShot 0x6B 0x5B 0x73 24
Write-Output "pointerover border pixels (#6B5B73): $hoverBorder"
if ($hoverBorder -lt 50) { Fail ':pointerover visual delta not observed' }
Park-Mouse $rect
Start-Sleep -Milliseconds 300

# ---- right-click quick-toggle: operation starts, tick ADVANCES -------------
$heightBefore = (Get-CardRect $window).DipH
[MouseNative]::SetCursorPos($center[0], $center[1]) | Out-Null
Start-Sleep -Milliseconds 200
[MouseNative]::mouse_event([MouseNative]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
[MouseNative]::mouse_event([MouseNative]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 700
$tick1 = Get-TickNumber $window
if ($null -eq $tick1) { Fail 'tick text did not appear after right-click' }
Start-Sleep -Seconds 2
$tick2 = Get-TickNumber $window
Write-Output "tick advanced: $tick1 -> $tick2"
if ($tick2 -le $tick1) { Fail "tick did not advance ($tick1 -> $tick2)" }

# lit ring pixel flip
Park-Mouse $rect
Start-Sleep -Milliseconds 300
$rectLit = Get-CardRect $window
$litShot = Join-Path $shots 'windows-uia-scale1-card-lit.png'
Shot $rectLit.X $rectLit.Y $rectLit.W $rectLit.H $litShot
$litBorder = Count-BorderPixels $litShot 0xE0 0x66 0xFF 32
Write-Output "lit border pixels (#E066FF): $litBorder"
if ($litBorder -lt 50) { Fail 'lit ring brush not observed after toggle on' }

# IsVisible load-bearing: card grew with the tick row
$heightAfter = $rectLit.DipH
Write-Output "card height: off=$heightBefore on=$heightAfter"
if ($heightAfter -le $heightBefore) { Fail 'IsVisible collapse is not load-bearing (no bounds delta)' }

# ElementName mirror follows the live tick text
$mirror = (Get-Texts $window) | Where-Object { $_ -like 'ElementName mirror:*' }
Write-Output "mirror: $mirror"
if ($mirror -notmatch 'tick \d+') { Fail 'ElementName mirror did not follow the live tick text' }

# ---- keyboard path: left-click focuses the card body, Enter toggles OFF ----
[MouseNative]::SetCursorPos($center[0], $center[1]) | Out-Null
Start-Sleep -Milliseconds 200
[MouseNative]::mouse_event([MouseNative]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
[MouseNative]::mouse_event([MouseNative]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Milliseconds 700
if ($null -ne (Get-TickNumber $window)) { Fail 'keyboard Enter did not toggle off' }
$heightOffAgain = (Get-CardRect $window).DipH
Write-Output "keyboard toggle off: card height back to $heightOffAgain"
if ($heightOffAgain -ne $heightBefore) { Fail 'IsVisible bounds did not revert after keyboard toggle off' }

# keyboard path toggles ON again (leave running for teardown test)
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Milliseconds 700
$tick3 = Get-TickNumber $window
if ($null -eq $tick3) { Fail 'keyboard Enter did not toggle on again' }
Write-Output "keyboard toggle on: tick $tick3"

# ---- teardown mid-operation ------------------------------------------------
$null = $script:proc.CloseMainWindow()
if (-not $script:proc.WaitForExit(10000)) { Fail 'process did not exit within 10s' }
Write-Output "exit code (teardown mid-operation): $($script:proc.ExitCode)"
if ($script:proc.ExitCode -ne 0) { Fail 'non-zero exit on teardown' }
if (-not (Select-String -Path $settingsFile -Pattern '"statusTickerEnabled": true' -Quiet)) { Fail 'settings file does not contain statusTickerEnabled true after close' }
Write-Output 'settings flushed with flag true (file-content proof)'

# ---- Phase B: restart restores flag AND restarts operation ------------------
$script:proc = [System.Diagnostics.Process]::Start($exe)
Start-Sleep -Seconds 4
$window = Get-Window $script:proc.Id
if ($null -eq $window) { Fail 'window not found after restart' }
Enter-Foreground
$tickA = Get-TickNumber $window
if ($null -eq $tickA) { Fail 'operation not running after restart (restore failed)' }
Start-Sleep -Seconds 2
$tickB = Get-TickNumber $window
Write-Output "restart restore: tick $tickA -> $tickB"
if ($tickB -le $tickA) { Fail 'tick not advancing after restart' }
$rectB = Get-CardRect $window
Park-Mouse $rectB
Start-Sleep -Milliseconds 300
$relitShot = Join-Path $shots 'windows-uia-scale1-card-restart-lit.png'
Shot $rectB.X $rectB.Y $rectB.W $rectB.H $relitShot
$relitBorder = Count-BorderPixels $relitShot 0xE0 0x66 0xFF 32
Write-Output "restart lit border pixels: $relitBorder"
if ($relitBorder -lt 50) { Fail 'lit ring not restored after restart' }
$null = $script:proc.CloseMainWindow()
if (-not $script:proc.WaitForExit(10000)) { Fail 'process did not exit (restart close)' }
Write-Output "exit code (restart close): $($script:proc.ExitCode)"
if ($script:proc.ExitCode -ne 0) { Fail 'non-zero exit on restart close' }

Write-Output 'SMOKE PASS'
