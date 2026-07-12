# Linux IFrameSource Implementation Contract

**Date:** 2026-07-12  
**Scope:** Hard-seam Linux screen capture for AvatarTube live-screen mirror + screen-derived effects  
**Status:** Judgment-tier design document (read-only research artifact)  
**Closes:** Linux side of IFrameSource bring-up (readiness map item #2)

This document specifies the behavior contract, backend architecture, and implementation
slices for Linux screen capture (`IFrameSource`). X11 and Wayland are CO-EQUAL first-class
backends, not "X11 primary + Wayland appendix." Per owner directive (2026-07-12), the
implementation **must work on ANY Linux system** — multi-backend with graceful fallback.

---

## 1. IFrameSource Behavior Contract

The `IFrameSource` interface (`CCP.Core/Platform/IFrameSource.cs`) defines the platform-agnostic
desktop capture seam. Every platform implementation MUST provide these behaviors:

### 1.1 Interface Definition

```csharp
// CCP.Core/Platform/IFrameSource.cs (complete)
public interface IFrameSource
{
    Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default);
}

public sealed record RawFrame(int Width, int Height, byte[] BgraData);
```

### 1.2 Behavioral Requirements (cited from WindowsFrameSource.cs)

| Requirement | Windows Implementation | Citation |
|-------------|------------------------|----------|
| **Per-screen capture** | Captures the specific screen identified by `ScreenInfo.Bounds` | `WindowsFrameSource.cs:24-27` |
| **BGRA 32-bit format** | Returns 32-bit BGRA pixel data (`PixelFormat.Format32bppArgb`) | `WindowsFrameSource.cs:30,37` |
| **Synchronous capture** | GDI `CopyFromScreen` is synchronous; Task wrapper for async signature | `WindowsFrameSource.cs:21` |
| **Cancellation honored** | `ThrowIfCancellationRequested` before and after capture | `WindowsFrameSource.cs:23,36` |
| **Minimum 1x1 dimension** | Clamps width/height to `Math.Max(1, ...)` | `WindowsFrameSource.cs:26-27` |
| **No persistence** | Raw frames exist in memory only; never written to disk | implicit |

### 1.3 Consumer Requirements (AvatarTube + Effects)

The primary consumers are:
1. **AvatarTube live-screen mirror** — displays a live view of the desktop behind the avatar
2. **Screen-derived effects** — effects that react to screen content (color sampling, etc.)

**Cadence:** Consumers typically request frames at 10-30 FPS. The capture implementation should
be efficient enough to sustain this without excessive CPU load.

**Thread safety:** `CaptureAsync` may be called from any thread; implementations must be
thread-safe or explicitly document single-thread requirements.

---

## 2. Linux Backend Architecture

### 2.1 Runtime Backend Selection

The Linux frame source uses a runtime backend selector. Selection happens once at service
initialization and falls back to a no-op capture on unknown environments.

```
│                    LinuxFrameSourceBackendSelector                │
│  │ Detection order:                                              ││
│  │ 1. XDG_SESSION_TYPE == "wayland" + WAYLAND_DISPLAY set       ││
│  │    → WaylandBackendProbe                                      ││
│  │ 2. XDG_SESSION_TYPE == "x11" OR DISPLAY set                  ││
│  │    → X11BackendProbe                                          ││
│  │ 3. Neither                                                    ││
│  │    → FallbackFrameSource (black frame)                        ││

X11BackendProbe:
  ├─ XShmQueryExtension returns true? → X11ShmFrameSource (fast path)
  └─ Otherwise → X11BasicFrameSource (XGetImage fallback)

WaylandBackendProbe:
  ├─ org.freedesktop.portal.ScreenCast available via D-Bus?
  │   └─ AND PipeWire runtime present? → PortalPipeWireFrameSource
  ├─ wlr-screencopy-unstable-v1 in registry? → WlrScreencopyFrameSource
  └─ Neither → FallbackFrameSource (black frame, logged reason)
```

### 2.2 Backend Fallback Chain

| Priority | Backend | Capabilities | When Selected |
|----------|---------|--------------|---------------|
| 1 | `X11ShmFrameSource` | Full speed: XShm shared memory capture | X11 session with MIT-SHM extension |
| 2 | `X11BasicFrameSource` | Works but slower: XGetImage over the wire | X11 session without MIT-SHM |
| 3 | `PortalPipeWireFrameSource` | Full Wayland: xdg-desktop-portal + PipeWire | Wayland with portal + PipeWire |
| 4 | `WlrScreencopyFrameSource` | wlroots-only: direct screencopy protocol | Wayland + wlr-screencopy (sway, Hyprland) |
| 5 | `FallbackFrameSource` | No-op: returns black frame | Unknown session or all probes fail |

**Guarantee:** The fallback chain ensures SOMETHING always returns. The feature never crashes
on an unknown environment; it degrades to a black frame with a logged reason.

### 2.3 Seam Structure

```
CCP.Core/Platform/
├── IFrameSource.cs                     # Unchanged interface
├── LinuxSessionType.cs                 # Shared: session detection enum
└── RawFrame.cs                         # Unchanged record

CCP.Avalonia.Desktop.Linux/
├── LinuxFrameSource.cs                 # IFrameSource impl delegating to backend
├── LinuxFrameSourceBackendSelector.cs  # Runtime selection
├── FrameSourceBackends/
│   ├── ILinuxFrameSourceBackend.cs     # Backend abstraction
│   ├── X11ShmFrameSource.cs            # XShm fast path
│   ├── X11BasicFrameSource.cs          # XGetImage fallback
│   ├── PortalPipeWireFrameSource.cs    # Portal + PipeWire
│   ├── WlrScreencopyFrameSource.cs     # wlr-screencopy
│   └── FallbackFrameSource.cs          # Black frame fallback
└── Interop/
    ├── X11Interop.cs                   # XLib P/Invoke (shared with overlay)
    ├── X11ShmInterop.cs                # MIT-SHM P/Invoke
    └── PipeWireInterop.cs              # libpipewire bindings
```

---

## 3. X11 Backend Design

### 3.1 X11 Display Connection

Unlike overlay windows (which use Avalonia's existing X11 connection), frame capture may need
its own display connection for thread safety:

```csharp
// Option A: Reuse Avalonia's display (single-threaded capture)
// Option B: Open a dedicated display connection (thread-safe capture)
var display = XOpenDisplay(null); // Uses DISPLAY env var
if (display == IntPtr.Zero)
    throw new InvalidOperationException("Cannot open X11 display");
```

**Thread model decision:** If `CaptureAsync` is called from multiple threads, a dedicated
connection per call (or pooled connections) is needed. For single-thread consumer patterns,
reusing Avalonia's connection is acceptable.

### 3.2 XGetImage Basic Capture (Slow Path)

The simplest X11 capture uses `XGetImage`:

```csharp
// Basic XGetImage capture (synchronous, copies over X protocol)
var root = XDefaultRootWindow(display);
var image = XGetImage(display, root, x, y, width, height, AllPlanes, ZPixmap);

if (image == IntPtr.Zero)
    return new RawFrame(width, height, new byte[width * height * 4]); // black fallback

try
{
    // XImage struct layout: width, height, xoffset, format, data, byte_order, etc.
    var ximg = Marshal.PtrToStructure<XImage>(image);
    var bytes = new byte[width * height * 4];
    
    // Copy pixel data from ximg.data (format depends on visual depth)
    // Typically 32-bit BGRA on modern systems with TrueColor visuals
    Marshal.Copy(ximg.data, bytes, 0, bytes.Length);
    
    return new RawFrame(width, height, bytes);
}
finally
{
    XDestroyImage(image);
}
```

**Performance:** `XGetImage` copies pixels over the X protocol wire, which is slow for
high-resolution screens at high frame rates. This is the fallback when SHM is unavailable.

### 3.3 XShm Shared Memory Capture (Fast Path)

The MIT-SHM extension provides shared memory for zero-copy capture:

```csharp
// Step 1: Verify MIT-SHM is available
if (!XShmQueryExtension(display))
    return null; // Fall back to basic capture

// Step 2: Create shared memory segment
var shminfo = new XShmSegmentInfo();
var image = XShmCreateImage(display, visual, depth, ZPixmap, IntPtr.Zero,
    ref shminfo, width, height);

shminfo.shmid = shmget(IPC_PRIVATE, image->bytes_per_line * height, IPC_CREAT | 0777);
shminfo.shmaddr = image->data = shmat(shminfo.shmid, IntPtr.Zero, 0);
shminfo.readOnly = false;

XShmAttach(display, ref shminfo);

// Step 3: Capture into shared memory (fast, no wire copy)
XShmGetImage(display, root, image, x, y, AllPlanes);
XSync(display, false); // Wait for completion

// Step 4: Copy from shared memory to managed array
var bytes = new byte[width * height * 4];
Marshal.Copy(image->data, bytes, 0, bytes.Length);

// Step 5: Cleanup (detach segment, destroy image)
XShmDetach(display, ref shminfo);
shmdt(shminfo.shmaddr);
shmctl(shminfo.shmid, IPC_RMID, IntPtr.Zero);
XDestroyImage(image);
```

**Performance:** XShm capture can sustain 60 FPS on modern hardware. The shared memory
segment can be reused across frames for additional efficiency (pool pattern).

### 3.4 Multi-Monitor via XRandR

Screen bounds come from `ScreenInfo.Bounds`. On multi-monitor setups with XRandR:

```csharp
// ScreenInfo.Bounds provides the physical pixel rect for the target monitor.
// For root-window capture, these coordinates are absolute X11 screen coordinates.
// No additional XRandR query needed if ScreenInfo is already populated correctly.
var x = (int)screen.Bounds.X;
var y = (int)screen.Bounds.Y;
var width = (int)screen.Bounds.Width;
var height = (int)screen.Bounds.Height;
```

**DPI note:** X11 coordinates are physical pixels. If Avalonia provides DIP coordinates,
apply the screen scale factor before capture.

### 3.5 X11 P/Invoke Declarations

Key P/Invoke signatures needed:

```csharp
[DllImport("libX11.so.6")]
static extern IntPtr XOpenDisplay(string? display_name);

[DllImport("libX11.so.6")]
static extern int XCloseDisplay(IntPtr display);

[DllImport("libX11.so.6")]
static extern IntPtr XDefaultRootWindow(IntPtr display);

[DllImport("libX11.so.6")]
static extern IntPtr XGetImage(IntPtr display, IntPtr drawable,
    int x, int y, uint width, uint height, ulong plane_mask, int format);

[DllImport("libX11.so.6")]
static extern void XDestroyImage(IntPtr ximage);

[DllImport("libX11.so.6")]
static extern bool XShmQueryExtension(IntPtr display);

[DllImport("libXext.so.6")]
static extern IntPtr XShmCreateImage(IntPtr display, IntPtr visual, uint depth,
    int format, IntPtr data, ref XShmSegmentInfo shminfo, uint width, uint height);

[DllImport("libXext.so.6")]
static extern bool XShmAttach(IntPtr display, ref XShmSegmentInfo shminfo);

[DllImport("libXext.so.6")]
static extern bool XShmGetImage(IntPtr display, IntPtr drawable, IntPtr image,
    int x, int y, ulong plane_mask);

[DllImport("libXext.so.6")]
static extern bool XShmDetach(IntPtr display, ref XShmSegmentInfo shminfo);

[DllImport("libc.so.6")]
static extern int shmget(int key, IntPtr size, int shmflg);

[DllImport("libc.so.6")]
static extern IntPtr shmat(int shmid, IntPtr shmaddr, int shmflg);

[DllImport("libc.so.6")]
static extern int shmdt(IntPtr shmaddr);

[DllImport("libc.so.6")]
static extern int shmctl(int shmid, int cmd, IntPtr buf);
```

---

## 4. Wayland Backend Design

### 4.1 The Wayland Screen Capture Problem

**Critical:** Wayland has NO direct client screen capture API by design. Unlike X11 where any
client can read any window's pixels, Wayland is security-first: clients only see their own
surfaces. Screen capture requires compositor cooperation.

**Available paths:**
1. **xdg-desktop-portal ScreenCast** — standard D-Bus API, works on GNOME/KDE/wlroots, BUT
   requires an interactive permission prompt
2. **wlr-screencopy-unstable-v1** — wlroots protocol, no prompt, BUT wlroots-only (sway, Hyprland)
3. **Compositor-specific D-Bus** — GNOME Shell, KWin have internal APIs (unstable, not portable)

### 4.2 Portal + PipeWire Backend (Standard Path)

The `org.freedesktop.portal.ScreenCast` portal provides the standard Wayland screen capture:

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Portal ScreenCast Flow                                                    │
│                                                                          │
│  1. D-Bus: CreateSession() → session_handle                              │
│  2. D-Bus: SelectSources(session, {types: MONITOR}) → triggers prompt    │
│  3. USER: Picks monitor in system dialog (BLOCKING until user acts)     │
│  4. D-Bus: Start(session) → returns PipeWire node_id                     │
│  5. PipeWire: Connect to node, receive video frames                      │
│  6. Convert PipeWire buffer → RawFrame BGRA data                         │
└──────────────────────────────────────────────────────────────────────────┘
```

**The interactive permission prompt problem:** Step 3 is the killer for ambient apps. The
portal pops a system dialog asking "Allow CCP to record your screen?" every time. Options:

| Approach | UX | Feasibility |
|----------|-----|-------------|
| One-time prompt at startup | User sees dialog once per session | Acceptable if cached |
| Remember permission | Persistent authorization | Portal may cache; compositor-dependent |
| Auto-decline and fallback | No dialog, feature degrades | Safe default for ambient use |

**Implementation note:** Check `org.freedesktop.portal.ScreenCast` version for `persist_mode`
(portal 4+). If supported, request persistent authorization to avoid repeated prompts.

### 4.3 Portal + PipeWire Implementation Outline

```csharp
// Step 1: D-Bus connection to portal
using var connection = new Connection(Address.Session);
var portal = connection.CreateProxy<IScreenCast>("org.freedesktop.portal.Desktop",
    "/org/freedesktop/portal/desktop");

// Step 2: Create session
var sessionPath = await portal.CreateSessionAsync(new Dictionary<string, object> {
    ["handle_token"] = GenerateToken(),
    ["session_handle_token"] = GenerateToken()
});

// Step 3: Select sources (triggers permission dialog)
await portal.SelectSourcesAsync(sessionPath, new Dictionary<string, object> {
    ["types"] = (uint)1, // MONITOR
    ["multiple"] = false,
    ["persist_mode"] = (uint)2 // Request persistent authorization
});

// Step 4: Start capture (returns PipeWire node)
var (_, results) = await portal.StartAsync(sessionPath, "");
var nodeId = (uint)results["streams"][0]["node_id"];

// Step 5: Connect to PipeWire and receive frames
var loop = pw_main_loop_new(IntPtr.Zero);
var context = pw_context_new(pw_main_loop_get_loop(loop), IntPtr.Zero, 0);
var core = pw_context_connect(context, IntPtr.Zero, 0);
var stream = pw_stream_new(core, "ccp-capture", IntPtr.Zero);

// ... register buffer callbacks, convert SPA_VIDEO_FORMAT to BGRA ...
```

**Dependencies:** Requires `libpipewire-0.3` and D-Bus bindings (`Tmds.DBus` or similar).

### 4.4 wlr-screencopy Backend (wlroots Fast Path)

The `wlr-screencopy-unstable-v1` protocol provides direct, no-prompt screen capture on
wlroots-based compositors (sway, Hyprland, river, wayfire):

```
┌──────────────────────────────────────────────────────────────────────────┐
│ wlr-screencopy Flow                                                       │
│                                                                          │
│  1. Bind zwlr_screencopy_manager_v1 from wl_registry                     │
│  2. capture_output(output, overlay_cursor: false) → frame                │
│  3. Wait for frame.buffer_done event                                     │
│  4. Create wl_shm buffer, frame.copy(buffer)                             │
│  5. Wait for frame.ready event                                           │
│  6. Read pixel data from shm buffer                                      │
└──────────────────────────────────────────────────────────────────────────┘
```

**Advantages:**
- No permission prompt required
- Low latency (direct compositor→client copy)
- Works on sway, Hyprland, river, wayfire, and other wlroots compositors

**Limitations:**
- wlroots-only; does NOT work on GNOME or KDE
- Protocol is unstable (v1), may change

### 4.5 Wayland Backend Selection Logic

```csharp
public static ILinuxFrameSourceBackend SelectWaylandBackend(WaylandRegistry registry)
{
    // Prefer wlr-screencopy on wlroots compositors (no prompt, fast)
    if (registry.HasGlobal("zwlr_screencopy_manager_v1"))
    {
        return new WlrScreencopyFrameSource(registry);
    }
    
    // Fall back to portal on GNOME/KDE (may prompt)
    if (IsPortalAvailable())
    {
        // Check if we have cached permission
        if (HasCachedScreenCapturePermission())
        {
            return new PortalPipeWireFrameSource();
        }
        
        // No cached permission — return fallback for ambient use
        // (avoid popping a dialog without user action)
        Log.Warning("ScreenCast portal available but no permission; screen capture disabled");
        return new FallbackFrameSource("Portal permission not granted");
    }
    
    return new FallbackFrameSource("No Wayland screen capture method available");
}
```

### 4.6 Wayland Compositor Compatibility Matrix

| Compositor | wlr-screencopy | Portal + PipeWire | Notes |
|------------|----------------|-------------------|-------|
| sway | Yes (native) | Yes | Best path: wlr-screencopy |
| Hyprland | Yes (native) | Yes | Best path: wlr-screencopy |
| river | Yes (native) | No | wlr-screencopy only |
| wayfire | Yes (native) | Maybe | wlr-screencopy recommended |
| GNOME/Mutter | No | Yes (with prompt) | Portal only; prompt always |
| KDE/KWin | No | Yes (with prompt) | Portal only; KWin may cache |
| Weston | No | Maybe | Minimal portal support |

---

## 5. Graceful Fallback Design

### 5.1 Fallback Frame Source

When no capture method is available, return a black frame:

```csharp
public sealed class FallbackFrameSource : ILinuxFrameSourceBackend
{
    private readonly string _reason;
    private bool _logged;
    
    public FallbackFrameSource(string reason) => _reason = reason;
    
    public Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken ct)
    {
        if (!_logged)
        {
            Log.Warning("Screen capture unavailable: {Reason}. Features depending on " +
                "screen capture will show black/empty. This is expected on this platform/session.",
                _reason);
            _logged = true;
        }
        
        var width = Math.Max(1, (int)screen.Bounds.Width);
        var height = Math.Max(1, (int)screen.Bounds.Height);
        return Task.FromResult(new RawFrame(width, height, new byte[width * height * 4]));
    }
}
```

### 5.2 Consumer Degrade Behavior

Consumers of `IFrameSource` must handle empty/black frames gracefully:
- **AvatarTube live mirror:** Shows black/dim background instead of live desktop
- **Screen-derived effects:** Operate on zero/default values, producing no visual change

This degrade is acceptable; it never crashes and preserves other app functionality.

---

## 6. Implementation Slice Plan

Each slice is independently committable with its own verification gate.

### Slice A: Backend Selection + Fallback (Foundation)

**Goal:** Runtime backend detection and graceful fallback with black frames.

**Files:**
- `CCP.Avalonia.Desktop.Linux/LinuxFrameSource.cs` — `IFrameSource` impl
- `CCP.Avalonia.Desktop.Linux/LinuxFrameSourceBackendSelector.cs` — detection logic
- `CCP.Avalonia.Desktop.Linux/FrameSourceBackends/ILinuxFrameSourceBackend.cs` — backend interface
- `CCP.Avalonia.Desktop.Linux/FrameSourceBackends/FallbackFrameSource.cs` — black frame fallback

**Verification (CI):**
```bash
# Headless (no display) — fallback path
unset DISPLAY WAYLAND_DISPLAY XDG_SESSION_TYPE
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-framesource-fallback
# Assert: "Screen capture unavailable" logged, frame returned (black, non-null)
```

**Acceptance:**
- [ ] Session type correctly detected from environment
- [ ] Fallback returns valid (black) RawFrame on unknown sessions
- [ ] Reason logged exactly once per session
- [ ] Consumer receives non-null frame, never crashes

---

### Slice B: X11 Basic Capture (XGetImage)

**Goal:** Working screen capture on X11 via XGetImage (slow but universal path).

**Files:**
- `CCP.Avalonia.Desktop.Linux/Interop/X11Interop.cs` — XGetImage, XDefaultRootWindow, XDestroyImage
- `CCP.Avalonia.Desktop.Linux/FrameSourceBackends/X11BasicFrameSource.cs` — XGetImage capture

**Verification (CI):**
```bash
# X11 via Xvfb
Xvfb :99 -screen 0 1920x1080x24 &
export DISPLAY=:99

# Set a known background color for verification
xsetroot -solid "#FF5500"

dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-framesource
# Assert: Frame captured with non-zero pixel data (orange pixels present)
```

**Acceptance:**
- [ ] X11 display opened successfully
- [ ] XGetImage returns valid image handle
- [ ] BGRA pixel data extracted correctly
- [ ] Frame dimensions match requested screen bounds
- [ ] CI passes on ubuntu-latest with Xvfb

---

### Slice C: X11 SHM Fast Path

**Goal:** High-performance X11 capture via MIT-SHM shared memory.

**Files:**
- `CCP.Avalonia.Desktop.Linux/Interop/X11ShmInterop.cs` — SHM P/Invoke declarations
- `CCP.Avalonia.Desktop.Linux/FrameSourceBackends/X11ShmFrameSource.cs` — XShmGetImage capture

**Verification (CI):**
```bash
Xvfb :99 -screen 0 1920x1080x24 +extension MIT-SHM &
export DISPLAY=:99

dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-framesource-shm
# Assert: SHM capture used (log line), frame captured successfully
```

**Acceptance:**
- [ ] MIT-SHM extension detected via XShmQueryExtension
- [ ] Shared memory segment created and attached
- [ ] XShmGetImage returns valid frame
- [ ] Cleanup (detach, destroy) executes without leak
- [ ] Falls back to X11Basic when SHM unavailable

---

### Slice D: wlr-screencopy Backend (wlroots)

**Goal:** No-prompt screen capture on wlroots compositors (sway, Hyprland).

**Files:**
- `CCP.Avalonia.Desktop.Linux/Interop/WaylandInterop.cs` — base Wayland bindings (shared)
- `CCP.Avalonia.Desktop.Linux/Interop/WlrScreencopyInterop.cs` — screencopy protocol bindings
- `CCP.Avalonia.Desktop.Linux/FrameSourceBackends/WlrScreencopyFrameSource.cs` — screencopy impl

**Verification (CI):**
```bash
# sway in headless mode
WLR_BACKENDS=headless WLR_LIBINPUT_NO_DEVICES=1 sway &
export WAYLAND_DISPLAY=wayland-0
sleep 2

dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-framesource-wlr
# Assert: "wlr-screencopy" in log, frame captured
```

**Acceptance:**
- [ ] zwlr_screencopy_manager_v1 bound from registry
- [ ] capture_output creates frame handle
- [ ] Frame data received via wl_shm buffer
- [ ] CI passes on ubuntu-latest with sway headless

---

### Slice E: Portal + PipeWire Backend (GNOME/KDE)

**Goal:** Portal-based capture for non-wlroots Wayland (GNOME, KDE).

**Files:**
- `CCP.Avalonia.Desktop.Linux/Interop/PipeWireInterop.cs` — libpipewire bindings
- `CCP.Avalonia.Desktop.Linux/Interop/ScreenCastPortalClient.cs` — D-Bus portal client
- `CCP.Avalonia.Desktop.Linux/FrameSourceBackends/PortalPipeWireFrameSource.cs` — portal impl

**Verification (CI):**
```bash
# Portal + PipeWire in CI is HARD — requires real session bus and portal daemon
# This slice requires MANUAL verification on a real GNOME/KDE desktop
# CI verification is compile + mock-based unit test only

dotnet build CCP.Avalonia.Desktop.Linux
dotnet test CCP.Core.Tests --filter "FrameSource"
# Assert: Code compiles, mock tests pass
```

**Manual verification (real desktop):**
- [ ] Portal session created successfully
- [ ] Permission dialog appears when no cached permission
- [ ] Persistent permission works on portal v4+
- [ ] PipeWire stream receives video frames
- [ ] Frames converted to BGRA correctly

**Acceptance:**
- [ ] Compiles without error
- [ ] Portal D-Bus client implemented
- [ ] PipeWire stream connection implemented
- [ ] Graceful fallback when portal unavailable

---

### Slice F: Multi-Monitor Support

**Goal:** Capture the correct monitor based on ScreenInfo bounds.

**Files:**
- Updates to all backends to respect `screen.Bounds`
- `CCP.Avalonia.Desktop.Linux/LinuxFrameSource.cs` — per-screen routing

**Verification (CI):**
```bash
# Xvfb with two screens
Xvfb :99 -screen 0 1920x1080x24 -screen 1 1920x1080x24 &
export DISPLAY=:99

dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-framesource-multimonitor
# Assert: Both screens captured with correct bounds
```

**Acceptance:**
- [ ] Screen bounds correctly passed to capture APIs
- [ ] X11: Capture offset matches ScreenInfo.Bounds.X/Y
- [ ] Wayland: Output selection matches requested monitor
- [ ] CI passes with multi-screen Xvfb

---

## 7. Risk/Unknowns Section

These items CANNOT be settled without running on a real Linux desktop or additional research:

### 7.1 Genuine Unknowns

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Portal permission UX** | User may be confused by recurring permission dialogs on GNOME | Document in user guide; check persist_mode support |
| **PipeWire version compat** | Older distros may have PipeWire 0.2 (incompatible API) | Require PipeWire 0.3+; document in Linux requirements |
| **XWayland capture** | Capturing X11 apps under XWayland may produce incorrect bounds | Test both native Wayland and XWayland scenarios |
| **NVIDIA GPU issues** | Some NVIDIA drivers have PipeWire/DMA-BUF issues | Document as known limitation; fallback to black frame |
| **High-DPI scaling** | Wayland fractional scaling may affect capture bounds | Test with scale factors 1.25, 1.5, 2.0 |
| **CI headless limitations** | Headless compositors may not exercise all code paths | Supplement CI with manual testing |

### 7.2 Requires Real Desktop Testing

- Permission dialog flow on GNOME (with and without cached permission)
- Permission dialog flow on KDE (Plasma 5 vs Plasma 6)
- wlr-screencopy frame timing and latency
- Multi-monitor capture with mixed DPI
- Capture during fullscreen apps/games
- Memory leak verification under sustained capture

### 7.3 Compositor-Specific Bugs (Track as Found)

| Compositor | Bug | Workaround |
|------------|-----|------------|
| (none yet) | — | — |

---

## 8. CI Verification Matrix

| Slice | X11 (Xvfb) | Wayland (sway) | GNOME/Portal | Notes |
|-------|------------|----------------|--------------|-------|
| A (Fallback) | N/A | N/A | N/A | Headless no-display test |
| B (X11 Basic) | Required | N/A | N/A | XGetImage verification |
| C (X11 SHM) | Required | N/A | N/A | MIT-SHM verification |
| D (wlr-screencopy) | N/A | Required | N/A | sway headless |
| E (Portal) | N/A | N/A | Manual | Real session required |
| F (Multi-Monitor) | Required | Optional | N/A | Multi-screen Xvfb |

**CI job additions needed:**
1. `ubuntu-latest` with Xvfb (X11 tests) — **existing, extend**
2. `ubuntu-latest` with sway headless (wlr-screencopy test)
3. Manual verification step documented for Portal/GNOME/KDE

---

## 9. Summary

This document defines:
- **6 implementation slices** (A through F), each independently committable
- **5 Linux backends** in a priority fallback chain
- **Per-backend CI verification** using headless compositors where possible
- **Concrete graceful fallback** (black frame with logged reason)

The X11 path (XShm + XGetImage fallback) provides full coverage for X11 sessions.
The Wayland path provides full coverage on wlroots compositors (wlr-screencopy) and
best-effort on GNOME/KDE (portal with permission prompt or cached authorization).
Unknown environments gracefully degrade to black frames.

**Total slice count:** 6

**Critical path:** Slices A → B → C (X11) can proceed immediately.
Slices D, E (Wayland) can proceed in parallel after A.
Slice F (multi-monitor) depends on backend completion.

---

## Sources

- `CCP.Core/Platform/IFrameSource.cs` — interface definition
- `CCP.Avalonia.Desktop.Windows/WindowsFrameSource.cs` — Windows reference impl
- `linux-overlay-contract.md` — template for backend architecture
- `linux-macos-readiness-map.md` — governing multi-backend principle
- X11 MIT-SHM specification — https://www.x.org/releases/current/doc/xextproto/shm.html
- wlr-screencopy protocol — https://wayland.app/protocols/wlr-screencopy-unstable-v1
- xdg-desktop-portal ScreenCast — https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.ScreenCast.html
- PipeWire documentation — https://docs.pipewire.org/
