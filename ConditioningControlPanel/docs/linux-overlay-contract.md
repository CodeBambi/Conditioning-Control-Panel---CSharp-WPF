# Linux Overlay Implementation Contract

**Date:** 2026-07-12 (hardened judgment-tier revision — supersedes the same-day draft)  
**Scope:** WS4/task-board row #5 — Linux overlay click-through bring-up  
**Status:** Authoritative design contract (read-only research artifact; no code changes)

This document specifies the behavior contract, backend architecture, and implementation
slices for Linux overlay support in the Avalonia head. X11 and Wayland are CO-EQUAL
first-class backends. Per owner directive (2026-07-12), the implementation **must work
on ANY Linux system** — multi-backend, runtime-selected, graceful fallback, CI-verified
per backend.

> **Revision note:** this version fixes the same bug classes the judgment pass found in
> the sibling contracts (framesource, foreground-title), which were drafted from the
> same template: (1) the Wayland session predicate used AND instead of OR — a Wayland
> session exposing only `WAYLAND_DISPLAY` (including this doc's own weston CI recipe)
> was misrouted to X11/fallback; (2) **no Xlib error trap** — the default Xlib error
> handler TERMINATES THE PROCESS, and `XFixesSetWindowShapeRegion`/`XSendEvent` on a
> just-destroyed window is a guaranteed eventual `BadWindow`; (3) no display-ownership
> or threading model stated; (4) the Wayland design contained two protocol-fatal
> errors: a second `wl_display` connection cannot manipulate Avalonia's surfaces
> (Wayland objects are per-connection, unlike X11 XIDs), and wrapping Avalonia's
> xdg_toplevel surface with layer-shell violates wl_surface role exclusivity — the
> compositor responds with a protocol error that **disconnects the client and kills the
> whole app**; (5) the "GNOME may ignore input regions" claim was wrong (input region
> is core `wl_surface` state every compositor honors — GNOME's real gap is topmost);
> (6) the fallback degrade "overlay captures all input; user must close overlay"
> violated the product rule *disable the overlay, never trap input*; (7) the
> multi-monitor Xvfb recipe created X protocol screens, not RandR monitors. Claims not
> verifiable from repo code or protocol text are marked **[confidence: …]** — §7.1 is
> the verify-before-slice table.
>
> **In-flight code note:** slices A–D were partially implemented against the draft in
> worktree `.pi/worktrees/overlay` (uncommitted). §6.0 lists the corrections that code
> needs before it may land. The most severe: `WaylandLayerShellBackend` reports
> `SupportsPerRegionInputShape = true` while all input-region methods are logging stubs
> — on sway that is an invisible fullscreen window that traps the entire desktop.

---

## 1. IOverlaySurface Behavior Contract

The `IOverlaySurface` interface (`CCP.Core/Platform/IOverlaySurface.cs`) defines the
platform-agnostic overlay window seam.

### 1.1 Interface Definition

```csharp
// CCP.Core/Platform/IOverlaySurface.cs (complete)
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

The in-flight Linux seam additionally introduces `ILinuxOverlayBackend`
(`CCP.Core/Platform/ILinuxOverlayBackend.cs`, uncommitted) with
`SetInputCaptureRegions(IReadOnlyList<PixelRect>)` — the per-region mask entry point —
plus truthful capability flags `SupportsPerRegionInputShape` / `SupportsTopmost`.
`LinuxOverlaySurface` exposes `SetInputCaptureRegions` alongside the `IOverlaySurface`
members. Capability flags are a **contract**: a backend MUST NOT report a capability it
does not deliver (see §6.0 — the current Wayland stub violates this).

### 1.2 Behavioral Requirements (cited from the Windows head)

| Requirement | Windows Implementation | Citation | Linux disposition |
|-------------|------------------------|----------|-------------------|
| **Topmost** | `Topmost = true` in base ctor; `HWND_TOPMOST` reassertion | `AvaloniaOverlaySurface.cs:14`, `CompositorWindow.axaml.cs:149-175` | §3.2 (X11 EWMH), §4 (Wayland: compositor-discretionary) |
| **Click-through toggle** | `SetClickThrough(true)` ORs `WS_EX_TRANSPARENT \| WS_EX_LAYERED`; `false` clears `TRANSPARENT` | `WindowsOverlaySurface.cs:24-40` | §3.3 input shape / §4 input region |
| **Focus non-stealing** | `ShowActivated=false`, `Focusable=false`, `IsHitTestVisible=false`; `WS_EX_NOACTIVATE` | `AvaloniaOverlaySurface.cs:17-20` | Avalonia flags + `_NET_WM_STATE_SKIP_*`; Wayland: layer-shell keyboard-interactivity NONE where applicable |
| **Taskbar/Alt-Tab exclusion** | `ShowInTaskbar=false`; `WS_EX_TOOLWINDOW` | `AvaloniaOverlaySurface.cs:15` | `_NET_WM_STATE_SKIP_TASKBAR` + `_SKIP_PAGER` (X11); N/A concept on most Wayland shells |
| **Transparency** | `WindowDecorations=None`, `TransparencyLevelHint=[Transparent]`, transparent background | `AvaloniaOverlaySurface.cs:11-13` | Same Avalonia flags; X11 needs a running compositor for alpha — without one the overlay is opaque black: detect `_NET_WM_CM_S0` selection owner and treat "no compositor" as degrade (§2.4) |
| **Screen-capture exclusion** | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` for brain-drain only | `CompositorWindow.axaml.cs:115-120` | **No Linux equivalent** (X11 or Wayland). Documented degrade: excluded-on-Windows layers WILL appear in Linux screen shares. Do not fake it |
| **Multi-monitor** | One window per monitor; `SetBounds(PixelRect)` covers the physical screen rect | `CompositorEngine.cs:127-134` | §3.6 / §4.6 |
| **Topmost reassertion** | Re-pin on 500ms probe / 5s force | `CompositorWindow.axaml.cs:149-175` | §3.2 re-send client message; Wayland: no client-side reassertion exists |

### 1.3 Per-Region Click-Through Contract (2026-07-09 team review, owner reconfirmed 2026-07-12)

The compositor window is NOT uniformly click-through. Input disposition is PER-REGION:

| Layer Type | Examples | Input Behavior |
|------------|----------|----------------|
| **Ambient (pass-through)** | `PinkTintLayer` (theme color filter), `SpiralLayer` — these two ONLY | Clicks pass to apps beneath |
| **Capture (absorb)** | `VideoLayer`, `FlashLayer`, `SubliminalLayer`, `BrainDrainLayer`, `BouncingTextLayer`, `BubbleLayer`, keyword highlight, all Chaos FX | Clicks captured by overlay |

**Windows mechanism (reference):** window stays `WS_EX_TRANSPARENT|LAYERED` always; a
global `WH_MOUSE_LL` hook swallows clicks inside the per-frame **capture mask** (union
of active non-ambient layer painted regions) and passes the rest.

**Linux mechanism (different, and better where available):** the capture mask is applied
NATIVELY as the window's input shape/region — the window genuinely accepts input only
inside the mask; everything else passes at the display-server level. Consequences the
draft missed:

- **No global mouse hook is needed on Linux** for the mask itself. There is no portable
  Linux equivalent of `WH_MOUSE_LL` anyway (X11 grabs are intrusive; Wayland forbids
  global hooks by design).
- **Interactive layers get REAL pointer events** over captured regions (the window
  accepts input there). The base overlay window sets `IsHitTestVisible = false`
  (framework level); on Linux, interactive layers (bubbles, clickable flash) need that
  re-enabled for their visuals or handled at the window level — an explicit Slice C
  design task, not an accident. Non-interactive capturing layers (subliminal,
  brain-drain) simply let the window absorb the click.
- **Mask semantics (normative precedence, matches the in-flight code):**
  - `SetClickThrough(false)` → full-window input (capture everything), regardless of regions.
  - `SetClickThrough(true)` + capture regions present → input shape = union of capture regions.
  - `SetClickThrough(true)` + no capture regions → empty input shape (fully ambient).
- **Coordinate space:** X11 input shapes are **window-local** coordinates, not screen
  coordinates, and in **physical pixels**. The compositor's capture mask must be
  translated from screen space by the window origin and scaled by the window's
  `RenderScaling` if the mask is produced in logical units. Getting this wrong shifts
  the click-through holes — a CI-visible bug (Slice C asserts positions).

### 1.4 The Never-Trap Rule (normative — replaces the draft's degrade)

Product rule (crossplatform plan §7.4 / overlay-clickthrough skill): **degrade by
disabling the overlay, never by trapping input.** A fullscreen, transparent, topmost
window whose click-through silently no-ops locks the user out of their desktop behind an
invisible wall — the worst possible failure for an ambient app.

Therefore, for every backend:

- A backend that cannot honor `SetClickThrough(true)` MUST NOT display the surface while
  ambient mode is requested. `Show()` while `_clickThroughEnabled == true` on such a
  backend hides/refuses and logs once. Full-capture mode (`SetClickThrough(false)`,
  e.g. mandatory video) MAY show — capturing input is then the intended behavior.
- `AvaloniaPlatformCapabilities.SupportsClickThrough` must report the SELECTED backend's
  real capability (today it hardcodes `IsWindows` — flip it per-backend when Slice A
  lands), so ambient features gate themselves off upstream.
- The draft's §4.4 "overlay captures all clicks; user must close overlay; mitigation:
  minimize hotkey" is REJECTED. There is no acceptable version of an invisible
  fullscreen input trap.

---

## 2. Linux Backend Architecture

### 2.1 Ground truth for selection: the Avalonia platform handle, not the session env

The draft selected backends from environment variables alone. Environment variables
select which *session* you are in; they do NOT tell you which windowing system
**Avalonia actually used** for the overlay window — and that is what determines which
native API can touch it:

- If `Window.TryGetPlatformHandle()?.HandleDescriptor == "XID"`, the window is an X11
  window (bare X11 session, or XWayland inside a Wayland session) and the X11 machinery
  (XFixes shape, EWMH) applies — **including on Wayland sessions**.
- Any other descriptor (or a Wayland surface handle, if Avalonia v12 exposes one) means
  X11 calls against this window are meaningless.

**The decisive unverified fork [confidence: low — VERIFY FIRST, §7.1 row 1]:** Avalonia
11 shipped **X11 as the only production Linux desktop windowing backend** (Wayland
sessions ran the app through XWayland). Whether Avalonia 12.0.5 (`Avalonia.Desktop`
12.0.5, the version this head pins) adds a native Wayland backend — and if so, whether
`TryGetPlatformHandle()` exposes the `wl_surface`/`wl_display` — is not verifiable from
this repo and is exactly the kind of v12 fact the avalonia-research skill exists for.
Everything in §4 branches on this:

- **Branch W-X (Avalonia is X11-only):** on Wayland sessions our windows are XWayland
  windows. The X11 input-shape backend is the universal click-through path; §4's native
  Wayland backends are impossible for Avalonia-created windows and stay parked.
- **Branch W-N (native Wayland backend exists):** the X11 path does not apply to those
  windows; input regions must go through Avalonia's OWN Wayland connection (§4.2).

### 2.2 Runtime Backend Selection (predicate corrected)

Session detection reuses `CCP.Core/Platform/LinuxSessionType.cs` +
`LinuxSessionDetector.cs` (in-flight, worktree `.pi/worktrees/overlay` — pure
env-var logic with unit tests). Its predicate is already the corrected OR form: Wayland
= `XDG_SESSION_TYPE == "wayland"` **OR** `WAYLAND_DISPLAY` set; X11 = `XDG_SESSION_TYPE
== "x11"` OR `DISPLAY` set; both → `XWayland`. The draft's AND predicate
(`XDG_SESSION_TYPE == "wayland"` **and** `WAYLAND_DISPLAY`) is superseded — under it, a
compositor reachable only via `WAYLAND_DISPLAY` (weston/sway headless CI, systemd user
sessions that don't export `XDG_SESSION_TYPE`) fell through to the X11-or-fallback arm.

```
LinuxOverlayBackendSelector (selection at overlay init; re-checked against the
                             actual platform handle when the first window opens)
  1. Session = Wayland or XWayland
       → if Avalonia platform handle == "XID" (Branch W-X: XWayland window)
            → X11 probe (§3) — XFixes shape on the XWayland window
         else (Branch W-N: native Wayland window)
            → Wayland native probe (§4.2)
       → probe failed → SafeDegrade (§2.4)
  2. Session = X11
       → X11 probe (§3)
       → probe failed → SafeDegrade
  3. Session = Unknown
       → SafeDegrade
```

Note the deliberate asymmetry with the sibling contracts: the title/framesource
contracts BAN X11 fallback from a Wayland probe (XWayland data is *misleading* there).
For the overlay the XWayland path is not misleading — under Branch W-X the overlay
window IS an XWayland window, and shaping it is the only lever we have. What remains
compositor-dependent under XWayland is (a) topmost and (b) whether pass-through reaches
**native Wayland** windows beneath (§3.5).

### 2.3 Backend Fallback Chain

| Priority | Backend | Capabilities | When Selected |
|----------|---------|--------------|---------------|
| 1 | `X11InputShapeBackend` | Full: EWMH topmost + per-region XFixes input shape | Handle descriptor "XID" (bare X11 AND XWayland) with XFixes ≥ 2 |
| 2 | `WaylandNativeInputRegionBackend` | Partial: per-region input via Avalonia's own connection; topmost compositor-discretionary | Branch W-N only — Avalonia native Wayland window with accessible `wl_surface` (§4.2 gate) |
| 3 | `WaylandLayerShellBackend` | Full-strength on wlroots+KDE, **but requires an own-surface render path** (§4.3) | PARKED — see role-exclusivity ruling. Do not implement by wrapping Avalonia's surface |
| 4 | `SafeDegrade` (policy, not a window) | Ambient overlay features OFF; full-capture overlays may show | Any session where no backend can honor click-through |
| 5 | `FallbackBackend` | Plain Avalonia window, `Topmost=true`, honest `SupportsPerRegionInputShape=false` | The window SafeDegrade uses for full-capture-only overlays |

**Never-hard-fails guarantee (audited against the in-flight code):**

- Every backend probe wraps its P/Invokes in `try/catch (DllNotFoundException,
  EntryPointNotFoundException, Exception)` — missing `libXfixes.so.3` /
  `libwayland-client.so.0` on a minimal distro must select the next tier, not crash.
  (In-flight code does this. Keep it.)
- .NET exceptions are NOT the whole story on X11: **Xlib errors are not exceptions** —
  they invoke the process-global error handler, whose default **exits the process**.
  The try/catch in the in-flight `ApplyInputShape` catches nothing for a `BadWindow`.
  §3.1's error trap is therefore part of the no-crash guarantee, not an optimization.
- `FallbackBackend` makes zero P/Invokes (pure Avalonia — verified in-flight) and
  always constructs. The selector's terminal arm must be `catch { return new
  FallbackBackend(reason) }` around every probe/construction so no probe exception
  escapes.
- `LinuxOverlaySurface` wraps backend calls; if even the fallback window cannot be
  created (no display server at all — headless CI), overlay operations become no-ops
  with one logged reason. Avalonia itself would normally have failed startup earlier
  in that environment; the seam still must not throw from `Show()`.

### 2.4 SafeDegrade policy

When no backend can honor click-through (Branch W-N with no surface access; XFixes
missing; unknown session; no X compositor for transparency):

- `SupportsClickThrough` reports false → ambient conditioning features (tint, spiral,
  ambient flash/subliminal placement over the desktop) are gated OFF upstream.
- Full-capture overlay uses (`SetClickThrough(false)` — mandatory video, lock card
  interactions) still work via `FallbackBackend`.
- One log line + one non-modal user notice ("ambient overlay effects are not available
  on this desktop environment"), never a prompt loop, never an invisible input trap
  (§1.4).

### 2.5 Seam Structure (aligned with the in-flight layout)

```
CCP.Core/Platform/
├── IOverlaySurface.cs              # UNCHANGED
├── ILinuxOverlayBackend.cs         # in-flight: backend abstraction (+ capability flags,
│                                   #   SetInputCaptureRegions). ADD: IDisposable
├── LinuxSessionType.cs             # in-flight (X11 | Wayland | XWayland | Unknown)
└── LinuxSessionDetector.cs         # in-flight (pure, unit-tested, OR-predicate)

CCP.Avalonia.Desktop.Linux/Platform/
├── LinuxOverlayBackendSelector.cs  # in-flight — needs §6.0 corrections
├── LinuxOverlaySurface.cs          # in-flight: IOverlaySurface + SetInputCaptureRegions
├── Backends/
│   ├── X11InputShapeBackend.cs     # in-flight — needs §6.0 corrections (error trap, lock)
│   ├── WaylandNativeInputRegionBackend.cs  # Branch W-N only (gated, §4.2)
│   ├── WaylandLayerShellBackend.cs # in-flight stub — PARKED; must not land as-is (§6.0)
│   └── FallbackBackend.cs          # in-flight — needs §1.4 never-trap behavior
└── Interop/
    ├── X11Interop.cs               # in-flight: Xlib/XFixes P/Invoke — shared with the
    │                               #   framesource/title contracts; extend, do not fork
    └── WaylandInterop.cs           # in-flight: probe-only registry bindings (§4.1 rules)
```

The Xlib scoped-error-trap helper and (if Branch W-N materializes) the Wayland dispatch
thread are SHARED infrastructure with `linux-framesource-contract.md` §3.2/§4.1 and
`linux-foreground-title-contract.md` §3.1 — one implementation per process, whichever
slice lands first builds it.

---

## 3. X11 Backend Design

### 3.1 Connection, Threading, and Error Trap (mandatory)

- **Own connection:** one dedicated `XOpenDisplay(null)` per backend instance; close on
  dispose. NEVER touch Avalonia's internal `Display*` or pump its event queue —
  Avalonia's X11 backend owns its connection and event loop; poking it races both.
  Operating on the Avalonia window's XID from OUR connection is legal and race-free at
  the protocol level: X11 windows are server-side resources addressable from any client
  connection (this is exactly why the two-connection design that is INVALID on Wayland
  (§4.1) is correct on X11).
- **Serialization:** Xlib is not thread-safe without `XInitThreads()`, and
  `XInitThreads` must be the first Xlib call in the process — un-guaranteeable next to
  Avalonia's own X11 use. Do not rely on it; **serialize all access to our display
  behind one lock**. This matters in practice: `SetInputCaptureRegions` arrives from
  the compositor tick, `SetClickThrough`/`Show` from the UI thread (the in-flight code
  takes no lock — §6.0).
- **Scoped error trap (process-kill risk):** the default Xlib error handler calls
  `exit()`. Realistic non-exceptional errors here: `BadWindow` when the overlay window
  is destroyed between acquiring the XID and a shape/`XSendEvent` call (monitor
  hot-unplug tears down and recreates compositor windows — this WILL race eventually);
  `BadValue`/`BadMatch` from malformed rectangles; `BadRegion` from a stale region id.
  Required pattern (shared helper with the framesource contract §3.2):

```csharp
// XSetErrorHandler is PROCESS-GLOBAL (not per-Display). Install once, chain:
private static XErrorHandlerDelegate? _previous;   // keep a GC handle alive forever
private static int ErrorHandler(IntPtr display, ref XErrorEvent ev)
{
    if (display == _ourDisplay) { _lastError = ev.error_code; return 0; }  // swallow
    return _previous?.Invoke(display, ref ev) ?? 0;                         // chain
}
// Trap scope: _lastError = 0; <Xlib calls>; XSync(display, false); check _lastError.
```

  On a trapped error: skip that operation, log once, and if errors repeat demote the
  backend to SafeDegrade. **Chaining is not optional:** Avalonia's X11 backend installs
  its own error handler for its display **[confidence: medium — verify against
  Avalonia.X11 source; if it does and we clobber it without chaining, we break
  Avalonia's own error recovery]**.
- **Extension probe:** `XFixesQueryExtension` alone is insufficient — input-shape
  support requires XFixes protocol **version ≥ 2** (and the server's Shape extension,
  which every modern server and Xvfb has). Call `XFixesQueryVersion(display, 2, 0)`
  during the probe and require major ≥ 2 **[confidence: medium-high — fixesproto:
  region/shape entry points are v2 additions]**.

### 3.2 Topmost via `_NET_WM_STATE_ABOVE` (corrected)

The draft presented "Option A: XChangeProperty before mapping" and "Option B: client
message" as equals. EWMH is explicit: clients set `_NET_WM_STATE` via **property only
before mapping**; once the window is mapped (and ours is — we acquire the XID after
`Show()`), state changes MUST go through a **client message to the root window**, which
the WM processes. Post-map `XChangeProperty` of `_NET_WM_STATE` is ignored by compliant
WMs. The in-flight code already uses the client-message form — keep it; delete the
draft's Option A.

```csharp
// data.l = { _NET_WM_STATE_ADD, atom, 0, 1 /* source: normal app */, 0 }
XSendEvent(display, root, false, SubstructureRedirectMask | SubstructureNotifyMask, ref ev);
```

Additional rules:

- **Avalonia already asserts topmost on X11** via its own `Topmost = true` handling
  **[confidence: medium-high — Avalonia's X11 backend maps Topmost to
  `_NET_WM_STATE_ABOVE`]**. Our native path is for *reassertion* (fullscreen apps and
  focus changes can restack us) — mirror the Windows head's cadence: probe/reassert on
  a timer, re-send the ADD message; it is idempotent.
- Also send `_NET_WM_STATE_SKIP_TASKBAR` + `_NET_WM_STATE_SKIP_PAGER` (in-flight code
  does).
- **Override-redirect is NOT a runtime fallback** for an Avalonia-managed window:
  toggling `CWOverrideRedirect` requires unmap/remap of a window whose lifecycle
  Avalonia owns, bypasses the WM (no compositor stacking cooperation), and breaks
  focus semantics. If EWMH ABOVE proves ineffective on some WM, record it in §7.3 and
  accept degrade — do not fight Avalonia for the window.
- **Transparency needs a compositor:** on bare X11 without a compositing manager, an
  ARGB window renders opaque. Probe the `_NET_WM_CM_S0` (per-screen) selection owner;
  if none, treat as SafeDegrade for ambient overlays (an opaque fullscreen tint is a
  desktop lockout even with correct input shape? No — input shape still passes clicks;
  but the VISUAL is a solid wall. Degrade.) **[confidence: high — standard X
  composite-manager selection convention]**

### 3.3 Per-Region Click-Through via XFixes Input Shape

```csharp
// Inside the error trap, under the display lock:
// captureRegions: window-local, physical-pixel rects (§1.3 coordinate rules)
IntPtr region = XFixesCreateRegion(display, xrects, xrects.Length);  // 0 rects = empty = fully ambient
XFixesSetWindowShapeRegion(display, xid, ShapeInput /* 2 */, 0, 0, region);
XFixesDestroyRegion(display, region);
XSync(display, false);   // flush + collect trapped errors
```

- Empty region → every click passes through. Region = capture-rect union → clicks
  inside are delivered to the overlay (real Avalonia pointer events — §1.3
  interactivity note), outside pass through. `region = None (IntPtr.Zero)` → shape
  removed → full-window capture. All three are used by the §1.3 precedence semantics;
  the in-flight code implements exactly this precedence — keep it.
- **Server-side regions are cheap**; create/destroy per update is fine. Update on
  compositor tick only when the mask changed (dirty flag), coalesce bursts.
- `XRectangle` fields are `short`/`ushort` — clamp rect coords to ±32767 before
  narrowing (multi-monitor virtual desktops can exceed this on exotic layouts; a
  negative-origin monitor gives negative window-local coords only if translation is
  wrong — assert instead of clamp for those).
- Re-apply the shape after `SetBounds` and after window recreation (Avalonia can
  recreate the native window on some property changes — re-acquire the XID on each
  `Opened`, do not cache across `Close()`).

### 3.4 Additional X11 Requirements

| Requirement | X11 Implementation |
|-------------|-------------------|
| Focus non-stealing | Avalonia `ShowActivated=false`/`Focusable=false`; plus `WM_HINTS.input = False` if focus-steal is ever observed (do not set preemptively — Avalonia manages WM_HINTS) |
| Taskbar exclusion | `_NET_WM_STATE_SKIP_TASKBAR` + `_SKIP_PAGER` client messages (§3.2). Do NOT change `_NET_WM_WINDOW_TYPE` on an Avalonia-managed window — Avalonia sets it at creation, and DOCK-typing a window changes WM treatment wholesale (stacking, struts) |
| Transparency | ARGB visual comes from Avalonia's `TransparencyLevelHint`; compositor presence probe per §3.2 |
| Topmost reassertion | Re-send ABOVE client message on timer; optionally watch `_NET_CLIENT_LIST_STACKING` (requires selecting PropertyNotify on root on OUR display — safe, it's our connection) |

### 3.5 XWayland (Branch W-X — the honest capability statement)

Under XWayland the overlay window is an X11 window inside a Wayland compositor:

- **Input shape via XFixes works at the X server level** unconditionally: clicks
  falling in a pass-through hole are never delivered to our window, and reach any
  OTHER XWayland window beneath.
- Whether a pass-through click reaches a **native Wayland** window (or the desktop)
  beneath depends on the compositor's XWM translating the X input shape into the
  XWayland surface's `wl_surface` input region. wlroots and KWin do this; Mutter
  does this **[confidence: low-medium — the single most load-bearing unverified claim
  of Branch W-X; §7.1 row 3. CI can only prove the X11-level behavior]**.
- Topmost: `_NET_WM_STATE_ABOVE` is processed by the compositor's XWM; XWayland
  windows generally cannot stack above native Wayland fullscreen windows. Documented
  degrade: best-effort topmost on Wayland sessions.

### 3.6 Multi-Monitor (X11)

One overlay window per monitor (matches Windows). The X11 virtual desktop is ONE root
window spanning all XRandR monitors; `ScreenInfo`/`SetBounds` rects are absolute
physical pixels into that space. Window-local shape coordinates come from subtracting
the window origin (§1.3). Monitor hotplug: bounds updates arrive via the existing
`CompositorEngine` screen tracking; every bounds change re-applies the input shape; the
error trap absorbs the teardown races.

---

## 4. Wayland Backend Design

### 4.1 Hard protocol facts the draft got wrong (normative)

1. **Wayland objects are per-connection.** A `wl_surface` proxy is meaningful only on
   the `wl_display` connection that created it. Our own `wl_display_connect()` (used by
   the probe) can NEVER address, shape, or commit Avalonia's surfaces. A second
   connection is legitimate ONLY for registry probing (which globals exist) and for
   surfaces we create ourselves. The in-flight `ApplyInputRegion` design (own
   connection + Avalonia's window) is unsound and can never work — §6.0.
2. **Surface roles are exclusive and permanent.** Avalonia's window surface already
   holds the `xdg_toplevel` role. Calling `zwlr_layer_shell_v1.get_layer_surface` on it
   is a protocol error; the compositor **disconnects the client**, which kills every
   Avalonia window in the process — app-fatal, not a degrade. Wrapping Avalonia's
   surface with layer-shell is BANNED. **[confidence: high — wl_surface role rules and
   layer-shell's `role` error are protocol text]**
3. **`set_input_region` is core `wl_surface` state honored by every compositor** —
   GNOME included. The draft's "GNOME/Mutter may ignore input regions for security" is
   wrong; what GNOME lacks is layer-shell/topmost control, not input regions.
   **[confidence: high — wl_surface spec; double-buffered state applied on commit]**
4. **`wl_region` has copy semantics**: after `wl_surface.set_input_region` the region
   object can be destroyed immediately. The pending region takes effect on the next
   `wl_surface.commit` — which for an Avalonia-rendered surface happens on Avalonia's
   next frame; do not commit someone else's surface yourself (commit pairs with buffer
   attach state owned by the renderer). **[confidence: high — protocol text]**
5. **Listener structs are not copied.** `wl_registry_add_listener` (like all
   `wl_*_add_listener`) stores the POINTER to the listener vtable for the proxy's
   lifetime. The in-flight probe passes `ref` to a stack-local struct — a dangling
   pointer the moment the method returns (works until it corrupts). Listener vtables
   must live in native memory or pinned managed memory for the proxy's lifetime, and
   their delegates must be rooted (the in-flight code roots the delegates but not the
   struct). **[confidence: high — libwayland API contract]**
6. **Registry binding is a real call.** The in-flight probe records `new
   IntPtr((int)name)` as a placeholder "handle". Fine for existence-probing ONLY if it
   is never dereferenced as a proxy — but then `_wlCompositor` is a lie of the same
   shape as the capability lie. Probe = existence booleans; bind = `wl_registry_bind`
   with a real proxy and version min(server, supported); never mix the two.

### 4.2 Branch W-N: `WaylandNativeInputRegionBackend` (gated on the §2.1 fork)

Exists ONLY if Avalonia v12's Linux backend creates native Wayland windows AND exposes
enough to reach the surface. Requirements to un-gate this backend (all three, verified
at Slice D):

1. `TryGetPlatformHandle()` (or a v12 platform API) yields the `wl_surface*` (and
   ideally the `wl_display*`/`wl_compositor*` proxies) for a shown window
   **[confidence: low — unverified v12 surface; this is the gate]**.
2. Requests are issued on **Avalonia's connection** (fact 4.1-1). libwayland is
   thread-safe for issuing requests on a shared connection (per-object serialization
   inside libwayland) **[confidence: medium — verify libwayland thread-safety
   guarantees for multi-thread request submission]**; still, marshal our
   `set_input_region` calls onto the thread Avalonia uses for that connection if v12
   exposes a dispatcher hook, to avoid interleaving surprises.
3. The region takes effect on Avalonia's next commit (fact 4.1-4) — acceptable latency
   (≤ 1 frame).

Capabilities: per-region click-through YES (input region = capture mask, same §1.3
semantics; empty region = fully ambient). Topmost NO GUARANTEE: xdg_toplevel has no
above-state; Avalonia's `Topmost` is best-effort or a no-op on Wayland. Consequence:
on Branch W-N, ambient overlays work but can be occluded; document, don't fake.

If any requirement fails → this backend does not exist; Wayland sessions get Branch
W-X treatment (if Avalonia is XWayland) or SafeDegrade.

### 4.3 `WaylandLayerShellBackend` — PARKED (re-scoped from the draft)

wlr-layer-shell (`OVERLAY` layer, anchored to output, exclusive zone -1,
keyboard-interactivity NONE) is the only real Wayland topmost mechanism, and input
regions work on layer surfaces. But per fact 4.1-2 it CANNOT wrap an Avalonia window.
A genuine layer-shell backend means: our own connection, our own `wl_surface` +
layer-shell role, our own buffer path (wl_shm or EGL) — i.e. rendering compositor
layers OUTSIDE Avalonia into a raw surface. That is a rendering-architecture project
(the UCE draws through Avalonia/Skia today), not an overlay-seam slice.

**Ruling:** parked. Documented here so nobody "helpfully" lands the current stub or a
surface-wrapping variant (both are §6.0 rejects). Revisit only if (a) Branch W-N is
false AND XWayland pass-through (§3.5) proves broken on major compositors, or (b) the
UCE ever gains an offscreen-texture render path that could present into a raw surface.
KDE note for whenever this unparks: KWin ships `zwlr_layer_shell_v1` — the draft's
"KDE: layer-shell No" matrix row was wrong **[confidence: medium — verify with
`wayland-info` on Plasma; registry probe is authoritative at runtime]**.

### 4.4 GNOME / Mutter (corrected honest answer)

- Input regions: honored (core protocol — fact 4.1-3), so Branch W-N click-through
  works on GNOME.
- Topmost: nothing available to native clients (no layer-shell, no above-state, no
  sanctioned always-on-top D-Bus for arbitrary apps). Best-effort only.
- Branch W-X: XWayland shape translation per §3.5 with the Mutter caveat.
- Never invent Mutter-private D-Bus APIs (sibling lesson: the title draft fabricated a
  KDE D-Bus API; assume the same temptation here and resist it).

### 4.5 Compositor Compatibility Matrix (corrected; runtime probe is authoritative)

| Compositor | layer-shell | Input regions (core) | Topmost for us | Per-region click-through path |
|------------|-------------|----------------------|----------------|-------------------------------|
| sway / Hyprland / river / wayfire | Yes | Yes | layer-shell only (parked) | W-N input region, or W-X XWayland shape |
| KDE / KWin | Yes **[confidence: medium]** (draft said No) | Yes | layer-shell only (parked) | W-N input region, or W-X XWayland shape |
| GNOME / Mutter | No | **Yes** (draft said "limited" — wrong) | None | W-N input region, or W-X XWayland shape |
| Weston | No **[confidence: medium]** | Yes | None | same |

The matrix is documentation; the registry/handle probes at runtime are the logic.

---

## 5. Verification Fork Summary (what must be true for each path)

| Path | Load-bearing claims | Verified? |
|------|--------------------------------|-----------|
| X11 sessions | `TryGetPlatformHandle` → "XID"; XFixes ≥ 2 input shape | XID: **web-verified 2026-07-12** (siblings' §7.1-VERIFIED: `TopLevel.TryGetPlatformHandle()` → `.Handle` + `.HandleDescriptor == "XID"` on Linux). XFixes v2: [confidence: medium-high] |
| Wayland via Branch W-X | Avalonia v12 is X11-only on Linux; compositor XWMs translate input shape → surface input region | Both UNVERIFIED — §7.1 rows 1, 3 |
| Wayland via Branch W-N | Avalonia v12 native Wayland + surface access + same-connection region setting | UNVERIFIED — §7.1 rows 1, 2 |
| Everything else | SafeDegrade never traps input; FallbackBackend always constructs | Repo-auditable (§2.3) |

---

## 6. Implementation Slice Plan

Standard repo gates (slnf 0 errors, WPF 0 errors, Core tests ≥ floor, smoke) apply to
every slice in addition to listed CI. All files under
`CCP.Avalonia.Desktop.Linux/Platform/…` (§2.5).

### 6.0 Corrections required to the in-flight worktree code (BLOCKING before any commit)

The uncommitted `.pi/worktrees/overlay` implementation was written against the draft:

1. **`WaylandLayerShellBackend` must not land.** It reports
   `SupportsPerRegionInputShape = true` / `SupportsTopmost = true` while every
   input-region method is a comment + log line, and its probe design violates §4.1
   facts 1, 5, 6 (second-connection surface manipulation, stack-local listener struct,
   fake IntPtr "handles"). As-is it selects itself on sway and produces the exact
   §1.4 lockout. Replace with: probe-only registry existence check retained for
   diagnostics, backend removed from the chain until §4.2/§4.3 gates resolve.
2. **`X11InputShapeBackend`:** add the §3.1 scoped error trap (its `try/catch` blocks
   do not catch Xlib errors — `BadWindow` still kills the process), the display lock,
   `XFixesQueryVersion` ≥ 2 in the probe, XID re-acquisition on window recreation, and
   capture-region translation to window-local physical pixels.
3. **`FallbackBackend`:** implement §1.4 (hide/refuse while `SetClickThrough(true)` is
   requested) instead of the documented-lockout no-op; keep zero-P/Invoke purity.
4. **Selector:** wrap every probe in `try/catch → FallbackBackend`; drop the
   `_logger as ILogger<X11InputShapeBackend>` casts (always null — inject a logger
   factory); re-check the platform-handle descriptor once the first window opens
   (§2.1) rather than trusting env vars alone.
5. **`ILinuxOverlayBackend`:** add `IDisposable` (backends own an X display / wl
   connection); `LinuxOverlaySurface.Close()` disposes the backend.

### Slice A: Backend Selection + Fallback Window (Foundation) — partially in-flight

**Goal:** corrected runtime detection, SafeDegrade policy, honest fallback window.

**Files:** `LinuxSessionType.cs` + `LinuxSessionDetector.cs` (Core, in-flight),
`LinuxOverlayBackendSelector.cs`, `LinuxOverlaySurface.cs`, `Backends/FallbackBackend.cs`,
`Program.cs` DI registration, `AvaloniaPlatformCapabilities` per-backend
`SupportsClickThrough`.

**Verification (CI):**
```bash
# No display at all: selector must produce FallbackBackend without crashing;
# overlay Show() with click-through requested must refuse + log once (§1.4)
env -u DISPLAY -u WAYLAND_DISPLAY -u XDG_SESSION_TYPE \
  dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-overlay-fallback

# X11 via Xvfb + openbox
Xvfb :99 -screen 0 1920x1080x24 & export DISPLAY=:99; openbox & sleep 1
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-overlay-shows
```

**Acceptance:**
- [ ] Unit tests over env permutations incl. the XWayland case (both vars set) and the
      WAYLAND_DISPLAY-only case the draft's AND predicate misrouted
- [ ] SafeDegrade: `SetClickThrough(true)` on FallbackBackend → surface hidden, reason
      logged once, `SetClickThrough(false)` → shows and captures (intended)
- [ ] No backend reports a capability it does not implement (assert in a unit test
      against the backend list)
- [ ] Selector never throws: fault-injected probe (missing lib simulation) yields
      FallbackBackend

### Slice B: X11 Topmost + Full Click-Through + Error Trap

**Goal:** X11 overlay always-on-top and fully ambient (empty input shape), with the
process-kill risk closed.

**Files:** `Interop/X11Interop.cs` (extend: `XSync`, `XSetErrorHandler`,
`XFixesQueryVersion`; shared trap helper), `Backends/X11InputShapeBackend.cs`.

**Verification (CI):**
```bash
Xvfb :99 -screen 0 1920x1080x24 & export DISPLAY=:99
openbox &   # EWMH WM — REQUIRED for _NET_WM_STATE_ABOVE and getactivewindow
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-clickthrough &
sleep 3
xterm -geometry 80x24+100+100 & sleep 1
# xdotool uses XTEST: synthetic events go through server-side picking, so input
# shapes ARE exercised — a valid probe of pass-through.
xdotool mousemove 200 200 click 1; sleep 0.5
FOCUSED=$(xdotool getactivewindow getwindowname)
[[ "$FOCUSED" == *"xterm"* ]] || exit 1
# Negative test (error trap): have the smoke hook destroy the overlay window natively,
# then call SetClickThrough again — assert the process is still alive and logged a
# trapped BadWindow instead of dying.
```

**Acceptance:**
- [ ] `_NET_WM_STATE_ABOVE`/`SKIP_TASKBAR`/`SKIP_PAGER` via client message (post-map
      correct form); reassertion timer mirrors the Windows cadence
- [ ] XFixes probed with `XFixesQueryVersion` ≥ 2; probe failure → next tier
- [ ] Empty input region applied; clicks pass through to xterm (CI assert)
- [ ] BadWindow trapped → no process death (explicit CI negative test)
- [ ] All Xlib access serialized behind the lock; dedicated display closed on dispose
- [ ] No-compositor probe (`_NET_WM_CM_S0`) logged; Xvfb+openbox has no compositor —
      assert the degrade signal fires there (which doubles as the probe's test)

### Slice C: X11 Per-Region Input Shape

**Goal:** capture-mask integration matching §1.3, with coordinate correctness.

**Files:** `X11InputShapeBackend.cs` (region path),
`CCP.Avalonia/Compositor/CompositorEngine.cs` (expose capture-mask snapshot — reuse the
Windows mask builder; it is already per-frame and immutable), interactivity wiring for
captured regions (§1.3).

**Verification (CI):**
```bash
Xvfb :99 -screen 0 1920x1080x24 & export DISPLAY=:99; openbox & sleep 1
xterm -geometry 80x24+100+100 & sleep 1
# Smoke mode paints ONE capture rect at a known position (e.g. 600,400 200x200):
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-capture-region &
sleep 3
xdotool mousemove 700 500 click 1   # inside capture rect → overlay receives it
xdotool mousemove 200 200 click 1   # outside → passes to xterm
# Smoke test asserts: overlay got exactly one pointer event at ~(700,500) — this
# simultaneously proves capture AND coordinate translation/DPI scaling; xterm focused
# after the second click.
```

**Acceptance:**
- [ ] Mask → window-local physical-pixel `XRectangle[]` (translation + `RenderScaling`)
- [ ] §1.3 precedence semantics exact (empty/union/None region three-state)
- [ ] Shape updates only on mask change (dirty flag), re-applied after `SetBounds`
- [ ] Positional CI assertion (not just "a click was captured somewhere")
- [ ] Interactive-layer input path decided and tested (window receives real pointer
      events over captured regions)

### Slice D: The Wayland Fork — verify, then wire Branch W-X or W-N

**Goal:** resolve §2.1 row 1 empirically and land the correct Wayland-session behavior.
This is a research + CI slice first, code second.

**Step 1 (gate):** on a Wayland CI compositor, start the head and log
`TryGetPlatformHandle()?.HandleDescriptor` + whether Avalonia connected via XWayland
(e.g. `DISPLAY` used, XID descriptor) or natively. Record the answer in this doc's §7.1.

**Step 2a (Branch W-X confirmed):** Wayland sessions route to the X11 backend against
the XWayland window (§2.2). CI proves X11-level pass-through under a Wayland
compositor; §3.5's native-window pass-through claim gets a manual checklist row per
compositor (sway, KWin, Mutter).

**Step 2b (Branch W-N confirmed):** implement `WaylandNativeInputRegionBackend` per
§4.2 (Avalonia-connection region setting; correct listener lifetimes per §4.1-5 for any
proxies we hold). Topmost = documented best-effort.

**Verification (CI):**
```bash
# sway headless; xwayland enabled so BOTH branches can start the app
export WLR_BACKENDS=headless WLR_LIBINPUT_NO_DEVICES=1 XDG_RUNTIME_DIR=/tmp/xdg
sway -c /dev/null & sleep 2
export WAYLAND_DISPLAY=$(ls $XDG_RUNTIME_DIR | grep '^wayland-[0-9]$' | head -1)
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-overlay-fork
# Assert: fork answer logged; selected backend consistent with the answer; overlay
# shows; ambient mode either works (input region/shape proven via a positional
# synthetic-input assertion where the compositor supports it) or SafeDegrades — never
# an invisible trap (negative assertion: a click at an ambient-only position must not
# be swallowed while degrade is active, because the surface must be HIDDEN).
```
Note: do NOT export `XDG_SESSION_TYPE` in this recipe — that IS the regression test for
the corrected OR-predicate.

**Acceptance:**
- [ ] Fork answer recorded in §7.1 with the CI log as evidence
- [ ] Selected-backend decision keyed off the actual handle descriptor
- [ ] Whichever branch: no capability lie, no §1.4 violation, sway CI green

### Slice E: Wayland Degrade UX

**Goal:** the SafeDegrade user experience on sessions where ambient overlays are off.

**Files:** `LinuxOverlaySurface.cs` / settings surface: one non-modal notice, settings
text ("ambient overlay effects are unavailable on this desktop environment"),
capability plumbed to feature gates.

**Verification (CI):** weston headless (no layer-shell; if Branch W-X, run
`weston --xwayland` so the app can start **[confidence: medium-high — weston's
xwayland flag; verify the ubuntu package ships the module]**). Assert degrade log,
hidden-surface behavior, and that full-capture mode still shows.

**Acceptance:**
- [ ] Degrade reason logged exactly once; notice is non-modal; no prompt loop
- [ ] Ambient features gated off via capabilities; capture-mode overlays functional

### Slice F: (PARKED) Layer-Shell Own-Surface Backend

Parked per §4.3. Gate to unpark: Branch W-N false AND §3.5 pass-through broken on ≥1
major compositor, or UCE offscreen render path exists. Until then: no code. This
replaces the draft's Slice D/F Wayland-backend work.

### Slice G: Multi-Monitor Support

**Goal:** one overlay window per monitor, both session types.

**Files:** `LinuxOverlaySurface.cs` (per-monitor windows already driven by
`CompositorEngine`), backends (per-window XIDs/shapes).

**Verification (CI — Xvfb recipe corrected):** the draft's `-screen 0 … -screen 1 …`
creates two X *protocol screens* with independent roots — NOT the one-root-multi-monitor
model `SetBounds` assumes. Correct recipe (same fix as the framesource contract §6-G):
```bash
Xvfb :99 -screen 0 3840x1080x24 +extension RANDR & export DISPLAY=:99
xrandr --setmonitor LEFT  1920/508x1080/286+0+0    none
xrandr --setmonitor RIGHT 1920/508x1080/286+1920+0 none
openbox & sleep 1
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-overlay-multimonitor
# Assert: two overlay windows, each covering its monitor's bounds; per-window input
# shapes independent (capture rect on RIGHT does not affect LEFT pass-through —
# positional xdotool asserts on both monitors)
```
Wayland: sway headless `swaymsg create_output` for a second output; note that on native
Wayland `Window.Position` is meaningless (compositor-placed) — per-monitor fullscreen
placement must use Avalonia's Screens/fullscreen APIs, and under Branch W-X it works
via X11 coordinates.

**Acceptance:**
- [ ] Per-monitor windows with correct bounds on both session types
- [ ] Independent per-window input shapes (cross-monitor positional CI assert)
- [ ] Monitor hotplug (xrandr delmonitor/setmonitor live) does not crash (error trap)

---

## 7. Risk / Unknowns

### 7.1 Claims to verify before the relevant slice lands (driver has web tools; tags to check)

| # | Claim | Confidence | Verify via | Blocks |
|---|-------|------------|-----------|--------|
| 1 | Avalonia 12.0.5 Linux windowing = X11 only (Wayland sessions via XWayland); no native Wayland backend | **Low — THE fork** | Avalonia v12 docs/repo (`Avalonia.X11`, any `Avalonia.Wayland`), avalonia-research protocol; empirically Slice D step 1 | D, E, whole §4 |
| 2 | If native Wayland exists: `TryGetPlatformHandle` exposes `wl_surface` (+ descriptor string) | Low | Avalonia v12 source `IPlatformHandle` implementations | D (W-N) |
| 3 | XWayland XWMs (wlroots, KWin, Mutter) translate X11 input shape → `wl_surface` input region, so pass-through reaches native Wayland windows | Low-medium — load-bearing for W-X | wlroots xwm.c / KWin / Mutter source or issue trackers; manual per-compositor checklist | D (W-X) |
| 4 | `XFixesSetWindowShapeRegion`/regions require XFixes protocol ≥ 2 (`XFixesQueryVersion` gate) | Medium-high | fixesproto spec | B |
| 5 | Avalonia's X11 backend installs an Xlib error handler (we must chain, not clobber) | Medium | Avalonia.X11 source (`XError` handling) | B |
| 6 | Avalonia `Topmost=true` maps to `_NET_WM_STATE_ABOVE` on X11 | Medium-high | Avalonia.X11 source | B |
| 7 | wl_surface role exclusivity + layer-shell `role` protocol error = client disconnect | High (protocol text) | wayland.app wl_surface + wlr-layer-shell spec | gate on F |
| 8 | `set_input_region` copy semantics; effect on next commit; honored by all compositors incl. Mutter | High (protocol text) | wayland.app wl_surface spec | D (W-N) |
| 9 | `wl_*_add_listener` stores the listener pointer (no copy) — pinned lifetime required | High | libwayland docs/source | D (W-N), any probe code |
| 10 | KWin ships `zwlr_layer_shell_v1` (draft matrix said No) | Medium | `wayland-info` on Plasma | matrix only |
| 11 | `_NET_WM_CM_S0` selection-owner probe detects a running X compositor | High | EWMH/composite-manager convention | B |
| 12 | weston `--xwayland` available on ubuntu CI packages | Medium-high | weston docs / apt package | E |
| 13 | sway `get_tree` does NOT list layer surfaces (draft's Slice D grep was unsound) | Medium | sway-ipc docs | (recipe removed — log asserts used instead) |
| 14 | libwayland multi-thread request submission safety on one connection | Medium | libwayland threading docs | D (W-N) |

### 7.2 Genuine Unknowns (real-desktop only)

| Risk | Impact | Mitigation |
|------|--------|------------|
| Compositor restack races vs our reassertion cadence (X11) | Flicker or lost topmost moments | Mirror Windows 500ms/5s cadence; tune on real desktops |
| XWayland topmost vs native-Wayland fullscreen apps/games | Overlay hidden during exactly the sessions the product targets | Accept + document; layer-shell unpark criteria (§4.3/F) |
| Avalonia native-window recreation invalidating cached XIDs | Shape silently applied to a dead window (trapped, but shape lost) | Re-acquire on `Opened`; re-apply shape after reacquisition |
| Fractional scaling (Wayland) vs physical-pixel mask math | Click-through holes offset from visuals | Slice C positional asserts; per-window `RenderScaling` at apply time |
| Focus-steal variance across WMs despite `ShowActivated=false` | Overlay steals focus on some WM | `WM_HINTS.input=False` escalation path (§3.4); per-WM row in §7.3 |
| CI headless compositors not exercising real XWM shape translation | W-X pass-through unproven for native windows beneath | Manual checklist per compositor recorded at Slice D |

### 7.3 Compositor-Specific Bugs (track as found)

| Compositor | Bug | Workaround |
|------------|-----|------------|
| (none yet) | — | — |

---

## 8. CI Verification Matrix

| Slice | No display | X11 (Xvfb+openbox+xdotool) | sway headless (+xwayland) | weston (+xwayland) | Manual (real desktops) |
|-------|-----------|----------------------------|---------------------------|--------------------|------------------------|
| A Selection+Fallback | Required | Required | — | — | — |
| B X11 topmost+ambient+trap | — | Required (+ BadWindow negative test) | — | — | — |
| C X11 per-region | — | Required (positional asserts) | — | — | — |
| D Wayland fork | — | — | Required (fork answer + branch behavior) | — | Per-compositor pass-through checklist (sway/KWin/Mutter) |
| E Degrade UX | — | — | — | Required | — |
| F (parked) | — | — | — | — | — |
| G Multi-monitor | — | Required (`xrandr --setmonitor`, NOT `-screen 1`) | Required (`create_output`) | — | Mixed-DPI |

CI job additions: extend the existing Xvfb job with openbox + xdotool + xterm; the
sway-headless job is SHARED with the framesource/title contracts (one compositor
instance can host all three suites). Every backend CI cannot fully prove carries its
explicit residual manual checklist in its slice — no slice lands compile-only
(readiness-map rule).

---

## 9. Summary

- **Selection ground truth corrected:** the Avalonia platform handle descriptor (XID
  vs Wayland) decides which native machinery can touch the overlay window; env-var
  session detection (OR-predicate, per the landed `LinuxSessionDetector`) is only the
  pre-window hint. The draft's AND-predicate misroute is fixed.
- **Process-kill risk closed:** scoped, chained Xlib error trap around every
  XFixes/EWMH call, on our OWN `XOpenDisplay` connection, serialized behind a lock —
  shared helper with the framesource/title contracts.
- **Wayland section rebuilt on protocol law:** a second connection can never shape
  Avalonia's surface; layer-shell can never wrap it (role exclusivity = app-fatal
  protocol error); input regions are core protocol GNOME included; listener structs
  must outlive their proxies. The one decisive unknown — whether Avalonia v12 is
  X11-only on Linux — is §7.1 row 1 and gates Slice D's two branches.
- **Degrade policy inverted to match the product rule:** never an invisible input
  trap; a backend that cannot honor ambient click-through hides the surface and gates
  ambient features off. Full-capture overlays still work everywhere.
- **In-flight code has a blocking correction list (§6.0)** — most urgently the Wayland
  stub whose capability lie would lock sway desktops.
- **Slices:** A (selection/fallback, partly in-flight) → B (X11 ambient + trap) → C
  (X11 per-region) → D (Wayland fork, research-then-code) → E (degrade UX) → G
  (multi-monitor); F (layer-shell own-surface) parked with explicit unpark criteria.

---

## Sources

- `CCP.Core/Platform/IOverlaySurface.cs`; in-flight `ILinuxOverlayBackend.cs`,
  `LinuxSessionDetector.cs`, `LinuxOverlaySurface.cs`, backends (worktree
  `.pi/worktrees/overlay`, uncommitted)
- `CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs:11-20`;
  `CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:24-40`;
  `CCP.Avalonia/Compositor/CompositorWindow.axaml.cs:98-175`;
  `CCP.Avalonia/Platform/AvaloniaPlatformCapabilities.cs` (`SupportsClickThrough`)
- `overlay-clickthrough` skill — per-region capture mask contract (2026-07-09 team
  review; owner reconfirmed 2026-07-12), never-trap degrade rule, two-level rule
- `docs/linux-framesource-contract.md` §3.1-3.2 (shared X11 connection/trap design),
  `docs/linux-foreground-title-contract.md` §2.1 (selection-order lesson)
- `docs/linux-macos-readiness-map.md` — governing multi-backend principle
- EWMH — https://specifications.freedesktop.org/wm-spec/wm-spec-latest.html
  (`_NET_WM_STATE` client-message rule, `_NET_WM_STATE_ABOVE`, SKIP_TASKBAR/PAGER)
- XFixes protocol — https://www.x.org/releases/current/doc/fixesproto/fixesproto.txt
  (regions + SetWindowShapeRegion, protocol v2)
- wl_surface (input region, role rules) —
  https://wayland.freedesktop.org/docs/html/apa.html#protocol-spec-wl_surface
- wlr-layer-shell — https://wayland.app/protocols/wlr-layer-shell-unstable-v1

### 7.1-VERIFIED (carried from sibling contracts' web verification, 2026-07-12 — driver)
| Claim | Result | Source |
|-------|--------|--------|
| Avalonia v12 X11 handle access | **VERIFIED** — `TopLevel.GetTopLevel(x)?.TryGetPlatformHandle()` → `.Handle` (IntPtr XID) + `.HandleDescriptor == "XID"` on Linux/X11. The X11 backend keys off this. | docs.avaloniaui.net native-interop / window-handles (verified for the framesource + title contracts) |

### 7.1-RESOLVED — THE FORK (web-verified 2026-07-12, driver): Avalonia 12.0 is X11-ONLY on Linux
**Decisive:** this project references **Avalonia 12.0.5** (NOT 12.1) and has **zero** `Avalonia.Wayland` /
`UseWayland` usage — the Linux head builds via `ProgramShared.BuildAvaloniaApp()` with default platform
detect = **X11**. Per Avalonia docs, "Avalonia targets X11 directly on Linux; Wayland support is in private
preview"; the native Wayland backend is **experimental, 12.1-only, opt-in** (`Avalonia.Wayland` package +
`UseWayland()`), and NOT selected by `UsePlatformDetect()`.

**Consequence — the entire native-Wayland (wlr-layer-shell) backend is OUT OF SCOPE / DEAD CODE for this
project:**
- On an **X11 session**, Avalonia creates a native X11 window → the X11 XFixes-shape backend applies.
- On a **Wayland session**, Avalonia runs under **XWayland** → the window is still an X11 (XWayland) window
  → the SAME X11 XFixes-shape backend applies. Avalonia never creates a native `wl_surface`, so there is
  nothing for a layer-shell backend to attach to (and attaching post-creation is the role-exclusivity
  crash fable-5 flagged).
- **Implement ONLY: the X11 backend (XFixes input shape + `_NET_WM_STATE` topmost) + the FallbackBackend
  (no-X).** Selector: X-display present (native or XWayland) → X11 backend; else → fallback. Do NOT ship
  `WaylandLayerShellBackend`/`WaylandInputRegionBackend`/`WaylandDegradeBackend` — remove/park them; they
  cannot be selected because Avalonia's surface is always X11 here.
- **Only residual (row 3, real-Wayland-only):** whether the compositor routes an X11 input-shape on an
  XWayland window as pass-through to NATIVE Wayland windows beneath. XWayland surfaces participate in
  compositor input routing and honor input regions, so this is expected to work; pure-X11 Xvfb CI proves
  the X11 half — the XWayland-underneath half needs a real Wayland+XWayland manual check (one row).
- Revisit the Wayland-native path ONLY if the project later adopts `Avalonia.Wayland` (12.1+, opt-in).

**IMPL-AUDIT DIRECTIVE:** the in-flight opus-4-5 overlay impl builds Wayland backends too — the fable-5
impl-audit KEEPS/hardens X11 + selector + fallback and DELETES the Wayland-native backends as dead code.

### 7.1-DECISION — DO NOT upgrade to Avalonia 12.1 native Wayland (web-researched 2026-07-12, driver + owner "support all users")
Owner asked whether upgrading Avalonia to gain native Wayland would "support all users." Researched Avalonia's
own Wayland deep-dive + the 12.1 release notes (2026-07-08). **Decision: stay on X11 (12.0.5). Upgrading does
NOT help and adds risk.**
- **X11/XWayland already reaches every Linux user** (Avalonia: "XWayland is installed by default on virtually
  every desktop Linux distribution … your existing Avalonia applications will continue to work … whether they
  use X11 or Wayland"). X11-only is not an under-support gap.
- **12.1's Wayland backend is experimental, embedded-first, opt-in (`UseWayland()`, not `UsePlatformDetect`),**
  and supports only CORE windowing (mouse/kbd/clipboard/DnD). It exposes NONE of the overlay's needs
  (topmost/always-on-top, layer-shell, per-region input) — Wayland has no window positioning and overlay
  features are compositor-specific extensions Avalonia does not surface. The overlay would be worse, not better.
- **Possible AGPL dual-licensing** of the Wayland backend — a licensing risk for a distributed app.
- Would force dual X11+Wayland maintenance on a 4-day-old backend, mid-port, unverifiable-locally.
- **Revisit ONLY** the 12.1 **X11** perf wins (>60fps refresh uncap, stencil buffers, XDND) as a separate future
  upgrade after the port stabilizes — independent of Wayland.
CONCLUSION: overlay stays X11-only (XFixes shape + _NET_WM_STATE), universal via XWayland, CI-provable via Xvfb.
