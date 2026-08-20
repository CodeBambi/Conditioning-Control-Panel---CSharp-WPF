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
#
# SP-122 — THE RACK, and two things this script was missing before it could be trusted with one.
#
# 1. IT NOW TAKES THE MACHINE-WIDE REAL-DESKTOP LEASE. This script puts a top-most window on the
#    interactive desktop and reads that desktop back, which is exactly what the test suite's
#    RealDesktopCollection serialises through %TEMP%/ccp-real-desktop.lease — and this script ran
#    outside that, so a capture could race a floor run. SP-107 measured what that costs: a failure
#    that read "Expected 0, Actual 676161", one whole FOREIGN run's flash counted as this one's.
# 2. IT NOW FENCES THE SCREEN READ. CopyFromScreen was called with no happens-before edge against
#    the compositor. SP-116 measured 34 misses in 1200 unfenced reads and 0 in 1500 fenced;
#    DwmFlush alone was the whole effect. An unfenced screen read is a defect, not a flake.
#
# The rack surfaces need NO product probe, and that is a finding rather than a convenience: every
# rack row is a RadioButton, so Avalonia gives it a real UIA peer carrying an AutomationId, a
# screen BoundingRectangle and SelectionItemPattern.IsSelected. The layout probe this script reads
# for rail doors exists because SP-007's anchor was a demonstrator CARD — a Border/Grid, which has
# no peer — and SP-091 re-anchored onto RadioButtons without revisiting it. Both channels are read
# here and they agree exactly (probe 174.9x44.0 DIP @ scale 1.75 == UIA 306x77 at the same origin).
# A rack probe was authorised and REFUSED: fifteen probe lines in the bottom-docked footer add
# 15 x 23.4 = 351 px to a rack viewport measured at 965 px, which pushes five of the fifteen rows
# it exists to photograph below the scroll fold. A probe that moves the thing it observes is not
# an observation seam.
# Usage: pwsh client/tools/verify/capture.ps1 -Surface rail-door -State selected
#        pwsh client/tools/verify/capture.ps1 -Surface dashboard -State unselected
#        pwsh client/tools/verify/capture.ps1 -Surface rack-row -State selected
#        pwsh client/tools/verify/capture.ps1 -Surface rack-row-dot -State armed
param(
    [Parameter(Mandatory)][ValidateSet('dashboard', 'rail-door', 'rack-row', 'rack-row-dot')] [string]$Surface,
    [Parameter(Mandatory)][ValidateSet('unselected', 'selected', 'off', 'armed')] [string]$State
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$verifyDir = $PSScriptRoot
$shots = Join-Path $verifyDir 'artifacts'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
$exe = Join-Path $verifyDir '..\..\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe'
# The deterministic-start set. It was ONE file, and that had been incomplete since SP-098: the rack
# rows' module dials do not live in settings.json, they live in session_preset.json in the same data
# directory (SessionPresetDocument.FileName, SessionParticipant.cs:96). Measured rather than
# reasoned — a `-State off` capture right-clicked Flash Images off, and the NEXT run's `-State
# armed` capture read back "Switched off." on what was supposed to be a cold start.
$stateFiles = @(
    (Join-Path $env:APPDATA 'CcpClient\settings.json'),
    (Join-Path $env:APPDATA 'CcpClient\session_preset.json')
)
$outFile = Join-Path $shots "windows-$Surface-$State.png"

# ValidateSet cannot express a PAIR, and an unpaired combination is not a typo the caller should
# have to debug from a pixel check: 'rack-row-dot -State selected' has no drive and would silently
# capture whatever the last state left behind. Refuse it by name here, once.
$statesFor = @{
    'dashboard'    = @('unselected', 'selected')
    'rail-door'    = @('unselected', 'selected')
    'rack-row'     = @('unselected', 'selected')
    'rack-row-dot' = @('off', 'armed')
}
if ($statesFor[$Surface] -notcontains $State) {
    Write-Output "FAIL: surface '$Surface' has no state '$State' (it has: $($statesFor[$Surface] -join ', '))"
    exit 1
}

if (-not (Test-Path $exe)) { Write-Output "FAIL: app not built: $exe"; exit 1 }

$native = @'
using System;
using System.Runtime.InteropServices;
public class VerifyNative {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    // The compositor fence. Identical to the one CcpClient.Tests' FlashPixelProbe.CaptureDesktop
    // takes before its own screen read (FlashPixelProbe.cs:235): DwmFlush blocks until the
    // compositor's NEXT PRESENT has consumed the outstanding surface updates, so it is an edge on
    // the producer's completion rather than a wait this harness chose a deadline for.
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    public const uint RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;
    public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
}
'@
Add-Type -TypeDefinition $native

# ---------------------------------------------------------------------------------------------
# SP-122 — the machine-wide real-desktop lease.
#
# Byte-for-byte the contract CcpClient.Tests' RealDesktopLease.TryTake uses
# (RealDesktopCollection.cs:110-118): FileMode.Create / FileAccess.Write / FileShare.Read, with
# "pid=<n>" written RAW into the stream. Raw matters — RealDesktopLease.HolderProcessId requires
# the file to start literally "pid=" (RealDesktopCollection.cs:148), so a StreamWriter's BOM or a
# trailing newline would make a contending floor run report "no readable holder" instead of naming
# this capture. Share mode Read, not None, for the same reason in the other direction: a contender
# can read WHO holds the desktop while it is held.
#
# A file handle rather than a Mutex because the OS closes it when the process dies, so a crashed
# capture cannot wedge the machine for the next run.
# ---------------------------------------------------------------------------------------------
$script:leasePath = Join-Path ([IO.Path]::GetTempPath()) 'ccp-real-desktop.lease'
$script:lease = $null

function Get-LeaseHolder([string]$path) {
    try {
        # FileShare.ReadWrite, exactly as RealDesktopCollection.cs:144 opens it. A reader that
        # granted only Read would itself be refused while the WRITER holds the file, and the whole
        # point of this read is to work while somebody else has the desktop.
        $reader = [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            $buffer = New-Object byte[] 64
            $read = $reader.Read($buffer, 0, $buffer.Length)
            $text = [Text.Encoding]::UTF8.GetString($buffer, 0, $read)
        }
        finally { $reader.Dispose() }
        if ($text.StartsWith('pid=')) { return $text.Substring(4) }
        return $null
    }
    catch { return $null }
}

function Release-Lease {
    if ($null -ne $script:lease) {
        $script:lease.Dispose()
        $script:lease = $null
        Write-Output 'real-desktop lease released'
    }
}

function Take-Lease {
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    $refusal = 'none recorded'
    while ($deadline.Elapsed.TotalSeconds -lt 300) {
        try {
            $script:lease = [IO.FileStream]::new(
                $script:leasePath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
            $identity = [Text.Encoding]::UTF8.GetBytes("pid=$PID")
            $script:lease.Write($identity, 0, $identity.Length)
            $script:lease.Flush()
            Write-Output "real-desktop lease held by pid=$PID (waited $([math]::Round($deadline.Elapsed.TotalSeconds, 1))s)"
            return
        }
        catch [IO.IOException] {
            $refusal = $_.Exception.Message
            Start-Sleep -Milliseconds 500
        }
        catch [UnauthorizedAccessException] {
            # An ACL, a read-only volume or a file-locking scanner — NOT a peer, and no peer
            # should be hunted for it (RealDesktopCollection.cs:127-132).
            Write-Output ("FAIL: the lease file could not be opened AT ALL ($($_.Exception.GetType().Name): " +
    "$($_.Exception.Message)). That is not another process holding the desktop.")
            exit 1
        }
    }

    $holder = Get-LeaseHolder $script:leasePath
    $who = if ($null -ne $holder) { "the lease file names process $holder as the holder" }
           else { 'the lease file names no readable holder, so WHO has the desktop is unknown' }
    Write-Output ("FAIL: could not take the real-desktop lease within $([int]$deadline.Elapsed.TotalSeconds)s. " +
    "This process is $PID; $who. Refusal: $refusal. A contended desktop is not a flake and must NOT be " +
    'captured around: the desktop is a singleton and this capture would photograph another run''s windows.')
    exit 1
}

function Fail([string]$msg) {
    Write-Output "FAIL: $msg"
    if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill() }
    Release-Lease
    exit 1
}

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

# The rack's SECOND gesture (StudioPage.axaml.cs:449-453 -> :559-569). The rack tells the user
# about it in its own hint text — "Right-click a row to flip that effect on or off" — and until
# SP-122 no run on a real desktop had ever performed it.
function RightClick-Rect($rect) {
    $cx = [int]($rect.X + $rect.W / 2); $cy = [int]($rect.Y + $rect.H / 2)
    [VerifyNative]::SetCursorPos($cx, $cy) | Out-Null
    Start-Sleep -Milliseconds 200
    [VerifyNative]::mouse_event([VerifyNative]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
    [VerifyNative]::mouse_event([VerifyNative]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
}

# ---------------------------------------------------------------------------------------------
# SP-122 — UIA element reads. THE RACK NEEDS NO PROBE.
#
# Every rack row is a RadioButton and Avalonia gives it a real automation peer: an AutomationId
# taken from x:Name, a screen BoundingRectangle, and SelectionItemPattern.IsSelected. So the three
# things a probe would have had to publish — where the row is, which row it is, and whether it is
# open — are already published, by the control itself, on the channel this script already reads
# (it has enumerated RadioButton peers since the SP-094 audit).
# ---------------------------------------------------------------------------------------------
function Get-Element($window, [string]$automationId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    $el = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $el) { Fail "no UIA element with AutomationId '$automationId'" }
    return $el
}

function Get-Rect($element) {
    $r = $element.Current.BoundingRectangle
    return @{ X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
}

function Get-Selected($element) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    return $pattern.Current.IsSelected
}

# The row's caption. A rack row's ONE Text descendant is its label, and its rect is the star
# column of the row's Grid — which is what makes the dot cell derivable without a probe.
function Get-RowLabelRect($row) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $labels = $row.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($labels.Count -ne 1) { Fail "rack row '$($row.Current.AutomationId)' has $($labels.Count) Text descendants; expected exactly 1 (its caption)" }
    return Get-Rect $labels[0]
}

# A rect is only capturable where it is really painted. UIA reports UNCLIPPED bounds and says
# IsOffscreen=False for them, which is measured, not supposed: RowIntensityRamp reports
# 501;1505;402;63 inside a window that ends at y=1470 and a rack viewport that ends at y=1140.
# Aiming CopyFromScreen there photographs the wallpaper and the check then reports on somebody's
# desktop background. Refuse instead.
function Assert-Inside($inner, $outer, [string]$what, [string]$container) {
    if ($inner.X -lt $outer.X -or $inner.Y -lt $outer.Y `
        -or ($inner.X + $inner.W) -gt ($outer.X + $outer.W) `
        -or ($inner.Y + $inner.H) -gt ($outer.Y + $outer.H)) {
        Fail ("$what at $($inner.X),$($inner.Y) $($inner.W)x$($inner.H) is not fully inside $container at " +
    "$($outer.X),$($outer.Y) $($outer.W)x$($outer.H) — it is clipped or scrolled away, and capturing " +
    'it would photograph whatever is really at those coordinates')
    }
}

function Assert-Route($window, [string]$route) {
    $texts = (Get-Texts $window) -join "`n"
    if ($texts -notmatch "route: $route") { Fail "the shell did not navigate to '$route' (state drive failed)" }
}

# Take the desktop BEFORE the app is launched: the window itself is the thing that must not
# contend with another run's windows, so the lease has to cover the launch and not just the read.
Take-Lease

# Deterministic start: remove the demo stores. This is what makes the SP-122 captures
# order-independent — the rack's right-click quick-toggle persists the module's enabled flag, and
# without the preset file in this set an 'off' capture leaks into the NEXT run's 'armed' capture.
foreach ($stateFile in $stateFiles) {
    if (Test-Path $stateFile) { Remove-Item $stateFile -Force }
}

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
if ($all -notlike '*route: studio*') { Fail "missing 'route: studio'" }

# DERIVE THE DOOR SET, NEVER HARD-CODE IT (SP-094 audit, 2026-08-18).
# This was a literal list of three door needles, written at SP-091 when three was the whole
# rail. SP-094 added a fourth door and did not widen it, so the harness stopped checking the
# only door that wave added -- while still printing "every rail door published a layout probe".
# A hard-coded list turns "every" into "the ones someone remembered", and it fails silently in
# the one direction that matters: a NEW door can go missing and this still passes.
#
# Both sides are now read from the running app: the rail's door buttons come from UIA, the
# probe lines come from the shell's own diagnostics, and they must agree. Add a door and this
# widens itself; break a door's probe and it fails naming that door.
$railDoors = @()
$btn = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::RadioButton)
foreach ($d in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btn)) {
    $n = $d.Current.Name
    if ($n -match '^(?<id>[a-z]+) door$') { $railDoors += $Matches['id'] }
}
if ($railDoors.Count -lt 1) { Fail 'no rail doors found in the UIA tree' }

$probed = [regex]::Matches($all, 'layout-probe: door (?<id>[a-z]+)') |
          ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique
foreach ($door in $railDoors) {
    if ($probed -notcontains $door) { Fail "rail door '$door' published no layout probe (probed: $($probed -join ', '))" }
}
Write-Output "shell mounted its default page; all $($railDoors.Count) rail doors published a layout probe ($($railDoors -join ', '))"

$windowRect = Get-Rect $window

if ($Surface -eq 'rack-row' -or $Surface -eq 'rack-row-dot') {
    # =========================================================================================
    # SP-122 — THE RACK. The shell opens on Studio (ShellRoutes.Default), so the rack is already
    # in front of us and no navigation is needed; navigating anywhere else would unmount the page
    # and take its peers with it.
    #
    # The captured row is Flash Images: first row of the first group, so it is above the scroll
    # fold at every window size this shell has, and it is the row whose module can be armed
    # without anything appearing on the screen ("Armed. Nothing is scheduled until the session
    # starts." — StudioPage.axaml.cs:1655).
    # =========================================================================================
    $scale = (Get-DoorRect $window 'studio').Scale
    $viewport = Get-Rect (Get-Element $window 'RackScroll')
    $row = Get-Element $window 'RowFlashImages'
    $rowRect = Get-Rect $row
    Write-Output ("rack: viewport $($viewport.X),$($viewport.Y) $($viewport.W)x$($viewport.H); " +
    "row RowFlashImages $($rowRect.X),$($rowRect.Y) $($rowRect.W)x$($rowRect.H) @ scale $scale (UIA, no probe)")

    # The rack SCROLLS (SP-112), and UIA reports unclipped bounds with IsOffscreen=False for rows
    # that are scrolled out of it. Both containments, or nothing is captured.
    Assert-Inside $rowRect $viewport 'rack row RowFlashImages' 'the rack viewport (RackScroll)'
    Assert-Inside $rowRect $windowRect 'rack row RowFlashImages' 'the shell window'

    if ($Surface -eq 'rack-row' -and $State -eq 'unselected') {
        # The captured row is genuinely NOT the open one — read, not assumed.
        if (Get-Selected $row) { Fail 'RowFlashImages is already selected on a cold start; the unselected capture would be a lie' }
        Write-Output 'state: RowFlashImages IsSelected=False on a cold start (UIA SelectionItemPattern)'
    }
    else {
        # Every other rack state starts by OPENING the row, through real input, because the dot
        # states need the module panel: FlashLiveState lives inside FlashModulePanel, whose
        # IsVisible is gated on this row being checked (StudioPage.axaml.cs:540). A right-click
        # alone sets Handled and deliberately does NOT select (:556-565), so the confirmation read
        # would be unreachable without this left-click first.
        Click-Rect $rowRect
        $row = Get-Element $window 'RowFlashImages'
        if (-not (Get-Selected $row)) { Fail 'the left-click did not open RowFlashImages (state drive failed)' }
        $rowRect = Get-Rect $row
        Write-Output 'state drive: left-click on the Flash Images rack row -> IsSelected=True'
    }

    if ($Surface -eq 'rack-row-dot') {
        # DRIVE THE STATE, NEVER ASSUME IT.
        # WHICH state costs a gesture is the opposite of the obvious one: SessionPresetDocument
        # .FlashEnabled defaults to TRUE (:64, ported from WPF's AppSettings.FlashEnabled), so a
        # cold start is already ARMED and 'off' is the state that needs the toggle.
        #
        # But the FIRST version of this branch hard-coded that, and it was WRONG the moment a
        # persisted preset leaked between runs — which one did, because the deterministic-start set
        # was missing session_preset.json. So the state is now READ, toggled only if it disagrees,
        # and read again. The rack's own second gesture (StudioPage.axaml.cs:449-453 -> :559-569) is
        # what does the toggling, on a real desktop, which no run had ever performed before SP-122.
        $expectedHead = if ($State -eq 'armed') { 'Armed.' } else { 'Switched off.' }
        $live = (Get-Element $window 'FlashLiveState').Current.Name
        if (-not $live.StartsWith($expectedHead)) {
            Write-Output "state drive: right-click quick-toggle on the Flash Images row (it read '$live')"
            RightClick-Rect $rowRect
            $live = (Get-Element $window 'FlashLiveState').Current.Name
        }
        if (-not $live.StartsWith($expectedHead)) {
            Fail "the module did not reach '$State': FlashLiveState reads '$live' (expected it to start '$expectedHead')"
        }
        Write-Output "state drive confirmed: FlashLiveState = '$live'"

        # THE DOT CELL, DERIVED FROM TWO MEASURED RECTS AND CROSS-CHECKED.
        # A rack row's Grid is ColumnDefinitions="*,Auto": the caption fills the star column and
        # the 8-DIP dot is the auto column. So the dot cell begins at the caption's right edge and
        # is 8 DIP wide. The cross-check comes from the Visuals row, the ONE row whose Grid has a
        # single child because upstream gives it no dot (StudioPage.axaml:172-174; upstream's rule
        # at StudioTabView.xaml.cs:494-496) — its caption therefore spans the WHOLE grid, so
        # caption + dot on any other row must equal it. A layout change fails here, loudly, instead
        # of aiming a 14-pixel capture at the wrong 14 pixels.
        #
        # THE CROSS-CHECK COMPARES WIDTHS, NOT EDGES, AND THAT IS NOT A DETAIL. Comparing the two
        # rows' right EDGES was the first draft and it was wrong by exactly 5 px on every selected
        # row: RadioButton.rack-row:checked carries BorderThickness="3,0,0,0", so 3 DIP x 1.75
        # displaces the checked row's content and the two rows stop sharing an origin. Widths are
        # invariant under that displacement; edges are not.
        $labelRect = Get-RowLabelRect $row
        $gridPx = (Get-RowLabelRect (Get-Element $window 'RowVisuals')).W
        $dotPx = [int][math]::Round(8 * $scale)
        if ([math]::Abs(($labelRect.W + $dotPx) - $gridPx) -gt 1) {
            Fail ("the rack row grid does not close: caption $($labelRect.W) px + 8 DIP dot $dotPx px at scale " +
    "$scale is $($labelRect.W + $dotPx) px, but the Visuals row's dotless caption spans $gridPx px. " +
    'The row grid has changed and this derivation no longer names the dot')
        }
        $capW = $dotPx
        $capH = $dotPx
        $capX = $labelRect.X + $labelRect.W
        $capY = [int]($rowRect.Y + ($rowRect.H - $dotPx) / 2)
        Write-Output ("dot cell: $capX,$capY ${capW}x${capH} — caption $($labelRect.W) px + dot $dotPx px == " +
    "Visuals dotless caption $gridPx px (8 DIP @ scale $scale)")
    }
    else {
        $capX = $rowRect.X; $capY = $rowRect.Y; $capW = $rowRect.W; $capH = $rowRect.H
    }
}
else {
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

    if ($Surface -eq 'rail-door') {
        $capX = $rect.X; $capY = $rect.Y; $capW = $rect.W; $capH = $rect.H
    } else {
        $capX = $windowRect.X; $capY = $windowRect.Y; $capW = $windowRect.W; $capH = $windowRect.H
    }
}

# Park the mouse off every interactive surface so :pointerover never leaks into a capture. The
# diagnostic footer's bottom-right corner has no control on it, and for the rack this matters
# twice over: RadioButton.rack-row:pointerover is #FF241E2A, only 11/10/11 away from the rack's
# own #FF19141F ground, so a hovering cursor is exactly the thing a ground check must not see.
[VerifyNative]::SetCursorPos($windowRect.X + $windowRect.W - 40, $windowRect.Y + $windowRect.H - 40) | Out-Null
Start-Sleep -Milliseconds 400

$bmp = New-Object System.Drawing.Bitmap $capW, $capH
$g = [System.Drawing.Graphics]::FromImage($bmp)
# FENCE THE READ (SP-116). Between "the app painted" and "this process read the screen" there is
# otherwise no happens-before edge of any kind, and the read can return what was behind the window:
# 34 misses in 1200 unfenced reads, 0 in 1500 fenced. A DWM that refuses is REPORTED and fails the
# capture rather than being swallowed — an unfenced read is a coin flip, and a PNG that might be of
# the wallpaper is not evidence.
try { $fence = [VerifyNative]::DwmFlush() }
catch { Fail "the compositor fence is unavailable ($($_.Exception.GetType().Name)); this read would be unfenced" }
if ($fence -ne 0) { Fail "DwmFlush returned 0x$('{0:X8}' -f $fence); this read would be unfenced" }
$g.CopyFromScreen($capX, $capY, 0, 0, $bmp.Size)
Write-Output 'screen read fenced through DwmFlush (HRESULT 0)'
$bmp.Save($outFile, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

$null = $script:proc.CloseMainWindow()
if (-not $script:proc.WaitForExit(10000)) { Fail 'process did not exit within 10s' }
if ($script:proc.ExitCode -ne 0) { Fail "non-zero exit on close: $($script:proc.ExitCode)" }

# The window is gone; the desktop belongs to whoever wants it next.
Release-Lease

Write-Output "CAPTURE: $outFile ($($capW)x$($capH))"
Write-Output 'CAPTURE PASS'
# SAY SO. Every failure path here calls `exit 1`, but success fell off the end of the script, and
# a .ps1 invoked with `&` that never calls `exit` leaves $LASTEXITCODE holding the PREVIOUS
# command's code. self-test.ps1 guards each capture with `if ($LASTEXITCODE -ne 0)`, so those
# guards were reading whatever ran before — vacuously green when the predecessor was a build, and
# a false FAILURE the moment the predecessor was CcpVerify reporting a seeded regression with
# exit 2. Found by that exact false failure while adding the rack phase (SP-122).
exit 0
