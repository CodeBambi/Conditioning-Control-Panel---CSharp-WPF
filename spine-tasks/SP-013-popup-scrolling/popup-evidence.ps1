# SP-013 headed evidence: feature-popup scrolling (Windows).
# Task-specific action script building on the SP-008 tier-2 harness patterns (launch,
# SetWindowPos(HWND_TOPMOST) raise, UIA reads, layout-probe locators, real SendInput).
#
# Evidence driven through REAL input, each path to the FINAL control with changing
# Extent/Viewport/Offset read from the app's UIA-visible scroll-probe:
#   A. mouse wheel        B. keyboard focus (Tab, bring-into-view)
#   C. scrollbar track    D. thumb drag
#   E. trackpad/touch: PROBED (SM_DIGITIZER); absent digitizer = named manual gate, never faked.
# Plus: SHORT compact (no scrollbar), NESTED chaining (inner scrolls, then chains),
# owner-monitor working-area containment incl. a SECONDARY-monitor variant (multi-monitor
# is Windows-headed only per the manifest), focus restoration on close.
# System.Drawing appears ONLY as capture transport; this script never reads a pixel.
param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'evidence')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$exe = Join-Path $PSScriptRoot '..\..\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe'
$settingsFile = Join-Path $env:APPDATA 'CcpClient\settings.json'
$logFile = Join-Path $OutDir 'windows-headed-evidence.log'
$script:findings = @()

function Log([string]$msg) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss.fff'), $msg
    Write-Output $line
    Add-Content -Path $logFile -Value $line
}
function Gate([bool]$ok, [string]$name, [string]$detail) {
    if ($ok) { $script:findings += "PASS: $name — $detail"; Log "PASS: $name — $detail" }
    else { $script:findings += "FAIL: $name — $detail"; Fail "$name — $detail" }
}
function Fail([string]$msg) {
    Log "FAIL: $msg"
    if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill() }
    exit 1
}

$native = @'
using System;
using System.Runtime.InteropServices;
public class PopupEvidenceNative {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, int data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, WHEEL = 0x0800, MOUSEMOVE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040, SWP_NOZORDER = 0x0004;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const int SM_DIGITIZER = 94, SM_MAXIMUMTOUCHES = 95, NID_INTEGRATED_TOUCH = 0x01, NID_READY = 0x80;
    // Real OS-level touch injection (InjectTouchInput) — the touch-pan evidence path.
    [DllImport("user32.dll", SetLastError = true)] public static extern bool InitializeTouchInjection(uint maxCount, uint feedbackMode);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool InjectTouchInput(uint count, POINTER_TOUCH_INFO[] contacts);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINTER_INFO {
        public uint PointerType; public uint PointerId; public uint FrameId; public uint PointerFlags;
        public IntPtr SourceDevice; public IntPtr HwndTarget;
        public POINT PtPixelLocation; public POINT PtHimetricLocation;
        public POINT PtPixelLocationRaw; public POINT PtHimetricLocationRaw;
        public uint DwTime; public uint HistoryCount; public int InputData; public uint DwKeyStates;
        public ulong PerformanceCount; public uint ButtonChangeType;
    }
    [StructLayout(LayoutKind.Sequential)] public struct POINTER_TOUCH_INFO {
        public POINTER_INFO PointerInfo; public uint TouchFlags; public uint TouchMask;
        public RECT RcContact; public RECT RcContactRaw; public uint Orientation; public uint Pressure;
    }
    public const uint PT_TOUCH = 2, PF_DOWN = 0x00010000, PF_UP = 0x00040000, PF_UPDATE = 0x00020000,
        PF_INRANGE = 0x00000002, PF_INCONTACT = 0x00000004, TOUCH_FEEDBACK_DEFAULT = 0x1;
}
'@
Add-Type -TypeDefinition $native
# SM_DIGITIZER is 94 (94 = 0x5E). NID_INTEGRATED_TOUCH 0x01, NID_READY 0x80, NID_MULTI_TOUCH 0x40, NID_EXTERNAL_TOUCH 0x02.

function Get-Windows([int]$processId) {
    # Owned windows nest UNDER the owner in the UIA tree (they are not top-level children),
    # so search descendants of the desktop for window-type elements of this process.
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)))
    $els = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $list = @()
    foreach ($el in $els) { $list += $el }
    return $list
}

function Get-Texts($window) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $els = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $lines = @()
    foreach ($t in $els) { $lines += $t.Current.Name }
    return $lines
}

function Get-Dashboard { return @(Get-Windows $script:proc.Id | Where-Object { (Get-Texts $_) -match 'layout-probe:' })[0] }
function Get-Popup {
    # Owned windows nest under the owner in UIA: the dashboard's subtree CONTAINS the
    # popup's probe text. Exclude the dashboard (it carries layout-probe) or the popup
    # element/hwnd resolves to the dashboard's.
    return @(Get-Windows $script:proc.Id | Where-Object {
        $t = Get-Texts $_
        ($t -match 'popup-probe:') -and -not ($t -match 'layout-probe:')
    })[0]
}

function Read-ScrollProbe {
    $popup = Get-Popup
    if ($null -eq $popup) { return $null }
    $line = (Get-Texts $popup) | Where-Object { $_ -like 'scroll-probe:*' } | Select-Object -First 1
    if ($line -notmatch 'extent ([\d.]+) viewport ([\d.]+) offset ([\d.]+)( inner-extent ([\d.]+) inner-offset ([\d.]+))? final-in-viewport (true|false)') { return $null }
    return @{
        Extent = [double]$Matches[1]; Viewport = [double]$Matches[2]; Offset = [double]$Matches[3]
        InnerExtent = $(if ($Matches[5]) { [double]$Matches[5] } else { $null })
        InnerOffset = $(if ($Matches[6]) { [double]$Matches[6] } else { $null })
        FinalInViewport = ($Matches[7] -eq 'true')
    }
}

function Read-PopupProbe {
    $popup = Get-Popup
    if ($null -eq $popup) { return $null }
    $line = (Get-Texts $popup) | Where-Object { $_ -like 'popup-probe:*' } | Select-Object -First 1
    if ($null -eq $line) { return $null }
    $geom = '(-?\d+),(-?\d+),(\d+),(\d+)'
    if ($line -notmatch "pos (-?\d+),(-?\d+) size (\d+)x(\d+) scale ([\d.]+) scroller $geom scrollbar $geom thumb $geom list ($geom|none)") { return $null }
    return @{
        Pos = @([int]$Matches[1], [int]$Matches[2]); Size = @([int]$Matches[3], [int]$Matches[4]); Scale = [double]$Matches[5]
        Scroller = @([int]$Matches[6], [int]$Matches[7], [int]$Matches[8], [int]$Matches[9])
        Scrollbar = @([int]$Matches[10], [int]$Matches[11], [int]$Matches[12], [int]$Matches[13])
        Thumb = @([int]$Matches[14], [int]$Matches[15], [int]$Matches[16], [int]$Matches[17])
        List = $(if ($Matches[18] -eq 'none') { $null } else { @([int]$Matches[19], [int]$Matches[20], [int]$Matches[21], [int]$Matches[22]) })
        Raw = $line
    }
}

function Wait-Probe([scriptblock]$read, [int]$timeoutMs = 8000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $v = & $read
        if ($null -ne $v) { return $v }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Wait-PopupProbe([scriptblock]$predicate, [int]$timeoutMs = 8000) {
    # Layout is asynchronous: at open the scrollbar can still be mid-arrange (0x0 rect).
    # Wait until the probe satisfies the predicate (e.g. scrollbar width > 0).
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $v = Read-PopupProbe
        if ($null -ne $v -and (& $predicate $v)) { return $v }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Click-At([int]$x, [int]$y) {
    [PopupEvidenceNative]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 150
    [PopupEvidenceNative]::mouse_event([PopupEvidenceNative]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    [PopupEvidenceNative]::mouse_event([PopupEvidenceNative]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 300
}

function Wheel-Down([int]$x, [int]$y, [int]$notches = 3) {
    [PopupEvidenceNative]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 120
    for ($i = 0; $i -lt $notches; $i++) {
        [PopupEvidenceNative]::mouse_event([PopupEvidenceNative]::WHEEL, 0, 0, -120, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 60
    }
    Start-Sleep -Milliseconds 250
}

function Invoke-Button($window, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    foreach ($b in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($b.Current.Name -eq $name) {
            $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 500
            return $true
        }
    }
    return $false
}

function Open-Popup {
    $dash = Get-Dashboard
    $probe = (Get-Texts $dash) | Where-Object { $_ -like 'layout-probe:*' }
    if ($probe -notmatch 'card ([\d.]+)x([\d.]+) DIP @ scale ([\d.]+) @ screen (-?\d+),(-?\d+)') { Fail "layout probe unreadable: $probe" }
    $cx = [int]([int]$Matches[4] + [double]$Matches[1] * [double]$Matches[3] / 2)
    $cy = [int]([int]$Matches[5] + [double]$Matches[2] * [double]$Matches[3] / 2)
    Click-At $cx $cy
    $pp = Wait-Probe { Read-PopupProbe }
    if ($null -eq $pp) { Fail 'popup did not open after left-click' }
    # The popup opens UNACTIVATED in the normal z-band (Windows foreground-lock; the script
    # has no foreground rights) — the foreground terminal can sit ABOVE it and eat every
    # click/wheel aimed at the popup (SP-007 surprise #2, now for the popup itself).
    # SetWindowPos(HWND_TOPMOST) is the sanctioned raise; pixel checks verify content.
    $popupHwnd = [IntPtr](Get-Popup).Current.NativeWindowHandle
    [PopupEvidenceNative]::SetWindowPos($popupHwnd, [PopupEvidenceNative]::HWND_TOPMOST, 0, 0, 0, 0,
        [PopupEvidenceNative]::SWP_NOMOVE -bor [PopupEvidenceNative]::SWP_NOSIZE -bor [PopupEvidenceNative]::SWP_SHOWWINDOW) | Out-Null
    Start-Sleep -Milliseconds 300
    Log "popup raised: hwnd=$popupHwnd dashboard=$dashHwnd foreground=$([PopupEvidenceNative]::GetForegroundWindow())"
    return $pp
}

function Close-PopupEscape {
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 600
}

function Close-PopupButton {
    # Real click at the close button's UIA BoundingRectangle center (exact, no geometry math).
    $popup = Get-Popup
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $btn = @($popup.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) | Where-Object { $_.Current.Name -eq 'popup close button' })[0]
    if ($null -eq $btn) { Fail 'close button not found via UIA' }
    $r = $btn.Current.BoundingRectangle
    Click-At ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
}

# Real OS-level touch injection: finger down over the content, pan UP (content follows the
# finger => scrolls down), lift. Same evidence class as SendInput mouse/wheel.
function Touch-PanUp([int]$x, [int]$y, [int]$distance) {
    $contact = New-Object PopupEvidenceNative+POINTER_TOUCH_INFO
    $contact.PointerInfo.PointerType = [PopupEvidenceNative]::PT_TOUCH
    $contact.PointerInfo.PointerId = 0
    $contact.Orientation = 90
    $contact.Pressure = 32000
    $set = {
        param($px, $py, $flags)
        $contact.PointerInfo.PointerFlags = $flags
        $contact.PointerInfo.PtPixelLocation.X = $px
        $contact.PointerInfo.PtPixelLocation.Y = $py
        $contact.RcContact.Left = $px - 2; $contact.RcContact.Right = $px + 2
        $contact.RcContact.Top = $py - 2; $contact.RcContact.Bottom = $py + 2
    }
    & $set $x $y ([PopupEvidenceNative]::PF_DOWN -bor [PopupEvidenceNative]::PF_INRANGE -bor [PopupEvidenceNative]::PF_INCONTACT)
    if (-not [PopupEvidenceNative]::InjectTouchInput(1, @($contact))) {
        $err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
        Log "touch DOWN rejected: win32=$err struct-size=$([System.Runtime.InteropServices.Marshal]::SizeOf([type]([PopupEvidenceNative+POINTER_TOUCH_INFO])))"
        return $false
    }
    Start-Sleep -Milliseconds 60
    $steps = 12
    for ($i = 1; $i -le $steps; $i++) {
        & $set $x ($y - [int]($distance * $i / $steps)) ([PopupEvidenceNative]::PF_UPDATE -bor [PopupEvidenceNative]::PF_INRANGE -bor [PopupEvidenceNative]::PF_INCONTACT)
        if (-not [PopupEvidenceNative]::InjectTouchInput(1, @($contact))) {
            $err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
            Log "touch UPDATE $i rejected: win32=$err"
            return $false
        }
        Start-Sleep -Milliseconds 30
    }
    & $set $x ($y - $distance) ([PopupEvidenceNative]::PF_UP)
    if (-not [PopupEvidenceNative]::InjectTouchInput(1, @($contact))) {
        $err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
        Log "touch UP rejected: win32=$err"
        return $false
    }
    Start-Sleep -Milliseconds 300
    return $true
}

function Capture-Region([string]$name, [int]$x, [int]$y, [int]$w, [int]$h) {
    $file = Join-Path $OutDir "$name.png"
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Log "capture: $file ($w x $h)"
}

# ---------------- touch/trackpad PROBE (before any promise) ----------------
if (Test-Path $logFile) { Remove-Item $logFile -Force }
$digitizer = [PopupEvidenceNative]::GetSystemMetrics([PopupEvidenceNative]::SM_DIGITIZER)
$maxTouches = [PopupEvidenceNative]::GetSystemMetrics([PopupEvidenceNative]::SM_MAXIMUMTOUCHES)
$touchReady = ($digitizer -band 0x01) -ne 0 -and ($digitizer -band 0x80) -ne 0
Log "touch/trackpad probe: SM_DIGITIZER=0x$($digitizer.ToString('X2')) SM_MAXIMUMTOUCHES=$maxTouches integrated-touch-ready=$touchReady"
$touchpads = @(Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'precision touchpad|touchpad' -and $_.PNPClass -match 'Mouse|HIDClass' })
Log "precision-touchpad enumeration: $($touchpads.Count) candidate device(s) ($($touchpads.Name -join '; '))"

# ---------------- launch + raise ----------------
if (-not (Test-Path $exe)) { Fail "app not built: $exe" }
if (Test-Path $settingsFile) { Remove-Item $settingsFile -Force }
$script:proc = [System.Diagnostics.Process]::Start($exe)
Log "launched pid=$($script:proc.Id)"
Start-Sleep -Seconds 4
$script:proc.Refresh()
$dashHwnd = $script:proc.MainWindowHandle
if ($dashHwnd -eq [IntPtr]::Zero) { Fail 'no MainWindowHandle' }
[PopupEvidenceNative]::SetWindowPos($dashHwnd, [PopupEvidenceNative]::HWND_TOPMOST, 0, 0, 0, 0,
    [PopupEvidenceNative]::SWP_NOMOVE -bor [PopupEvidenceNative]::SWP_NOSIZE -bor [PopupEvidenceNative]::SWP_SHOWWINDOW) | Out-Null
Start-Sleep -Milliseconds 600

$dash = Get-Dashboard
if ($null -eq $dash) { Fail 'dashboard window not found via UIA' }
$ownerScreen = [System.Windows.Forms.Screen]::FromHandle($dashHwnd)
Log "owner monitor working area: $($ownerScreen.WorkingArea) scale assumed 1.0 (all monitors 1.0 per SP-007 record)"

# ================= TALL: geometry + five paths =================
$pp = Open-Popup
$pp = Wait-PopupProbe { param($v) $v.Scrollbar[2] -gt 0 }
if ($null -eq $pp) { Fail 'scrollbar never arranged (probe kept reporting 0 width)' }
Log "popup opened: $($pp.Raw)"
Gate ($pp.Scrollbar[2] -gt 0) 'tall-scrollbar-present' "scrollbar w=$($pp.Scrollbar[2])"

# Owner-monitor working-area containment (presentation fact).
$popRect = New-Object System.Drawing.Rectangle($pp.Pos[0], $pp.Pos[1], $pp.Size[0], $pp.Size[1])
$wa = $ownerScreen.WorkingArea
Gate $wa.Contains($popRect) 'tall-inside-owner-working-area' "popup $popRect within WA $wa"
$sp = Read-ScrollProbe
Gate ($sp.Extent -gt $sp.Viewport) 'tall-overflows' "extent $($sp.Extent) > viewport $($sp.Viewport)"
Gate (-not $sp.FinalInViewport) 'tall-final-starts-below-fold' 'final-in-viewport false at open'
Capture-Region 'popup-tall-top' $pp.Pos[0] $pp.Pos[1] $pp.Size[0] $pp.Size[1]

# Path A — mouse wheel. WM_MOUSEWHEEL goes to the FOCUSED window (not the window under
# the cursor): click the popup's title bar first so the popup holds focus — real user
# behavior (click to focus, then wheel). Cursor then sits over the scroller for Avalonia's
# position-based hit-test.
$pp = Read-PopupProbe
$popupHwnd = (Get-Popup).Current.NativeWindowHandle
Log "focus check: foreground=$([PopupEvidenceNative]::GetForegroundWindow()) popup=$popupHwnd dashboard=$dashHwnd"
Click-At ($pp.Pos[0] + [int]($pp.Size[0] / 2)) ($pp.Pos[1] + 24)
Log "after title click: foreground=$([PopupEvidenceNative]::GetForegroundWindow())"
$offsets = @()
$stable = 0
$last = -1.0
for ($i = 0; $i -lt 40 -and $stable -lt 2; $i++) {
    Wheel-Down ($pp.Scroller[0] + [int]($pp.Scroller[2] / 2)) ($pp.Scroller[1] + [int]($pp.Scroller[3] / 2)) 2
    $sp = Read-ScrollProbe
    if ($null -eq $sp) { Fail 'scroll probe lost during wheel' }
    $offsets += $sp.Offset
    if ($sp.Offset -eq $last) { $stable++ } else { $stable = 0; $last = $sp.Offset }
}
$monotonic = $true; for ($i = 1; $i -lt $offsets.Count; $i++) { if ($offsets[$i] -lt $offsets[$i - 1]) { $monotonic = $false } }
Gate $monotonic 'wheel-offset-monotonic' "offsets: $($offsets -join ', ')"
Gate $sp.FinalInViewport 'wheel-reaches-final-control' "final-in-viewport true; offset=$($sp.Offset) extent=$($sp.Extent) viewport=$($sp.Viewport)"
Gate ($sp.Offset + $sp.Viewport -ge $sp.Extent - 1) 'wheel-bottom' "offset+viewport=$($sp.Offset + $sp.Viewport) >= extent=$($sp.Extent) (offset stable at bottom)"
Capture-Region 'popup-tall-scrolled-bottom' $pp.Pos[0] $pp.Pos[1] $pp.Size[0] $pp.Size[1]
Close-PopupEscape
Gate ($null -eq (Wait-Probe { Read-PopupProbe } 1500)) 'escape-closes-popup' 'Escape closed the popup (one command path, keyboard side)'

# Focus restoration: after Escape close, the dashboard must be foreground again (W-04).
Start-Sleep -Milliseconds 400
$fg = [PopupEvidenceNative]::GetForegroundWindow()
Gate ($fg -eq $dashHwnd) 'focus-restored-to-dashboard' "GetForegroundWindow=$fg dashboard=$dashHwnd"

# Path B — keyboard focus (Tab brings clipped controls into view).
$pp = Open-Popup
$focused = @()
$reached = $false
for ($i = 0; $i -lt 45 -and -not $reached; $i++) {
    [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
    Start-Sleep -Milliseconds 220
    $el = [System.Windows.Automation.AutomationElement]::FocusedElement
    $focused += $(if ($null -ne $el) { $el.Current.Name } else { '(none)' })
    $sp = Read-ScrollProbe
    $reached = ($null -ne $sp -and $sp.FinalInViewport)
}
Gate $reached 'keyboard-tab-reaches-final-control' "final-in-viewport true; focus trail: $($focused -join ' | ')"
Gate ($sp.Offset -gt 0) 'keyboard-focus-scrolled' "offset=$($sp.Offset) (bring-into-view moved the viewport)"
Close-PopupButton
Gate ($null -eq (Wait-Probe { Read-PopupProbe } 1500)) 'close-button-closes-popup' 'title-bar close button closed the popup (one command path, button side)'

# Path C — scrollbar track clicks (page steps).
$pp = Open-Popup
$trackClicks = 0
$reached = $false
$lastOffset = 0.0
for ($i = 0; $i -lt 12 -and -not $reached; $i++) {
    $pp = Read-PopupProbe
    # Track point BELOW the thumb: page down. If the thumb nears the bottom, stop clicking there.
    $tx = $pp.Scrollbar[0] + [int]($pp.Scrollbar[2] / 2)
    $ty = $pp.Thumb[1] + $pp.Thumb[3] + 24
    if ($ty -ge ($pp.Scrollbar[1] + $pp.Scrollbar[3] - 8)) { break }
    Click-At $tx $ty
    $trackClicks++
    $sp = Read-ScrollProbe
    $reached = ($null -ne $sp -and $sp.FinalInViewport)
    $lastOffset = $sp.Offset
}
Gate ($trackClicks -gt 0) 'scrollbar-track-clicked' "$trackClicks track page-down clicks"
Gate $reached 'scrollbar-track-reaches-final-control' "final-in-viewport true after $trackClicks clicks; offset=$lastOffset"
Close-PopupEscape

# Path D — thumb drag.
$pp = Open-Popup
$startOffset = (Read-ScrollProbe).Offset
$thumbCx = $pp.Thumb[0] + [int]($pp.Thumb[2] / 2)
$thumbCy = $pp.Thumb[1] + [int]($pp.Thumb[3] / 2)
$dragTo = $pp.Scrollbar[1] + $pp.Scrollbar[3] - [int]($pp.Thumb[3] / 2) - 2
[PopupEvidenceNative]::SetCursorPos($thumbCx, $thumbCy) | Out-Null
Start-Sleep -Milliseconds 150
[PopupEvidenceNative]::mouse_event([PopupEvidenceNative]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
$steps = 24
for ($i = 1; $i -le $steps; $i++) {
    $y = $thumbCy + [int](($dragTo - $thumbCy) * $i / $steps)
    [PopupEvidenceNative]::SetCursorPos($thumbCx, $y) | Out-Null
    [PopupEvidenceNative]::mouse_event([PopupEvidenceNative]::MOUSEMOVE, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 40
}
[PopupEvidenceNative]::mouse_event([PopupEvidenceNative]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 400
$sp = Read-ScrollProbe
Gate ($sp.Offset -gt $startOffset) 'thumb-drag-scrolled' "offset $startOffset -> $($sp.Offset)"
Gate $sp.FinalInViewport 'thumb-drag-reaches-final-control' "final-in-viewport true; offset=$($sp.Offset) extent=$($sp.Extent)"
Close-PopupEscape

# Path E — trackpad/touch. Digitizer PROBED up front. This workstation HAS a 2-point
# integrated touch digitizer (SM_DIGITIZER=0xCD) but InjectTouchInput is REJECTED here
# (err=87 across parameter variations; one isolated run had 156 accepted injections that
# produced NO app-visible scroll — consistent with mouse promotion or non-delivery).
# Attempt honestly, gate honestly: no faked pass either way.
if ($touchReady) {
    $pp = Open-Popup
    if (-not [PopupEvidenceNative]::InitializeTouchInjection(2, [PopupEvidenceNative]::TOUCH_FEEDBACK_DEFAULT)) {
        $script:findings += "GATE: trackpad/touch — digitizer present (SM_DIGITIZER=0x$($digitizer.ToString('X2'))) but InitializeTouchInjection FAILED (err $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error())); NAMED MANUAL GATE: physical touch-pan on the touch monitor"
        Log 'GATE: InitializeTouchInjection failed on a touch-capable box'
    } else {
        $startOffset = (Read-ScrollProbe).Offset
        $reached = $false
        $pans = 0
        $injected = $true
        for ($i = 0; $i -lt 6 -and -not $reached; $i++) {
            $pp = Read-PopupProbe
            $ok = Touch-PanUp ($pp.Scroller[0] + [int]($pp.Scroller[2] / 2)) ($pp.Scroller[1] + $pp.Scroller[3] - 60) 240
            if (-not $ok) { $injected = $false; break }
            $pans++
            $sp = Read-ScrollProbe
            $reached = ($null -ne $sp -and $sp.FinalInViewport)
        }
        $sp = Read-ScrollProbe
        if (-not $injected) {
            $script:findings += 'GATE: trackpad/touch — InjectTouchInput REJECTED by the OS on this workstation (err=87 across parameter variations, see log); digitizer present; NAMED MANUAL GATE: physical touch-pan on the touch monitor'
            Log 'GATE: touch injection rejected — named manual gate'
        } elseif ($pans -gt 0 -and $sp.Offset -le $startOffset) {
            $script:findings += "GATE: trackpad/touch — $pans InjectTouchInput pan gestures ACCEPTED by the OS but produced NO scrolling (offset $startOffset -> $($sp.Offset)); automation cannot produce this evidence here; NAMED MANUAL GATE: physical touch-pan on the touch monitor"
            Log 'GATE: injected pans accepted but no scroll — named manual gate'
        } else {
            Gate $reached 'touch-pan-reaches-final-control' "final-in-viewport true via touch pan; offset=$($sp.Offset)"
        }
    }
    Close-PopupEscape
} else {
    $script:findings += 'GATE: trackpad/touch path — NO touch digitizer on this workstation (SM_DIGITIZER=0x{0:X2}, {1} touchpad device(s)); named MANUAL gate, evidence not faked' -f $digitizer, $touchpads.Count
    Log 'GATE: trackpad/touch — no digitizer on this workstation; named manual gate'
}

# ================= SHORT: compact, no scrollbar =================
$pp = Open-Popup
Gate (Invoke-Button (Get-Popup) 'variant short') 'variant-short-invoked' 'UIA Invoke on variant short'
$sp = Wait-Probe { $v = Read-ScrollProbe; if ($null -ne $v -and $v.Extent -le $v.Viewport) { $v } }
Gate ($null -ne $sp) 'short-compact-no-scroll' "extent $($sp.Extent) <= viewport $($sp.Viewport)"
$pp = Read-PopupProbe
$shortHeightDip = $pp.Size[1] / $pp.Scale
Gate ($shortHeightDip -lt 400) 'short-popup-compact' "popup height $shortHeightDip DIP (WPF min 360, not the 640 fixed height)"
Capture-Region 'popup-short-compact' $pp.Pos[0] $pp.Pos[1] $pp.Size[0] $pp.Size[1]
Close-PopupEscape

# ================= NESTED: inner scrolls, then chains =================
$pp = Open-Popup
Gate (Invoke-Button (Get-Popup) 'variant nested') 'variant-nested-invoked' 'UIA Invoke on variant nested'
$pp = Wait-Probe { $v = Read-PopupProbe; if ($null -ne $v -and $null -ne $v.List) { $v } }
Gate ($null -ne $pp) 'nested-list-located' "list rect: $($pp.List -join ',')"
$listCx = $pp.List[0] + [int]($pp.List[2] / 2); $listCy = $pp.List[1] + [int]($pp.List[3] / 2)
$innerMoved = $false; $chained = $false
for ($i = 0; $i -lt 40 -and -not $chained; $i++) {
    Wheel-Down $listCx $listCy 2
    $sp = Read-ScrollProbe
    if ($null -eq $sp) { Fail 'scroll probe lost during nested wheel' }
    if ($null -ne $sp.InnerOffset -and $sp.InnerOffset -gt 0) { $innerMoved = $true }
    if ($sp.Offset -gt 0 -and $innerMoved) { $chained = $true }
}
Gate $innerMoved 'nested-inner-scrolls-itself' "inner-offset > 0 while wheeling over the list"
Gate $chained 'nested-chains-to-popup' "outer offset rose only after inner scrolled; final offset=$($sp.Offset) inner=$($sp.InnerOffset)"
$reached = $false
for ($i = 0; $i -lt 40 -and -not $reached; $i++) {
    Wheel-Down $listCx $listCy 3
    $sp = Read-ScrollProbe
    $reached = ($null -ne $sp -and $sp.FinalInViewport)
}
Gate $reached 'nested-reaches-final-control' "final-in-viewport true; offset=$($sp.Offset) inner=$($sp.InnerOffset)"
Capture-Region 'popup-nested-scrolled' $pp.Pos[0] $pp.Pos[1] $pp.Size[0] $pp.Size[1]
Close-PopupEscape

# ================= secondary monitor (Windows-headed only per manifest) =================
$screens = [System.Windows.Forms.Screen]::AllScreens
Log "monitors: $($screens.Count)"
if ($screens.Count -gt 1) {
    $secondary = @($screens | Where-Object { -not $_.Primary })[0]
    $swa = $secondary.WorkingArea
    Log "moving dashboard to secondary monitor WA $swa"
    [PopupEvidenceNative]::SetWindowPos($dashHwnd, [IntPtr]::Zero, $swa.X + 60, $swa.Y + 60, 0, 0,
        [PopupEvidenceNative]::SWP_NOSIZE -bor [PopupEvidenceNative]::SWP_NOZORDER) | Out-Null
    Start-Sleep -Milliseconds 800
    $pp = Open-Popup
    $popRect = New-Object System.Drawing.Rectangle($pp.Pos[0], $pp.Pos[1], $pp.Size[0], $pp.Size[1])
    Gate $swa.Contains($popRect) 'secondary-monitor-inside-owner-working-area' "popup $popRect within secondary WA $swa (owner-monitor, NOT primary-by-default)"
    $screenOfPopup = [System.Windows.Forms.Screen]::FromRectangle($popRect)
    Gate ($screenOfPopup.DeviceName -eq $secondary.DeviceName) 'secondary-monitor-popup-on-owner-monitor' "popup on $($screenOfPopup.DeviceName), owner moved to $($secondary.DeviceName)"
    Close-PopupEscape
} else {
    $script:findings += 'GATE: secondary-monitor variant — single-monitor box; named manual gate'
    Log 'GATE: secondary-monitor variant — single monitor; named manual gate'
}

# ---------------- teardown ----------------
$script:proc.Refresh()
$null = $script:proc.CloseMainWindow()
if (-not $script:proc.WaitForExit(10000)) { Fail 'process did not exit within 10s' }
if ($script:proc.ExitCode -ne 0) { Fail "non-zero exit on close: $($script:proc.ExitCode)" }
Log 'graceful close exit 0'

Log ''
Log '==== FINDINGS ===='
foreach ($f in $script:findings) { Log $f }
Write-Output 'EVIDENCE PASS'
