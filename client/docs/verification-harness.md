# Tiered targeted verification harness

**Status:** active deliverable of task-board row 7 (SP-008). Replaces the rejected first-attempt whole-app smoke/layer strategy (`first-attempt-lessons.md`). Owner decisions applied: A-012 (targeted checks over blanket sweeps), A-014 (checks exist only with a real current consumer), `runtime-capability-contract.md` evidence-class discipline extended to test evidence.

## The four tiers

### Tier 1 — fast affected checks (never launches the app)

Build + unit tests + headless tests. Runs on every iteration; the only tier that runs unconditionally.

- **"Affected" is defined concretely by csproj path**, matching how the contract testCommand narrows:
  - `dotnet build client/CcpClient.sln -c Debug --nologo` — the solution build IS the affected-build check (the solution contains only projects that exist; there is no wider build to narrow).
  - `dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` — pure unit tests (no Avalonia runtime, no app launch). Assertion-logic unit tests for the tier-3 console tool live here.
  - `dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` — headless Avalonia tests (in-memory windowing/rendering; no real display, no app launch).
- Tier selection is by csproj path: a task touches only logic → run `CcpClient.Tests`; touches AXAML/visual tree → also run `CcpClient.HeadlessTests`. The full contract testCommand (all three commands) is the pre-`.DONE` gate.
- The headless tests live in a SEPARATE project because `[assembly: AvaloniaTestApplication]` is assembly-wide; putting them in `CcpClient.Tests` would force an Avalonia application onto the 85 landed unit tests.

### Tier 2 — targetable one-surface/state capture + headed actions (task close)

Thin scripts under `client/tools/verify/` formalizing the SP-007 headed-smoke patterns. Launch the real app, raise it (`SetWindowPos(HWND_TOPMOST)` on Windows — the app opens unactivated behind existing windows and pixel captures read the occluder, SP-007 surprise #2), read UIA/layout-probe facts, drive real input, capture ONE surface+state by name:

```
pwsh client/tools/verify/capture.ps1 -Surface rail-door -State selected
pwsh client/tools/verify/capture.ps1 -Surface rail-door -State unselected
pwsh client/tools/verify/capture.ps1 -Surface dashboard -State unselected
```

Surfaces are NAMED capture scopes, not app screens: `rail-door` = a navigation rail door rect taken from the app's own layout probe (Avalonia exposes no UIA peers for Border/Grid/StackPanel — SP-007 surprise #1); `dashboard` = the whole window. **Re-anchored at SP-091** from `dashboard-card`/`lit`/`unlit`, which named the retired `demo.status-ticker` demonstrator card. State drives differ per platform, honestly: **Windows** drives `selected` through REAL input (a left-click on a rail door, the route confirmed by a UIA read before any pixel is captured — the same user path a regression would break); **WSLg** needs no drive at all now, because on cold start the Studio door is already `:checked` and the Companion door is not, so both states are capturable with zero input. That is strictly better than the old WSLg path, which pre-seeded a settings file: WSLg has no input automation (SP-007 named gate), and the new anchor removes the need for one rather than working around it. Interaction evidence stays Windows-headed; WSLg captures are render/scale/session-facts only.

- Windows: real window + `CopyFromScreen` crop to the layout-probe rect → PNG. (System.Drawing appears here ONLY as capture transport; scripts never read a pixel — SP-008 consult.)
- WSLg (Linux/X11): `XGetImage` via python3 ctypes against the app's X window (`capture-wslg.sh` + `xgetimage.py`) → BMP. WSLg RAIL windows are invisible to Windows-side GDI capture (SP-007 surprise #3).
- Headed ACTIONS (right-click quick-toggle, keyboard, teardown) beyond what a state needs remain task-specific scripts building on the same helpers; the harness owns launch/raise/read/capture, the task owns its action sequence.
- Output: `client/tools/verify/artifacts/<platform>-<surface>-<state>.png|bmp` (gitignored, in-scope .gitignore).

### Tier 3 — K3 image inspection driven by the named-check manifest

Deterministic named checks BEFORE/DESIDE model review: one cross-platform .NET console tool (`client/tools/verify/CcpVerify`) reads the manifest (`client/tools/verify/checks.json`), decodes a capture via `Avalonia.Media.Imaging.Bitmap` (no System.Drawing, zero new packages), evaluates each named check for the captured surface+state, and exits non-zero naming the FIRST failed check. K3 (`app-visual-verification` skill) then reviews the same capture against the same manifest — the manifest row is the shared contract between deterministic assertion and model review.

- Checks are scoped to REAL current consumers only: the SP-007 dashboard card lit/unlit border bands and the dashboard background. The capability surface is verified by TIER-2 UIA TEXT NEEDLES inside `capture.ps1` (`capability display-session: Available` etc.) — pixel checks cannot read text, so no manifest check claims it. No speculative checks for surfaces that do not exist (A-014). Each new surface adds its checks with its own task.

### Tier 4 — theme/language/platform matrices (named milestones/releases ONLY)

Broader matrices (five themes, languages, scaling levels, platforms) run ONLY at a named milestone or release gate. This task defines the hook and does NOT run matrices. Trigger: the board row for a milestone/release names the matrix; the capture tool's `-Surface`/`-State` parameterization plus the manifest's per-surface check list are the execution mechanism. No matrix automation exists beyond this hook (A-014).

## Evidence-class rule (hard)

Every check in the manifest declares an evidence class:

- **`draw-verified`** — the check asserts facts about the visual tree, styles, bindings, or Skia draw output. Headless tests (`CcpClient.HeadlessTests`) may satisfy ONLY draw-verified assertions: the headless platform replaces windowing AND rendering with in-memory backends (default: fake drawing, no pixels at all; with `UseHeadlessDrawing=false` + `.UseSkia()`: Skia draw output). A headless frame has NO compositor, NO real window, NO DPI/scaling, NO activation/occlusion, NO OS chrome.
- **`presentation-verified`** — the check asserts what a user sees on a real display: composited pixels, window geometry, scaling, occlusion, z-order. ONLY a headed Windows/WSLg capture (tier 2) can satisfy presentation-verified checks.

**A headed Windows/WSLg gate is NEVER dischargeable by a headless frame.** All current manifest checks against real captures are `presentation-verified`; `draw-verified` checks appear when a task adds headless-frame assertions (none exist yet — the headless spike asserts tree/layout/style/binding facts, not frames).

## Audio evidence class (SP-109)

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

## Input evidence class (SP-110)

Input needs its own class for the mirror of SP-099's reason. That packet proved a surface is
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
> (`spine-tasks/SP-110-input-capturing-window/plan.md` §0, run 1).

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

## What tier 1 does NOT cover on the real desktop (SP-107, admitted rather than hidden)

Some tier-1 facts are not headless. `OverlayCapabilityTests` (SP-099), `FlashDrawTests` (SP-100) and `TrayCapabilityTests` (SP-093) put REAL windows on the developer's REAL desktop inside `dotnet test` and ask the operating system about them: presence, z-order, click-through routing, layered alpha, composited pixels. That is deliberate and it is the port's only OS-level evidence for those capabilities. It also means those facts share a resource with everything else on the machine, so the boundary has to be stated.

**Covered, and mechanically enforced.** Those three fixtures live in one xunit collection, `RealDesktopCollection`, which holds an exclusive machine-wide lease (`%TEMP%/ccp-real-desktop.lease`) for as long as it runs. So no two of them contend inside a process, and **no two `check-floor.mjs` runs contend across processes** — which matters because `client/tools/gate/with-slot.mjs --slots 3` permits three concurrent gate runs by design. `RealDesktopCollectionGuardTests` fails the suite when a class that touches the desktop is not in the collection, and `CcpClient.HeadlessTests` may carry no real-desktop probe at all, because collections do not span assemblies and nothing over there could hold the lease.

**Measured, in aggregate, and not selectively.** Before: **0 red in 20** sequential floor runs, **8 red in 12** with three concurrent. After: **2 red in 36** with three concurrent (two rounds of 18: 2 red then 0 red) and **2 red in 40** sequential (two rounds of 20: 2 red then 0 red) — **4 red in 76 runs overall, 5.3 %** (SP-107 record §0 and §3). Every one of the three cross-process collisions is gone from all 76 runs. **Provenance, because it changes what the figure licenses:** those 76 runs were taken at `737fa739`/`e068d50f`/`87752586`, not at the landed head — the shipped lease grants read sharing and writes its pid so the holder is nameable, whereas the measured one opened `FileShare.None`. The exclusion those runs measured is unchanged and is pinned PER RUN by `RealDesktopLeaseTests`, so every run carries its own proof that exclusion held during it; the shipped mechanism was not itself re-baselined over 76 runs.

**So the gate is better, not deterministic, and the difference matters to how you read a green.** A green floor run now means "no two test processes fought over the desktop", which it did not mean before. It does not yet mean "green implies the tree is green": a residual ~5 % intermittent survives on one fact, `FlashDrawTests.ARealImageFile_ReachesTheCompositedDesktop_AndLeavesItWhenTheFlashIsHidden`, where the OS confirms the surface visible, topmost and holding the painted frame while a full desktop read comes back with none of its colour. No concurrency dependence is VISIBLE at these counts (2 in 36 concurrent versus 2 in 40 sequential), which is four events and far too few to establish independence, and its mechanism is unresolved — the candidates and the instrumentation pointed at them are SP-107 record §4. **Three consecutive greens are worth far more than they were and are still not proof.**

**NOT covered, and no in-process mechanism can cover it.** A FOREIGN topmost window can still own a point on the real desktop while these facts run:

- the shipping WPF product re-asserting `HWND_TOPMOST` on a cadence (`Services/Flash/FlashService.cs:206-243`) — measured as the window that won the point while SP-099 was being written;
- a locked workstation, a screen saver, a UAC secure desktop, a full-screen exclusive game, Magnifier, a mirror driver, or an RDP session;
- any other application that raises itself over the test's rectangle at the moment the hit test is asked.

When that happens the facts **fail loudly**, and how much they can say differs by refusal: `OverlayInputNotReceived` names the winning window's class (it has a handle from `WindowFromPoint` to ask about), while `OverlayNotOnTop` can only report z-order indices, because a z-order walk that finds the surface below an ordinary window has no single culprit to name. Either way the port would rather see a red it can read than a green it cannot trust. None of these are `allowedSkips` candidates — that list is for properties of the MACHINE, and "something else was on top just then" is a property of the MOMENT. **The floor therefore claims exclusivity against other test processes and claims nothing at all against the rest of the desktop.** Sustained topmost under real contention, multi-monitor placement, cross-DPI behaviour and delivered (rather than routed) input remain tier-2 headed claims and the named manual gates in the SP-093/SP-099/SP-100 records.

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
- `region` — **capture-relative, never absolute pixels** (captures differ across platforms: Windows scale 1.0, WSLg scale 1.5, card 77 vs 71 DIP — SP-007 measured font-metric delta). Either `{ "band": "top|bottom|left|right", "thicknessPx": N }` (first N pixel rows/columns of the capture) or `{ "rect": { "x": 0.0, "y": 0.0, "w": 1.0, "h": 0.05 } }` (fractions of capture width/height).
- `expectedColor` — `#RRGGBB`; `tolerance` — per-channel absolute delta; `minPixelFraction` — pass criterion: fraction (0..1) of sampled region pixels within tolerance. Fraction-based, not absolute counts, so one manifest is valid at Windows scale 1.0 AND WSLg scale 1.5 (SP-008 consult).

## Seeded-regression self-test

Re-runnable proof that the targeted gate catches real regressions (throwaway-edit pattern, SP-007 AVLN2000 precedent; NO defect-injection flags in product code):

```
pwsh client/tools/verify/self-test.ps1
```

Sequence: (1) edit the REAL `MainWindow.axaml` — break the selected-door border brush (`#E066FF` → wrong value); (2) build; (3) capture `dashboard`/`selected`; (4) assert the SPECIFIC named check `rail-door-selected-border` fails with its name in the output; (5) restore the AXAML (git checkout); (6) re-capture, assert green. A self-test pass requires BOTH the seeded failure AND the restored green.

## Runtime budgets (measured, never invented)

Measured 2026-07-19 (SP-008 Step 4); budget = observed + stated headroom. Cold = clean `bin/obj` + first run; incremental = no source change since last build. Machines: Windows .NET SDK 10.0.302; WSL2 Ubuntu 26.04 SDK 10.0.110 (native-dir copy, never /mnt/e).

| Tier | Windows cold | Windows incremental | WSL2 cold | WSL2 incremental | Budget |
|---|---|---|---|---|---|
| Tier 1 (build + both test projects) | 12.9 s (2.3 build + 6.1 + 4.4 tests) | 8.3 s | 14.6 s (7.5 build + 4.6 + 2.5 tests) | 9.0 s | **60 s** |
| Tier 2 (launch + capture one state) | 5.5 s | same (always a fresh launch) | 6.0 s (WSLg XGetImage) | same | **30 s** |
| Tier 3 (console assert one capture) | 0.4 s | — | 0.3 s | — | **5 s** |
| Self-test (full cycle: seed/build/capture/fail/restore/green) | 23 s | — | not applicable (Windows PS path) | — | **120 s** |

Headroom rationale: ~4x on tier 1 (dominated by NuGet/SDK variance on cold CI-like machines), ~5x on tiers 2–3, ~5x on the self-test (two builds + two captures + two assertions). Re-measure when a tier adds a project or a surface.

## K3 integration

The manifest is the shared contract between deterministic assertion and model review (`app-visual-verification` skill, exact model `kimi-coding/k3`): the review prompt names the manifest checks (name, region, property, tolerance) and asks K3 only for what pixels-with-tolerance cannot see (clipping, truncation, state pairing, glow bleed, contrast). First run recorded in `spine-tasks/SP-008-verification-harness/record.md`: card lit+unlit captures, VERDICT PASS. Interaction gates K3 cannot discharge (toggle behavior, tick advance) are covered by the tier-2 state drive (real right-click + tick-advance verification in `capture.ps1`), never by stills.

## Tier-4 milestone hook

Matrices (five themes, languages, scaling levels, platform pairs) run ONLY when a task-board row names a milestone/release. The hook: `capture.ps1`/`capture-wslg.sh` take `-Surface`/`-State`; a milestone row adds its matrix as a list of surface/state invocations plus matching manifest entries. No matrix automation exists beyond this hook (A-014); do not run matrices outside a named milestone.

## Video evidence class (SP-111)

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
already names that residue. It is not hypothetical: while SP-111 was being written the SHIPPING WPF
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
   the machine SP-111 was written on — neither did the `Z:` volume — so `TestAvi` writes an
   uncompressed 32bpp AVI in pure managed code and Media Foundation opens it as
   `MFVideoFormat_RGB32`.

   **The substitution is BOUNDED, and the boundary is the useful part.** The DISPLAY half is fed
   pictures and cannot tell where they came from, so `frame-on-surface` and `desktop-composited`
   are unaffected: a synthetic frame exercises the surface exactly as a decoded one does. **The
   cost falls entirely on `clip-decodable`**, and it is precisely two things — the source
   reader's video PROCESSOR is never exercised (the fixture is natively RGB32, so no conversion
   is ever required), and the format set the port can actually open is untested against anything
   a user owns (D124: the playable set becomes what Windows can open rather than what LibVLC
   can). Both are carried as NAMED uncovered mutation survivors, `M-y` and `M-w`, in
   `spine-tasks/SP-111-video-capability/record.md` §4.

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
