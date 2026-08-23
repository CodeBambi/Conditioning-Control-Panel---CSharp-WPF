# CCP greenfield verification harness — tier 2 Windows capture.
# Captures ONE named surface+state to a PNG for the CcpVerify named-check tool and K3 review.
# Formalizes the headed-smoke patterns: SetWindowPos(HWND_TOPMOST) raise (the app
# opens unactivated and pixels belong to the occluder), UIA text reads, layout-probe door
# rect (Avalonia exposes no UIA peers for Border/Grid/StackPanel), real-input state driving.
# System.Drawing appears ONLY as capture transport (CopyFromScreen -> PNG file); this script
# never reads a pixel — all pixel logic lives in CcpVerify.
# Re-anchored: the demonstrator card this harness used to drive is retired, and the
# navigation shell replaced it. Same three techniques, new anchors — surface dashboard-card ->
# rail-door, state lit -> selected; 'dashboard' still means the whole window. The state is still
# driven through REAL input (a left-click on a rail door), and the drive is still confirmed by a
# UIA read before any pixel is captured.
#
# THE RACK, and two things this script was missing before it could be trusted with one.
#
# 1. IT NOW TAKES THE MACHINE-WIDE REAL-DESKTOP LEASE. This script puts a top-most window on the
#    interactive desktop and reads that desktop back, which is exactly what the test suite's
#    RealDesktopCollection serialises through %TEMP%/ccp-real-desktop.lease — and this script ran
#    outside that, so a capture could race a floor run. What that costs was measured: a failure
#    that read "Expected 0, Actual 676161", one whole FOREIGN run's flash counted as this one's.
# 2. IT NOW FENCES THE SCREEN READ. CopyFromScreen was called with no happens-before edge against
#    the compositor. Measured: 34 misses in 1200 unfenced reads and 0 in 1500 fenced;
#    DwmFlush alone was the whole effect. An unfenced screen read is a defect, not a flake.
#
# The rack surfaces need NO product probe, and that is a finding rather than a convenience: every
# rack row is a RadioButton, so Avalonia gives it a real UIA peer carrying an AutomationId, a
# screen BoundingRectangle and SelectionItemPattern.IsSelected. The layout probe this script reads
# for rail doors exists because the original anchor was a demonstrator CARD — a Border/Grid, which
# has no peer — and the re-anchor onto RadioButtons never revisited it. Both channels are read
# here and they agree exactly (probe 174.9x44.0 DIP @ scale 1.75 == UIA 306x77 at the same origin).
# A rack probe was authorised and REFUSED: fifteen probe lines in the bottom-docked footer add
# 15 x 23.4 = 351 px to a rack viewport measured at 965 px, which pushes five of the fifteen rows
# it exists to photograph below the scroll fold. A probe that moves the thing it observes is not
# an observation seam.
# Usage: pwsh client/tools/verify/capture.ps1 -Surface rail-door -State selected
#        pwsh client/tools/verify/capture.ps1 -Surface dashboard -State unselected
#        pwsh client/tools/verify/capture.ps1 -Surface rack-row -State selected
#        pwsh client/tools/verify/capture.ps1 -Surface rack-row-dot -State armed
#        pwsh client/tools/verify/capture.ps1 -Surface goon-page -State first-run
#        pwsh client/tools/verify/capture.ps1 -Surface trainer-card -State no-runs-yet
#
# THE TRAINER CARD, and the two things a MODULE needs that a rail door did not.
#
# The card is not a page and not a control: it is a Border.module on the Graded Intake page, so
# Avalonia gives it no UIA peer at all and it is not the thing the door click lands on. Two
# consequences, both handled below rather than assumed away.
#
# 1. THE RECT IS DERIVED FROM THE CARD'S OWN TEXT, not from a probe. The card's first and last
#    TextBlocks DO have peers (TrainerCardTitle, TrainerCardLocalOnlyNote), they are children of the
#    same StackPanel, and Border.module insets its content by BorderThickness 1 + Padding 16
#    (MainWindow.axaml:121-127). So the card's edge is 17 DIP outside that content box on every
#    side, and the derivation is cross-checked: both TextBlocks must share a left edge, or the
#    layout has changed and this refuses instead of aiming at the wrong rectangle. A probe line was
#    considered and refused for the reason the rack's was — the footer is the only place to publish
#    one, and every line added there moves the very content this capture photographs.
#
# 2. THE CARD SCROLLS. It is the second module on a page inside a ScrollViewer, and UIA reports
#    UNCLIPPED bounds with IsOffscreen=False for content scrolled out of a viewport (measured
#    during the rack work). So this drives the wheel — real input, one notch at a time — until the
#    DERIVED card rect is fully inside the viewport the page names (IntakeScroll), and refuses if it
#    never is. Never a fixed number of notches: a page that grows a module would silently stop
#    scrolling far enough while still reporting a plausible rect.
#
# The route is confirmed by the shell's own probe AND the card's own text before any pixel is read
# (the card renders on AttachedToVisualTree — IntakePage.axaml.cs:71 — so a mounted page with an
# unrendered card is a real state, and it would photograph as a plausible empty rectangle).
#
# THE GOON PAGE, and the one way this surface differs from every other one here.
#
# Every surface above is Avalonia painting into a window this script launched. `goon-page` is a
# REAL EMBEDDED BROWSER rendering a payload page inside one, and that changes what "confirm the
# state before you read a pixel" has to mean. A rail door is selected or it is not; a page can be
# loading, wedged on a spinner, showing its own boot-failure text, or showing a WebView2 error
# page -- and every one of those photographs as a plausible-looking rectangle.
#
# So this surface is gated on the PAGE'S OWN STATE, read back out of the page's object graph
# through the host window's probe line (GoonHostWindow's GoonProbe): `ready=true` is written in
# exactly one place in the payload (boot.js:418, inside settle(), behind a guard that requires
# BOTH init and manifest to have been parsed), and `screen=title` is the id the page's own router
# writes onto the document element (ui/router.js:222). The host's own "I sent the messages" would
# have proved nothing -- that flag is set before either message is dispatched.
#
# The gate is POLLED TO A DEADLINE, never read once: a slow-but-healthy boot is otherwise
# indistinguishable from one that never completes, and a check that fails honestly-passing runs
# gets disabled. The state is driven by REAL INPUT throughout (Play door, then PRACTICE) -- there
# is a --goon-demo flag and this script deliberately does not use it, because the click is the
# thing a regression would break.
# THE SESSION RACK, and the one thing it needs that no surface here needed before: a state that
# only exists because a user ASKED for it. `session-start -State running` is not a style, a dial or
# a selection — it is a scripted session really running inside the app, started the way a user
# starts one: open the SESSIONS row at the foot of the Studio rack, pick a session, press the
# button, read the confirmation, press its Start Session. Four gestures, every one real input, and
# the state cannot be reached any other way (there is no flag for it and deliberately so).
#
# So this surface is gated on the RUN's own published state before any pixel: the rack rows'
# names and durations, then — for `running` — the confirmation's own promise text, then the
# readout's phase line and the button's own caption carrying a countdown. Every one of those is a
# UIA read of a control the product painted, not an inference from "the click did not throw".
#
# The row is at the FOOT of the rack (upstream's presets tab comes after its studio tab,
# MainWindow/MainWindow.TabNavigation.cs:592), so it is below the scroll fold at this window size
# and is wheeled in one notch at a time, testing after each — the trainer-card rule, and never a
# fixed count.
param(
    [Parameter(Mandatory)][ValidateSet('dashboard', 'rail-door', 'rack-row', 'rack-row-dot', 'goon-page', 'trainer-card', 'session-row', 'session-start', 'session-history', 'studio-dial', 'companion-permissions')] [string]$Surface,
    [Parameter(Mandatory)][ValidateSet('unselected', 'selected', 'off', 'armed', 'first-run', 'no-runs-yet', 'easy', 'hard', 'idle', 'running', 'kept', 'not-kept', 'live', 'locked', 'closed', 'admitted')] [string]$State
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$verifyDir = $PSScriptRoot
$shots = Join-Path $verifyDir 'artifacts'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
$exe = Join-Path $verifyDir '..\..\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe'
# The deterministic-start set. It was ONE file, and that had long been incomplete: the rack
# rows' module dials do not live in settings.json, they live in session_preset.json in the same data
# directory (SessionPresetDocument.FileName, SessionParticipant.cs:96). Measured rather than
# reasoned — a `-State off` capture right-clicked Flash Images off, and the NEXT run's `-State
# armed` capture read back "Switched off." on what was supposed to be a cold start.
# The third file is the Trainer Card's whole subject: the graded-run award record
# (GradedRunAwards.cs:37, read at IntakeLaunch.cs:108-111 out of the SAME data directory as
# settings.json). Its absence is not a failure — it is the card's `no-runs-yet` state, which is the
# one state this harness can drive without running a whole graded intake.
#
# The FOURTH is the session feature lock's own subject. `studio-dial` photographs the Lock Card
# panel's Repeats slider, and what makes that capture evidence is that the dial has the SAME VALUE
# in both states — so a leaked session_lockcard.json from a previous run would move the thumb and
# the two captures would differ for a reason that has nothing to do with the lock
# (LockCardPresetDocument.FileName; the same leak session_preset.json above was added for).
$stateFiles = @(
    (Join-Path $env:APPDATA 'CcpClient\settings.json'),
    (Join-Path $env:APPDATA 'CcpClient\session_preset.json'),
    (Join-Path $env:APPDATA 'CcpClient\graded_run_awards.json'),
    (Join-Path $env:APPDATA 'CcpClient\session_lockcard.json')
)
# AND THE PAGE'S OWN PREFS. Hygiene, and NOT what makes this deterministic.
#
# The goon PAGE keeps preferences in WebView2 localStorage, and one of them decides what is on
# screen: the title screen auto-opens its "how it works" explainer once, on a first visit, and
# never again -- `if (prefs && !prefs.get('seenHowItWorks')) ledger.timer(showHowItWorks, 420)`
# (ui/screens/title.js:157), with showHowItWorks setting the flag as its first act (:137).
#
# WHAT ACTUALLY MAKES IT DETERMINISTIC IS THE ORIGIN, and that was MEASURED rather than assumed.
# localStorage is scoped per origin, this page is served from http://127.0.0.1:<EPHEMERAL PORT>,
# and the port is redrawn on every launch -- so every run gets an empty store and the explainer
# opens every time. A run with this clear deliberately SKIPPED still reported `modal=open`, which
# is the measurement, and it is why the clear below is best-effort rather than fatal: WebView2
# child processes can still hold files in that directory seconds after a previous run, and a
# capture that refused for that reason would be refusing for a reason that does not affect it.
#
# THE REAL GUARD IS THE PROBE. The gate below requires `modal=open` before any pixel, so if page
# state ever did survive a run, this capture REFUSES BY NAME instead of photographing the other
# screen. That is the mechanism; this is tidying.
# AND THE LOG FOLDER, for the session-history surface only. The retained logs are the SUBJECT of
# those two captures (ScriptedSessionLogStore.FolderName), so a folder left behind by a previous run
# would put a row on the `not-kept` capture and make it photograph the wrong claim - the exact
# leak the session_preset.json note above records for the rack.
$sessionLogsDir = Join-Path $env:APPDATA 'CcpClient\session_logs'
$goonProfileDir = Join-Path $env:APPDATA 'CcpClient\dtrh\wv2-profile-goon'
$outFile = Join-Path $shots "windows-$Surface-$State.png"

# ValidateSet cannot express a PAIR, and an unpaired combination is not a typo the caller should
# have to debug from a pixel check: 'rack-row-dot -State selected' has no drive and would silently
# capture whatever the last state left behind. Refuse it by name here, once.
$statesFor = @{
    'dashboard'    = @('unselected', 'selected')
    'rail-door'    = @('unselected', 'selected')
    'rack-row'     = @('unselected', 'selected')
    'rack-row-dot' = @('off', 'armed')
    'goon-page'    = @('first-run')
    'trainer-card' = @('no-runs-yet')
    # The two session-rack states are two different ROWS (a 30-minute Easy one and a 60-minute
    # Hard one), because the thing being photographed is a per-session colour: a stripe check that
    # cannot fail on another row is a check that is not reading the session's own data.
    'session-row'   = @('easy', 'hard')
    'session-start' = @('idle', 'running')
    # THE TWO HISTORY STATES ARE ONE RULE. Both runs are the SAME session, started and stopped the
    # same way through the same four gestures; the only difference between the captures is HOW LONG
    # the session was left running, and upstream's retention rule decides whether that run is kept
    # (Services/Session/SessionLogService.cs:24, :93-94 - no media AND under 30 seconds is the only
    # case that is dropped). So a check that passes on both would be saying the log ignores the
    # rule.
    'session-history' = @('kept', 'not-kept')
    # THE SESSION FEATURE LOCK, AND IT IS THE STRICTEST INVERSION ON THIS MANIFEST: the two states
    # are the SAME control, on the SAME panel, at the SAME value, in the same app. `locked` differs
    # from `live` by one thing only - a scripted session is running - and the captured dial is one
    # the session never WRITES (LockCardRepeats is absent from ScriptedSessionDials.Apply), so its
    # thumb sits in the same place in both captures and the only thing a check can be reading is
    # the disabled livery.
    'studio-dial' = @('live', 'locked')
    # THE TWO PERMISSION STATES ARE ONE GESTURE APART, and the gesture is the whole claim: `closed`
    # is what a fresh process gives the user (master off, not one per-effect switch on screen) and
    # `admitted` is what she gets after pressing the master switch once. A check that passed on both
    # would be saying the default is not a default.
    'companion-permissions' = @('closed', 'admitted')
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
    // The goon host is a SECOND top-level window, and Process.MainWindowHandle does not
    // say which of the two it names. WM_CLOSE is posted to the handle UIA gave us for the window
    // this script actually found, so the close targets the window it means.
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    public const uint WM_CLOSE = 0x0010;
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    public const uint RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;
    // The wheel, for the one surface that has to be scrolled into view. WHEEL_DOWN is
    // -WHEEL_DELTA (-120) as the unsigned dwData mouse_event takes: one notch toward the user.
    // Declared here rather than cast in PowerShell, which has no unchecked conversion.
    public const uint WHEEL = 0x0800, WHEEL_DOWN = 0xFFFFFF88;
    public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
}
'@
Add-Type -TypeDefinition $native

# ---------------------------------------------------------------------------------------------
# The machine-wide real-desktop lease.
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
# about it in its own hint text — "Right-click a row to flip that effect on or off" — and no run
# on a real desktop had ever performed it before this harness did.
function RightClick-Rect($rect) {
    $cx = [int]($rect.X + $rect.W / 2); $cy = [int]($rect.Y + $rect.H / 2)
    [VerifyNative]::SetCursorPos($cx, $cy) | Out-Null
    Start-Sleep -Milliseconds 200
    [VerifyNative]::mouse_event([VerifyNative]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
    [VerifyNative]::mouse_event([VerifyNative]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
}

# One wheel notch over a rect. WM_MOUSEWHEEL goes to the FOCUSED window, not the one under the
# cursor (the "scroll inactive windows" setting is a property of the machine and is not relied on
# here); the shell has focus because this script has already clicked a rail door in it, and
# Avalonia then routes the wheel to the element under the pointer.
function Wheel-Down($rect) {
    $cx = [int]($rect.X + $rect.W / 2); $cy = [int]($rect.Y + $rect.H / 2)
    [VerifyNative]::SetCursorPos($cx, $cy) | Out-Null
    Start-Sleep -Milliseconds 100
    [VerifyNative]::mouse_event([VerifyNative]::WHEEL, 0, 0, [VerifyNative]::WHEEL_DOWN, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 250
}

# ---------------------------------------------------------------------------------------------
# UIA element reads. THE RACK NEEDS NO PROBE.
#
# Every rack row is a RadioButton and Avalonia gives it a real automation peer: an AutomationId
# taken from x:Name, a screen BoundingRectangle, and SelectionItemPattern.IsSelected. So the three
# things a probe would have had to publish — where the row is, which row it is, and whether it is
# open — are already published, by the control itself, on the channel this script already reads
# (it has enumerated RadioButton peers since the 2026-08-18 audit).
# ---------------------------------------------------------------------------------------------
function Get-Element($window, [string]$automationId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    $el = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $el) { Fail "no UIA element with AutomationId '$automationId'" }
    return $el
}

# Get-Element's nullable twin, for the ONE thing this script has to assert the ABSENCE of. An
# Avalonia control with IsVisible=False has no automation peer at all, so "the session lock banner
# is not on screen" reads as $null here — and Get-Element would (rightly) fail the run instead.
function Find-Element($window, [string]$automationId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

# Wheel a rack row fully inside the rack viewport, one notch at a time, testing after each. Never a
# fixed count, for the reason the session block records: a rack that grew another row would stop
# scrolling far enough while UIA still reported a plausible rect with IsOffscreen=False.
function Scroll-RowIntoView($window, $viewport, [string]$rowId) {
    $notches = 0
    while ($true) {
        $rect = Get-Rect (Get-Element $window $rowId)
        if (Test-Inside $rect $viewport) { return @{ Rect = $rect; Notches = $notches } }
        if ($notches -ge 24) {
            Fail ("the '$rowId' rack row never came fully inside the rack viewport after $notches wheel " +
    "notches: row $($rect.X),$($rect.Y) $($rect.W)x$($rect.H) vs viewport " +
    "$($viewport.X),$($viewport.Y) $($viewport.W)x$($viewport.H)")
        }
        Wheel-Down $viewport
        $notches++
    }
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
function Test-Inside($inner, $outer) {
    return -not ($inner.X -lt $outer.X -or $inner.Y -lt $outer.Y `
        -or ($inner.X + $inner.W) -gt ($outer.X + $outer.W) `
        -or ($inner.Y + $inner.H) -gt ($outer.Y + $outer.H))
}

function Assert-Inside($inner, $outer, [string]$what, [string]$container) {
    if (-not (Test-Inside $inner $outer)) {
        Fail ("$what at $($inner.X),$($inner.Y) $($inner.W)x$($inner.H) is not fully inside $container at " +
    "$($outer.X),$($outer.Y) $($outer.W)x$($outer.H) — it is clipped or scrolled away, and capturing " +
    'it would photograph whatever is really at those coordinates')
    }
}

# ---------------------------------------------------------------------------------------------
# The goon host window.
#
# Get-Window above finds a window by PROCESS ID ALONE, which is unambiguous only while a process
# has one top-level window. The moment PRACTICE is pressed this process has two, and which one
# FindFirst returns is not specified. So the goon window is looked up BY NAME, and the dashboard
# lookup above is left exactly as it was -- the four landed captures were taken through it and
# cannot be re-verified from inside this packet.
# ---------------------------------------------------------------------------------------------
function Get-GoonWindow([int]$processId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
        # The window's Title is "Goon Game - Practice" with an em dash; matched on the stable
        # prefix so an encoding round-trip through this file can never decide the lookup.
        if ($w.Current.Name -like '*Goon Game*') { return $w }
    }
    return $null
}

# The probe line, as the window itself publishes it (GoonHostWindow's GoonProbe). Returned raw:
# every caller below asserts on a NAMED field of it and prints the whole line on refusal, because
# "the page was not ready" and "the payload was missing" are different failures and the operator
# needs to see which one happened.
function Get-GoonProbe($goonWindow) {
    # The window can CLOSE ITSELF while this is polling, and then every UIA call against it throws
    # "The target element corresponds to UI that is no longer available". That is not hypothetical:
    # a seeded build whose manifest never arrives makes the page give up at its own 45s deadline
    # (boot.js:113), post boot-error, and the host closes the window honestly in response. The
    # first version of this loop died on that with a raw .NET exception instead of a named
    # refusal -- a worse outcome than the failure it was reporting. Returning $null lets the
    # caller decide, which is where the window-vanished check lives.
    try {
        $el = $goonWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'GoonProbe')))
        if ($null -eq $el) { return $null }
        return $el.Current.Name
    }
    catch { return $null }
}

# The session's own windows: the recap the end of a run raises, and the Recent Sessions history.
# Looked up BY TITLE for the reason Get-GoonWindow gives - the moment either is up this process has
# two top-level windows and Get-Window's process-id-only lookup stops being unambiguous.
# WHERE AN OWNED WINDOW REALLY IS IN THE UIA TREE, and this was MEASURED rather than assumed. The
# goon host is found as a CHILD of the desktop root; the session recap and the history are not, and
# a first draft that looked only there refused a window the app had demonstrably shown -- the app's
# own diagnostic read `visible=True pos=534,166 size=540x620 owner=shell` in the same run. They are
# shown OWNED (Window.Show(owner), the LoomLaunch convention), and Windows nests an owned window
# under its owner, so UIA reports it as a DESCENDANT of the shell rather than as a sibling. Both
# places are searched, cheap one first.
function Get-NamedWindow([int]$processId, [string]$title) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $byProcess = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $byProcess)) {
        if ($w.Current.Name -eq $title) { return $w }
        $owned = $w.FindAll([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Window)))
        foreach ($o in $owned) {
            if ($o.Current.Name -eq $title) { return $o }
        }
    }
    return $null
}

# Wait for one of them to appear. Bounded, and it FAILS BY NAME rather than returning null: a
# window that never opened is the finding, not a missing variable three lines later.
function Wait-NamedWindow([int]$processId, [string]$title, [int]$seconds = 10) {
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt $seconds) {
        $w = Get-NamedWindow $processId $title
        if ($null -ne $w) { return $w }
        Start-Sleep -Milliseconds 200
    }
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $seen = @()
    foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
        $seen += "'$($w.Current.Name)'"
        foreach ($o in $w.FindAll([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Window)))) {
            $seen += "'$($o.Current.Name)' (owned)"
        }
    }
    Fail "no '$title' window appeared within ${seconds}s (this process has: $($seen -join ', '))"
}

# The same bounded wait, matched on a PREFIX. The companion window's title carries an em dash
# ("Companion - CCP Client", with a real U+2014), and this file deliberately keeps no such
# character in a needle: the session block already records that non-ASCII needles do not survive an
# encoding round trip here. A prefix match reads the same window without the trap.
function Wait-WindowLike([int]$processId, [string]$prefix, [int]$seconds = 10) {
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $byProcess = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $isWindow = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $seen = @()
    while ($deadline.Elapsed.TotalSeconds -lt $seconds) {
        $seen = @()
        foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $byProcess)) {
            $seen += "'$($w.Current.Name)'"
            if ($w.Current.Name.StartsWith($prefix)) { return $w }
            # An owned Avalonia window is a UIA DESCENDANT of its owner, not a sibling of it - the
            # finding the recap path already records, and the companion window is owned the same way.
            foreach ($o in $w.FindAll([System.Windows.Automation.TreeScope]::Children, $isWindow)) {
                $seen += "'$($o.Current.Name)' (owned)"
                if ($o.Current.Name.StartsWith($prefix)) { return $o }
            }
        }
        Start-Sleep -Milliseconds 200
    }
    Fail "no window whose title starts with '$prefix' appeared within ${seconds}s (this process has: $($seen -join ', '))"
}

# A non-failing finder. Get-Element refuses by name when an element is missing, which is right
# everywhere it is used - but the permissions grid's closed state is defined by an ABSENCE, and an
# absence has to be readable without being a failure.
function Find-Element($window, [string]$automationId) {
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)))
}

function Get-Toggle($element) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    return $pattern.Current.ToggleState
}

function Assert-Route($window, [string]$route) {
    $texts = (Get-Texts $window) -join "`n"
    if ($texts -notmatch "route: $route") { Fail "the shell did not navigate to '$route' (state drive failed)" }
}

# Take the desktop BEFORE the app is launched: the window itself is the thing that must not
# contend with another run's windows, so the lease has to cover the launch and not just the read.
Take-Lease

# Deterministic start: remove the demo stores. This is what makes the rack captures
# order-independent — the rack's right-click quick-toggle persists the module's enabled flag, and
# without the preset file in this set an 'off' capture leaks into the NEXT run's 'armed' capture.
foreach ($stateFile in $stateFiles) {
    if (Test-Path $stateFile) { Remove-Item $stateFile -Force }
}
if ($Surface -eq 'session-history' -and (Test-Path $sessionLogsDir)) {
    Remove-Item $sessionLogsDir -Recurse -Force
    Write-Output "deterministic start: retained session logs cleared ($sessionLogsDir)"
}
if ($Surface -eq 'goon-page' -and (Test-Path $goonProfileDir)) {
    # Only for this surface: blowing away a WebView2 profile is not free (the next launch rebuilds
    # it), and no other capture here depends on page-side state.
    try {
        Remove-Item $goonProfileDir -Recurse -Force -ErrorAction Stop
        Write-Output "deterministic start: goon WebView2 profile cleared ($goonProfileDir)"
    }
    catch {
        # REPORTED, never silent -- but not fatal, because it is not what this capture depends on.
        Write-Output ("NOTE: the goon WebView2 profile could not be cleared " +
    "($($_.Exception.GetType().Name): $($_.Exception.Message)). Continuing: the page's store is " +
    'scoped to an ephemeral origin that changes every launch, and the modal=open gate below is ' +
    'what would catch surviving page state')
    }
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

# DERIVE THE DOOR SET, NEVER HARD-CODE IT (audit, 2026-08-18).
# This was a literal list of three door needles, written when three was the whole
# rail. A later wave added a fourth door and did not widen it, so the harness stopped checking the
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

$script:goonWindow = $null
$script:goonHwnd = [IntPtr]::Zero
# The session recap / history window, when one is up at capture time. Same reason the goon window
# has its own handle: with two top-level windows Process.MainWindowHandle does not say which one it
# names, so CloseMainWindow could send WM_CLOSE to either.
$script:extraWindow = $null
$script:extraHwnd = [IntPtr]::Zero

if ($Surface -eq 'goon-page') {
    # =========================================================================================
    # THE GOON PAGE. The first capture in this harness of something the PRODUCT did not
    # paint: a payload page inside a real WebView2.
    #
    # Two hops of real input, because that is the user path and because the port gives no surface
    # a dashboard tile (wpf-surface-reachability.md): the Play door, then the PRACTICE button on
    # the Play page. A --goon-demo flag exists and is deliberately NOT used here -- the click is
    # the thing a regression would break, and a flag would step around it.
    # =========================================================================================
    Click-Rect (Get-DoorRect $window 'play')
    Assert-Route $window 'play'
    Write-Output 'state drive: left-click on the Play door -> route: play'

    # The Play page is a plain StackPanel in an unscrolled ContentControl, so a card low on the
    # page can sit BELOW the window on a short screen. UIA reports unclipped bounds either way
    # (a finding from the rack work), so clicking without this check would click the wallpaper.
    $practice = Get-Element $window 'GoonPracticeButton'
    $practiceRect = Get-Rect $practice
    Assert-Inside $practiceRect $windowRect 'the PRACTICE button' 'the shell window'
    Click-Rect $practiceRect
    Write-Output 'state drive: left-click on PRACTICE'

    # The window binds a loopback origin and builds a WebView2 environment, so it is slower to
    # arrive than an Avalonia window. Poll to a deadline; never a fixed sleep.
    $goonDeadline = [Diagnostics.Stopwatch]::StartNew()
    while ($goonDeadline.Elapsed.TotalSeconds -lt 60) {
        if ($script:proc.HasExited) { Fail "app exited (code $($script:proc.ExitCode)) before the goon window appeared" }
        $script:goonWindow = Get-GoonWindow $script:proc.Id
        if ($null -ne $script:goonWindow) {
            $script:goonHwnd = [IntPtr]$script:goonWindow.Current.NativeWindowHandle
            if ($script:goonHwnd -ne [IntPtr]::Zero) { break }
        }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $script:goonWindow) {
        Fail ("the Goon host window never appeared within $([int]$goonDeadline.Elapsed.TotalSeconds)s. " +
    'PRACTICE was clicked and confirmed; the window is what did not arrive. The Play page renders a ' +
    'launch fault under its own card (GoonFaultText) when the launch THREW -- read that before ' +
    'treating this as a timeout')
    }
    if ($script:goonHwnd -eq [IntPtr]::Zero) { Fail 'the Goon host window has no native handle; it cannot be raised or captured' }
    Write-Output "goon window up after $([math]::Round($goonDeadline.Elapsed.TotalSeconds, 1))s"

    # Raise it. The host ducked the dashboard itself (a plain minimize -- GoonHostService.cs:20-23
    # parity), so the goon window is the only thing of ours on screen; it did not necessarily open
    # topmost, and pixels belong to whatever is.
    [VerifyNative]::SetWindowPos($script:goonHwnd, [VerifyNative]::HWND_TOPMOST, 0, 0, 0, 0,
        [VerifyNative]::SWP_NOMOVE -bor [VerifyNative]::SWP_NOSIZE -bor [VerifyNative]::SWP_SHOWWINDOW) | Out-Null
    Start-Sleep -Milliseconds 500

    # -----------------------------------------------------------------------------------------
    # THE GATE. Poll the window's own probe line until the PAGE says it settled -- and refuse, by
    # name, on every state that is not that. Each refusal below is a real outcome this surface can
    # produce, and every one of them would otherwise photograph as a plausible rectangle:
    #
    #   surface=payload-missing  the goon tree is not in the build output (typed honest surface)
    #   surface=unsupported      no WebView2 runtime, or a non-Windows head (typed honest surface)
    #   nav=failed               the navigation failed -- a WebView2 error page is on screen
    #   ready=false              the page loaded but init/manifest never landed: the LOADER, or
    #                            after 45s (boot.js:113) the page's own boot-failure text
    #   screen<>title            the page settled somewhere other than the title screen
    # -----------------------------------------------------------------------------------------
    $probe = $null
    $lastProbe = $null
    $probeDeadline = [Diagnostics.Stopwatch]::StartNew()
    while ($probeDeadline.Elapsed.TotalSeconds -lt 90) {
        if ($script:proc.HasExited) { Fail "the app exited (code $($script:proc.ExitCode)) while waiting for the page to settle. Last probe: $lastProbe" }
        if ($null -eq (Get-GoonWindow $script:proc.Id)) {
            Fail ("the Goon host window CLOSED ITSELF while this capture waited for the page. " +
    "Last probe: $lastProbe. The page gives up at its own 45s deadline (boot.js:113) and posts " +
    'boot-error; the host closes the window honestly in response, because no fallback surface ' +
    'exists. This is the product being correct and the PAGE failing to boot -- a finding to read, ' +
    'not a flake to retry')
        }
        $probe = Get-GoonProbe $script:goonWindow
        if ($null -ne $probe) { $lastProbe = $probe }
        if ($null -ne $probe) {
            if ($probe -match 'surface=(?<s>[a-z-]+)' -and $Matches['s'] -ne 'embedded' -and $Matches['s'] -ne 'pending') {
                Fail ("the Goon host did not select the embedded surface: $probe. " +
    'payload-missing means the goon tree is absent from the build output; unsupported means no ' +
    'WebView2 runtime (or a non-Windows head). Both are the product being honest, and neither is ' +
    'a page that can be photographed')
            }
            if ($probe -match 'nav=failed') {
                Fail ("the Goon page NAVIGATION FAILED: $probe. There is an error page in that window " +
    'and capturing it would be the exact defect this gate exists to prevent. The platform detail, ' +
    "when there is any, is on the app's transcript beside the NavigationCompleted line")
            }
            if (($probe -match 'ready=true') -and ($probe -match 'screen=title') -and ($probe -match 'modal=open')) { break }
        }
        Start-Sleep -Milliseconds 500
    }
    if ($null -eq $probe) { Fail 'the Goon host window published no probe line (no GoonProbe in its UIA tree)' }
    if ($probe -notmatch 'ready=true') {
        Fail ("the Goon page never reported ready=true within $([int]$probeDeadline.Elapsed.TotalSeconds)s: $probe. " +
    'ready is written in ONE place in the payload (boot.js:418, inside settle()) and means init AND ' +
    'manifest were both parsed BY THE PAGE. Without it that window is showing the boot loader or the ' +
    "page's own failure text, and this run is NOT evidence that the handshake completed")
    }
    if ($probe -notmatch 'screen=title') { Fail "the Goon page settled on a screen other than the title: $probe" }
    if ($probe -notmatch 'modal=open') {
        Fail ("the Goon page reached the title screen but its first-run explainer is not open: $probe. " +
    'That card auto-opens 420ms after the title mounts, ONCE, on a profile that has never seen it ' +
    "(ui/screens/title.js:157). modal=closed here means the profile was not really cleared and this " +
    'capture would be of a different screen than the one the checks were measured on')
    }
    Write-Output "goon handshake confirmed: $probe"

    # THE MICROPHONE RESIDUAL (D250). This script never touches a menu item, so the voice screen is
    # never reached -- but WebView2 can ask for a device this host can neither grant nor deny, and
    # if it ever does, that prompt is EVIDENCE and must not be photographed as a title screen.
    # NAMED LIMIT, not a claim: whether WebView2 projects its permission bar into the host UIA tree
    # at all is unverified, so an enumeration that fails is REPORTED rather than passed over.
    try {
        $prompts = @()
        foreach ($el in $script:goonWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($el.Current.Name -match '\bAllow\b') { $prompts += $el.Current.Name }
        }
        if ($prompts.Count -gt 0) {
            Fail ("a permission-prompt shape is present in the Goon window: $($prompts -join ' | '). " +
    'That is the D250 residual made visible -- record it as evidence; it is not a flake and must ' +
    'not be captured around')
        }
        Write-Output 'no permission-prompt shape in the goon window UIA tree (best-effort; see D250)'
    }
    catch {
        Write-Output ("NOTE: the goon window's UIA subtree could not be enumerated for a permission " +
    "prompt ($($_.Exception.GetType().Name)). The capture continues and the prompt check is UNMADE " +
    'for this run; it must not be reported as having passed')
    }

    # THE RECT, from the window's own probe. Avalonia gives Panel no UIA peer,
    # which is exactly why the probe publishes one.
    if ($probe -notmatch 'page-rect (?<w>[\d.]+)x(?<h>[\d.]+) DIP @ scale (?<s>[\d.]+) @ screen (?<x>-?\d+),(?<y>-?\d+)') {
        Fail "the goon probe carries no readable page rect: $probe"
    }
    $goonScale = [double]$Matches['s']
    $pageRect = @{
        X = [int]$Matches['x']; Y = [int]$Matches['y']
        W = [int]([double]$Matches['w'] * $goonScale); H = [int]([double]$Matches['h'] * $goonScale)
    }
    $goonRect = Get-Rect $script:goonWindow
    Write-Output ("goon page rect $($pageRect.X),$($pageRect.Y) $($pageRect.W)x$($pageRect.H) @ scale $goonScale; " +
    "window $($goonRect.X),$($goonRect.Y) $($goonRect.W)x$($goonRect.H)")
    Assert-Inside $pageRect $goonRect 'the goon page rect' 'the goon host window'

    $windowRect = $goonRect   # the cursor is parked relative to the window being captured
    $capX = $pageRect.X; $capY = $pageRect.Y; $capW = $pageRect.W; $capH = $pageRect.H
}
elseif ($Surface -eq 'trainer-card') {
    # =============================================================================================
    # THE TRAINER CARD. One real click on the Graded Intake rail door, then THREE confirmations
    # before a pixel is read: the shell's own route probe, the CARD's own text, and the geometry
    # the manifest's fractional regions depend on.
    # =============================================================================================
    $intakeDoor = Get-DoorRect $window 'intake'
    $scale = $intakeDoor.Scale
    Click-Rect $intakeDoor
    Assert-Route $window 'intake'
    Write-Output "state drive: left-click on the Graded Intake door -> route: intake (probe: $($intakeDoor.Raw))"

    # (1) THE CARD'S OWN TEXT. The page mounts on navigation but the card renders on
    # AttachedToVisualTree (IntakePage.axaml.cs:71), so "the route is intake" does NOT imply "the
    # card rendered" — an unrendered module photographs as a perfectly plausible rectangle.
    $cardTitle = (Get-Element $window 'TrainerCardTitle').Current.Name
    if ($cardTitle -ne 'Trainer Card') { Fail "the Trainer Card's title reads '$cardTitle', not 'Trainer Card'" }
    $pageText = (Get-Texts $window) -join "`n"
    foreach ($row in @('Top of the Class', 'Honor Roll', "Teacher's Pet", 'Held Back')) {
        if ($pageText -notlike "*$row*") { Fail "the Trainer Card is missing its '$row' row (Models/Achievement.cs:663-701)" }
    }

    # (2) THE STATE, by its own name. The deterministic-start set removed graded_run_awards.json, so
    # the card must be reading NoRunsYetNote (TrainerCard.cs) — if it is not, a run's record
    # survived and this capture would be of a DIFFERENT card than the one the checks name.
    $recordNote = (Get-Element $window 'TrainerCardRecordNote').Current.Name
    if (-not $recordNote.StartsWith('No graded run has been recorded')) {
        Fail ("the Trainer Card is not in the 'no-runs-yet' state: its record note reads '$recordNote'. " +
    'The award record was deleted before launch, so a card saying anything else means the record ' +
    'was rewritten between the delete and the read')
    }
    Write-Output "card gate: title '$cardTitle', four award rows present, record note '$recordNote'"

    # THE ABSENCE, CHECKED AS AN ABSENCE. The card's own last line says there is no sharing, export,
    # upload or publish path in this build, and a greyed-out one would be the fake-available shape
    # the capability contract bans (IntakePage.axaml's own note, §9 D7). A pixel check cannot see a
    # control that is not there, so it is read off the UIA tree: matched on BUTTONS only, because
    # the sentence making the claim contains all four of those words itself.
    $btnCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    foreach ($b in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)) {
        if ($b.Current.Name -match 'shar|export|upload|publish|leaderboard') {
            Fail ("a sharing-shaped BUTTON is on the Graded Intake page: '$($b.Current.Name)'. The card " +
    'states there is no sharing, export, upload or publish path in this build, and upstream''s ' +
    'counterpart traffic is owner-gated and unapproved')
        }
    }
    $null = Get-Element $window 'BeginIntakeButton'   # the page's ONE button is still the launcher
    Write-Output 'no sharing-shaped button anywhere on the page (UIA Button enumeration)'

    # (3) THE RECT. Border.module insets its content by BorderThickness 1 + Padding 16
    # (MainWindow.axaml:121-127), so the card's edge is 17 DIP outside the content box its first and
    # last TextBlocks bound. Both are children of the same StackPanel and must therefore share a
    # left edge — cross-checked, because a layout change here would otherwise aim a capture at a
    # rectangle that merely looks right.
    $inset = [int][math]::Round(17 * $scale)
    $viewport = Get-Rect (Get-Element $window 'IntakeScroll')
    $card = $null
    $titleRect = $null
    $notches = 0
    while ($true) {
        $titleRect = Get-Rect (Get-Element $window 'TrainerCardTitle')
        $lastRect = Get-Rect (Get-Element $window 'TrainerCardLocalOnlyNote')
        if ([math]::Abs($lastRect.X - $titleRect.X) -gt 1) {
            Fail ("the Trainer Card's first and last lines do not share a left edge (title x=$($titleRect.X), " +
    "last line x=$($lastRect.X)); the card's content box cannot be derived from them")
        }
        $card = @{
            X = $titleRect.X - $inset
            Y = $titleRect.Y - $inset
            W = $titleRect.W + 2 * $inset
            H = ($lastRect.Y + $lastRect.H + $inset) - ($titleRect.Y - $inset)
        }
        if (Test-Inside $card $viewport) { break }

        # SCROLL IT INTO VIEW WITH REAL INPUT, one notch at a time, testing after each. Never a
        # fixed count: a page that grows another module would otherwise stop scrolling far enough
        # while still reporting a plausible rect, and UIA would still say IsOffscreen=False.
        if ($notches -ge 24) {
            Fail ("the Trainer Card never came fully inside the page viewport after $notches wheel notches: " +
    "card $($card.X),$($card.Y) $($card.W)x$($card.H) vs viewport $($viewport.X),$($viewport.Y) " +
    "$($viewport.W)x$($viewport.H). If the card is TALLER than the viewport it cannot be captured " +
    'whole at this window size, and that is a finding about the page rather than a flake')
        }
        Wheel-Down $viewport
        $notches++
    }
    Write-Output ("card rect $($card.X),$($card.Y) $($card.W)x$($card.H) @ scale $scale " +
    "(derived: title $($titleRect.X),$($titleRect.Y) $($titleRect.W)x$($titleRect.H) + 17 DIP inset); " +
    "$notches wheel notch(es) to bring it inside the viewport $($viewport.X),$($viewport.Y) $($viewport.W)x$($viewport.H)")

    Assert-Inside $card $viewport 'the Trainer Card' 'the Graded Intake viewport (IntakeScroll)'
    Assert-Inside $card $windowRect 'the Trainer Card' 'the shell window'

    # THE TWO BANDS checks.json SAMPLES, PROVED AGAINST THE MEASURED LAYOUT RATHER THAN ASSUMED.
    # A fraction of a capture is only evidence if the thing it names is really at that fraction, and
    # both of these depend on a layout this script can measure. Widen either band in checks.json
    # past what is proved here and TrainerCardTests.NoUniformCaptureCanPassTheHeadedTrainerCardChecks
    # reddens, because it reads both files and compares them.
    # The ink band sits in the MIDDLE of the title's line rather than spanning it: the line box is
    # 38 px tall at scale 1.75 and the glyphs' cap band is the middle two thirds of it, so a band
    # measured to the line's own edges would refuse on a pixel of layout jitter while sampling more
    # leading than ink.
    $inkBand = @(0.050, 0.082)    # trainer-card-ink: y, and it must land ON the title's own line
    $groundBand = @(0.80, 0.98)   # trainer-card-ground: x, and it must be blank card ground
    $inkTop = $card.Y + [int]($card.H * $inkBand[0])
    $inkBottom = $card.Y + [int]($card.H * $inkBand[1])
    if ($inkTop -lt $titleRect.Y -or $inkBottom -gt ($titleRect.Y + $titleRect.H)) {
        Fail ("the ink band y $($inkBand[0])..$($inkBand[1]) of this capture is $inkTop..$inkBottom, which is " +
    "not inside the title's line at $($titleRect.Y)..$($titleRect.Y + $titleRect.H). The card's height " +
    "($($card.H) px) has moved the title out from under the band checks.json samples, so those pixels " +
    'are no longer the title and this capture would not be evidence about it')
    }
    # Every note on the card is MaxWidth=640 DIP from the content's left edge (IntakePage.axaml), and
    # the award rows' own lines are shorter still, so the blank column begins there.
    $groundLeft = $card.X + [int]($card.W * $groundBand[0])
    $textRight = $titleRect.X + [int][math]::Round(640 * $scale)
    if ($groundLeft -lt $textRight) {
        Fail ("the ground band x $($groundBand[0])..$($groundBand[1]) starts at $groundLeft, which is left of " +
    "the card's 640 DIP text column ending at $textRight. At this window size the region checks.json " +
    'samples for flat card ground would contain the card''s own text')
    }
    Write-Output ("regions proved: ink band y $inkTop..$inkBottom inside the title line " +
    "$($titleRect.Y)..$($titleRect.Y + $titleRect.H); ground band x from $groundLeft, right of the " +
    "640 DIP text column ending at $textRight")

    $capX = $card.X; $capY = $card.Y; $capW = $card.W; $capH = $card.H
}
elseif ($Surface -eq 'rack-row' -or $Surface -eq 'rack-row-dot') {
    # =========================================================================================
    # THE RACK. The shell opens on Studio (ShellRoutes.Default), so the rack is already
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

    # The rack SCROLLS, and UIA reports unclipped bounds with IsOffscreen=False for rows
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
        # what does the toggling, on a real desktop, which no run had ever performed before.
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
elseif ($Surface -eq 'studio-dial') {
    # =============================================================================================
    # THE SESSION FEATURE LOCK. The same dial, twice: once as the user's, once on loan.
    #
    # WHY THE LOCK CARD PANEL AND WHY THE REPEATS SLIDER. The lock's whole claim is that a dial a
    # session owns stops responding while the session runs, and the ONLY honest way to photograph
    # that is a control whose picture cannot differ for any other reason. LockCardRepeats qualifies
    # twice over: ScriptedSessionDials.Apply never writes it (upstream's block writes only the
    # enable and the frequency, Services/Session/SessionEngine.cs:1361-1366), so the thumb sits at
    # the same value in both captures; and its own panel carries LockCardStrictToggle, which is
    # DELIBERATELY NOT locked (Features/LockCardFeatureControl.xaml:124 is unmarked, and rule 3 at
    # MainWindow/MainWindow.SessionFeatureLock.cs:39-42 names Strict Lock in the never-lock list).
    # So one run reads both directions of the classification off the same panel.
    # =============================================================================================
    $scale = (Get-DoorRect $window 'studio').Scale
    $viewport = Get-Rect (Get-Element $window 'RackScroll')

    if ($State -eq 'locked') {
        # A REAL SESSION, STARTED THE ONLY WAY A USER CAN. There is no flag for this and
        # deliberately so, which is what makes the drive the same four gestures session-start uses.
        $sessions = Scroll-RowIntoView $window $viewport 'RowScriptedSession'
        Click-Rect $sessions.Rect
        if (-not (Get-Selected (Get-Element $window 'RowScriptedSession'))) {
            Fail 'the left-click did not open the Scripted Sessions row (state drive failed)'
        }
        Click-Rect (Get-Rect (Get-Element $window 'SessionRowMorningDrift'))
        if (-not (Get-Selected (Get-Element $window 'SessionRowMorningDrift'))) {
            Fail 'the left-click did not select the Morning Drift session row (state drive failed)'
        }
        Click-Rect (Get-Rect (Get-Element $window 'ScriptedSessionStartButton'))
        $promise = (Get-Element $window 'ScriptedSessionConfirmPromise').Current.Name
        if ($promise -notlike '*restored when the session ends*') {
            Fail "the confirmation does not carry the settings promise: '$promise'"
        }
        $stillIdle = (Get-Element $window 'ScriptedSessionStartButton').Current.Name
        if ($stillIdle -ne 'Start Session') { Fail "a session started before the confirmation was answered: '$stillIdle'" }
        Click-Rect (Get-Rect (Get-Element $window 'ScriptedSessionConfirmButton'))
        $caption = (Get-Element $window 'ScriptedSessionStartButton').Current.Name
        if ($caption -notlike 'STOP SESSION (*') { Fail "the session did not start: the button reads '$caption'" }
        Write-Output "start gate: promise shown, nothing started until it was answered, button '$caption' ($($sessions.Notches) wheel notch(es))"
    }

    # THE PANEL, opened the same way in both states so the two captures differ by the session and
    # by nothing else.
    $lockCard = Scroll-RowIntoView $window $viewport 'RowLockCard'
    Click-Rect $lockCard.Rect
    if (-not (Get-Selected (Get-Element $window 'RowLockCard'))) {
        Fail 'the left-click did not open the Lock Card rack row (state drive failed)'
    }
    Write-Output "state drive: left-click on the Lock Card rack row -> IsSelected=True ($($lockCard.Notches) wheel notch(es))"

    # =========================================================================================
    # THE UIA GATE, AND IT IS READ BEFORE ANY PIXEL. Three facts, and all three are properties the
    # operating system's own automation tree publishes about the real controls in the real window.
    # =========================================================================================
    $dial = Get-Element $window 'LockCardRepeatsSlider'
    $strict = Get-Element $window 'LockCardStrictToggle'
    $banner = Find-Element $window 'SessionLockReason'
    $dialEnabled = $dial.Current.IsEnabled
    $strictEnabled = $strict.Current.IsEnabled

    if ($State -eq 'locked') {
        if ($dialEnabled) { Fail 'the Repeats dial is still enabled while a session runs; the locked capture would be a lie' }
        if (-not $strictEnabled) {
            Fail ('the Strict Lock box is disabled while a session runs. Upstream leaves it unmarked and rule 3 ' +
    'names Strict Lock in the never-lock list, so this is over-locking, not the lock')
        }
        if ($null -eq $banner) { Fail 'the session lock banner is not on screen, so the greyed dial has no explanation' }
        $reason = $banner.Current.Name
        if ($reason -ne 'Morning Drift is running this. Its features and intensity are locked until the session ends.') {
            Fail "the banner does not name the running session: '$reason'"
        }
        $repeats = (Get-Element $window 'LockCardRepeatsValue').Current.Name
        Write-Output "lock gate: Repeats IsEnabled=False at '$repeats', Strict Lock IsEnabled=True, banner reads '$reason'"
    }
    else {
        if (-not $dialEnabled) { Fail 'the Repeats dial is disabled with no session running; the live capture would be a lie' }
        if (-not $strictEnabled) { Fail 'the Strict Lock box is disabled with no session running' }
        if ($null -ne $banner) {
            Fail "a session lock banner is on screen with nothing running: '$($banner.Current.Name)'"
        }
        # The dial's own value, read off the control, so the two captures can be shown to be of the
        # same picture rather than of two different slider positions.
        $repeats = (Get-Element $window 'LockCardRepeatsValue').Current.Name
        Write-Output "live gate: Repeats IsEnabled=True at '$repeats', Strict Lock IsEnabled=True, no banner"
    }

    $dialRect = Get-Rect $dial
    Assert-Inside $dialRect $windowRect 'the Lock Card Repeats slider' 'the shell window'

    # THE BAND checks.json SAMPLES, PROVED AGAINST THE MEASURED CONTROL. y 0.40..0.60 is the
    # slider's own centre line, where Fluent draws the track; x 0.02..0.10 is inside the FILLED
    # part of that track for any value above this dial's minimum, which LockCardSchedule's default
    # of 3 (of 1..10) is. A slider whose geometry changed fails here by name rather than sampling
    # its own background.
    if ($dialRect.H -lt [int][math]::Round(12 * $scale)) {
        Fail "the Repeats slider is only $($dialRect.H) px tall at scale $scale; its centre band would not be the track"
    }
    Write-Output ("dial rect $($dialRect.X),$($dialRect.Y) $($dialRect.W)x$($dialRect.H) @ scale $scale; " +
    'track band y 0.40..0.60, filled band x 0.02..0.10')

    $capX = $dialRect.X; $capY = $dialRect.Y; $capW = $dialRect.W; $capH = $dialRect.H
}
elseif ($Surface -eq 'session-row' -or $Surface -eq 'session-start' -or $Surface -eq 'session-history') {
    # =============================================================================================
    # THE SESSION RACK. The shell opens on Studio, so no navigation is needed — but the SESSIONS
    # row is the LAST row of the rack and is below the scroll fold at this window size, so it is
    # wheeled in with real input, one notch at a time, testing after each. Never a fixed count: a
    # rack that grew another row would otherwise stop scrolling far enough while still reporting a
    # plausible rect, and UIA would still say IsOffscreen=False (the trainer-card finding).
    # =============================================================================================
    $scale = (Get-DoorRect $window 'studio').Scale
    $viewport = Get-Rect (Get-Element $window 'RackScroll')
    $notches = 0
    while ($true) {
        $sessionsRow = Get-Element $window 'RowScriptedSession'
        $sessionsRect = Get-Rect $sessionsRow
        if (Test-Inside $sessionsRect $viewport) { break }
        if ($notches -ge 24) {
            Fail ("the Scripted Sessions rack row never came fully inside the rack viewport after $notches " +
    "wheel notches: row $($sessionsRect.X),$($sessionsRect.Y) $($sessionsRect.W)x$($sessionsRect.H) vs " +
    "viewport $($viewport.X),$($viewport.Y) $($viewport.W)x$($viewport.H)")
        }
        Wheel-Down $viewport
        $notches++
    }
    Assert-Inside $sessionsRect $windowRect 'the Scripted Sessions rack row' 'the shell window'

    Click-Rect $sessionsRect
    $sessionsRow = Get-Element $window 'RowScriptedSession'
    if (-not (Get-Selected $sessionsRow)) { Fail 'the left-click did not open the Scripted Sessions row (state drive failed)' }
    Write-Output "state drive: left-click on the Scripted Sessions rack row -> IsSelected=True ($notches wheel notch(es))"

    # (1) THE PANEL'S OWN TEXT. The rows are built at runtime from the four .session.json files
    # beside the binary (Session/ScriptedSession.ReadBuiltIns), so an EMPTY rack is a real state
    # with its own line — and it would photograph as a perfectly plausible panel. Every needle here
    # is a value that came out of a file rather than out of the markup.
    #
    # ASCII ONLY, deliberately, and for the reason the Goon window lookup gives: this file has to
    # survive an encoding round trip, so no needle contains the middle dot the meta cell really
    # renders or the em dash the readout uses.
    $panelText = (Get-Texts $window) -join "`n"
    foreach ($needle in @('Morning Drift', 'Gamer Girl', 'Good Girls', 'The Distant Doll', '30 min', '60 min')) {
        if ($panelText -notlike "*$needle*") { Fail "the session rack is missing '$needle' (its rows did not build from the shipped files)" }
    }
    Write-Output 'rack gate: four shipped sessions present with their own names and durations'

    if ($Surface -eq 'session-history') {
        # =========================================================================================
        # WHAT THE USER SEES WHEN IT ENDS. One real session is started through its confirmation,
        # left running for a length THIS STATE CHOOSES, stopped through its confirmation, and the
        # recap that upstream raises for an abort as much as for a completion is read off the real
        # desktop (MainWindow/MainWindow.Presets.cs:1681; the log's LogReady fires either way,
        # Services/Session/SessionLogService.cs:95-101). Then the door's Recent sessions button is
        # pressed and the history window is photographed.
        #
        # THE PAIR IS UPSTREAM'S RETENTION RULE. `kept` leaves the run past the 30-second floor and
        # `not-kept` stops it at once, and NOTHING ELSE DIFFERS - same session, same gestures, same
        # window, same derived cell. So the row that appears in one capture and not the other is
        # `log.Media.Count > 0 || duration >= PersistenceMinDuration` (:94) reaching the screen.
        # =========================================================================================
        $pick = Get-Element $window 'SessionRowMorningDrift'
        Click-Rect (Get-Rect $pick)
        if (-not (Get-Selected (Get-Element $window 'SessionRowMorningDrift'))) {
            Fail 'the left-click did not select the Morning Drift session row (state drive failed)'
        }
        Click-Rect (Get-Rect (Get-Element $window 'ScriptedSessionStartButton'))
        $confirmPromise = (Get-Element $window 'ScriptedSessionConfirmPromise').Current.Name
        if ($confirmPromise -notlike '*restored when the session ends*') {
            Fail "the confirmation does not carry the settings promise: '$confirmPromise'"
        }
        $stillIdle = (Get-Element $window 'ScriptedSessionStartButton').Current.Name
        if ($stillIdle -ne 'Start Session') { Fail "a session started before the confirmation was answered: button reads '$stillIdle'" }
        Click-Rect (Get-Rect (Get-Element $window 'ScriptedSessionConfirmButton'))
        $caption = (Get-Element $window 'ScriptedSessionStartButton').Current.Name
        if ($caption -notlike 'STOP SESSION (*') { Fail "the session did not start: the button reads '$caption'" }
        Write-Output "start gate: promise shown, nothing started until it was answered, button '$caption'"

        # THE DWELL IS THE STATE, and it is the product's own real clock rather than an injected
        # one - the only place in this repository where that 30-second floor is exercised against
        # wall time. 33 seconds, not 30: the stop below is three real clicks with the harness's own
        # 700 ms settle in each, and the number that decides retention is the elapsed the run
        # reports at STOP.
        if ($State -eq 'kept') {
            Write-Output 'dwell: leaving the session running past the 30-second retention floor'
            Start-Sleep -Seconds 33
        }

        Click-Rect (Get-Rect (Get-Element $window 'ScriptedSessionStartButton'))
        $timing = (Get-Element $window 'ScriptedSessionConfirmPromise').Current.Name
        if ($timing -notmatch 'Time elapsed: (?<mm>\d\d):(?<ss>\d\d)') {
            Fail "the stop confirmation does not report the elapsed time: '$timing'"
        }
        $elapsedSeconds = ([int]$Matches['mm'] * 60) + [int]$Matches['ss']
        if ($State -eq 'kept' -and $elapsedSeconds -lt 30) {
            Fail "the 'kept' capture would not be kept: the run reports ${elapsedSeconds}s elapsed, under the 30-second floor"
        }
        if ($State -eq 'not-kept' -and $elapsedSeconds -ge 30) {
            Fail "the 'not-kept' capture would be kept: the run reports ${elapsedSeconds}s elapsed, past the 30-second floor"
        }
        Write-Output "retention gate: the run reports ${elapsedSeconds}s elapsed, which is the side of the 30-second floor this state needs"
        Click-Rect (Get-Rect (Get-Element $window 'ScriptedSessionConfirmButton'))

        # THE RECAP, AS A REAL SECOND WINDOW ON A REAL DESKTOP. Nothing in the test suites can make
        # this claim: a headless frame has no window manager and no compositor.
        $recap = Wait-NamedWindow $script:proc.Id 'Session Complete'

        # RAISE IT BEFORE TOUCHING IT. The shell was put HWND_TOPMOST at launch (the harness's own
        # occluder rule), and the recap is an ordinary owned window - so a click at the recap's own
        # UIA coordinates lands on the SHELL instead. Measured: the first run here read the recap's
        # every field through UIA and then reported 'the recap did not close on Continue', because
        # the press never reached it.
        $recapHwnd = [IntPtr]$recap.Current.NativeWindowHandle
        if ($recapHwnd -eq [IntPtr]::Zero) { Fail 'the recap window has no native handle; it cannot be raised' }
        [VerifyNative]::SetWindowPos($recapHwnd, [VerifyNative]::HWND_TOPMOST, 0, 0, 0, 0,
            [VerifyNative]::SWP_NOMOVE -bor [VerifyNative]::SWP_NOSIZE -bor [VerifyNative]::SWP_SHOWWINDOW) | Out-Null
        Start-Sleep -Milliseconds 300
        $recapRect = Get-Rect $recap
        $headline = (Get-Element $recap 'SessionRecapHeadline').Current.Name
        $subtitle = (Get-Element $recap 'SessionRecapSubtitle').Current.Name
        $recapDuration = (Get-Element $recap 'SessionRecapDuration').Current.Name
        $names = (Get-Element $recap 'SessionRecapNamesNotice').Current.Name
        $awards = (Get-Element $recap 'SessionRecapAwardsNotice').Current.Name
        $noMedia = (Get-Element $recap 'SessionRecapNoMedia').Current.Name
        if ($headline -ne 'Session Ended Early') { Fail "the recap does not say the run was stopped early: '$headline'" }
        if ($subtitle -notlike '*Morning Drift*') { Fail "the recap does not name the session: '$subtitle'" }
        if ($recapDuration -notmatch '^\d\d:\d\d$') { Fail "the recap's duration cell is not MM:SS: '$recapDuration'" }
        if ($names -notlike '*never a name or a path*') { Fail "the recap does not carry the media-name refusal: '$names'" }
        if ($awards -notlike '*No XP*') { Fail "the recap does not carry the XP refusal: '$awards'" }
        if ($noMedia -notlike '*No videos or images*') { Fail "the recap does not report an empty media list: '$noMedia'" }
        if ($recapRect.W -le 0 -or $recapRect.H -le 0) { Fail 'the recap window has no rect on this desktop' }
        Write-Output ("recap gate: '$headline' / '$subtitle' / duration $recapDuration, both refusals present, " +
    "window at $($recapRect.X),$($recapRect.Y) $($recapRect.W)x$($recapRect.H)")

        Click-Rect (Get-Rect (Get-Element $recap 'SessionRecapCloseButton'))
        if ($null -ne (Get-NamedWindow $script:proc.Id 'Session Complete')) { Fail 'the recap did not close on Continue' }

        # UPSTREAM'S DOOR BUTTON (MainWindow/MainWindow.Presets.cs:1440).
        Click-Rect (Get-Rect (Get-Element $window 'ScriptedSessionHistoryButton'))
        $history = Wait-NamedWindow $script:proc.Id 'Recent Sessions'
        $script:extraHwnd = [IntPtr]$history.Current.NativeWindowHandle
        if ($script:extraHwnd -eq [IntPtr]::Zero) { Fail 'the history window has no native handle; it cannot be raised or captured' }
        [VerifyNative]::SetWindowPos($script:extraHwnd, [VerifyNative]::HWND_TOPMOST, 0, 0, 0, 0,
            [VerifyNative]::SWP_NOMOVE -bor [VerifyNative]::SWP_NOSIZE -bor [VerifyNative]::SWP_SHOWWINDOW) | Out-Null
        Start-Sleep -Milliseconds 400
        $script:extraWindow = $history
        $historyRect = Get-Rect $history

        $count = (Get-Element $history 'SessionHistoryCount').Current.Name
        if ($State -eq 'kept') {
            if ($count -ne '1 sessions') { Fail "the kept run is not in the history: the count cell reads '$count'" }
            $rowTitle = (Get-Element $history 'SessionHistoryTitle0').Current.Name
            $rowStatus = (Get-Element $history 'SessionHistoryStatus0').Current.Name
            $rowDetail = (Get-Element $history 'SessionHistoryDetail0').Current.Name
            if ($rowTitle -notlike '*Morning Drift*') { Fail "the history row does not name the session: '$rowTitle'" }
            if ($rowStatus -ne 'Aborted') { Fail "the history row does not report the abort: '$rowStatus'" }
            if ($rowDetail -notmatch '\d\d:\d\d') { Fail "the history row does not report a duration: '$rowDetail'" }
            Write-Output "history gate: '$count' / '$rowTitle' / '$rowStatus' / '$rowDetail'"
        }
        else {
            if ($count -ne '') { Fail "the short run WAS kept: the count cell reads '$count'" }
            $empty = (Get-Element $history 'SessionHistoryEmpty').Current.Name
            if ($empty -notlike '*No session logs yet*') { Fail "the empty history does not say so: '$empty'" }
            if ($null -ne $history.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SessionHistoryRow0')))) {
                Fail 'the short run WAS kept: the history has a row in it'
            }
            Write-Output "history gate: no rows, and the window says '$empty'"
        }

        # THE CELL, DERIVED FROM TWO MEASURED RECTS AND THE WINDOW'S OWN CONSTANTS - the stripe
        # cell's rule, and it has to be a derivation here because the `not-kept` state has no row to
        # measure. The list plate's inner top is the header's bottom plus the header grid's 12 DIP
        # margin, the plate's 1 DIP border and its 12 DIP padding; the band sits 2..7 DIP below that,
        # which is inside Button.history-row's own 8 DIP top padding and therefore flat fill in the
        # kept state and flat plate in the other.
        $headerRect = Get-Rect (Get-Element $history 'SessionHistoryHeader')
        $closeRect = Get-Rect (Get-Element $history 'SessionHistoryCloseButton')
        $rowTop = $headerRect.Y + $headerRect.H + [int][math]::Round((12 + 1 + 12) * $scale)
        $bandRight = $closeRect.X + $closeRect.W - [int][math]::Round((12 + 1) * $scale)
        $bandLeft = $bandRight - [int][math]::Round(200 * $scale)
        $capX = $bandLeft
        $capY = $rowTop + [int][math]::Round(2 * $scale)
        $capW = $bandRight - $bandLeft
        $capH = [int][math]::Round(5 * $scale)

        if ($State -eq 'kept') {
            # THE CROSS-CHECK, and it is the whole reason the derivation is trustworthy: in the one
            # state that HAS a row, the derived top must land on the real row's top and the band must
            # lie inside it. If the window's layout changes and this script does not, the capture
            # aims at the plate instead of at the row and the check fails looking like a product
            # regression.
            $rowRect = Get-Rect (Get-Element $history 'SessionHistoryRow0')
            if ([math]::Abs($rowRect.Y - $rowTop) -gt 2) {
                Fail ("the history list does not start where the plate says it does: the header ends at " +
    "$($headerRect.Y + $headerRect.H) and its 12+1+12 DIP puts the first row at $rowTop, but the row is really " +
    "at $($rowRect.Y) (scale $scale). The window's layout has changed and this no longer names the row")
            }
            if ($bandLeft -lt $rowRect.X -or $bandRight -gt ($rowRect.X + $rowRect.W)) {
                Fail ("the sample band $bandLeft..$bandRight is not inside the history row at " +
    "$($rowRect.X)..$($rowRect.X + $rowRect.W)")
            }
            $padBottom = $rowRect.Y + [int][math]::Round(8 * $scale)
            if (($capY + $capH) -gt $padBottom) {
                Fail ("the sample band ends at $($capY + $capH), past the row's own 8 DIP top padding ending at " +
    "$padBottom - those pixels would contain the row's glyphs")
            }
            Write-Output ("row cell: $capX,$capY ${capW}x${capH} - row $($rowRect.X),$($rowRect.Y) " +
    "$($rowRect.W)x$($rowRect.H), derived top $rowTop, padding ends $padBottom (scale $scale)")
        }
        else {
            Write-Output "plate cell: $capX,$capY ${capW}x${capH} - derived first-row top $rowTop, no row there (scale $scale)"
        }

        Assert-Inside @{ X = $capX; Y = $capY; W = $capW; H = $capH } $historyRect 'the history sample band' 'the history window'
        $windowRect = $historyRect   # the cursor is parked relative to the window being captured
    }
    elseif ($Surface -eq 'session-row') {
        # ONE ROW PER STATE, and they are different SESSIONS: the stripe's colour is the session's
        # own difficulty (Resources/Theme/Colors.xaml:191-197), so the two captures differ only in
        # which row was photographed. That is what makes each check's failure on the other capture
        # mean something about the data rather than about a style.
        $sessionId = if ($State -eq 'easy') { 'MorningDrift' } else { 'GoodGirlsDontCum' }
        $row = Get-Element $window "SessionRow$sessionId"
        $rowRect = Get-Rect $row
        Assert-Inside $rowRect $windowRect "session row $sessionId" 'the shell window'

        # THE STRIPE CELL, DERIVED FROM TWO MEASURED RECTS AND CROSS-CHECKED — the dot cell's rule.
        # The row's Grid is Auto,Auto,*,Auto,Auto: the stripe is the trailing Auto column, 4 DIP
        # wide and 20 DIP tall inside a row whose template pads 10 DIP each side, and the meta cell
        # carries a 10 DIP right margin. So meta's right edge + 10 DIP must land exactly on the
        # stripe's left edge, or the row grid has changed and this derivation no longer names it.
        $metaRect = Get-Rect (Get-Element $window "SessionMeta$sessionId")
        $pad = [int][math]::Round(10 * $scale)
        $stripeW = [int][math]::Round(4 * $scale)
        $stripeH = [int][math]::Round(20 * $scale)
        $stripeX = $rowRect.X + $rowRect.W - $pad - $stripeW
        $closes = $metaRect.X + $metaRect.W + $pad
        if ([math]::Abs($closes - $stripeX) -gt 2) {
            Fail ("the session row grid does not close: the meta cell ends at $($metaRect.X + $metaRect.W) px and " +
    "its 10 DIP margin puts the stripe at $closes, but the row's trailing edge less its 10 DIP padding and the " +
    "4 DIP stripe puts it at $stripeX (scale $scale). The row grid has changed and this no longer names the stripe")
        }

        $capX = $stripeX
        $capY = [int]($rowRect.Y + ($rowRect.H - $stripeH) / 2)
        $capW = $stripeW
        $capH = $stripeH
        Write-Output ("stripe cell: $capX,$capY ${capW}x${capH} - row $($rowRect.X),$($rowRect.Y) " +
    "$($rowRect.W)x$($rowRect.H), meta ends $($metaRect.X + $metaRect.W), grid closes at $closes (scale $scale)")
    }
    else {
        # session-start
        # THE ONE BUTTON, IN ITS TWO STATES. Idle is pink and says Start Session; running is red and
        # says STOP SESSION with the time left in it (MainWindow.StartStop.cs:756,
        # MainWindow.Presets.cs:1752, en.json:2321). Reaching `running` means really starting a
        # scripted session, through the confirmation, with real clicks.
        $pick = Get-Element $window 'SessionRowMorningDrift'
        Click-Rect (Get-Rect $pick)
        if (-not (Get-Selected (Get-Element $window 'SessionRowMorningDrift'))) {
            Fail 'the left-click did not select the Morning Drift session row (state drive failed)'
        }
        Write-Output 'state drive: left-click on the Morning Drift session row -> IsSelected=True'

        $button = Get-Element $window 'ScriptedSessionStartButton'
        if ($State -eq 'running') {
            Click-Rect (Get-Rect $button)

            # THE CONFIRMATION IS THE CEREMONY, and it is read before it is answered: this is the
            # sentence the whole snapshot/restore machinery exists to keep
            # (MainWindow/MainWindow.Presets.cs:1467-1470). A capture taken past a confirmation that
            # never appeared would be a capture of a session started without one.
            $confirmTitle = (Get-Element $window 'ScriptedSessionConfirmTitle').Current.Name
            $confirmDetail = (Get-Element $window 'ScriptedSessionConfirmDetail').Current.Name
            $confirmPromise = (Get-Element $window 'ScriptedSessionConfirmPromise').Current.Name
            if ($confirmTitle -ne 'Start Morning Drift?') { Fail "the start confirmation reads '$confirmTitle'" }
            if ($confirmDetail -ne 'Duration: 30 minutes') { Fail "the confirmation does not name the duration: '$confirmDetail'" }
            if ($confirmPromise -notlike '*restored when the session ends*') {
                Fail "the confirmation does not carry the settings promise: '$confirmPromise'"
            }

            # NOTHING HAS STARTED YET, read rather than assumed: the button is still the start.
            $stillIdle = (Get-Element $window 'ScriptedSessionStartButton').Current.Name
            if ($stillIdle -ne 'Start Session') { Fail "a session started before the confirmation was answered: button reads '$stillIdle'" }
            Write-Output "confirm gate: '$confirmTitle' / '$confirmDetail' / promise present, and nothing started yet"

            Click-Rect (Get-Rect (Get-Element $window 'ScriptedSessionConfirmButton'))

            # THE RUN'S OWN STATE, from three of its own controls: the phase it announced at START,
            # the readout it published on the same reading, and the button's countdown caption.
            $phase = (Get-Element $window 'ScriptedSessionPhaseState').Current.Name
            $readout = (Get-Element $window 'ScriptedSessionProgressState').Current.Name
            $caption = (Get-Element $window 'ScriptedSessionStartButton').Current.Name
            if ($phase -notlike '*Phase 1 of 5*') { Fail "the session did not announce its first phase: '$phase'" }
            # A PATTERN, NOT AN INSTANT, and the first draft got that wrong: it demanded
            # "00:00 elapsed" and a real desktop read back "00:01 elapsed, 29:58 remaining",
            # because a real session's clock really does run between the click and the read. What
            # is being confirmed is that the readout is a live countdown of a 30-minute session,
            # which is exactly what this matches.
            if ($readout -notmatch '^\d+%.*\b(\d\d):(\d\d) elapsed, (\d\d):(\d\d) remaining$') {
                Fail "the readout is not a live countdown: '$readout'"
            }
            if ($caption -notlike 'STOP SESSION (*') { Fail "the button did not become the stop: '$caption'" }
            Write-Output "run gate: '$phase' / '$readout' / button '$caption'"
        }
        else {
            $caption = (Get-Element $window 'ScriptedSessionStartButton').Current.Name
            if ($caption -ne 'Start Session') { Fail "the idle capture would not be idle: the button reads '$caption'" }
            Write-Output "idle gate: button '$caption', nothing running"
        }

        $buttonRect = Get-Rect (Get-Element $window 'ScriptedSessionStartButton')
        Assert-Inside $buttonRect $windowRect 'the scripted session button' 'the shell window'

        # THE BAND checks.json SAMPLES, PROVED AGAINST THE MEASURED CONTROL. y 0.08..0.20 must land
        # inside the button's own 12 DIP top padding (Button.session-start, MainWindow.axaml:357) so
        # it is flat fill and not caption glyphs, and x 0.25..0.75 must clear the 14 DIP corner
        # radius (:353) at both ends. A button that grew a different padding fails here by name
        # instead of sampling its own text.
        $bandTop = $buttonRect.Y + [int]($buttonRect.H * 0.08)
        $bandBottom = $buttonRect.Y + [int]($buttonRect.H * 0.20)
        $padTop = $buttonRect.Y + [int][math]::Round(12 * $scale)
        if ($bandBottom -gt $padTop) {
            Fail ("the fill band y 0.08..0.20 of this capture is $bandTop..$bandBottom, which reaches past the " +
    "button's own top padding ending at $padTop. Those pixels would contain the caption's glyphs, so the " +
    'capture would not be evidence about the fill')
        }
        $radius = [int][math]::Round(14 * $scale)
        $bandLeft = $buttonRect.X + [int]($buttonRect.W * 0.25)
        $bandRight = $buttonRect.X + [int]($buttonRect.W * 0.75)
        if ($bandLeft -lt ($buttonRect.X + $radius) -or $bandRight -gt ($buttonRect.X + $buttonRect.W - $radius)) {
            Fail ("the fill band x 0.25..0.75 is $bandLeft..$bandRight, which is not clear of the button's 14 DIP " +
    "corner radius at $($buttonRect.X + $radius)..$($buttonRect.X + $buttonRect.W - $radius)")
        }
        Write-Output ("button rect $($buttonRect.X),$($buttonRect.Y) $($buttonRect.W)x$($buttonRect.H) @ scale $scale; " +
    "fill band y $bandTop..$bandBottom inside the top padding ending $padTop, x $bandLeft..$bandRight clear of the radius")

        $capX = $buttonRect.X; $capY = $buttonRect.Y; $capW = $buttonRect.W; $capH = $buttonRect.H
    }
}
elseif ($Surface -eq 'companion-permissions') {
    # =============================================================================================
    # WHAT SHE IS ALLOWED TO DO. Two hops of real input to the companion surface - the Companion
    # rail door, then the door's own Show companion button - and then ONE gesture is the whole
    # difference between the two states: `closed` is what a fresh process gives the user, and
    # `admitted` is the same window after the master switch has been pressed once.
    #
    # THE DEFAULT IS THE CLAIM, so it is gated on the UIA tree before any pixel is read: in `closed`
    # not one of the ten per-effect switches EXISTS in the tree, and in `admitted` all ten exist and
    # every one of them reads ToggleState Off. A pixel check cannot tell an unticked switch from a
    # ticked one at this size, and it certainly cannot tell an absent one from an off-screen one.
    # =============================================================================================
    $companionDoor = Get-DoorRect $window 'companion'
    $scale = $companionDoor.Scale
    Click-Rect $companionDoor
    Assert-Route $window 'companion'
    Write-Output "state drive: left-click on the Companion door -> route: companion (probe: $($companionDoor.Raw))"

    # THE DOOR'S OWN SENTENCE, read before the window opens. Upstream's permissions grid lives on
    # the companion PAGE (Views/Controls/Companion/AiPermissionsGrid.xaml, inside CompanionTabView);
    # this port's page is a door, so the door has to say where the grid went or a user looking for
    # it on this page finds nothing and concludes there is nothing.
    $pointer = (Get-Element $window 'PermissionsPointer').Current.Name
    if ($pointer -notlike '*allowed to do to your screen*') {
        Fail "the Companion door does not name the permissions surface: '$pointer'"
    }
    if ($pointer -notlike '*Nothing is admitted until you say so*') {
        Fail "the Companion door does not state the default: '$pointer'"
    }
    Write-Output "door gate: '$pointer'"

    Click-Rect (Get-Rect (Get-Element $window 'CompanionButton'))
    $companion = Wait-WindowLike $script:proc.Id 'Companion'
    $script:extraWindow = $companion
    $script:extraHwnd = [IntPtr]$companion.Current.NativeWindowHandle
    if ($script:extraHwnd -eq [IntPtr]::Zero) { Fail 'the companion window has no native handle; it cannot be raised or captured' }

    # RAISE IT BEFORE TOUCHING IT. The shell is HWND_TOPMOST (the harness's own occluder rule) and
    # this window is an ordinary owned one, so a press at its own UIA coordinates lands on the SHELL.
    # The recap path measured that exact failure; the master switch below would silently never move.
    [VerifyNative]::SetWindowPos($script:extraHwnd, [VerifyNative]::HWND_TOPMOST, 0, 0, 0, 0,
        [VerifyNative]::SWP_NOMOVE -bor [VerifyNative]::SWP_NOSIZE -bor [VerifyNative]::SWP_SHOWWINDOW) | Out-Null
    Start-Sleep -Milliseconds 400
    $companionRect = Get-Rect $companion
    if ($companionRect.W -le 0 -or $companionRect.H -le 0) { Fail 'the companion window has no rect on this desktop' }

    # THE WINDOW'S OWN HONESTY LINE, which is what stops this panel over-claiming: her replies do
    # not reach the command executor in this build at all.
    $notice = (Get-Element $companion 'EffectDispatchNotice').Current.Name
    if ($notice -notlike 'Nothing dispatches yet*') { Fail "the companion window does not state that nothing dispatches: '$notice'" }

    $rowIds = @('Flash', 'Video', 'Audio', 'Bubbles', 'Subliminal', 'Overlay', 'LockCard', 'Bounce', 'Haptic', 'GetBackToMe')
    $master = Get-Element $companion 'EffectsMasterToggle'
    if ((Get-Toggle $master) -ne [System.Windows.Automation.ToggleState]::Off) {
        Fail 'the master effect switch is ON in a fresh process; the default is not closed'
    }
    foreach ($id in $rowIds) {
        if ($null -ne (Find-Element $companion $id)) {
            Fail "the '$id' permission switch is on screen while the master switch is off (upstream hides the panel, MainWindow/MainWindow.Patreon.cs:1477)"
        }
    }
    Write-Output "closed gate: master switch Off and none of the $($rowIds.Count) per-effect switches is in the tree"

    if ($State -eq 'admitted') {
        Click-Rect (Get-Rect $master)
        if ((Get-Toggle (Get-Element $companion 'EffectsMasterToggle')) -ne [System.Windows.Automation.ToggleState]::On) {
            Fail 'the master switch did not take the press (state drive failed)'
        }
        foreach ($id in $rowIds) {
            $sw = Find-Element $companion $id
            if ($null -eq $sw) { Fail "the '$id' permission switch did not appear when the master switch went on" }
            if ((Get-Toggle $sw) -ne [System.Windows.Automation.ToggleState]::Off) {
                Fail ("the '$id' permission switch is TICKED the moment the panel opens. The master switch is a " +
    'door, never a bulk admission, and a pre-ticked switch is a consent decision nobody made')
            }
        }
        Write-Output "admitted gate: all $($rowIds.Count) switches on screen, every one of them still Off"

        # AND THE CHANGE HALF: one press on the Overlay switch, which is the one switch that governs
        # two command kinds (upstream's AllowAiOverlay covers spiral and pink,
        # Services/Commands/AiCommandService.cs:193-194).
        $overlay = Find-Element $companion 'Overlay'
        Click-Rect (Get-Rect $overlay)
        if ((Get-Toggle (Find-Element $companion 'Overlay')) -ne [System.Windows.Automation.ToggleState]::On) {
            Fail 'the Overlay permission switch did not take the press; the user cannot change what she is allowed to do'
        }
        Write-Output 'change gate: one real press ticked the Overlay permission'
    }

    # THE BAND, derived from the master switch's own rect. The panel is a Border and Avalonia gives
    # Border no automation peer (harness surprise #1), so its rect cannot be read - but the switch
    # above it is a CheckBox with a real peer, and the panel opens 6 DIP below it (the settings
    # StackPanel's Spacing) behind a 1 DIP border and 6 DIP of padding. So 8..12 DIP below the
    # switch is inside that padding in `admitted`, and is the window's own ground in `closed`.
    $masterRect = Get-Rect (Get-Element $companion 'EffectsMasterToggle')
    $capX = $companionRect.X + [int][math]::Round(20 * $scale)
    $capY = $masterRect.Y + $masterRect.H + [int][math]::Round(8 * $scale)
    $capW = [int][math]::Round(380 * $scale)
    $capH = [int][math]::Round(4 * $scale)

    if ($State -eq 'admitted') {
        # THE CROSS-CHECK: the band must be above the FIRST switch's line, or it is sampling a row
        # of the grid rather than the panel's own padding and a layout change would aim it at glyphs.
        $firstSwitch = Get-Rect (Find-Element $companion 'Flash')
        if ($firstSwitch.Y -lt ($capY + $capH)) {
            Fail ("the sample band ends at $($capY + $capH) and the first permission switch starts at " +
    "$($firstSwitch.Y); the band is inside the grid's own rows rather than the panel's padding")
        }
        Write-Output ("band $capX,$capY ${capW}x${capH} @ scale $scale - between the master switch ending at " +
    "$($masterRect.Y + $masterRect.H) and the first switch at $($firstSwitch.Y)")
    }
    else {
        Write-Output "band $capX,$capY ${capW}x${capH} @ scale $scale - below the master switch ending at $($masterRect.Y + $masterRect.H), no panel there"
    }

    Assert-Inside @{ X = $capX; Y = $capY; W = $capW; H = $capH } $companionRect 'the permissions sample band' 'the companion window'
    $windowRect = $companionRect   # the cursor is parked relative to the window being captured
}
else {
    # The startup trace and the typed capability states live on the System page now, so
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
# FENCE THE READ. Between "the app painted" and "this process read the screen" there is
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

# CLOSE THE GOON WINDOW FIRST, BY ITS OWN HANDLE.
#
# Two reasons, and both are defects if skipped. (1) Process.MainWindowHandle does not say which of
# two top-level windows it names, so CloseMainWindow could send WM_CLOSE to either; the goon window
# is closed through the handle UIA gave us for the window this script actually found, and only then
# is the process refreshed so the dashboard close targets the dashboard. (2) The goon window CANCELS
# its first close on a live page and runs the real exit handshake -- end-run to the page, a bounded
# 1200 ms wait for exit-done (boot.js:2437-2465) -- so a close that is not waited on races it.
if ($null -ne $script:goonWindow -and $script:goonHwnd -ne [IntPtr]::Zero) {
    [VerifyNative]::PostMessage($script:goonHwnd, [VerifyNative]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    $closeDeadline = [Diagnostics.Stopwatch]::StartNew()
    while ($closeDeadline.Elapsed.TotalSeconds -lt 20) {
        if ($script:proc.HasExited) { break }
        if ($null -eq (Get-GoonWindow $script:proc.Id)) { break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $script:proc.HasExited -and $null -ne (Get-GoonWindow $script:proc.Id)) {
        Fail ("the Goon host window did not close within $([int]$closeDeadline.Elapsed.TotalSeconds)s of WM_CLOSE. " +
    'Its graceful path posts end-run and waits a bounded 1200ms for exit-done; a window still open ' +
    'well past that is a real finding about the exit handshake, not a slow machine')
    }
    Write-Output "goon window closed after $([math]::Round($closeDeadline.Elapsed.TotalSeconds, 1))s (graceful exit handshake)"
    # The dashboard is the only top-level window left; re-read the handle that names it.
    $script:proc.Refresh()
}

if ($null -ne $script:extraWindow -and $script:extraHwnd -ne [IntPtr]::Zero) {
    [VerifyNative]::PostMessage($script:extraHwnd, [VerifyNative]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 600
    $script:proc.Refresh()
    Write-Output 'session window closed by its own handle'
}

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
# exit 2. Found by that exact false failure while adding the rack phase.
exit 0
