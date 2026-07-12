# Linux Overlay Implementation Contract

**Date:** 2026-07-12  
**Scope:** WS4/task-board row #5 — Linux overlay click-through bring-up  
**Status:** Judgment-tier design document (read-only research artifact)

This document specifies the behavior contract, backend architecture, and implementation
slices for Linux overlay support in the Avalonia head. X11 and Wayland are CO-EQUAL
first-class backends, not "X11 primary + Wayland appendix."

---

## 1. IOverlaySurface Behavior Contract

The `IOverlaySurface` interface (`CCP.Core/Platform/IOverlaySurface.cs`) defines the
platform-agnostic overlay window seam. Every platform implementation MUST provide
these behaviors:

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

### 1.2 Behavioral Requirements (cited from WindowsOverlaySurface.cs)

| Requirement | Windows Implementation | Citation |
|-------------|------------------------|----------|
| **Topmost/always-on-top** | `Topmost = true` in base `AvaloniaOverlaySurface` ctor; `WS_EX_TOPMOST` via `SetWindowPos(HWND_TOPMOST)` | `AvaloniaOverlaySurface.cs:14`, `CompositorWindow.axaml.cs:165-172` |
| **Click-through toggle** | `SetClickThrough(true)` ORs `WS_EX_TRANSPARENT \| WS_EX_LAYERED` onto `GWL_EXSTYLE`; `false` clears `WS_EX_TRANSPARENT` | `WindowsOverlaySurface.cs:24-35` |
| **Focus non-stealing** | `ShowActivated = false`, `Focusable = false`, `IsHitTestVisible = false` in base; `WS_EX_NOACTIVATE` at native level | `AvaloniaOverlaySurface.cs:17-20`, `CompositorWindow.axaml.cs:99` |
| **Taskbar/Alt-Tab exclusion** | `ShowInTaskbar = false` in base; `WS_EX_TOOLWINDOW` at native level | `AvaloniaOverlaySurface.cs:15`, `CompositorWindow.axaml.cs:99` |
| **Transparency** | `WindowDecorations = None`, `TransparencyLevelHint = [Transparent]`, `Background = Transparent` | `AvaloniaOverlaySurface.cs:11-13` |
| **Screen-capture exclusion** | `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` for brain-drain surface ONLY; main surface deliberately visible | `CompositorWindow.axaml.cs:115-120` |
| **Multi-monitor** | One `CompositorWindow` per monitor; bounds set via `SetBounds(PixelRect)` to cover physical screen rect | `CompositorEngine.cs:127-134` |
| **Topmost reassertion** | `ReassertTopmost(force)` re-pins to `HWND_TOPMOST` when z-order is lost (500ms probe, 5s force) | `CompositorWindow.axaml.cs:149-175` |
| **Style flush** | `SetWindowPos(..., SWP_FRAMECHANGED)` after any `SetWindowLong` to commit ex-style changes | `CompositorWindow.axaml.cs:103-104` |

### 1.3 Per-Region Click-Through Contract (2026-07-09 Team Review)

The compositor window is NOT uniformly click-through. Input disposition is PER-REGION:

| Layer Type | Examples | Input Behavior |
|------------|----------|----------------|
| **Ambient (pass-through)** | `PinkTintLayer` (theme color filter), `SpiralLayer` | Clicks pass to apps beneath |
| **Capture (absorb)** | `VideoLayer`, `FlashLayer`, `SubliminalLayer`, `BrainDrainLayer`, `BouncingTextLayer`, `BubbleLayer`, all Chaos FX | Clicks captured by overlay |

**Mechanism:** The compositor builds a per-frame **capture mask** = union of all active
non-ambient layer painted regions. The global mouse hook swallows clicks inside the mask
and passes clicks outside it. Every Linux backend MUST either:

1. Implement per-region input shaping natively (X11 XFixes, Wayland input regions), OR
2. Document the concrete degrade (e.g., "all clicks pass through" or "all clicks captured")

---

## 2. Linux Backend Architecture

### 2.1 Runtime Backend Selection

The Linux overlay implementation uses a runtime backend selector that detects the
session type and instantiates the best available backend. Selection happens once
at overlay service initialization.

```
┌─────────────────────────────────────────────────────────────────┐
│                    LinuxOverlayBackendSelector                  │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ Detection order:                                            ││
│  │ 1. XDG_SESSION_TYPE == "wayland" + WAYLAND_DISPLAY set     ││
│  │    → WaylandBackendProbe                                    ││
│  │ 2. XDG_SESSION_TYPE == "x11" OR DISPLAY set                ││
│  │    → X11Backend                                             ││
│  │ 3. Neither                                                  ││
│  │    → FallbackBackend                                        ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘

WaylandBackendProbe:
  ├─ wlr-layer-shell available? → WaylandLayerShellBackend
  ├─ wl_surface.set_input_region works? → WaylandInputRegionBackend  
  └─ Neither → WaylandDegradeBackend (topmost only, no click-through)
```

### 2.2 Backend Fallback Chain

| Priority | Backend | Capabilities | When Selected |
|----------|---------|--------------|---------------|
| 1 | `X11InputShapeBackend` | Full: topmost + per-region input shape | X11 session with XFixes extension |
| 2 | `WaylandLayerShellBackend` | Full: layer-shell topmost + input regions | Wayland + wlr-layer-shell (sway, Hyprland, wlroots compositors) |
| 3 | `WaylandInputRegionBackend` | Partial: topmost (compositor-dependent) + input regions | Wayland + compositor honors `wl_surface.set_input_region` |
| 4 | `WaylandDegradeBackend` | Minimal: topmost attempt, no click-through | Wayland without layer-shell or input region support (GNOME/Mutter, some KDE) |
| 5 | `FallbackBackend` | Minimal: normal window, always-on-top, no click-through | Unknown session or all probes fail |

**Guarantee:** The fallback chain ensures SOMETHING functional always runs. The feature
never hard-fails on an unknown environment; it degrades to a visible overlay that may
capture all input (documented degrade).

### 2.3 Seam Structure

```
CCP.Core/Platform/
├── IOverlaySurface.cs              # Unchanged interface
├── ILinuxOverlayBackend.cs         # NEW: backend abstraction
└── LinuxSessionType.cs             # NEW: session detection enum

CCP.Avalonia.Desktop.Linux/Platform/
├── LinuxOverlayBackendSelector.cs  # NEW: runtime selection
├── LinuxOverlaySurface.cs          # NEW: IOverlaySurface impl delegating to backend
├── Backends/
│   ├── X11InputShapeBackend.cs     # X11 + XFixes input regions
│   ├── WaylandLayerShellBackend.cs # wlr-layer-shell
│   ├── WaylandInputRegionBackend.cs # wl_surface input regions
│   ├── WaylandDegradeBackend.cs    # Wayland fallback
│   └── FallbackBackend.cs          # Last resort
└── Interop/
    ├── X11Interop.cs               # XLib/XFixes P/Invoke
    └── WaylandInterop.cs           # libwayland-client bindings
```

---

## 3. X11 Backend Design

### 3.1 X11 Window Handle Access (Avalonia v12)

Avalonia v12 exposes the X11 window ID through `TryGetPlatformHandle()`:

```csharp
var platformHandle = window.TryGetPlatformHandle();
if (platformHandle?.HandleDescriptor == "XID")
{
    IntPtr xid = platformHandle.Handle;
    // xid is the X11 Window (XID) for this TopLevel
}
```

**Verified:** The Windows head already uses `TryGetPlatformHandle()` successfully
(`CompositorWindow.axaml.cs:98`, `WindowsOverlaySurface.cs:25`). The Avalonia v12
X11 backend uses the same API with descriptor `"XID"` instead of `"HWND"`.

### 3.2 Topmost via _NET_WM_STATE_ABOVE

X11 topmost is achieved by setting `_NET_WM_STATE_ABOVE` on the window:

```csharp
// Option A: Window property (before mapping)
XChangeProperty(display, xid, 
    XInternAtom(display, "_NET_WM_STATE", false),
    XInternAtom(display, "ATOM", false),
    32, PropModeReplace,
    new[] { XInternAtom(display, "_NET_WM_STATE_ABOVE", false) }, 1);

// Option B: Client message (after mapping, for EWMH-compliant WMs)
var ev = new XClientMessageEvent {
    type = ClientMessage,
    window = xid,
    message_type = XInternAtom(display, "_NET_WM_STATE", false),
    format = 32,
    data = { l = { 1, XInternAtom(display, "_NET_WM_STATE_ABOVE", false), 0, 1, 0 } }
};
XSendEvent(display, root, false, SubstructureRedirectMask | SubstructureNotifyMask, ref ev);
```

**Override-redirect trade-off:** Setting `override_redirect = true` bypasses the window
manager entirely (guaranteed topmost, no decorations) but breaks keyboard focus,
taskbar integration, and some compositors. Prefer `_NET_WM_STATE_ABOVE` for EWMH
compliance; use override-redirect only as a fallback probe if ABOVE is ineffective.

### 3.3 Per-Region Click-Through via XFixes Input Shape

X11 provides per-region input passthrough through the XFixes extension's input shape:

```csharp
// XFixes input region: clicks in the region are captured; clicks outside pass through.
// To make the whole window click-through: set an empty input region.
// To capture specific areas: set a region covering those areas.

// Get the capture mask from the compositor (union of non-ambient layer rects)
var captureMask = compositor.GetCaptureMaskSnapshot();

// Build an X11 region from the mask
IntPtr region = XFixesCreateRegion(display, captureMask.ToXRectangles(), captureMask.Count);

// Apply the input shape (ShapeInput = 2)
XFixesSetWindowShapeRegion(display, xid, ShapeInput, 0, 0, region);

// Free the region handle
XFixesDestroyRegion(display, region);
```

**Key functions:**
- `XFixesCreateRegion(Display*, XRectangle[], int count)` — creates a server-side region
- `XFixesSetWindowShapeRegion(Display*, Window, int kind, int x, int y, XserverRegion)` — applies region as shape
- `kind = ShapeInput (2)` — input shape (vs ShapeBounding for visual shape)
- Empty region = full click-through; window-sized region = full capture

**Update cadence:** The capture mask changes when layers activate/deactivate. Update
the input shape on compositor tick when the mask changes (dirty flag pattern).

### 3.4 Additional X11 Requirements

| Requirement | X11 Implementation |
|-------------|-------------------|
| Focus non-stealing | `_NET_WM_STATE_SKIP_TASKBAR`, `_NET_WM_STATE_SKIP_PAGER`, `WM_HINTS.input = false` |
| Taskbar exclusion | `_NET_WM_STATE_SKIP_TASKBAR` + `_NET_WM_WINDOW_TYPE_DOCK` or `_NOTIFICATION` |
| Transparency | Compositor-dependent; ARGB visual + `_NET_WM_WINDOW_OPACITY` or rely on compositor |
| Topmost reassertion | Re-send `_NET_WM_STATE_ABOVE` client message; poll `_NET_CLIENT_LIST_STACKING` |

### 3.5 XWayland Compatibility

When running under XWayland (X11 apps inside a Wayland session), the X11 backend
works but with caveats:
- XFixes input shapes work (XWayland implements them)
- `_NET_WM_STATE_ABOVE` may not work (no X11 window manager; Wayland compositor controls stacking)
- Topmost is compositor-dependent; may need Wayland-side hints

**Detection:** If `XDG_SESSION_TYPE == "wayland"` but `DISPLAY` is set, we're on XWayland.
Prefer native Wayland backend; fall back to X11 if Wayland backend unavailable.

---

## 4. Wayland Backend Design

### 4.1 Wayland Session Detection

```csharp
public static bool IsWaylandSession()
{
    var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
    var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
    return sessionType == "wayland" || !string.IsNullOrEmpty(waylandDisplay);
}
```

### 4.2 wlr-layer-shell Backend (Full Strength)

The `wlr-layer-shell` protocol (wlroots-based compositors: sway, Hyprland, river, etc.)
provides dedicated overlay/panel surfaces with explicit layer and input control.

**Capabilities:**
- `ZWLR_LAYER_SHELL_V1_LAYER_OVERLAY` — renders above all windows
- Exclusive zones and anchoring for panels
- Input regions via standard `wl_surface.set_input_region`

**Integration approach:**
1. Probe for `zwlr_layer_shell_v1` global in the Wayland registry
2. If present, create layer surfaces instead of xdg_toplevel
3. Set layer to `OVERLAY`, anchor to output, set exclusive zone to -1 (no exclusive)
4. Apply input regions per frame

**Avalonia integration challenge:** Avalonia v12 creates xdg_toplevel surfaces by default.
To use layer-shell, we need either:
- A: Avalonia exposes a hook to customize surface creation (check v12 platform API)
- B: Create a raw wayland surface outside Avalonia for the overlay, render into it via shared texture
- C: Use a separate library (e.g., `gtk-layer-shell` bindings if using GTK backend)

**v12 research needed:** Check if `Avalonia.Wayland` platform backend exposes any
surface-type customization or if `TryGetPlatformHandle()` returns a `wl_surface*` that
can be wrapped with layer-shell post-creation.

### 4.3 Wayland Input Region Backend (Partial)

Standard Wayland provides input regions via `wl_surface.set_input_region`:

```c
// Create a region
struct wl_region* region = wl_compositor_create_region(compositor);

// Add rectangles for capture areas (or leave empty for full click-through)
for (rect in captureMask) {
    wl_region_add(region, rect.x, rect.y, rect.width, rect.height);
}

// Apply to surface
wl_surface_set_input_region(surface, region);
wl_surface_commit(surface);

// Destroy region handle (compositor keeps a copy)
wl_region_destroy(region);
```

**Limitations:**
- Topmost is compositor-discretionary; no guaranteed `_NET_WM_STATE_ABOVE` equivalent
- GNOME/Mutter may ignore input regions on xdg_toplevel windows (security policy)
- Input regions apply to the whole surface, not sub-surfaces

### 4.4 Wayland Degrade Backend (GNOME/KDE Fallback)

GNOME (Mutter) and some KDE (KWin) configurations do not support wlr-layer-shell and
may ignore input regions for security reasons. The degrade backend:

1. Creates a normal xdg_toplevel window
2. Requests `xdg_toplevel.set_fullscreen` on the target output
3. Sets window to stay on top via compositor-specific D-Bus or hints
4. **Does NOT implement click-through** — all input is captured

**Documented degrade:**
- Per-region click-through: NOT AVAILABLE (compositor security policy)
- User impact: Overlay will capture all clicks; user must close overlay to interact with desktop
- Mitigation: Provide a prominent "minimize overlay" hotkey/button

### 4.5 Portal-Based Overlay (Future Investigation)

The XDG Desktop Portal provides `org.freedesktop.portal.Screencast` and future overlay
portals. This is a potential future path for GNOME/KDE but:
- No overlay-specific portal exists today (2026)
- Screencast portal is for capture, not overlay
- Would require compositor-side changes

**Status:** Not implemented; document as a future investigation area.

---

## 5. Wayland Appendix: Compositor Compatibility Matrix

| Compositor | Layer-Shell | Input Regions | Topmost | Click-Through |
|------------|-------------|---------------|---------|---------------|
| sway | Yes (native wlroots) | Yes | Yes | Full per-region |
| Hyprland | Yes (wlroots) | Yes | Yes | Full per-region |
| river | Yes (wlroots) | Yes | Yes | Full per-region |
| wayfire | Yes (wlroots) | Yes | Yes | Full per-region |
| GNOME/Mutter | No | Limited | Partial | DEGRADE: none |
| KDE/KWin | No | Yes | Yes | Partial (may work) |
| Weston | Partial | Yes | Yes | Partial |

**Key insight:** wlroots-based compositors (sway, Hyprland, etc.) get full functionality.
GNOME gets the degrade path. KDE may work with input regions but needs testing.

---

## 6. Implementation Slice Plan

Each slice is independently committable with its own verification gate. Slices are
ordered by dependency; later slices depend on earlier ones.

### Slice A: Backend Selection + Fallback Window (Foundation)

**Goal:** Runtime backend detection and a working (non-click-through) overlay window.

**Files:**
- `CCP.Core/Platform/LinuxSessionType.cs` — enum for session detection
- `CCP.Avalonia.Desktop.Linux/Platform/LinuxOverlayBackendSelector.cs` — detection logic
- `CCP.Avalonia.Desktop.Linux/Platform/LinuxOverlaySurface.cs` — `IOverlaySurface` delegating to backend
- `CCP.Avalonia.Desktop.Linux/Platform/Backends/FallbackBackend.cs` — basic always-on-top window

**Verification (CI):**
```bash
# X11 via Xvfb
Xvfb :99 -screen 0 1920x1080x24 &
export DISPLAY=:99
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-overlay-shows
# Assert: overlay window created and visible

# Wayland via weston headless
weston --backend=headless-backend.so --socket=wayland-test &
export WAYLAND_DISPLAY=wayland-test
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-overlay-shows
# Assert: overlay window created (degrade mode, but visible)
```

**Acceptance:**
- [ ] Session type correctly detected from environment
- [ ] Overlay window shows on both X11 and Wayland sessions
- [ ] Window is topmost (best-effort on Wayland)
- [ ] SetClickThrough is a no-op (documented as fallback behavior)

---

### Slice B: X11 Topmost + Full Click-Through

**Goal:** X11 overlay that is always-on-top and fully click-through (no capture mask yet).

**Files:**
- `CCP.Avalonia.Desktop.Linux/Platform/Interop/X11Interop.cs` — P/Invoke for XLib basics
- `CCP.Avalonia.Desktop.Linux/Platform/Backends/X11InputShapeBackend.cs` — partial impl (topmost + empty input shape)

**Verification (CI):**
```bash
# Xvfb + xdotool for input verification
Xvfb :99 -screen 0 1920x1080x24 &
export DISPLAY=:99
openbox &  # Minimal EWMH-compliant WM

# Launch app with overlay
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-clickthrough &
APP_PID=$!
sleep 3

# Spawn a test window beneath the overlay
xterm -geometry 80x24+100+100 &
XTERM_PID=$!
sleep 1

# Click through overlay onto xterm — xdotool should report xterm focused
xdotool mousemove 200 200 click 1
FOCUSED=$(xdotool getactivewindow getwindowname)
kill $APP_PID $XTERM_PID

# Assert: xterm received focus (click passed through)
[[ "$FOCUSED" == *"xterm"* ]] || exit 1
```

**Acceptance:**
- [ ] X11 window has `_NET_WM_STATE_ABOVE`
- [ ] XFixes empty input region applied
- [ ] Clicks pass through to windows beneath
- [ ] CI passes on ubuntu-latest with Xvfb + openbox

---

### Slice C: X11 Per-Region Input Shape

**Goal:** X11 capture mask integration — per-region click-through matching the product contract.

**Files:**
- `CCP.Avalonia.Desktop.Linux/Platform/Interop/X11Interop.cs` — add `XFixesCreateRegion`, `XFixesSetWindowShapeRegion`
- `CCP.Avalonia.Desktop.Linux/Platform/Backends/X11InputShapeBackend.cs` — capture mask → X11 region conversion
- `CCP.Avalonia/Compositor/CompositorEngine.cs` — expose capture mask snapshot for Linux

**Verification (CI):**
```bash
Xvfb :99 -screen 0 1920x1080x24 &
export DISPLAY=:99
openbox &

# Launch with a flash layer active (creates capture region)
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-capture-region &
APP_PID=$!
sleep 3

xterm -geometry 80x24+100+100 &
sleep 1

# Click INSIDE the capture region (should NOT pass through)
# Click OUTSIDE the capture region (should pass through)
# This requires the smoke test to report click disposition

# Assert via smoke test output: capture region correctly applied
grep "CaptureRegion: applied" logs/linux-run-*.log || exit 1
```

**Acceptance:**
- [ ] Capture mask converted to XRectangle array
- [ ] `XFixesSetWindowShapeRegion` called with capture regions
- [ ] Clicks inside capture regions absorbed; clicks outside pass through
- [ ] Input shape updates when layers activate/deactivate

---

### Slice D: Wayland Layer-Shell Backend

**Goal:** Full-strength overlay on wlroots-based compositors (sway, Hyprland).

**Files:**
- `CCP.Avalonia.Desktop.Linux/Platform/Interop/WaylandInterop.cs` — libwayland-client bindings
- `CCP.Avalonia.Desktop.Linux/Platform/Interop/WlrLayerShellInterop.cs` — layer-shell protocol bindings
- `CCP.Avalonia.Desktop.Linux/Platform/Backends/WaylandLayerShellBackend.cs` — layer surface creation

**Verification (CI):**
```bash
# sway in headless mode
WLR_BACKENDS=headless WLR_LIBINPUT_NO_DEVICES=1 sway &
export WAYLAND_DISPLAY=wayland-0
sleep 2

dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-layer-shell &
APP_PID=$!
sleep 3

# Verify layer-shell surface created
swaymsg -t get_tree | grep -q "layer_surface" || exit 1

# Verify input region applied
grep "LayerShell: input region set" logs/linux-run-*.log || exit 1
```

**Acceptance:**
- [ ] Layer-shell protocol bound from registry
- [ ] Overlay surface created with `ZWLR_LAYER_SHELL_V1_LAYER_OVERLAY`
- [ ] Input regions applied via `wl_surface.set_input_region`
- [ ] CI passes on ubuntu-latest with sway headless

---

### Slice E: Wayland Input-Region Backend (KDE/Generic)

**Goal:** Partial overlay support for compositors with input regions but no layer-shell.

**Files:**
- `CCP.Avalonia.Desktop.Linux/Platform/Backends/WaylandInputRegionBackend.cs` — input regions on xdg_toplevel

**Verification (CI):**
```bash
# Weston headless (no layer-shell, but has input regions)
weston --backend=headless-backend.so --socket=wayland-test &
export WAYLAND_DISPLAY=wayland-test
sleep 2

dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-input-region &
APP_PID=$!
sleep 3

grep "InputRegion: applied" logs/linux-run-*.log || exit 1
```

**Acceptance:**
- [ ] Input region created and applied to xdg_toplevel surface
- [ ] Topmost is best-effort (compositor-dependent)
- [ ] Documented degrade: topmost not guaranteed

---

### Slice F: Wayland Degrade Backend (GNOME)

**Goal:** Working overlay on GNOME/Mutter with documented limitations.

**Files:**
- `CCP.Avalonia.Desktop.Linux/Platform/Backends/WaylandDegradeBackend.cs` — fullscreen + no click-through

**Verification (CI):**
```bash
# GNOME Shell in CI is hard; use Mutter directly
mutter --wayland --headless &
export WAYLAND_DISPLAY=wayland-0
sleep 2

dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-degrade-mode &
APP_PID=$!
sleep 3

# Verify degrade mode activated
grep "Degrade: click-through unavailable" logs/linux-run-*.log || exit 1
```

**Acceptance:**
- [ ] Overlay shows on GNOME/Mutter
- [ ] Click-through explicitly disabled with user notification
- [ ] Feature gracefully degrades (never crashes)

---

### Slice G: Multi-Monitor Support

**Goal:** One overlay window per monitor, matching Windows behavior.

**Files:**
- `CCP.Avalonia.Desktop.Linux/Platform/LinuxOverlaySurface.cs` — multi-monitor creation
- All backends — per-output surface creation

**Verification (CI):**
```bash
# Xvfb with two screens
Xvfb :99 -screen 0 1920x1080x24 -screen 1 1920x1080x24 &
export DISPLAY=:99
openbox &

dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-multimonitor &
sleep 5

# Verify two overlay windows created
grep "Overlay created for screen" logs/linux-run-*.log | wc -l | grep -q "2" || exit 1
```

**Acceptance:**
- [ ] Overlay window created for each connected monitor
- [ ] Each window covers its monitor's full bounds
- [ ] Input shapes applied per-monitor

---

## 7. Risk/Unknowns Section

These items CANNOT be settled without running on a real Linux desktop or additional
upstream research:

### 7.1 Genuine Unknowns

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Avalonia v12 Wayland surface access** | Cannot create layer-shell surfaces if Avalonia doesn't expose `wl_surface*` | Check v12 source; may need to P/Invoke into Avalonia's libwayland bindings or use IPC |
| **GNOME input region rejection** | GNOME may silently ignore `wl_surface.set_input_region` on popup/toplevel surfaces | Test on real GNOME session; document degrade |
| **KDE/KWin behavior variance** | KWin versions differ in input region support | Test on multiple KDE versions; maintain compat matrix |
| **XWayland input shape interaction** | XFixes input shapes may not propagate correctly through XWayland | Test X11 app under Wayland; may need native Wayland fallback |
| **Compositor-specific topmost races** | Some compositors reorder windows on focus; reassertion may flicker | Test with real compositor; tune reassertion timing |
| **CI headless compositor limitations** | Headless backends may not exercise all code paths | Supplement CI with manual testing on real desktop |

### 7.2 Requires Real Desktop Testing

- Flash layer click behavior with capture mask
- Spiral/pink-tint click-through vs. subliminal capture
- Multi-monitor with mixed DPI (X11 RandR vs. Wayland per-output scale)
- Focus non-stealing during video playback
- Topmost behavior across fullscreen apps, games, and screen sharing

### 7.3 Compositor-Specific Bugs (Track as Found)

| Compositor | Bug | Workaround |
|------------|-----|------------|
| (none yet) | — | — |

---

## 8. CI Verification Matrix

| Slice | X11 (Xvfb) | Wayland (weston) | Wayland (sway) | Notes |
|-------|------------|------------------|----------------|-------|
| A (Foundation) | Required | Required | Optional | Basic overlay visibility |
| B (X11 Topmost) | Required | N/A | N/A | X11-only slice |
| C (X11 Input Shape) | Required | N/A | N/A | X11-only slice |
| D (Layer-Shell) | N/A | N/A | Required | sway headless in CI |
| E (Input Region) | N/A | Required | N/A | weston for generic Wayland |
| F (Degrade) | N/A | Required | N/A | Verify degrade path |
| G (Multi-Monitor) | Required | Required | Optional | Both backends |

**CI job additions needed:**
1. `ubuntu-latest` with Xvfb + openbox (X11 tests)
2. `ubuntu-latest` with weston headless (generic Wayland)
3. `ubuntu-latest` with sway headless (layer-shell tests)

---

## 9. Summary

This document defines:
- **7 implementation slices** (A through G), each independently committable
- **5 Linux backends** in a priority fallback chain
- **Per-backend CI verification** using headless compositors
- **Concrete degrade paths** for each backend limitation

The per-region click-through contract is preserved on X11 and wlr-layer-shell compositors.
GNOME and restrictive Wayland environments get a documented degrade (topmost overlay that
captures all input).

**Total slice count:** 7

**Critical path:** Slices A → B → C (X11 full support) can proceed immediately.
Slices D → E → F (Wayland) can proceed in parallel after A.
Slice G (multi-monitor) depends on all backends.

---

## Sources

- `CCP.Core/Platform/IOverlaySurface.cs` — interface definition
- `CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs:11-20` — base window setup
- `CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:24-35` — Windows click-through
- `CCP.Avalonia/Compositor/CompositorWindow.axaml.cs:98-120, 149-175` — native style application
- `overlay-clickthrough` skill — per-region capture mask contract (2026-07-09 team review)
- XFixes specification — `https://www.x.org/releases/current/doc/fixesproto/fixesproto.txt`
- wlr-layer-shell protocol — `https://wayland.app/protocols/wlr-layer-shell-unstable-v1`
- Wayland input regions — `https://wayland.freedesktop.org/docs/html/apa.html#protocol-spec-wl_surface`
