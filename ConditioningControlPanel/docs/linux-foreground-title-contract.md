# Linux IForegroundWindowTitleProvider Implementation Contract

**Date:** 2026-07-12 (hardened judgment-tier revision — supersedes the same-day draft)  
**Scope:** Hard-seam Linux foreground window title for the awareness engine  
**Status:** Authoritative design contract (read-only research artifact; no code changes)  
**Closes:** Linux side of AI-1/AI-2/AI-10 (awareness feature bring-up, readiness map item #3)

This document specifies the behavior contract, backend architecture, and implementation
slices for Linux foreground window title detection (`IForegroundWindowTitleProvider`).
X11 and Wayland are CO-EQUAL first-class backends. Per owner directive (2026-07-12), the
implementation **must work on ANY Linux system** — multi-backend, runtime-selected,
graceful fallback, CI-verified per backend.

> **Revision note:** this version fixes a critical backend-selection ordering bug in the
> draft (X11-before-Wayland — which would misroute every Wayland desktop through XWayland
> and see only X11 windows), removes a fabricated KDE D-Bus API, corrects the KDE
> compatibility matrix (KWin implements wlr-foreign-toplevel-management — KDE is a
> full-function target, not a D-Bus special case), replaces a wrong Xlib thread-safety
> claim, adds the mandatory Xlib error trap (the default handler kills the process on a
> BadWindow race), and redesigns the Wayland event-pumping model (the draft's
> `dispatch_pending`-only sketch never reads the socket). Claims not re-verified against
> upstream docs in this pass are marked **[confidence: …]**.

**The honest Wayland reality (kept from the draft, sharpened):** wlroots compositors AND
KDE expose foreign-toplevel info → full functionality. GNOME exposes nothing to native
clients by policy → graceful "unknown" unless the user installs a Shell extension.

---

## 1. IForegroundWindowTitleProvider Behavior Contract

The interface lives at `CCP.Core/Platform/IForegroundWindowTitleProvider.cs` (namespace
`ConditioningControlPanel.Core.Platform` — note: *Platform*, not *Services/Awareness*).

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

The seam is **synchronous and cheap-per-call**: the awareness engine polls it every 1.5s
from a `System.Threading.Timer` threadpool callback
(`CCP.Core/Services/Awareness/AwarenessService.cs` — `PollInterval` 1.5s). This shapes
the whole Linux design: the call must be a fast in-memory or single-round-trip read, and
it must tolerate being invoked from *different* (and, if a poll ever runs long,
overlapping) threadpool threads.

### 1.2 Behavioral Requirements (cited from `CCP.Avalonia.Desktop.Windows/Platform/WindowsForegroundWindowTitleProvider.cs`)

| Requirement | Windows Implementation | Citation |
|-------------|------------------------|----------|
| **Title only** | Window title text — NO process name, NO PID | `WindowsForegroundWindowTitleProvider.cs:12,14` (doc + class), interface doc |
| **Unicode** | `GetWindowText` with `CharSet.Unicode` | `WindowsForegroundWindowTitleProvider.cs:20-21` |
| **Bounded read** | 512-char buffer — long titles truncate, never overrun | `WindowsForegroundWindowTitleProvider.cs:29-30` |
| **Null-safe** | `null` when no foreground window | `WindowsForegroundWindowTitleProvider.cs:26-27` |
| **Synchronous, no allocation-heavy work** | Direct Win32 call | whole method `:23-32` |

Linux backends adopt the same bounds: cap returned titles at **512 chars** (truncate,
don't fail) for parity and to bound the memory the privacy contract governs.

### 1.3 Privacy Contract (HARD CONSTRAINT — extended for Wayland)

From the interface doc and `AwarenessService.cs` header:

> the raw foreground title lives in memory only for change detection. It is never
> written to disk, never sent over the network, and never logged — log lines carry the
> derived detected name only. (The WPF debug line that logged the raw title was
> deliberately NOT ported.)

**Wayland extension (NEW — the draft missed this):** foreign-toplevel protocols do not
let you ask for "the active title" on demand — the compositor *pushes* title/state events
for **every** toplevel, so a Wayland backend necessarily holds an in-memory map of ALL
open window titles, not just the foreground one. The hard line therefore covers the
whole map:

- The toplevel map (titles, app_ids, handles) is memory-only. Never serialized, never
  logged (not even at trace level), never exposed beyond `GetForegroundWindowTitle()`.
- `app_id` may be *used* internally to disambiguate, but the seam returns the TITLE
  string only — no app_id, no PID, matching the Windows title-only contract.
- Handles/entries are dropped on `closed`; `Dispose()` clears the map.
- Fallback/selector logging carries backend names and reasons ONLY — never title content.

Violating any of this is a security bug of the same class as the webcam rule.

### 1.4 Consumer Integration (AwarenessService)

```csharp
// AwarenessService.Start() — platform gap guard
if (_titleProvider is null)
{
    _logger?.LogInformation("WindowAwareness: no foreground window title provider on this platform - not starting");
    return;
}
```

Two distinct degrade levels, both correct:
- **No provider registered** → engine refuses to start (feature off).
- **Provider registered but backend returns null/""** → engine runs, classifies
  `Unknown`, no reactions fire. This is what the Linux fallback backend produces.

Once the Linux provider exists, prefer the second mode: register the provider
unconditionally and let the backend chain degrade — the user sees a consistent
"awareness on, activity unknown" rather than a feature that silently refuses to start,
and a session-type change (e.g. login to X11 instead of Wayland) changes capability
without changing wiring.

---

## 2. Linux Backend Architecture

### 2.1 Runtime Backend Selection (ORDER CORRECTED — the draft's critical bug)

The draft selected X11 first ("`XDG_SESSION_TYPE == "x11"` OR `DISPLAY` set"). On
virtually every Wayland desktop `DISPLAY` is *also* set (XWayland), so that predicate
routes ALL Wayland sessions to the X11 backend — which sees only XWayland windows and
reports wrong/no titles for native Wayland apps. **Wayland must be probed first**,
consistent with the overlay and framesource contracts. Reuse
`CCP.Core/Platform/LinuxSessionType.cs` + `LinuxSessionDetector.cs` (already landed on
`feature/linux-overlay`).

```
LinuxTitleProviderBackendSelector (selection once, at provider init)
  1. Wayland session (WAYLAND_DISPLAY set, or XDG_SESSION_TYPE == "wayland")
       → WaylandBackendProbe
  2. X11 session (DISPLAY set)
       → X11TitleBackend (full functionality)
  3. Neither
       → FallbackTitleBackend (returns null)

WaylandBackendProbe (registry is ground truth):
  ├─ zwlr_foreign_toplevel_manager_v1 in registry?
  │     → WlrForeignToplevelBackend        (sway, Hyprland, river, wayfire, KDE KWin)
  ├─ ext_foreign_toplevel_list_v1 in registry AND a usable activation signal? (§4.3)
  │     → ExtForeignToplevelBackend        (title source only — see honest limits)
  ├─ GNOME session + known title extension answering on D-Bus?
  │     → GnomeExtensionDbusBackend        (best-effort, opt-in)
  └─ None
       → FallbackTitleBackend (returns null; awareness classifies Unknown)
```

**Deliberate non-fallback:** when a Wayland probe finds nothing, do NOT fall back to the
X11 (XWayland) backend as the primary. XWayland's root `_NET_ACTIVE_WINDOW` is maintained
by the compositor's XWM *for X11 windows only*; when a native Wayland window is focused
it points at none. That produces *misleading* data (stale X11 app reported as foreground)
— worse for awareness classification than an honest `null`. **[Judgment call: prefer
honest Unknown over plausible-but-wrong activity.]** An optional XWayland-assist mode may
be revisited later if real-desktop testing shows the staleness is detectable.

### 2.2 Backend Fallback Chain (matrix-corrected)

| Priority | Backend | Capabilities | When Selected |
|----------|---------|--------------|---------------|
| 1 | `X11TitleBackend` | Full: `_NET_ACTIVE_WINDOW` + `_NET_WM_NAME`/`WM_NAME` | X11 session |
| 2 | `WlrForeignToplevelBackend` | Full: titles + `activated` state | Wayland with `zwlr_foreign_toplevel_manager_v1` — wlroots family AND KDE KWin (KWin implements this protocol, ~Plasma 5.22+ **[confidence: medium-high — registry probe is authoritative]**) |
| 3 | `ExtForeignToplevelBackend` | Titles only — the protocol has **no activated state**; needs a pairing signal for focus (§4.3) | Wayland with `ext_foreign_toplevel_list_v1` and a usable focus signal |
| 4 | `GnomeExtensionDbusBackend` | Best-effort: depends on a user-installed Shell extension | GNOME with a known extension present |
| 5 | `FallbackTitleBackend` | Returns `null` | Everything else (stock GNOME, Weston, unknown) |

**Guarantee:** never crash; stock GNOME and unknown environments give `null` → awareness
runs with `Unknown` activity.

### 2.3 Seam Structure (aligned with the landed overlay-branch layout)

```
CCP.Core/Platform/
├── IForegroundWindowTitleProvider.cs        # UNCHANGED
├── LinuxSessionType.cs                      # EXISTS (overlay slices)
└── LinuxSessionDetector.cs                  # EXISTS (overlay slices)

CCP.Avalonia.Desktop.Linux/Platform/
├── LinuxForegroundWindowTitleProvider.cs    # seam impl delegating to selected backend
├── LinuxTitleProviderBackendSelector.cs
├── TitleProviderBackends/
│   ├── ILinuxTitleProviderBackend.cs        # + IDisposable
│   ├── X11TitleBackend.cs
│   ├── WlrForeignToplevelBackend.cs
│   ├── ExtForeignToplevelBackend.cs
│   ├── GnomeExtensionDbusBackend.cs
│   └── FallbackTitleBackend.cs
└── Interop/
    ├── X11Interop.cs                        # EXISTS (overlay) — extend, do not fork
    ├── WaylandInterop.cs                    # EXISTS (overlay) — extend (registry, dispatch thread)
    ├── WlrForeignToplevelInterop.cs         # NEW
    └── ExtForeignToplevelInterop.cs         # NEW
```

The Wayland dispatch-thread infrastructure is SHARED with the framesource backends
(`linux-framesource-contract.md` §4.1) — one connection-owning event thread per process,
not one per seam.

---

## 3. X11 Backend Design

### 3.1 Connection, Threading, and Error Traps (corrected)

The draft claimed "`XGetWindowProperty` is thread-safe within a single display
connection" — **wrong**. Xlib is not thread-safe without `XInitThreads()` (which must be
the first Xlib call in the process — un-guaranteeable next to Avalonia's own X11 use),
and `System.Threading.Timer` callbacks run on *different* threadpool threads and can
overlap if a poll stalls. Design:

- One dedicated `XOpenDisplay(null)` owned by the backend; **all access serialized by a
  lock**. A single connection touched by one thread at a time needs no Xlib-internal
  locking.
- **Mandatory scoped error trap** (shared helper with the framesource backend, see
  `linux-framesource-contract.md` §3.2): the active window can be destroyed between
  reading `_NET_ACTIVE_WINDOW` and reading its `_NET_WM_NAME` — that's a `BadWindow`,
  and the **default Xlib error handler terminates the process**. For a 1.5s ambient
  poll this race WILL eventually fire. Trap → return `null` for that poll. The handler
  is process-global: install once, chain to the previous handler for foreign displays.
- Poll cost: two `XGetWindowProperty` round trips per 1.5s — negligible. A
  `PropertyNotify`-based push model is possible but unnecessary for this cadence; the
  synchronous read matches the seam. (Draft's recommendation kept.)

### 3.2 Active Window via `_NET_ACTIVE_WINDOW` (EWMH)

```csharp
var netActiveWindow = XInternAtom(display, "_NET_ACTIVE_WINDOW", only_if_exists: true);
if (netActiveWindow == IntPtr.Zero) return null;   // WM does not implement EWMH

var root = XDefaultRootWindow(display);
// long_length is in 32-BIT UNITS (we want 1 item). XA_WINDOW = 33.
var status = XGetWindowProperty(display, root, netActiveWindow,
    0, 1, false, XA_WINDOW,
    out var type, out var format, out var nItems, out var after, out var prop);
if (status != Success || nItems == 0 || prop == IntPtr.Zero) return null;
var activeWindow = Marshal.ReadIntPtr(prop);        // format 32 → item marshaled as long
XFree(prop);
if (activeWindow == IntPtr.Zero) return null;        // desktop focused / none
```

Requirements the draft implied but must be explicit:
- `_NET_ACTIVE_WINDOW` exists only under an **EWMH-compliant window manager**. Bare X
  (Xvfb with no WM) has no such property → `null` → Unknown. Correct behavior; the CI
  recipe therefore runs a WM (openbox).
- Intern atoms **once** at backend init, not per poll.
- On 64-bit, a `format=32` property item occupies 8 bytes in the returned buffer (Xlib
  long-ification) — read as `IntPtr`/long, not int32.

### 3.3 Title via `_NET_WM_NAME` (UTF8_STRING), fallback `WM_NAME`

Preference order per EWMH: `_NET_WM_NAME` (type `UTF8_STRING`) first; legacy `WM_NAME`
(ICCCM `TEXT` — may be `STRING`/Latin-1 or `COMPOUND_TEXT`) second.

```csharp
string? GetWindowTitle(IntPtr display, IntPtr window)   // called inside the error trap
{
    // _NET_WM_NAME, request up to 512 chars: long_length is in 32-bit units → 128 units
    var status = XGetWindowProperty(display, window, _atomNetWmName,
        0, 128, false, _atomUtf8String,
        out var type, out var format, out var nItems, out var after, out var data);
    if (status == Success && data != IntPtr.Zero)
    {
        try { if (nItems > 0 && type == _atomUtf8String) return Marshal.PtrToStringUTF8(data); }
        finally { XFree(data); }
    }

    // WM_NAME fallback (AnyPropertyType). On Unix .NET, PtrToStringAnsi decodes UTF-8;
    // genuine Latin-1/COMPOUND_TEXT titles may mojibake — acceptable degrade (memory-only
    // classification input, no persistence). Modern toolkits all set _NET_WM_NAME.
    status = XGetWindowProperty(display, window, _atomWmName,
        0, 128, false, AnyPropertyType,
        out type, out format, out nItems, out after, out data);
    if (status == Success && data != IntPtr.Zero)
    {
        try { if (nItems > 0) return Marshal.PtrToStringAnsi(data); }
        finally { XFree(data); }
    }
    return null;
}
```

Corrections vs draft: `long_length` is counted in **32-bit multiples** (the draft's 1024
requested 4KB — harmless but wrong for a 512-char cap); `XFree` in `finally`; verify the
returned `type` for the UTF-8 path; truncate to 512 chars post-decode.

### 3.4 P/Invoke Additions

Extend the overlay branch's `Interop/X11Interop.cs` (it already carries
`XInternAtom`-class helpers for the overlay work — verify before adding duplicates):

```csharp
[DllImport("libX11.so.6")] static extern int XGetWindowProperty(
    IntPtr display, IntPtr window, IntPtr property,
    long long_offset, long long_length, bool delete, IntPtr req_type,
    out IntPtr actual_type, out int actual_format,
    out ulong nitems, out ulong bytes_after, out IntPtr prop);
[DllImport("libX11.so.6")] static extern int XFree(IntPtr data);
// XOpenDisplay/XCloseDisplay/XDefaultRootWindow/XInternAtom/XSetErrorHandler: shared.
const int Success = 0;
static readonly IntPtr XA_WINDOW = (IntPtr)33;
static readonly IntPtr AnyPropertyType = IntPtr.Zero;
```

### 3.5 XWayland (kept, sharpened)

Under XWayland the X11 backend sees only X11 windows; `_NET_ACTIVE_WINDOW` is maintained
by the compositor's XWM and does not reflect native-Wayland focus. This is why the
selector never routes Wayland sessions here (§2.1) and why the Wayland probe's terminal
fallback is `null`, not X11.

---

## 4. Wayland Backend Design

### 4.1 Connection and Event-Pump Model (redesigned — draft was broken)

The draft's `GetForegroundWindowTitle()` called `wl_display_dispatch_pending()` then read
state. `dispatch_pending` only dispatches **already-read** events; nothing ever reads the
socket → the state never updates. Correct design, which also fits the synchronous seam
perfectly:

- A **dedicated dispatch thread** owns the `wl_display` connection and blocks in
  `wl_display_dispatch()` (shared infrastructure with the framesource Wayland backends).
- Event handlers maintain the toplevel map and an `_activeTitle` snapshot **under a
  lock** (or as an immutable string swapped atomically).
- `GetForegroundWindowTitle()` performs a **lock-protected snapshot read only** — no
  Wayland calls on the polling thread at all. O(1), thread-safe, and always as fresh as
  the compositor's last event.
- `Dispose()`: wake the thread (`wl_display` fd + pipe in `poll`, or
  `wl_display_disconnect` semantics), join, clear the map (§1.3).

### 4.2 wlr-foreign-toplevel-management (wlroots family AND KDE)

`zwlr_foreign_toplevel_management_unstable_v1` — the primary Wayland backend:

```
1. Bind zwlr_foreign_toplevel_manager_v1 from the registry
2. manager.toplevel(handle) → track new toplevel
3. handle events: title(string) | app_id(string) | state(wl_array of uint32,
   contains ACTIVATED=2) | done() | closed()
4. Apply title/state changes ONLY on done() (protocol contract: events between
   done()s are a batch — the draft applied them immediately)
5. Maintain: map<handle, (title, activated)>; _activeTitle = title of the handle
   whose last done()-committed state contains ACTIVATED
6. closed() → remove from map; if it was active, _activeTitle = null
7. manager.finished() → compositor is withdrawing the protocol: clear map, report
   null from then on (degrade, don't crash)
```

Corrections/additions vs draft: batch-commit on `done()`; handle `manager.finished`;
multiple outputs can in principle yield transient multi-activated states — last
`done()`-committed ACTIVATED wins; cap stored titles at 512 chars.

**KDE matrix correction (draft error):** the draft listed KDE as "wlr-foreign-toplevel:
No" and invented an `org.kde.KWin`/`activeClient` D-Bus API (KWin's real D-Bus surface
has `queryWindowInfo` — *interactive*, requires a user click — and the scripting API;
neither is a poll API — the draft's code sketch does not correspond to a real interface
and is removed). In reality **KWin implements `zwlr_foreign_toplevel_manager_v1`**
(added ~Plasma 5.22 **[confidence: medium-high; verify with `wayland-info` on Plasma
before relying on the version number — the runtime registry probe makes the exact
version moot]**). So Plasma 5.22+ takes backend #2 with full titles + activated state,
and no KDE-specific code path exists at all. KWin also offers its own
`org_kde_plasma_window_management` protocol (taskbar protocol: titles + active state) as
a spare if wlr-ftm is ever dropped **[confidence: medium]**.

### 4.3 ext-foreign-toplevel-list-v1 (honest demotion)

`ext_foreign_toplevel_list_v1` (wayland-protocols staging) provides `title`, `app_id`,
`identifier`, `done`, `closed` — **and no activated/focused state** (the draft knew this
but still labeled the backend "full functionality"; it is not). Titles without focus
cannot answer "what is the FOREGROUND window".

Status: implemented by wlroots 0.18+ and KDE Plasma 6.x **[confidence: medium]** — but
both of those *also* expose better options (wlr-ftm), so ext-ftl's practical value today
is only for future compositors that adopt the ext- protocol family exclusively. A
companion state protocol (ext-foreign-toplevel-state) has been proposed but is not
standardized **[confidence: medium — re-check wayland-protocols when this slice is
picked up]**.

**Ruling:** implement `ExtForeignToplevelBackend` LAST (slice ordering §6), and only
activate it when a usable focus signal exists (companion protocol, or a
compositor-specific activation hint). Until then a registry hit on ext-ftl *alone* still
selects `FallbackTitleBackend` — honest `null` beats guessing focus by
most-recently-changed-title heuristics, which misfire on background tab-title updates
(media players, chat unread counters). **[Judgment: heuristics rejected — awareness
drives user-visible AI reactions; wrong-activity reactions are worse than none.]**

### 4.4 GNOME: The Honest Answer (kept, sharpened)

GNOME/Mutter exposes neither foreign-toplevel protocol, by policy. Native options:

| Approach | Reality |
|----------|---------|
| `org.gnome.Shell.Introspect` D-Bus | Exists but **restricted** since GNOME 41 — allowlisted callers (portal implementations) or unsafe-mode only. NOT available to us. **[confidence: high]** |
| Shell extension exposing a D-Bus interface | Works. Real extensions exist (e.g. "Window Calls", "Focused Window D-Bus") but each has its own interface and GNOME-version compatibility churn. User must install one. |
| AT-SPI accessibility bus | Genuine best-effort possibility (focus events + accessible names, toolkit-dependent coverage). Investigation-only; NOT a slice. |
| Mutter private D-Bus | DO NOT USE (undocumented, version-coupled). |

**Ruling:** stock GNOME = `FallbackTitleBackend` → awareness runs with `Unknown`.
`GnomeExtensionDbusBackend` is a best-effort adapter that probes a SHORT allowlist of
known extension D-Bus names at init (title-only reads, same 512 cap, same privacy rules)
and is clearly marked in settings/docs:

> "Window awareness has limited support on GNOME. Install a compatible Shell extension
> (e.g. 'Window Calls') to enable it."

Do not ship or auto-install an extension; do not scrape; accept the degrade.

### 4.5 Compositor Compatibility Matrix (corrected)

| Compositor | wlr-ftm | ext-ftl | Foreground title available |
|------------|---------|---------|----------------------------|
| sway | Yes | ≥1.10 | **YES** (wlr-ftm) |
| Hyprland | Yes | version-dependent | **YES** (wlr-ftm) |
| river / wayfire | Yes | wlroots-version-dependent | **YES** (wlr-ftm) |
| KDE Plasma 5.22+ / 6.x | **Yes** (draft said No) | 6.x **[unverified]** | **YES** (wlr-ftm) |
| KDE Plasma < 5.22 | No | No | NO → fallback (the draft's D-Bus path was not real) |
| GNOME / Mutter | No | No | NO → fallback; YES with user-installed extension |
| Weston | No | No | NO → fallback |

Registry probe at runtime is authoritative; the matrix is documentation, not logic.

---

## 5. Graceful Fallback Design

### 5.1 FallbackTitleBackend

```csharp
public sealed class FallbackTitleBackend : ILinuxTitleProviderBackend
{
    private readonly string _reason;
    private int _logged;

    public FallbackTitleBackend(string reason) => _reason = reason;

    public string? GetForegroundWindowTitle()
    {
        if (Interlocked.Exchange(ref _logged, 1) == 0)
            _logger.LogInformation("Foreground title detection unavailable: {Reason}. " +
                "Awareness will classify activity as Unknown on this desktop.", _reason);
        return null;
    }
}
```

Reason strings name the backend/probe outcome only — never title content (§1.3). Also
the demotion target if a live backend's connection dies (compositor restart): swap to
fallback, keep the app alive.

### 5.2 Consumer Degrade

With the provider registered and returning `null`: activity `Unknown`, no reactions, no
AI activity context, no crash. With the provider unregistered the engine refuses to
start (`AwarenessService.Start()` guard) — after this contract lands, the Linux head
always registers the provider and degrades via the backend chain (§1.4).

---

## 6. Implementation Slice Plan

All files under `CCP.Avalonia.Desktop.Linux/Platform/…` (§2.3). Standard repo gates
(slnf 0 errors, Core tests, smoke) apply to every slice in addition to the listed CI.

### Slice A: Selector + Fallback + DI (Foundation — DI moved up from the draft's Slice F)

Registering the provider early means every later backend lands as a pure capability
upgrade with wiring already proven.

**Files:**
- `Platform/LinuxForegroundWindowTitleProvider.cs`
- `Platform/LinuxTitleProviderBackendSelector.cs`
- `Platform/TitleProviderBackends/ILinuxTitleProviderBackend.cs`
- `Platform/TitleProviderBackends/FallbackTitleBackend.cs`
- `Program.cs` — `services.AddSingleton<IForegroundWindowTitleProvider, LinuxForegroundWindowTitleProvider>()`

**CI (headless, no display):**
```bash
env -u DISPLAY -u WAYLAND_DISPLAY -u XDG_SESSION_TYPE \
  dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --verify-titleprovider-fallback
# Assert: "unavailable" logged once; null returned; AwarenessService.Start() proceeds
# past the null-provider guard (provider IS registered) and runs with Unknown activity
```

**Acceptance:**
- [ ] Wayland-before-X11 selection order with unit tests over env permutations (incl.
      the XWayland trap: both `WAYLAND_DISPLAY` and `DISPLAY` set → Wayland probe)
- [ ] Null returned; reason logged exactly once; never any title content in logs
- [ ] Awareness engine starts and classifies Unknown without crashing

### Slice B: X11 Backend

**Files:**
- `Platform/Interop/X11Interop.cs` (extend: `XGetWindowProperty`, `XFree`; shared error trap)
- `Platform/TitleProviderBackends/X11TitleBackend.cs`

**CI (Xvfb + EWMH WM):**
```bash
Xvfb :99 -screen 0 1920x1080x24 & export DISPLAY=:99
openbox & sleep 1                                  # EWMH WM — REQUIRED for _NET_ACTIVE_WINDOW
xterm -T "Test Window Title 12345" & sleep 1
xdotool search --name "Test Window Title 12345" windowactivate; sleep 0.5
TITLE=$(dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --get-foreground-title)
[[ "$TITLE" == *"Test Window Title 12345"* ]] || exit 1
# Negative tests:
#  - destroy the focused window, poll again → null, process alive (BadWindow trapped)
#  - kill openbox, poll → null (no EWMH), process alive
```

**Acceptance:**
- [ ] `_NET_ACTIVE_WINDOW` → `_NET_WM_NAME` (UTF8_STRING, type-checked) → `WM_NAME` fallback
- [ ] Atoms interned once; `long_length` in 32-bit units; 512-char truncation
- [ ] BadWindow race trapped → null, no process death (explicit CI negative test)
- [ ] Dedicated display + lock; disposed cleanly
- [ ] UTF-8 title with emoji/CJK survives round trip (xdotool can set such a title)

### Slice C: wlr-foreign-toplevel Backend (wlroots + KDE)

**Files:**
- `Platform/Interop/WaylandInterop.cs` (extend: shared dispatch thread if the framesource
  work hasn't landed it yet — whichever slice lands first builds it)
- `Platform/Interop/WlrForeignToplevelInterop.cs`
- `Platform/TitleProviderBackends/WlrForeignToplevelBackend.cs`

**CI (sway headless):**
```bash
export WLR_BACKENDS=headless WLR_LIBINPUT_NO_DEVICES=1 XDG_RUNTIME_DIR=/tmp/xdg
sway -c /dev/null & sleep 2; export WAYLAND_DISPLAY=$(ls $XDG_RUNTIME_DIR | grep '^wayland-[0-9]$')
foot --title "Test Wayland Title 67890" & sleep 1        # foot: native Wayland terminal
swaymsg '[title="Test Wayland Title 67890"] focus'; sleep 0.5
TITLE=$(dotnet run --project CCP.Avalonia.Desktop.Linux -- --smoke-test --get-foreground-title)
[[ "$TITLE" == *"Test Wayland Title 67890"* ]] || exit 1
# Also: open a second foot, switch focus via swaymsg, assert the snapshot follows;
# close the focused window, assert null-or-new-focus (no stale title); kill sway,
# assert provider degrades to null without crashing the app
```

**Acceptance:**
- [ ] Batch-commit on `done()`; ACTIVATED tracking; `closed()` clears active
- [ ] `manager.finished` → degrade to null (no crash)
- [ ] Snapshot read is O(1), no Wayland calls on the polling thread
- [ ] Focus-follow and window-close CI assertions pass on sway headless
- [ ] Map cleared on dispose; no title strings in any log output (grep the CI log for
      the test title as a NEGATIVE assertion — it must appear only in the harness's own
      stdout, never in app logs)

### Slice D: GNOME Extension D-Bus Backend (best-effort, optional)

**Files:**
- `Platform/TitleProviderBackends/GnomeExtensionDbusBackend.cs` (Tmds.DBus.Protocol;
  probes a short allowlist of known extension bus names, e.g. Window Calls)

**CI:** compile + unit tests with a mocked D-Bus (no GNOME session in CI). Manual
checklist on a real GNOME box: extension absent → clean fallback; extension present →
correct title; extension uninstalled mid-session → degrade to null.

**Acceptance:**
- [ ] No probe traffic unless the session is GNOME
- [ ] Absent/broken extension → FallbackTitleBackend, single log line
- [ ] Settings/user-docs carry the extension guidance text (§4.4)

### Slice E: ext-foreign-toplevel Backend (deferred until a focus signal exists)

**Files:**
- `Platform/Interop/ExtForeignToplevelInterop.cs`
- `Platform/TitleProviderBackends/ExtForeignToplevelBackend.cs`

**Gate to even start this slice:** a standardized activation/state companion to
ext-foreign-toplevel-list exists (check wayland-protocols), OR a target compositor ships
ext-ftl *without* wlr-ftm plus a usable focus signal. Until then this slice stays parked
— documented here so nobody "helpfully" implements it with a focus heuristic (§4.3).

**Acceptance (when unparked):**
- [ ] Focus signal is protocol-based, not heuristic
- [ ] Same CI shape as Slice C on a compositor shipping the protocol pair

---

## 7. Risk / Unknowns

### 7.1 Claims to re-verify before the relevant slice lands (web verification was
unavailable in this revision pass)

| Claim | Confidence | Verify via |
|-------|------------|-----------|
| KWin implements zwlr_foreign_toplevel_manager_v1 (~Plasma 5.22+) | Medium-high | `wayland-info` on a Plasma box; KWin release notes. Non-fatal either way: registry probe decides |
| org.gnome.Shell.Introspect restricted since GNOME 41 | High | GNOME Shell release notes / issue tracker |
| "Window Calls" / "Focused Window D-Bus" extension interfaces + GNOME-version coverage | Medium | extensions.gnome.org + their repos, at Slice D time |
| ext-foreign-toplevel state/activation companion protocol status | Medium | wayland-protocols repo, at Slice E gate check |
| wlroots ships ext-ftl since 0.18 | Medium-high | wlroots changelog |

### 7.2 Genuine Unknowns (real-desktop only)

| Risk | Impact | Mitigation |
|------|--------|------------|
| Rapid window switching / title churn (browser tabs) vs 1.5s poll + event snapshot | Awareness lag or flapping | Engine already debounces via change detection; verify on real desktops |
| Compositor restart (sway reload, KWin crash-restart) kills the wl_display | Backend goes dead silently | Connection-death detection → demote to fallback + optional re-probe timer |
| Fullscreen games (especially XWayland games on Wayland) | Title missing or generic | Accept; classification falls back to Unknown/Gaming heuristics at the classifier level |
| Non-UTF-8 `WM_NAME` titles (legacy X11 apps) | Mojibake in classification input | Accepted degrade (§3.3); modern apps set `_NET_WM_NAME` |
| Multiple seats/outputs with independent focus | Ambiguous "the" foreground window | Last `done()`-committed ACTIVATED wins; revisit only if real reports arrive |
| GNOME extension interface churn across GNOME versions | Slice D breakage | Allowlist + version probe; treat every failure as fallback, never crash |

### 7.3 Desktop-Environment Notes

| Desktop | Path | Notes |
|---------|------|-------|
| i3 / XFCE / MATE / Cinnamon (X11) | X11 backend | Full function |
| KDE Plasma (Wayland, ≥5.22) | wlr-ftm backend | Full function (matrix corrected) |
| KDE Plasma (X11 mode) | X11 backend | Full function |
| GNOME (Wayland) | fallback / extension | Unknown activity unless extension installed |
| GNOME (Xorg mode) | X11 backend | Full function — worth noting in user docs as the zero-extension GNOME option |

---

## 8. CI Verification Matrix

| Slice | No display | X11 (Xvfb+openbox+xdotool) | sway headless (+foot) | Mocked D-Bus | Manual |
|-------|-----------|----------------------------|------------------------|--------------|--------|
| A Selector+Fallback+DI | Required | — | — | — | — |
| B X11 | — | Required (+ BadWindow & no-WM negative tests) | — | — | — |
| C wlr-ftm | — | — | Required (+ focus-follow, close, compositor-kill, log-privacy negative) | — | — |
| D GNOME extension | — | — | — | Required | Once, real GNOME |
| E ext-ftl (parked) | — | — | (future) | — | — |

CI job additions: extend the existing Xvfb job with openbox+xdotool+xterm; reuse the
sway-headless job from the framesource contract (same compositor instance can host both
suites). KDE/GNOME cannot be CI-proven — their residual checks are the explicit manual
checklists in slices C (KDE = same wlr-ftm code path, verify once on Plasma) and D.

---

## 9. Summary

- **5 slices**; DI + fallback land FIRST so every backend is a pure capability upgrade.
- **Backend chain (corrected):** X11 EWMH ‖ wlr-foreign-toplevel (wlroots AND KDE — the
  draft's fabricated KDE D-Bus path is removed) → GNOME extension best-effort →
  honest `null`.
- **Selection order fixed:** Wayland before X11 — the draft's DISPLAY-first check would
  have broken every Wayland desktop via XWayland.
- **No focus heuristics:** ext-ftl (no activated state) stays parked until a real focus
  signal exists; wrong-activity AI reactions are worse than none.
- **Privacy hardened:** the Wayland toplevel-title map (all windows, pushed by the
  compositor) is inside the memory-only/no-log hard line; CI includes a negative
  log-content assertion.
- **AI-1/AI-2/AI-10 closure:** slices A + B make awareness functional on every X11
  desktop; slice C adds sway/Hyprland/river/wayfire AND KDE Plasma Wayland. Stock GNOME
  degrades to Unknown, honestly, with a documented extension path.

---

## Sources

- `CCP.Core/Platform/IForegroundWindowTitleProvider.cs` — interface + privacy doc
- `CCP.Avalonia.Desktop.Windows/Platform/WindowsForegroundWindowTitleProvider.cs` — reference impl
- `CCP.Core/Services/Awareness/AwarenessService.cs` — poll cadence, privacy header, null guard
- `docs/linux-overlay-contract.md` — template + landed `Platform/` seam layout
- `docs/linux-framesource-contract.md` §3.2/§4.1 — shared error-trap + Wayland dispatch thread
- `docs/linux-macos-readiness-map.md` — governing multi-backend principle
- EWMH — https://specifications.freedesktop.org/wm-spec/wm-spec-latest.html
  (`_NET_ACTIVE_WINDOW`, `_NET_WM_NAME`/UTF8_STRING)
- wlr-foreign-toplevel-management — https://wayland.app/protocols/wlr-foreign-toplevel-management-unstable-v1
- ext-foreign-toplevel-list — https://wayland.app/protocols/ext-foreign-toplevel-list-v1

### 7.1-VERIFIED (web research, 2026-07-12 — driver)
| Claim | Result | Source |
|-------|--------|--------|
| KWin implements `zwlr_foreign_toplevel_manager_v1` | **VERIFIED** — KWin exposes it at **v3** (wayland-info dumps). KDE (Wayland) gets FULL awareness via wlr-ftm; the draft's `org.kde.KWin` D-Bus API was correctly removed. | wayland.app/protocols/wlr-foreign-toplevel-management-unstable-v1 ; Arch BBS wayland-info dump (id=283804) |
| `org.gnome.Shell.Introspect` restricted since GNOME 41 | **VERIFIED** — `GetWindows` → `AccessDenied` unless unsafe-mode/allowlisted (confirmed GNOME 42.9). GNOME (Wayland) = honest fallback/extension-only. | discourse.gnome.org/t/…/21201 ; gnome-shell MR!1970 |
| Avalonia v12 X11 handle access | **VERIFIED** — `TopLevel.GetTopLevel(x)?.TryGetPlatformHandle()` → `.Handle` (IntPtr) + `.HandleDescriptor == "XID"` on Linux. Use for the X11 backend. | docs.avaloniaui.net native-interop / window-handles |
