# Tiered targeted verification harness

**Status:** active deliverable of task-board row 7. Replaces the rejected first-attempt whole-app smoke/layer strategy (`first-attempt-lessons.md`). Owner decisions applied: A-012 (targeted checks over blanket sweeps), A-014 (checks exist only with a real current consumer), `runtime-capability-contract.md` evidence-class discipline extended to test evidence.

## The four tiers

### Tier 1 — fast affected checks (never launches the app)

Build + unit tests + headless tests. Runs on every iteration; the only tier that runs unconditionally.

- **"Affected" is defined concretely by csproj path**, matching how the contract testCommand narrows:
  - `dotnet build client/CcpClient.sln -c Debug --nologo` — the solution build IS the affected-build check (the solution contains only projects that exist; there is no wider build to narrow).
  - `dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` — pure unit tests (no Avalonia runtime, no app launch). Assertion-logic unit tests for the tier-3 console tool live here.
  - `dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` — headless Avalonia tests (in-memory windowing/rendering; no real display, no app launch).
  - `node client/tests/floor/check-warnings.mjs` — the build-warning gate. It performs the build above with `--no-incremental` and reads the whole unfiltered output, so "0 warnings / 0 errors" is observed rather than asserted. Run it immediately BEFORE `check-floor.mjs`; see "Build-warning evidence class" below for what it covers and what it cannot see.
- Tier selection is by csproj path: a task touches only logic → run `CcpClient.Tests`; touches AXAML/visual tree → also run `CcpClient.HeadlessTests`. The full contract testCommand (all three commands) is the pre-`.DONE` gate.
- The headless tests live in a SEPARATE project because `[assembly: AvaloniaTestApplication]` is assembly-wide; putting them in `CcpClient.Tests` would force an Avalonia application onto the 85 landed unit tests.

### Tier 2 — targetable one-surface/state capture + headed actions (task close)

Thin scripts under `client/tools/verify/` formalizing the headed-smoke patterns. Launch the real app, raise it (`SetWindowPos(HWND_TOPMOST)` on Windows — the app opens unactivated behind existing windows and pixel captures read the occluder, harness surprise #2), read UIA/layout-probe facts, drive real input, capture ONE surface+state by name:

```
pwsh client/tools/verify/capture.ps1 -Surface rail-door -State selected
pwsh client/tools/verify/capture.ps1 -Surface rail-door -State unselected
pwsh client/tools/verify/capture.ps1 -Surface dashboard -State unselected
pwsh client/tools/verify/capture.ps1 -Surface rack-row -State selected
pwsh client/tools/verify/capture.ps1 -Surface rack-row -State unselected
pwsh client/tools/verify/capture.ps1 -Surface rack-row-dot -State armed
pwsh client/tools/verify/capture.ps1 -Surface rack-row-dot -State off
pwsh client/tools/verify/capture.ps1 -Surface session-row -State easy
pwsh client/tools/verify/capture.ps1 -Surface session-row -State hard
pwsh client/tools/verify/capture.ps1 -Surface session-start -State idle
pwsh client/tools/verify/capture.ps1 -Surface session-start -State running
pwsh client/tools/verify/capture.ps1 -Surface session-history -State kept
pwsh client/tools/verify/capture.ps1 -Surface session-history -State not-kept
pwsh client/tools/verify/capture.ps1 -Surface trainer-card-level -State fresh
pwsh client/tools/verify/capture.ps1 -Surface trainer-card-level -State earned
pwsh client/tools/verify/capture.ps1 -Surface mantra-window -State fresh
pwsh client/tools/verify/capture.ps1 -Surface mantra-window -State typed
```

Surfaces are NAMED capture scopes, not app screens: `rail-door` = a navigation rail door rect taken from the app's own layout probe (Avalonia exposes no UIA peers for Border/Grid/StackPanel — harness surprise #1); `dashboard` = the whole window; `rack-row` = the Studio rack's Flash Images row rect; `rack-row-dot` = that row's 8-DIP state dot cell; `session-row` = one scripted-session row's 4-DIP difficulty stripe cell, on the SESSIONS row's panel; `session-start` = that panel's one start/stop button; `session-history` = a 200x5 DIP band at the top of the Recent Sessions window's list, where its first row is or is not.

**The session surfaces are the first whose STATE is a thing the user asked for rather than a thing the app is.** Every other surface here is a style or a selection: it is painted or it is not, and driving it is one click. `session-start -State running` is a scripted session really running inside the app, reached only the way a user reaches it — open the SESSIONS row at the foot of the Studio rack, pick a session, press the button, read the confirmation, press its `Start Session` — because there is no flag for it and deliberately so. That makes a new failure mode possible: a click that silently did nothing photographs a perfectly plausible pink button and passes the `idle` check. So this surface is gated on the RUN's own published state before any pixel — the four shipped session names and durations, the confirmation's own promise text, the assertion that NOTHING has started while that question is up, then the readout's phase line and countdown and the button's own `STOP SESSION (mm:ss)` caption. That gate has already earned itself: a first draft demanded `00:00 elapsed` and a real desktop read back `00:01 elapsed, 29:58 remaining`, because a real session's clock really does run between the click and the read.

**The two `session-history` states differ by NOTHING A USER DID DIFFERENTLY, and that is the newest and strangest inversion here.** Both captures start the same session with the same four gestures, stop it with the same two, press the same Recent sessions button and photograph the same derived band of the same window. The only difference is that `kept` leaves the session running for 34 seconds and `not-kept` stops it at once — and upstream's retention rule (`Services/Session/SessionLogService.cs:24`, `:93-94`: no media AND under 30 seconds is the only case that is dropped) decides whether that run is in the list at all. Each check scored 2352/2352 EXACT on its own capture and 0/2352 on the other; both also fail on a capture of the whole dashboard (0.019 and 0.000). **The 30-second floor is the only thing in this repository exercised against real wall time** — every test of it drives an injected clock — and the capture refuses by name if the run's own reported elapsed lands on the wrong side of it, so a machine slow enough to spend 30 seconds between two clicks cannot photograph a row and call it the empty state.

**The `session-history` runs are also the only evidence that the SESSION COMPLETE recap opens at all.** It has no surface of its own — it wears the shell's palette, so no pixel check could tell its plate from the shell's — and it is instead gated through UIA inside both history runs: a SECOND top-level window whose headline reads `Session Ended Early`, whose subtitle names Morning Drift, whose duration cell is MM:SS, and which carries both of its refusals and its empty-media line. Two harness findings came out of driving it, and both are recorded in `capture.ps1` where they bite: **an owned Avalonia window is a UIA DESCENDANT of its owner, not a sibling of it** (the first draft looked only at the desktop root's children and refused a window the app's own diagnostic showed as `visible=True pos=534,166 size=540x620 owner=shell`), and **an owned window under an HWND_TOPMOST shell cannot be clicked at its own coordinates** — the press lands on the shell, which is why the recap is raised before it is touched.

**The two `session-row` states are two different ROWS, and that is the strongest inversion on this manifest.** `easy` is Morning Drift's stripe and `hard` is Good Girls Don't Cum's: same page, same panel, same derivation, same 7x35 cell, and the only difference between the two captures is the difficulty in the `.session.json` file. Each check scored 1.0000 on its own capture and 0.0000 on the other, so each one failing on the other is a statement about the DATA reaching the screen. Both also score 0.0000 on a capture of the whole dashboard — an image that contains both surfaces and still cannot pass either check. The stripe cell is derived (a `Border` has no peer) from the row's rect and the meta cell's rect using the row's own geometry constants, cross-checked by "the row grid does not close", which refused a stripe 43 DIP short of the trailing edge and found a real layout defect in the row rather than aiming at the wrong pixels.

**`companion-permissions` is the first surface whose two states are one GESTURE apart, and the gesture is the claim.** `closed` is the AI effect-permissions grid as a fresh process gives it to the user — the master switch off and not one of the ten per-effect switches on screen — and `admitted` is the same window after that switch has been pressed once. Both captures are the same 665x7 band, derived 8 DIP below the master switch's own UIA rect (the panel is a `Border` and has no peer; the switch above it is a `CheckBox` and has one), and cross-checked in `admitted` against the first switch's rect so the band cannot drift into the grid's own rows. Measured: `companion-permissions-panel-fill` 1.0000 on its own capture and 0.0000 on the other, `companion-permissions-closed-ground` 0.9580 and 0.0000, and both fail on a capture of the whole dashboard (0.512 and 0.127) — an image containing the shell's every colour and still unable to pass either. **The default-closed claim itself is NOT a pixel claim and is not made as one:** no check can tell a ticked switch from an unticked one at this size, so it is gated on the UIA tree before any pixel — the master switch reads `ToggleState.Off`, the ten per-effect switches are ABSENT from the tree entirely in `closed`, all ten are present and every one still `Off` the instant the panel opens in `admitted`, and only then is one of them pressed and read back On. The window is owned, so it is raised HWND_TOPMOST before it is touched, for the reason the recap path already measured.

**The rack surfaces take their geometry from UIA, not from a probe, and the difference is a finding rather than a preference.** Harness surprise #1 is about `Border`/`Grid`/`StackPanel`, which is what the demonstrator card was. A rack row is a `RadioButton`, and Avalonia gives every `RadioButton` a real automation peer carrying an `AutomationId`, a screen `BoundingRectangle` and `SelectionItemPattern.IsSelected` — so where the row is, which row it is, and whether it is open are all already published. Measured on the same run: the probe says `door studio 174.9x44.0 DIP @ scale 1.75 @ screen 121,223` and UIA says `121;223;306;77`, and 174.9 x 1.75 = 306.1. The probe is redundant for the rail doors too, on Windows; it is NOT redundant on Linux, where WSLg has no UIA and `capture-wslg.sh` reads the logged line, so it stays. **A rack probe was authorised and refused:** fifteen probe lines in the bottom-docked footer add 15 x 23.4 = 351 px against a rack viewport measured at 965 px, pushing five of the fifteen rows below the scroll fold. A probe that displaces the thing it observes is not an observation seam. **Consequence to plan for: a WSLg rack capture would need a logged rack line, and this packet did not add one.**

**Two geometry traps the rack path has to handle, both measured.** (1) UIA reports UNCLIPPED bounds and `IsOffscreen=False` for rows scrolled out of the rack viewport — `RowIntensityRamp` reports `501;1505;402;63` inside a window ending at y=1470 — so `capture.ps1` refuses unless the target rect lies fully inside both the `RackScroll` viewport and the window. (2) The dot cell is derived as the row caption's right edge plus 8 DIP, cross-checked against the Visuals row, which is the only row whose grid has one child because upstream gives it no dot (`StudioPage.axaml:172-174`; `StudioTabView.xaml.cs:494-496`). The cross-check compares WIDTHS, not edges: `rack-row:checked` carries `BorderThickness="3,0,0,0"`, so a checked row's content is displaced 5 px at scale 1.75 and the two rows stop sharing an origin.

**Every capture now holds the machine-wide real-desktop lease and fences its screen read.** `capture.ps1` puts a top-most window on the interactive desktop and reads that desktop back — exactly what `RealDesktopCollection`'s fixtures do — and it ran OUTSIDE their lease until the rack work, so a capture could race a floor run and take the measured failure mode (`Expected 0, Actual 676161`). It now takes `%TEMP%/ccp-real-desktop.lease` with the identical contract (`FileMode.Create` / `FileAccess.Write` / `FileShare.Read`, raw `pid=<n>` bytes so `RealDesktopLease.HolderProcessId` can name it), before the app launches and until after it exits, and it FAILS naming the holder's pid rather than capturing around a contended desktop. `CopyFromScreen` is now preceded by `DwmFlush` (measured: 34 misses in 1200 unfenced reads, 0 in 1500 fenced); a DWM that refuses fails the capture instead of being swallowed. **Re-anchored** from `dashboard-card`/`lit`/`unlit`, which named the retired `demo.status-ticker` demonstrator card. State drives differ per platform, honestly: **Windows** drives `selected` through REAL input (a left-click on a rail door, the route confirmed by a UIA read before any pixel is captured — the same user path a regression would break); **WSLg** needs no drive at all now, because on cold start the Studio door is already `:checked` and the Companion door is not, so both states are capturable with zero input. That is strictly better than the old WSLg path, which pre-seeded a settings file: WSLg has no input automation (a named gate), and the new anchor removes the need for one rather than working around it. Interaction evidence stays Windows-headed; WSLg captures are render/scale/session-facts only.

- Windows: real window + `CopyFromScreen` crop to the layout-probe rect → PNG. (System.Drawing appears here ONLY as capture transport; scripts never read a pixel — harness consult.)
- WSLg (Linux/X11): `XGetImage` via python3 ctypes against the app's X window (`capture-wslg.sh` + `xgetimage.py`) → BMP. WSLg RAIL windows are invisible to Windows-side GDI capture (harness surprise #3).
- Headed ACTIONS (right-click quick-toggle, keyboard, teardown) beyond what a state needs remain task-specific scripts building on the same helpers; the harness owns launch/raise/read/capture, the task owns its action sequence.
- Output: `client/tools/verify/artifacts/<platform>-<surface>-<state>.png|bmp` (gitignored, in-scope .gitignore).

### Tier 3 — K3 image inspection driven by the named-check manifest

Deterministic named checks BEFORE/DESIDE model review: one cross-platform .NET console tool (`client/tools/verify/CcpVerify`) reads the manifest (`client/tools/verify/checks.json`), decodes a capture via `Avalonia.Media.Imaging.Bitmap` (no System.Drawing, zero new packages), evaluates each named check for the captured surface+state, and exits non-zero naming the FIRST failed check. K3 (`app-visual-verification` skill) then reviews the same capture against the same manifest — the manifest row is the shared contract between deterministic assertion and model review.

- Checks are scoped to REAL current consumers only. **This line named the original demonstrator card until the rack work, and had been stale since the re-anchor retired it. It was stale AGAIN — it said eight after the Goon page and the Trainer Card had added four more — and is corrected on every change by counting the manifest rather than by memory.** The thirty-six checks today are (counted from the manifest, never from memory - this line has now been stale four separate times, and the count is the least useful thing on it): the rail door's selected/unselected border bands and the dashboard background (shell); the Studio rack's selected marker, selected fill, unselected ground and its state dot armed/off (rack); the Goon page's backdrop and explainer card; the Trainer Card's ground and ink; the session rack's two difficulty stripes and its start button's two liveries; the Recent Sessions list's first-row cell in the two states upstream's retention rule produces; the AI permissions grid's panel fill against the window ground it replaces; the Studio dial's track in the two liveries a running session produces, on a control the session LOCKS but never WRITES so both captures are the same control at the same value; the toast's accent in the two states a real save and a real refusal produce; the companion's privacy dial and its transcript window; and the POP QUIZ CARD's ground and question ink, whose ink floor is set an order of magnitude BELOW the lowest of four measurements because the question is drawn at random and its inked fraction moves with the card - a floor near the mean redded a perfectly good card on its second run; and the TRAINER CARD LEVEL BAR in its two states, which is the first pair on this manifest whose claim is a FRACTION rather than a colour - the same 420x8 DIP bar painted in the same two colours in both captures, separated only by how much of the track the fill covers, and the band that reads it sits at 0.10..0.30 against a fill reaching 0.5949 because the pop quiz lesson is that a boundary set near a value the product moves reds a good capture. And the TYPED MANTRA GAME's own line in its two states, which is the first pair here taken inside a MAXIMIZED, CHROMELESS window that covers the shell: the same band of the same mantra, dim before a keystroke and lit after all but its last two characters have arrived on a real keyboard (`mantra-window-fresh-dim` 0.281 on its own capture and 0.003 on the other's, `mantra-window-typed-lit` 0.273 and 0.000 EXACT). The mantra is DRAWN AT RANDOM, so the band is cut from the line's own UIA rect rather than fixed, and the floors sit an order of magnitude below the measurements for the pop quiz card's reason. The capability surface is verified by TIER-2 UIA TEXT NEEDLES inside `capture.ps1` (`capability display-session: Available` etc.) — pixel checks cannot read text, so no manifest check claims it. No speculative checks for surfaces that do not exist (A-014). Each new surface adds its checks with its own task.

### Tier 4 — theme/language/platform matrices (named milestones/releases ONLY)

Broader matrices (five themes, languages, scaling levels, platforms) run ONLY at a named milestone or release gate. This task defines the hook and does NOT run matrices. Trigger: the board row for a milestone/release names the matrix; the capture tool's `-Surface`/`-State` parameterization plus the manifest's per-surface check list are the execution mechanism. No matrix automation exists beyond this hook (A-014).

## Evidence-class rule (hard)

Every check in the manifest declares an evidence class:

- **`draw-verified`** — the check asserts facts about the visual tree, styles, bindings, or Skia draw output. Headless tests (`CcpClient.HeadlessTests`) may satisfy ONLY draw-verified assertions: the headless platform replaces windowing AND rendering with in-memory backends (default: fake drawing, no pixels at all; with `UseHeadlessDrawing=false` + `.UseSkia()`: Skia draw output). A headless frame has NO compositor, NO real window, NO DPI/scaling, NO activation/occlusion, NO OS chrome.
- **`presentation-verified`** — the check asserts what a user sees on a real display: composited pixels, window geometry, scaling, occlusion, z-order. ONLY a headed Windows/WSLg capture (tier 2) can satisfy presentation-verified checks.

**A headed Windows/WSLg gate is NEVER dischargeable by a headless frame.** All current manifest checks against real captures are `presentation-verified`; `draw-verified` checks appear when a task adds headless-frame assertions (none exist yet — the headless spike asserts tree/layout/style/binding facts, not frames).

### What `presentation-verified` covers for the Studio rack, and what it does not

**Covered.** Composited pixels, read back off the real Windows desktop through a fenced `CopyFromScreen`, for four states of two rack surfaces, driven by real mouse input and confirmed by a UIA read before any pixel was taken. Five named checks, each shown FAILING twice over: once against a real capture of the opposite state, and once against the seeded `#FFE066FF -> #FF336633` regression `self-test.ps1` drives through `MainWindow.axaml`, where `rack-row-selected-marker` scored 0/189 while `rack-row-selected-fill` still passed — the seed moved one brush and exactly one check noticed. Fractions, pass state then wrong state: marker 0.862 / 0.000, fill 1.000 / 0.000, ground 1.000 / 0.000, dot-armed 0.714 / 0.224, dot-off 1.000 / 0.000. The check manifest retains the measured thresholds.

**NOT covered, and none of it should be read into the class.**

- **No human has compared any of it to the WPF build.** `presentation-verified` means composited pixels were read back and matched named colours in named regions. Whether the port's rack LOOKS like upstream's rack is a MANUAL gate, and it is open. Nothing in this harness can close it.
- One row of fifteen (Flash Images), one machine, one scale (1.75), one theme. Colour and position only: not typography, not the rack's scroll behaviour, not the other fourteen rows, not Linux/WSLg.
- The row's `:pointerover` livery is never captured — the mouse is parked in the diagnostic footer before every read — so no check has ever seen it. What stops it being mistaken for the ground is the tolerance sizing, not a capture.
- `CopyFromScreen` reads the composited desktop, so unlike the glyph class's `PrintWindow` it is not structurally blind to transparent-versus-black. It IS blind to a foreign top-most window owning the point, and the lease cannot exclude one (`RealDesktopCollection.cs:45-49`). That residue is unchanged.
- The floor-run guards in `RackPresentationTests` that assert the lease and the fence are LEXICAL: they prove those code paths were not deleted, not that they still work. What proves the mechanism is the pair of transcripts in the record — a foreign holder took the lease and `capture.ps1` waited 22.1 s for it, and with the deadline seeded down it refused by name reporting the holder's pid.

## Audio evidence class

Audio needs its own class, and for a reason none of the visual classes have: **every audio failure
mode is inaudible from inside the process, and silence is indistinguishable from a working clip
played too quietly to hear.** A drawing capability that lies is at least visibly wrong — nothing
appears. An audio capability that lies looks exactly like an audio capability that works. So the
class is defined by WHAT WAS ASKED OF THE OPERATING SYSTEM, never by what the product's own call
returned.

- **`render-session-verified`** — the check asserts that the operating system reports an **active
  audio render session owned by this process**, read back through an instrument INDEPENDENT of the
  code that opened the device. On Windows that is `IMMDeviceEnumerator::EnumAudioEndpoints` for the
  endpoint count, `IAudioSessionManager2::GetSessionEnumerator` +
  `IAudioSessionControl2::GetProcessId` for ownership, and `IAudioSessionControl::GetState` for
  liveness. Satisfiable inside `dotnet test` on a Windows box with a render endpoint
  (`AudioCapabilityTests`), and on a box with none it is satisfied by both sides going false
  together — the assertion still bites.
- **`render-metered`** — the strongest automated audio claim this port can make: the OS's own peak
  meter (`IAudioMeterInformation::GetPeakValue`) read a **non-zero** sample level on this process's
  stream while a clip played, and **zero** on the same stream with the device open and nothing cued.
  The second half is what stops the first from passing vacuously: a product that opened a device and
  played nothing reads zero and reds. The number is produced by the Windows audio engine from the
  samples it consumed from us; it is not a value this process chose.
- **`audible-verified`** — **a human confirmed they heard it.** NOT satisfiable by any automated
  step on any platform, Windows included. It is a manual gate and it is the only thing that closes
  the last link.

**A `render-metered` green is never an `audible-verified` green,** and the gap is not a formality.
`render-metered` proves samples reached the Windows audio ENGINE. It says nothing about endpoint
mute, endpoint volume, an exclusive-mode holder, a disabled jack, a DAC, an amplifier, or whether
anything is plugged in. **A virtual or RDP sink satisfies every automated audio check in this
document with no physical output anywhere** — the port has already recorded exactly that class on
WSLg (`client/docs/audio-backend-spike.md:24,88`: a single "RDP Sink", and no `pactl` present to
check it).

**What tier 1 cannot cover for audio, stated as its own list rather than folded into the desktop
section below.** Tier 1 runs `AudioCapabilityTests` against this machine's real endpoint, so it
carries the same real-resource caveat as the overlay and tray fixtures — plus three of its own:

1. **Nothing is heard.** No tier discharges `audible-verified`; there is no capture device in the
   harness and adding one would prove the room, not the app.
2. **Content is not checked.** A peak proves non-silent samples, not which clip, not the right
   channel, not the intended moment. Two modules cueing the wrong clip each would pass every check.
3. **A muted endpoint or a muted session is a property of the MACHINE and would legitimately red
   `render-metered`.** That is the honest failure and it must not be answered by weakening the
   assertion or by adding a name to `allowedSkips` — the assertion is doing exactly its job.

**Linux audio is `render-session-verified`-INCAPABLE in this build**, and refuses in type rather than
claiming anything: the backend works there (SoundFlow ships `libminiaudio.so` per-RID) and the
read-back does not exist, so `AudioPresenceFactory` returns a typed refusal carrying its own four-step
manual gate (`Audio/AudioPresenceFactory.LinuxManualGate` — `pactl list short sinks`,
`pactl list sink-inputs` filtered on `application.process.id`, `pw-dump` for the rendering node, and a
human). WSLg cannot discharge it.

## Input evidence class

Input needs its own class for the mirror of the overlay class's reason. That work proved a surface is
click-**through** and its review recorded that "input passes through" is the property most easily
faked, because *a window that does not exist satisfies it too*. Proving input is **captured** is
faked just as cheaply and in more ways: a handler was attached, `WS_EX_TRANSPARENT` was not set,
`SetForegroundWindow` returned, a focus API answered yes. So this class is defined, like the audio
one, by **what was asked of the operating system** — and by one measurement that made the definition
necessary.

> **The measurement.** On this machine, before any of it was written: a plain `SetForegroundWindow`
> from a process that did not already own the foreground **returned FALSE**, the OS kept the keyboard
> where it was, every synthesised keystroke went to the other application — and
> `GetGUIThreadInfo(<i>ourThreadId</i>).hwndFocus` **still answered "our window"**. That read is
> THREAD-LOCAL. The system-wide fact is `GetGUIThreadInfo(0)`, documented as the foreground thread,
> and in the same instant it answered the other application's window
> in the same observed interval.

- **`focus-verified`** — the check asserts that the operating system reports the window as **the
  foreground window** (`GetForegroundWindow`) **and** as **the foreground THREAD's keyboard focus**
  (`GetGUIThreadInfo(0).hwndFocus`), and that the window manager routes a hit test at an interior
  point **to** it (`WindowFromPoint`), read back through an instrument INDEPENDENT of the code that
  created the window. Both focus facts are required: foreground without focus routes keystrokes to a
  child or to nothing, and a focus read without foreground is the thread-local answer above.
  Satisfiable inside `dotnet test` on a Windows box with an interactive desktop
  (`InputCapabilityTests`); on a box without one both sides go false together and the assertion still
  bites.
- **`input-delivered`** — the strongest automated input claim this port can make: a keystroke
  **synthesised at OS level** (`SendInput`) was **received by the window's own window procedure**,
  and was **not** received when another window held the foreground. The negative leg is what stops
  the first from passing vacuously — an instrument that counted its own calls would pass both.
  `SendInput` enters the system input stream below the driver and above the hardware, so the OS's own
  raw-input thread does the routing by the same rules it routes a keyboard.
- **`answered-verified`** — **a human pressed the keys.** NOT satisfiable by any automated step on
  any platform, Windows included. It is a manual gate and it is the only thing that closes the last
  link.

**An `input-delivered` green is never an `answered-verified` green.** `input-delivered` proves the
operating system routed input to this window. It says nothing about a person: every keystroke in the
suite is injected, and injection is refused outright by UIPI against a higher-integrity window and by
the secure desktop, which the fixture detects rather than skips. It also says nothing about whether
the card is **legible**: the ink read-back (the OS's own `GetPixel` over the card's client area)
proves the OS holds non-background pixels in the question band, not that the phrase is readable,
unclipped, correctly laid out, or on a screen anybody is looking at. That remains
`presentation-verified` and is undischarged for this surface.

**What tier 1 cannot cover for input**, as its own list:

1. **Nobody typed anything.** No tier discharges `answered-verified`.
2. **The foreground is LENT, not owned.** Any process can take it back the instant after the check,
   so every claim here is "what the OS said when it was asked" and never "what the OS will keep
   saying". The product's own escalation (`AttachThreadInput` + `SetForegroundWindow`, a divergence
   from WPF's plain call) is bounded and then *asked about*; it is never trusted.
3. **The host's own message loop is not proven to translate keystrokes for this window.** The
   product's pump is Avalonia's; the evidence here is taken on the presence's own bounded pump.

**Linux input capture is `focus-verified`-INCAPABLE in this build**, and refuses in type rather than
claiming anything: `InputPresenceFactory` returns a typed refusal carrying its own five-step manual
gate (`Input/InputPresenceFactory.LinuxManualGate`) which is run separately on X11 and on Wayland
**because they answer differently** — `_NET_ACTIVE_WINDOW` plus an input-focus query on X11, and
`wl_keyboard.enter` under Wayland, where the protocol deliberately offers **no** unprompted
activation at all (`xdg-activation-v1` needs a token minted from a real user interaction). The gate
says in its own text that Wayland will probably fail its step 2 **by design**, and that the honest
outcome there is a named refusal rather than a card shown anyway.

## What tier 1 does NOT cover on the real desktop (admitted rather than hidden)

Some tier-1 facts are not headless. `OverlayCapabilityTests`, `FlashDrawTests` and `TrayCapabilityTests` put REAL windows on the developer's REAL desktop inside `dotnet test` and ask the operating system about them: presence, z-order, click-through routing, layered alpha, composited pixels. That is deliberate and it is the port's only OS-level evidence for those capabilities. It also means those facts share a resource with everything else on the machine, so the boundary has to be stated.

**Covered, and mechanically enforced.** Those three fixtures live in one xunit collection, `RealDesktopCollection`, which holds an exclusive machine-wide lease (`%TEMP%/ccp-real-desktop.lease`) for as long as it runs. So no two of them contend inside a process, and **no two `check-floor.mjs` runs contend across processes** — which matters because `client/tools/gate/with-slot.mjs --slots 3` permits three concurrent gate runs by design. `RealDesktopCollectionGuardTests` fails the suite when a class that touches the desktop is not in the collection, and `CcpClient.HeadlessTests` may carry no real-desktop probe at all, because collections do not span assemblies and nothing over there could hold the lease.

**Measured, in aggregate, and not selectively.** Before: **0 red in 20** sequential floor runs, **8 red in 12** with three concurrent. After: **2 red in 36** with three concurrent (two rounds of 18: 2 red then 0 red) and **2 red in 40** sequential (two rounds of 20: 2 red then 0 red) — **4 red in 76 runs overall, 5.3 %** (the measurement record §0 and §3). Every one of the three cross-process collisions is gone from all 76 runs. **Provenance, because it changes what the figure licenses:** those 76 runs were taken at `737fa739`/`e068d50f`/`87752586`, not at the landed head — the shipped lease grants read sharing and writes its pid so the holder is nameable, whereas the measured one opened `FileShare.None`. The exclusion those runs measured is unchanged and is pinned PER RUN by `RealDesktopLeaseTests`, so every run carries its own proof that exclusion held during it; the shipped mechanism was not itself re-baselined over 76 runs.

**So the gate is better, not deterministic, and the difference matters to how you read a green.** A green floor run now means "no two test processes fought over the desktop", which it did not mean before. **The fence work NAMED the residual that survived it, and measured it to 0 in 1500 rather than to zero -- the difference is the whole discipline of this section.** The lease work left a ~5 % intermittent on the composited-pixel facts with four printable verdicts and a decision deferred until one fired. One fired: a complete desktop returned (5,184,000 pixels = 2880x1800), not uniform, the display metrics identical at the placement and at the read, and none of the flash's colour. That killed the allocation, blank-display and geometry hypotheses at once and left the verdict nobody had named -- **there was no happens-before edge between painting a layered top-most window and reading the screen at all.** Measured directly, on a rig replicating `FlashDrawObservations`' own control window with the thread pool loaded: **34 misses in 1200 unfenced reads, 0 in 1500 fenced ones**, and on every single miss the window OWNED its own centre point by `WindowFromPoint` and rendered its own colour through `PrintWindow` -- so occlusion by a foreign window is REFUTED rather than assumed away, and the window was up, on top and painted while the compositor had simply not published it yet. Decomposed: `GdiFlush` alone is 8 in 300 (no effect), `DwmFlush` alone is 0 in 300.

### The compositor fence, and why it is not a wait

`FlashPixelProbe.CaptureDesktop` -- the single choke point through which every composited desktop read in this suite passes, flash, glyph and video alike -- takes a `DwmFlush` fence before the `BitBlt(SRCCOPY|CAPTUREBLT)`. **That is an edge on the producer's own completion, not a deadline this suite chose:** `DwmFlush` blocks until the compositor's NEXT PRESENT has consumed the outstanding surface updates, which is the pixel-world twin of awaiting a task. Nothing is re-read, nothing is re-asserted, no window was widened and no assertion moved; every downstream fact still gets exactly one screen read and must still be exactly right about it. The flash class's §1 measurement that an immediate `CAPTUREBLT` already carries the painted pixel was taken on an IDLE machine, and the numbers above are what it costs under the load a floor run is always under.

On every miss the read also came back with the IDENTICAL majority colour `0x26171E` at the identical count, i.e. the same static content behind the window each time -- so a VARYING foreign occluder is refuted as well as a foreign owner of the point.

**The one control this measurement does NOT have, stated because it is the control the claim rests on.** There is no equal-elapsed-time, NO-FENCE arm. `GdiFlush` returns near-instantly, so `GdiFlush`-alone versus `DwmFlush`-alone separates "some flush" from "the compositor's flush" -- it does not separate "an ordering edge on the compositor" from "roughly one present interval of elapsed time". So: that the composited read had no happens-before edge, and that supplying one removes the fault, is **established interventionally**, with every competing explanation refuted by a printed control. That the operative property is `DwmFlush`'s **fence semantics rather than its elapsed cost** is a best-supported reading of its documented contract and is **not measured here**. Nothing else depends on it: the fence is in the instrument, not the product, and no assertion anywhere is conditioned on how long it took.

`FlashDrawTests.EveryCompositedReadIsOrderedBehindTheCompositor_OrEveryNumberBelowIsACoinFlip` pins it, so removing the fence reds the suite on EVERY run rather than restoring a 5 % intermittent that took three packets to name. Where DWM refuses the fence (composition disabled, no `dwmapi`) that is reported as itself and is distinguishable from "no fence was taken": on such a session `DesktopCaptureIsLive` is already false and this file's composited expectations are all `false == false`.

**Measured at the suite level, same protocol both sides.** One full `CcpClient.Tests` run, sequential, TRX preserved, is one sample. Base `11036bbc`: **3 red in 60** (5.0 %), every one of them `FlashDrawTests`. Then 30 strictly interleaved base/lane pairs in one window, which is the only design that separates "the tree changed" from "the desktop drifted": **base 2 red in 30, lane 0 red in 30**. So base overall is **5 red in 90 (5.6 %)** and lane is **0 in 30**, which on its own bounds the lane's rate at 9.5 % and not at zero -- the strong evidence is the 34/1200 versus 0/1500 above, not the 30 lane runs.

**So the residue is closed as a MECHANISM and bounded, not measured, as a rate: 0 in 1500 fenced reads bounds it at 0.20 % and 0 in 30 lane runs bounds the suite at 9.5 %. Neither number is zero. _Three consecutive greens are worth far more than they were and are still not proof_ -- that sentence has now survived two packets that each thought they had finished the job.**

**NOT covered, and no in-process mechanism can cover it.** A FOREIGN topmost window can still own a point on the real desktop while these facts run:

- the shipping WPF product re-asserting `HWND_TOPMOST` on a cadence (`Services/Flash/FlashService.cs:206-243`) — measured as the window that won the point while the overlay class was being written;
- a locked workstation, a screen saver, a UAC secure desktop, a full-screen exclusive game, Magnifier, a mirror driver, or an RDP session;
- any other application that raises itself over the test's rectangle at the moment the hit test is asked.

When that happens the facts **fail loudly**, and how much they can say differs by refusal: `OverlayInputNotReceived` names the winning window's class (it has a handle from `WindowFromPoint` to ask about), while `OverlayNotOnTop` can only report z-order indices, because a z-order walk that finds the surface below an ordinary window has no single culprit to name. Either way the port would rather see a red it can read than a green it cannot trust. None of these are `allowedSkips` candidates — that list is for properties of the MACHINE, and "something else was on top just then" is a property of the MOMENT. **The floor therefore claims exclusivity against other test processes and claims nothing at all against the rest of the desktop.** Sustained topmost under real contention, multi-monitor placement, cross-DPI behaviour and delivered (rather than routed) input remain tier-2 headed claims and the named manual gates in the tray, overlay and flash records.

### The residue above is still not excludable, and it is no longer silent

**A prior full-floor investigation saw three green and six red runs, every failure a `RealDesktopCollection` fact, with the failing test moving between runs — `VideoOverlayCoexistenceTests` four times, `PointerCapabilityTests` once, `InputCapabilityTests` once, all of them passing 5/5 in isolation.** The shipping WPF product on the same desktop is a plausible cause, not an established fact. This document establishes instead how a competing window breaks these checks and how the mechanism is detected; that earlier attribution remains a hypothesis with a falsification condition stated at the end of this section. Excluding such a window is impossible. **Detecting it is a different problem, and it was solved.** `RealDesktopLease`'s constructor now runs a pre-flight (`RealDesktopCollection.cs`, `DesktopPreflight`) after the lease is held and before the collection's first fact, and a contended desktop refuses there, by name, instead of surfacing two or three assertion failures that never mention the window responsible.

**The obvious detector was built, run, and rejected ON EVIDENCE — this is the part worth keeping.** "A foreign topmost window owns a contended point" was true on the authoring machine (`ConditioningControlPanel v6.8.1`, pid 16712: foreground in 12 of 12 samples, its `WS_EX_TOPMOST` main window owning all five probed points in 12 of 12, rect `(42,19)-(1605,962)` over the whole band) **and the floor run under exactly that contention was green, 2573/2573 and 152/152.** Presence is not the harm, and a detector built on it would have reddened the whole collection — 212 `[Fact]`/`[Theory]` declarations at that commit, 213 across 15 classes once this packet's own control joined — on a tree that was fine. The discriminator is one line upstream: `ChaosModeService.cs:930` returns from `RunTick` unless `_spawning && !_paused && !_manualPaused`, and `:787` returns from `KeepChromeTopmost` again unless `_spawning`, so an **idle or paused** copy cannot re-assert at all and is merely parked in the topmost band, while a **running session** re-asserts on a ~1 s cadence (`:937-940`, four ticks of the `:503` 250 ms timer, into `FlashService.cs:206-243`) and climbs back over whatever the suite raised. **That mechanism would also account for the same application present throughout producing three green and six red — but "would account for" is the honest verb.** The pre-flight demonstrated the mechanism on a staged contender it controlled; it did not reproduce wave-66, and no run in this packet was contended in the detected sense.

**So the pre-flight measures the capability the collection depends on, not a proxy for it.** Three sentinels are raised at the three x-offsets the suite's own rigs occupy (`OverlayWindowProbe.cs:286-289` at centre−260, centre, `InputWindowProbe.cs:387-393` at centre+300), lifted to the top of the topmost band, then WATCHED for 2500 ms and never re-raised — re-raising would take the point back every poll and mask the thing being detected. Any reading lost to a window this process does not own refuses, naming the process, pid, title, class, rect, the round and millisecond of first loss, and the readings-lost count, plus the foreground owner as evidence.

**The lift is why this works at all, and it is a fact about Windows worth writing down.** A plain `SetWindowPos(HWND_TOPMOST)` from a process that does not own the foreground lands at the BOTTOM of the topmost band: measured at z-order rank 6, under three `ConditioningControlPanel` windows, the taskbar and `Click to Do`, with `GetWindowBand` = 1 on both sides, so it is the foreground restriction and not a band. The suite's own probes get above it with the input class's attach-thread-input ladder (`InputWindowProbe.cs:295-318`); the pre-flight uses the same lift **minus `SetForegroundWindow` and `BringWindowToTop`**, measured to reach rank 1 and own the point while leaving the foreground window unchanged, so a floor run never takes the keyboard from whoever is at the machine.

**Demonstrated on a real foreign window, both directions, at commit `b3868c168`.** A separate process holding an 800x200 topmost window over the band and re-asserting it every 250 ms — a direct mimic of `ChaosModeService.cs:937-940` — makes the fixture refuse, naming `process 'powershell' (pid 63356) … title 'red demonstration contender'`, first lost at round 2 of 161, 159 of 483 readings: **234 attributed failures, 0 passed in the collection, total preserved at 2585.** The SAME window, same process, same rect, **parked topmost once instead of re-asserting, does NOT refuse: 0 failed, 2583 passed.** That pair is the whole claim.

**What it is not, and what it still misses.** It is not a skip, not a quarantine, not a retry, not an `allowedSkips` entry, and deliberately has no environment-variable bypass. It MISSES a contender whose period exceeds the span or that starts after the fixture is built (it is a PRE-flight), anything outside the three probed points at the vertical centre **including a second monitor** (the geometry is `SM_CXSCREEN`/`SM_CYSCREEN`, the primary display, the same number the probes use), and a **foreground-only thief that never raises a topmost window** — the input class's ladder beats a passive foreground holder, which is why the foreground is reported in every refusal and never refused on by itself. In the FALSE-POSITIVE direction it can refuse a run the suite would have passed, when a **transient one-shot occluder** — a toast, an installer popup, a window animating across the band once — owns a point at a single reading inside the span. The any-sample rule is deliberate, because a majority rule would miss the cadence; the evidence to tell the two apart therefore travels with the refusal (first-loss round, elapsed ms, readings-lost-out-of-taken) instead of changing it. **And it is falsifiable: if wave-66's condition recurs and the pre-flight reports a clean desktop while those three tests fail, the detector is wrong.**

## Build-warning evidence class

**Every landed wave of this port has reported "0 warnings / 0 errors", and until 2026-08-20 not one of those claims was checked by anything except a lane reading its own build output.** `check-floor.mjs` is a TEST floor: it runs `--no-build` by design and has never had any warning handling at all. The gate below closes that.

```
node client/tests/floor/check-warnings.mjs             # build + observe; exit 0 only on 0/0
node client/tests/floor/check-warnings.mjs --self-test  # parser corpus only, no build
node client/tests/floor/check-warnings.mjs --cold       # also force a full NuGet re-resolve
```

### The two false-green mechanisms it exists to kill, both measured

- **A filtered stream cannot report what the filter cannot match.** An earlier lane read its builds through `grep -E "error|warning CS|Build succ"`. That expression is structurally incapable of matching `warning xUnit2013`; the lane reported clean four times, and the two real warnings surfaced only when a reviewer forced a full rebuild. The gate keys on `warning <CODE>:` with the code as a wildcard, and its `--self-test` corpus carries the exact xUnit2013 line together with the retired filter, so the two are compared on every run. `WarningGateGuardTests` lifts the shipped regex out of the gate's source and executes it against that same line, so the fact binds the pattern that actually runs rather than a copy of it.
- **An incremental build reports only the warnings of the compilations it actually ran, which can be none.** Measured on base `4332b8b9`, with a live `CS0219` sitting in a source file: `dotnet build client/CcpClient.sln -c Debug --nologo` printed `1 Warning(s)`, and the very next run of the same command over the unchanged tree printed `0 Warning(s)` — MSBuild skipped `CoreCompile` for an up-to-date project. A warning is a property of the **compilation**, not of the assembly. The gate therefore forces `--no-incremental`; without that flag it is vacuous, and `WarningGateGuardTests` pins the flag with that measurement as its stated reason.

### It does not compromise the stale-build guard

`check-floor.mjs` was not modified when the warning gate landed. It still passes `--no-build`, still calls `assertBuildIsFresh`, still contains no build invocation, and a guard test pins all three. The warning gate builds because building is the thing it observes, into the same `bin/obj` the floor then reads — so in the intended order (warning gate, then floor) it *is* the "always build immediately before the gate" step and satisfies the floor's freshness precondition. **Hazard, named:** it rebuilds shared `bin/obj`, so it must never run concurrently with a floor run **in the same worktree**. Across worktrees each lane has its own output and `client/tools/gate/with-slot.mjs` already bounds machine-wide build concurrency.

### What a green gate claims

Zero warnings and zero errors, positively read, from a forced full compilation of every project in `client/CcpClient.sln` (`CcpClient.Desktop`, `CcpClient.Tests`, `CcpClient.HeadlessTests`, `CcpVerify`) in `Debug|Any CPU`, `net10.0`, on the SDK that ran it. It fails closed on every "I could not tell": a non-zero build exit, a missing or duplicated MSBuild summary counter line, a solution project that produced no output line, and any disagreement between its two independent readings (the per-diagnostic line parse and MSBuild's own `N Warning(s)` summary). It never reports a count it did not read.

### What it cannot see — the boundary

- **A suppressed warning, by construction.** `NoWarn`, an inline pragma disable, a code-analysis suppression attribute (including the `[assembly:]` and `[module:]` targeted forms, which suppress for the WHOLE assembly from any file), an `.editorconfig` **or `.globalconfig`** severity of `none`, a lowered `WarningLevel` — every one of them acts before MSBuild prints anything, so no output-reading gate can observe them. **Measured, twice, at the warning gate's code review and again in the lane:** a two-line `.globalconfig` carrying `dotnet_diagnostic.CS0219.severity = none`, with **zero csproj references** because the SDK auto-includes it from the project directory, turned the gate's own forced-full build from `1 Warning(s)` naming a real `CS0219` into `0 Warning(s)` — same probe, same flags, one file added.

  The mitigation is a **different instrument**: `WarningSuppressionCensusTests`. It enumerates exactly these shapes and claims nothing beyond them — inline pragma disables in `*.cs`; `NoWarn` in `*.csproj`, `*.props` and `*.targets`; suppression attributes including the targeted forms (the pattern is EXECUTED against all five shapes before it is used to assert an absence); `GlobalSuppressions.cs`; `.editorconfig` and `.globalconfig`/`*.globalconfig` both under `client/` and on the walk up to the repository root; and the project-level warning-policy properties. Measured at the same time: one `NoWarn`, `AVLN3001` in `CcpClient.Desktop.csproj`, deliberate per `CLAUDE.md`; nine inline `CS0067` pragma sites; zero suppression attributes, zero `GlobalSuppressions.cs`, zero analyzer-config files, zero project-level warning policy. A new suppression **of an enumerated shape** fails that test with file and code. It is lexical, it judges nothing about whether a pinned suppression is justified, and its enumeration is a list that has already been wrong once — `.globalconfig` and the targeted attribute forms were both missing from the first draft and were found by review, not by the instrument.
- **An analyzer config file (`.editorconfig` or `.globalconfig`) ABOVE the repository root.** Roslyn's discovery walks to the drive root; the census stops at the repository root, because a file outside the repository is not something an in-tree red could act on. Named, not closed.
- **Anything outside `client/CcpClient.sln` in `Debug`.** The Release/RID publish path (`client/tools/publish/publish.ps1`) is not covered, and neither legacy tree (`ConditioningControlPanel/`, `CCP.*`) is in scope at all.
- **Another machine's analyzer set.** The warning set is whatever this SDK and these package/analyzer versions emit (measured on SDK 10.0.303, MSBuild 18.6.14). A green here is not a claim about a different SDK.
- **Restore-time (`NU*`) warnings on a run where restore no-ops.** `--no-incremental` forces compilation, not restore. The gate REPORTS when restore no-opped so the reader knows which reading they hold, and names the fix in the same line. `--cold` (opt-in, appends `--force`) re-resolves every dependency so restore-time warnings reappear; use it when the diff touches a `*.csproj`, `*.props`, `*.targets` or a lock file. It is not the default because it costs a full re-resolve and can touch the network, and it weakens nothing — it only makes more of the build speak.
- **Behaviour.** It observes a build. It discharges no `draw-verified` or `presentation-verified` claim, and proves nothing about rendering, timing, audio, focus, input or window behaviour.

## Check manifest schema

`client/tools/verify/checks.json`:

```json
{
  "version": 1,
  "checks": [
    {
      "name": "rail-door-selected-border",
      "surface": "rail-door",
      "state": "selected",
      "evidenceClass": "presentation-verified",
      "kind": "border-color-band",
      "region": { "band": "top", "thicknessPx": 3 },
      "expectedColor": "#E066FF",
      "tolerance": 32,
      "minPixelFraction": 0.5
    }
  ]
}
```

Fields:

- `name` — stable check identity; the console tool exits non-zero naming the first failed `name`. K3's review prompt cites the same names.
- `surface` / `state` — match `capture.ps1 -Surface/-State`. A check is evaluated only against captures of its own surface+state.
- `evidenceClass` — `draw-verified` | `presentation-verified` (rule above).
- `kind` — evaluation semantics: `border-color-band` (sample an edge band of the capture) | `region-color` (sample a fractional rect of the capture).
- `region` — **capture-relative, never absolute pixels** (captures differ across platforms: Windows scale 1.0, WSLg scale 1.5, card 77 vs 71 DIP — a measured font-metric delta). Either `{ "band": "top|bottom|left|right", "thicknessPx": N }` (first N pixel rows/columns of the capture) or `{ "rect": { "x": 0.0, "y": 0.0, "w": 1.0, "h": 0.05 } }` (fractions of capture width/height).
- `expectedColor` — `#RRGGBB`; `tolerance` — per-channel absolute delta; `minPixelFraction` — pass criterion: fraction (0..1) of sampled region pixels within tolerance. Fraction-based, not absolute counts, so one manifest is valid at Windows scale 1.0 AND WSLg scale 1.5 (harness consult).

## Seeded-regression self-test

Re-runnable proof that the targeted gate catches real regressions (throwaway-edit pattern, AVLN2000 precedent; NO defect-injection flags in product code):

```
pwsh client/tools/verify/self-test.ps1
```

Sequence: (1) edit the REAL `MainWindow.axaml` — break the selected-door border brush (`#E066FF` → wrong value); (2) build; (3) capture `dashboard`/`selected`; (4) assert the SPECIFIC named check `rail-door-selected-border` fails with its name in the output; (5) restore the AXAML (git checkout); (6) re-capture, assert green. A self-test pass requires BOTH the seeded failure AND the restored green.

## Runtime budgets (measured, never invented)

Measured 2026-07-19 (Step 4); budget = observed + stated headroom. Cold = clean `bin/obj` + first run; incremental = no source change since last build. Machines: Windows .NET SDK 10.0.302; WSL2 Ubuntu 26.04 SDK 10.0.110 (native-dir copy, never /mnt/e).

| Tier | Windows cold | Windows incremental | WSL2 cold | WSL2 incremental | Budget |
|---|---|---|---|---|---|
| Tier 1 (build + both test projects) | 12.9 s (2.3 build + 6.1 + 4.4 tests) | 8.3 s | 14.6 s (7.5 build + 4.6 + 2.5 tests) | 9.0 s | **60 s** |
| Tier 2 (launch + capture one state) | 5.5 s | same (always a fresh launch) | 6.0 s (WSLg XGetImage) | same | **30 s** |
| Tier 3 (console assert one capture) | 0.4 s | — | 0.3 s | — | **5 s** |
| Self-test (full cycle: seed/build/capture/fail/restore/green) | 23 s | — | not applicable (Windows PS path) | — | **120 s** |

Headroom rationale: ~4x on tier 1 (dominated by NuGet/SDK variance on cold CI-like machines), ~5x on tiers 2–3, ~5x on the self-test (two builds + two captures + two assertions). Re-measure when a tier adds a project or a surface.

## Independent Visual Review

The manifest is the shared contract between deterministic assertions and independent visual review:
the review uses its named checks (name, region, property, tolerance) only for what
pixel-with-tolerance assertions cannot assess, such as clipping, truncation, state pairing, glow
bleed, and contrast. Interaction gates, including toggle behavior and tick advancement, require the
tier-2 state drive in `capture.ps1`; still images never discharge them.

## Tier-4 milestone hook

Matrices (five themes, languages, scaling levels, platform pairs) run ONLY when a task-board row names a milestone/release. The hook: `capture.ps1`/`capture-wslg.sh` take `-Surface`/`-State`; a milestone row adds its matrix as a list of surface/state invocations plus matching manifest entries. No matrix automation exists beyond this hook (A-014); do not run matrices outside a named milestone.

## Video evidence class

Video needs its own class for the sharpest version of a hazard this repository has recorded four
times: **"frames decoded" is not "frames displayed."** A decoder that returns bytes proves a FILE is
readable and nothing more — the same grade of evidence as a tray method returning, an overlay flag
being set, `Play()` returning, and a fake dial re-imposing its own clamp under test. The class exists
so a claim about video has to say WHICH of the four steps below it reached.

| class | what it means | how it is earned | who may claim it |
|---|---|---|---|
| **`clip-decodable`** | the operating system opened the container, reported a video stream and its native frame size, and handed pictures back | `MFStartup` / `MFCreateSourceReaderFromURL` / `GetNativeMediaType` / `SetCurrentMediaType(RGB32)` / `IMFSourceReader::ReadSample` | tier 1, `VideoCapabilityTests`. **On its own it is the TRAP level and no caller may report a module as working on it** |
| **`frame-on-surface`** | the operating system's own copy of the surface carries the decoded picture, over a bar the painter filled that reads back exactly its own colour, and it CHANGED when a different picture was handed over | `GetDC(hwnd)` + `GetPixel` differential inside the product; cross-checked by `PrintWindow` into a bitmap of the harness's, a path the product never takes | tier 1, `VideoCapabilityTests`. This is what `IVideoPresence` returns `Available` for |
| **`desktop-composited`** | the picture is in the COMPOSITED DESKTOP at the surface's own screen rectangle, and it changes frame by frame | `BitBlt(SRCCOPY \| CAPTUREBLT)` from the screen device context, mapped through the OS's own physical/virtual ratio (`FlashPixelProbe`) | tier 1, **machine-conditional** — see below |
| **`watched-verified`** | a human watched the clip and says it played | a person, looking at a screen | **nobody automatically. A NAMED MANUAL GATE, and no automated step on any platform discharges it, Windows included** |

**Why `desktop-composited` is machine-conditional and never a skip.** A FOREIGN topmost window can
own the point a surface is at, and no in-process mechanism can exclude one — `RealDesktopCollection`
already names that residue. It is not hypothetical: while the video class was being written the SHIPPING WPF
product was running on the same desktop and `WindowFromPoint` at the surface's own centre answered
`HwndWrapper[ConditioningControlPanel;;…]`. So the harness measures the machine first, with a LANDED
capability as its control (a click-through overlay at a disjoint rectangle painted a known colour),
and asserts the desktop leg only where that control is visible in the same capture. Where it is not,
the window-level leg is asserted instead and the failure text carries both numbers. **The expectation
flips with the machine; nothing is skipped and no assertion is relaxed.**

**Four things tier 1 cannot cover for video, and they are not defects in the suite:**

1. **That a human watched anything.** `watched-verified` is undischarged and undischargeable here.
2. **Cadence, order and timing.** Every fact drives the frame advance by hand on the injected clock.
   Nothing measures that frames arrive at the right RATE, in the right ORDER, or in time — and a
   clip that played at half speed, or backwards, satisfies every automated check in this packet.
3. **Sound.** The port's video capability has none at all (D121), so there is no A/V synchronisation
   to measure and no fact that could measure one.
4. **REAL MEDIA. Every clip in this suite is SYNTHESISED**, and a later author must know that
   before designing anything on top of it. The owner's library (`Z:\CCP Vids`) did not exist on
   the machine the video class was written on — neither did the `Z:` volume — so `TestAvi` writes an
   uncompressed 32bpp AVI in pure managed code and Media Foundation opens it as
   `MFVideoFormat_RGB32`.

   **The substitution is BOUNDED, and the boundary is the useful part.** The DISPLAY half is fed
   pictures and cannot tell where they came from, so `frame-on-surface` and `desktop-composited`
   are unaffected: a synthetic frame exercises the surface exactly as a decoded one does. **The
   cost falls entirely on `clip-decodable`**, and it is precisely two things — the source
   reader's video PROCESSOR is never exercised (the fixture is natively RGB32, so no conversion
   is ever required), and the format set the port can actually open is untested against anything
  a user owns (D124: the playable set becomes what Windows can open rather than what LibVLC
  can). Both remain NAMED uncovered mutation survivors, `M-y` and `M-w`.

   **One real clip closes both.** A packet that gains access to real media should add one
   COMPRESSED fixture before adding anything else, and must not assume the synthetic path proved
   the decoder. The fixture is deliberately non-degenerate in the one way that matters for the
   display half: `TestAvi.VerticalSplit` makes a picture whose halves differ, which is what
   catches a mishandled negative stride — a solid-colour fixture would have passed while showing
   every video upside down.

**One DWM read is used and one is deliberately refused.** `DwmIsCompositionEnabled` is a documented
`BOOL*` out-parameter and is part of the display observation. `DwmGetCompositionTimingInfo` is NOT
read: measured on the machine this was written on, the shipping `dwmapi.dll` accepts `cbSize = 292`
where `dwmapi.h:168-339` declares 320 and returns `MILERR_MISMATCHED_SIZE` for the header's layout,
so any offset into it would be a guess — and its counters are SYSTEM-WIDE, so they can never be a
fact about this process's frames. Recorded as D132 rather than left as an absence.

## Pointer evidence class

A pointer target needs its own class because it is the **third** thing this port has had to prove
about a window, and it is the inverse of both earlier ones at once. The overlay class proved a surface is
click-**through** and its review recorded that as the property most easily faked. The input class proved a
window **takes** the keyboard, and its chain rested on `GetForegroundWindow` and
`GetGUIThreadInfo(0)`. **This class is for a window that must take a CLICK and must NOT take the
foreground** — so every fact the input class's chain leaned on is one this class must assert the negation of,
and "we are the foreground" is a BUG rather than a success.

> **The measurement that shaped the instrument.** On this machine, while `PointerWindowProbe` was
> being written: a scratch target was visible, held its exact rectangle, and sat at z-index 6 with
> the first ordinary window at 7 — and `WindowFromPoint` at its own centre still answered
> `HwndWrapper[ConditioningControlPanel;...]`, **the shipping WPF product**, which is topmost too and
> re-asserts `HWND_TOPMOST` on a cadence (`Services/Flash/FlashService.cs:206-243`). Being above
> every ORDINARY window is not the same as owning a point. The instrument therefore re-asserts the
> band before it takes a routing answer — which is what the product does, what the overlay does, and
> what upstream's own bubbles do (`Services/BubbleService.cs:4778-4787`) — and the foreign-topmost
> residue below is the part that stays.

- **`pointer-routed`** — the check asserts that the operating system reports the window on screen at
  the exact rectangle asked for, holds `WS_EX_NOACTIVATE` and **not** `WS_EX_TRANSPARENT` for it,
  places it above every ordinary window in its own z-order walk, routes a hit test at the target's
  own centre **to** it (`WindowFromPoint` — the exact inverse of the overlay class), and reports the SAME
  foreground window before and after the operation, which is never the target. All six are read back
  through an instrument INDEPENDENT of the code that created the window. Satisfiable inside
  `dotnet test` on a Windows box with an interactive desktop (`PointerCapabilityTests`); on a box
  without one every leg goes false together and the assertions still bite. **This is what the
  product's `Available` rests on and it is the whole of it.**
- **`click-delivered`** — the strongest automated pointer claim this port can make, and it is three
  measurements taken together: a click **synthesised at OS level** (`SendInput`, absolute,
  virtual-desktop normalised) arrived as **`WM_LBUTTONDOWN` and `WM_LBUTTONUP` in that target's own
  window procedure**; that procedure received **`WM_MOUSEACTIVATE` and answered `MA_NOACTIVATE`**
  during the same click; and `GetForegroundWindow()` is **byte-identical either side of it**. The
  negative leg is what stops the first from passing vacuously, and it is deliberately not "the count
  did not move": a probe-owned decoy is placed under the point and asserted to have CAUGHT the click,
  so "it did not arrive here" cannot be satisfied by an injection the OS refused.
- **`popped-verified`** — **a human clicked a bubble.** NOT satisfiable by any automated step on any
  platform, Windows included. It is a manual gate and it is the only thing that closes the last link.

**A `click-delivered` green is never a `popped-verified` green,** and it is not even a claim about
what the OS will do next. It proves the operating system routed one click to this window at one
instant while leaving the foreground alone. It says nothing about a person: every click in the suite
is injected, and injection is refused outright by UIPI against a higher-integrity window, by the
secure desktop, and by a locked workstation, all of which the fixture detects rather than skips. It
also says nothing about whether a bubble is **visible or aimable**: the ink read-back is the OS's own
`GetPixel` over the target's client area against a control point in a margin the painter fills, which
proves the OS holds a drawn disc — not that it is on a screen anybody is looking at, at a size anybody
could hit. That remains `presentation-verified` and is undischarged for this surface.

**What tier 1 cannot cover for a pointer target**, as its own list:

1. **Nobody clicked anything.** No tier discharges `popped-verified`.
2. **`Available` cannot include the activation refusal, and that is the chain's sharpest stopping
   point.** `WM_MOUSEACTIVATE` exists only when a click really happens, and the product must never
   synthesise a click — a capability that pressed the mouse to check it could receive presses would
   be clicking whatever the user really had under the cursor. So the product claims the STYLE
   (`WS_EX_NOACTIVATE`, read back out of the OS) plus "the foreground is not me and did not move",
   and the message-level refusal is evidence a FACT reads, never an input to a claim.
3. **The routing answer is momentary, and a FOREIGN topmost window can own the point.** The
   shipping WPF product was measured doing exactly that on this machine. Re-asserting the band is
   bounded and then *asked about*; it is never trusted, and it cannot exclude a screen locker, a
   full-screen game or a magnifier.
4. **The MOVING-TARGET race is bounded rather than eliminated, and the bound is arithmetic.** The
   product never hit-tests and then acts on the answer — each target is its own top-level window, so
   the arbiter at click time is the window manager over the position the OS itself holds — but a
   caller's belief about routing can be one animation step old. One step moves a bubble at most
   `sqrt(12² + 4.5²) = 12.81` px (`BubbleService.cs:54`, `:2823`, `:2831-2834`, `:3460-3463`) against
   a smallest legal radius of 30 px (`BubbleSizing.cs:70`), so the target's own centre never leaves
   the target in one step. Both directions are MEASURED with real windows and real clicks
   (`PointerCapabilityTests`: one step later the click still arrives; more than a side later it does
   not, and the decoy proves it went elsewhere). **What is NOT covered: a move interleaved with the
   OS's own delivery of a click already in flight.** The delivery facts hold the field still between
   the injection and the pump, which is stated plainly rather than hidden; no fixture can pin that
   window without a wall-clock wait, which is banned.
5. **The host's own message loop is not proven to deliver mouse messages for this window.** The
   product's pump is Avalonia's; the evidence here is taken on the surface's own bounded pump. Same
   residue, same shape, as the input class's item 3.

**Linux pointer targets are `pointer-routed`-INCAPABLE in this build**, and refuse in type rather
than claiming anything: `PointerSurfaceFactory` returns a typed refusal carrying its own five-step
manual gate (`Pointer/PointerSurfaceFactory.LinuxManualGate`), run separately on X11 and on Wayland
because they answer differently. **The Linux route is not the overlay's route with the polarity
flipped.** X11 gives a client one direct mechanism for click-*through* (an empty
`XFixesSetWindowShapeRegion` on `ShapeInput`) and NO single mechanism for "receive the button press
without the click raising or focusing me": that is the window manager's click-to-focus policy, and
the nearest a client gets is `_NET_WM_WINDOW_TYPE_DOCK` plus `WM_HINTS.input = False` plus
override-redirect — three hints, honoured differently by Mutter, KWin and wlroots. The gate names
`_NET_ACTIVE_WINDOW` being unchanged across a click as the step most likely to fail. Under a native
Wayland compositor the non-activation half is the one part the protocol gets right by default (a
client cannot take activation at all without an `xdg-activation-v1` token minted from real user
interaction) and that is precisely why the rest is unavailable: the compositor owns placement,
stacking and focus policy together.

## Glyph evidence class — per-pixel alpha

The port's other drawing capabilities composite at ONE uniform opacity over an opaque frame. This one
composites **per-pixel alpha**, and alpha is the property most easily faked: a window can be layered,
present, topmost and composite **nothing** — or composite a black rectangle over the user's desktop,
which is worse than absent. So this capability's evidence is classed more finely than the others'.

| class | what it claims | instrument | conditional on |
|---|---|---|---|
| `glyph-surface-held` | the OS holds the window: it exists, is visible, carries exactly the requested rectangle and every extended style, sits above every ordinary window in the OS's own z-order, routes its own centre point the way the request asked in BOTH polarities, and never took the foreground | `IsWindow` / `IsWindowVisible` / `GetWindowRect` / `GetWindowLongPtr` / `GetTopWindow`+`GetWindow` / `WindowFromPoint` / `GetForegroundWindow` | an interactive desktop |
| `glyph-composited` | **the operating system's own copy of the surface carries the frame**: every fully-opaque ink point reads back exactly its own colour and every fully-transparent sample reads back nothing | `PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT)` into a bitmap the caller owns | an interactive desktop |
| `glyph-desktop-composited` | **a fully transparent pixel shows the background BEHIND it, an opaque black pixel does NOT, a glyph pixel is its own colour, and a partial alpha is exact premultiplied source-over** — at the two uniform-opacity settings named below, and at those only | `BitBlt(SRCCOPY \| CAPTUREBLT)` from the screen DC over a known background, DPI-mapped, with a control capture taken with the surface hidden, at uniform opacity **1.0 and 0.5** | an interactive desktop **and** the occlusion arbitration winning |
| `watched-verified` | a human saw a word bounce | a person | **undischarged, and no automated step on any platform discharges it** |

### The negative control is the class, not a footnote

`glyph-composited` is worth nothing unless a window that composites NOTHING answers the same question
differently, so the instrument builds that window on **every suite run** and measures both arms:

- layered, shown, `UpdateLayeredWindow` never called: **0 non-zero pixels of 14400**;
- the same handle after ONE composite: **0 mismatches over 10 colours**, on the first call after the
  show and on every call after it.

The same control re-measures the overlay class's recorded hazard rather than quoting it: a window holding uniform
layered attributes REFUSES a per-pixel composite (`FALSE`, last-error 87, uniform alpha intact), and
clearing `WS_EX_LAYERED` and restoring it lets one through and destroys the uniform read-back for good.

### The boundary `glyph-composited` cannot cross, measured

A window read-back flattens the surface onto black, so **a fully transparent pixel and an opaque BLACK
pixel are the same value there**. No reading of it can separate them. That separation is
`glyph-desktop-composited`'s alone, and it is why this capability has a fourth class where the others
have three.

Partial-alpha values are **not** asserted in `glyph-composited`. They were measured stable across
repeated calls and ALSO measured once at half the expected value on a window whose device context had
been read with `GetDC` + `BitBlt` first, so the product anchors on alpha 255 and alpha 0 only and
never calls `GetDC` on its own surface. Partial alpha is asserted in `glyph-desktop-composited`.

### The oracle for `glyph-desktop-composited`, and the exact size of its claim

The predicted value is `GlyphFrame.CompositeOver`: premultiplied source-over,
`src·k/255 + dst·(255−α)/255` with `α = a·k/255`, evaluated over a common denominator and **rounded
to nearest exactly once, per channel, at the end**. It is asserted with **no tolerance** and it holds
exactly at **every** sampled point at both measured settings of the uniform dial.

**The single rounding is load-bearing and was got wrong first.** An earlier draft rounded each of the
three terms separately; it reproduced seven of the eight measured values and was one unit low in the
red channel of the eighth, and that miss was absorbed with a `±1` allowance. **A `±1` allowance on
this class is not a safety margin — it is exactly the size of the defect the class exists to catch**,
so it would have silently passed a one-unit-per-channel regression at every non-255 dial setting. The
formula was never wrong; the number of roundings was.

**What is measured, and what is not.** Uniform opacity has been read off a screen at **1.0 and 0.5
only**. Every other setting of the dial is this arithmetic, unchecked against any display. Nothing in
this class measures cadence, order or timing.

### Occlusion arbitration — what replaces "the rectangles are disjoint"

The pointer class's final review recorded that the coexistence argument does not scale past four disjoint
rectangles. It does not apply to this capability AT ALL: proving that a transparent pixel shows the
background behind it requires the surface to be placed OVER that background, so **the overlap IS the
evidence**.

Ownership of a sampled point is therefore DECIDED rather than assumed. The z-order is walked from the
top; every visible window strictly between the surface and its background is fetched with
`GetWindowRect` and tested for intersection with the sampled area; the pair is re-raised a bounded
number of times (a COUNT, never a wall-clock wait) until nobody is between them. A run that never wins
reports **who** owns the region, by class name and rectangle, instead of reading a pixel that belongs
to somebody else.

Both arms were observed while the capability was built: with the two raises adjacent the list is
empty, and with an ordinary interval between them the shipping WPF product sat in the gap and the
sampled "background" pixels were its own.

### What this class does NOT cover

- **Any human.** `watched-verified` is undischarged and is not dischargeable by this suite.
- **A monitor.** `glyph-composited` is an OS query about pixels the OS holds FOR A WINDOW, measured by
  the video class not to be monitor-aware; `glyph-desktop-composited` is a screen read from inside the process,
  not a photograph, and cannot see a Magnifier, a mirror driver, an exclusive-fullscreen swap chain or
  a physically dark monitor.
- **Cadence, order or timing.** Every frame advance in every fact is driven by hand on the injected
  clock. A logo that moved at half speed, or backwards, satisfies every check.
- **Anything on Linux.** The five-step gate in `GlyphSurfaceFactory.LinuxManualGate` is undischarged,
  and its step 5 is expected to be impossible under Wayland, where a client cannot read back the
  composited output at all — so the honest Wayland outcome is that the PROOF is unavailable even where
  the picture works.

## Haptic evidence class

Haptics needs its own class, and it is the FIRST class in this document whose top rung is not on this
machine at all.

Every other capability here fails somewhere the port can look: a window the OS will not place, an
endpoint the mixer will not meter, a click the window manager routes elsewhere. **A haptic sink's
output leaves this process, leaves this machine's API surface, and ends in a motor.** Upstream's two
providers are clients of a separate server the user installs — Buttplug over a WebSocket to
`ws://127.0.0.1:12345` (`Services/Haptics/ButtplugProvider.cs:27,83`), Lovense over HTTP to
`http://127.0.0.1:20010` (`Services/Haptics/LovenseProvider.cs:21,83,89`) — and that server talks
Bluetooth to hardware neither it nor this process can interrogate. So the ladder has a rung that no
amount of engineering closes.

### The four classes, and where the chain stops

| class | what it means | who can produce it |
|---|---|---|
| **`haptic-admitted`** | this build has a provider CLIENT at all — a route in `HapticSinkFactory.AdmittedRoutes` | **This build, since the Lovense route was admitted.** The list carries Lovense and a real `LovenseHapticSink` is constructed for it; Buttplug has a sink but is NOT in the list. This class is a property of the BUILD, identical on Windows and Linux, and it is the only one of the four this build has reached |
| **`haptic-server-answered`** | the separate server process answered a real request over loopback | a build with an admitted client, plus Intiface Central / Lovense Connect running |
| **`haptic-device-named`** | the server answered and named at least one device this process can address. **This — and only this — is what `CapabilityState.Available` may rest on** (`HapticServerObservation.Confirmed`) | the above, plus a paired toy |
| **`felt-verified`** | a human reports that a device really moved, and that it STOPPED when the all-stop ran | **a HUMAN, and nothing else, on any platform, at any depth of API** |

**`felt-verified` is not merely undischarged; it is undischargeable by code.** A haptic server reports
what it believes it commanded over Bluetooth. Neither this process nor the shipping WPF app can
distinguish a toy that vibrated from one with a flat battery in the next room, from one whose motor
has failed, or from a server that acknowledged a command it never sent. There is no counterpart here
to the audio class's `render-metered` — no OS-side meter reads back what the device did, because the
device is not the OS's.

**The STOP half of `felt-verified` is the half that matters and it is easy to forget.** Upstream's own
shutdown comment is the reason: *"a Lovense level has no server-side watchdog, so a toy we don't
countermand keeps running after the app is gone"* (`ConditioningControlPanel/App.xaml.cs:4401-4404`).
A haptic check that only ever confirms motion would pass on a build that can start a device and not
stop one.

### What tier 1 cannot cover for haptics

1. **That any client exists.** The port's automated facts today are all about a REFUSAL, and a refusal
   is cheap to be right about. Nothing in the suite has ever spoken either wire protocol.
2. **That a level arrived.** Even with a client, `SetOutputsAsync` returning `Available` would mean a
   server acknowledged a request — one process telling another it received a message.
3. **That a stop arrived.** Same ceiling, and worse consequences.
4. **Anything about latency or cadence.** Both providers throttle or quantize in ways the seam
   deliberately does not model: Lovense drops commands inside 200 ms in continuous mode
   (`LovenseProvider.cs:207-219`) and floors LAN durations at a whole second (`:232-233`).

### The manual gate

`Haptics.HapticSinkFactory.DeviceManualGate` carries the four steps verbatim, so the gate travels with
the refusal rather than living only in a document. **Its first line says it cannot be attempted until
a provider client is admitted** — which is deliberate: quoting the device gate today would send a
reader to fix something that is not what is wrong.
