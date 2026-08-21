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
#        pwsh client/tools/verify/capture.ps1 -Surface goon-page -State first-run
#
# SP-132 -- THE GOON PAGE, and the one way this surface differs from every other one here.
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
param(
    [Parameter(Mandatory)][ValidateSet('dashboard', 'rail-door', 'rack-row', 'rack-row-dot', 'goon-page')] [string]$Surface,
    [Parameter(Mandatory)][ValidateSet('unselected', 'selected', 'off', 'armed', 'first-run')] [string]$State
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
# SP-132 -- AND THE PAGE'S OWN PREFS. Hygiene, and NOT what makes this deterministic.
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
    // SP-132: the goon host is a SECOND top-level window, and Process.MainWindowHandle does not
    // say which of the two it names. WM_CLOSE is posted to the handle UIA gave us for the window
    // this script actually found, so the close targets the window it means.
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    public const uint WM_CLOSE = 0x0010;
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

# ---------------------------------------------------------------------------------------------
# SP-132 -- the goon host window.
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

$script:goonWindow = $null
$script:goonHwnd = [IntPtr]::Zero

if ($Surface -eq 'goon-page') {
    # =========================================================================================
    # SP-132 -- THE GOON PAGE. The first capture in this harness of something the PRODUCT did not
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
    # (the SP-122 finding), so clicking without this check would click the wallpaper.
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

    # THE RECT, from the window's own probe. Avalonia gives Panel no UIA peer (SP-007 surprise #1),
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
elseif ($Surface -eq 'rack-row' -or $Surface -eq 'rack-row-dot') {
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

# SP-132 -- CLOSE THE GOON WINDOW FIRST, BY ITS OWN HANDLE.
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
