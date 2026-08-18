# SP-099 — plan checkpoint (Review Level 3, before the first product edit)

Branch `lane/SP-099-overlay-surface`, worktree
`.claude/worktrees/agent-aae7debda314cb7f4`, base `3f96c24f`.

---

## 1. Where the first attempt died, precisely

Read: `ConditioningControlPanel/CCP.Core/Platform/IOverlaySurface.cs`,
`CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs`,
`CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs`,
`CCP.Avalonia.Desktop.Linux/Platform/LinuxOverlaySurface.cs`,
`CCP.Avalonia.Desktop.Linux/Platform/LinuxOverlayBackendSelector.cs`,
`CCP.Avalonia/Platform/AvaloniaPlatformCapabilities.cs`,
`tests/CCP.Avalonia.Desktop.Windows.Smoke/VisibleOverlayVerification.cs`,
plus `client/docs/first-attempt-systemic-lessons.md:16` and
`client/docs/first-attempt-lessons.md:20-25,141-151`.

Five separate failures, each of which this packet has to make structurally impossible:

**(a) The whole surface returns `void`. There is nothing to be wrong.**
`IOverlaySurface` is `void Show(); void Hide(); void Close(); void SetClickThrough(bool);
void SetBounds(PixelRect); bool IsVisible { get; }`. Not one member can report a refusal.
An overlay that failed to appear and an overlay that appeared are the same call. This is the
`ITrayIcon.SetIsVisible -> void` shape SP-093 refused for exactly this reason
(`client/src/CcpClient.Desktop/Tray/Win32TrayPresence.cs:24-34`).

**(b) Click-through is an empty method body with a comment in it.**
`AvaloniaOverlaySurface.SetClickThrough` (`:31-35`) is:

```
public virtual void SetClickThrough(bool enabled)
{
    // Cross-platform input passthrough is not available in Avalonia core.
    // Platform heads can subclass and override this with native interop.
}
```

Every caller on every platform that did not get a head-specific subclass got a silent
success. `IsHitTestVisible = false` in the constructor (`:26`) is an *Avalonia-internal*
hit-test flag: it stops Avalonia routing input to controls, it does not stop the OS
delivering the click to the window. So the base class both claims click-through and does
not have it.

**(c) The Windows override sets `WS_EX_LAYERED` and never gives the OS an alpha — the
"exists, is on top, and is invisible" window.**
`WindowsOverlaySurface.SetClickThrough` (`:26-45`) ORs in `WS_EX_TRANSPARENT | WS_EX_LAYERED`
and calls `SetWindowLong`. It never calls `SetLayeredWindowAttributes` or
`UpdateLayeredWindow`. **Measured on this machine today** (scratch probe, results in §5):
a window with `WS_EX_LAYERED` and no attributes ever set reports `IsWindowVisible = TRUE`
while `GetLayeredWindowAttributes` returns FALSE — the OS holds no alpha for it and composites
nothing. That is precisely trap 1 of this packet, shipped. It also never checks a return value,
never re-reads the style, and its disable path (`exStyle &= ~WS_EX_TRANSPARENT`) leaves
`WS_EX_LAYERED` on, so the two directions are not symmetric.

**(d) Linux is a documented no-op with a documented reason for being one.**
`LinuxOverlaySurface` (`:9-78`) wraps every operation in `Guard(...)` whose catch block logs
and continues — the class doc calls this the "never-throw seam ... overlay operations degrade
to logged no-ops". `LinuxOverlayBackendSelector.SelectBackend` (`:41-88`) is "guaranteed never
to throw and never to return null (worst case: FallbackBackend)", and `FallbackBackend` "makes
zero P/Invokes and always constructs". So on a Linux box with no usable display server the
overlay comes up, reports success on every call, and draws nothing forever.
`first-attempt-systemic-lessons.md:16` names this file.

**(e) Availability was computed from `OperatingSystem`, and verification was a human.**
`AvaloniaPlatformCapabilities` (`:29-30`): `SupportsOverlays = IsDesktop;` — a platform check
promoted straight to a capability claim. The only overlay verification in the tree is
`VisibleOverlayVerification`, a `--verify-visible` harness that prints
`"Tell me what you saw in STAGE 1 vs STAGE 2"` and ends with
`Environment.ExitCode = 0; // human-judged; never fail the process`. Nothing in that tree can
go red because an overlay did not appear.

## 2. How this packet differs, item by item

| first attempt | here |
|---|---|
| `void` everywhere | every operation returns `CapabilityState`; `Available` is constructed at exactly two places in the backend and both are downstream of an OS read-back |
| click-through = empty method | click-through is claimed only after the OS's own hit test (`WindowFromPoint`) stops returning our window, and the same run proves the same window DOES get returned with the flag cleared |
| `WS_EX_LAYERED`, no alpha | `SetLayeredWindowAttributes` is mandatory and `GetLayeredWindowAttributes` must read back alpha > 0, or the presence refuses. The refusal names the CCP shape |
| Linux never-throw no-op | `UnsupportedOverlayPresence` — a typed `Unavailable` with a named manual gate, the `TrayPresenceFactory.LinuxManualGate` shape (`TrayPresenceFactory.cs:33-42`) |
| `SupportsOverlays = IsDesktop` | platform decides *selection* only (`runtime-capability-contract` §2 rule 2); nothing in the platform switch can produce `Available` |
| verification = a human reading console output | facts in `CcpClient.Tests` with an independent P/Invoke oracle that carries its own negative control, in the SP-093 `TrayShellProbe` shape |

One more difference of shape: the first attempt made the overlay **an Avalonia `Window`
subclass**, which is why its verification could only ever be headed. This backend owns a
**plain Win32 top-level window it creates itself**, exactly as `Win32TrayPresence` owns its
hidden owner window — so the whole of the Windows half is drivable and falsifiable from the
pure-logic test project with no Avalonia runtime at all.

## 3. WPF ground truth (v6.8.1) actually read

`ConditioningControlPanel/Services/Flash/FlashService.cs`:

- `:3611-3625` the flash window: `AllowsTransparency = true` (`:3613`), `WindowStyle.None`
  (`:3614`), `Topmost = true` (`:3615`), `ShowInTaskbar = false` (`:3616`),
  `ShowActivated = false` (`:3617`), sized before the first `Show()` because resizing a
  realized layered window deadlocks the render thread (`:3576-3583`).
- `:3660-3673` `ApplyClickability`: `GWL_EXSTYLE |= WS_EX_LAYERED | WS_EX_NOACTIVATE`
  (`:3666`), then `&= ~WS_EX_TRANSPARENT` when clickable else `|= WS_EX_TRANSPARENT`
  (`:3667-3668`). Re-applied on the live hwnd every spawn, never via `SourceInitialized`,
  because pooled windows flip polarity between spawns.
- `:3816-3841` `HideFromAltTab`: `|= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` (`:3826`).
- `:3861-3873` `ForceTopmost`: `SetWindowPos(hwnd, HWND_TOPMOST, 0,0,0,0,
  SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)` (`:3867`).
- `:206-243` `RaiseAllToFront`: topmost is **contested** and WPF re-asserts it on a cadence
  driven by the chaos layer (~1/s), because other layers bury an already-showing flash.
- `:2204-2245` `GetMonitors`: per-screen enumeration with a per-screen DPI scale, bounds
  converted to DIPs; `:4130-4141` `MonitorInfo` keeps `DpiScale` because a flash's geometry is
  computed manually per monitor.
- `:4164-4189` `NativeMethods`: the exact flag values.

**Spec-vs-code discrepancy (recorded, not improvised):** the packet cites `Topmost = true`
at `:3612`, `WS_EX_TRANSPARENT` at `:3666`, `SetWindowPos HWND_TOPMOST` at `:3862`. In the tree
at this SHA they are `:3615`, `:3667-3668` and `:3867`. `client/docs/wpf-surface-reachability.md`
row D47 already carries the correct trio (`FlashService.cs:3615`, `:3667-3668`, `:3862-3868`),
so the doc is right and the packet's copy drifted by ~3 lines. I will cite the lines that are
in the tree.

## 4. Design

New folder `client/src/CcpClient.Desktop/Overlay/` (nothing else in `src/` is touched):

| file | what |
|---|---|
| `IOverlayPresence.cs` | `Present(OverlaySurfaceRequest) : CapabilityState`, `SetClickThrough(bool) : CapabilityState`, `Withdraw() : CapabilityState`, `bool IsPresenting`, `OverlayNativeHandles NativeHandles`, `IDisposable`. Doc states what `Available` may and may not mean |
| `OverlaySurfaceRequest.cs` | `OverlayBounds` (x, y, w, h in physical pixels — a Win32 top-level window's coordinates ARE physical pixels), `Opacity` in `(0, 1]`, `ClickThrough` bool. Opacity `<= 0` is refused, not clamped: an invisible surface that reports success is the CCP ghost |
| `OverlayReasonCodes.cs` | `overlay-mechanism-absent`, `overlay-window-creation-failed`, `overlay-not-composited` (the layered-alpha refusal), `overlay-not-on-top`, `overlay-input-not-passing-through`, `overlay-geometry-refused`, `overlay-nothing-presented`, `overlay-presence-disposed`, `overlay-request-invisible` |
| `Win32OverlayInterop.cs` | the product's own P/Invokes (test re-declares its own — SP-093 oracle discipline) |
| `Win32OverlayPresence.cs` | the backend |
| `UnsupportedOverlayPresence.cs` | the honest refusal; never reports `IsPresenting`, refuses `Withdraw` as well as `Present` |
| `OverlayPresenceFactory.cs` | platform → backend, plus `LinuxManualGate` / `WaylandNote` constants that travel with the refusal |
| `OverlayDisplays.cs` | `EnumDisplayMonitors` + `GetMonitorInfoW` → display rects, work areas, primary flag. WPF places per monitor (`FlashService.cs:2204-2245`); a primary-only capability would be a lesser outcome |

`Win32OverlayPresence.Present` creates a `WS_POPUP` window with
`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` (WPF's set,
`FlashService.cs:3666`, `:3826`, `:3667-3668`), calls `SetLayeredWindowAttributes(LWA_ALPHA)`,
places it with `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE | SWP_SHOWWINDOW)` (WPF `:3867`),
and only then **asks the OS six questions**. Any "no" is a typed `Unavailable` naming the
failing check and the Win32 last-error; there is no path to `Available` that skips them.
Topmost is re-asserted in a bounded loop (no wall-clock wait) before the hit test, because
WPF's own code proves topmost is contested (`:206-243`) — and this machine proves it too
(§5: the shipping WPF app is running and re-raising itself over the screen centre right now).

## 5. What I prove from the OS, and what stays headed-only

All of the following were **measured on this machine before any of it was written**
(scratch console probe, Windows 11, 1 monitor, 1646x1029; raw output preserved in `record.md`):

| # | fact | instrument | measured |
|---|---|---|---|
| 1 | the window exists and is visible | `IsWindow` / `IsWindowVisible` | yes |
| 2 | the OS holds the geometry that was asked for, including after a move | `GetWindowRect` | exact match, both placement and move |
| 3 | **something is composited** — the OS holds a non-zero layered alpha | `GetLayeredWindowAttributes` | alpha 160 read back; and a layered window with attributes never set reports `IsWindowVisible=TRUE` + `GetLayeredWindowAttributes=FALSE` — the CCP ghost, detectable |
| 4 | **it is above every ordinary window** — the OS's own z-order, not a flag | walk `GetTopWindow` → `GetWindow(GW_HWNDNEXT)`, require our index < the first visible window without `WS_EX_TOPMOST` | overlay index 2, first ordinary window index 6 |
| 5 | **input passes through** — the window manager's hit test does not route the point to us | `WindowFromPoint` at the surface centre | with `WS_EX_TRANSPARENT`: returns another window; with the flag cleared: returns the overlay; restored: returns another window again. Both polarities, same run |
| 6 | it does not steal focus | `GetForegroundWindow` before/after | unchanged |
| 7 | withdraw really hides | `IsWindowVisible` false, hit test stops returning it | yes |
| 8 | dispose leaves no window | `IsWindow` false | yes |
| 9 | display enumeration agrees with the OS | `GetSystemMetrics(SM_CMONITORS/SM_CXSCREEN/SM_CYSCREEN)` vs `EnumDisplayMonitors` | to be asserted |

Fact 5 is the answer to trap 2 and it is deliberately not "the flag was set". `WindowFromPoint`
is the window manager's hit-test walk over the real z-ordered stack of real windows; the
differential (same window, same point, two flag states, two different answers) is what makes it
non-vacuous, and the probe re-runs that differential on its own scratch windows every run as a
negative control, so an oracle that degenerated into "always answers the top window" fails the
suite instead of certifying everything.

**Stays headed-only — named, not hand-waved:**

- **That anything is drawn. Nothing draws.** There is no content, no renderer, no effect wired.
  The surface is uniformly `LWA_ALPHA`-tinted and empty.
- **Composited pixels.** That a human sees it above another application's window. DWM
  composition, exclusive-fullscreen / DirectX apps, Magnifier, RDP and mirror drivers can all
  break it with every OS query above still answering yes. `presentation-verified` is the
  orchestrator's headed capture; this packet claims none of it.
- **A real pointer.** `WindowFromPoint` is a *query* of the hit test, not delivered input.
  Proving a physical click passes through needs `SendInput` (which moves the user's cursor
  mid-suite and fails silently on a locked workstation) or a human hand. I am not doing that in
  the floor; it is a headed gate.
- **Multi-monitor.** This box reports `SM_CMONITORS = 1`. Placement on a second display, and
  cross-DPI placement, are unproven here.
- **Sustained topmost under contention.** WPF re-asserts on a cadence (`:206-243`); this
  capability re-asserts on demand only. That another app cannot bury the surface over minutes
  is not proven.
- **Everything on Linux.**

## 6. Linux

`OverlayPresenceFactory.CreateFor(Linux)` returns `UnsupportedOverlayPresence` with
`overlay-mechanism-absent` and a detail naming the route and the gate — the
`TrayPresenceFactory.cs:33-42` shape. Verified for the text: the pinned `Avalonia.Desktop`
12.1.1 ships `Avalonia.X11` and `Avalonia.FreeDesktop` and **no Wayland package exists in the
graph**, so a Linux session is X11 or XWayland. The route that would work is X11
override-redirect + `_NET_WM_STATE_ABOVE` + an empty XFixes input-shape region; under a native
Wayland compositor there is no protocol an ordinary client can use to guarantee an always-on-top
click-through surface at all (`wlr-layer-shell` is wlroots-only; Mutter does not implement it).
The gate will name: a real X11 desktop session (not WSLg — `port-lessons.md:52` records that
WSLg's XWayland root has no `_NET_CLIENT_LIST`, so window enumeration cannot be trusted there),
`xprop` showing `_NET_WM_STATE_ABOVE` on the surface, `xwininfo` z-order, and a human confirming
a click lands on the window underneath. Board row: `BLOCKED`, not `WIP`.

Both branches are reachable from either OS (SP-093 precedent), so the refusal path is a real
test on this Windows box.

## 7. Tests and floor

`client/tests/CcpClient.Tests/` (pure logic project — no Avalonia runtime is involved anywhere
in this capability):

- `OverlayWindowProbe.cs` — independent P/Invokes, its own negative control
  (`RunNegativeControl`: scratch catcher + scratch transparent window prove the hit-test oracle
  can distinguish; scratch layered-without-attributes window proves the alpha oracle can say no;
  scratch window destroyed proves `WindowExists` can say no).
- `OverlayObservations.cs` — one run per scenario, claim and effect side by side in one record,
  so every fact asserts at statement depth 0 with no predicate (the `TrayObservations` shape;
  keeps `VacuousShapeDetector` clean without a ledger edit).
- `OverlayCapabilityTests.cs` — the facts. Expected ~10-12.

Every platform predicate lives in the probe helper, never in a fact body. No wall-clock wait:
the only loops are bounded iteration counts, like `TrayShellProbe.MaxPumpIterations`.

`spine-tasks/SP-099-overlay-surface/floor-delta.json` declares the count. `floor.json` is not
opened. Pin is 1175 unit / 81 headless; the observed total will be `1175 + <unit delta>` and I
will state both numbers.

**Step 5 (prove it bites)** will be run as stated: make `Present` claim `Available` without
creating the window, watch the suite red, restore byte-identically, and record the exact
failure text. Not committed.

## 8. Divergences to record (from D52)

- **D52** — the surface exists and **nothing draws on it**; WPF's flash window carries an
  `Image` and an opacity animation, this carries nothing. Extends D47 rather than closing it.
- **D53** — WPF re-asserts `HWND_TOPMOST` on a ~1/s cadence driven by the chaos layer
  (`FlashService.cs:206-243`); the port re-asserts on demand inside `Present`/`SetClickThrough`
  and runs no cadence, because there is no chaos layer and no second overlay to fight.
- **D54** — WPF pools and recycles flash windows and flips clickability per spawn on the live
  hwnd (`:3584-3607`, `:3660-3673`); the port creates one window per presence and flips the same
  flag on the same live hwnd, but does not pool. WPF's reason for pooling is a render-thread
  deadlock on resizing a realized layered window (`:3576-3583`), which the port cannot hit
  because it never resizes a realized window — it re-places with `SetWindowPos`.
- **D55** — WPF's monitor geometry is in DIPs with a per-screen DPI scale
  (`:2204-2245`, `:4130-4141`); the port's overlay bounds are physical pixels, because a Win32
  top-level window's coordinates are physical pixels and there is no WPF layout system in the
  path. DPI becomes a concern when content is drawn, which is a later packet.
- **D56** — Linux gets no overlay at all.

Anything found during implementation gets added rather than smoothed over.

## 9. Explicitly NOT in this packet

- Nothing is wired to Flash Images or to any effect, session, view or capability registry.
  `client/src/CcpClient.Desktop/{Effects,Session,Views}/**` and `Capabilities/**` are untouched.
- No `presentation-verified` claim.
- No board edit, no `floor.json` edit, no `client/tools/**` edit.

## 10. Risks I can see now

1. **Contention on the hit test.** The shipping WPF app is running on this machine right now
   with a topmost full-screen window over the screen centre and its own re-raise. Mitigation:
   bounded re-raise retry before the hit test, attempt count recorded in the observation. If it
   still loses, the honest answer is `Unavailable("another window is above ours")` and the test
   will say so rather than pass.
2. **A visible flicker during the suite.** The surface is a real visible window for the duration
   of a few syscalls. It is `SWP_NOACTIVATE` + `WS_EX_NOACTIVATE` so it cannot take focus, and
   it is small and centred. This is unavoidable if the facts are to be real.
3. **xunit parallelism.** All overlay observations live in one class so they serialize; the
   probe's scratch windows use per-instance GUID class names.
