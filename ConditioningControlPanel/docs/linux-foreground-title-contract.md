# Linux IForegroundWindowTitleProvider Implementation Contract

**Date:** 2026-07-12  
**Scope:** Hard-seam Linux foreground window title for awareness engine  
**Status:** Judgment-tier design document (read-only research artifact)  
**Closes:** Linux side of AI-1/AI-2/AI-10 (awareness feature bring-up)

This document specifies the behavior contract, backend architecture, and implementation
slices for Linux foreground window title detection (`IForegroundWindowTitleProvider`).
X11 and Wayland are CO-EQUAL first-class backends. Per owner directive (2026-07-12), the
implementation **must work on ANY Linux system** — multi-backend with graceful fallback.

**CRITICAL Wayland judgment:** Most Wayland compositors do NOT expose other windows to
clients for privacy reasons. This document captures the honest reality: full functionality
on X11 and wlroots, graceful degrade to "unknown" on locked-down GNOME.

---

## 1. IForegroundWindowTitleProvider Behavior Contract

The `IForegroundWindowTitleProvider` interface (`CCP.Core/Platform/IForegroundWindowTitleProvider.cs`)
defines the platform-agnostic foreground window title seam.

### 1.1 Interface Definition

```csharp
// CCP.Core/Platform/IForegroundWindowTitleProvider.cs (complete)
public interface IForegroundWindowTitleProvider
{
    /// <summary>
    /// Title of the current foreground window, or null/empty when unavailable.
    /// The returned string must never be persisted or logged by callers — it is
    /// memory-only input for activity classification.
    /// </summary>
    string? GetForegroundWindowTitle();
}
```

### 1.2 Behavioral Requirements (cited from WindowsForegroundWindowTitleProvider.cs)

| Requirement | Windows Implementation | Citation |
|-------------|------------------------|----------|
| **Title only** | Returns window title text, NOT process name or PID | `WindowsForegroundWindowTitleProvider.cs:10-12` |
| **UTF-8/Unicode** | Uses Unicode API (`GetWindowTextW` via `CharSet.Unicode`) | `WindowsForegroundWindowTitleProvider.cs:20` |
| **Buffer size** | 512-character buffer for title text | `WindowsForegroundWindowTitleProvider.cs:28` |
| **Null-safe** | Returns `null` when no foreground window exists | `WindowsForegroundWindowTitleProvider.cs:26-27` |
| **Synchronous** | Direct Win32 call, no async | implicit |

### 1.3 Privacy Contract (HARD CONSTRAINT)

From the interface doc and `AwarenessService.cs:19-23`:

> **Privacy (hard contract):** the raw foreground title lives in memory only for change
> detection. It is never written to disk, never sent over the network, and never logged.

This constraint applies to ALL platform implementations:
- **Memory-only:** Title string exists only in RAM, never serialized
- **No disk persistence:** Never written to settings, logs, or temp files
- **No network transmission:** The title itself never leaves the machine
- **Derived data only:** Only the derived `ActivityCategory` / `DetectedName` may be logged or sent

Any implementation violating these constraints is a **security bug**.

### 1.4 Consumer Integration (AwarenessService)

The `AwarenessService` (`CCP.Core/Services/Awareness/AwarenessService.cs`) consumes the provider:

```csharp
// AwarenessService.cs:31-32
private readonly IForegroundWindowTitleProvider? _titleProvider;

// AwarenessService.cs:101-106
// Platform gap guard: no foreground-title seam registered on this head (Linux/macOS).
if (_titleProvider is null)
{
    _logger?.LogInformation("WindowAwareness: no foreground window title provider on this platform - not starting");
    return;
}
```

**Graceful degrade:** When no provider is registered, the awareness engine **refuses to start**
(logs and returns from `Start()`). This is correct behavior — the feature is simply off,
not broken.

---

## 2. Linux Backend Architecture

### 2.1 Runtime Backend Selection

The Linux title provider uses a runtime backend selector. Unlike overlay/capture where
we need compositor cooperation for effects, title detection has a simpler but bleaker
fallback chain:

```
│                    LinuxTitleProviderBackendSelector              │
│  │ Detection order:                                              ││
│  │ 1. XDG_SESSION_TYPE == "x11" OR DISPLAY set                  ││
│  │    → X11TitleProvider (full functionality)                    ││
│  │ 2. XDG_SESSION_TYPE == "wayland" + WAYLAND_DISPLAY set       ││
│  │    → WaylandBackendProbe                                      ││
│  │ 3. Neither                                                    ││
│  │    → FallbackTitleProvider (returns null/"")                  ││

WaylandBackendProbe:
  ├─ ext-foreign-toplevel-list-v1 in registry? → WaylandForeignToplevelProvider
  ├─ wlr-foreign-toplevel-management in registry? → WlrForeignToplevelProvider
  ├─ GNOME Shell D-Bus active? → (CRITICAL: see section 4.4)
  └─ Otherwise → FallbackTitleProvider (returns null/"")
```

### 2.2 Backend Fallback Chain

| Priority | Backend | Capabilities | When Selected |
|----------|---------|--------------|---------------|
| 1 | `X11TitleProvider` | Full: `_NET_ACTIVE_WINDOW` + `_NET_WM_NAME`/`WM_NAME` | X11 session |
| 2 | `WlrForeignToplevelProvider` | Full: wlroots foreign-toplevel-management | Wayland + wlroots (sway, Hyprland) |
| 3 | `WaylandForeignToplevelProvider` | Full: ext-foreign-toplevel-list-v1 (future standard) | Wayland + ext-ftl (Plasma 6.1+, future compositors) |
| 4 | `GnomeShellDbusProvider` | Partial: Extension-dependent, unstable | GNOME with specific extension (see 4.4) |
| 5 | `FallbackTitleProvider` | None: returns `null`/`""` | Unknown session or all probes fail |

**Guarantee:** The fallback chain ensures no crash. On GNOME (without extension) and unknown
environments, the provider returns `null` or empty string; the awareness engine no-ops gracefully.

### 2.3 Seam Structure

```
CCP.Core/Platform/
├── IForegroundWindowTitleProvider.cs   # Unchanged interface
└── LinuxSessionType.cs                 # Shared: session detection enum

CCP.Avalonia.Desktop.Linux/Platform/
├── LinuxForegroundWindowTitleProvider.cs    # IForegroundWindowTitleProvider impl
├── LinuxTitleProviderBackendSelector.cs     # Runtime selection
├── TitleProviderBackends/
│   ├── ILinuxTitleProviderBackend.cs        # Backend abstraction
│   ├── X11TitleProvider.cs                  # X11 via _NET_ACTIVE_WINDOW
│   ├── WlrForeignToplevelProvider.cs        # wlr-foreign-toplevel-management
│   ├── WaylandForeignToplevelProvider.cs    # ext-foreign-toplevel-list-v1
│   ├── GnomeShellDbusProvider.cs            # GNOME Shell D-Bus (limited)
│   └── FallbackTitleProvider.cs             # Returns null/empty
└── Interop/
    └── X11Interop.cs                        # XLib P/Invoke (shared)
```

---

## 3. X11 Backend Design

### 3.1 Overview

X11 provides standardized window manager hints (EWMH/ICCCM) that expose the active window
and its title to any client. This is the most reliable path on Linux.

### 3.2 Get Active Window via _NET_ACTIVE_WINDOW

The active (focused) window is identified by the `_NET_ACTIVE_WINDOW` property on the
root window:

```csharp
// Step 1: Get atoms
var netActiveWindow = XInternAtom(display, "_NET_ACTIVE_WINDOW", false);
var actualTypeReturn = IntPtr.Zero;
int actualFormatReturn = 0;
ulong nItemsReturn = 0, bytesAfterReturn = 0;
IntPtr propReturn = IntPtr.Zero;

// Step 2: Read _NET_ACTIVE_WINDOW from root window
var root = XDefaultRootWindow(display);
var result = XGetWindowProperty(display, root, netActiveWindow,
    0, 1, false, XA_WINDOW,
    out actualTypeReturn, out actualFormatReturn,
    out nItemsReturn, out bytesAfterReturn, out propReturn);

if (result != Success || nItemsReturn == 0 || propReturn == IntPtr.Zero)
    return null; // No active window

// Step 3: Extract the Window ID
var activeWindow = Marshal.ReadIntPtr(propReturn);
XFree(propReturn);

if (activeWindow == IntPtr.Zero)
    return null; // No active window (desktop focused)
```

### 3.3 Get Window Title via _NET_WM_NAME / WM_NAME

Once we have the active window, read its title:

```csharp
// Preference order (EWMH → ICCCM):
// 1. _NET_WM_NAME (UTF-8, EWMH standard)
// 2. WM_NAME (legacy, may be Latin-1)

string? GetWindowTitle(IntPtr display, IntPtr window)
{
    // Try _NET_WM_NAME first (UTF-8)
    var netWmName = XInternAtom(display, "_NET_WM_NAME", false);
    var utf8String = XInternAtom(display, "UTF8_STRING", false);
    
    var result = XGetWindowProperty(display, window, netWmName,
        0, 1024, false, utf8String,
        out var type, out var format, out var nitems, out var after, out var data);
    
    if (result == Success && nitems > 0 && data != IntPtr.Zero)
    {
        var title = Marshal.PtrToStringUTF8(data);
        XFree(data);
        return title;
    }
    
    // Fall back to WM_NAME (ICCCM, may be XA_STRING/Latin-1)
    var wmName = XInternAtom(display, "WM_NAME", false);
    result = XGetWindowProperty(display, window, wmName,
        0, 1024, false, AnyPropertyType,
        out type, out format, out nitems, out after, out data);
    
    if (result == Success && nitems > 0 && data != IntPtr.Zero)
    {
        // Note: May be Latin-1; Marshal.PtrToStringAnsi handles ASCII subset
        var title = Marshal.PtrToStringAnsi(data);
        XFree(data);
        return title;
    }
    
    return null;
}
```

### 3.4 X11 P/Invoke Declarations

```csharp
[DllImport("libX11.so.6")]
static extern IntPtr XOpenDisplay(string? display_name);

[DllImport("libX11.so.6")]
static extern int XCloseDisplay(IntPtr display);

[DllImport("libX11.so.6")]
static extern IntPtr XDefaultRootWindow(IntPtr display);

[DllImport("libX11.so.6")]
static extern IntPtr XInternAtom(IntPtr display, string atom_name, bool only_if_exists);

[DllImport("libX11.so.6")]
static extern int XGetWindowProperty(
    IntPtr display, IntPtr window, IntPtr property,
    long long_offset, long long_length, bool delete,
    IntPtr req_type,
    out IntPtr actual_type_return, out int actual_format_return,
    out ulong nitems_return, out ulong bytes_after_return,
    out IntPtr prop_return);

[DllImport("libX11.so.6")]
static extern int XFree(IntPtr data);

// Constants
const int Success = 0;
const long XA_WINDOW = 33;
const long AnyPropertyType = 0;
```

### 3.5 Thread Safety

`XGetWindowProperty` is thread-safe within a single display connection, but each call
should be fast (no long blocking). Since `GetForegroundWindowTitle()` is called from
the awareness engine's background timer (every 1.5s), thread safety is not a concern
if we use a dedicated display connection.

**Recommendation:** Open a dedicated display connection in the backend constructor,
reuse it for all calls, and close it on dispose.

### 3.6 XWayland Compatibility

When running under XWayland, `_NET_ACTIVE_WINDOW` may NOT reflect the true Wayland
active window — it only tracks X11 windows in the XWayland instance. This means:
- X11 apps under XWayland: titles detected correctly
- Native Wayland apps: NOT detected (XWayland doesn't see them)

**Detection:** If `XDG_SESSION_TYPE == "wayland"` but `DISPLAY` is set, we're on XWayland.
The X11 backend can still be used but will only see XWayland windows.

---

## 4. Wayland Backend Design

### 4.1 The Wayland Window Enumeration Problem

**CRITICAL:** Wayland intentionally hides other windows from clients for privacy/security.
Unlike X11 where any client can enumerate all windows, standard Wayland provides NO
way to see other applications' windows.

**The honest reality:**
- **wlroots compositors (sway, Hyprland):** Implement `wlr-foreign-toplevel-management`
  which DOES expose window titles — full functionality available
- **KDE Plasma 6.1+:** Implements `ext-foreign-toplevel-list-v1` — full functionality
- **GNOME/Mutter:** Does NOT expose other windows via any standard protocol —
  **no title detection possible without a Shell extension**
- **Older KDE, Weston, others:** No standardized way — graceful fallback

### 4.2 wlr-foreign-toplevel-management (wlroots)

The `wlr-foreign-toplevel-management-unstable-v1` protocol provides window listing on
wlroots-based compositors:

```
┌──────────────────────────────────────────────────────────────────────────┐
│ wlr-foreign-toplevel-management Flow                                      │
│                                                                          │
│  1. Bind zwlr_foreign_toplevel_manager_v1 from wl_registry               │
│  2. Listen for toplevel events:                                          │
│     - toplevel(toplevel_handle) — new window appeared                    │
│     - toplevel.title(string) — window title changed                      │
│     - toplevel.state(array) — includes ACTIVATED state                   │
│     - toplevel.done() — batch of property changes complete               │
│     - toplevel.closed() — window closed                                  │
│  3. Track which toplevel has ACTIVATED state (= focused window)          │
│  4. Return that toplevel's title                                         │
└──────────────────────────────────────────────────────────────────────────┘
```

**Key events on `zwlr_foreign_toplevel_handle_v1`:**
- `title(string)` — the window title (what we need)
- `state(array<uint>)` — includes `ZWLR_FOREIGN_TOPLEVEL_HANDLE_V1_STATE_ACTIVATED`
- `done()` — signals end of property batch

**Implementation pattern:**
```csharp
class WlrForeignToplevelProvider : ILinuxTitleProviderBackend
{
    private readonly Dictionary<IntPtr, ToplevelInfo> _toplevels = new();
    private IntPtr _activeToplevel = IntPtr.Zero;
    
    public string? GetForegroundWindowTitle()
    {
        // Dispatch any pending Wayland events
        wl_display_dispatch_pending(_display);
        
        if (_activeToplevel == IntPtr.Zero)
            return null;
            
        return _toplevels.TryGetValue(_activeToplevel, out var info) ? info.Title : null;
    }
    
    // Event handlers populate _toplevels and track _activeToplevel
}
```

### 4.3 ext-foreign-toplevel-list-v1 (Future Standard)

The `ext-foreign-toplevel-list-v1` protocol is the emerging standard for cross-compositor
window listing. As of 2026:
- **KDE Plasma 6.1+**: Implements ext-foreign-toplevel-list
- **GNOME**: Does NOT implement it (security policy)
- **wlroots**: Plans to support alongside wlr-foreign-toplevel

The protocol is similar to wlr-foreign-toplevel:
```
ext_foreign_toplevel_list_v1 → ext_foreign_toplevel_handle_v1
  - title(string)
  - identifier(string)  
  - done()
  - closed()
```

**Note:** ext-foreign-toplevel does NOT include an "activated" state — tracking the
focused window may require compositor-specific D-Bus queries or best-effort heuristics.

### 4.4 GNOME Shell: The Honest Answer

**GNOME/Mutter does NOT expose other windows to clients.** This is an intentional
security/privacy decision. There is no standard Wayland protocol that GNOME implements
for window enumeration.

**Potential workarounds (each with serious limitations):**

| Approach | Feasibility | Limitations |
|----------|-------------|-------------|
| **GNOME Shell extension** | Works | Requires user to install an extension; CCP cannot ship one in-process |
| **D-Bus `org.gnome.Shell.Extensions`** | Works if extension installed | Extension must expose a D-Bus interface; fragile, breaks across GNOME versions |
| **AT-SPI accessibility APIs** | May work | Intended for accessibility; may not provide focused window reliably |
| **Mutter private D-Bus** | DO NOT USE | Completely undocumented, changes without notice, not for apps |

**Recommendation for GNOME:**
1. Detect GNOME session (`XDG_CURRENT_DESKTOP == "GNOME"` or `gnome-shell --version`)
2. Check if a compatible extension is installed (probe D-Bus for known interface)
3. If present, use extension's D-Bus interface
4. If absent, return `null`/`""` — awareness feature is unavailable on this desktop

**User-facing message:**
> "Window awareness is not available on GNOME. Install the 'Window Title Reporter'
> extension from extensions.gnome.org to enable this feature."

### 4.5 KDE/KWin Options

**KDE Plasma 6.1+:** Implements `ext-foreign-toplevel-list-v1` — use that protocol.

**Older KDE/KWin:** May support:
- `org.kde.KWin` D-Bus interface with `activeClient` property
- KWin scripting API (complex, requires script installation)

```csharp
// KWin D-Bus approach (Plasma 5.x)
var connection = new Connection(Address.Session);
var kwin = connection.CreateProxy<IKWin>("org.kde.KWin", "/KWin");
var activeClient = await kwin.GetActiveClientAsync();
// activeClient contains caption/title
```

**Recommendation:** Prefer `ext-foreign-toplevel-list-v1` on Plasma 6+; fall back to
D-Bus on Plasma 5.x; degrade gracefully on very old versions.

### 4.6 Wayland Compositor Compatibility Matrix

| Compositor | wlr-foreign-toplevel | ext-foreign-toplevel | D-Bus | Title Available |
|------------|---------------------|---------------------|-------|-----------------|
| sway | Yes (native) | No | N/A | **YES** |
| Hyprland | Yes (native) | No | N/A | **YES** |
| river | Yes (native) | No | N/A | **YES** |
| wayfire | Yes (native) | No | N/A | **YES** |
| KDE Plasma 6.1+ | No | Yes | Yes | **YES** |
| KDE Plasma 5.x | No | No | Yes | **YES** (D-Bus) |
| GNOME/Mutter | No | No | Extension-only | **NO** (fallback) |
| Weston | No | No | No | **NO** (fallback) |

---

## 5. Graceful Fallback Design

### 5.1 Fallback Title Provider

When no detection method is available, return null/empty:

```csharp
public sealed class FallbackTitleProvider : ILinuxTitleProviderBackend
{
    private readonly string _reason;
    private bool _logged;
    
    public FallbackTitleProvider(string reason) => _reason = reason;
    
    public string? GetForegroundWindowTitle()
    {
        if (!_logged)
        {
            // Log exactly once — don't spam logs every 1.5s
            Log.Information("Window title detection unavailable: {Reason}. " +
                "Awareness features will not function on this desktop environment.",
                _reason);
            _logged = true;
        }
        
        return null; // Awareness engine handles null gracefully
    }
}
```

### 5.2 Consumer Degrade Behavior (AwarenessService)

From `AwarenessService.cs:101-106`:

```csharp
if (_titleProvider is null)
{
    _logger?.LogInformation("WindowAwareness: no foreground window title provider on this platform - not starting");
    return; // Engine stays off, feature degrades gracefully
}
```

When `GetForegroundWindowTitle()` returns `null`/`""`:
- Activity classified as `Unknown`
- No awareness reactions triggered
- AI companion receives no activity context
- User experience: awareness features simply off, no crash or error

---

## 6. Implementation Slice Plan

Each slice is independently committable with its own verification gate.

### Slice A: Backend Selection + Fallback (Foundation)

**Goal:** Runtime backend detection and graceful fallback returning null.

**Files:**
- `CCP.Avalonia.Desktop.Linux/Platform/LinuxForegroundWindowTitleProvider.cs` — main impl
- `CCP.Avalonia.Desktop.Linux/Platform/LinuxTitleProviderBackendSelector.cs` — detection logic
- `CCP.Avalonia.Desktop.Linux/Platform/TitleProviderBackends/ILinuxTitleProviderBackend.cs` — interface
- `CCP.Avalonia.Desktop.Linux/Platform/TitleProviderBackends/FallbackTitleProvider.cs` — null return

**Verification (CI):**
```bash
# Headless (no display) — fallback path
unset DISPLAY WAYLAND_DISPLAY XDG_SESSION_TYPE
dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-titleprovider-fallback
# Assert: "Window title detection unavailable" logged, null returned, no crash
```

**Acceptance:**
- [ ] Session type correctly detected from environment
- [ ] Fallback returns null on unknown sessions
- [ ] Reason logged exactly once
- [ ] Awareness engine handles null gracefully (doesn't crash)

---

### Slice B: X11 Title Provider

**Goal:** Full title detection on X11 via `_NET_ACTIVE_WINDOW` + `_NET_WM_NAME`.

**Files:**
- `CCP.Avalonia.Desktop.Linux/Interop/X11Interop.cs` — XInternAtom, XGetWindowProperty (extend if needed)
- `CCP.Avalonia.Desktop.Linux/Platform/TitleProviderBackends/X11TitleProvider.cs` — X11 impl

**Verification (CI):**
```bash
# X11 via Xvfb + xdotool
Xvfb :99 -screen 0 1920x1080x24 &
export DISPLAY=:99
openbox &  # EWMH-compliant WM
sleep 1

# Create a window with a known title
xterm -T "Test Window Title 12345" &
XTERM_PID=$!
sleep 1

# Focus it
xdotool search --name "Test Window Title 12345" windowactivate
sleep 0.5

# Run title detection
TITLE=$(dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --get-foreground-title)
kill $XTERM_PID

# Assert
[[ "$TITLE" == *"Test Window Title 12345"* ]] || exit 1
echo "PASS: Title detected correctly"
```

**Acceptance:**
- [ ] X11 display opened successfully
- [ ] `_NET_ACTIVE_WINDOW` read from root window
- [ ] `_NET_WM_NAME` (UTF-8) read from active window
- [ ] Falls back to `WM_NAME` when `_NET_WM_NAME` absent
- [ ] Returns null when no active window
- [ ] CI passes on ubuntu-latest with Xvfb + openbox + xdotool

---

### Slice C: wlr-foreign-toplevel Provider (wlroots)

**Goal:** Full title detection on wlroots compositors (sway, Hyprland).

**Files:**
- `CCP.Avalonia.Desktop.Linux/Interop/WaylandInterop.cs` — base Wayland bindings (shared)
- `CCP.Avalonia.Desktop.Linux/Interop/WlrForeignToplevelInterop.cs` — protocol bindings
- `CCP.Avalonia.Desktop.Linux/Platform/TitleProviderBackends/WlrForeignToplevelProvider.cs` — impl

**Verification (CI):**
```bash
# sway in headless mode with a test window
WLR_BACKENDS=headless WLR_LIBINPUT_NO_DEVICES=1 sway &
export WAYLAND_DISPLAY=wayland-0
sleep 2

# Create a terminal with known title (foot is sway-native)
foot --title "Test Wayland Title 67890" &
FOOT_PID=$!
sleep 1

# Focus it via swaymsg
swaymsg '[title="Test Wayland Title 67890"] focus'
sleep 0.5

# Run title detection
TITLE=$(dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --get-foreground-title)
kill $FOOT_PID

# Assert
[[ "$TITLE" == *"Test Wayland Title 67890"* ]] || exit 1
echo "PASS: wlr-foreign-toplevel title detected"
```

**Acceptance:**
- [ ] `zwlr_foreign_toplevel_manager_v1` bound from registry
- [ ] Toplevel handles tracked with titles
- [ ] Activated state detected correctly
- [ ] Returns title of activated toplevel
- [ ] CI passes on ubuntu-latest with sway headless

---

### Slice D: ext-foreign-toplevel Provider (KDE Plasma 6+)

**Goal:** Title detection on KDE Plasma 6.1+ via `ext-foreign-toplevel-list-v1`.

**Files:**
- `CCP.Avalonia.Desktop.Linux/Interop/ExtForeignToplevelInterop.cs` — protocol bindings
- `CCP.Avalonia.Desktop.Linux/Platform/TitleProviderBackends/WaylandForeignToplevelProvider.cs` — impl

**Verification (CI):**
```bash
# KDE Plasma in CI is complex — KWin headless mode
# This requires manual verification on a real Plasma 6.1+ desktop
# CI verification is compile + mock-based unit test only

dotnet build CCP.Avalonia.Desktop.Linux
dotnet test CCP.Core.Tests --filter "TitleProvider"
# Assert: Code compiles, mock tests pass
```

**Manual verification (Plasma 6.1+):**
- [ ] ext-foreign-toplevel-list bound from registry
- [ ] Toplevel handles tracked with titles
- [ ] Active window detection works (may need heuristics)
- [ ] Returns correct title

**Acceptance:**
- [ ] Compiles without error
- [ ] Protocol bindings complete
- [ ] Graceful fallback when protocol unavailable

---

### Slice E: GNOME D-Bus Provider (Extension-Dependent)

**Goal:** Best-effort title detection on GNOME when a compatible extension is installed.

**Files:**
- `CCP.Avalonia.Desktop.Linux/Platform/TitleProviderBackends/GnomeShellDbusProvider.cs` — D-Bus client

**Verification (CI):**
```bash
# GNOME Shell in CI is not feasible without a full session
# This slice is compile + unit test only in CI

dotnet build CCP.Avalonia.Desktop.Linux
# Assert: Compiles, falls back gracefully when D-Bus unavailable
```

**Manual verification (GNOME):**
- [ ] Detects when extension is NOT installed (returns null, no crash)
- [ ] When extension installed, queries D-Bus correctly
- [ ] Returns title from extension's interface

**Acceptance:**
- [ ] Compiles without error
- [ ] Graceful fallback when GNOME extension absent
- [ ] Clear user-facing message about extension requirement

---

### Slice F: DI Registration + Awareness Integration

**Goal:** Wire the provider into the Linux head's DI and verify awareness engine integration.

**Files:**
- `CCP.Avalonia.Desktop.Linux/Program.cs` — register `IForegroundWindowTitleProvider`
- Unit tests for `AwarenessService` with Linux provider

**Verification (CI):**
```bash
# Full integration test on X11
Xvfb :99 -screen 0 1920x1080x24 &
export DISPLAY=:99
openbox &

dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-awareness-starts
# Assert: "WindowAwareness: Started monitoring" in logs (not "no foreground window title provider")
```

**Acceptance:**
- [ ] `IForegroundWindowTitleProvider` registered in Linux head DI
- [ ] `AwarenessService` starts successfully with provider
- [ ] Activity changes detected and classified
- [ ] AI-1/AI-2/AI-10 behavior functional on Linux X11

---

## 7. Risk/Unknowns Section

These items CANNOT be settled without running on a real Linux desktop or additional research:

### 7.1 Genuine Unknowns

| Risk | Impact | Mitigation |
|------|--------|------------|
| **GNOME extension ecosystem** | No reliable extension for window titles may exist | Document as GNOME limitation; accept graceful degrade |
| **GNOME 45+ API breakage** | Shell D-Bus interfaces change between versions | Test on GNOME 42, 44, 45, 46; version-guard code paths |
| **KDE ext-ftl focus tracking** | ext-foreign-toplevel has no "activated" state | Use KWin D-Bus for active window, or heuristic (most-recently-updated) |
| **wlr-ftm version differences** | Protocol versions vary across wlroots versions | Query protocol version, handle gracefully |
| **XWayland split-brain** | X11 provider only sees XWayland windows under Wayland | Detect XWayland, prefer native Wayland provider, document limitation |
| **Title encoding edge cases** | Non-UTF-8 titles in WM_NAME, emoji, RTL text | Handle encoding errors gracefully, return partial title |

### 7.2 Requires Real Desktop Testing

- GNOME Shell extension discovery and D-Bus invocation
- Plasma 6.1 ext-foreign-toplevel with various window managers
- Hyprland/sway title updates during rapid window switching
- Fullscreen games and their title behavior
- Multi-monitor focus tracking
- Title changes during tab switches (browser titles)

### 7.3 Desktop Environment-Specific Notes (Track as Found)

| Desktop | Notes | Workarounds |
|---------|-------|-------------|
| GNOME | No native support | Requires extension |
| KDE Plasma 5.x | D-Bus only, no ext-ftl | Use `org.kde.KWin` D-Bus |
| i3 (X11) | Works via X11 | — |
| Cinnamon | Works via X11 | — |
| MATE | Works via X11 | — |
| XFCE | Works via X11 | — |

---

## 8. CI Verification Matrix

| Slice | X11 (Xvfb) | Wayland (sway) | KDE/GNOME | Notes |
|-------|------------|----------------|-----------|-------|
| A (Fallback) | N/A | N/A | N/A | Headless no-display test |
| B (X11) | Required | N/A | N/A | xdotool sets known title |
| C (wlr-ftm) | N/A | Required | N/A | sway headless + foot |
| D (ext-ftl) | N/A | N/A | Manual | Plasma 6.1+ required |
| E (GNOME D-Bus) | N/A | N/A | Manual | Real GNOME session |
| F (Integration) | Required | Optional | N/A | Full DI integration |

**CI job additions needed:**
1. `ubuntu-latest` with Xvfb + openbox + xdotool (X11 tests) — **existing, extend**
2. `ubuntu-latest` with sway headless + foot (wlr-ftm test)
3. Manual verification documented for KDE/GNOME

---

## 9. Summary

This document defines:
- **6 implementation slices** (A through F), each independently committable
- **5 Linux backends** in a priority fallback chain
- **Per-backend CI verification** using headless compositors where possible
- **Honest GNOME coverage** — no native support, extension-dependent, graceful fallback

The X11 path provides full coverage for X11 sessions (including most traditional desktops).
The wlroots path provides full coverage on sway/Hyprland/river/wayfire.
The KDE path provides coverage on Plasma 6.1+ (ext-ftl) and older Plasma (D-Bus).
GNOME users without a Shell extension see awareness features disabled with a clear message.

**Total slice count:** 6

**Critical path:** Slices A → B (X11) can proceed immediately and provide the most coverage.
Slices C → D (Wayland) can proceed in parallel after A.
Slice E (GNOME) is best-effort and may remain minimal.
Slice F (integration) depends on at least one backend being complete.

**AI-1/AI-2/AI-10 closure:** When slices A + B + F land, the awareness feature is functional
on Linux X11 desktops. Additional Wayland backends expand coverage to more environments.

---

## Sources

- `CCP.Core/Platform/IForegroundWindowTitleProvider.cs` — interface definition
- `CCP.Avalonia.Desktop.Windows/Platform/WindowsForegroundWindowTitleProvider.cs` — Windows impl
- `CCP.Core/Services/Awareness/AwarenessService.cs:19-23,101-106` — privacy contract, null guard
- `linux-overlay-contract.md` — template for backend architecture
- `linux-macos-readiness-map.md` — governing multi-backend principle
- EWMH specification — https://specifications.freedesktop.org/wm-spec/wm-spec-latest.html
- wlr-foreign-toplevel-management — https://wayland.app/protocols/wlr-foreign-toplevel-management-unstable-v1
- ext-foreign-toplevel-list — https://wayland.app/protocols/ext-foreign-toplevel-list-v1
- KDE KWin D-Bus — https://api.kde.org/frameworks/kwin/html/
