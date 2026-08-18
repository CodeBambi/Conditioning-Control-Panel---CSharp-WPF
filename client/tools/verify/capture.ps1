# CCP greenfield verification harness — tier 2 Windows capture (SP-008).
# Captures ONE named surface+state to a PNG for the CcpVerify named-check tool and K3 review.
# Formalizes the SP-007 headed-smoke patterns: SetWindowPos(HWND_TOPMOST) raise (the app
# opens unactivated and pixels belong to the occluder), UIA text reads, layout-probe door
# rect (Avalonia exposes no UIA peers for Border/Grid/StackPanel), real-input state driving.
# System.Drawing appears ONLY as capture transport (CopyFromScreen -> PNG file); this script
# never reads a pixel — all pixel logic lives in CcpVerify.
# SP-091 re-anchor: the demonstrator card this harness used to drive is retired, and the
# navigation shell replaced it. Same three techniques, new anchors — surface dashboard-card ->
# rail-door, state lit -> selected; 'dashboard' still means the whole window. The state is still
# driven through REAL input (a left-click on a rail door), and the drive is still confirmed by a
# UIA read before any pixel is captured.
# Usage: pwsh client/tools/verify/capture.ps1 -Surface rail-door -State selected
#        pwsh client/tools/verify/capture.ps1 -Surface dashboard -State unselected
param(
    [Parameter(Mandatory)][ValidateSet('dashboard', 'rail-door')] [string]$Surface,
    [Parameter(Mandatory)][ValidateSet('unselected', 'selected')] [string]$State
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$verifyDir = $PSScriptRoot
$shots = Join-Path $verifyDir 'artifacts'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
$exe = Join-Path $verifyDir '..\..\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe'
$settingsFile = Join-Path $env:APPDATA 'CcpClient\settings.json'
$outFile = Join-Path $shots "windows-$Surface-$State.png"

if (-not (Test-Path $exe)) { Write-Output "FAIL: app not built: $exe"; exit 1 }

$native = @'
using System;
using System.Runtime.InteropServices;
public class VerifyNative {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    public const uint RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;
    public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
}
'@
Add-Type -TypeDefinition $native

function Fail([string]$msg) { Write-Output "FAIL: $msg"; if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill() }; exit 1 }

function Get-Window([int]$processId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Get-Texts($window) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $els = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $lines = @()
    foreach ($t in $els) { $lines += $t.Current.Name }
    return $lines
}

# The shell publishes one probe line per rail door (MainWindow.axaml.cs ProbeLine); a UIA Text
# element carries them all in one Name, so match the requested door out of the joined text.
function Get-DoorRect($window, [string]$door) {
    $probe = (Get-Texts $window) -join "`n"
    $pattern = "door $door ([\d.]+)x([\d.]+) DIP @ scale ([\d.]+) @ screen (-?\d+),(-?\d+)"
    if ($probe -notmatch $pattern) { Fail "layout probe for door '$door' unreadable: $probe" }
    $scale = [double]$Matches[3]
    return @{
        X = [int]$Matches[4]; Y = [int]$Matches[5]
        W = [int]([double]$Matches[1] * $scale); H = [int]([double]$Matches[2] * $scale)
        Scale = $scale; Raw = $Matches[0]
    }
}

function Click-Rect($rect) {
    $cx = [int]($rect.X + $rect.W / 2); $cy = [int]($rect.Y + $rect.H / 2)
    [VerifyNative]::SetCursorPos($cx, $cy) | Out-Null
    Start-Sleep -Milliseconds 200
    [VerifyNative]::mouse_event([VerifyNative]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    [VerifyNative]::mouse_event([VerifyNative]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
}

function Assert-Route($window, [string]$route) {
    $texts = (Get-Texts $window) -join "`n"
    if ($texts -notmatch "route: $route") { Fail "the shell did not navigate to '$route' (state drive failed)" }
}

# Deterministic start: remove the demonstrator settings file (demo store only).
if (Test-Path $settingsFile) { Remove-Item $settingsFile -Force }

$script:proc = [System.Diagnostics.Process]::Start($exe)
Write-Output "launched pid=$($script:proc.Id)"

# Poll to a DEADLINE, never a fixed sleep. This was `Start-Sleep -Seconds 4` and it rotted:
# startup grew a 1542-file DTRH payload probe and a loopback origin bind, the window stopped
# arriving inside 4s, and the harness reported 'window not found' as though the APP were broken.
# A fixed sleep encodes today's startup cost as tomorrow's correctness condition. Polling is
# also strictly faster on a warm run, because it returns the moment the window is really there.
$deadline = [Diagnostics.Stopwatch]::StartNew()
$window = $null; $hwnd = [IntPtr]::Zero
while ($deadline.Elapsed.TotalSeconds -lt 40) {
    if ($script:proc.HasExited) { Fail "app exited during startup (code $($script:proc.ExitCode)) before a window appeared" }
    $window = Get-Window $script:proc.Id
    if ($null -ne $window) {
        $script:proc.Refresh()
        $hwnd = $script:proc.MainWindowHandle
        # Both, or neither: a UIA element with no HWND cannot be raised or captured.
        if ($hwnd -ne [IntPtr]::Zero) { break }
    }
    Start-Sleep -Milliseconds 250
}
if ($null -eq $window) { Fail "window not found within $([int]$deadline.Elapsed.TotalSeconds)s" }
if ($hwnd -eq [IntPtr]::Zero) { Fail "no MainWindowHandle within $([int]$deadline.Elapsed.TotalSeconds)s" }
Write-Output "window up after $([math]::Round($deadline.Elapsed.TotalSeconds, 1))s"

# Raise: the app opens unactivated behind other windows; pixels belong to whatever is on top.
[VerifyNative]::SetWindowPos($hwnd, [VerifyNative]::HWND_TOPMOST, 0, 0, 0, 0,
    [VerifyNative]::SWP_NOMOVE -bor [VerifyNative]::SWP_NOSIZE -bor [VerifyNative]::SWP_SHOWWINDOW) | Out-Null
Start-Sleep -Milliseconds 500

$all = (Get-Texts $window) -join "`n"
foreach ($needle in @('route: studio', 'layout-probe: door studio', 'layout-probe: door companion', 'layout-probe: door system')) {
    if ($all -notlike "*$needle*") { Fail "missing '$needle'" }
}
Write-Output 'shell mounted its default page; every rail door published a layout probe (UIA reads)'

# The startup trace and the typed capability states live on the System page now (SP-091), so
# reaching them is itself a real navigation. Drive it, then read them.
Click-Rect (Get-DoorRect $window 'system')
Assert-Route $window 'system'
$all = (Get-Texts $window) -join "`n"
foreach ($needle in @('CapabilityProbes: ok', 'capability display-session: Available')) {
    if ($all -notlike "*$needle*") { Fail "missing '$needle'" }
}
Write-Output 'System door reached by real input; capability surface renders (UIA reads)'

# The captured door is Companion: it is unselected while System is showing, and selecting it is
# one real click. Same door, two states, one gesture between them.
$rect = Get-DoorRect $window 'companion'
Write-Output "probe: $($rect.Raw)"

if ($State -eq 'selected') {
    # Drive the state through REAL input (the user path a regression would break), then confirm
    # the shell actually navigated before any pixel is read.
    Click-Rect $rect
    Assert-Route $window 'companion'
    Write-Output 'state drive: left-click on the Companion door -> route: companion'
    $rect = Get-DoorRect $window 'companion'
} else {
    Assert-Route $window 'system'   # the captured door is genuinely NOT the selected one
}

# Park the mouse off the door so :pointerover never leaks into a capture.
[VerifyNative]::SetCursorPos($rect.X + $rect.W + 200, $rect.Y + $rect.H + 200) | Out-Null
Start-Sleep -Milliseconds 400

if ($Surface -eq 'rail-door') {
    $capX = $rect.X; $capY = $rect.Y; $capW = $rect.W; $capH = $rect.H
} else {
    $bounds = $window.Current.BoundingRectangle
    $capX = [int]$bounds.X; $capY = [int]$bounds.Y; $capW = [int]$bounds.Width; $capH = [int]$bounds.Height
}

$bmp = New-Object System.Drawing.Bitmap $capW, $capH
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($capX, $capY, 0, 0, $bmp.Size)
$bmp.Save($outFile, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

$null = $script:proc.CloseMainWindow()
if (-not $script:proc.WaitForExit(10000)) { Fail 'process did not exit within 10s' }
if ($script:proc.ExitCode -ne 0) { Fail "non-zero exit on close: $($script:proc.ExitCode)" }

Write-Output "CAPTURE: $outFile ($($capW)x$($capH))"
Write-Output 'CAPTURE PASS'
