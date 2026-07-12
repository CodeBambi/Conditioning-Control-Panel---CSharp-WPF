# macOS Overlay Implementation Contract

**Date:** 2026-07-12  
**Scope:** macOS overlay click-through bring-up (`IOverlaySurface`) — readiness-map "BIG ONE" row, macOS side  
**Status:** Authoritative design contract (read-only research artifact; no code changes)  
**Siblings:** `linux-overlay-contract.md` (template + shared rules), `macos-foreground-title-contract.md`, `macos-framesource-contract.md`

This document specifies the behavior contract, backend architecture, and implementation
slices for macOS overlay support in the Avalonia head. Per the governing principle in
`linux-macos-readiness-map.md`: multi-backend where the platform demands it,
runtime-selected, graceful fallback, CI-verified. macOS is simpler than Linux in one
decisive way — there is exactly ONE window system (AppKit/Quartz) — so "multi-backend"
here means multiple *mechanisms* for per-region click-through behind one seam, not
multiple display servers.

> **Verification posture:** three load-bearing claims were web-verified while drafting
> (§7.1-VERIFIED). Everything else derived from AppKit/Quartz knowledge is
> **[confidence:]**-tagged for the driver's web pass. The avalonia-research skill
> applies: v12 is new, v11 answers are suspect, and Avalonia's macOS backend
> (`Avalonia.Native` / libAvaloniaNative) is the least-documented of the three desktop
> backends — every "Avalonia does X on macOS" claim below carries a tag.

---

## 1. IOverlaySurface Behavior Contract

The `IOverlaySurface` interface (`CCP.Core/Platform/IOverlaySurface.cs`) is UNCHANGED:

```csharp
public interface IOverlaySurface
{
    void Show();
    void Hide();
    void Close();
    bool IsVisible { get; }
    void SetClickThrough(bool enabled);
    void SetBounds(PixelRect rect);
}
```

### 1.1 Behavioral Requirements (cited from the Windows head)

| Requirement | Windows Implementation | Citation | macOS disposition |
|---|---|---|---|
| **Topmost** | `Topmost = true` in base ctor; `HWND_TOPMOST` reassertion probe | `AvaloniaOverlaySurface.cs:14`, `CompositorWindow.axaml.cs:149-175` | `NSWindow.level` (§3.2). Level-based stacking is *stable* — no Win32-style z-order races, so the reassertion timer is expected to be a no-op **[web-verified §7.1 row 4: Avalonia's only level write is `SetTopMost` (→ floating/normal) — no spontaneous resets in source, but re-assert our level after any `Topmost` property write; Slice B watch stays]** |
| **Click-through toggle** | `SetClickThrough(true)` ORs `WS_EX_TRANSPARENT \| WS_EX_LAYERED`; `false` clears `TRANSPARENT` | `WindowsOverlaySurface.cs:24-40` | `NSWindow.ignoresMouseEvents` (§3.3) |
| **Per-region click-through** | Window always transparent; global `WH_MOUSE_LL` hook swallows clicks inside the capture mask | overlay-clickthrough skill; 2026-07-09 team review | Dynamic `ignoresMouseEvents` toggling driven by a permission-free mouse-location poller (§3.4) — NOT an event tap (TCC-gated, rejected §3.4) |
| **Focus non-stealing** | `ShowActivated=false`, `Focusable=false`, `IsHitTestVisible=false`; `WS_EX_NOACTIVATE` | `AvaloniaOverlaySurface.cs:17-20` | Same Avalonia flags; verify the NSWindow never becomes key on click-capture (§3.6). NO class swizzling to force `canBecomeKey=false` — mirror of the `SetWindowSubclass` ban |
| **Taskbar/Alt-Tab exclusion** | `ShowInTaskbar=false`; `WS_EX_TOOLWINDOW` | `AvaloniaOverlaySurface.cs:15` | `collectionBehavior` `ignoresCycle` (Cmd-Tab shows apps not windows on macOS; Mission Control exclusion via `stationary`) §3.5 |
| **Transparency** | `WindowDecorations=None`, `TransparencyLevelHint=[Transparent]` | `AvaloniaOverlaySurface.cs:11-13` | Same Avalonia flags; Quartz composites alpha natively — no "no compositor" degrade case exists on macOS |
| **Screen-capture exclusion** | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` for brain-drain only | `CompositorWindow.axaml.cs:115-120` | `NSWindow.sharingType = NSWindowSharingNone` (§3.7) — **BEST-EFFORT ONLY: on macOS 15+, ScreenCaptureKit IGNORES `sharingType` (web-REFUTED §7.1 row 9)**; still blocks legacy CG capture. Honest parity gap recorded |
| **Multi-monitor** | One window per monitor; `SetBounds(PixelRect)` | `CompositorEngine.cs:127-134` | One window per `NSScreen`; positioning stays in Avalonia (§3.8) — native code never moves the window |
| **All-Spaces / fullscreen coverage** | N/A (Windows has no Spaces) | — | `collectionBehavior` `canJoinAllSpaces \| fullScreenAuxiliary` (§3.5) — REQUIRED or the overlay vanishes when the user switches Spaces or an app goes fullscreen |

### 1.2 Per-Region Click-Through Contract (2026-07-09 team review, owner reconfirmed 2026-07-12)

Identical to the Linux/Windows contract — input disposition is PER-REGION:

| Layer Type | Examples | Input Behavior |
|---|---|---|
| **Ambient (pass-through)** | `PinkTintLayer` (theme color filter), `SpiralLayer` — these two ONLY | Clicks pass to apps beneath |
| **Capture (absorb)** | `VideoLayer`, `FlashLayer`, `SubliminalLayer`, `BrainDrainLayer`, `BouncingTextLayer`, `BubbleLayer`, keyword highlight, all Chaos FX | Clicks captured by overlay |

**Mask semantics (normative, identical precedence to the Linux contract §1.3):**
- `SetClickThrough(false)` → full-window input (capture everything), regardless of regions.
- `SetClickThrough(true)` + capture regions present → input accepted only inside the
  union of capture regions.
- `SetClickThrough(true)` + no capture regions → fully ambient (all input passes).

**macOS mechanism reality:** unlike X11 (server-side input shape) and Windows
(low-level hook), macOS gives NO per-region input shape on a foreign-toolkit window and
NO permission-free global click hook. `ignoresMouseEvents` is whole-window and binary.
The per-region contract is therefore synthesized by *toggling* that binary flag from a
mouse-position tracker (§3.4). Consequences:

- **Interactive layers get REAL pointer events** over captured regions (the window
  accepts input while the pointer is inside the mask) — same interactivity note as
  Linux §1.3; the base window's `IsHitTestVisible=false` must be revisited for
  interactive layers in Slice C.
- **Boundary race:** a click that lands within one poll interval of the pointer
  crossing a mask edge can be mis-routed (§3.4 limits). Accepted and bounded; the
  input-catcher-window variant (Slice F, parked) removes it if it ever matters.

### 1.3 The Never-Trap Rule (normative — inherited verbatim from Linux contract §1.4)

**Degrade by disabling the overlay, never by trapping input.** For macOS concretely:

- If the NSWindow handle cannot be acquired (§2.1) or `ignoresMouseEvents` cannot be
  set, the backend MUST NOT display the surface while ambient mode is requested
  (`Show()` while `_clickThroughEnabled == true` hides/refuses and logs once).
  Full-capture mode (`SetClickThrough(false)`, e.g. mandatory video) MAY show.
- The per-region poller failing (timer dead, coordinate conversion throwing) demotes
  the window to FULLY ambient (`ignoresMouseEvents=true` latched) — losing capture
  regions is annoying; trapping the desktop is unacceptable. Log once, raise the
  degrade signal.
- `AvaloniaPlatformCapabilities.SupportsClickThrough` must report the macOS backend's
  real capability (today it hardcodes `IsWindows` — flip it when Slice A lands).

---

## 2. macOS Backend Architecture

### 2.1 Ground truth: the Avalonia platform handle

**VERIFIED (§7.1-VERIFIED row 1):** on macOS, `TopLevel.TryGetPlatformHandle()` returns
the **NSWindow pointer** with `HandleDescriptor == "NSWindow"` — the exact mirror of the
Linux "XID" fact. All native work below operates on that pointer via `objc_msgSend`
P/Invoke (`libobjc.A.dylib`), setting *properties only*. We never subclass, swizzle, or
replace the window's class or delegate — Avalonia.Native owns the window lifecycle
(mirror of the Linux rule "never fight Avalonia for the window" and the compositor's
`SetWindowSubclass` ban).

### 2.2 Threading model (normative)

- **AppKit is main-thread-only.** Every NSWindow property mutation (`level`,
  `ignoresMouseEvents`, `collectionBehavior`, `sharingType`) must run on the AppKit
  main thread. On macOS, Avalonia's `Dispatcher.UIThread` IS the AppKit main thread
  **[VERIFIED §7.1 row 5 — Avalonia has one main UI thread and its macOS backend runs
  NSApplication on the process main thread (docs.avaloniaui.net threading)]** — marshal with
  `Dispatcher.UIThread.Post/InvokeAsync`; never block a threadpool thread waiting on it.
- `NSEvent.mouseLocation` (class property) and pure CoreGraphics queries used by the
  poller are callable off-main **[confidence: medium-high — CG APIs are thread-safe;
  +[NSEvent mouseLocation] is documented as usable outside the event stream]** — but
  the *toggle* the poller triggers still marshals to the main thread.
- No Xlib-style error-trap problem exists: `objc_msgSend` on a nil receiver is a safe
  no-op, and AppKit property setters don't have an X11-style async fatal error channel.
  The failure modes are ObjC exceptions (which P/Invoke turns into process aborts if
  thrown across the boundary) — avoided by only calling simple property setters on
  valid, live window pointers, re-acquired on every `Opened` (never cached across
  `Close()`).

### 2.3 Runtime Backend Selection

One window system → a short chain. "Backends" differ in the per-region mechanism:

```
MacOSOverlayBackendSelector (at overlay init; re-checked when the first window opens)
  1. TryGetPlatformHandle() yields descriptor "NSWindow"
       → NSWindowOverlayBackend (level + ignoresMouseEvents + poller-driven per-region)
  2. Handle unavailable / descriptor unexpected (future Avalonia change)
       → SafeDegrade policy + FallbackBackend
```

| Priority | Backend | Capabilities | When Selected |
|---|---|---|---|
| 1 | `NSWindowOverlayBackend` | Full: level topmost, all-Spaces, full + per-region click-through, sharingType exclusion | "NSWindow" handle acquired |
| 2 | `SafeDegrade` (policy) | Ambient overlay features OFF; full-capture overlays may show | Handle acquisition fails |
| 3 | `FallbackBackend` | Plain Avalonia window, `Topmost=true`, honest `SupportsPerRegionInput=false` | The window SafeDegrade uses for full-capture-only overlays |

**Never-hard-fails guarantee:** the selector wraps handle acquisition and every native
call block in `try/catch → FallbackBackend(reason)`. `FallbackBackend` makes zero
native calls and always constructs. Capability flags are a contract — a backend MUST
NOT report a capability it does not deliver (Linux contract §1.1 rule, inherited).

### 2.4 Seam Structure

Mirrors the Linux head's landed `Platform/` layout:

```
CCP.Avalonia.Desktop.macOS/Platform/
├── MacOSOverlaySurface.cs           # IOverlaySurface impl (derives AvaloniaOverlaySurface,
│                                    #   like WindowsOverlaySurface) + SetInputCaptureRegions
├── MacOSOverlayBackendSelector.cs
├── Backends/
│   ├── IMacOSOverlayBackend.cs      # capability flags + IDisposable (mirror ILinuxOverlayBackend)
│   ├── NSWindowOverlayBackend.cs
│   └── FallbackBackend.cs
├── MouseRegionTracker.cs            # the §3.4 poller (pure logic + one NSEvent read — unit-testable)
└── Interop/
    ├── ObjCInterop.cs               # objc_msgSend / sel_registerName / objc_getClass — SHARED
    │                                #   with the title/framesource contracts; one impl per process
    └── AppKitInterop.cs             # NSWindow property helpers, NSEvent.mouseLocation, level keys
```

The `ObjCInterop.cs` helper is SHARED infrastructure with
`macos-foreground-title-contract.md` and `macos-framesource-contract.md` — whichever
slice lands first builds it; the others extend, never fork (Linux `X11Interop` rule).

---

## 3. NSWindow Backend Design

### 3.1 Interop shape

All calls are `objc_msgSend` against the handle from §2.1:

```csharp
// ObjCInterop.cs (shared)
[DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
static extern void objc_msgSend_void_long(IntPtr receiver, IntPtr selector, long value);
[DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
static extern void objc_msgSend_void_bool(IntPtr receiver, IntPtr selector, bool value);
[DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
static extern IntPtr sel_registerName(string name);
// setLevel:, setIgnoresMouseEvents:, setCollectionBehavior:, setSharingType:
```

Struct-returning and float-argument variants need the correct `objc_msgSend` flavor per
ABI; on arm64 there is no `objc_msgSend_stret` split **[confidence: high — arm64 ABI]**.
Keep the interop layer to scalar property setters plus `NSEvent.mouseLocation`
(`NSPoint` return — two doubles, safe by-value on arm64/x64).

### 3.2 Topmost via `NSWindow.level`

Quartz window levels (from `CGWindowLevelForKey`):

| Level key | Value | Notes |
|---|---|---|
| `kCGNormalWindowLevel` | 0 | ordinary windows |
| `kCGFloatingWindowLevel` | 3 | `NSWindow.Level.floating`; **VERIFIED — Avalonia maps `Topmost=true` to exactly this**: `WindowBaseImpl::SetTopMost` does `[Window setLevel:value ? NSFloatingWindowLevel : NSNormalWindowLevel]` (Avalonia.Native `WindowBaseImpl.mm`; §7.1 row 4). Consequence: ANY later write to the Avalonia `Topmost` property re-fires `SetTopMost` and clobbers our custom level back to 3 — re-assert the custom level after any `Topmost` write, and keep the Slice B level-reset watch |
| `kCGStatusWindowLevel` | 25 | above menu bar items |
| `kCGPopUpMenuWindowLevel` | 101 | above open menus |
| `kCGScreenSaverWindowLevel` | 1000 | above practically everything |

**Design:** `setLevel: kCGScreenSaverWindowLevel (1000)` for parity with the Windows
head's "above everything" behavior (the WPF/Windows overlay tints over all app content
including fullscreen video). This deliberately draws over the menu bar and Dock — for
the ambient tint that is the product intent; verify visually in the headed CI lane and
manual pass. If real-Mac testing shows screensaver-level interferes with system UI
(Notification Center, Mission Control), fall back to `kCGStatusWindowLevel` as a
settings-selectable compromise — record the outcome in §7.3.

- Set the level AFTER `Show()` (window must exist; also some AppKit paths reset level
  on ordering-in **[confidence: low-medium — the classic gotcha is orderFront resetting
  custom levels set pre-show; verify empirically in Slice B]**). Re-apply on every
  `Opened` since Avalonia may recreate the native window. **Verified reset vector
  (§7.1 row 4):** Avalonia's `SetTopMost` sets `level` directly — re-assert our level
  after any code path that writes the `Topmost` property.
- **No reassertion timer initially:** level-based stacking has no HWND_TOPMOST-style
  racing. Keep the Windows head's 500ms probe DISABLED on macOS unless Slice B's headed
  CI shows the level being reset (Avalonia touching `level` on
  activation/fullscreen-transition is the plausible culprit — §7.1 row 4). If needed,
  the probe re-reads `window.level` and re-sets — idempotent and cheap.

### 3.3 Full click-through via `ignoresMouseEvents`

```
SetClickThrough(true)  → window.setIgnoresMouseEvents(true)   // all input passes through
SetClickThrough(false) → window.setIgnoresMouseEvents(false)  // window receives input
```

This is the exact analogue of `WS_EX_TRANSPARENT` and works at the window-server level:
events in the window's frame are delivered to whatever is beneath. Note: once
`setIgnoresMouseEvents:` has been called explicitly with ANY value, AppKit's implicit
per-transparent-pixel pass-through is disabled for that window **[VERIFIED — Loom's
ElectronMacOSClickThrough README documents exactly this tri-state: "a borderless
NSWindow will register clicks in opaque regions AND pass clicks through in transparent
regions UNLESS you set ignoresMouseEvents at which point all clicks register … or all
pass through" (github.com/loomhq/ElectronMacOSClickThrough); §7.1 row 6]** — irrelevant to us
(our ambient layers paint visible pixels, so we never relied on alpha pass-through),
but worth knowing when debugging.

### 3.4 Per-region click-through — mechanism selection (the judgment core)

Candidate mechanisms, evaluated:

| Mechanism | Verdict | Why |
|---|---|---|
| **`contentView.hitTest` override** | REJECTED | `hitTest` runs only AFTER the window server has already routed the event to our window; returning nil re-routes within our app, it does NOT forward the click to another app's window. Cannot implement pass-through. Also requires subclassing Avalonia's view (banned, §2.1) |
| **Per-pixel alpha pass-through** | REJECTED | Only fully-transparent pixels pass; the ambient tint/spiral paint visible alpha. Also disabled once `setIgnoresMouseEvents:` is used (§3.3) |
| **`CGEventTap` global hook (Windows-hook mirror)** | REJECTED | An event tap that observes/blocks clicks requires **TCC Accessibility (Input Monitoring)** — an ambient app must not demand Accessibility just to draw overlays (TCC judgment: see title contract §5). Also listen-only taps get disabled by the OS under event pressure |
| **Dynamic `ignoresMouseEvents` toggling from a mouse-location poller** | **PRIMARY** | Zero TCC. `NSEvent.mouseLocation` is readable without any permission (VERIFIED — used freely in permissionless sample code, e.g. stackoverflow.com/questions/31931403). Poll ~60Hz (or on a global `mouseMoved` monitor **[VERIFIED — mouse-moved global monitors work even when `AXIsProcessTrusted()` is false; only KEY-event monitors need Accessibility (stackoverflow.com/questions/49716420 + Apple "Monitoring Events" archive doc); §7.1 row 7]**): pointer inside capture-mask union → `ignoresMouseEvents=false`; outside → `true` |
| **Transparent input-catcher child NSWindows over capture regions** | PARKED (Slice F) | Robust (no boundary race, true per-region at the window server), zero TCC, but per-mask-update window churn and event re-routing complexity. Unpark if the poller's race proves user-visible |

**Poller design (`MouseRegionTracker`):**
- Timer at 16ms while the overlay is shown AND ambient mode is active AND the capture
  mask is non-empty (the two all-or-nothing states need no poller — latch the flag).
- Read `NSEvent.mouseLocation` → **bottom-left-origin screen POINTS**. The compositor
  capture mask is top-left-origin PIXELS (`PixelRect`). Convert: flip Y against the
  primary screen height, scale by the window's `RenderScaling`/backing scale factor.
  Getting this wrong inverts the click-through map vertically — Slice C has a
  positional CI assert for exactly this.
- Hysteresis: only call the setter when the inside/outside verdict CHANGES (one ObjC
  call per boundary crossing, not per tick).
- **Known limits (documented, accepted):** (1) a click within one poll interval of a
  boundary crossing can mis-route — at 16ms and human motor speed this is rare;
  (2) during a drag that crosses the boundary, the routing latches to the state at
  mouse-down (AppKit routes the whole drag to the mouse-down window) — acceptable;
  (3) two overlapping overlay windows (multi-monitor edge) each need their own tracker
  keyed to their own mask.
- Poller failure → latch `ignoresMouseEvents=true` (fully ambient), log once, degrade
  signal (§1.3 never-trap direction: fail OPEN, never fail trapping).

### 3.5 All-Spaces and fullscreen coverage via `collectionBehavior`

```
collectionBehavior = canJoinAllSpaces (1<<0)      // overlay follows every Space
                   | fullScreenAuxiliary (1<<8)   // may appear over fullscreen apps' Spaces
                   | stationary (1<<4)            // not moved by Mission Control / Exposé
                   | ignoresCycle (1<<6)          // excluded from window cycling
```

Flag raw values VERIFIED against the AppKit SDK header (`NSWindow.h`):
`CanJoinAllSpaces = 1<<0`, `Stationary = 1<<4`, `IgnoresCycle = 1<<6`,
`FullScreenAuxiliary = 1<<8`.

Without `canJoinAllSpaces` the overlay silently disappears when the user switches
Spaces — the macOS-specific failure Windows/Linux don't have. `fullScreenAuxiliary`
plus a level above `kCGNormalWindowLevel` is what lets the overlay draw over a
fullscreen app **[confidence: medium — fullscreen Spaces + auxiliary windows interplay
varies by macOS version; headed CI can't create a fullscreen third-party app easily —
manual checklist row, §7.2]**.

**Avalonia-touches-collectionBehavior warning (web-verified from Avalonia.Native
`WindowBaseImpl.mm`):** (1) `Show()` reads `collectionBehavior`, strips
`FullScreenPrimary` for the duration of the show, then RESTORES the pre-show value —
so flags set mid-`Show()` can be lost; (2) `SetParent` hard-OVERWRITES
`collectionBehavior` with `FullScreenAuxiliary` alone. **Rules:** apply our
collectionBehavior AFTER `Show()` returns (on `Opened`), and NEVER parent overlay
windows to another window.

### 3.6 Focus non-stealing

Avalonia's `ShowActivated=false` / `Focusable=false` carry over. The residual macOS
risk: when a capture-region click is delivered, AppKit may make the window key/main and
activate the app (bouncing the Dock icon, deactivating the user's app). Mitigations in
order: (1) Avalonia's non-activating SHOW path is web-verified — `Show(activate:false)`
uses bare `orderFront:` with no makeKey/activate (`WindowBaseImpl.mm`); CLICK-time
activation remains the open half **[UNCERTAIN — §7.1 row 8; Slice D CI assert]**;
(2) if the app activates on capture clicks, `NSApp.deactivate` after handling is
a crude repair — record findings in §7.3 before choosing. NO `canBecomeKey` swizzle.

### 3.7 Screen-capture exclusion via `sharingType` — BEST-EFFORT on macOS 15+ (design corrected after web verification)

`window.setSharingType(NSWindowSharingNone /* 0 */)` — enum values VERIFIED from the
AppKit SDK header (`None=0, ReadOnly=1 (default), ReadWrite=2`). Applied to the
brain-drain window only, mirroring `CompositorWindow.axaml.cs:115-120` gating.

**REFUTED as a full equivalent (§7.1 row 9):** on **macOS 15+ (Sequoia),
ScreenCaptureKit IGNORES `sharingType = .none`** — Apple confirmed via developer
forums that all window contents are composited into a single framebuffer which SCK
captures regardless of sharing flags; Apple's own docs now describe
`NSWindow.SharingType.none` as "a legacy constant that macOS no longer uses"
(developer.apple.com/documentation/appkit/nswindow/sharingtype-swift.enum/none;
developer.apple.com/forums/thread/792152; tauri-apps/tauri#14200 — no known
workaround, matches Electron #48258). On macOS ≤ 14 it works; on 15+ it still blocks
**legacy CG capture paths** (CGWindowList/CGDisplay image APIs) but NOT SCK-based
capture (which includes QuickTime/system screenshot flows on current macOS).

**Revised design:** still SET `sharingType=none` on brain-drain windows (harmless,
blocks legacy capture, full exclusion on ≤ 14), but the Windows-parity claim is
downgraded to **best-effort**: on macOS 15+ third-party SCK capture WILL see the
brain-drain layer. Record this as an honest parity gap in the readiness map — do NOT
chase compositor hacks (CGShieldingWindowLevel etc. are confirmed ineffective).
Self-consistency with CCP's OWN framesource is handled on the framesource side:
its SCK backend explicitly excludes CCP's protected windows via
`SCContentFilter(excludingWindows:)` (see `macos-framesource-contract.md` §3.5
revised), and its CG backend honors `sharingType` natively.

### 3.8 Multi-monitor

One overlay window per `NSScreen`, driven by the existing `CompositorEngine` per-screen
logic. Native code NEVER moves/sizes the window — `SetBounds` stays pure Avalonia
(coordinate flipping between Cocoa's bottom-left points and Avalonia's top-left pixels
is exactly the kind of bug we refuse to own; Avalonia already does it). Per-window
concerns: each window gets its own level/collectionBehavior/sharingType application on
`Opened`, its own capture-mask, its own tracker. Retina: masks are in physical pixels;
`RenderScaling` per window at conversion time (mixed-DPI dual-monitor is the §7.2 risk
row).

---

## 4. TCC / Permissions

**The overlay needs ZERO TCC permissions.** This is a hard design constraint the
mechanism selection in §3.4 exists to satisfy: window levels, `ignoresMouseEvents`,
`collectionBehavior`, `sharingType`, and `NSEvent.mouseLocation` are all
permission-free. Any future overlay change that introduces an event tap or
accessibility call is a design regression — reject in review. (The title and
framesource contracts carry the TCC burden; see their §5/§4 and the shared prompt
policy in `macos-foreground-title-contract.md` §5.)

---

## 5. Graceful Fallback Design

- **FallbackBackend:** plain Avalonia window, `Topmost=true`, zero native calls,
  honest capability flags. `SetClickThrough(true)` + `Show()` → surface hidden, reason
  logged once (never-trap §1.3). `SetClickThrough(false)` → shows and captures
  (intended).
- **SafeDegrade policy:** `SupportsClickThrough=false` → ambient features gated off
  upstream; one non-modal notice ("ambient overlay effects are not available"), never
  a prompt loop.
- **Runtime demotion:** repeated native-call failures on a live backend demote to
  FallbackBackend without restart.

---

## 6. Implementation Slice Plan

Standard repo gates (slnf 0 errors, WPF 0 errors, Core tests ≥ floor, Windows smoke)
apply to every slice. All files under `CCP.Avalonia.Desktop.macOS/Platform/…` (§2.4).

**The macos-smoke CI lane (shared by all three macOS contracts — defined once here):**
GitHub `macos-latest` runners provide a REAL WindowServer/Aqua session (unlike Linux
xvfb) — a HEADED smoke lane is feasible: build the macOS head, `dotnet run … --
--smoke-test --verify-<x>`, assert on structured log output. Two lane facts:
- **Window-state assertions are permission-free:** `CGWindowListCopyWindowInfo` exposes
  our window's `kCGWindowLayer` (= level), bounds, and owner WITHOUT any TCC grant
  (only `kCGWindowName` is gated — verified, see title contract §7.1-VERIFIED). The
  smoke harness can therefore assert "overlay window exists at level 1000 with bounds
  covering the screen" natively.
- **Synthetic input needs a TCC grant:** posting clicks (`CGEventPost`) requires
  Accessibility. On GitHub runners the TCC database is editable via
  `sudo sqlite3 …/TCC.db` inserts (§7.1-VERIFIED row 2 — actions/runner-images #9529
  ships a working recipe incl. the Sonoma schema delta). Grant
  `kTCCServiceAccessibility`/`kTCCServicePostEvent` to the runner binary in a lane
  setup step; mark the input-assertion jobs `continue-on-error` until the recipe is
  proven on the current image **[confidence: medium — image-version-sensitive]**.
- **What still needs a real Mac (cannot be CI-scripted):** the user-facing TCC prompt
  UX (no prompts here — overlay is zero-TCC, but siblings need it), Spaces switching
  and fullscreen-app coverage (§3.5), `sharingType` verified against a real
  screen-share app, and per-OS-version behavior deltas. Each slice lists its residual
  manual rows — no slice lands compile-only (readiness-map rule).

### Slice A: Handle + Interop Foundation + Selector + Fallback

**Goal:** NSWindow handle acquisition, ObjC interop layer, selection, SafeDegrade,
honest fallback.

**Files:** `Interop/ObjCInterop.cs`, `Interop/AppKitInterop.cs`,
`MacOSOverlayBackendSelector.cs`, `MacOSOverlaySurface.cs`,
`Backends/IMacOSOverlayBackend.cs`, `Backends/FallbackBackend.cs`, `Program.cs` DI
registration, `AvaloniaPlatformCapabilities` per-backend `SupportsClickThrough`.

**CI (macos-smoke):**
```bash
dotnet build ConditioningControlPanel/CCP.Desktop.slnf -c Debug   # all heads stay green
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.macOS -- --smoke-test --verify-overlay-handle
# Assert: log line "overlay handle acquired: NSWindow"; backend = NSWindowOverlayBackend
# Fault injection: force handle-acquisition failure via a test hook → FallbackBackend
# selected, SetClickThrough(true)+Show() refuses + logs once (§1.3)
```

**Acceptance:**
- [ ] `HandleDescriptor == "NSWindow"` observed and logged on macos-latest (turns §7.1
      row 1 from doc-verified into runtime-verified)
- [ ] Selector never throws; fault-injected failure yields FallbackBackend
- [ ] No backend reports a capability it does not implement (unit test)
- [ ] Never-trap behavior on FallbackBackend (hide/refuse + single log)

### Slice B: Topmost Level + collectionBehavior + Full Click-Through

**Goal:** overlay above everything, on every Space, fully ambient or fully capturing.

**Files:** `Backends/NSWindowOverlayBackend.cs`, `Interop/AppKitInterop.cs` (extend:
`setLevel:`, `setIgnoresMouseEvents:`, `setCollectionBehavior:`).

**CI (macos-smoke):**
```bash
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.macOS -- --smoke-test --verify-overlay-topmost
# Harness: show overlay; open a plain NSWindow-level Avalonia helper window; assert via
# CGWindowListCopyWindowInfo (no TCC needed) that the overlay's kCGWindowLayer == 1000
# and it precedes the helper in front-to-back order.
# Input half (TCC-granted lane, continue-on-error until recipe proven):
# ignoresMouseEvents=true → CGEventPost click at overlay center → helper window
# beneath receives it (harness asserts its own click handler fired).
```

**Acceptance:**
- [ ] Level applied post-Show and re-applied on `Opened` (window recreation safe)
- [ ] `canJoinAllSpaces|fullScreenAuxiliary|stationary|ignoresCycle` set; values unit-tested as constants
- [ ] Full click-through proven by synthetic click OR documented as the lane's
      continue-on-error residual with the manual row recorded
- [ ] Level-reset watch: harness re-reads level after activation changes; if reset
      observed, reassertion probe enabled and §7.1 row 4 answered

### Slice C: Per-Region Click-Through (MouseRegionTracker)

**Goal:** §1.2 mask semantics via dynamic toggling, with coordinate correctness.

**Files:** `MouseRegionTracker.cs`, `Backends/NSWindowOverlayBackend.cs` (region path),
`CCP.Avalonia/Compositor/CompositorEngine.cs` (reuse the Windows capture-mask snapshot —
already per-frame and immutable), interactivity wiring for captured regions (§1.2).

**CI (macos-smoke, TCC-granted lane):**
```bash
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.macOS -- --smoke-test --verify-capture-region
# Smoke mode paints ONE capture rect at a known position (600,400 200x200 px):
# CGEventPost: move to (700,500), dwell 100ms (≥ several poll ticks), click
#   → overlay receives exactly one pointer event at ~(700,500)  [capture + Y-flip + scaling proven]
# CGEventPost: move to (200,200), dwell, click
#   → helper window beneath receives it                          [pass-through proven]
```
Unit tests (no display needed): `MouseRegionTracker` verdict logic over mask
permutations, bottom-left→top-left Y-flip math, Retina scale factors, hysteresis
(setter called only on verdict change).

**Acceptance:**
- [ ] §1.2 precedence semantics exact (three-state, unit-tested)
- [ ] Positional CI assertion incl. the Y-flip (not just "a click was captured")
- [ ] Hysteresis: ≤1 native call per boundary crossing (counter assert in harness)
- [ ] Poller failure latches fully-ambient + degrade signal (fault-injection test)
- [ ] Interactive-layer input path decided and tested (real pointer events over
      captured regions)

### Slice D: Screen-Capture Exclusion + Focus Non-Stealing Verification

**Goal:** brain-drain capture exclusion parity; prove the overlay never activates.

**Files:** `Backends/NSWindowOverlayBackend.cs` (`setSharingType:`), window-level gating
mirroring `CompositorWindow.axaml.cs:115-120`.

**CI (macos-smoke):**
```bash
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.macOS -- --smoke-test --verify-overlay-sharing
# With the framesource TCC grant (screen-recording row from the sibling lane):
# capture the screen via the sibling's CGDisplayCreateImage path with the brain-drain
# window shown+excluded → assert the known-color test pattern under it is visible in
# the capture (window absent). NOTE: this assert is valid ONLY via the legacy CG path —
# sharingType still blocks CG capture on macOS 15+; an SCK-path capture on macos-latest
# (≥15) WILL show the window (expected per §3.7 revised — assert that too, as the
# documented-gap regression canary). Focus: post capture-region click → assert the
# harness's previously-key helper window is STILL key (NSApp not activated).
```
Manual rows: real screen-share app (Zoom/QuickTime) exclusion check — EXPECTED VISIBLE
on macOS 15+ SCK-based apps per §3.7 revised; §3.6 activation behavior on a real desktop.

**Acceptance:**
- [ ] `sharingType=none` applied only to brain-drain windows (best-effort per §3.7
      revised — legacy-CG exclusion asserted; SCK visibility on 15+ documented, not fought)
- [ ] CI capture assert green or recorded continue-on-error residual
- [ ] Focus non-steal assert green; findings recorded in §7.3 either way

### Slice E: Multi-Monitor

**Goal:** one overlay per NSScreen with independent masks/trackers.

**Files:** `MacOSOverlaySurface.cs` (per-monitor windows already driven by
`CompositorEngine`), per-window backend state.

**CI:** GitHub macOS runners have a single display — multi-monitor CANNOT be CI-proven
on hosted runners **[confidence: high — no scriptable virtual-display facility on
hosted macOS images]**. CI covers the single-monitor regression suite plus unit tests
over per-window mask routing; multi-monitor (incl. mixed-DPI Retina/non-Retina) is an
explicit manual checklist on a real Mac. This is the honest analogue of the Linux
contract's `xrandr --setmonitor` recipe — macOS has no equivalent; do not fake it.

**Acceptance:**
- [ ] Per-window level/behavior/mask/tracker isolation (unit-tested routing)
- [ ] Single-monitor CI suite green
- [ ] Manual multi-monitor checklist written into §7.3 and executed once on real hardware

### Slice F: (PARKED) Input-Catcher Child Windows

Parked per §3.4. Unpark criteria: Slice C's boundary race produces user-visible
mis-routing in real use, or an interactive layer needs sub-16ms input fidelity
(fast-clicking bubbles). Design sketch retained: transparent, non-activating child
NSWindows (our own app) positioned over capture rects with `ignoresMouseEvents=false`,
events routed to the compositor hit-test; the main overlay window stays permanently
`ignoresMouseEvents=true`. Until unparked: no code.

---

## 7. Risk / Unknowns

### 7.1 Claims to verify before the relevant slice lands ([confidence:] tags; driver has web tools)

All rows re-verified against current sources in the 2026-07-12 §7.1 web pass
(annotations below; full evidence in §7.1-VERIFIED).

| # | Claim | Verdict (web pass 2026-07-12) | Source / evidence | Blocks |
|---|---|---|---|---|
| 1 | `TryGetPlatformHandle()` → NSWindow ptr, descriptor "NSWindow" on macOS | **VERIFIED** — runtime-confirm in Slice A stays | docs.avaloniaui.net/xpf/interop/native-window-handles (literal `case "NSWindow":`); Avalonia.Native `WindowBaseImpl.mm` `ObtainNSWindowHandle` returns the NSWindow | A |
| 2 | GitHub macos runner TCC.db editable via sqlite for Accessibility/ScreenCapture grants | **VERIFIED (recipe exists; image-sensitive)** | actions/runner-images #7792 (workflow recipe), #8951 (`kTCCServiceScreenCapture` insert; also documents a case where the grant didn't take effect — keep continue-on-error), #8214 (Accessibility/AppleEvents insert) | B/C/D input asserts |
| 3 | `kCGScreenSaverWindowLevel` overlay coexists acceptably with menu bar/Dock/Notification Center | **UNCERTAIN** — real-desktop judgment; level value 1000 itself VERIFIED (SDK `CGWindowLevel.h`) | headed CI + real-Mac manual | B |
| 4 | Avalonia.Native maps `Topmost` to floating level and does NOT re-set `level` after we change it | **VERIFIED (mapping)** — `SetTopMost` → `setLevel:NSFloatingWindowLevel/NSNormalWindowLevel`; no spontaneous re-set found in source, BUT any `Topmost` property write re-fires it → re-assert level after `Topmost` writes; keep the Slice B watch | github.com/AvaloniaUI/Avalonia `native/Avalonia.Native/src/OSX/WindowBaseImpl.mm` | B |
| 5 | Avalonia `Dispatcher.UIThread` == AppKit main thread on macOS | **VERIFIED** — Avalonia has one main UI thread; the macOS backend runs NSApplication on the process main thread (AppKit enforces via main-thread checker) | docs.avaloniaui.net/docs/app-development/threading; Avalonia.Native architecture | A |
| 6 | Explicit `setIgnoresMouseEvents:` disables implicit per-transparent-pixel pass-through | **VERIFIED** — tri-state behavior documented verbatim by Loom's production workaround | github.com/loomhq/ElectronMacOSClickThrough README | C (debugging only) |
| 7 | Global `NSEvent` mouse-moved monitors are permission-free (unlike key monitors) | **VERIFIED** — mouse-move global monitors fire with `AXIsProcessTrusted()==false`; only key monitors need Accessibility | stackoverflow.com/questions/49716420; Apple "Monitoring Events" (Cocoa Event Handling Guide) | C |
| 8 | Avalonia `ShowActivated=false` prevents app activation on macOS capture-clicks | **UNCERTAIN (show-path VERIFIED)** — `Show(activate:false)` uses bare `orderFront:` (no makeKey/activate) per source; CLICK-time activation behavior still needs the Slice D assert | Avalonia.Native `WindowBaseImpl.mm` `Show()` | D |
| 9 | `sharingType=none` honored by ScreenCaptureKit + CGWindowList capture on macOS 13/14/15 | **REFUTED for macOS 15+** — SCK ignores `sharingType`; only legacy CG capture still honors it; Apple calls `.none` a legacy constant. §3.7 redesigned to best-effort | developer.apple.com/forums/thread/792152; tauri-apps/tauri#14200; developer.apple.com/documentation/appkit/nswindow/sharingtype-swift.enum/none | D |
| 10 | `fullScreenAuxiliary` + high level draws over fullscreen apps' Spaces | **UNCERTAIN** — real-Mac manual (CI can't fullscreen a third-party app); flag value 1<<8 VERIFIED (SDK header) | AppKit `NSWindow.h`; manual row | B (manual row) |
| 11 | No scriptable multi-display on hosted macOS runners | **UNCERTAIN (no contrary evidence found)** — treat as true; manual row stands | runner-images hardware docs | E (manual row) |
| 12 | Quartz level constants: floating=3, status=25, popup=101, screensaver=1000 | **VERIFIED** | AppKit/CG SDK headers (`CGWindowLevel.h` values; `NSWindow.h` maps NS levels to kCG levels) | B |
| 13 | `objc_msgSend` P/Invoke from .NET 8 on osx-arm64/x64; no `_stret` variant on arm64 | **VERIFIED (feasibility)** — .NET officially intercepts P/Invokes to the objc_msgSend family; cast-per-signature is the documented pattern; arm64 ABI has no stret split | learn.microsoft.com/dotnet/ios/advanced-concepts/exception-marshaling; mikeash.com "objc_msgSend's New Prototype" | A |
| 14 | `NSEvent.mouseLocation` readable with zero TCC | **VERIFIED** — used permissionlessly in mainstream samples; no TCC service gates cursor position | stackoverflow.com/questions/31931403 (and ubiquitous usage) | C |

### 7.2 Genuine Unknowns (real-desktop only)

| Risk | Impact | Mitigation |
|---|---|---|
| Poller boundary race (click within one tick of crossing) | Rare mis-routed click | 16ms cadence + hysteresis; Slice F unpark criteria if user-visible |
| Spaces switching mid-session (overlay on all Spaces but masks per-Space state?) | Stale capture behavior after Space switch | `stationary`+`canJoinAllSpaces`; manual checklist row |
| Mixed-DPI multi-monitor mask math (Retina + external 1x) | Click-through holes offset on one monitor | Per-window `RenderScaling` at conversion; manual mixed-DPI row |
| macOS version drift (13→15 window-server behavior) | Level/sharing semantics shift | Per-version manual matrix in §7.3 as reports arrive |
| App Nap / timer coalescing throttling the 16ms poller | Sluggish boundary response when app inactive | `NSProcessInfo` activity assertion around the tracker if observed |

### 7.3 macOS-Version-Specific Bugs (track as found)

| macOS | Bug | Workaround |
|---|---|---|
| (none yet) | — | — |

---

## 8. CI Verification Matrix

| Slice | macos-smoke (headed, no TCC) | macos-smoke (TCC-granted input lane) | Manual (real Mac) |
|---|---|---|---|
| A Handle+Selector+Fallback | Required | — | — |
| B Topmost+Spaces+full CT | Required (CGWindowList layer assert) | Click pass-through assert (continue-on-error until proven) | Fullscreen-app coverage; menu-bar/Dock interplay |
| C Per-region | Unit tests required | Required (positional asserts, both directions) | Boundary-race feel check |
| D Sharing+Focus | Required | Capture-exclusion + focus asserts | Real screen-share app |
| E Multi-monitor | Single-monitor regression only | — | Required (incl. mixed DPI) |
| F (parked) | — | — | — |

CI job addition: one `macos-smoke` job on `macos-latest` extending the existing macOS
build job (`.github/workflows/build.yml` desktop matrix) — build, TCC-grant setup step
(sqlite recipe, continue-on-error), run per-slice `--verify-*` switches. The lane is
SHARED with the title/framesource contracts (one job hosts all three suites).

---

## 9. Summary

- **One window system, one real backend:** NSWindow property control via `objc_msgSend`
  on the VERIFIED `TryGetPlatformHandle()` → "NSWindow" handle; properties only, never
  subclass/swizzle (SetWindowSubclass-ban mirror); AppKit main-thread rule normative.
- **Topmost is level-based and stable:** `kCGScreenSaverWindowLevel` + all-Spaces
  `collectionBehavior`; no reassertion timer unless CI proves Avalonia resets levels.
- **Per-region click-through mechanism NAMED:** dynamic `ignoresMouseEvents` toggling
  from a permission-free `NSEvent.mouseLocation` poller (16ms, hysteresis, Y-flip
  math); event taps REJECTED (TCC Accessibility); hitTest overrides REJECTED (cannot
  forward to other apps); input-catcher child windows PARKED as Slice F.
- **Zero TCC for the overlay** — a hard constraint the mechanism selection satisfies;
  capture exclusion via `sharingType=none` is **best-effort**: full on macOS ≤ 14 and
  against legacy CG capture, but IGNORED by ScreenCaptureKit on macOS 15+ (web-refuted
  §3.7/§7.1 row 9) — still better than Linux (which has nothing), honestly documented
  as a parity gap.
- **Never-trap rule inherited:** degrade hides the surface; poller failure fails OPEN
  (fully ambient), never trapping.
- **CI:** headed macos-smoke lane on macos-latest with permission-free CGWindowList
  window-state asserts + TCC-granted synthetic-input asserts (sqlite TCC.db recipe,
  verified to exist); multi-monitor and fullscreen coverage honestly deferred to a
  real-Mac manual checklist.
- **Slices:** A (handle/selector/fallback) → B (level/Spaces/full CT) → C (per-region
  tracker) → D (sharing/focus) → E (multi-monitor); F parked. **5 active + 1 parked.**

---

## Sources

- `CCP.Core/Platform/IOverlaySurface.cs`;
  `CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:24-40`;
  `CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs:11-20`;
  `CCP.Avalonia/Compositor/CompositorWindow.axaml.cs:98-175`;
  `CCP.Avalonia/Platform/AvaloniaPlatformCapabilities.cs`
- `docs/linux-overlay-contract.md` — template, never-trap rule, mask semantics,
  shared-infrastructure conventions
- `docs/linux-macos-readiness-map.md` — governing principle
- `overlay-clickthrough` skill — per-region contract (2026-07-09 team review)
- `.pi/skills/avalonia-research/SKILL.md` — v12 verification protocol

### 7.1-VERIFIED (web research, 2026-07-12 — drafting pass + full §7.1 verify pass)
| Claim | Result | Source |
|-------|--------|--------|
| Avalonia macOS handle | **VERIFIED** — `TryGetPlatformHandle()` returns the NSWindow pointer; `HandleDescriptor == "NSWindow"` (docs show the exact `case "NSWindow":` switch alongside "HWND"/"XID") | docs.avaloniaui.net window-handles + native-interop; AvaloniaUI/Avalonia discussion #13174 |
| GitHub macOS runner TCC grants scriptable | **VERIFIED (recipe exists)** — `sudo sqlite3` inserts into system+user `TCC.db` (e.g. `kTCCServiceScreenCapture`) work on runner images; Sonoma adds 4 schema columns; SIP status varies by image version — treat as image-sensitive, prove per image | actions/runner-images #9529 (working recipe), #7792, #1567, #8951 |
| Avalonia `Topmost` mapping + collectionBehavior interference | **VERIFIED** — `SetTopMost` → `setLevel:NSFloatingWindowLevel(3)/NSNormalWindowLevel(0)`; `Show()` save/strip-FullScreenPrimary/restore on `collectionBehavior`; `SetParent` overwrites `collectionBehavior` with `FullScreenAuxiliary` only (→ §3.5 rules: set behavior after Show, never parent overlays) | AvaloniaUI/Avalonia `native/Avalonia.Native/src/OSX/WindowBaseImpl.mm` (fetched 2026-07-12) |
| `sharingType=none` vs ScreenCaptureKit | **REFUTED for macOS 15+** — SCK captures the composited framebuffer and ignores `sharingType`/content-protection flags (Apple-confirmed; Electron/Tauri track it as upstream-unfixable); legacy CG capture still honors it; Apple docs call `.none` "a legacy constant that macOS no longer uses" | developer.apple.com/forums/thread/792152; tauri-apps/tauri#14200; electron/electron#48258; developer.apple.com/documentation/appkit/nswindow/sharingtype-swift.enum/none |
| collectionBehavior flag raw values | **VERIFIED** — `CanJoinAllSpaces=1<<0`, `Stationary=1<<4`, `IgnoresCycle=1<<6`, `FullScreenAuxiliary=1<<8`; sharingType `None=0/ReadOnly=1/ReadWrite=2` | AppKit SDK `NSWindow.h` (phracker/MacOSX-SDKs mirror, macOS 11.3 SDK) |
| Window level values | **VERIFIED** — screensaver=1000, popup=101, status=25, floating=3 | SDK `CGWindowLevel.h` mirrors (ampede, xpwn); stackoverflow.com/questions/24168257 |
| Mouse-moved global monitors + `mouseLocation` permission-free | **VERIFIED** — mouse-move monitors and `NSEvent.mouseLocation` need no TCC; only key-event monitors require Accessibility | stackoverflow.com/questions/49716420; Apple Cocoa Event Handling Guide "Monitoring Events" |
| Explicit `setIgnoresMouseEvents:` kills per-pixel pass-through | **VERIFIED** | github.com/loomhq/ElectronMacOSClickThrough (production Electron workaround built on exactly this behavior) |
| objc_msgSend P/Invoke viability from .NET | **VERIFIED** — objc_msgSend-family P/Invoke is an officially recognized .NET interop path; per-signature typed declarations; arm64 has no `_stret` | learn.microsoft.com/dotnet/ios/advanced-concepts/exception-marshaling; mikeash.com/pyblog/objc_msgsends-new-prototype.html |
