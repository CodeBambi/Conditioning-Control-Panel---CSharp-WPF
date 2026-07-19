# CCP greenfield verification harness — tier 2 Windows capture (SP-008).
# Captures ONE named surface+state to a PNG for the CcpVerify named-check tool and K3 review.
# Formalizes the SP-007 headed-smoke patterns: SetWindowPos(HWND_TOPMOST) raise (the app
# opens unactivated and pixels belong to the occluder), UIA text reads, layout-probe card
# rect (Avalonia exposes no UIA peers for Border/Grid/StackPanel), real-input state driving.
# System.Drawing appears ONLY as capture transport (CopyFromScreen -> PNG file); this script
# never reads a pixel — all pixel logic lives in CcpVerify.
# Usage: pwsh client/tools/verify/capture.ps1 -Surface dashboard-card -State lit
#        pwsh client/tools/verify/capture.ps1 -Surface dashboard -State unlit
param(
    [Parameter(Mandatory)][ValidateSet('dashboard', 'dashboard-card')] [string]$Surface,
    [Parameter(Mandatory)][ValidateSet('unlit', 'lit')] [string]$State
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

function Get-CardRect($window) {
    $probe = (Get-Texts $window) | Where-Object { $_ -like 'layout-probe:*' }
    if ($probe -notmatch 'card ([\d.]+)x([\d.]+) DIP @ scale ([\d.]+) @ screen (-?\d+),(-?\d+)') { Fail "layout probe unreadable: $probe" }
    $scale = [double]$Matches[3]
    return @{
        X = [int]$Matches[4]; Y = [int]$Matches[5]
        W = [int]([double]$Matches[1] * $scale); H = [int]([double]$Matches[2] * $scale)
        Scale = $scale; Raw = $probe
    }
}

# Deterministic start: remove the demonstrator settings file (demo store only).
if (Test-Path $settingsFile) { Remove-Item $settingsFile -Force }

$script:proc = [System.Diagnostics.Process]::Start($exe)
Write-Output "launched pid=$($script:proc.Id)"
Start-Sleep -Seconds 4
$window = Get-Window $script:proc.Id
if ($null -eq $window) { Fail 'window not found' }

# Raise: the app opens unactivated behind other windows; pixels belong to whatever is on top.
$script:proc.Refresh()
$hwnd = $script:proc.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { Fail 'no MainWindowHandle' }
[VerifyNative]::SetWindowPos($hwnd, [VerifyNative]::HWND_TOPMOST, 0, 0, 0, 0,
    [VerifyNative]::SWP_NOMOVE -bor [VerifyNative]::SWP_NOSIZE -bor [VerifyNative]::SWP_SHOWWINDOW) | Out-Null
Start-Sleep -Milliseconds 500

$all = (Get-Texts $window) -join "`n"
foreach ($needle in @('CapabilityProbes: ok', 'capability display-session: Available', 'Demo: Status Ticker', 'layout-probe: card')) {
    if ($all -notlike "*$needle*") { Fail "missing '$needle'" }
}
Write-Output 'capability surface + card + layout probe render (UIA reads)'

$rect = Get-CardRect $window
Write-Output "probe: $($rect.Raw)"

if ($State -eq 'lit') {
    # Drive the state through REAL input: right-click quick-toggle (the user path a
    # regression would break), then confirm the operation actually started (tick advances).
    $cx = [int]($rect.X + $rect.W / 2); $cy = [int]($rect.Y + $rect.H / 2)
    [VerifyNative]::SetCursorPos($cx, $cy) | Out-Null
    Start-Sleep -Milliseconds 200
    [VerifyNative]::mouse_event([VerifyNative]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
    [VerifyNative]::mouse_event([VerifyNative]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    $tick1 = ((Get-Texts $window) | Where-Object { $_ -match '^demo\.status-ticker: tick (\d+)$' })
    if ($null -eq $tick1) { Fail 'tick text did not appear after right-click (state drive failed)' }
    Start-Sleep -Seconds 2
    $tick2 = ((Get-Texts $window) | Where-Object { $_ -match '^demo\.status-ticker: tick (\d+)$' })
    if ($tick1 -eq $tick2) { Fail "tick did not advance ($tick1)" }
    Write-Output "state drive: tick $tick1 -> $tick2"
    $rect = Get-CardRect $window
}

# Park the mouse off the card so :pointerover never leaks into a capture.
[VerifyNative]::SetCursorPos($rect.X + $rect.W + 200, $rect.Y + $rect.H + 200) | Out-Null
Start-Sleep -Milliseconds 400

if ($Surface -eq 'dashboard-card') {
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
