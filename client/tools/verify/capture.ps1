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
    [Parameter(Mandatory)][ValidateSet('dashboard', 'rail-door', 'rack-row', 'rack-row-dot', 'goon-page', 'trainer-card', 'trainer-card-level', 'trainer-card-record', 'session-row', 'session-start', 'session-history', 'studio-dial', 'audio-dial', 'companion-permissions', 'companion-privacy', 'companion-transcript', 'toast', 'popquiz-card', 'mantra-window')] [string]$Surface,
    [Parameter(Mandatory)][ValidateSet('unselected', 'selected', 'off', 'armed', 'first-run', 'no-runs-yet', 'easy', 'hard', 'idle', 'running', 'kept', 'not-kept', 'live', 'locked', 'closed', 'admitted', 'broad', 'titles', 'open', 'saved', 'refused', 'asking', 'fresh', 'earned', 'typed', 'read', 'unreadable')] [string]$State
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
    (Join-Path $env:APPDATA 'CcpClient\session_lockcard.json'),
    # The FIFTH, and the Pop Quiz card's own subject. Its two dials are what decide whether a
    # question ever comes up and how long the capture has to wait for it, and both are driven here
    # through the real controls - so a document left behind by a previous run would mean the drive
    # was confirming a state it did not set (PopQuizPresetDocument.FileName).
    (Join-Path $env:APPDATA 'CcpClient\session_popquiz.json'),
    # The SIXTH, and the Trainer Card level block's whole subject: the XP ledger
    # (ProgressionDocument.FileName, read at IntakeLaunch.ReadTrainerCardLevel out of the SAME data
    # directory as settings.json). Its absence is a STATE rather than a failure — the card's `fresh`
    # answer, LVL 1 with an empty bar (TrainerCardLevel.Read's missing-file arm) — and the `earned`
    # state re-seeds it below. Unconditional here for the same reason session_preset.json is: a
    # ledger left behind by a previous run would put a level on the `fresh` capture and the pair
    # would differ for a reason that has nothing to do with what was seeded.
    (Join-Path $env:APPDATA 'CcpClient\progression.json'),
    # The SEVENTH, and the audio-dial pair's own subject. Both of its captures are of the master
    # volume slider AT THE SAME VALUE - that is what makes them a statement about the livery rather
    # than about two thumb positions - and the value they are both at is upstream's fresh-install 32
    # (Models/AppSettings.cs:1127). A document left behind by a previous run would move the thumb,
    # so this is the session_lockcard.json note applied to the row beside it
    # (AudioSettingsDocument.FileName).
    (Join-Path $env:APPDATA 'CcpClient\audio.json')
)
$progressionFile = Join-Path $env:APPDATA 'CcpClient\progression.json'
$awardsFile = Join-Path $env:APPDATA 'CcpClient\graded_run_awards.json'
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
$companionMemoryFile = Join-Path $env:APPDATA 'CcpClient\ai_memory.json'
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
    # THE LEVEL BLOCK'S TWO STATES ARE THE SAME REGION OF THE SAME BAR, and the only difference
    # between the two captures is what is in progression.json — which is the session-row precedent
    # (two rows, one .session.json field between them) applied to a fraction instead of a colour.
    # `fresh` is the ledger REMOVED: TrainerCardLevel.Read's missing-file arm answers LVL 1 with
    # nothing banked, so the bar is all track. `earned` is a seeded level 42 with 1000.5 into it, so
    # the sampled band is fill. Each check must fail on the other capture; if it does not, the bar
    # is not reading the ledger at all.
    'trainer-card-level' = @('fresh', 'earned')
    # THE RECORD'S THREE STATES, and the middle one is a finding rather than a choice. The card's
    # own type has three (TrainerCardRecordState: NoRunsYet, Read, Unreadable); `no-runs-yet` is the
    # landed `trainer-card` surface's state, and these are the other two - plus the earned row that
    # `read` cannot be photographed without.
    #
    # A READ RECORD WITH NOTHING EARNED IS NOT PRODUCIBLE BY THIS BUILD, so there is no such capture
    # to take. GradedRunAwards.RecordGradedRun awards top_of_the_class FIRST and UNCONDITIONALLY on a
    # top-marks run, before the category is even looked at (GradedRunAwards.cs:245-248, upstream's
    # GamificationBridge.cs:600), and the file is written ONLY when something was awarded or a
    # category was new (:261-264). So the first bytes that record ever holds already carry an earned
    # row. `read` is therefore ONE top-marks run - top_of_the_class earned, one category cleared -
    # and `earned` is three distinct categories, which is the only way honor_roll is ever added.
    'trainer-card-record' = @('read', 'unreadable', 'earned')
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
    # THE SAME PAIR RUN IN REVERSE, and the only surface here whose two states are meant to look
    # IDENTICAL. `live` is the audio row's master volume with nothing running; `running` is the same
    # dial at the same value with a real scripted session under way. A check that failed on
    # `running` would be reporting an OVER-LOCK - a control taken away from the user for no benefit,
    # which upstream calls out as its own regression (Features/SessionLock.cs:36-38). The inversion
    # is therefore against studio-dial-locked-track rather than against this surface's other state.
    'audio-dial' = @('live', 'running')
    # THE TWO PERMISSION STATES ARE ONE GESTURE APART, and the gesture is the whole claim: `closed`
    # is what a fresh process gives the user (master off, not one per-effect switch on screen) and
    # `admitted` is what she gets after pressing the master switch once. A check that passed on both
    # would be saying the default is not a default.
    'companion-permissions' = @('closed', 'admitted')
    # THE PRIVACY DIAL'S TWO STOPS ARE ONE NAMED APP APART, and that is the claim: `broad` is what
    # the user gets after asking for page titles WITHOUT naming anyone (the dial refuses to move,
    # because nothing widened), `titles` is the same window after one app has been named. A check
    # that passed on both would be saying the third stop means nothing.
    'companion-privacy' = @('broad', 'titles')
    # THE TYPED MANTRA GAME. Both states are the same window on the same repetition, and the only
    # thing a USER did differently is type: `fresh` is the line as the game hands it over - every
    # character dim - and `typed` is a line after all but its last two characters have arrived on a
    # real keyboard. A check that passed on both would be saying the per-character feedback, which
    # is the whole game, is paint.
    #
    # THE TWO RUNS MAY OR MAY NOT SHOW THE SAME SENTENCE, and both cases have been measured. The
    # mantra is DRAWN AT RANDOM from a pool of five, so each run gets its own line at its own
    # Viewbox scale: one pair drew 'I am deeply relaxed' (2450x473) against 'My mind is open and
    # receptive' (2450x307) and inverted 0.281/0.003 and 0.273/0.000; the next pair drew the same
    # sentence twice, the same band to the pixel, and inverted 0.289/0.003 and 0.273/0.000. That
    # the checks are indifferent to WHICH sentence is drawn is what says they read the
    # per-character STATE and not the layout - and it is why the band is cut from the line's own
    # rect rather than fixed.
    'mantra-window' = @('fresh', 'typed')
    # THE TRANSCRIPT: `closed` is the companion window with no transcript in the UIA tree at all,
    # `open` is the same window with the read-only viewer over it. One press between them.
    'companion-transcript' = @('closed', 'open')
    # THE POP QUIZ CARD, and it has ONE state because the card has one: a question is up or it is
    # not, and "not" is a photograph of the desktop. What makes this capture evidence is not a second
    # state but the DRIVE - the module is switched on through its own box, paced through its own
    # slider, and the card is waited for on the session's real clock, so a card that never appeared
    # refuses by name instead of photographing whatever was there.
    'popquiz-card' = @('asking')
    # THE IN-APP TOAST, AND ITS TWO STATES ARE TWO REAL OUTCOMES OF TWO REAL FILE DIALOGS: `saved`
    # is a phrase export that really wrote a file the user chose, `refused` is an import handed a
    # file that is not a backup. Same control, same derived band; the only thing that differs is the
    # accent, which is chosen by the TYPED outcome. A check that passed on both would be saying the
    # type of a message is decoration.
    'toast' = @('saved', 'refused')
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
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    // THE WORK AREA, and it is here because a measurement corrected an assumption. A maximized
    // Avalonia window with WindowDecorations=None was expected to take the whole primary display
    // and does NOT: it takes the work area, measured at 2880x1716 against a 2880x1800 screen on
    // the first run of the mantra surface. That is the same rule WPF's WindowState=Maximized
    // obeys, so the port is not diverging - but a harness that demanded the screen would have
    // called a correct window broken.
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, ref RECT data, uint winIni);
    public const uint SPI_GETWORKAREA = 0x0030;
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
    // The keyboard, for the one control this harness has to press that the PRODUCT did not build:
    // the Windows common item dialog's default action. Measured rather than chosen — its OPEN
    // variant exposes no IDOK-shaped control in the UIA tree at all (only Cancel, id 2), so there
    // is nothing to Invoke, while ENTER on the focused file-name edit commits BOTH variants.
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    public const byte VK_RETURN = 0x0D;
    // The same call types an app name into the privacy card's own box. Real keystrokes rather than
    // a ValuePattern SetValue, because typing IS what a user does there and a SetValue would not
    // exercise the binding a regression would break. Only a-z is needed and VK_A..VK_Z are the
    // uppercase ASCII codes, which with no shift held produce lowercase.
    // And the one key the PRODUCT reads on its own surface: Escape closes a pop quiz card with no
    // answer, which is upstream's own behaviour (Windows/PopQuizWindow.xaml.cs:128-134). Pressed
    // here so the capture ends by proving the card really takes keyboard input from a real
    // keyboard, rather than by killing a process that still holds the user's foreground.
    public const byte VK_ESCAPE = 0x1B;
    // And the one key VK_A..VK_Z cannot produce. The mantra game's lines are words with spaces in
    // them, and a mantra typed without its spaces stops matching at the first gap.
    public const byte VK_SPACE = 0x20;
    public const uint KEYUP = 0x0002;
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

# Every Text descendant with its screen rect, in the order a reader meets them (top to bottom, then
# left to right). Get-Texts above answers "is this sentence on the page"; this answers "these are the
# card's lines, in this order, at these rects" - which is what a surface whose STATES differ by which
# sentences are present, and by how long they are, has to gate on before it reads a pixel.
function Get-TextRects($window) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $els = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $out = @()
    foreach ($t in $els) {
        $r = $t.Current.BoundingRectangle
        $out += [pscustomobject]@{
            Name = $t.Current.Name
            X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height
        }
    }
    return @($out | Sort-Object Y, X)
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

# A window that is owned by an owned window. Get-NamedWindow above walks ONE level of ownership,
# which is every owned window this harness had until the transcript: it is owned by the COMPANION
# window, which is itself owned by the shell, so it is a GRANDCHILD in the UIA tree and the
# one-level walk never sees it. Measured, not reasoned: the first run of the `open` state failed
# with the transcript plainly on screen and the diagnostic listing only 'CCP Client' and
# 'Companion - CCP Client' (owned).
#
# Matched on control type AND name together, so a Text element that happens to carry the same
# caption (the button's own label, the window's own heading) can never be mistaken for the window.
function Get-DeepWindow([int]$processId, [string]$title) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $byProcess = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $named = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $title)))
    foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $byProcess)) {
        if ($w.Current.Name -eq $title) { return $w }
        $hit = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $named)
        if ($null -ne $hit) { return $hit }
    }
    return $null
}

# The same search, bounded, failing BY NAME with what this process really has on screen.
function Wait-DeepWindow([int]$processId, [string]$title, [int]$seconds = 10) {
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt $seconds) {
        $w = Get-DeepWindow $processId $title
        if ($null -ne $w) { return $w }
        Start-Sleep -Milliseconds 200
    }
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $isWindow = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $seen = @()
    foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)))) {
        $seen += "'$($w.Current.Name)'"
        foreach ($o in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $isWindow)) {
            $seen += "'$($o.Current.Name)' (owned, any depth)"
        }
    }
    Fail "no '$title' window appeared within ${seconds}s (this process has: $($seen -join ', '))"
}

# Type a lowercase word into a product text box with REAL keystrokes. VK_A..VK_Z are the uppercase
# ASCII codes and produce lowercase with no shift held, which is the whole alphabet this harness
# needs: the one box it fills is an app name, and the product lowercases every entry anyway
# (AiTitleAllowList.SanitizeEntry). The typed text is READ BACK through UIA before anything is
# pressed, so a keystroke the window never received is a named failure rather than a mystery.
function Type-Lowercase($element, [string]$text) {
    Click-Rect (Get-Rect $element)
    Start-Sleep -Milliseconds 150
    foreach ($ch in $text.ToCharArray()) {
        if ($ch -lt 'a' -or $ch -gt 'z') { Fail "Type-Lowercase can only type a-z; '$ch' is not" }
        $vk = [byte][char]([string]$ch).ToUpperInvariant()
        [VerifyNative]::keybd_event($vk, 0, 0, [IntPtr]::Zero)
        [VerifyNative]::keybd_event($vk, 0, [VerifyNative]::KEYUP, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 25
    }
    Start-Sleep -Milliseconds 150
    $typed = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($typed -ne $text) { Fail "the box reads '$typed' after typing '$text'; the keystrokes did not reach it" }
    Write-Output "typed '$text' into '$($element.Current.AutomationId)' (read back through UIA)"
}

# The companion window, reached the way a user reaches it: the Companion rail door, then the
# door's own button. Raised topmost before anything is pressed - the shell is HWND_TOPMOST, so a
# press at an owned window's own UIA coordinates otherwise lands on the SHELL (the finding the
# recap and permissions paths already record).
# The typed mantra window, found by a SUBSTRING of its title rather than a prefix: the shell is
# called 'CCP Client' and the game 'CCP - Mantra Lab' (with an em dash the product owns and this
# script has no reason to depend on the encoding of), so StartsWith('CCP') would return the SHELL.
# Owned windows are UIA DESCENDANTS of their owner, which is the finding the recap path recorded.
function Wait-MantraWindow([int]$processId, [int]$seconds = 15) {
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
            if ($w.Current.Name -like '*Mantra Lab*') { return $w }
            foreach ($o in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $isWindow)) {
                $seen += "'$($o.Current.Name)' (owned, any depth)"
                if ($o.Current.Name -like '*Mantra Lab*') { return $o }
            }
        }
        Start-Sleep -Milliseconds 200
    }
    Fail "no mantra window appeared within ${seconds}s (this process has: $($seen -join ', '))"
}

# Type a mantra with REAL keystrokes into whatever holds the keyboard - deliberately NOT clicking
# first, because acquiring focus without a click is the claim. VK_A..VK_Z with no shift produce
# lowercase, and MantraSession.Match compares case-insensitively (MantraSession.cs:257-275, WPF
# Windows/MantraWindow.xaml.cs:121-130), so a lowercase 'i' matches the mantra's 'I' and lights it.
function Type-Mantra([string]$text) {
    foreach ($ch in $text.ToCharArray()) {
        if ($ch -eq ' ') {
            [VerifyNative]::keybd_event([VerifyNative]::VK_SPACE, 0, 0, [IntPtr]::Zero)
            [VerifyNative]::keybd_event([VerifyNative]::VK_SPACE, 0, [VerifyNative]::KEYUP, [IntPtr]::Zero)
        }
        elseif (($ch -ge 'a' -and $ch -le 'z') -or ($ch -ge 'A' -and $ch -le 'Z')) {
            $vk = [byte][char]([string]$ch).ToUpperInvariant()
            [VerifyNative]::keybd_event($vk, 0, 0, [IntPtr]::Zero)
            [VerifyNative]::keybd_event($vk, 0, [VerifyNative]::KEYUP, [IntPtr]::Zero)
        }
        else { Fail "Type-Mantra can only type letters and spaces; '$ch' is not one" }
        Start-Sleep -Milliseconds 25
    }
    Start-Sleep -Milliseconds 250
}

function Open-CompanionWindow($window) {
    $door = Get-DoorRect $window 'companion'
    Click-Rect $door
    Assert-Route $window 'companion'
    Write-Output "state drive: left-click on the Companion door -> route: companion (probe: $($door.Raw))"

    Click-Rect (Get-Rect (Get-Element $window 'CompanionButton'))
    $companion = Wait-WindowLike $script:proc.Id 'Companion'
    $script:extraWindow = $companion
    $script:extraHwnd = [IntPtr]$companion.Current.NativeWindowHandle
    if ($script:extraHwnd -eq [IntPtr]::Zero) { Fail 'the companion window has no native handle; it cannot be raised or captured' }
    [VerifyNative]::SetWindowPos($script:extraHwnd, [VerifyNative]::HWND_TOPMOST, 0, 0, 0, 0,
        [VerifyNative]::SWP_NOMOVE -bor [VerifyNative]::SWP_NOSIZE -bor [VerifyNative]::SWP_SHOWWINDOW) | Out-Null
    Start-Sleep -Milliseconds 400
    # The window is handed back on $script:extraWindow rather than RETURNED. A PowerShell function
    # returns everything written to its output stream, so returning it would hand the caller an
    # array of this function's own transcript lines with the element last - and member access on
    # that array enumerates, so `$companion.FindFirst(...)` reads as a call on a STRING. Measured:
    # the first run of this surface died with exactly that message before any state was driven.
}

# ---------------------------------------------------------------------------------------------
# THE OS FILE DIALOG. The only control in this harness that the PRODUCT did not build: it is the
# Windows common item dialog, opened by Avalonia's Win32StorageProvider through IFileDialog.
#
# Driven through UIA PATTERNS rather than through coordinates, and that is a deliberate exception
# to this file's real-input rule. The rule exists because a click is what a user does and what a
# regression breaks; the thing under test HERE is that the port opens a real system dialog and reads
# a real file back, not that Windows' own dialog can be clicked. Coordinates would also have to
# cope with a dialog this script does not lay out, at whatever size the shell's owner rect gives it.
# Both halves refuse BY NAME when the control is missing, so a dialog that changed shape is a
# finding rather than a silent no-op.
# ---------------------------------------------------------------------------------------------
function Submit-DialogPath($dialog, [string]$path, [string]$caption, [int]$processId) {
    $edit = Get-DialogFileNameEdit $dialog
    $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($path)
    Write-Output "dialog: typed the chosen path into '$($edit.Current.Name)' (id $($edit.Current.AutomationId))"

    # A REAL ENTER on the focused file-name edit, rather than an Invoke on IDOK, and that is
    # measured rather than preferred: the SAVE variant exposes IDOK as a Button with an EMPTY Name,
    # and the OPEN variant exposes NO pressable control with id 1 at all - its whole button set is
    # Help, Organize, New folder, the scrollbars, the column filters and Cancel (id 2). An id-only
    # search across every control type is worse still: the file browser gives each ITEM in the
    # current folder a numeric AutomationId, so 'id = 1' matched a ListItem named 'Adobe' and
    # invoked THAT. Enter commits both variants, and it is what a user does.
    $edit.SetFocus()
    Start-Sleep -Milliseconds 250
    [VerifyNative]::keybd_event([VerifyNative]::VK_RETURN, 0, 0, [IntPtr]::Zero)
    [VerifyNative]::keybd_event([VerifyNative]::VK_RETURN, 0, [VerifyNative]::KEYUP, [IntPtr]::Zero)

    # A dialog still up means the commit did not take - a wrong path, a filter that hid the file, a
    # focus that went elsewhere. Reported as itself rather than as "no toast appeared" 20 s later.
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt 20) {
        if ($null -eq (Get-NamedWindow $processId $caption)) {
            Write-Output ("dialog: '$caption' committed with ENTER and closed after " +
    "$([math]::Round($deadline.Elapsed.TotalSeconds, 1))s")
            return
        }
        Start-Sleep -Milliseconds 200
    }
    Fail "the '$caption' dialog was still open $([int]$deadline.Elapsed.TotalSeconds)s after ENTER; the commit did not take"
}

function Get-DialogFileNameEdit($dialog) {
    $isEdit = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $edits = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, $isEdit)
    $seen = @()
    foreach ($e in $edits) {
        $seen += "'$($e.Current.Name)' (id '$($e.Current.AutomationId)')"
        # THE CONTROL ID, NOT THE CAPTION: the caption is localized, the id is not. 1001 is what
        # this desktop's common item dialog reports for the file-name edit; 1148 is the id the older
        # shell shape uses, kept because it costs one comparison and a machine that has it would
        # otherwise fail for a reason that has nothing to do with the port. Matched by id rather
        # than by position so the browser's column headers and the Search Box - which is also an
        # Edit, and comes back in the same enumeration - can never be typed into by accident.
        if ($e.Current.AutomationId -eq '1001' -or $e.Current.AutomationId -eq '1148') {
            return $e
        }
    }
    Fail "the '$($dialog.Current.Name)' dialog has no file-name edit with AutomationId 1001 or 1148 (it has: $($seen -join ', '))"
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
# THE LEVEL BLOCK'S `earned` STATE IS SEEDED DATA, and that is the session-row precedent rather
# than a shortcut. The only way to move this ledger through the product is to complete a whole
# graded intake, which needs an AI-drafted run this harness cannot drive; the thing being
# photographed is not the granting, it is whether the number on disk reaches a GEOMETRY on screen.
# So the file is written the way the product writes it — schemaVersion + camelCase members,
# PersistenceStore.cs:86,92-94 — and the card's own passive reader binds it.
#
# 42 and 1000.5 are chosen to be checkable rather than convenient: 42 is in upstream's second rank
# band (MainWindow/MainWindow.UiUpdates.cs:72, `< 50`) so the rank line is a DIFFERENT string from
# the `fresh` capture's, and 1000.5 into a level costing Math.Round(800 + 41 x 1700/79) = 1682 is
# 0.5949 of the bar — comfortably past the sampled band's right edge and comfortably short of the
# track's, so neither "all fill" nor "all track" could be mistaken for it.
if ($Surface -eq 'trainer-card-level' -and $State -eq 'earned') {
    New-Item -ItemType Directory -Force -Path (Split-Path $progressionFile) | Out-Null
    Set-Content -Path $progressionFile -Encoding utf8 -Value '{"schemaVersion":1,"level":42,"xp":1000.5,"highestLevelEver":42}'
    Write-Output "state drive: seeded $progressionFile with level 42, 1000.5 XP into it"
}
# THE AWARD RECORD'S TWO READABLE STATES AND ITS UNREADABLE ONE. Seeded for the reason the ledger
# above is: the only producer is a whole graded intake behind an AI-drafted run this harness cannot
# drive (IntakeHostWindow.axaml.cs:587 -> IntakeQuizRun.Record), and the thing photographed is not
# the GRANTING - the unit suite owns that - it is whether the bytes on disk reach the card.
#
# EVERY SEED IS A RECORD THE PRODUCT ITSELF WOULD HAVE WRITTEN, which is the line between seeding and
# staging. `read` is the file after ONE top-marks run in one category: top_of_the_class is awarded
# first and unconditionally (GradedRunAwards.cs:245-248) and the category joins the set
# (:253-259), so an awardedIds-empty file is NOT a state this build can be in and none is written
# here. `earned` is the file after three top-marks runs in three distinct categories, which is the
# only path that adds honor_roll (:254-257, the >= 3 clause). Categories are the port's own niches
# lower-cased (IntakeNiche.All, normalised at GradedRunAwards.NormalizeCategory), never invented
# strings. Both are written the way the store writes them - schemaVersion + camelCase members,
# PersistenceStore.cs:89,92-94.
#
# `unreadable` is TRUNCATED JSON rather than a made-up error: TrainerCard.Read's JsonException arm
# answers UnreadableInvalidJson, and truncation is what a half-finished write leaves behind.
if ($Surface -eq 'trainer-card-record') {
    New-Item -ItemType Directory -Force -Path (Split-Path $awardsFile) | Out-Null
    $awardBytes = switch ($State) {
        'read' { '{"schemaVersion":1,"perfectedCategories":["bambi"],"awardedIds":["top_of_the_class"]}' }
        'earned' { '{"schemaVersion":1,"perfectedCategories":["bambi","sissy","drone"],"awardedIds":["top_of_the_class","honor_roll"]}' }
        default { '{"schemaVersion":1,"perfectedCategories":["bambi"],"awardedIds":["top_of_' }
    }
    Set-Content -Path $awardsFile -Encoding utf8 -Value $awardBytes -NoNewline
    Write-Output "state drive: seeded $awardsFile with $awardBytes"
}
if ($Surface -eq 'session-history' -and (Test-Path $sessionLogsDir)) {
    Remove-Item $sessionLogsDir -Recurse -Force
    Write-Output "deterministic start: retained session logs cleared ($sessionLogsDir)"
}
# AND THE COMPANION'S PERSISTED RECORD, for the transcript surface only - which is the same
# argument as the session logs above, applied to the one surface whose SUBJECT is a stored
# document. The transcript shows exactly what is persisted, so a conversation left behind by a
# previous run changes what the `open` capture photographs and the pair would differ for a reason
# that has nothing to do with the window opening. Measured, not reasoned: the first run of this
# surface found six turns from an earlier provider-lab session still on disk and refused at the
# empty-state gate.
#
# Scoped to this surface deliberately. Every other capture here is indifferent to it, and a memory
# document is USER CHAT - the deterministic-start set above is unconditional, and this must not be.
if ($Surface -eq 'companion-transcript' -and (Test-Path $companionMemoryFile)) {
    # MOVED ASIDE, NEVER DELETED. This file is the developer's own companion chat, and a capture
    # command is not a reason to destroy it. The port's persistence contract already answers this
    # shape for the store (§5: quarantine MOVES the original bytes and records where they went), and
    # a harness that deletes what the product would preserve is the harness lying about the product.
    # Restoring is a rename.
    $aside = "$companionMemoryFile.capture-aside-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Move-Item $companionMemoryFile $aside -Force
    Write-Output "deterministic start: the companion's persisted record moved aside to $aside (rename it back to restore)"
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
# The pop quiz card, when one is up at capture time. It is not an Avalonia window and it holds the
# user's keyboard, so it is taken down by the key the PRODUCT reads rather than by a WM_CLOSE from
# here - see the teardown after the capture.
$script:popQuizCard = $null
# The typed mantra window, when one is up at capture time. It is closed by the key the PRODUCT
# reads (Escape, MantraWindow.OnKeyDown) rather than by a WM_CLOSE from here - see the teardown.
$script:mantraWindow = $null

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
    # RE-MEASURED 2026-08-24, when the level block landed above the card's notes and took the card
    # from 830 px to 928 px at scale 1.75. The title's line did not move in ABSOLUTE terms — it is
    # still the first line, 39 px tall, 30 px below the card's top edge — but the band is a FRACTION
    # of a taller card, so 0.050..0.082 walked off the bottom of it and this gate REFUSED BY NAME
    # rather than photographing leading and calling it ink. That refusal is the gate working; the
    # numbers below are the new measurement (title line at 30..69 of 928 = 0.0323..0.0744) with
    # roughly a third of the line left as margin at each end.
    $inkBand = @(0.042, 0.068)    # trainer-card-ink: y, and it must land ON the title's own line
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
elseif ($Surface -eq 'trainer-card-level') {
    # =============================================================================================
    # THE TRAINER CARD'S LEVEL BLOCK. The port has banked a level from three call sites since the XP
    # spine landed and nothing rendered it; this photographs the bar that ended that.
    #
    # THE CLAIM IS A GEOMETRY, NOT A COLOUR. Both states paint the same two colours in the same
    # place: a #2A2130 track with a #D05CE8 fill in front of part of it. What separates them is HOW
    # MUCH of the track the fill covers, and the sampled band is positioned so that the answer flips
    # between the two — fill in `earned`, bare track in `fresh`. A bar that ignored the ledger would
    # paint identically in both and neither check could fail.
    #
    # GATED ON UIA TEXT BEFORE ANY PIXEL, and the gate is the level's own reading rather than the
    # route: a Border has no automation peer (harness surprise #1), so the bar's rect is DERIVED,
    # and a derived rect aimed at a page that rendered a different level would photograph perfectly
    # plausible pixels of the wrong claim.
    # =============================================================================================
    $intakeDoor = Get-DoorRect $window 'intake'
    $scale = $intakeDoor.Scale
    Click-Rect $intakeDoor
    Assert-Route $window 'intake'
    Write-Output "state drive: left-click on the Graded Intake door -> route: intake (probe: $($intakeDoor.Raw))"

    # (1) THE LEVEL THE CARD ACTUALLY READ, by its own words. The page mounts on navigation but the
    # level renders on AttachedToVisualTree (IntakePage.axaml.cs), so "the route is intake" does not
    # imply "the ledger was read" — and an unrendered block photographs as a plausible rectangle.
    $expected = if ($State -eq 'earned') {
        @{ Level = 'LVL 42'; Rank = 'DUMB AIRHEAD'; Xp = '1000 / 1682 XP' }
    }
    else {
        # TrainerCardLevel.Read's missing-file arm: upstream's fresh account (AppSettings.cs:237,
        # `private int _playerLevel = 1;`) at the first band's 800 (ProgressionService.cs:301-305).
        @{ Level = 'LVL 1'; Rank = 'BASIC BIMBO'; Xp = '0 / 800 XP' }
    }
    $levelLine = (Get-Element $window 'TrainerCardLevelLine').Current.Name
    $rankLine = (Get-Element $window 'TrainerCardRankLine').Current.Name
    $xpLine = (Get-Element $window 'TrainerCardXpLine').Current.Name
    if ($levelLine -ne $expected.Level) {
        Fail ("the Trainer Card's level line reads '$levelLine', not '$($expected.Level)'. This capture is " +
    "named '$State', and the ledger that state seeds is not the ledger the card read")
    }
    if ($rankLine -ne $expected.Rank) {
        Fail ("the rank line reads '$rankLine', not '$($expected.Rank)' (MainWindow/MainWindow.UiUpdates.cs:70-76)")
    }
    if ($xpLine -ne $expected.Xp) {
        Fail ("the XP line reads '$xpLine', not '$($expected.Xp)'. The readout is the numerator of the very " +
    'fraction the bar below is about, so a capture taken over a wrong one is not evidence about the bar')
    }

    # THE UNKNOWN NOTE MUST BE ABSENT, and its absence is read as an absence. Both of these states
    # are readable ledgers; a card showing its cannot-read sentence has COLLAPSED the bar
    # (IntakePage.axaml.cs RenderLevel hides the track on a null fill), and this capture would then
    # photograph the gap where the bar used to be. Find-Element rather than Get-Element: an Avalonia
    # control with IsVisible=False has no automation peer at all.
    $unknownNote = Find-Element $window 'TrainerCardLevelUnknownNote'
    if ($null -ne $unknownNote) {
        Fail ("the card is showing its unreadable-ledger note ('$($unknownNote.Current.Name)'), so the bar is " +
    'collapsed and there is nothing at the derived rect to photograph')
    }
    Write-Output "level gate: '$levelLine', '$rankLine', '$xpLine'; no unreadable-ledger note on the page"

    # (2) THE RECT. TrainerCardXpTrack is a Border and has no peer, so it is derived from the two
    # TextBlocks that bracket it — the level line above and the XP line below — using the block's
    # own declared layout (IntakePage.axaml: StackPanel Spacing 6, track Height 8, Width 420, all
    # DIP). The derivation is then CROSS-CHECKED against the measured gap, which is what makes it an
    # observation rather than an assumption: change the spacing or the height in the markup and this
    # refuses by name instead of aiming at pixels that merely look right.
    $viewport = Get-Rect (Get-Element $window 'IntakeScroll')
    $spacing = [int][math]::Round(6 * $scale)
    $barHeight = [int][math]::Round(8 * $scale)
    $barWidth = [int][math]::Round(420 * $scale)
    $track = $null
    $notches = 0
    while ($true) {
        $levelRect = Get-Rect (Get-Element $window 'TrainerCardLevelLine')
        $xpRect = Get-Rect (Get-Element $window 'TrainerCardXpLine')
        $gap = $xpRect.Y - ($levelRect.Y + $levelRect.H)
        $declaredGap = $spacing + $barHeight + $spacing
        if ([math]::Abs($gap - $declaredGap) -gt 2) {
            Fail ("the gap between the level line and the XP line measures $gap px, and the block declares " +
    "$declaredGap (6 + 8 + 6 DIP at scale $scale). The bar's rect cannot be derived from a layout that " +
    'has changed under it, so this refuses rather than photographing whatever is at those coordinates')
        }
        if ([math]::Abs($xpRect.X - $levelRect.X) -gt 1) {
            Fail ("the level line and the XP line do not share a left edge (level x=$($levelRect.X), " +
    "xp x=$($xpRect.X)); the bar's left edge cannot be derived from them")
        }

        $track = @{ X = $levelRect.X; Y = $levelRect.Y + $levelRect.H + $spacing; W = $barWidth; H = $barHeight }
        if (Test-Inside $track $viewport) { break }

        if ($notches -ge 24) {
            Fail ("the level bar never came fully inside the page viewport after $notches wheel notches: " +
    "bar $($track.X),$($track.Y) $($track.W)x$($track.H) vs viewport $($viewport.X),$($viewport.Y) " +
    "$($viewport.W)x$($viewport.H)")
        }
        Wheel-Down $viewport
        $notches++
    }

    Write-Output ("bar rect $($track.X),$($track.Y) $($track.W)x$($track.H) @ scale $scale " +
    "(derived: level line bottom + $spacing px, 8 DIP tall, 420 DIP wide); $notches wheel notch(es) to " +
    "bring it inside the viewport $($viewport.X),$($viewport.Y) $($viewport.W)x$($viewport.H)")

    Assert-Inside $track $viewport 'the Trainer Card level bar' 'the Graded Intake viewport (IntakeScroll)'
    Assert-Inside $track $windowRect 'the Trainer Card level bar' 'the shell window'

    # (3) THE BAND checks.json SAMPLES, PROVED AGAINST THE FRACTION THE MODEL COMPUTED. Both checks
    # read the same band, and the whole pair is evidence only if that band lies strictly inside the
    # `earned` fill and strictly inside the corner-radius-free middle of the bar. 1000.5/1682 =
    # 0.5949, so the band's right edge at 0.30 has nearly a 2x margin — and THE MARGIN IS THE RULE
    # here, not the number: a band placed near the fill's edge would red a perfectly good capture
    # the first time a curve band or a seeded value moved. TrainerCardLevelPresentationTests reads
    # this file and checks.json together, so widening the band there without moving it here reddens.
    $fillBand = @(0.10, 0.30)     # x, and it must be well inside the earned fill
    $inkBandY = @(0.20, 0.80)     # y, and it must clear the 4 DIP corner radius top and bottom
    $earnedFraction = 1000.5 / 1682
    if ($fillBand[1] -ge ($earnedFraction * 0.75)) {
        Fail ("the sampled band's right edge is at $($fillBand[1]) of the bar and the earned fill reaches " +
    "$([math]::Round($earnedFraction, 4)). The band must sit well inside the fill, not near its edge — a floor " +
    'set near a boundary the product moves reds a good capture')
    }
    $radius = [int][math]::Round(4 * $scale)
    $bandTop = $track.Y + [int]($track.H * $inkBandY[0])
    $bandBottom = $track.Y + [int]($track.H * $inkBandY[1])
    $bandLeft = $track.X + [int]($track.W * $fillBand[0])
    if ($bandLeft -lt ($track.X + $radius)) {
        Fail ("the sampled band starts at $bandLeft, which is inside the bar's $radius px corner radius at " +
    "$($track.X)..$($track.X + $radius); those pixels are antialiased and neither check would be flat")
    }
    Write-Output ("regions proved: band x $($fillBand[0])..$($fillBand[1]) = $bandLeft..$($track.X + [int]($track.W * $fillBand[1])) " +
    "px, inside the earned fill reaching $($track.X + [int]($track.W * $earnedFraction)) px and clear of the " +
    "$radius px corner radius; band y $bandTop..$bandBottom inside the $($track.H) px bar")

    $capX = $track.X; $capY = $track.Y; $capW = $track.W; $capH = $track.H
}
elseif ($Surface -eq 'trainer-card-record') {
    # =============================================================================================
    # THE TRAINER CARD'S RECORD STATES. The landed `trainer-card` surface photographs the card with
    # NO record at all; this is the card with a record it could read and with one it could not, and
    # the difference between the three captures is entirely what is in graded_run_awards.json.
    #
    # THE CLAIM IS AN EXTENT, NOT A COLOUR, which is the level bar's shape applied to text. Every
    # line on this card is painted the same two ways - card ground #1B1622 behind, one of the
    # shell's inks in front - so what separates the states is WHERE THE INK STOPS:
    #
    #   * `unreadable` is the only state that has a record note at all, and its sentence runs far
    #     to the right of anything the award rows draw at that height;
    #   * `earned` says "Earned." on the Honor Roll row, which stops well before a band that the
    #     same row's "Not earned yet. 1 of 3 categories cleared at top marks." runs straight through.
    #
    # So each state declares ONE ink check and ONE ground check, and it is their CONJUNCTION that is
    # unique to it - no single band separates all three, and pretending one did would be the vacuous
    # kind of green this manifest has already paid for once. One ink plus one ground also means no
    # uniform capture can pass any of the three states, which TrainerCardRecordPresentationTests
    # asserts over this manifest rather than over the measurements.
    #
    # GATED ON THE CARD'S OWN TEXT BEFORE ANY PIXEL, and the gate is the whole tail of the card IN
    # ORDER rather than a needle: the bands below are offsets into a layout, and a layout that
    # gained or lost a line would put perfectly plausible pixels under every one of them.
    # =============================================================================================
    $intakeDoor = Get-DoorRect $window 'intake'
    $scale = $intakeDoor.Scale
    Click-Rect $intakeDoor
    Assert-Route $window 'intake'
    Write-Output "state drive: left-click on the Graded Intake door -> route: intake (probe: $($intakeDoor.Raw))"

    # (1) THE CARD RENDERED. The page mounts on navigation but the card renders on
    # AttachedToVisualTree (IntakePage.axaml.cs:77-81), so "the route is intake" does not imply the
    # card read anything - an unrendered module photographs as a plausible empty rectangle.
    $cardTitle = (Get-Element $window 'TrainerCardTitle').Current.Name
    if ($cardTitle -ne 'Trainer Card') { Fail "the Trainer Card's title reads '$cardTitle', not 'Trainer Card'" }

    # (2) THE RECORD NOTE, PRESENT OR ABSENT BY STATE, AND THE ABSENCE READ AS AN ABSENCE.
    # IntakePage.RenderTrainerCard hides that TextBlock when the record has nothing to say for
    # itself, and an Avalonia control with IsVisible=False has no automation peer at all - so
    # Find-Element returning $null IS the read state, and Get-Element would fail the run instead.
    $unreadableNote = 'This card cannot say what you have earned: graded_run_awards.json is not valid JSON.'
    $recordNote = Find-Element $window 'TrainerCardRecordNote'
    if ($State -eq 'unreadable') {
        if ($null -eq $recordNote) {
            Fail ("this capture is named 'unreadable' and the card is showing NO record note, so it read " +
    'the seeded bytes as a record it understood. There is nothing unreadable on screen to photograph')
        }
        if ($recordNote.Current.Name -ne $unreadableNote) {
            Fail ("the record note reads '$($recordNote.Current.Name)', not '$unreadableNote'. The seeded " +
    'bytes are truncated JSON and TrainerCard.Read''s JsonException arm names that case; any other ' +
    'sentence means the card failed for a different reason than this capture claims')
        }
    }
    elseif ($null -ne $recordNote) {
        Fail ("this capture is named '$State' - a record the card READ - and the card is showing a record " +
    "note: '$($recordNote.Current.Name)'. The seeded record did not bind")
    }

    # (3) THE ABSENCE NO PIXEL CAN SEE, re-checked on the state where it matters most. An EARNED
    # card is exactly where a share button would be tempting, and the card's own last line says
    # there is none; a greyed-out one would be the fake-available shape the capability contract
    # bans (IntakePage.axaml's own note, section 9 D7). Matched on BUTTONS only, because the
    # sentence making the claim contains all four of those words itself.
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

    # (4) THE CARD'S TAIL, IN ORDER, IN THIS STATE'S OWN WORDS. Not a needle list: the exact
    # sequence of lines from the portrait note down, each one the model's own constant. A card that
    # gained a line, lost one, or said "not earned" where the record says earned would put different
    # pixels under every band below, and this refuses by name instead of photographing them.
    $tocStatus = if ($State -eq 'unreadable') { 'This build cannot tell: the award record could not be read.' } else { 'Earned.' }
    $honorStatus = switch ($State) {
        'unreadable' { 'This build cannot tell: the award record could not be read.' }
        'earned' { 'Earned.' }
        default { 'Not earned yet. 1 of 3 categories cleared at top marks.' }
    }
    $expected = @('There is no portrait, wardrobe or banner art in this build either, so the card is words rather than a picture.')
    if ($State -eq 'unreadable') { $expected += $unreadableNote }
    $expected += @(
        'Top of the Class', 'Score 90% or better on a quiz', $tocStatus,
        'Honor Roll', 'Score 90% or better in 3 different categories', $honorStatus,
        'Teacher''s Pet', 'Pass 25 quizzes',
        'Not tracked here: this build counts no passed runs, so it cannot say how close this is, and it never awards it.',
        'Held Back', 'Fail three quizzes in a row',
        'Cannot be earned here: the graded intake has no fail state, so nothing in this build can lose three runs in a row.',
        'All four of these are patron-exclusive in the shipping app. This build has no entitlement authority to ask, so it cannot tell whether you are a patron: it claims no tier for you, and grants what a run earns rather than refusing everyone.',
        'This card is read from this machine and stays on it. There is no sharing, export, upload or publish path in this build.')

    # (5) THE CROP, AND IT IS ANCHORED ON THE FIRST AWARD ROW RATHER THAN ON THE CARD.
    #
    # THAT ANCHOR IS THE WHOLE DESIGN, and it was chosen after measuring the alternative. An
    # `unreadable` card carries ONE EXTRA LINE - the record note - which pushes every award row
    # 24 DIP down the card. A crop anchored anywhere ABOVE the rows therefore puts a different line
    # under every band in that one state, and the bands stop being about what the card SAYS and
    # become about how far it slid. Anchored on the "Top of the Class" line, all three captures have
    # the two ledger rows at the SAME offsets, and the only thing left that differs is where the ink
    # on each STATUS line stops - which is exactly the record reaching the screen.
    #
    # The anchor is the row's own UIA rect, not a probe and not a constant: the four rows are an
    # ItemsControl over the model's typed rows and their TextBlocks carry no AutomationId, so the
    # line is found by the name upstream authored for it (Models/Achievement.cs:663-701).
    $cropWidthDip = 460
    $cropHeightDip = 110
    $viewport = Get-Rect (Get-Element $window 'IntakeScroll')
    $crop = $null
    $portrait = $null
    $tail = @()
    $anchor = $null
    $notches = 0
    while ($true) {
        $portrait = Get-Rect (Get-Element $window 'TrainerCardPortraitNote')
        # The card's own column: every line from the portrait note down shares the content box's
        # left edge, so this selects the card's tail out of the page without a probe. Two px of
        # slack because UIA hands back a double and this rounds it.
        $tail = @(Get-TextRects $window | Where-Object {
            [math]::Abs($_.X - $portrait.X) -le 2 -and $_.Y -ge $portrait.Y })
        $anchor = $tail | Where-Object { $_.Name -eq 'Top of the Class' } | Select-Object -First 1
        if ($null -eq $anchor) { Fail "the Trainer Card has no 'Top of the Class' row to anchor the capture on" }
        $crop = @{
            X = $anchor.X; Y = $anchor.Y
            W = [int][math]::Round($cropWidthDip * $scale); H = [int][math]::Round($cropHeightDip * $scale)
        }
        if (Test-Inside $crop $viewport) { break }

        # Real input, one notch at a time, testing after each - never a fixed count. UIA reports
        # UNCLIPPED bounds with IsOffscreen=False for content scrolled out of a viewport, so a page
        # that grew a module would otherwise stop scrolling far enough while still reporting a
        # plausible rect (the trainer-card finding).
        if ($notches -ge 24) {
            Fail ("the Trainer Card's award rows never came fully inside the page viewport after $notches " +
    "wheel notches: crop $($crop.X),$($crop.Y) $($crop.W)x$($crop.H) vs viewport $($viewport.X)," +
    "$($viewport.Y) $($viewport.W)x$($viewport.H)")
        }
        Wheel-Down $viewport
        $notches++
    }
    Assert-Inside $crop $viewport 'the Trainer Card award rows' 'the Graded Intake viewport (IntakeScroll)'
    Assert-Inside $crop $windowRect 'the Trainer Card award rows' 'the shell window'

    if ($tail.Count -ne $expected.Count) {
        Fail ("the Trainer Card's tail has $($tail.Count) lines and this state expects $($expected.Count):" +
    "`n" + (($tail | ForEach-Object { "  '$($_.Name)'" }) -join "`n"))
    }
    for ($i = 0; $i -lt $expected.Count; $i++) {
        if ($tail[$i].Name -ne $expected[$i]) {
            Fail ("the Trainer Card's line $i reads '$($tail[$i].Name)' and this state expects " +
    "'$($expected[$i])'. The bands this capture is sampled at are offsets into a layout, and a card " +
    'saying something else at that offset is not evidence about what it says')
        }
    }
    Write-Output ("record region $($crop.X),$($crop.Y) $($crop.W)x$($crop.H) @ scale $scale (derived: the " +
    "'Top of the Class' line's top-left + $($cropWidthDip)x$($cropHeightDip) DIP); $notches wheel notch(es) " +
    "to bring it inside the viewport $($viewport.X),$($viewport.Y) $($viewport.W)x$($viewport.H)")
    Write-Output "card gate: $($tail.Count) lines in order; Top of the Class '$tocStatus'; Honor Roll '$honorStatus'"
    foreach ($line in $tail) {
        Write-Output ("  line y {0:F4}..{1:F4} x {2:F4}..{3:F4}  '{4}'" -f `
            (($line.Y - $crop.Y) / $crop.H), (($line.Y + $line.H - $crop.Y) / $crop.H), `
            (($line.X - $crop.X) / $crop.W), (($line.X + $line.W - $crop.X) / $crop.W), $line.Name)
    }

    # (6) THE FOUR BANDS checks.json SAMPLES, PROVED AGAINST THIS STATE'S MEASURED LAYOUT BEFORE
    # ANY PIXEL IS READ. A fraction of a capture is only evidence if the thing it names is really at
    # that fraction, and every one of these depends on a layout this script has just measured.
    #
    # THE MARGIN IS THE RULE, NOT THE NUMBER - the pop quiz lesson, and the level bar's. Each status
    # line must be either a quarter CLEAR of the sampled column or a quarter PAST it; a status that
    # ended anywhere near the band's edge would red a perfectly good capture the first time a
    # sentence, a font or a window size moved, and this refuses at capture time instead.
    $statusBandX = @(0.15, 0.45)        # x, shared by both status checks: the column the record decides
    $tocStatusBandY = @(0.34, 0.41)     # y, and it must land ON the Top of the Class status line
    $honorStatusBandY = @(0.84, 0.92)   # y, and it must land ON the Honor Roll status line
    $rowNameBandX = @(0.02, 0.12)       # x, inside the row NAME's glyphs, which no record can change
    $rowNameBandY = @(0.045, 0.115)     # y, and it must land ON the Top of the Class name line
    $clearColumnBandX = @(0.75, 0.98)   # x, right of every line the card draws in this region
    $clearColumnBandY = @(0.05, 0.95)   # y, most of the crop's height

    function Fraction($line, [string]$part) {
        switch ($part) {
            'top' { return ($line.Y - $crop.Y) / $crop.H }
            'bottom' { return ($line.Y + $line.H - $crop.Y) / $crop.H }
            default { return ($line.X + $line.W - $crop.X) / $crop.W }
        }
    }

    # The three lines the bands name, taken out of the tail by position rather than by text: the
    # first award row is the anchor, so its three lines are the next three, and the Honor Roll
    # status is three further on.
    $anchorIndex = [array]::IndexOf(@($tail | ForEach-Object { $_.Name }), 'Top of the Class')
    $tocNameLine = $tail[$anchorIndex]
    $tocStatusLine = $tail[$anchorIndex + 2]
    $honorStatusLine = $tail[$anchorIndex + 5]
    if ($tocStatusLine.Name -ne $tocStatus -or $honorStatusLine.Name -ne $honorStatus) {
        Fail ("the two status lines are not where the card's row order puts them: read " +
    "'$($tocStatusLine.Name)' and '$($honorStatusLine.Name)'")
    }

    foreach ($band in @(
            @{ What = 'Top of the Class status'; Band = $tocStatusBandY; Line = $tocStatusLine },
            @{ What = 'Honor Roll status'; Band = $honorStatusBandY; Line = $honorStatusLine },
            @{ What = 'Top of the Class name'; Band = $rowNameBandY; Line = $tocNameLine })) {
        $top = Fraction $band.Line 'top'
        $bottom = Fraction $band.Line 'bottom'
        if ($band.Band[0] -lt $top -or $band.Band[1] -gt $bottom) {
            Fail ("the $($band.What) band y $($band.Band[0])..$($band.Band[1]) of this capture is not inside " +
    "that line, which measures $([math]::Round($top, 4))..$([math]::Round($bottom, 4)) of the crop. Those " +
    'pixels are no longer the line checks.json names, so this capture would not be evidence about it')
        }
    }

    # EVERY STATUS LINE IS EITHER CLEAR OF THE SAMPLED COLUMN OR STRAIGHT THROUGH IT. This is the
    # whole inversion, asserted as geometry before it is asserted as pixels: a short status ("Earned.")
    # must stop well before the band, and a long one must run well past it.
    $widestStatus = 0.0
    foreach ($line in @($tocStatusLine, $honorStatusLine)) {
        $right = Fraction $line 'right'
        if ($right -gt $widestStatus) { $widestStatus = $right }
        if ($right -lt $statusBandX[0]) {
            if ($right -gt ($statusBandX[0] * 0.8)) {
                Fail ("'$($line.Name)' ends at $([math]::Round($right, 4)) of the crop and the sampled column " +
    "starts at $($statusBandX[0]). It is clear of the band but only just, and a floor set near a boundary " +
    'the product moves reds a good capture')
            }
        }
        elseif ($right -lt ($statusBandX[1] / 0.8)) {
            Fail ("'$($line.Name)' ends at $([math]::Round($right, 4)) of the crop and the sampled column ends " +
    "at $($statusBandX[1]). It runs through the band but only just, and this capture would red the first " +
    'time the sentence, the font or the window size moved')
        }
    }
    if ($clearColumnBandX[0] -lt ($widestStatus + 0.05)) {
        Fail ("the clear column starts at $($clearColumnBandX[0]) of the crop and the widest status line on " +
    "this card reaches $([math]::Round($widestStatus, 4)). The band checks.json samples for empty card " +
    'ground would contain the card''s own text')
    }
    Write-Output ("regions proved: status column x $($statusBandX[0])..$($statusBandX[1]) against " +
    "'$tocStatus' ending at $([math]::Round((Fraction $tocStatusLine 'right'), 4)) and '$honorStatus' ending " +
    "at $([math]::Round((Fraction $honorStatusLine 'right'), 4)); clear column from $($clearColumnBandX[0]), " +
    "right of the widest at $([math]::Round($widestStatus, 4))")

    $capX = $crop.X; $capY = $crop.Y; $capW = $crop.W; $capH = $crop.H
}
elseif ($Surface -eq 'toast') {
    # =============================================================================================
    # THE IN-APP TOAST, AND THE FIRST FILE DIALOG THIS PORT HAS EVER OPENED.
    #
    # Two states, ONE control, ONE geometry: `saved` is what the app says after a phrase export
    # really wrote a file, `refused` is what it says after an import was handed something that is
    # not a backup. The captured band is derived identically in both, and the ONLY thing that
    # differs in it is the accent - which is chosen by the TYPED OUTCOME
    # (Views/Pages/PhraseBackupNotices.cs), so a check that passed on both would be saying the type
    # of a message is decoration.
    #
    # Both states drive a REAL Windows common item dialog to completion. That is not incidental to
    # this capture: the board row that admitted the file-picker seam recorded six lines nobody had
    # ever executed - the capability probe against a real provider, both picker calls, cancellation
    # mapping and IStorageItem.Dispose - because Avalonia marks IStorageProvider
    # [NotClientImplementable] and no test can reach them. Four of the six run here, on this
    # desktop, for the first time.
    # =============================================================================================
    $systemDoor = Get-DoorRect $window 'system'
    $scale = $systemDoor.Scale
    Click-Rect $systemDoor
    Assert-Route $window 'system'
    Write-Output "state drive: left-click on the System door -> route: system (probe: $($systemDoor.Raw))"

    # (1) THE MODULE'S OWN TEXT, before anything is pressed. The page mounts on navigation, but a
    # module that failed to render photographs as a perfectly plausible rectangle.
    $moduleTitle = (Get-Element $window 'PhraseBackupTitle').Current.Name
    if ($moduleTitle -ne 'Phrase backup') { Fail "the phrase-backup module's title reads '$moduleTitle'" }
    $blurb = (Get-Element $window 'PhraseBackupBlurb').Current.Name
    if ($blurb -notlike '*Back them up before an update or when moving to a new PC.*') {
        Fail "the phrase-backup module does not say why it exists: '$blurb'"
    }
    if ($null -ne (Find-Element $window 'ToastMessage')) {
        Fail 'a toast is already on screen before anything was pressed; this capture would photograph the wrong message'
    }
    Write-Output "module gate: '$moduleTitle' present, blurb reads '$blurb', no toast on screen yet"

    # (2) A CLEAN PLACE TO PUT A FILE. Never a remembered path and never the app's choice: the
    # dialog is what asks, and this is what a user would type into it.
    $verifyDir = Join-Path ([IO.Path]::GetTempPath()) 'ccp-verify-phrases'
    if (Test-Path $verifyDir) { Remove-Item $verifyDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $verifyDir | Out-Null

    if ($State -eq 'saved') {
        $target = Join-Path $verifyDir 'headed-export.ccpphrases.json'
        Click-Rect (Get-Rect (Get-Element $window 'ExportPhrasesButton'))
        $dialog = Wait-NamedWindow $script:proc.Id 'Export Phrases' 20
        Write-Output "dialog: 'Export Phrases' opened (upstream's own title, MainWindow/MainWindow.PresetIO.cs:70)"
        Submit-DialogPath $dialog $target 'Export Phrases' $script:proc.Id

        # THE BYTES ON DISK ARE THE PROOF THE DIALOG REALLY RETURNED A FILE. A toast alone could be
        # produced by a seam that never wrote anything.
        $writeDeadline = [Diagnostics.Stopwatch]::StartNew()
        while ($writeDeadline.Elapsed.TotalSeconds -lt 20 -and -not (Test-Path $target)) {
            Start-Sleep -Milliseconds 200
        }
        if (-not (Test-Path $target)) { Fail "the export dialog closed but nothing was written to the chosen destination" }
        $written = Get-Content $target -Raw
        if ($written -notlike '*ccp-phrases/v1*') {
            Fail "the exported file is not a phrase backup envelope (Services/PhraseBackupService.cs:72-78)"
        }
        Write-Output "wrote $((Get-Item $target).Length) bytes through the real picker, and they carry upstream's schema"
        $expected = '^Saved \d+ phrases?\.$'
    }
    else {
        # A REAL FILE THAT IS REALLY NOT A BACKUP. Upstream refuses it before ever asking the user
        # to confirm a replacement (MainWindow/MainWindow.PresetIO.cs:107-118) and so does this.
        $target = Join-Path $verifyDir 'not-a-backup.ccpphrases.json'
        Set-Content -Path $target -Value 'this file is not JSON at all' -NoNewline
        Click-Rect (Get-Rect (Get-Element $window 'ImportPhrasesButton'))
        $dialog = Wait-NamedWindow $script:proc.Id 'Import Phrases' 20
        Write-Output "dialog: 'Import Phrases' opened (MainWindow/MainWindow.PresetIO.cs:101)"
        Submit-DialogPath $dialog $target 'Import Phrases' $script:proc.Id
        $expected = '^That file isn''t a phrase backup: the bytes are not JSON\.$'
    }

    # (3) THE TOAST'S OWN TEXT, gated BEFORE any pixel. There must be exactly one - two would make
    # the derived rect belong to whichever one UIA returned first.
    $toastDeadline = [Diagnostics.Stopwatch]::StartNew()
    $message = $null
    while ($toastDeadline.Elapsed.TotalSeconds -lt 20) {
        $message = Find-Element $window 'ToastMessage'
        if ($null -ne $message) { break }
        Start-Sleep -Milliseconds 200
    }
    if ($null -eq $message) { Fail "no toast appeared within $([int]$toastDeadline.Elapsed.TotalSeconds)s of the dialog closing" }

    $said = $message.Current.Name
    if ($said -notmatch $expected) { Fail "the toast reads '$said', which does not match /$expected/" }
    $allToasts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ToastMessage')))
    if ($allToasts.Count -ne 1) { Fail "$($allToasts.Count) toasts are on screen; this capture needs exactly one" }
    $dismiss = Get-Element $window 'ToastDismiss'   # every toast carries its own way out (NotificationService.cs:200-212)
    Write-Output "toast gate: one toast, dismissable, reading '$said'"

    # THE SEAM'S RULE, READ OFF THE REAL SCREEN. Upstream prints the full path in the same sentence
    # (MainWindow/MainWindow.PresetIO.cs:82); the port may not, and this is the only place that
    # claim is checked against what a user is actually looking at rather than against a return value.
    if ($said -match '[\\/]' -or $said -match 'ccpphrases') {
        Fail "the toast names a path, a separator or the file's extension: '$said'"
    }
    Write-Output 'seam gate: the sentence on screen carries no path, no separator and no file name'

    # (4) THE RECT. A Border has no automation peer (harness surprise #1), so the toast's own edges
    # are derived from the two children that DO have one - its message and its dismiss button - plus
    # the padding and border thickness ToastHost.axaml declares (BorderThickness 4,1,1,1 and
    # Padding 14,10,10,10). Cross-checked against the 360 DIP MaxWidth upstream sets
    # (Services/Notifications/NotificationService.cs:136), so a layout change fails here rather than
    # aiming the capture at a rectangle that merely looks right.
    $msgRect = Get-Rect $message
    $dismissRect = Get-Rect $dismiss
    $leftInset = [int][math]::Round(18 * $scale)    # 4 accent + 14 padding
    $rightInset = [int][math]::Round(11 * $scale)   # 10 padding + 1 border
    $toast = @{
        X = $msgRect.X - $leftInset
        Y = $msgRect.Y - [int][math]::Round(11 * $scale)
        W = ($dismissRect.X + $dismissRect.W + $rightInset) - ($msgRect.X - $leftInset)
        H = $msgRect.H + [int][math]::Round(22 * $scale)
    }
    $maxWidth = [int][math]::Round(361 * $scale)
    if ($toast.W -le 0 -or $toast.W -gt $maxWidth) {
        Fail ("the derived toast is $($toast.W) px wide, which is not between 0 and the 360 DIP MaxWidth " +
    "($maxWidth px at scale $scale); the message at $($msgRect.X) and the dismiss button at " +
    "$($dismissRect.X)+$($dismissRect.W) do not bound a toast")
    }
    Assert-Inside $toast $windowRect 'the toast' 'the shell window'
    Write-Output ("toast rect $($toast.X),$($toast.Y) $($toast.W)x$($toast.H) @ scale $scale (derived: message " +
    "$($msgRect.X),$($msgRect.Y) $($msgRect.W)x$($msgRect.H) + dismiss $($dismissRect.X)x$($dismissRect.W))")

    # (5) THE BAND. 18 DIP wide from the toast's own left edge - the 4 DIP accent and the 14 DIP of
    # padding behind the text - taken across the MIDDLE HALF of the message's line, which is well
    # clear of the 8 DIP corner radius at both ends. Deliberately narrower than the message's own
    # left edge so no glyph can ever enter it.
    $capX = $toast.X
    $capW = [int][math]::Round(18 * $scale)
    $capY = $msgRect.Y + [int][math]::Round($msgRect.H / 4)
    $capH = [math]::Max(1, [int][math]::Round($msgRect.H / 2))

    # THE TWO REGIONS checks.json SAMPLES, PROVED AGAINST THE MEASURED LAYOUT. A fraction is only
    # evidence if the thing it names is really at that fraction. Widen either past what is proved
    # here and ToastPresentationTests reddens, because it reads both files and compares them.
    #
    # NEITHER REGION TOUCHES THE OUTERMOST COLUMN, AND THAT IS MEASURED RATHER THAN CAUTIOUS. This
    # left edge is the DIFFERENCE of two independently layout-rounded rects, so it carries +/-1 px:
    # the first run of this surface derived 1724 for an accent bar that really began at 1725, and
    # column 0 came back as the window ground #141018 - 70/84 on a check that had every other pixel
    # right. So both regions are inset by a column and the arithmetic below proves they survive the
    # error in EITHER direction.
    $accentPx = [int][math]::Round(4 * $scale)
    $accentLeft = [int][math]::Round(0.09 * $capW)         # toast-*-accent: x 0.09 .. 0.18
    $accentRight = [int][math]::Round(0.18 * $capW)
    $plateLeft = [int][math]::Round(0.40 * $capW)          # toast-*-plate:  x 0.40 .. 0.90
    $plateRight = [int][math]::Round(0.90 * $capW)
    if ($accentLeft -lt 1) {
        Fail "the accent band starts at column $accentLeft, which is the +/-1 px edge this derivation cannot place"
    }
    if ($accentRight -gt $accentPx) {
        Fail ("the accent band x 0.09..0.18 of this capture is $accentLeft..$accentRight px, but the accent bar is " +
    "only $accentPx px wide at scale $scale; those pixels are no longer all accent")
    }
    if ($plateLeft -le $accentPx) {
        Fail "the plate band starts at $plateLeft px, which a ${accentPx}px accent bar can still reach"
    }
    if ($plateRight -ge $capW) {
        Fail "the plate band ends at $plateRight px, which is outside this ${capW}px capture"
    }
    if (($capX + $capW) -gt ($msgRect.X + 1)) {
        Fail ("the band ends at $($capX + $capW) and the message's own line starts at $($msgRect.X); the region " +
    'checks.json samples for flat plate would contain the toast''s own text')
    }
    if ($capY -lt $msgRect.Y -or ($capY + $capH) -gt ($msgRect.Y + $msgRect.H)) {
        Fail ("the band y $capY..$($capY + $capH) is not inside the message's line at " +
    "$($msgRect.Y)..$($msgRect.Y + $msgRect.H); it would reach the toast's rounded corners")
    }
    Assert-Inside @{ X = $capX; Y = $capY; W = $capW; H = $capH } $toast 'the toast sample band' 'the toast'
    Write-Output ("regions proved: accent $accentLeft..$accentRight px inside the ${accentPx}px accent bar; plate " +
    "$plateLeft..$plateRight px, clear of it and inside the ${capW}px capture; band right edge " +
    "$($capX + $capW) at the message's line at $($msgRect.X)")
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
elseif ($Surface -eq 'popquiz-card') {
    # =============================================================================================
    # THE POP QUIZ CARD, AND IT IS THE FIRST SURFACE IN THIS HARNESS THAT IS NOT AN AVALONIA
    # CONTROL. The card is a raw Win32 popup this process creates, paints with GDI and gives the
    # user's keyboard to (Input/Win32InputPresence.cs), so UIA publishes the WINDOW and NOT ONE WORD
    # of what is written on it. That shapes the whole gate below: everything readable is read off the
    # SHELL's own panel - which is Avalonia and does have peers - and the card contributes its
    # existence, its rectangle, and its answer to a real key press.
    #
    # WHY THIS RUN TAKES A MINUTE AND CANNOT BE SHORTENED. There is no Test button in this build
    # (upstream's BtnTestPopQuiz -> PopQuizService.TestPopQuiz shows one immediately,
    # MainWindow/MainWindow.Lab.cs:646-649), so the only way to a card is the schedule. At the dial's
    # ceiling of 100/hour that is 60/100 minutes +/-30% - 25.2s to 46.8s (Effects/PopQuizSchedule.cs)
    # - and the wait below is bounded at 90s so a card that never comes is a finding, not a hang.
    #
    # NOTHING HERE IS SEEDED. Both dials are driven through the real controls and read back through
    # UIA, because what this packet built is exactly those two controls being wired to the module at
    # all - a seeded document would confirm the module and skip the claim.
    # =============================================================================================
    $scale = (Get-DoorRect $window 'studio').Scale
    $viewport = Get-Rect (Get-Element $window 'RackScroll')

    # (1) THE ROW. It is the last row of GAMES & CARDS and is below the scroll fold at this window
    # size, so it is wheeled in one notch at a time and tested after each - never a fixed count.
    $rowInfo = Scroll-RowIntoView $window $viewport 'RowPopQuiz'
    Click-Rect $rowInfo.Rect
    if (-not (Get-Selected (Get-Element $window 'RowPopQuiz'))) {
        Fail 'the left-click did not open the Pop Quiz rack row (state drive failed)'
    }
    Write-Output "state drive: left-click on the Pop Quiz rack row -> IsSelected=True ($($rowInfo.Notches) wheel notch(es))"

    # (2) THE PANEL'S OWN TEXT, BEFORE ANYTHING IS DRIVEN. A panel that failed to render is a
    # perfectly plausible rectangle; a panel that rendered somebody else's module is worse.
    $live = (Get-Element $window 'PopQuizLiveState').Current.Name
    if ($live -ne 'Switched off. No question will come up, session or no session.') {
        Fail "the Pop Quiz panel does not read as switched off on a cold start: '$live'"
    }
    $warning = (Get-Element $window 'PopQuizInterruptionNotice').Current.Name
    if ($warning -notlike '*takes the keyboard*' -or $warning -notlike '*EVERY ANSWER IS CORRECT*') {
        Fail "the panel does not warn what this module does before it is switched on: '$warning'"
    }
    $noTest = (Get-Element $window 'PopQuizTestNotice').Current.Name
    if ($noTest -notlike '*no Test button*') { Fail "the panel does not admit the missing Test button: '$noTest'" }
    Write-Output "panel gate: '$live' / the warning names the keyboard and that every answer is correct"

    # (3) SWITCH IT ON, through the real box, and read the OS's own toggle state back.
    Click-Rect (Get-Rect (Get-Element $window 'PopQuizEnableToggle'))
    $toggle = Get-Toggle (Get-Element $window 'PopQuizEnableToggle')
    if ("$toggle" -ne 'On') { Fail "the enable box did not turn on: ToggleState=$toggle" }
    $live = (Get-Element $window 'PopQuizLiveState').Current.Name
    if ($live -ne 'Armed. No question comes up until the session starts.') {
        Fail "switching the module on did not arm it: '$live'"
    }
    Write-Output "state drive: enable box ToggleState=On -> '$live'"

    # (4) DRIVE THE RATE TO ITS CEILING, through the real slider, by clicking its far right end.
    # Read back the panel's own value label rather than the slider's internal value: the label is
    # what a user sees, and it is upstream's own string (MainWindow/MainWindow.Lab.cs:643).
    $sliderRect = Get-Rect (Get-Element $window 'PopQuizFrequencySlider')
    Click-Rect @{ X = $sliderRect.X + $sliderRect.W - 4; Y = $sliderRect.Y; W = 3; H = $sliderRect.H }
    $rate = (Get-Element $window 'PopQuizFrequencyValue').Current.Name
    if ($rate -ne '100/session hr') {
        Fail ("the frequency slider did not reach its ceiling: the panel reads '$rate'. This capture needs the " +
    'ceiling because the wait below is sized from it')
    }
    Write-Output "state drive: frequency slider clicked at its right end -> panel reads '$rate'"

    # (5) START THE SESSION, through the shell's one START button.
    Click-Rect (Get-Rect (Get-Element $window 'SessionStartButton'))
    $startCaption = (Get-Element $window 'SessionStartButton').Current.Name
    Write-Output "state drive: START pressed -> the button reads '$startCaption'"

    # (6) WAIT FOR A REAL QUESTION, on the module's real clock. Bounded, and it FAILS BY NAME with
    # whatever the panel last said, so a run that never got a card reports the module's own account
    # of why rather than a missing variable three lines later.
    $card = $null
    $cardDeadline = [Diagnostics.Stopwatch]::StartNew()
    while ($cardDeadline.Elapsed.TotalSeconds -lt 90) {
        $card = Get-NamedWindow $script:proc.Id 'CCP input prompt'
        if ($null -ne $card) { break }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $card) {
        $why = (Get-Element $window 'PopQuizCapabilityState').Current.Name
        $state = (Get-Element $window 'PopQuizLiveState').Current.Name
        Fail ("no pop quiz card appeared within $([int]$cardDeadline.Elapsed.TotalSeconds)s at 100 questions/hour " +
    "(the schedule's own ceiling is 46.8s). The panel says: '$state' / '$why'")
    }
    Write-Output "card up after $([math]::Round($cardDeadline.Elapsed.TotalSeconds, 1))s (schedule: 25.2s..46.8s at 100/hour)"

    # AND ITS CONTROL TYPE IS CHECKED, not only its name - the harness's own rule that a match on an
    # identifier alone is dangerous, applied to a title.
    #
    # THE TYPE IS 'Pane', AND THAT IS A MEASURED FINDING RATHER THAN A GUESS. The first draft of this
    # line demanded ControlType.Window and refused a card that was really on the screen: UIA
    # classifies a captionless WS_POPUP with WS_EX_TOOLWINDOW as a PANE, not a Window
    # (Input/Win32InputPresence.cs's CreateWindowEx - WsPopup, WsExToolwindow, no owner). It is still
    # a top-level element and Get-NamedWindow finds it among the desktop root's children; what it is
    # NOT is a Window peer, and a harness that assumed otherwise would report "no card" for a card
    # every user could see.
    if ("$($card.Current.ControlType.ProgrammaticName)" -ne 'ControlType.Pane') {
        Fail ("the 'CCP input prompt' element is a $($card.Current.ControlType.ProgrammaticName); the card is a " +
    'captionless popup and UIA publishes it as a Pane')
    }
    Write-Output "card type: $($card.Current.ControlType.ProgrammaticName) (a captionless WS_POPUP is a UIA Pane, not a Window)"

    # (7) THE UIA TEXT GATE, READ BEFORE ANY PIXEL. The card publishes no text of its own, so these
    # are the SHELL's words about it - and the second is the load-bearing half: this capability
    # returns Available only when the operating system confirmed the foreground, the keyboard focus
    # AND a differential ink read-back of the card's own device context
    # (Input/Win32InputPresence.cs; InputCaptureObservation.BackgroundHeld / InkedPixels). A blank
    # topmost window comes back Degraded, and the module takes such a card straight back down.
    $asking = (Get-Element $window 'PopQuizLiveState').Current.Name
    if ($asking -notlike 'Asking you now:*') {
        Fail "a card is on screen but the panel does not say a question is up: '$asking'"
    }
    $capability = (Get-Element $window 'PopQuizCapabilityState').Current.Name
    if ($capability -notlike 'The operating system gave the card the keyboard:*') {
        Fail "the input capability did not report an available card: '$capability'"
    }
    Write-Output "card gate: '$asking'"
    Write-Output "capability gate: '$capability'"

    # (8) THE RECT, from the card's own window, cross-checked against the fraction of the primary
    # display the module asks for (PopQuizEffect.CardWidthFraction 0.45, CardHeightFraction 0.5,
    # centred through Effects/PrimaryDisplayPlacement). A card that had drifted to another size would
    # still photograph plausibly, so the arithmetic is checked rather than trusted.
    $cardRect = Get-Rect $card
    $screenW = [int][VerifyNative]::GetSystemMetrics(0)
    $screenH = [int][VerifyNative]::GetSystemMetrics(1)
    $wantW = [int]($screenW * 0.45)
    $wantH = [int]($screenH * 0.5)
    if ([math]::Abs($cardRect.W - $wantW) -gt 2 -or [math]::Abs($cardRect.H - $wantH) -gt 2) {
        Fail ("the card is $($cardRect.W)x$($cardRect.H) but 0.45 x 0.5 of the primary display " +
    "(${screenW}x${screenH}) is ${wantW}x${wantH}")
    }
    Write-Output ("card rect $($cardRect.X),$($cardRect.Y) $($cardRect.W)x$($cardRect.H) = 0.45 x 0.5 of the " +
    "primary display ${screenW}x${screenH} @ scale $scale")

    $script:popQuizCard = $card
    $capX = $cardRect.X; $capY = $cardRect.Y; $capW = $cardRect.W; $capH = $cardRect.H
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
elseif ($Surface -eq 'audio-dial') {
    # =============================================================================================
    # THE AUDIO ROW'S MASTER VOLUME, PHOTOGRAPHED TWICE, AND THE CLAIM IS THAT THE TWO PICTURES ARE
    # THE SAME.
    #
    # This is the studio-dial pair run in reverse. There, a session is running and the dial the
    # session OWNS must go grey; here, a session is running and this dial must NOT — because
    # audio.json is not one of the eleven documents a run borrows (ScriptedSessionDials'
    # constructor) and upstream classes volumes as COMFORT rather than dosage, naming audio volume
    # in the never-lock list outright (MainWindow/MainWindow.SessionFeatureLock.cs:39-42,
    # Features/SessionLock.cs:21-38). Over-locking is a regression in its own right (:36-38), and
    # this is the only evidence class that can catch one: a greyed dial and a live dial are the same
    # control at the same value, and the difference is entirely in composited pixels.
    #
    # SO THE INVERSION IS NOT AGAINST THE OTHER STATE OF THIS SURFACE - both states are the same
    # livery on purpose - IT IS AGAINST studio-dial-locked-track, which reads #333333 in the same
    # band of the same kind of control in a capture taken under the same running session. Each pair
    # must fail the other's check.
    #
    # THE UIA GATE IS WHAT MAKES THE `running` CAPTURE MEAN ANYTHING, and it is read before any
    # pixel: no check can tell a session that is running from one that never started, and a dial
    # that is live because the click silently failed photographs identically to one that is live
    # because the lock let it be.
    # =============================================================================================
    $scale = (Get-DoorRect $window 'studio').Scale
    $viewport = Get-Rect (Get-Element $window 'RackScroll')

    if ($State -eq 'running') {
        # The same four gestures studio-dial and session-start use. There is no flag for a running
        # session and deliberately so.
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
        $stillIdle = (Get-Element $window 'ScriptedSessionStartButton').Current.Name
        if ($stillIdle -ne 'Start Session') { Fail "a session started before the confirmation was answered: '$stillIdle'" }
        Click-Rect (Get-Rect (Get-Element $window 'ScriptedSessionConfirmButton'))
        $caption = (Get-Element $window 'ScriptedSessionStartButton').Current.Name
        if ($caption -notlike 'STOP SESSION (*') { Fail "the session did not start: the button reads '$caption'" }
        Write-Output "start gate: button '$caption' ($($sessions.Notches) wheel notch(es))"
    }

    # THE PANEL, opened the same way in both states so the two captures differ by the session and
    # by nothing else. It is the last row of IMMERSION and below the fold at this window size.
    $audioRow = Scroll-RowIntoView $window $viewport 'RowAudio'
    Click-Rect $audioRow.Rect
    if (-not (Get-Selected (Get-Element $window 'RowAudio'))) {
        Fail 'the left-click did not open the Audio rack row (state drive failed)'
    }
    Write-Output "state drive: left-click on the Audio rack row -> IsSelected=True ($($audioRow.Notches) wheel notch(es))"

    $master = Get-Element $window 'AudioMasterSlider'
    $picker = Get-Element $window 'AudioDevicePicker'
    $test = Get-Element $window 'AudioTestButton'
    $banner = Find-Element $window 'SessionLockReason'
    $reading = (Get-Element $window 'AudioMasterValue').Current.Name

    # THE VALUE, IN BOTH STATES. What makes this pair evidence rather than two photographs is that
    # the thumb is in the same place in each, so a check can only be reading the livery. 32% is
    # upstream's own fresh-install master volume (Models/AppSettings.cs:1127) and this run cleared
    # the settings file, so anything else means a document leaked in from a previous run and the
    # two captures would differ for a reason that has nothing to do with the lock.
    if ($reading -ne '32%') {
        Fail "the master volume reads '$reading' rather than the fresh-install 32%; a leaked audio.json would move the thumb"
    }

    # THE CLAIM. All three controls stay the user's while the session runs.
    foreach ($pair in @(@('master volume', $master), @('output picker', $picker), @('Test audio', $test))) {
        if (-not $pair[1].Current.IsEnabled) {
            Fail ("the audio $($pair[0]) control is disabled$(if ($State -eq 'running') { ' while a session runs' } else { ' with nothing running' })" +
    '; a scripted session does not borrow audio.json and upstream names audio volume in its own never-lock list')
        }
    }

    if ($State -eq 'running') {
        # AND THE LOCK REALLY IS UP. Without this, a capture in which the session silently failed to
        # start would photograph a live dial and pass - which is the whole failure mode this state
        # exists to exclude. The banner is page-level (StudioPage.axaml:496-502), so it is on screen
        # with the AUDIO panel open even though the dials it explains are in panels that are not.
        if ($null -eq $banner) { Fail 'no session lock banner is on screen, so nothing here shows the lock was ever active' }
        $reason = $banner.Current.Name
        if ($reason -ne 'Morning Drift is running this. Its features and intensity are locked until the session ends.') {
            Fail "the banner does not name the running session: '$reason'"
        }
        Write-Output "lock gate: banner reads '$reason', and master/picker/Test are all IsEnabled=True at '$reading'"
    }
    else {
        if ($null -ne $banner) { Fail "a session lock banner is on screen with nothing running: '$($banner.Current.Name)'" }
        Write-Output "live gate: no banner, and master/picker/Test are all IsEnabled=True at '$reading'"
    }

    # AND THE DEVICE, WHICH IS THE OTHER HALF OF THIS ROW'S CONTRACT. Opening the panel lists
    # endpoints and opens NOTHING, so the panel's own device line still reads "nothing has been
    # asked" in a real process that has been driven to this point by real clicks. A pixel cannot
    # read text, so this is a UIA needle rather than a manifest check - the capability-line
    # precedent in verification-harness.md.
    $deviceLine = (Get-Element $window 'AudioDeviceState').Current.Name
    if ($deviceLine -notlike 'Nothing has been asked of the operating system yet*') {
        Fail "a device was brought up merely by opening the audio panel: '$deviceLine'"
    }
    Write-Output "device gate: '$deviceLine'"

    $dialRect = Get-Rect $master
    Assert-Inside $dialRect $windowRect 'the audio master volume slider' 'the shell window'

    # THE BAND checks.json SAMPLES, PROVED AGAINST THE MEASURED CONTROL - the studio-dial pair's own
    # arithmetic, because it is the same Fluent slider template: y 0.40..0.60 is the centre line
    # where the track is drawn, and x 0.02..0.10 is inside the FILLED part of it for any value above
    # the minimum, which 32 of 0..100 is.
    if ($dialRect.H -lt [int][math]::Round(12 * $scale)) {
        Fail "the master slider is only $($dialRect.H) px tall at scale $scale; its centre band would not be the track"
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
elseif ($Surface -eq 'mantra-window') {
    # =============================================================================================
    # THE TYPED MANTRA GAME, THROUGH A DOOR THAT EXISTS AGAIN.
    #
    # Upstream's Mantras card was the only caller of its typed mantra window and came off the Play
    # page in the 2026-08-12 relayout, whose own commit records "MantraWindow entry point orphaned -
    # re-home pending owner call" (a9859e7b6; MainWindow/MainWindow.PlayTab.cs:262). Nothing had
    # ever driven this window on a real desktop, upstream or here, because for the whole of that
    # time there was nothing to press.
    #
    # THREE THINGS THIS RUN ESTABLISHES AND A HEADLESS FRAME CANNOT.
    #   1. A MAXIMIZED, CHROMELESS WINDOW AGAINST A REAL WINDOW MANAGER. The window declares
    #      WindowState=Maximized, WindowDecorations=None and Topmost (MantraWindow.axaml:5-7); this
    #      reads its rect back off the desktop and requires it to be the whole primary display.
    #   2. IT TAKES THE SCREEN OFF THE SHELL. The shell has been raised HWND_TOPMOST by this script
    #      before anything was pressed, and the band captured below lies INSIDE the shell's own
    #      rect - so the pixels checked are provably at coordinates the shell was occupying.
    #      NOTE THE LIMIT: the game's window is OWNED by the shell, so this proves occlusion and
    #      does NOT isolate Topmost from owner ordering. A foreign topmost window would be needed
    #      for that, and this harness has none.
    #   3. IT ACQUIRES THE KEYBOARD WITHOUT BEING CLICKED. Nothing clicks into the game. The
    #      characters below are pressed into whatever holds the focus, and the game's own echo line
    #      is read back through UIA before any pixel - so a window that opened without taking focus
    #      is a named failure rather than a photograph of an empty box.
    #
    # THE MANTRA IS DRAWN AT RANDOM from the built-in pool of five (MantraSession.DefaultPool,
    # upstream's Models/AppSettings.cs:6318-6322), so this run READS the line before typing it back
    # rather than assuming one - and it stops TWO characters short of the end, which is what keeps
    # the run deterministic: a completed repetition would bank XP, move the streak, warm every
    # colour on the ramp (MantraIntensity) and draw a different mantra underneath the capture.
    # =============================================================================================
    $playDoor = Get-DoorRect $window 'play'
    $scale = $playDoor.Scale
    Click-Rect $playDoor
    Assert-Route $window 'play'
    Write-Output "state drive: left-click on the Play door -> route: play (probe: $($playDoor.Raw))"

    # (1) THE CARD IS THE THIRD ON THE WALL and its button sits below the shell's fold, so it is
    # wheeled into the page's own viewport one notch at a time, testing after each - the rack's
    # rule, never a fixed count.
    $viewport = Get-Rect (Get-Element $window 'PlayScroll')
    $notches = 0
    $beginRect = Get-Rect (Get-Element $window 'MantraBeginButton')
    while (-not (Test-Inside $beginRect $viewport)) {
        if ($notches -ge 24) {
            Fail ("the Begin button never came fully inside the Play page viewport after $notches wheel " +
    "notches: button $($beginRect.X),$($beginRect.Y) $($beginRect.W)x$($beginRect.H) vs viewport " +
    "$($viewport.X),$($viewport.Y) $($viewport.W)x$($viewport.H)")
        }
        Wheel-Down $viewport
        $notches++
        $beginRect = Get-Rect (Get-Element $window 'MantraBeginButton')
    }
    Write-Output "Begin button inside the Play viewport after $notches wheel notch(es)"

    # (2) THE CARD'S OWN WORDS, BEFORE IT IS PRESSED. A card that rendered another feature's blurb
    # is a perfectly plausible rectangle.
    $mantraTitle = (Get-Element $window 'MantraTitle').Current.Name
    if ($mantraTitle -ne 'MANTRAS') { Fail "the Play wall's third card is not the Mantras card: '$mantraTitle'" }
    $scopeLine = (Get-Element $window 'MantraScopeLine').Current.Name
    if ($scopeLine -notlike '*silent*') {
        Fail "the Mantras card does not admit that this build's game is silent: '$scopeLine'"
    }
    if ($null -ne (Find-Element $window 'MantraFaultText')) {
        Fail 'the Mantras card is already showing a launch fault before anything was pressed'
    }
    Write-Output "card gate: '$mantraTitle' / the card admits the missing audio / no fault line on screen"

    # (3) PRESS IT.
    Click-Rect $beginRect
    $game = Wait-MantraWindow $script:proc.Id
    $script:mantraWindow = $game
    $gameRect = Get-Rect $game
    Write-Output "mantra window up: '$($game.Current.Name)' at $($gameRect.X),$($gameRect.Y) $($gameRect.W)x$($gameRect.H)"

    # (4) MAXIMIZED AND CHROMELESS, MEASURED AGAINST THE REAL DESKTOP - and the measurement
    # CORRECTED this line rather than confirming it. A maximized chromeless window takes the WORK
    # AREA, not the screen: the first run of this surface read 2880x1716 against a 2880x1800
    # display, which is the taskbar, and demanding the screen would have called a correct window
    # broken. WPF's WindowState=Maximized obeys the same rule, so this is parity and not a
    # divergence. What is asserted is therefore exact: the work area, to the pixel.
    $screenW = [int][VerifyNative]::GetSystemMetrics(0)
    $screenH = [int][VerifyNative]::GetSystemMetrics(1)
    $work = New-Object VerifyNative+RECT
    if (-not [VerifyNative]::SystemParametersInfo([VerifyNative]::SPI_GETWORKAREA, 0, [ref]$work, 0)) {
        Fail 'the work area is unreadable; the maximized claim cannot be measured on this desktop'
    }
    $workW = $work.Right - $work.Left
    $workH = $work.Bottom - $work.Top
    if ($gameRect.X -ne $work.Left -or $gameRect.Y -ne $work.Top -or $gameRect.W -ne $workW -or $gameRect.H -ne $workH) {
        Fail ("the mantra window is $($gameRect.X),$($gameRect.Y) $($gameRect.W)x$($gameRect.H) and the work " +
    "area is $($work.Left),$($work.Top) ${workW}x${workH} (primary display ${screenW}x${screenH}): " +
    'WindowState=Maximized with WindowDecorations=None did not take the desktop')
    }
    Write-Output ("maximized: $($gameRect.W)x$($gameRect.H) at $($gameRect.X),$($gameRect.Y) IS the work area " +
    "exactly, on a ${screenW}x${screenH} primary display (the taskbar keeps the difference)")

    # (5) IT IS OVER THE SHELL, and the shell is HWND_TOPMOST.
    Assert-Inside $windowRect $gameRect 'the shell window' 'the mantra window'
    Write-Output ("occlusion: the shell at $($windowRect.X),$($windowRect.Y) " +
    "$($windowRect.W)x$($windowRect.H) is inside the game's rect (the shell is HWND_TOPMOST; the " +
    'game is owned by it, so this is occlusion rather than an isolated Topmost claim)')

    # (6) THE UIA GATE, READ BEFORE ANY PIXEL. The run is real, it is the count the CARD asked for,
    # and nothing has been typed yet.
    $mantra = (Get-Element $game 'MantraText').Current.Name
    if ([string]::IsNullOrWhiteSpace($mantra)) {
        Fail 'the mantra line publishes no text; there is nothing on this screen to read or to type back'
    }
    $target = (Get-Element $game 'MantraTargetText').Current.Name
    if ($target -ne '/25') {
        Fail ("the run asks for '$target' repetitions; the card's picker opens on 25 " +
    "(PlayPage.DefaultCardReps, upstream's SelectedIndex=1), so the picked count did not reach the run")
    }
    foreach ($pair in @(@('MantraCompletionsText', '0'), @('MantraStreakText', '0'), @('MantraBestStreakText', '0'))) {
        $read = (Get-Element $game $pair[0]).Current.Name
        if ($read -ne $pair[1]) { Fail "$($pair[0]) reads '$read' on a fresh run; expected '$($pair[1])'" }
    }
    $answer = (Get-Element $game 'MantraAnswerText').Current.Name
    if (-not [string]::IsNullOrEmpty($answer)) { Fail "the echo line already reads '$answer' before anything was typed" }
    Write-Output "run gate: mantra '$mantra' | 0$target reps | streak 0 | best 0 | echo line empty"

    # (7) THE GESTURE, and it is the whole difference between the two states.
    if ($State -eq 'typed') {
        if ($mantra.Length -lt 6) { Fail "the drawn mantra '$mantra' is too short to type all but its last two characters" }
        $prefix = $mantra.Substring(0, $mantra.Length - 2)
        Type-Mantra $prefix

        # FOCUS ACQUISITION, READ BACK OFF THE PRODUCT. Nothing clicked into this window.
        $echo = (Get-Element $game 'MantraAnswerText').Current.Name
        if ($echo -ne $prefix) {
            Fail ("the echo line reads '$echo' after typing '$prefix' on the real keyboard: the window " +
    'did not take the keyboard focus when it opened, or the keystrokes went somewhere else')
        }
        # And NOTHING completed: two characters short is short, so no XP was banked, no streak
        # moved, and the mantra under the capture is still the one that was read above.
        $after = (Get-Element $game 'MantraCompletionsText').Current.Name
        $streak = (Get-Element $game 'MantraStreakText').Current.Name
        if ($after -ne '0' -or $streak -ne '0') {
            Fail "typing a PREFIX completed a repetition (reps '$after', streak '$streak'); the capture's colours would be off the warmed ramp"
        }
        if ((Get-Element $game 'MantraText').Current.Name -ne $mantra) {
            Fail 'the mantra changed under the capture; the run advanced when it should not have'
        }
        Write-Output "state drive: '$prefix' typed on the REAL keyboard with no click into the window -> the game echoes it back"
    }

    # (8) THE BAND, derived from the mantra line's OWN rect and identical in both states: the left
    # 60% of it, clear of the last two characters that stay dim in `typed`, and clear of the top
    # and bottom edges where the glyphs' antialiasing and the drop shadow live.
    $lineRect = Get-Rect (Get-Element $game 'MantraText')
    if ($lineRect.W -le 0 -or $lineRect.H -le 0) { Fail 'the mantra line has no rect on this desktop' }
    Assert-Inside $lineRect $gameRect 'the mantra line' 'the mantra window'
    Write-Output ("mantra line $($lineRect.X),$($lineRect.Y) $($lineRect.W)x$($lineRect.H) - " +
    "declared FontSize 72 inside a Viewbox capped at 1400x300 DIP @ scale $scale")

    $capX = $lineRect.X + [int][math]::Round($lineRect.W * 0.05)
    $capY = $lineRect.Y + [int][math]::Round($lineRect.H * 0.25)
    $capW = [int][math]::Round($lineRect.W * 0.60)
    $capH = [int][math]::Round($lineRect.H * 0.50)
    Assert-Inside @{ X = $capX; Y = $capY; W = $capW; H = $capH } $lineRect 'the mantra sample band' 'the mantra line'
    # THE OCCLUSION CLAIM IS THE BAND'S OWN, not a statement about the window: these exact pixels
    # are inside the shell's rect, and the shell is the topmost window on this desktop.
    Assert-Inside @{ X = $capX; Y = $capY; W = $capW; H = $capH } $windowRect 'the mantra sample band' "the shell's own rect"
    Write-Output "band $capX,$capY ${capW}x${capH} - inside the mantra line AND inside the shell's rect"

    $windowRect = $gameRect
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
elseif ($Surface -eq 'companion-privacy') {
    # =============================================================================================
    # WHAT LEAVES YOUR PC. The privacy dial (audit row A3) over the per-app title allow-list
    # (row A4), and the two states are ONE NAMED APP apart.
    #
    # THE CLAIM IS THE INVERSION, so it is gated on the UIA tree before any pixel is read. Pressing
    # "+ Page titles" with nobody named opens the per-app editor and MOVES NOTHING ELSE: the strip
    # snaps back to "App names only", because the dial reports the state rather than the press.
    # Upstream names that exact failure - "a stop that silently meant nothing would be the privacy
    # failure that looks like a working feature"
    # (Views/Controls/Companion/Runtime/AwarenessPrivacyRuntimeVm.cs:24-27). Naming one app is the
    # single thing that moves it, and `titles` is that same window after one real typed name.
    # =============================================================================================
    Open-CompanionWindow $window
    $companion = $script:extraWindow
    $scale = (Get-DoorRect $window 'companion').Scale
    $companionRect = Get-Rect $companion
    if ($companionRect.W -le 0 -or $companionRect.H -le 0) { Fail 'the companion window has no rect on this desktop' }

    $head = (Get-Element $companion 'PrivacyDialHead').Current.Name
    if ($head -ne 'what leaves your PC') { Fail "the privacy card is not headed by upstream's line: '$head'" }

    $off = Get-Element $companion 'DialOff'
    $broad = Get-Element $companion 'DialBroad'
    $titles = Get-Element $companion 'DialTitles'
    if (-not (Get-Selected $off)) { Fail 'the dial is not at Off in a fresh process; the default is not closed' }
    $hint = (Get-Element $companion 'PrivacyDialHint').Current.Name
    if ($hint -ne 'her eyes are closed. nothing is watched, nothing is counted.') {
        Fail "the Off stop does not carry upstream's sentence: '$hint'"
    }
    if ($null -ne (Find-Element $companion 'TitleAllowInput')) {
        Fail 'the per-app editor is on screen before anyone asked for it'
    }
    Write-Output "closed gate: dial at Off, editor absent, hint '$hint'"

    # ONE PRESS ON THE THIRD STOP. This is the state both captures share.
    Click-Rect (Get-Rect $titles)
    Start-Sleep -Milliseconds 250
    $box = Find-Element $companion 'TitleAllowInput'
    if ($null -eq $box) { Fail 'asking for page titles did not open the per-app editor' }
    if (Get-Selected (Get-Element $companion 'DialTitles')) {
        Fail ('the dial moved to "+ Page titles" with NO app named. Nothing widened, so the stop is ' +
    'reporting a breadth the filter does not have - the privacy failure that looks like a working feature')
    }
    if (-not (Get-Selected (Get-Element $companion 'DialBroad'))) {
        Fail 'the dial is at neither of the two reachable stops after asking for page titles'
    }
    $hint = (Get-Element $companion 'PrivacyDialHint').Current.Name
    if ($hint -ne 'the category, the app name and a rounded time. never a page title.') {
        Fail "the middle stop does not carry upstream's sentence: '$hint'"
    }
    Write-Output "broad gate: the editor opened, the dial stayed at 'App names only', hint '$hint'"

    if ($State -eq 'titles') {
        Type-Lowercase $box 'browser'
        Click-Rect (Get-Rect (Get-Element $companion 'TitleAllowAdd'))
        Start-Sleep -Milliseconds 250
        if (-not (Get-Selected (Get-Element $companion 'DialTitles'))) {
            Fail 'naming an app did not move the dial to "+ Page titles"; the third stop is unreachable'
        }
        if (Get-Selected (Get-Element $companion 'DialBroad')) { Fail 'two stops read as selected at once' }
        $hint = (Get-Element $companion 'PrivacyDialHint').Current.Name
        if ($hint -ne 'app names, plus page titles for the apps you name yourself.') {
            Fail "the third stop does not carry upstream's sentence: '$hint'"
        }
        Write-Output "titles gate: one typed app name moved the dial, hint '$hint'"
    }

    # THE BAND, derived from the third segment's OWN rect. Each segment sits in a Border seat with
    # 6,5 padding (CompanionWindow.axaml, Border.dial-seat), and Avalonia gives Border no automation
    # peer (harness surprise #1) - but the RadioButton inside it has one, and the 5 DIP above its
    # rect is the seat's own fill. That fill is the only thing the two states differ by here:
    # #FF1E1822 unselected, #FF4A2C55 selected.
    $titlesRect = Get-Rect (Get-Element $companion 'DialTitles')
    $capX = $titlesRect.X
    $capY = $titlesRect.Y - [int][math]::Round(4 * $scale)
    $capW = $titlesRect.W
    $capH = [int][math]::Round(3 * $scale)
    if (($capY + $capH) -gt $titlesRect.Y) {
        Fail "the seat band ends at $($capY + $capH) and the segment starts at $($titlesRect.Y); it is sampling the segment's own glyphs"
    }
    if ($capY -lt ($titlesRect.Y - [int][math]::Round(5 * $scale))) {
        Fail "the seat band starts at $capY, above the seat's 5 DIP of top padding; it is outside the seat"
    }
    Write-Output ("band $capX,$capY ${capW}x${capH} @ scale $scale - inside the '+ Page titles' seat's top padding, " +
    "above the segment at $($titlesRect.Y)")

    Assert-Inside @{ X = $capX; Y = $capY; W = $capW; H = $capH } $companionRect 'the dial seat band' 'the companion window'
    $windowRect = $companionRect
}
elseif ($Surface -eq 'companion-transcript') {
    # =============================================================================================
    # EVERYTHING YOU TWO HAVE SAID (audit row D11). `closed` is the companion window with no
    # transcript in the UIA tree AT ALL; `open` is the same window with the read-only viewer over
    # it, one press apart.
    #
    # GATED ON THE TREE FIRST, and on this surface that matters more than usual: the transcript is
    # an OWNED window, and an owned Avalonia window is a UIA DESCENDANT of its owner rather than a
    # sibling (the finding the recap path records) - so "it opened" has to be read where it really
    # is, and "it did not" has to be an absence rather than a lookup that quietly found the wrong
    # window.
    # =============================================================================================
    Open-CompanionWindow $window
    $companion = $script:extraWindow
    $scale = (Get-DoorRect $window 'companion').Scale
    $companionRect = Get-Rect $companion
    if ($companionRect.W -le 0 -or $companionRect.H -le 0) { Fail 'the companion window has no rect on this desktop' }

    if ($null -ne (Get-DeepWindow $script:proc.Id 'Everything you two have said')) {
        Fail 'the transcript window is already open before anything was pressed'
    }
    $button = Get-Element $companion 'TranscriptButton'
    Write-Output "closed gate: no transcript window in this process; the button reads '$($button.Current.Name)'"

    $transcriptRect = $null
    if ($State -eq 'open') {
        Click-Rect (Get-Rect $button)
        $transcript = Wait-DeepWindow $script:proc.Id 'Everything you two have said'
        $transcriptHwnd = [IntPtr]$transcript.Current.NativeWindowHandle
        if ($transcriptHwnd -eq [IntPtr]::Zero) { Fail 'the transcript window has no native handle; it cannot be raised or captured' }
        [VerifyNative]::SetWindowPos($transcriptHwnd, [VerifyNative]::HWND_TOPMOST, 0, 0, 0, 0,
            [VerifyNative]::SWP_NOMOVE -bor [VerifyNative]::SWP_NOSIZE -bor [VerifyNative]::SWP_SHOWWINDOW) | Out-Null
        Start-Sleep -Milliseconds 400

        $heading = (Get-Element $transcript 'TranscriptHeading').Current.Name
        if ($heading -ne 'Everything you two have said') { Fail "the transcript is not headed by upstream's line: '$heading'" }
        $empty = (Get-Element $transcript 'TranscriptEmpty').Current.Name
        if ($empty -ne 'nothing yet. the first thing you say is the first thing she keeps.') {
            Fail "the transcript's empty state is not upstream's line: '$empty'"
        }
        $note = (Get-Element $transcript 'TranscriptNote').Current.Name
        if ($note -ne 'her memory lives on this machine only') { Fail "the transcript's storage note is not upstream's line: '$note'" }
        Write-Output "open gate: transcript window present, heading '$heading', empty state '$empty', note '$note'"

        $transcriptRect = Get-Rect $transcript
        if ($transcriptRect.W -le 0 -or $transcriptRect.H -le 0) { Fail 'the transcript window has no rect on this desktop' }
        Write-Output "transcript rect $($transcriptRect.X),$($transcriptRect.Y) $($transcriptRect.W)x$($transcriptRect.H)"
    }

    # THE BAND, at the SAME screen coordinates in both states, derived from a control that exists
    # in both: the privacy card's heading, whose settings panel sits under the transcript when the
    # transcript is up. 4 DIP above it is the settings Border's own top padding (14,8), which the
    # Border does not paint - so in `closed` those pixels are the window's ground #FF141018, and in
    # `open` they are the transcript's ground #FF1E1822.
    $headRect = Get-Rect (Get-Element $companion 'PrivacyDialHead')
    $capX = $headRect.X + [int][math]::Round(20 * $scale)
    $capY = $headRect.Y - [int][math]::Round(4 * $scale)
    $capW = [int][math]::Round(300 * $scale)
    $capH = [int][math]::Round(3 * $scale)
    if (($capY + $capH) -gt $headRect.Y) {
        Fail "the band ends at $($capY + $capH) and the card heading starts at $($headRect.Y); it is sampling the heading's glyphs"
    }
    Write-Output "band $capX,$capY ${capW}x${capH} @ scale $scale - in the settings panel's top padding, above the card heading at $($headRect.Y)"

    Assert-Inside @{ X = $capX; Y = $capY; W = $capW; H = $capH } $companionRect 'the transcript sample band' 'the companion window'
    if ($State -eq 'open') {
        # The `open` capture is only evidence if the transcript really covers those pixels. It is
        # centred on its owner, so this holds by construction - and it is ASSERTED rather than
        # assumed, because a resize on either window would otherwise photograph the companion's own
        # ground and the check would pass for the wrong reason.
        Assert-Inside @{ X = $capX; Y = $capY; W = $capW; H = $capH } $transcriptRect 'the transcript sample band' 'the transcript window'
    }
    $windowRect = $companionRect
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

# THE CARD'S LAST FACT, AND IT IS AN INTERACTION RATHER THAN A PICTURE. Escape closes a pop quiz
# with no answer, which is upstream's own behaviour (Windows/PopQuizWindow.xaml.cs:128-134) and the
# module's PopQuizResolution.Skipped. Pressed here on the REAL keyboard, into a window that holds
# the real keyboard focus, and read back off the shell's own panel - so this run ends by proving the
# card takes input from a person and not only that it was drawn. It is also how the card comes down:
# a WM_CLOSE from this script would take it down without any of that being true.
if ($null -ne $script:popQuizCard) {
    [VerifyNative]::keybd_event([VerifyNative]::VK_ESCAPE, 0, 0, [IntPtr]::Zero)
    [VerifyNative]::keybd_event([VerifyNative]::VK_ESCAPE, 0, [VerifyNative]::KEYUP, [IntPtr]::Zero)
    $escDeadline = [Diagnostics.Stopwatch]::StartNew()
    while ($escDeadline.Elapsed.TotalSeconds -lt 15) {
        if ($null -eq (Get-NamedWindow $script:proc.Id 'CCP input prompt')) { break }
        Start-Sleep -Milliseconds 200
    }
    if ($null -ne (Get-NamedWindow $script:proc.Id 'CCP input prompt')) {
        Fail "the pop quiz card was still up $([int]$escDeadline.Elapsed.TotalSeconds)s after Escape was pressed"
    }
    $afterEsc = (Get-Element $window 'PopQuizLiveState').Current.Name
    if ($afterEsc -notlike '*You skipped the last one with Esc.*') {
        Fail "the card closed but the module did not record a skip: '$afterEsc'"
    }
    Write-Output ("card closed by a real Escape after $([math]::Round($escDeadline.Elapsed.TotalSeconds, 1))s -> " +
    "'$afterEsc'")

    # And stop the session, through the same button that started it, so the app is closed below in
    # the state every other capture closes it in.
    Click-Rect (Get-Rect (Get-Element $window 'SessionStartButton'))
    Write-Output "session stopped: the button reads '$((Get-Element $window 'SessionStartButton').Current.Name)'"
}

# THE GAME IS CLOSED THE WAY A PLAYER CLOSES IT. Escape leaves (MantraWindow.OnKeyDown, upstream's
# Windows/MantraWindow.xaml.cs:442-447, and the window says 'Esc to exit' beside its own box), and
# pressing it here proves one more thing no picture can: the window is still taking keystrokes from
# a real keyboard at the end of the run. A WM_CLOSE from this script would take it down without any
# of that being true.
if ($null -ne $script:mantraWindow) {
    [VerifyNative]::keybd_event([VerifyNative]::VK_ESCAPE, 0, 0, [IntPtr]::Zero)
    [VerifyNative]::keybd_event([VerifyNative]::VK_ESCAPE, 0, [VerifyNative]::KEYUP, [IntPtr]::Zero)
    $escDeadline = [Diagnostics.Stopwatch]::StartNew()
    while ($escDeadline.Elapsed.TotalSeconds -lt 15) {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $still = $false
        foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:proc.Id)))) {
            if ($w.Current.Name -like '*Mantra Lab*') { $still = $true }
            foreach ($o in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Window)))) {
                if ($o.Current.Name -like '*Mantra Lab*') { $still = $true }
            }
        }
        if (-not $still) { break }
        Start-Sleep -Milliseconds 200
    }
    Write-Output "mantra window closed by a real Escape after $([math]::Round($escDeadline.Elapsed.TotalSeconds, 1))s"
    $script:proc.Refresh()
}

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

# NON-VACUITY IS PART OF "CAPTURED", not a downstream opinion. A correctly-sized image of ONE
# colour is exactly what this step produces when nothing was drawn, and it printed CAPTURE PASS
# over 7,700 black pixels on the Linux leg before this gate existed. The rule lives in CcpVerify
# --vacuity so both capture legs share ONE implementation, and this script keeps its standing
# promise to read no pixel itself.
#
# $PSScriptRoot rather than $verifyDir ON PURPOSE: the companion-transcript/phrase-backup branch
# above rebinds $verifyDir to a temp export directory at script scope, so $verifyDir no longer
# names this folder by the time control reaches here.
$vacuityExe = Join-Path $PSScriptRoot 'CcpVerify\bin\Debug\net10.0\CcpVerify.exe'
if (-not (Test-Path $vacuityExe)) {
    Fail "the capture-vacuity gate is not built: $vacuityExe (build client/CcpClient.sln)"
}
$census = (& $vacuityExe --vacuity $outFile 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { Fail "$census (CcpVerify --vacuity exit $LASTEXITCODE)" }
Write-Output $census

Write-Output "CAPTURE: $outFile ($($capW)x$($capH))"
Write-Output 'CAPTURE PASS'
# SAY SO. Every failure path here calls `exit 1`, but success fell off the end of the script, and
# a .ps1 invoked with `&` that never calls `exit` leaves $LASTEXITCODE holding the PREVIOUS
# command's code. self-test.ps1 guards each capture with `if ($LASTEXITCODE -ne 0)`, so those
# guards were reading whatever ran before — vacuously green when the predecessor was a build, and
# a false FAILURE the moment the predecessor was CcpVerify reporting a seeded regression with
# exit 2. Found by that exact false failure while adding the rack phase.
exit 0
