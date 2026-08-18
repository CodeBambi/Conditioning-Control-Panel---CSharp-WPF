# SP-099 — the overlay surface, approached the way the first attempt was not

Branch `lane/SP-099-overlay-surface`, base `3f96c24f`. Plan checkpoint in `plan.md` (approved
before the first product edit). Review Level 3.

---

## 1. Where the first attempt died, in its own source

Read before designing: `ConditioningControlPanel/CCP.Core/Platform/IOverlaySurface.cs`,
`CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs`,
`CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs`,
`CCP.Avalonia.Desktop.Linux/Platform/LinuxOverlaySurface.cs`,
`CCP.Avalonia.Desktop.Linux/Platform/LinuxOverlayBackendSelector.cs`,
`CCP.Core/Platform/ILinuxOverlayBackend.cs`, `CCP.Avalonia/Platform/AvaloniaPlatformCapabilities.cs`,
`tests/CCP.Avalonia.Desktop.Windows.Smoke/VisibleOverlayVerification.cs`, plus
`client/docs/first-attempt-systemic-lessons.md:16` and `client/docs/first-attempt-lessons.md:20-25`.

1. **The seam returns `void`, so nothing can be wrong.** `IOverlaySurface` is
   `void Show(); void Hide(); void Close(); void SetClickThrough(bool); void SetBounds(PixelRect);
   bool IsVisible { get; }`. An overlay that never appeared and one that covered the screen are the
   same call.
2. **Click-through is an empty method body.** `AvaloniaOverlaySurface.cs:31-35` is a comment inside
   braces; every platform that did not get a head-specific subclass silently "had" click-through.
   Its `IsHitTestVisible = false` (`:26`) is Avalonia-internal input routing, not OS hit testing.
3. **The Windows override builds the invisible window.** `WindowsOverlaySurface.cs:26-45` ORs in
   `WS_EX_TRANSPARENT | WS_EX_LAYERED` and never calls `SetLayeredWindowAttributes`. It checks no
   return value, re-reads nothing, and its disable path (`exStyle &= ~WS_EX_TRANSPARENT`) leaves
   `WS_EX_LAYERED` on, so the two directions are asymmetric.
4. **Linux is a documented no-op.** `LinuxOverlaySurface.cs:9-78` wraps every operation in a catch
   that logs and continues — the class doc calls it a "never-throw seam" where "overlay operations
   degrade to logged no-ops". `LinuxOverlayBackendSelector.cs:41-88` is "guaranteed never to throw
   and never to return null (worst case: FallbackBackend)", and that fallback "makes zero P/Invokes
   and always constructs".
5. **Availability was a platform check, verification was a human.**
   `AvaloniaPlatformCapabilities.cs:29-30` is `SupportsOverlays = IsDesktop;`. The only overlay
   verification in the tree prints `"Tell me what you saw in STAGE 1 vs STAGE 2"` and ends
   `Environment.ExitCode = 0; // human-judged; never fail the process`.

## 2. The measurement that settled the design, taken before any of it was written

A throwaway console probe on this machine (Windows 11, `SM_CMONITORS = 1`, primary 1646x1029),
run twice. Raw output of the second run:

```
monitors=1 primary=1646x1029
[A] click-through ON  -> OTHER (0x130938)
[B] click-through OFF -> OVERLAY (0x16E08AC)
[A2] restored ON      -> OTHER (0x130938)
[C] ghost(layered, attrs never set): IsWindowVisible=True GetLayeredWindowAttributes=False alpha=0 flags=0x0 err=0
[D] z index overlay=2 catcher=8 visibleTops=23
[E] messages drained after the fact: 2
[F] moved rect = 714,431,243x185 requested 714,431,243x185
[G] after SW_HIDE IsWindowVisible=False; WindowFromPoint=OTHER
[H] after destroy IsWindow(overlay)=False IsWindow(catcher)=False
```

Three things came out of it.

**(a) `WindowFromPoint` honours `WS_EX_TRANSPARENT` on this machine, in both directions.** `[A]`,
`[B]`, `[A2]` are the same point and the same window, answered differently by the window manager
depending only on the flag. That is a hit-test fact, not a flag assertion, and it is what trap 2
asked for.

**(b) `[C]` is trap 1, shipped and running.** A window with `WS_EX_LAYERED` whose attributes were
never set — the exact state `WindowsOverlaySurface.cs:26-45` leaves a window in — reports
`IsWindowVisible = TRUE` while `GetLayeredWindowAttributes` returns FALSE. The OS agrees it exists,
agrees it is visible, and composites nothing.

**(c) The window that won the point at `[A]` is the shipping WPF product itself.** Enumerating the
z-order identified `0x130938` as
`HwndWrapper[ConditioningControlPanel;;...] 'Conditioning Control Panel v6.8.1'`, topmost, 1563x943
over the screen centre — live confirmation of its own `RaiseAllToFront` contention
(`Services/Flash/FlashService.cs:206-243`). The bounded re-raise loop in both the backend and the
probe exists because of an observed collision, not a hypothetical one.

## 3. WPF ground truth used (v6.8.1, cited against the tree)

`ConditioningControlPanel/Services/Flash/FlashService.cs`:

| line | fact |
|---|---|
| `:3611-3625` | the flash window: `AllowsTransparency = true` (`:3613`), `WindowStyle.None` (`:3614`), `Topmost = true` (`:3615`), `ShowInTaskbar = false` (`:3616`), `ShowActivated = false` (`:3617`) |
| `:3576-3583` | why it is configured completely before the first `Show()`: resizing a realized layered WPF window deadlocks the UI thread on `MediaContext.CompleteRender` |
| `:3654-3673` | `ApplyClickability`: `\|= WS_EX_LAYERED \| WS_EX_NOACTIVATE` (`:3666`), then `&= ~WS_EX_TRANSPARENT` when clickable else `\|= WS_EX_TRANSPARENT` (`:3667-3668`), written to the LIVE hwnd every spawn |
| `:3816-3841` | `HideFromAltTab`: `\|= WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE` (`:3826`) |
| `:3861-3873` | `ForceTopmost`: `SetWindowPos(hwnd, HWND_TOPMOST, 0,0,0,0, SWP_NOMOVE\|SWP_NOSIZE\|SWP_NOACTIVATE)` (`:3867`) |
| `:206-243` | `RaiseAllToFront`: topmost is contested and re-asserted on a cadence |
| `:2204-2245`, `:4130-4141` | per-monitor enumeration with a per-screen DPI scale kept on `MonitorInfo.DpiScale` |

**Spec-vs-code discrepancy, resolved in favour of the tree.** The packet cites `Topmost` at `:3612`,
`WS_EX_TRANSPARENT` at `:3666` and `SetWindowPos HWND_TOPMOST` at `:3862`; the tree at this SHA has
`:3615`, `:3667-3668` and `:3867`. `client/docs/wpf-surface-reachability.md` row D47 already carried
the correct trio, so the document was right and the packet's copy drifted about three lines. The
correction is recorded in that document under the SP-099 section, and every citation here is against
the tree.

## 4. What was built

`client/src/CcpClient.Desktop/Overlay/` (new; nothing else under `src/` was touched):

| file | what |
|---|---|
| `IOverlayPresence.cs` | `Present` / `SetClickThrough` / `Withdraw` / `IsPresenting` / `IDisposable`, every operation returning `CapabilityState` |
| `OverlaySurfaceRequest.cs` | `OverlayBounds` (physical pixels) + opacity in `(0, 1]` + click-through. Opacity `<= 0` throws rather than being clamped or accepted: an invisible surface is the ghost at the request level, and it is a caller bug rather than a platform state |
| `OverlayReasonCodes.cs` | thirteen codes, because "invisible", "buried" and "swallowing clicks" are different facts and a caller picks a different response for each |
| `Win32OverlayInterop.cs` | the product's own P/Invokes |
| `Win32OverlayPresence.cs` | the backend |
| `UnsupportedOverlayPresence.cs` | the honest refusal |
| `OverlayPresenceFactory.cs` | platform → backend, plus `LinuxManualGate` and `WaylandNote` as constants that travel with the refusal |
| `OverlayDisplays.cs` | `EnumDisplayMonitors` + `GetMonitorInfoW`, because WPF places per monitor |

**The structural decision.** The backend owns a plain Win32 top-level window it creates itself
(`WS_POPUP`, `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, alpha set
while still hidden, then `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE | SWP_SHOWWINDOW)`), rather than
subclassing an Avalonia `Window` as the first attempt did. That is what moves the whole Windows half
from "a human watched it" to falsifiable in the pure-logic test project with no Avalonia runtime
present. It is the same choice `Win32TrayPresence` made and for the same reason.

**Both `Available` construction sites are downstream of the OS.** `Present` and `SetClickThrough`
each construct exactly one, and each sits after `Confirm(...)` / `ConfirmInputRouting(...)` returned
null. `Confirm` asks, in order: `IsWindow`, `IsWindowVisible`, `GetWindowRect` equals the request,
**`GetLayeredWindowAttributes` returns a non-zero `LWA_ALPHA` equal to the request**, the ex-style
read-back carries `WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST` and the
requested `WS_EX_TRANSPARENT` polarity, the z-order walk from `GetTopWindow` places the surface before
the first visible non-topmost window, the hit test answers correctly in both polarities, and
`GetForegroundWindow` is not the surface.

**Why the hit test is asked twice.** "The point does not route to this window" is also true of a
window that was never created. So `ConfirmInputRouting` first clears `WS_EX_TRANSPARENT`, re-raises,
and requires the point to route TO the surface — establishing that the point is really the surface's
— and only then restores the requested polarity and requires the answer the request asked for. Both
legs are two style writes and two hit-test queries with no wait between them, the alpha is never
touched, and nothing changes on screen. A refusal restores the requested polarity before returning.

## 5. What is proven from the OS, and what is not

Facts in `client/tests/CcpClient.Tests/OverlayCapabilityTests.cs` (16), reading
`OverlayObservations` (one cached real-window lifecycle) against `OverlayWindowProbe` — an
independent second copy of every P/Invoke, in the SP-093 `TrayShellProbe` shape, carrying its own
negative control that re-runs on every suite execution.

**Proven from the operating system**

| fact | instrument |
|---|---|
| the surface exists and the OS reports it visible | `IsWindow`, `IsWindowVisible` |
| the OS holds exactly the requested rectangle | `GetWindowRect` |
| the OS holds the requested non-zero alpha, so the compositor has something to draw | `GetLayeredWindowAttributes` |
| the OS's own z-order puts it above every ordinary window | walk `GetTopWindow` → `GetWindow(GW_HWNDNEXT)`, compare against the first visible window without `WS_EX_TOPMOST` |
| the window manager routes the surface's centre AWAY from it while click-through is on, TO it while click-through is off, and away again when restored | `WindowFromPoint`, same point, three times, one run |
| showing it never takes the foreground | `GetForegroundWindow` |
| withdrawing removes it from the visible set AND from the hit test | `IsWindowVisible`, `WindowFromPoint` |
| disposing leaves no top-level window behind | `IsWindow` |
| the display enumeration agrees with the OS | `GetSystemMetrics(SM_CMONITORS / SM_CXSCREEN / SM_CYSCREEN)` |
| the instrument itself can say "no" | the negative control: an opaque scratch window wins its own point; a `WS_EX_TRANSPARENT` scratch raised ABOVE it does not take the point; clearing that flag hands the point over; a `WS_EX_LAYERED` scratch with attributes never set reads alpha -1 while the OS still calls it visible; all scratch windows are gone after teardown |

**Headed-only, named**

- **That anything is drawn.** Nothing is. No content, no renderer, no effect wired (D52).
- **Composited pixels** — that a human sees the surface above another application's window. DWM
  composition, exclusive-fullscreen and DirectX applications, Magnifier, RDP and mirror drivers can
  each defeat it with every query above still answering yes. `presentation-verified` belongs to the
  orchestrator's headed capture and is not claimed here.
- **A real pointer.** `WindowFromPoint` is the window manager's routing question asked, not delivered
  input. `SendInput` was considered and deliberately rejected: it moves the user's cursor mid-suite
  and fails silently on a locked workstation. Physical click-through is a headed gate.
- **Multi-monitor and cross-DPI placement.** This machine reports one display.
- **Sustained topmost under contention.** The port re-asserts on demand, not on a cadence (D53).
- **Everything on Linux** (D56).

## 6. Linux

`OverlayPresenceFactory.CreateFor(Linux)` returns `UnsupportedOverlayPresence` with
`overlay-mechanism-absent`. The detail names the route (X11 override-redirect +
`_NET_WM_STATE_ABOVE` + an empty XFixes `ShapeInput` region), names why Wayland is a refusal rather
than a harder Linux (no protocol an ordinary client may use; `wlr-layer-shell` is a wlroots extension
Mutter does not implement; the pinned Avalonia 12.1.1 graph ships `Avalonia.X11` and
`Avalonia.FreeDesktop` and no Wayland package — verified in the local package cache), and carries a
four-step manual gate: `_NET_WM_STATE_ABOVE` via `xprop` plus a z-order check via
`xwininfo -root -children`; an empty input shape; a human confirming a click lands on the application
underneath and that the surface is visible; and the same run under a native Wayland session where the
expected outcome is a refusal. **This machine cannot discharge it**: the port's Linux environment is
WSLg, whose XWayland root has no `_NET_CLIENT_LIST` (`client/docs/port-lessons.md:52`).

The refusal covers `Withdraw` and `SetClickThrough` as well as `Present`, and never reports
`IsPresenting` — a test asserts all three, because a partial refusal is a path a caller can mistake
for a surface on screen. macOS and Unknown refuse the same way. **No test is skipped to conceal any
of this**; the Linux branch is exercised for real on this Windows box.

Board disposition: **BLOCKED**, naming the gate above.

## 7. Prove it bites (packet step 5)

Both mutations were applied to the committed tree, measured, and reverted with
`git checkout --` so the restore is byte-identical. Neither is committed; `git status` was clean
after each.

**Mutation A — the packet's: claim `Available` without creating the window.** Inserted
`_presenting = true; return new CapabilityState.Available("MUTATION: ...");` immediately before
`EnsureWindow`.

```
Failed!  - Failed: 7, Passed: 9, Skipped: 0, Total: 16
```

Reds: presence, geometry, alpha, z-order, input routing (both polarities), withdraw, and the
no-draw contract string. Representative message:

```
this session has an interactive desktop = True, but after Present the OS reporting the surface
visible = False. On a machine with a desktop the window must really be on it; with no desktop
nothing may be claimed. Backend said: Available(MUTATION: claimed without creating the window)
```

**Mutation B — the CCP shape specifically: `WS_EX_LAYERED` set, attributes never given.** Replaced
the `SetLayeredWindowAttributes` guard with `if (false)`, i.e. exactly
`WindowsOverlaySurface.cs:26-45`.

```
Failed!  - Failed: 5, Passed: 11, Skipped: 0, Total: 16
```

and the backend refused, unprompted, with the code that exists for it:

```
Unavailable(overlay-not-composited: the OS holds NO layered attributes for window 0x117094E
(GetLayeredWindowAttributes returned FALSE, last-error 0); it is a window that exists, reports
visible, and composites nothing)
```

That is the first attempt's defect, reproduced deliberately, detected by the check the coordinator
asked to be among the `Available` preconditions, and named in the refusal.

## 8. Gate results

```
dotnet build client/CcpClient.sln -c Debug --nologo      ->  0 Warning(s)  0 Error(s)
node client/tests/floor/check-floor.mjs                  ->  CcpClient.Tests total 1191
dotnet test client/tests/CcpClient.HeadlessTests/...     ->  81 passed, 0 skipped
```

Pin is **1175 unit / 81 headless**. Observed **1191 unit / 81 headless**. `1191 = 1175 + 16` and
`81 = 81 + 0`, which is exactly `floor-delta.json` (`unit: 16`, `headless: 0`). The floor run
therefore reports a violation against the shared pin, which is the expected and documented outcome
for a lane: the pin is not this lane's to edit, and the orchestrator sums the declared deltas at
land. **Nothing was widened, skipped or special-cased**; `client/tests/floor/floor.json` was never
opened and no name was added to `allowedSkips`.

The document-reading guards were re-run after the `wpf-surface-reachability.md` edit
(`UpstreamPayloadInventoryTests`, `VersionDerivationTests`, `AiOperationContractTests`,
`VacuousShapeGuardTests`, `FloorWrapperGuardTests`, `TestTimingGuardTests`): 64 passed, 0 failed. No
vacuous-shape ledger entry was needed — every fact body asserts at statement depth 0 with no
predicate, because all platform checks live in the probe helper.

## 9. Wired to nothing, and saying so

Nothing in this packet touches `client/src/CcpClient.Desktop/Effects/**`, `Session/**`, `Views/**`,
`Capabilities/**`, the composition root, `client/tools/**`, `client/docs/task-board.md`,
`client/tests/floor/floor.json`, or `ConditioningControlPanel/**`. The overlay is not registered
anywhere and no effect can reach it. **The temptation to close the loop by making Flash Images draw
on it was real and was declined**: it would have entangled this capability's evidence with the
effect's, and the resulting claim — "a flash appeared" — is a headed one that cannot be discharged
from here, so a headed gate nobody can run would have blocked a capability that is otherwise sound.
D47 stays open. Nothing draws yet.
