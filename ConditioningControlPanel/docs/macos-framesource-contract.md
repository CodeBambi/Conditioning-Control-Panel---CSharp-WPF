# macOS IFrameSource Implementation Contract

**Date:** 2026-07-12  
**Scope:** Hard-seam macOS screen capture (`IFrameSource`)  
**Status:** Authoritative design contract (read-only research artifact; no code changes)  
**Closes:** macOS side of IFrameSource bring-up (readiness map leverage item #2)  
**Siblings:** `linux-framesource-contract.md` (template + shared rules), `macos-overlay-contract.md` (shared interop + CI lane), `macos-foreground-title-contract.md` (shared TCC policy + grant synergy)

This document specifies the behavior contract, backend architecture, and implementation
slices for macOS screen capture (`IFrameSource`). Per the readiness-map governing
principle: multi-backend, runtime-selected, graceful fallback, CI-verified. On macOS the
backend split is **modern-vs-legacy API generation** (ScreenCaptureKit vs CoreGraphics
one-shot), both gated by the SAME TCC Screen Recording permission — so unlike Linux
there is no prompt-free path at all; the TCC policy (§4) is therefore the contract's
judgment core.

> **Recommendation up front (the compare the prompt asked for):** **ScreenCaptureKit
> (macOS 12.3+) is the primary target-state backend; the bring-up backend and fallback
> is `CGDisplayCreateImage` — NOT `CGDisplayStream`.** CGDisplayStream is a push-model
> continuous-callback API: equally deprecated (macOS 14 SDK), harder ObjC-block interop,
> and a worse fit for a pull-based `CaptureAsync` seam than the synchronous, plain-C,
> trivially P/Invokable `CGDisplayCreateImage`. Both CG paths and SCK are gated by the
> same TCC grant, so the fallback buys API-simplicity and OS-version reach (12.0-12.2),
> not permission avoidance. Claims tagged **[confidence: …]** feed the driver's web pass
> (§7.1).

---

## 1. IFrameSource Behavior Contract

The `IFrameSource` interface (`CCP.Core/Platform/IFrameSource.cs`) is UNCHANGED:

```csharp
public interface IFrameSource
{
    Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default);
}

public sealed record RawFrame(int Width, int Height, byte[] BgraData);
```

`ScreenInfo` (`CCP.Core/Platform/IScreenInfo.cs:10`) is
`record ScreenInfo(string Name, PixelRect Bounds, PixelRect WorkingArea, double Scaling)`.

### 1.1 Behavioral Requirements (cited from `CCP.Avalonia.Desktop.Windows/WindowsFrameSource.cs`)

| Requirement | Windows Implementation | Citation | macOS disposition |
|---|---|---|---|
| **Per-screen capture** | Captures `ScreenInfo.Bounds` (absolute virtual-desktop pixels) | `WindowsFrameSource.cs:25-29,34` | `ScreenInfo` → `CGDirectDisplayID` mapping (§3.3); whole-display capture (the seam's consumers are per-screen) |
| **BGRA 32-bit format** | `Format32bppArgb` bitmap, `LockBits` copy | `WindowsFrameSource.cs:31,40` | CGImage/SCK surface → BGRA repack via `CGBitmapContext` with `kCGImageAlphaPremultipliedFirst \| kCGBitmapByteOrder32Little` (§3.2) |
| **Tightly packed rows** | `BgraData.Length == Width*Height*4`, no padding | `WindowsFrameSource.cs:43-46` (stride==width*4 for 32bpp) | Native `bytesPerRow` may be padded/page-aligned — MUST repack tight (Linux §1.2 normative rule inherited) |
| **Synchronous capture** | GDI blit + `Task.FromResult` | `WindowsFrameSource.cs:34,46` | CG backend: synchronous C call. SCK backend: async under the hood; `CaptureAsync` is already a Task — fine |
| **Cancellation honored** | `ThrowIfCancellationRequested` before/after blit | `WindowsFrameSource.cs:23,37` | Same check points; SCK path also cancels its await |
| **Minimum 1x1 dimension** | `Math.Max(1, …)` clamps | `WindowsFrameSource.cs:28-29` | Same clamps |
| **No persistence** | Frames memory-only | implicit; normative in §1.3 | Same hard line |

**RawFrame packing contract (normative, inherited from Linux §1.2):** tightly packed
BGRA, row-major, `Width*Height*4` bytes, opaque alpha acceptable. **Retina rule:**
capture returns BACKING pixels — a "1920x1080" Retina display yields a 3840x2160 frame.
Return the *captured* pixel dimensions in `RawFrame`; consumers already index by
`RawFrame.Width/Height` (Linux fractional-scaling ruling, same resolution).

### 1.2 Consumers and Cadence (inherited verbatim from Linux §1.3)

Wired consumers: `LabTabViewModel.cs:39,73`, `WebcamCalibrationWindow.axaml.cs:59,130`,
`WebcamGazeTrackerWindow.axaml.cs:30,40`, `WebcamQuickRecalWindow.axaml.cs:35,50`.
Planned: AvatarTube live-screen mirror, screen-derived effects, screen OCR triggers.

**Cadence contract:** correct for one-shot calls with no mandatory warm-up between
calls; sustains ~10-15 FPS preview at 1080p. A slow first call is allowed (SCK stream
start-up); subsequent calls must be cheap. `CaptureAsync` callable from any thread;
backends serialize internally.

### 1.3 Privacy Hard-Line (normative, inherited from Linux §1.4)

- **Raw frames are memory-only.** Never disk, never network, never logged.
- Derived non-reconstructable data (OCR hits, average colors, calibration
  coefficients) may flow per each consumer's own contract.
- macOS has NO restore-token analogue to persist — the TCC grant itself is the
  persistent authorization, held by the OS, not by us. We persist NOTHING for this
  seam (simpler than Linux).
- Violations are security bugs of webcam-rule severity.

---

## 2. macOS Backend Architecture

### 2.1 Runtime Backend Selection

One window system; selection keys on **TCC grant state** (shared
`MacOSPermissionService`, see title contract §5) and **OS version / API availability**:

```
MacOSFrameSourceBackendSelector (at first capture or DI init; re-probed on grant events)
  1. CGPreflightScreenCaptureAccess() == false
       → FallbackFrameSource (black frame; NEVER triggers a prompt from the seam — §4)
  2. Granted AND macOS ≥ 12.3 AND ScreenCaptureKit loadable
       → ScreenCaptureKitBackend      (target-state primary)
  3. Granted (any supported macOS)
       → CGDisplayImageBackend        (bring-up + fallback; deprecated-but-functional)
  4. Native failure cascade at runtime
       → FallbackFrameSource
```

Until the SCK slice lands (Slice D), arm 2 routes to arm 3 — the selector shape ships
in Slice A so SCK later lands as a pure upgrade (Linux Slice-A convention).
OS-version probe: `NSProcessInfo.operatingSystemVersion` via ObjC interop; SCK
loadability probe: `dlopen` of the framework + class lookup, wrapped `try/catch` —
never assume from version alone (Linux registry-probe-is-authoritative rule).

### 2.2 Backend Fallback Chain

| Priority | Backend | Capabilities | When Selected |
|---|---|---|---|
| 1 | `ScreenCaptureKitBackend` | Modern: GPU-efficient stream, per-display content filter, excluded-windows support, sustained-FPS-friendly | Granted + macOS ≥ 12.3 + framework probe OK (Slice D onward) |
| 2 | `CGDisplayImageBackend` | Universal one-shot: `CGDisplayCreateImage(displayId)` — synchronous, plain C, deprecated in the macOS 14 SDK wave but functional. **VERIFIED (§7.1 row 2), with a Sequoia caveat: on macOS 15+, apps using deprecated CG capture APIs can trigger EXTRA system warning alerts ("can collect detailed information about the user") on top of the monthly re-auth — reinforces SCK as the primary and CG as bring-up/fallback only** | Granted; SCK unavailable or its slice not yet landed |
| 3 | `FallbackFrameSource` | Black frame + reason logged once | Ungranted, or all probes/backends fail |

**Rejected: `CGDisplayStream`** — push-model continuous callback (dispatch-queue +
ObjC blocks), deprecated in the same SDK wave as the other CG capture APIs, and its
continuous model buys nothing for a pull seam that `SCStream` doesn't do better on
modern OSes. Two backends + fallback beat three where the third adds interop risk and
zero capability. (Documented so nobody "helpfully" adds it — Linux §4.3 parking
convention.)

**Guarantee:** something always returns. Never crash; never prompt from the seam;
degrade to a black frame with a reason logged exactly once.

### 2.3 Seam Structure

```
CCP.Core/Platform/
└── IFrameSource.cs                          # UNCHANGED

CCP.Avalonia.Desktop.macOS/Platform/
├── MacOSFrameSource.cs                      # IFrameSource impl delegating to backend
├── MacOSFrameSourceBackendSelector.cs
├── FrameSourceBackends/
│   ├── IMacOSFrameSourceBackend.cs          # + IDisposable
│   ├── ScreenCaptureKitBackend.cs
│   ├── CGDisplayImageBackend.cs
│   └── FallbackFrameSource.cs
├── MacOSPermissionService.cs                # SHARED with title contract (§4; one impl per process)
└── Interop/
    ├── ObjCInterop.cs                       # SHARED (overlay contract §2.4) — extend, do not fork
    ├── CoreGraphicsInterop.cs               # SHARED with title contract: CGDisplay*, CGImage,
    │                                        #   CGBitmapContext, CFRelease, CGPreflight/Request
    └── ScreenCaptureKitInterop.cs           # NEW: SCK ObjC bindings incl. block trampolines (§3.4)
```

`ObjCInterop`, `CoreGraphicsInterop`, and `MacOSPermissionService` are SHARED
infrastructure with the sibling macOS contracts — whichever slice lands first builds
each; the others extend, never fork.

### 2.4 Threading model

- `CGDisplayCreateImage`, `CGBitmapContext*`, `CGGetActiveDisplayList`,
  `CGDisplayBounds` are C CoreGraphics calls, callable off the main thread
  **[confidence: medium-high — CG display services are not AppKit; §7.1 row 5]** —
  the CG backend serializes captures behind one lock and never touches the UI thread.
- SCK delivers frames on its own dispatch queue via delegate callbacks; the backend
  bridges to a `TaskCompletionSource`/ring-slot; no UI-thread involvement.
- Managed delegates passed as ObjC blocks/delegates MUST be rooted for the native
  lifetime (Linux §4.1-5 listener-lifetime lesson, same bug class: a collected
  delegate is a works-until-it-corrupts crash).

---

## 3. Backend Designs

### 3.1 CGDisplayImageBackend (bring-up + fallback)

Per capture, inside the lock, after cancellation check:

```
1. displayId = Map(screen)                          // §3.3
2. imageRef  = CGDisplayCreateImage(displayId)      // one C call; whole display,
                                                    //   backing (Retina) pixels
   imageRef == NULL → black frame for this call (+ demotion counting)
3. Repack to tight BGRA (§3.2); CFRelease/CGImageRelease imageRef
4. RawFrame(pixelW, pixelH, bytes)
```

- A rect-limited variant (`CGDisplayCreateImageForRect`) exists; whole-display is what
  the seam's per-screen consumers need — keep it simple.
- Cost: one full-display readback per call. At 10-15 FPS/1080p this is comfortably
  within budget on any supported Mac **[confidence: medium — measure in the Slice B
  soak; if Retina 5K readback + repack breaks the budget, that becomes the concrete
  motivation to prioritize Slice D]**.
- Deprecation posture: deprecated in the macOS 14 SDK wave in favor of SCK
  (VERIFIED — the CG capture family carries "first deprecated in macOS 14.0" markers);
  still present and functional through current OS versions. **Sequoia UX caveat
  (web-verified): macOS 15+ can show extra "this app may collect detailed
  information" alerts for apps using the deprecated capture APIs — acceptable for a
  bring-up backend, one more reason Slice D (SCK) is the target state.** If a future
  macOS hard-removes it, the selector's arm-2 SCK path is the answer, and the probe
  (`dlsym` check) must detect absence rather than crash.

### 3.2 BGRA repack (shared by both backends)

CGImage internal formats vary (padded `bytesPerRow`, RGBA vs BGRA component order).
Normalize by drawing into a context we fully control:

```
ctx = CGBitmapContextCreate(buffer, w, h, 8, w*4, CGColorSpaceCreateDeviceRGB(),
                            kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little)
      // = BGRA byte order in memory on little-endian, premultiplied — desktop content
      //   is opaque so premultiplication is a no-op; alpha byte = 0xFF
CGContextDrawImage(ctx, CGRectMake(0, 0, w, h), imageRef)
// buffer is now tight BGRA, w*4 stride by construction — copy/wrap into RawFrame
CFRelease(ctx); CFRelease(colorSpace)
```

This one pattern absorbs every stride/order/color-space variation (the analogue of
Linux §3.3's bytes_per_line + alpha normalization) and is unit-assertable with a
known-color capture. For SCK, the same context-draw applies when frames arrive as
`CGImage`/`IOSurface`-backed pixel buffers; if the SCK stream negotiates
`kCVPixelFormatType_32BGRA` directly (it can — §3.4), the repack is a row-copy
honoring `CVPixelBufferGetBytesPerRow`.

### 3.3 ScreenInfo → CGDirectDisplayID mapping

```
CGGetActiveDisplayList(max, ids, out count)
for each id: CGDisplayBounds(id)   // GLOBAL TOP-LEFT-origin POINTS (CG, not Cocoa —
                                   //   no Y-flip against Avalonia's top-left PixelRect)
match on bounds: ScreenInfo.Bounds (physical px) vs CGDisplayBounds (points) —
  compare via ScreenInfo.Scaling (bounds.X/Scaling == cgBounds.X etc.), tolerant ±1
tie-break / no-match: primary display (CGMainDisplayID()) + one logged warning
```

Avalonia's `Screens` on macOS report `PixelRect` in physical pixels with `Scaling` =
backing scale factor **[confidence: medium — verify Avalonia.Native screen reporting
before trusting the division, §7.1 row 6; the Slice B CI assert (captured dimensions
vs `system_profiler` display resolution) proves it empirically]**. Mapping refreshes
on every capture-call miss (display hotplug) rather than caching forever.

### 3.4 ScreenCaptureKitBackend (target-state primary)

SCK is an ObjC framework — the interop is the highest-complexity item in the three
macOS contracts (block trampolines + delegate protocol). Scope it honestly:

```
Session establishment (first capture; slow-first-call allowance, §1.2):
1. SCShareableContent.getShareableContentWithCompletionHandler(block)   // async, block cb
2. Find SCDisplay matching the CGDirectDisplayID from §3.3
3. filter = SCContentFilter(display, excludingWindows: [])              // whole display
4. config = SCStreamConfiguration { width/height = display backing px,
     pixelFormat = kCVPixelFormatType_32BGRA, minimumFrameInterval = 1/15,
     queueDepth small }
5. stream = SCStream(filter, config, delegate)
6. addStreamOutput(self, type:.screen, sampleHandlerQueue: our dispatch queue)
7. startCaptureWithCompletionHandler(block)

Per CaptureAsync call:
8. Latest-frame slot model: the stream's didOutputSampleBuffer callback keeps ONE
   latest CVPixelBuffer (copied to managed BGRA on arrival, honoring bytesPerRow);
   CaptureAsync returns the latest slot (or awaits the first frame with the ct).
9. Idle teardown: no CaptureAsync for N seconds (e.g. 10s) → stopCapture + release —
   an ambient app must not hold a live capture stream (and its purple menu-bar
   indicator) while no feature is consuming frames.
```

Interop notes:
- Completion handlers are ObjC **blocks**: build block literals with the libobjc block
  ABI (`_NSConcreteStackBlock`/global block + descriptor + invoke fn ptr) — a small,
  well-trodden trampoline; keep it inside `ScreenCaptureKitInterop.cs`
  **[VERIFIED (feasibility) §7.1 row 4 — objc_msgSend-family P/Invoke is an
  officially recognized .NET interop path with Xamarin/.NET-macOS precedent; the
  residual risk is implementation fiddliness, not feasibility]**.
- The stream delegate + output object must be a real ObjC object: allocate via
  `objc_allocateClassPair` with IMPs pointing at rooted managed delegates (same
  lifetime rule as §2.4).
- SCK availability: macOS 12.3+; `SCScreenshotManager` (one-shot API, macOS 14+) could
  replace the stream for pure one-shot use, but the stream model serves the 10-15 FPS
  preview loop better and works from 12.3 — one code path, wider reach.
- Menu-bar capture indicator: macOS shows a recording indicator while a stream runs —
  correct and honest; the idle teardown (step 9) bounds it to actual feature use.

### 3.5 Capture-exclusion interaction (overlay contract cross-link) — design corrected after web verification

Brain-drain windows set `sharingType = none` (`macos-overlay-contract.md` §3.7
revised). **REFUTED as sufficient (§7.1 row 9): on macOS 15+, ScreenCaptureKit
IGNORES `sharingType`** (Apple-confirmed; see overlay contract §3.7 revised for full
evidence) — only legacy CG capture still honors it. Per-backend consequences:

- **CGDisplayImageBackend:** honors `sharingType=none` natively on all OS versions —
  brain-drain layers stay absent from CCP's own CG captures. No change.
- **ScreenCaptureKitBackend:** MUST exclude CCP's protected windows EXPLICITLY —
  build the filter as `SCContentFilter(display, excludingWindows: <our
  sharingType=none windows>)`, resolving our own window numbers at session
  establishment (and on stream rebuild). This is now REQUIRED for self-consistency
  (Windows-parity: our own capture must not see brain-drain), not an optional
  alternative. Slice D acceptance updated.

Third-party SCK-based capture apps on macOS 15+ WILL see the brain-drain window — an
accepted, documented parity gap owned by the overlay contract (§3.7 revised there).

---

## 4. TCC Permission UX (CRITICAL judgment — shared policy, framesource specifics)

Policy owner: the shared `MacOSPermissionService`
(`macos-foreground-title-contract.md` §5 — normative there; deltas here):

1. **No prompt-free path exists** on macOS (unlike Linux wlr-screencopy). Every
   capture backend needs TCC Screen Recording. Therefore the FIRST capture attempt is
   itself prompt-triggering **[VERIFIED — calling any screen-record API while
   ungranted triggers the system prompt; the documented trigger-on-purpose trick is a
   1px `CGWindowListCreateImage` (stackoverflow.com/questions/59337022); §7.1 row
   3]** — which means the seam MUST NOT
   attempt a real capture until the grant is confirmed: **preflight-gate every
   backend selection** (§2.1 arm 1). The prompt happens only via the explicit-enable
   flow: `CGRequestScreenCaptureAccess()` when the user turns on a screen-dependent
   feature (AvatarTube mirror, screen OCR, calibration screen preview).
2. **Denied / dismissed:** feature enabled-but-degraded — black frames, ONE non-modal
   notice ("screen features are paused — click to grant screen recording"), deep link
   `x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture`.
   No re-prompt loop; re-request only on explicit click.
3. **Relaunch reality:** Screen Recording grants take effect only after app relaunch
   — **VERIFIED (current guidance across the Electron ecosystem and 2025-2026 macOS
   permission guides: the app must be fully quit and relaunched)** — the notice says
   "restart CCP to finish enabling". The 60s degraded re-probe (title contract §5.4)
   picks up out-of-band grants where the OS allows.
4. **Grant synergy (design intent, both directions):** one Screen Recording grant
   serves BOTH this seam and the CGWindowList title backend — the enable flow surfaces
   that ("also enables window awareness titles"). First-run: never request; the
   request rides the first explicit feature enable, whenever that is.
5. **macOS 15+ periodic re-authorization — VERIFIED: MONTHLY re-confirm dialogs**
   (weekly in early Sequoia betas, relaxed to monthly in beta 6 and shipped; the
   every-reboot prompt was dropped; policy held through Sequoia's release cycle —
   9to5mac.com/2024/08/14, daringfireball.net 2024-08-17, tidbits.com coverage;
   §7.1 row 7): treat a re-auth lapse exactly like a revocation — degrade + notice,
   never a spontaneous prompt.
6. **TCC identity:** grants key on the bundle identity — dev `dotnet run` grants
   attach to the `dotnet` host, not the shipped `.app` (title contract §5.5; same
   caveat, same mitigation: stable bundle id + signing).

---

## 5. Graceful Fallback Design

### 5.1 FallbackFrameSource

Identical shape to Linux §5.1: returns `Math.Max(1, bounds)` -sized black
`Width*Height*4` frame, logs the reason once (reason = TCC state or probe failure
names — never frame content), serves as both the ungranted terminal and the
repeated-failure demotion target.

### 5.2 Consumer Degrade

Same as Linux §5.2: calibration windows get black screen-frames (camera path
unaffected); AvatarTube mirror black/dim; OCR no hits. Never a crash.

---

## 6. Implementation Slice Plan

All files under `CCP.Avalonia.Desktop.macOS/Platform/…` (§2.3). Standard repo gates
apply. CI uses the shared **macos-smoke** lane (`macos-overlay-contract.md` §6):
headed Aqua session on `macos-latest`; the `kTCCServiceScreenCapture` sqlite grant
recipe is VERIFIED to exist (overlay §7.1-VERIFIED row 2 — actions/runner-images
#9529, incl. Sonoma schema delta) and is exactly the grant this seam needs, so the
REAL capture path is CI-exercisable. What CI cannot prove: the system prompt dialog,
grant-relaunch timing, re-auth policy, multi-monitor — explicit real-Mac manual rows.

### Slice A: Selector + Fallback + DI (Foundation)

**Files:**
- `Platform/MacOSFrameSource.cs`
- `Platform/MacOSFrameSourceBackendSelector.cs`
- `Platform/FrameSourceBackends/IMacOSFrameSourceBackend.cs`
- `Platform/FrameSourceBackends/FallbackFrameSource.cs`
- `Platform/Interop/CoreGraphicsInterop.cs` (`CGPreflightScreenCaptureAccess` — shared
  with title contract Slice A; whichever lands first)
- `Program.cs` — `services.AddSingleton<IFrameSource, MacOSFrameSource>()`

**CI (macos-smoke, NO TCC grant — ungranted path is the test):**
```bash
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.macOS -- --smoke-test --verify-framesource-fallback
# Assert: preflight false → FallbackFrameSource; "unavailable" logged once with TCC
# reason; RawFrame returned with Width*Height*4 black bytes; NO prompt appeared
# (negative assert per title-contract Slice A convention)
```

**Acceptance:**
- [ ] Selection keyed on live preflight + OS/framework probes; unit tests over
      (granted, version, probe) permutations
- [ ] Fallback frame correct byte length; reason logged exactly once
- [ ] Seam never triggers a TCC prompt (CI negative assert)
- [ ] No crash on any permutation; selector terminal arm is `catch → Fallback(reason)`

### Slice B: CGDisplayCreateImage Backend

**Files:**
- `Platform/Interop/CoreGraphicsInterop.cs` (extend: `CGDisplayCreateImage`,
  `CGGetActiveDisplayList`, `CGDisplayBounds`, `CGMainDisplayID`, `CGBitmapContext*`,
  CFRelease helpers)
- `Platform/FrameSourceBackends/CGDisplayImageBackend.cs`

**CI (macos-smoke, `kTCCServiceScreenCapture` sqlite grant):**
```bash
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.macOS -- --smoke-test --verify-framesource
# Harness shows its OWN fullscreen known-color window (#FF5500) on the target display
# (self-contained — no desktop-background mutation needed), waits one frame, captures:
# Assert: frame contains B=0x00,G=0x55,R=0xFF at sampled offsets; alpha == 0xFF;
#         length == Width*Height*4; Width/Height match the display's BACKING pixels
#         (compare vs system_profiler SPDisplaysDataType — proves the Retina rule AND
#         the ScreenInfo mapping/Scaling assumption, §3.3)
# Soak: 150 captures at ~15 FPS — stable RSS (CFRelease discipline), no slowdown
# Negative: run the same job WITHOUT the TCC row → arm-1 fallback (never a null-image
#           crash or a prompt)
```

**Acceptance:**
- [ ] Known-color pixel assertion (not just "non-null data"); tight-BGRA repack via
      the §3.2 context (stride/order absorbed by construction)
- [ ] Retina backing-pixel dimensions returned; mapping tolerant of Scaling
- [ ] NULL image → per-call black frame + demotion counting; no crash
- [ ] Serialized captures; leak-free soak; dispose closes cleanly

### Slice C: TCC Permission UX (screen-capture half)

**Files:**
- `Platform/MacOSPermissionService.cs` (extend: `CGRequestScreenCaptureAccess`,
  ScreenCapture deep link, relaunch notice — the AX half lands with the title
  contract's Slice D; whichever lands first builds the shared service)
- Settings wiring for screen-dependent feature enables (AvatarTube mirror, OCR,
  calibration preview)

**CI:** unit tests with a mocked permission service. Manual checklist (real Mac, once,
recorded):
- [ ] First screen-feature enable → ONE system prompt; deny → degraded-running +
      single notice + working deep link; no loop
- [ ] Grant → relaunch notice; capture works after relaunch
- [ ] Out-of-band grant while degraded → 60s re-probe upgrades to live backend
- [ ] Synergy: after this grant, the title contract's CGWindowList backend also
      lights up with NO additional prompt (cross-contract assert)
- [ ] `.app` bundle identity: grant survives restart + version update

**Acceptance:**
- [ ] §4 policy fully enforced (preflight-gated selection; prompts only on explicit
      enable/click; every denial lands in degrade)
- [ ] Re-probe triggers wired (enable, notice click, 60s degraded timer)

### Slice D: ScreenCaptureKit Backend

**Files:**
- `Platform/Interop/ScreenCaptureKitInterop.cs` (block trampolines, delegate class
  via `objc_allocateClassPair`, SCK class bindings)
- `Platform/FrameSourceBackends/ScreenCaptureKitBackend.cs` (session lifecycle,
  latest-frame slot, idle teardown)

**CI (macos-smoke, TCC grant — macos-latest is ≥ 14, so SCK is always present there):**
```bash
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.macOS -- --smoke-test --verify-framesource-sck
# Assert: selector picks ScreenCaptureKitBackend (log line); same known-color +
# Retina-dimension + soak asserts as Slice B; PLUS:
#  - first-call latency logged (session establishment) and subsequent-call latency
#    < 100ms (cadence contract §1.2)
#  - idle teardown: 12s without capture → "stream stopped" logged; next capture
#    re-establishes (slow-first-call again) without error
#  - stress: cancel mid-first-capture via ct → clean OperationCanceledException,
#    stream not leaked (next capture works)
# Fault probe: rename/absent-framework simulation via test hook → selector falls to
# CGDisplayImageBackend (arm 2 → arm 3 cascade proven)
```

**Acceptance:**
- [ ] 32BGRA negotiated (or repacked via §3.2 when not) honoring
      `CVPixelBufferGetBytesPerRow`
- [ ] `SCContentFilter(display, excludingWindows:)` excludes CCP's own
      `sharingType=none` windows (§3.5 revised — REQUIRED on macOS 15+ where SCK
      ignores sharingType; re-resolved on every stream rebuild)
- [ ] Blocks/delegates rooted for native lifetime (no GC-collection crash under soak)
- [ ] Idle teardown bounds the OS capture indicator to active feature use
- [ ] Version/framework probe: 12.3 gate + `dlopen` check; cascade to CG proven
- [ ] Manual row: verify on a real macOS 12.3-13 machine if available (CI only proves
      current runner OS); record per-version results in §7.3

### Slice E: Multi-Monitor + Retina Correctness

**Files:** `Platform/MacOSFrameSource.cs` (per-screen routing), both backends
(§3.3 mapping hardening: hotplug refresh, tie-breaks).

**CI:** hosted macOS runners have a single display — multi-monitor CANNOT be
CI-proven **[confidence: high — same limitation as overlay Slice E]**. CI covers:
mapping unit tests over fabricated display lists (offsets, mixed scaling, hotplug
diffs), plus the single-display regression suite. Manual checklist (real Mac,
dual-monitor incl. one Retina + one 1x):
- [ ] Each `ScreenInfo` captures ITS display (distinct known-color windows per display)
- [ ] Mixed-DPI dimensions correct per display (backing px each)
- [ ] Hotplug: unplug/replug during a capture loop → per-call black frame or remap,
      no crash

**Acceptance:**
- [ ] Mapping unit tests green (incl. no-match tie-break to main display + warn)
- [ ] Manual dual-monitor checklist executed once and recorded in §7.3

---

## 7. Risk / Unknowns

### 7.1 Claims to verify before the relevant slice lands ([confidence:] tags; driver has web tools)

All rows re-verified in the 2026-07-12 §7.1 web pass (evidence in §7.1-VERIFIED).

| # | Claim | Verdict (web pass 2026-07-12) | Source / evidence | Blocks |
|---|---|---|---|---|
| 1 | CI TCC.db sqlite grant works for `kTCCServiceScreenCapture` on current macos-latest | **VERIFIED (recipe exists; image-sensitive)** — literal `kTCCServiceScreenCapture` insert published; one no-effect report exists (#8951) → keep assert-the-preflight lane design | actions/runner-images #7792, #8951; overlay §7.1-VERIFIED row 2 | B, D |
| 2 | `CGDisplayCreateImage` deprecated (macOS 14 SDK) but present + functional on macOS 14/15/16 runtimes | **VERIFIED** — CG capture family carries macOS 14.0 deprecation markers and remains functional; Sequoia may add extra "detailed information" warning alerts for deprecated-API captures (design note added §2.2/§3.1) | stackoverflow.com/questions/76181646 (Xcode 14.3 deprecation wave); getsentry/sentry-unreal#1126 ("first deprecated in macOS 14.0"); r/MacOSBeta Sequoia alert reports | B |
| 3 | First ungranted capture attempt auto-triggers the system TCC prompt (hence preflight-gating is mandatory) | **VERIFIED** — any screen-record API call triggers the prompt; 1px `CGWindowListCreateImage` is the canonical deliberate trigger | stackoverflow.com/questions/59337022 | A |
| 4 | ObjC block ABI trampolines from C# are stable/viable for SCK completion handlers | **VERIFIED (feasibility)** — objc_msgSend-family P/Invoke is an officially recognized .NET path; block ABI stable; Xamarin/.NET-macOS precedent | learn.microsoft.com/dotnet/ios/advanced-concepts/exception-marshaling; mikeash.com objc_msgSend ABI articles | D |
| 5 | CoreGraphics display-services calls are safe off the main thread | **UNCERTAIN** — no authoritative doc found either way; CG is not AppKit and community usage is off-main; the backend's single-lock serialization stands as the safety net | — | B |
| 6 | Avalonia macOS `Screens` reports physical-pixel `PixelRect` + `Scaling` = backingScaleFactor | **UNCERTAIN** — Avalonia-specific; not settled by this pass; Slice B dimension assert (captured px vs `system_profiler`) remains the empirical check | avalonia-research protocol | B |
| 7 | macOS 15+ periodic screen-capture re-authorization policy | **VERIFIED — MONTHLY** (weekly in early betas → monthly from beta 6; reboot prompts dropped; shipped that way) | 9to5mac.com/2024/08/14; daringfireball.net 2024-08-17; tidbits.com Sequoia coverage | C (notice wording) |
| 8 | SCK minimum version 12.3; `SCScreenshotManager` 14.0+ | **VERIFIED** — SCK availability macOS 12.3+; SCScreenshotManager + SCContentSharingPicker are 14.0+ (we use the stream, so only 12.3 matters) | developer.apple.com/documentation/screencapturekit; ecosystem bindings (screencapturekit crate) gate screenshot-manager behind 14.0 | D |
| 9 | `sharingType=none` windows absent from both CG and SCK capture (self-exclusion consistency) | **REFUTED for SCK on macOS 15+** — SCK ignores sharingType; CG still honors it. §3.5 design-fixed: SCK backend explicitly excludes our windows via `SCContentFilter(excludingWindows:)` | developer.apple.com/forums/thread/792152; tauri-apps/tauri#14200 (Apple-confirmed) | D (cross-link assert) |

### 7.2 Genuine Unknowns (real-desktop only)

| Risk | Impact | Mitigation |
|---|---|---|
| Retina 5K/6K readback + repack cost vs 10-15 FPS budget on the CG backend | Preview-loop jank on big displays | Slice B soak measures; SCK (GPU path) is the designed escape — prioritize Slice D if soak fails |
| SCK behavior drift across 12.3→16 (API refinements, entitlement tightening) | Backend breakage on OS updates | Version-keyed §7.3 matrix; CG cascade remains the net |
| Capture indicator UX (menu-bar purple dot) perceived as suspicious by users | Support noise | Idle teardown (§3.4 step 9) + user-docs wording; indicator is honest by design |
| TCC grant loss on app update (signature/path change) | Sudden black frames post-update | Stable bundle id + signing (§4.6); degrade notice re-appears by design |
| Display hotplug mid-stream (SCK) | Stream error / stale display id | SCK error delegate → session rebuild; per-call black frame meanwhile |

### 7.3 macOS-Version-Specific Results (track as found)

| macOS | Backend | Result | Notes |
|---|---|---|---|
| (none yet) | — | — | — |

---

## 8. CI Verification Matrix

| Slice | macos-smoke (no TCC) | macos-smoke (SR granted) | Mocked unit | Manual (real Mac) |
|---|---|---|---|---|
| A Selector+Fallback | Required (ungranted path + no-prompt negative) | — | Required (permutations) | — |
| B CGDisplayCreateImage | Negative (fallback selected) | Required (known-color, Retina dims, soak, NULL-image negative) | — | — |
| C TCC UX | — | — | Required | Required once (prompt/deny/grant/relaunch/synergy) |
| D ScreenCaptureKit | — | Required (color, latency, idle-teardown, cascade fault probe) | — | Older-macOS (12.3-13) spot check if hardware available |
| E Multi-monitor | — | Single-display regression only | Required (mapping) | Required (dual-monitor incl. mixed DPI) |

CI job additions: reuse the shared macos-smoke lane's granted/ungranted permutations
(title contract §8) — the `kTCCServiceScreenCapture` row serves both contracts, which
itself CI-proves the §4.4 synergy claim.

---

## 9. Summary

- **5 slices, 3 backends** (2 live + fallback): ScreenCaptureKit (modern, 12.3+,
  stream + latest-frame slot + idle teardown) as target-state primary;
  `CGDisplayCreateImage` (plain-C one-shot, deprecated-but-functional) as bring-up and
  cascade fallback; black-frame fallback terminal. **CGDisplayStream explicitly
  rejected** — push-model, equally deprecated, worse seam fit than either.
- **No prompt-free path exists on macOS** — so selection is preflight-gated and the
  seam NEVER triggers a TCC prompt; prompts ride explicit feature enables only, with
  deny→degrade (black frames + one notice + deep link), relaunch reality surfaced,
  and re-auth lapses treated as revocations. One Screen Recording grant serves both
  this seam and the title contract's CGWindowList backend.
- **Nothing is persisted by us** — the TCC grant is the OS-held authorization; no
  restore-token analogue (simpler than the Linux portal).
- **Correctness spine:** §3.2 CGBitmapContext repack absorbs all stride/order
  variation by construction; Retina backing pixels returned per the RawFrame contract;
  ScreenInfo→display mapping is bounds/Scaling-based with hotplug refresh.
- **CI:** the verified TCC.db grant recipe makes REAL capture CI-provable on
  macos-latest (known-color pixel asserts, soaks, cascade fault probes) — stronger
  than the Linux portal lane; prompts, grant timing, older-OS SCK, and multi-monitor
  are honest real-Mac manual rows.
- **Critical path:** A → B → C immediately (working capture + honest UX on every
  granted Mac via the CG backend); D (SCK) as the quality/futureproof upgrade; E last.

---

## Sources

- `CCP.Core/Platform/IFrameSource.cs`, `CCP.Core/Platform/IScreenInfo.cs:10`
- `CCP.Avalonia.Desktop.Windows/WindowsFrameSource.cs` (reference impl)
- `CCP.Avalonia/ViewModels/Tabs/LabTabViewModel.cs:39,73` + webcam windows (consumers)
- `docs/linux-framesource-contract.md` — template, packing contract, privacy hard-line,
  prompt-policy shape
- `docs/macos-overlay-contract.md` — shared ObjC interop, macos-smoke lane, TCC.db
  recipe verification
- `docs/macos-foreground-title-contract.md` — shared `MacOSPermissionService` policy,
  grant synergy
- `docs/linux-macos-readiness-map.md` — governing principle
- Apple: ScreenCaptureKit (SCStream/SCContentFilter/SCShareableContent),
  CGDisplayCreateImage, CGWindowLevel/CGDisplay services, TCC Screen Recording

### 7.1-VERIFIED (web research, 2026-07-12 — drafting pass + full §7.1 verify pass)
| Claim | Result | Source |
|-------|--------|--------|
| CI TCC grant for `kTCCServiceScreenCapture` scriptable on GitHub macOS runners | **VERIFIED (recipe exists)** — sqlite inserts into system+user TCC.db, incl. Sonoma's 4 extra schema columns; image-version-sensitive, prove per image | actions/runner-images #9529 (recipe), #7792, #7818 (carried via overlay contract) |
| Avalonia macOS platform handle = NSWindow (interop foundation these backends share) | **VERIFIED** | carried from `macos-overlay-contract.md` §7.1-VERIFIED (docs.avaloniaui.net) |
| SCK ignores `sharingType=none` on macOS 15+ (self-exclusion broken for the SCK backend without an explicit filter) | **REFUTED claim / design fixed** — §3.5 now REQUIRES `SCContentFilter(excludingWindows:)` for our own protected windows | developer.apple.com/forums/thread/792152; tauri-apps/tauri#14200; developer.apple.com/documentation/appkit/nswindow/sharingtype-swift.enum/none |
| macOS 15 screen-capture re-auth is MONTHLY | **VERIFIED** | 9to5mac.com/2024/08/14; daringfireball.net 2024-08-17 |
| First ungranted capture triggers the TCC prompt | **VERIFIED** | stackoverflow.com/questions/59337022 |
| CG capture family deprecated in the macOS 14 wave, still functional; Sequoia adds extra warning alerts for deprecated-API capture | **VERIFIED** | stackoverflow.com/questions/76181646; getsentry/sentry-unreal#1126; r/MacOSBeta Sequoia reports |
