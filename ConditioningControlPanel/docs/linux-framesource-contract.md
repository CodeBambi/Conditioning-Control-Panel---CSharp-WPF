# Linux IFrameSource Implementation Contract

**Date:** 2026-07-12 (hardened judgment-tier revision — supersedes the same-day draft)  
**Scope:** Hard-seam Linux screen capture (`IFrameSource`)  
**Status:** Authoritative design contract (read-only research artifact; no code changes)  
**Closes:** Linux side of IFrameSource bring-up (readiness map leverage item #2)

This document specifies the behavior contract, backend architecture, and implementation
slices for Linux screen capture (`IFrameSource`). X11 and Wayland are CO-EQUAL first-class
backends. Per owner directive (2026-07-12), the implementation **must work on ANY Linux
system** — multi-backend, runtime-selected, graceful fallback, CI-verified per backend.

> **Revision note:** this version corrects protocol/API errors in the first draft (XShm
> library placement, XImage stride/alpha handling, missing Xlib error traps, portal
> PipeWire fd handshake, restore_token omission, Wayland backend priority inconsistency,
> multi-screen Xvfb misuse) and strengthens the portal permission-persistence strategy
> and the privacy contract. Claims that could not be re-verified against upstream docs in
> this pass are explicitly marked **[confidence: …]**.

---

## 1. IFrameSource Behavior Contract

The `IFrameSource` interface (`CCP.Core/Platform/IFrameSource.cs`) defines the
platform-agnostic desktop capture seam ("Desktop frame capture source for screen
OCR/effects" per its XML doc).

### 1.1 Interface Definition

```csharp
// CCP.Core/Platform/IFrameSource.cs (complete)
public interface IFrameSource
{
    Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default);
}

public sealed record RawFrame(int Width, int Height, byte[] BgraData);
```

`ScreenInfo` (`CCP.Core/Platform/IScreenInfo.cs:10`) is
`record ScreenInfo(string Name, PixelRect Bounds, PixelRect WorkingArea, double Scaling)`.

### 1.2 Behavioral Requirements (cited from `CCP.Avalonia.Desktop.Windows/WindowsFrameSource.cs`)

Note the correct path: the Windows impl lives at the head root, **not** under `Platform/`.

| Requirement | Windows Implementation | Citation |
|-------------|------------------------|----------|
| **Per-screen capture** | Captures the rect identified by `ScreenInfo.Bounds` (absolute virtual-desktop pixels) | `WindowsFrameSource.cs:25-29,34` |
| **BGRA 32-bit format** | `PixelFormat.Format32bppArgb` bitmap, `LockBits` copy | `WindowsFrameSource.cs:31,40` |
| **Tightly packed rows** | Buffer is `stride * height`; for `Format32bppArgb`, `stride == width * 4` always — so `BgraData.Length == Width * Height * 4`, no row padding | `WindowsFrameSource.cs:43-46` |
| **Synchronous capture** | GDI `CopyFromScreen` is synchronous; `Task.FromResult` wrapper | `WindowsFrameSource.cs:34,46` |
| **Cancellation honored** | `ThrowIfCancellationRequested` before and after the blit | `WindowsFrameSource.cs:23,37` |
| **Minimum 1x1 dimension** | `Math.Max(1, …)` clamps | `WindowsFrameSource.cs:28-29` |
| **No persistence** | Frames exist in memory only | implicit; made explicit in §1.4 |

**RawFrame packing contract (normative, was implicit):** `BgraData` is tightly packed
BGRA, row-major, `Width * Height * 4` bytes, alpha byte present (0xFF for opaque desktop
content is acceptable). Linux backends whose native buffers carry row padding
(`bytes_per_line > width * 4`) or undefined alpha (depth-24 visuals) **must repack** to
this contract. Consumers index by `Width`/`Height` and will corrupt on padded rows.

### 1.3 Consumers and Cadence (corrected)

Wired consumers today (grep-verified):
- `CCP.Avalonia/ViewModels/Tabs/LabTabViewModel.cs:39,73` — injects the source into the
  webcam calibration/gaze windows.
- `CCP.Avalonia/Windows/WebcamCalibrationWindow.axaml.cs:59,130`,
  `WebcamGazeTrackerWindow.axaml.cs:30,40`, `WebcamQuickRecalWindow.axaml.cs:35,50` —
  screen-frame input for calibration/preview loops.

Planned consumers (readiness map): AvatarTube live-screen mirror, screen-derived effects,
screen OCR keyword triggers.

**Cadence contract:** implementations must be correct for **one-shot** calls (OCR-style —
no mandatory warm-up state between calls) and sustain a **~10-15 FPS preview loop at
1080p** without pathological CPU use. A backend MAY have a slow first call (session
establishment — the portal backend needs a D-Bus + PipeWire handshake); this is allowed,
but subsequent calls must be cheap. `CaptureAsync` may be called from any thread; Linux
backends serialize internally (§3.1).

### 1.4 Privacy Hard-Line (NEW — normative)

Screen frames contain arbitrary user content (messages, passwords on screen, medical
data). The same class of hard line as the webcam contract applies:

- **Raw frames are memory-only.** Never written to disk, never sent over the network,
  never logged (not even dimensions + a sample; log dimensions only if needed).
- Derived, non-reconstructable data (OCR keyword *hits*, average colors, calibration
  coefficients) may flow onward per each consumer's own contract.
- The only thing any backend may persist is the **portal restore token** (§4.4) — an
  opaque authorization string that contains no image data. Store it via `ISecretStore`.

Any implementation violating this is a security bug, same severity as the webcam rule.

---

## 2. Linux Backend Architecture

### 2.1 Runtime Backend Selection

Session detection reuses `CCP.Core/Platform/LinuxSessionType.cs` + `LinuxSessionDetector.cs`
**already introduced by the overlay contract implementation** (branch
`feature/linux-overlay`) — do not re-invent detection. Wayland is checked FIRST: on any
Wayland session `DISPLAY` is almost always also set (XWayland), so a DISPLAY-first check
would misroute every Wayland desktop onto the X11 path.

```
LinuxFrameSourceBackendSelector (selection once, at first capture or DI init)
  1. Wayland session (WAYLAND_DISPLAY set, or XDG_SESSION_TYPE == "wayland")
       → WaylandBackendProbe
  2. X11 session (DISPLAY set)
       → X11BackendProbe
  3. Neither
       → FallbackFrameSource (black frame, logged reason)

X11BackendProbe:
  ├─ MIT-SHM usable? (XShmQueryExtension AND display is local AND a test
  │   attach round-trip succeeds)            → X11ShmFrameSourceBackend
  └─ Otherwise                               → X11BasicFrameSourceBackend (XGetImage)

WaylandBackendProbe (registry-driven; the registry is the ground truth, not the
compositor name):
  ├─ ext_image_copy_capture_manager_v1 in registry?   → ExtImageCopyCaptureBackend
  ├─ zwlr_screencopy_manager_v1 in registry?          → WlrScreencopyBackend
  ├─ org.freedesktop.portal.ScreenCast on D-Bus AND PipeWire runtime present?
  │                                                    → PortalPipeWireBackend
  └─ None                                              → FallbackFrameSource
```

**XWayland note:** on a Wayland session, the X11 XGetImage path only sees XWayland
content (typically a black root). Never fall from a Wayland probe to the X11 backend;
fall to `FallbackFrameSource`.

### 2.2 Backend Fallback Chain (priority corrected)

The first draft's table put the portal above wlr-screencopy while its code sketch did
the opposite. Authoritative order — **prompt-free compositor protocols before the
portal**, because an ambient app must not depend on a permission dialog when a silent
path exists:

| Priority | Backend | Capabilities | When Selected |
|----------|---------|--------------|---------------|
| 1 | `X11ShmFrameSourceBackend` | Fast: MIT-SHM shared-memory capture, segment reused across frames | X11 session, local display, SHM attach verified |
| 2 | `X11BasicFrameSourceBackend` | Universal but slower: XGetImage over the wire | X11 session without usable MIT-SHM |
| 3 | `ExtImageCopyCaptureBackend` | Modern standard, no prompt, damage-driven sessions | Wayland + `ext-image-copy-capture-v1` (wlroots 0.18+/sway 1.10+, niri; KDE recent Plasma 6.x **[confidence: KWin support plausible but unverified — probe the registry, never assume]**) |
| 4 | `WlrScreencopyBackend` | No prompt, per-frame request | Wayland + `zwlr_screencopy_manager_v1` (sway, Hyprland, river, wayfire — ubiquitous on wlroots; deprecated upstream in favor of #3 but shipping everywhere) |
| 5 | `PortalPipeWireBackend` | Standard path for GNOME/KDE; one-time prompt then silent restore via `restore_token` | Wayland with portal + PipeWire, no compositor protocol available |
| 6 | `FallbackFrameSource` | Black frame + logged reason | Unknown session or all probes fail |

**Guarantee:** something always returns. Never crash; degrade to a black frame with a
reason logged exactly once.

### 2.3 Seam Structure (aligned with the landed overlay-branch layout)

All new Linux files go under `CCP.Avalonia.Desktop.Linux/Platform/…` (the draft had them
at the head root — wrong; the overlay implementation already established `Platform/`,
`Platform/Backends/`, `Platform/Interop/`).

```
CCP.Core/Platform/
├── IFrameSource.cs                     # UNCHANGED
├── LinuxSessionType.cs                 # EXISTS (overlay slices, feature/linux-overlay)
└── LinuxSessionDetector.cs             # EXISTS (overlay slices)

CCP.Avalonia.Desktop.Linux/Platform/
├── LinuxFrameSource.cs                     # IFrameSource impl delegating to backend
├── LinuxFrameSourceBackendSelector.cs      # Runtime probe + selection
├── FrameSourceBackends/
│   ├── ILinuxFrameSourceBackend.cs         # Backend abstraction (+ Dispose)
│   ├── X11ShmFrameSourceBackend.cs
│   ├── X11BasicFrameSourceBackend.cs
│   ├── ExtImageCopyCaptureBackend.cs
│   ├── WlrScreencopyBackend.cs
│   ├── PortalPipeWireBackend.cs
│   └── FallbackFrameSource.cs
└── Interop/
    ├── X11Interop.cs                       # EXISTS (overlay) — extend, do not fork
    ├── X11ShmInterop.cs                    # NEW: libXext MIT-SHM + libc shm*
    ├── WaylandInterop.cs                   # EXISTS (overlay) — extend
    ├── WlrScreencopyInterop.cs             # NEW
    ├── ExtImageCopyCaptureInterop.cs       # NEW
    ├── ScreenCastPortalClient.cs           # NEW: D-Bus portal client (Tmds.DBus.Protocol)
    └── PipeWireInterop.cs                  # NEW: libpipewire-0.3 bindings
```

---

## 3. X11 Backend Design

### 3.1 Display Connection and Threading (corrected)

The draft's "Option A: reuse Avalonia's display" is not viable — Avalonia does not
expose its `Display*`. Design:

- Open **one dedicated** `XOpenDisplay(null)` per backend instance at init; close on
  dispose.
- Serialize all Xlib calls on that display behind a single lock (or a single capture
  thread). Xlib is not thread-safe without `XInitThreads()`, and `XInitThreads` must be
  the *first* Xlib call in the process — we cannot guarantee that ordering against
  Avalonia's own X11 usage, so **do not rely on `XInitThreads`; rely on serialization
  of our private connection**. One connection touched by one thread at a time needs no
  Xlib-internal locking.
- `CaptureAsync` from arbitrary threads is satisfied by taking the lock (or posting to
  the capture thread) inside the backend.

### 3.2 Xlib Error Traps (NEW — mandatory, the draft's biggest omission)

The **default Xlib error handler terminates the process**. Two realistic error paths:

- `XGetImage`/`XShmGetImage` raise `BadMatch` if the requested rect is not fully inside
  the drawable (monitor unplugged/resized between `ScreenInfo` snapshot and capture).
- `XShmAttach` raises `BadAccess` *asynchronously* (delivered later, e.g. remote display
  or SHM policy) — you must `XSync` and trap to detect it.

For an ambient app this is a crash-on-race unless trapped. Required pattern (a scoped
error trap, like GDK's `error_trap_push/pop`):

```csharp
// XSetErrorHandler is PROCESS-GLOBAL (not per-Display). Install once at backend init,
// chain to the previous handler for events on displays that are not ours:
private static XErrorHandlerDelegate? _previous;
private static int ErrorHandler(IntPtr display, ref XErrorEvent ev)
{
    if (display == _ourDisplay) { _lastError = ev.error_code; return 0; } // swallow
    return _previous?.Invoke(display, ref ev) ?? 0;                        // chain
}
// Trap scope: _lastError = 0; <Xlib call>; XSync(display, false); check _lastError.
```

On a trapped error, return the fallback black frame for that call (and if errors repeat,
demote the backend to `FallbackFrameSource`). The delegate passed to `XSetErrorHandler`
must be kept alive (GC handle) for the process lifetime.

### 3.3 XGetImage Basic Capture (slow universal path — pixel handling corrected)

```csharp
var root = XDefaultRootWindow(display);
// AllPlanes = ~0UL, ZPixmap = 2
var imagePtr = XGetImage(display, root, x, y, (uint)width, (uint)height, AllPlanes, ZPixmap);
if (imagePtr == IntPtr.Zero) return BlackFrame(width, height);
try
{
    var ximg = Marshal.PtrToStructure<XImage>(imagePtr);
    // CORRECTED vs draft: honor bytes_per_line (rows may be padded) and normalize alpha.
    // Root visuals are typically depth 24 (32 bpp ZPixmap, high byte UNDEFINED) — the
    // straight Marshal.Copy of width*height*4 bytes in the draft is wrong on both counts.
    var bytes = new byte[width * height * 4];
    for (var row = 0; row < height; row++)
    {
        Marshal.Copy(ximg.data + row * ximg.bytes_per_line, bytes, row * width * 4, width * 4);
    }
    if (ximg.depth == 24)
        for (var i = 3; i < bytes.Length; i += 4) bytes[i] = 0xFF; // force opaque alpha
    return new RawFrame(width, height, bytes);
}
finally
{
    XDestroyImage(imagePtr);
}
```

Notes:
- Byte order: on little-endian servers (`LSBFirst`, i.e. every x86/ARM desktop) a 32-bit
  ZPixmap from a standard TrueColor visual is B,G,R,X in memory — matching BGRA directly.
  Big-endian is out of scope; assert `ximg.byte_order == LSBFirst` in debug.
- `XImage` contains function pointers (`f.*`); define the marshaling struct with exact
  native layout (see Avalonia's own `Avalonia.X11/XLib.cs` `XImage` as the layout
  reference — it P/Invokes `XGetImage`/`XDestroyImage` from `libX11.so.6` the same way).
- `XDestroyImage` is historically a Xutil.h macro, but libX11 exports the symbol and
  Avalonia's X11 backend P/Invokes it successfully — safe to P/Invoke.
  **[confidence: high — anchored on Avalonia's shipping interop; if a stripped libX11
  build ever lacks it, free via the XImage `f.destroy_image` function pointer.]**
- Trap `BadMatch` per §3.2 — do not let a monitor-hotplug race kill the app.

### 3.4 XShm Fast Path (lifecycle corrected)

Draft errors fixed: (1) `XShmQueryExtension` and all `XShm*` functions live in
**libXext.so.6**, not libX11; (2) creating/destroying the segment **per frame** is
wasteful and leak-prone — create once, reuse, recreate only on size change; (3)
`shmget` mode `0777` is world-readable-writable shared memory holding screen pixels —
use `0600`; (4) `IPC_RMID` should be marked **immediately after the server attach is
confirmed** so the kernel reclaims the segment even if the process dies.

Lifecycle:

```
Init (per capture size):
  XShmQueryExtension(display)?                 else → basic backend
  image = XShmCreateImage(display, visual, depth, ZPixmap, NULL, ref shminfo, w, h)
  shminfo.shmid  = shmget(IPC_PRIVATE, image->bytes_per_line * h, IPC_CREAT | 0600)
  shminfo.shmaddr = image->data = shmat(shminfo.shmid, NULL, 0)
  shminfo.readOnly = false
  XShmAttach(display, ref shminfo); XSync(display, false)   // trap async BadAccess (§3.2)
  attach failed? → tear down, fall back to basic backend
  shmctl(shminfo.shmid, IPC_RMID, NULL)        // server + client attached; safe on Linux

Per frame:
  XShmGetImage(display, root, image, x, y, AllPlanes)   // returns Bool; x,y = root coords
  XSync(display, false)
  repack rows honoring bytes_per_line + alpha (same as §3.3)

Dispose / size change:
  XShmDetach(display, ref shminfo); XSync
  shmdt(shminfo.shmaddr); XDestroyImage(image)
```

**Locality guard:** MIT-SHM only works when client and server share a machine. A remote
`DISPLAY` (SSH forwarding) may still report the extension present; the **attach
round-trip is the real probe** — on failure fall back to XGetImage silently.

Sustains 60 FPS at 1080p; our ~10-15 FPS budget is comfortable.

### 3.5 Multi-Monitor

`ScreenInfo.Bounds` is an absolute physical-pixel rect in the X11 virtual desktop
(one root window spans all XRandR monitors). Root-window capture at
`(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height)` is sufficient — no additional
XRandR queries needed at capture time. Defensively clamp the rect to the root window
geometry (monitor layouts change; see §3.2 BadMatch).

DPI: X11 coordinates are physical pixels; Avalonia's `Screens` on X11 report physical
`PixelRect` bounds, so no scaling conversion is applied. Assert, don't assume, in the
smoke test (compare capture size vs `xrandr` output).

### 3.6 P/Invoke Declarations (libraries corrected)

```csharp
// libX11.so.6
[DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(string? name);
[DllImport("libX11.so.6")] static extern int    XCloseDisplay(IntPtr display);
[DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
[DllImport("libX11.so.6")] static extern IntPtr XGetImage(IntPtr display, IntPtr drawable,
    int x, int y, uint width, uint height, ulong plane_mask, int format);
[DllImport("libX11.so.6")] static extern int    XDestroyImage(IntPtr ximage);
[DllImport("libX11.so.6")] static extern int    XSync(IntPtr display, bool discard);
[DllImport("libX11.so.6")] static extern IntPtr XSetErrorHandler(XErrorHandlerDelegate handler);

// libXext.so.6  (CORRECTED: XShmQueryExtension was wrongly under libX11 in the draft)
[DllImport("libXext.so.6")] static extern bool XShmQueryExtension(IntPtr display);
[DllImport("libXext.so.6")] static extern IntPtr XShmCreateImage(IntPtr display, IntPtr visual,
    uint depth, int format, IntPtr data, ref XShmSegmentInfo shminfo, uint width, uint height);
[DllImport("libXext.so.6")] static extern bool XShmAttach(IntPtr display, ref XShmSegmentInfo shminfo);
[DllImport("libXext.so.6")] static extern bool XShmGetImage(IntPtr display, IntPtr drawable,
    IntPtr image, int x, int y, ulong plane_mask);
[DllImport("libXext.so.6")] static extern bool XShmDetach(IntPtr display, ref XShmSegmentInfo shminfo);

// libc
[DllImport("libc", SetLastError = true)] static extern int    shmget(IntPtr key, UIntPtr size, int shmflg);
[DllImport("libc", SetLastError = true)] static extern IntPtr shmat(int shmid, IntPtr shmaddr, int shmflg);
[DllImport("libc", SetLastError = true)] static extern int    shmdt(IntPtr shmaddr);
[DllImport("libc", SetLastError = true)] static extern int    shmctl(int shmid, int cmd, IntPtr buf);
```

Extend the overlay branch's `Interop/X11Interop.cs`; put SHM + libc in `X11ShmInterop.cs`.

---

## 4. Wayland Backend Design

### 4.1 The Wayland Capture Reality

Wayland has no direct client capture by design. Three viable paths, in our priority
order: `ext-image-copy-capture-v1` (the standardized successor), `wlr-screencopy-v1`
(deprecated upstream but ubiquitous on wlroots), and the ScreenCast portal (universal
but permission-gated). Compositor-private D-Bus APIs (GNOME Shell screenshot API, KWin
internals) are **out of scope** — unstable, version-coupled, and the portal covers those
desktops.

**Connection/thread model (applies to all Wayland backends):** open a dedicated
`wl_display_connect(NULL)` owned by a single dispatch thread. The draft's
`wl_display_dispatch_pending()` sketch never *reads* the socket — correct pumping is a
dedicated thread in `wl_display_dispatch()` (or the
`prepare_read/read_events/dispatch_pending` triplet). `CaptureAsync` posts a request to
the dispatch thread and awaits a completion source.

### 4.2 ext-image-copy-capture-v1 (modern standard, no prompt)

Standardized in wayland-protocols (staging, 2024) as the successor to wlr-screencopy;
supports capture *sessions* with damage-driven frame delivery — a better fit for a
preview loop than per-frame requests. Implemented by wlroots 0.18+ (sway 1.10+), niri;
KDE support **[confidence: unverified — probe `ext_image_copy_capture_manager_v1` in the
registry at runtime; the probe is authoritative]**.

Flow: get an image-capture-source for the output
(`ext_output_image_capture_source_manager_v1.create_source(wl_output)`), create a
capture session, receive `buffer_size`/`shm_format` constraints, attach a `wl_shm`
buffer, `capture_frame`, wait `ready`, repack to BGRA.

### 4.3 wlr-screencopy-unstable-v1 (wlroots fast path, no prompt)

Per-frame request model:

```
1. Bind zwlr_screencopy_manager_v1 from the registry
2. capture_output(overlay_cursor = 0, wl_output)          → zwlr_screencopy_frame_v1
3. Frame emits buffer event(s) advertising shm format/size/stride
   (protocol v2 added buffer_done = "all buffer options sent")
4. Create wl_shm buffer with the ADVERTISED format+stride; frame.copy(buffer)
5. Frame emits flags (Y_INVERT!) then ready (or failed)
6. Read pixels; honor stride when repacking; if Y_INVERT is set, flip rows
7. Destroy the frame object; buffers are reusable across frames
```

Draft gaps fixed: the **`flags` event's `y_invert`** must be honored (upside-down frames
otherwise), the shm buffer must use the advertised stride/format (commonly `xrgb8888` =
BGRX little-endian → force alpha like §3.3), and `failed` must be handled (fall back to
black frame for that call).

Deprecated upstream in favor of §4.2 but shipping on effectively every wlroots
compositor in the field; keep both, probe §4.2 first.

### 4.4 Portal + PipeWire (GNOME/KDE) — the permission-persistence strategy

`org.freedesktop.portal.ScreenCast` handshake, with two draft omissions corrected:

```
1. D-Bus (session bus): CreateSession(options)      — portal calls return a Request
   object path; the actual result arrives via the Request's Response SIGNAL. Every
   step below follows that request/response pattern (draft showed plain awaits).
2. SelectSources(session, { types: MONITOR(1), multiple: false,
                            persist_mode: 2, restore_token: <stored token if any> })
3. Start(session, parent_window: "")
     Response includes: streams [(pipewire_node_id, props)], restore_token (NEW token)
4. OpenPipeWireRemote(session)  → PIPEWIRE FD              ← draft omitted this entirely
5. pw_context_connect_fd(context, fd, …)  — connect PipeWire over the portal-provided
   fd (NOT pw_context_connect() to the default socket, which fails under sandboxes
   and on systems where the user session socket isn't where libpipewire expects)
6. pw_stream targeting the node id; negotiate SPA video formats (BGRx/BGRA preferred);
   copy frames out of the stream buffers, repack to tight BGRA
```

**Restore token — the answer to the ambient-prompt problem (draft under-specified):**

- `persist_mode` (SelectSources, portal **version ≥ 4**): `0` = no persist, `1` =
  persist while the app runs, `2` = persist until explicitly revoked. Use `2`.
- On success, the `Start` response carries a **`restore_token`**. It is **single-use**:
  every successful `Start` invalidates the old token and issues a new one. Persist the
  fresh token after *every* session start, via `ISecretStore` (it grants silent screen
  capture — treat as a secret).
- Next launch: pass the stored token in `SelectSources`. If the portal accepts it, the
  session starts **with no dialog**. If the token is stale/revoked/config-changed, the
  dialog re-appears — see policy below.
- Support: GNOME 42+ and KDE Plasma portals implement persist/restore
  **[confidence: high]**. `xdg-desktop-portal-wlr` does **not** implement persistence
  **[confidence: high]** — irrelevant in practice because wlroots systems take the
  §4.2/§4.3 prompt-free paths.
- Check the portal's `version` property at runtime; if `< 4`, treat as
  no-persist-available and apply the policy below.

**Prompt policy for an ambient app (normative):**

1. The portal dialog may only ever appear as a direct consequence of an **explicit user
   action** — the user enabling a screen-capture-dependent feature in settings, or
   re-enabling it after revocation. Never prompt spontaneously at launch or mid-session.
2. First enable: run the handshake, accept the dialog cost once, store the token.
3. Subsequent launches with a valid token: silent restore during feature init.
4. Token restore fails: do **not** immediately re-prompt. Degrade to
   `FallbackFrameSource`, surface a non-modal notice ("screen features paused — click to
   re-authorize"), and only re-run the dialog on that click.
5. User cancels the dialog: mark the feature disabled, black-frame fallback, no re-prompt
   loop.

This keeps ANY-Linux compliance: wlroots = silent native protocols; GNOME/KDE = one
dialog ever (until revoked); everything else = honest black-frame degrade.

### 4.5 Compositor Compatibility Matrix (corrected)

Draft errors: river/wayfire *do* have portal capture via `xdg-desktop-portal-wlr` (it
backs the portal with wlr-screencopy on any wlroots compositor); GNOME is not "prompt
always" (restore token, GNOME 42+); KDE persistence is supported, not "may cache".

| Compositor | ext-image-copy-capture | wlr-screencopy | Portal + PipeWire | Selected path |
|------------|------------------------|----------------|-------------------|---------------|
| sway ≥1.10 | Yes | Yes | Yes (xdpw, no persist) | ext-image-copy-capture |
| sway ≤1.9 / older wlroots | No | Yes | Yes (xdpw) | wlr-screencopy |
| Hyprland | Version-dependent — probe | Yes | Yes (xdph) | registry probe decides |
| river / wayfire | wlroots-version-dependent | Yes | Yes (xdpw) | wlr-screencopy or newer |
| GNOME / Mutter | No | No | Yes; restore_token silent after first grant (GNOME 42+) | portal |
| KDE / KWin | probe **[unverified]** | No **[confidence: medium]** | Yes; persist supported | portal (or ext-icc if probe hits) |
| Weston | No | No | Portal support marginal | fallback (black) |

The **registry/D-Bus probe at runtime is always authoritative**; the matrix is
documentation, not logic.

---

## 5. Graceful Fallback Design

### 5.1 FallbackFrameSource

```csharp
public sealed class FallbackFrameSource : ILinuxFrameSourceBackend
{
    private readonly string _reason;
    private int _logged;

    public FallbackFrameSource(string reason) => _reason = reason;

    public Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _logged, 1) == 0)
            _logger.LogWarning("Screen capture unavailable: {Reason}. Screen-dependent " +
                "features will show black/empty on this session type.", _reason);

        var w = Math.Max(1, screen.Bounds.Width);
        var h = Math.Max(1, screen.Bounds.Height);
        return Task.FromResult(new RawFrame(w, h, new byte[w * h * 4]));
    }
}
```

(Log the *reason*, never frame content — §1.4.) Also used as the per-call degrade when a
live backend hits a trapped error, and as the permanent demotion target when a backend
fails repeatedly.

### 5.2 Consumer Degrade

- Webcam calibration/gaze windows: black screen-frame input; calibration still runs on
  camera frames.
- AvatarTube live mirror (when wired): black/dim backdrop.
- Screen OCR / screen-derived effects: no hits / no-op.

Never a crash; other app functionality unaffected.

---

## 6. Implementation Slice Plan

Each slice is independently committable with its own gate. All files under
`CCP.Avalonia.Desktop.Linux/Platform/…` (§2.3). Every slice must also keep the standard
repo gates green (`CCP.Desktop.slnf` 0 errors, Core tests, smoke).

### Slice A: Selector + Fallback (Foundation)

**Files:**
- `Platform/LinuxFrameSource.cs`
- `Platform/LinuxFrameSourceBackendSelector.cs` (reuses `LinuxSessionDetector` from Core)
- `Platform/FrameSourceBackends/ILinuxFrameSourceBackend.cs`
- `Platform/FrameSourceBackends/FallbackFrameSource.cs`
- `Program.cs` — DI registration `services.AddSingleton<IFrameSource, LinuxFrameSource>()`

**CI (headless, no display):**
```bash
env -u DISPLAY -u WAYLAND_DISPLAY -u XDG_SESSION_TYPE \
  dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-framesource-fallback
# Assert: "Screen capture unavailable" logged once; RawFrame returned, correct byte length
```

**Acceptance:**
- [ ] Wayland-before-X11 detection order (§2.1) with unit tests over env permutations
- [ ] Fallback returns `Width*Height*4` black frame; reason logged exactly once
- [ ] No crash on any env permutation (both vars set, neither, contradictory)

### Slice B: X11 Basic (XGetImage) + Error Traps

**Files:**
- `Platform/Interop/X11Interop.cs` (extend), scoped error-trap helper
- `Platform/FrameSourceBackends/X11BasicFrameSourceBackend.cs`

**CI (Xvfb):**
```bash
Xvfb :99 -screen 0 1920x1080x24 & export DISPLAY=:99
xsetroot -solid "#FF5500"
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-framesource
# Assert: frame contains the known solid color at BGRA offsets (B=0x00,G=0x55,R=0xFF),
#         alpha normalized to 0xFF, length == W*H*4
# Negative test: request a rect extending past the root — assert black frame returned,
#         process alive (error trap works)
```

**Acceptance:**
- [ ] Row repack honors `bytes_per_line`; alpha forced on depth-24
- [ ] Known-color pixel assertion passes (not just "non-zero data")
- [ ] BadMatch trapped → per-call black frame, no process death
- [ ] Dedicated display, serialized access, closed on dispose

### Slice C: X11 MIT-SHM Fast Path

**Files:**
- `Platform/Interop/X11ShmInterop.cs`
- `Platform/FrameSourceBackends/X11ShmFrameSourceBackend.cs`

**CI (Xvfb has MIT-SHM by default):**
```bash
Xvfb :99 -screen 0 1920x1080x24 & export DISPLAY=:99
xsetroot -solid "#FF5500"
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-framesource-shm
# Assert: log shows SHM path chosen; same pixel assertions as Slice B;
#         `ipcs -m` before/after shows no leaked segments (IPC_RMID marked)
```

**Acceptance:**
- [ ] Attach probe (XSync + trap) decides SHM vs basic; remote/denied → silent basic fallback
- [ ] Segment created 0600, reused across ≥100 captures, recreated on size change
- [ ] `IPC_RMID` marked post-attach; `ipcs -m` clean after process exit AND after kill -9
- [ ] Sustained 100 captures at 1080p without leak or slowdown

### Slice D: wlr-screencopy Backend

**Files:**
- `Platform/Interop/WaylandInterop.cs` (extend: registry, wl_shm, dispatch thread)
- `Platform/Interop/WlrScreencopyInterop.cs`
- `Platform/FrameSourceBackends/WlrScreencopyBackend.cs`

**CI (sway headless):**
```bash
export WLR_BACKENDS=headless WLR_LIBINPUT_NO_DEVICES=1 XDG_RUNTIME_DIR=/tmp/xdg
sway -c /dev/null & sleep 2; export WAYLAND_DISPLAY=wayland-1  # sway picks the name; read it from sway's log or swaymsg
swaymsg "output HEADLESS-1 bg #FF5500 solid_color"
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-framesource-wlr
# Assert: "wlr-screencopy" chosen; known-color pixel assertion; y_invert honored
```
(Headless sway capture is proven viable — `grim` works on headless outputs.)

**Acceptance:**
- [ ] Registry probe binds `zwlr_screencopy_manager_v1`
- [ ] Advertised format/stride respected; `y_invert` flag honored; `failed` → black frame
- [ ] Dedicated dispatch thread; `CaptureAsync` never blocks the Avalonia UI thread
- [ ] CI passes on ubuntu-latest with sway headless

### Slice E: ext-image-copy-capture Backend

**Files:**
- `Platform/Interop/ExtImageCopyCaptureInterop.cs`
- `Platform/FrameSourceBackends/ExtImageCopyCaptureBackend.cs`

**CI:** same sway-headless recipe as Slice D but requires sway ≥ 1.10 (wlroots 0.18).
`ubuntu-latest`'s sway may be older — if so, run this job in a container with a newer
sway (e.g. Alpine/Arch container) or mark the job `continue-on-error` with the probe
result asserted instead. Be honest in the workflow about which it is.

**Acceptance:**
- [ ] Probed BEFORE wlr-screencopy; falls back cleanly when absent
- [ ] Session-based capture delivers repeated frames (not one-shot)
- [ ] Same pixel assertions as Slice D where CI sway is new enough

### Slice F: Portal + PipeWire Backend

**Files:**
- `Platform/Interop/ScreenCastPortalClient.cs` (Tmds.DBus.Protocol — session-bus client,
  Request/Response signal pattern, version check, restore_token store via `ISecretStore`)
- `Platform/Interop/PipeWireInterop.cs` (`pw_context_connect_fd`, stream, SPA format nego)
- `Platform/FrameSourceBackends/PortalPipeWireBackend.cs`

**CI (portal mechanics ARE CI-testable — improvement over the draft's "manual only"):**
```bash
# sway headless + PipeWire + xdg-desktop-portal-wlr with the chooser disabled
apt-get install -y pipewire wireplumber xdg-desktop-portal xdg-desktop-portal-wlr
export XDG_RUNTIME_DIR=/tmp/xdg XDG_CURRENT_DESKTOP=sway
dbus-run-session -- bash -c '
  pipewire & wireplumber &
  sway -c /dev/null &            # WLR_BACKENDS=headless
  sleep 2
  printf "[screencast]\nchooser_type=none\noutput_name=HEADLESS-1\n" > "$XDG_CONFIG_HOME/xdg-desktop-portal-wlr/config"
  /usr/libexec/xdg-desktop-portal-wlr &   # path varies by distro
  /usr/libexec/xdg-desktop-portal &
  sleep 2
  dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-framesource-portal
'
# Assert: CreateSession→SelectSources→Start→OpenPipeWireRemote handshake completes,
#         PipeWire stream delivers ≥1 frame, BGRA repack correct
```
This exercises the full D-Bus + PipeWire path with **no dialog** (xdpw chooser disabled).
What CI **cannot** prove: GNOME/KDE dialog UX and persist/restore (xdpw has no persist) —
those remain the manual checklist below.

**Manual verification (real GNOME 42+ / KDE Plasma 6):**
- [ ] First enable → one dialog; token stored via ISecretStore
- [ ] Relaunch → silent restore, NO dialog
- [ ] Token rotates after every Start (verify stored value changes)
- [ ] Revoke in system settings → graceful degrade + re-auth notice, no prompt loop
- [ ] Cancel dialog → feature disabled, black frames, no re-prompt

**Acceptance:**
- [ ] Portal version checked; `persist_mode=2` + `restore_token` round-trip implemented
- [ ] PipeWire connected via `OpenPipeWireRemote` fd, not the default socket
- [ ] Prompt policy of §4.4 enforced (prompts only on explicit user action)
- [ ] CI portal job green; manual checklist executed once on GNOME or KDE and recorded

### Slice G: Multi-Monitor Correctness

**Files:** touched backends + `Platform/LinuxFrameSource.cs` (per-screen routing;
Wayland: map `ScreenInfo` → `wl_output` by position/name).

**CI (Xvfb recipe corrected):** the draft's `Xvfb -screen 0 … -screen 1 …` creates two
separate X *protocol screens* (`:99.0`/`:99.1`) with independent roots — that is NOT the
one-root-many-monitors model `ScreenInfo.Bounds` assumes. Correct recipe: one wide root +
fake XRandR monitors:
```bash
Xvfb :99 -screen 0 3840x1080x24 +extension RANDR & export DISPLAY=:99
xrandr --setmonitor LEFT  1920/508x1080/286+0+0    none
xrandr --setmonitor RIGHT 1920/508x1080/286+1920+0 none
# paint left and right halves different colors (xsetroot + a positioned solid window)
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-framesource-multimonitor
# Assert: capturing each ScreenInfo yields its own color, correct dimensions
```
Wayland: sway headless `create_output` gives a second headless output — assert per-output
capture routing.

**Acceptance:**
- [ ] X11: offsets into the shared root honored; out-of-bounds clamped
- [ ] Wayland: `ScreenInfo` ↔ `wl_output` mapping correct on 2 outputs
- [ ] Distinct per-monitor colors verified in CI on both session types

---

## 7. Risk / Unknowns

### 7.1 Claims to re-verify before the relevant slice lands (web verification was
unavailable in this revision pass)

| Claim | Confidence | Verify via |
|-------|------------|-----------|
| KWin implements ext-image-copy-capture-v1 | Low-medium | KWin release notes / `wayland-info` on Plasma 6.x; registry probe makes this non-fatal either way |
| Exact `persist_mode` enum values (0/1/2) and token-rotation semantics | High | xdg-desktop-portal ScreenCast docs (portal version 4); OBS's portal capture is the reference client |
| `xdg-desktop-portal-wlr` lacks persist/restore | High | xdpw README/issues |
| libX11 exports `XDestroyImage` as a symbol | High (Avalonia P/Invokes it) | `nm -D libX11.so.6` in CI as a one-line guard |
| Ubuntu-latest sway version ≥1.10 for Slice E | Unknown | check in CI; container fallback documented in the slice |

### 7.2 Genuine Unknowns (real-desktop only)

| Risk | Impact | Mitigation |
|------|--------|------------|
| Portal permission UX variance (GNOME vs KDE dialogs, revocation surfaces) | Confusing re-auth flows | Manual checklist in Slice F; non-modal re-auth notice |
| PipeWire format negotiation on NVIDIA/DMA-BUF-only configs | Stream gives no SHM-mappable frames | Negotiate SPA `MemFd`/`MemPtr` buffer types explicitly; degrade to black on failure |
| Fractional scaling (Wayland) — buffer size vs logical `ScreenInfo` size | Dimension mismatch vs contract | Return the *captured* pixel dimensions in `RawFrame`; consumers already use `RawFrame.Width/Height` |
| XWayland sessions where `DISPLAY` capture shows black | Silent wrong output if misrouted | Wayland-first selection (§2.1); never X11-fallback from a Wayland probe |
| Sustained-capture memory behavior (PipeWire buffer pools) | Slow leak in ambient use | Slice F soak: 10-minute capture loop, RSS assertion |
| Older PipeWire (0.2) on LTS distros | API incompatibility | Require libpipewire-0.3; probe `dlopen` and fall back to black frame with reason |

### 7.3 Compositor-Specific Bugs (track as found)

| Compositor | Bug | Workaround |
|------------|-----|------------|
| (none yet) | — | — |

---

## 8. CI Verification Matrix

| Slice | No display | X11 (Xvfb) | sway headless | sway+portal+PipeWire | Manual (GNOME/KDE) |
|-------|-----------|------------|---------------|----------------------|--------------------|
| A Fallback | Required | — | — | — | — |
| B X11 basic | — | Required (+ error-trap negative test) | — | — | — |
| C X11 SHM | — | Required (+ `ipcs` leak check) | — | — | — |
| D wlr-screencopy | — | — | Required | — | — |
| E ext-image-copy-capture | — | — | Required if sway ≥1.10, else container job | — | — |
| F Portal+PipeWire | — | — | — | Required (mechanics; chooser disabled) | Required once (dialog + persist/restore) |
| G Multi-monitor | — | Required (`xrandr --setmonitor`) | Required (`create_output`) | — | — |

CI job additions: extend the existing Xvfb job; add a sway-headless job; add the
sway+xdpw+PipeWire portal job (containerized if ubuntu-latest packages are too old).
Every backend that CI cannot fully prove has its exact residual manual checklist written
in its slice — no slice lands "compile-only" (readiness-map rule).

---

## 9. Summary

- **7 slices** (A-G), each committable with pixel-level assertions (known-color capture),
  not just "returns non-null".
- **6 backends** in a corrected priority chain: X11 SHM → X11 basic ‖
  ext-image-copy-capture → wlr-screencopy → portal(+restore_token) → black-frame fallback.
- **Prompt-free paths preferred everywhere they exist**; the portal dialog appears at
  most once per user grant, only on explicit user action, silently restored thereafter.
- **Privacy hard-line added:** raw screen frames are memory-only, never disk/network/log;
  the only persisted artifact is the opaque restore token in `ISecretStore`.
- **Critical path:** A → B → C immediately; D/E in parallel after A; F after D (shares the
  Wayland dispatch infrastructure); G last.

---

## Sources

- `CCP.Core/Platform/IFrameSource.cs`, `CCP.Core/Platform/IScreenInfo.cs:10`
- `CCP.Avalonia.Desktop.Windows/WindowsFrameSource.cs` (reference impl; corrected path)
- `CCP.Avalonia/ViewModels/Tabs/LabTabViewModel.cs:39,73` + webcam windows (consumers)
- `docs/linux-overlay-contract.md` (template + landed `Platform/` seam layout)
- `docs/linux-macos-readiness-map.md` (governing multi-backend principle)
- MIT-SHM spec — https://www.x.org/releases/current/doc/xextproto/shm.html
- Xlib error handling — XSetErrorHandler is process-global; default handler exits
- wlr-screencopy — https://wayland.app/protocols/wlr-screencopy-unstable-v1
- ext-image-copy-capture — https://wayland.app/protocols/ext-image-copy-capture-v1
- ScreenCast portal (persist_mode/restore_token, OpenPipeWireRemote) —
  https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.ScreenCast.html
- PipeWire — https://docs.pipewire.org/ (`pw_context_connect_fd`)

### 7.1-VERIFIED (web research, 2026-07-12 — driver)
| Claim | Result | Source |
|-------|--------|--------|
| Portal ScreenCast `persist_mode` enum + token semantics | **VERIFIED** — default 0; 0=none, 1=transient (while app runs), 2=persistent (token in permissions store until user revokes). Restore token is **single-use, rotates** on each Start response — persist per-use. Known caveat: monitor selection not always preserved across restore (xdp #1371). | flatpak.github.io/xdg-desktop-portal ScreenCast docs ; xdg-desktop-portal PR#638 |
| Avalonia v12 X11 handle access | **VERIFIED** — `TopLevel.TryGetPlatformHandle()` → `.Handle` + `.HandleDescriptor == "XID"` (Linux). | docs.avaloniaui.net native-interop |
| KWin implements `ext-image-copy-capture-v1` | **STILL UNVERIFIED** — registry probe makes it non-fatal; portal path is the KDE fallback either way. Re-probe at Slice E. | (deferred to CI registry probe) |
