# SP-096 — the tray menu shipped; the tuck stayed refused, for a new reason

Branch `lane/SP-096-tray-menu-and-tuck`, base `f1a92764`.
Review Level 3. Plan checkpoint at `plan.md`, approved with one condition (pin the measurement),
which is discharged below.

## Outcome in one paragraph

`ITrayPresence` grew a menu surface and a balloon surface. `ShellTray` is the port's first consumer
of the tray capability anywhere: on a DTRH launch the shell is minimized and a **shell-confirmed
icon carrying WPF's four-entry menu** goes up for the interval WPF's icon is up, with WPF's
first-minimize balloon on the first duck of the process. The shell is **never hidden** — not on the
refusing branch, and not on the fully-working one — because hiding it in Avalonia takes the DTRH
window down with it. That is a measured fact, now pinned by a test whose name says why it exists.

## The judgement, and what changed it

SP-094 refused the tuck because the icon had no menu. That cause is **discharged**: the menu exists
and the OS itself is its oracle.

The tuck is still refused, for a different cause discovered by executing rather than reasoning:

> **Avalonia 12.1.1 `Window.Hide()` on an owner also hides every window owned by it, and the
> owner's later `Show()` does not bring them back. `Show(owner)` on a hidden owner throws.
> Minimize propagates to nothing.**

The DTRH host window is owned by the shell (`DtrhLaunchCoordinator.cs:167`, `window.Show(_owner)`).
So a WPF-shaped tuck would hide the descent the tuck exists to make room for, break the SP-027
watchdog's one permitted relaunch, and take an open companion window down with no re-show.

WPF survives the same call because Win32 does not propagate hide to owned windows — **and WPF needs
that ownership, in its own words at the construction site** (`DtrhHostService.cs:129-132`,
`OwnedByMainWindow = true`): *"Glue the descent above MainWindow via native ownership: main gets
raised by plenty of things we don't control (avatar barks, a video window closing, a tray restore)
and used to land on top of the game."* In Avalonia the two properties are mutually exclusive at
12.1.1. Keeping the descent owned and visible is the load-bearing outcome.

**Unowning the descent window to get both is recorded as an owner call and was not made here.**

## The pinned measurement (the coordinator's condition)

`client/tests/CcpClient.HeadlessTests/ShellTrayHeadlessTests.cs`:
`AvaloniaHidesOwnedWindowsWithTheirOwner_WhichIsWhyTheShellIsNeverHidden`.

It asserts, on real Avalonia windows: an owned window is visible before the owner hides, invisible
after, still invisible after the owner shows again; `Show(hiddenOwner)` throws
`InvalidOperationException`; a minimized owner keeps its owned window visible, still counts as
`IsVisible`, and accepts a new owned window. Its failure messages say what a red means — *"Avalonia
no longer hides owned windows with their owner … re-open the tuck decision on purpose rather than
leaving this test green in a direction nobody read."*

## Menu

WPF `Services/Notifications/TrayIconService.cs:96-110`, four entries in WPF's order:

| # | Label | Source | Port action |
|---|---|---|---|
| 1 | `Show Dashboard` | `:98` verbatim | `ShellTray.Restore` |
| 2 | `Wake Up!` | `:102` — WPF's non-bambi branch, which the port lives in permanently (no mod system) | `MainWindow.ShowCompanion` (§5's third companion route) |
| 3 | *(separator)* | `:107` | — |
| 4 | `Exit` | `:109` verbatim | classic lifetime `Shutdown()` -> `desktop.Exit` -> `ApplicationHost.ShutdownAsync` (`App.axaml.cs:88-95`) |

`TrayMenu`'s constructor **refuses** a menu with no restore entry, with two, with duplicate ids or
with no commands. The hazard SP-094 named is structural, not a convention.

## The `Unavailable` branch — what it does and how it is proved

`TrayPresenceFactory.Create()` hands every non-Windows run an `UnsupportedTrayPresence`, which now
refuses `SetMenu` and `ShowNotification` as well as `Place`/`Remove`. `ShellTray` then:

- minimizes the shell (never hides it), so the taskbar button is the way back;
- places **no** icon — the menu is asked for first and its refusal stops the placement;
- asks for **no** balloon, and does **not** consume the once-ever latch;
- writes one diagnostic line carrying the typed reason code and the words *"taskbar button is the
  way back"*.

Proved by `DuckingWithAnUnavailableTray_MinimizesAndNeverHides_AndSaysWhyThereIsNoIcon`, driving a
real `UnsupportedTrayPresence` (not a double) against a real Avalonia window, plus
`TheRefusingBackend_RefusesTheMenuAndTheBalloonToo` in the unit suite, which also re-asserts that
`LinuxManualGate` still travels with the refusal.

## Bite tests (step 6) — both seeded, both red, both reverted

| Mutation | Result |
|---|---|
| `ShellTray.PlaceIcon` calls `_shell.Hide()` on the menu-refused (Unavailable) branch | `DuckingWithAnUnavailableTray_…` **FAILED**: *"the shell was HIDDEN while the tray reported Unavailable — there is no icon and no menu, so the taskbar button was the only way back and it has just been removed"* |
| `Win32TrayPresence.SetMenu` skips the `wake` entry | **3 unit facts FAILED**: the OS read-back, the right-click dispatch, and the dispose/oracle fact |

Both reverted; `grep SEEDED` over the two files returns nothing, and the build is 0/0 after revert.

## Floor

Pin `1110` unit / `54` headless. Declared delta in `floor-delta.json`: **+7 unit, +8 headless**.

Observed: **1117 unit (1110 + 7), 62 headless (54 + 8)**, 0 failed, 2 skipped (both pre-existing
OS-gated `allowedSkips` entries). `floor.json` untouched; no name added to `allowedSkips`.

## What this does NOT prove

Nothing here is presentation-verified. Specifically undischarged, and all of it headed:

- that the tray icon is visible to a human, or that a real left- or right-click lands on it;
- `TrackPopupMenu`'s modal loop and the menu it draws — the one call behind a seam, whose product
  default is the real call;
- that a balloon ever appears (Windows suppresses notifications under Focus Assist, quiet hours, a
  full-screen app and the per-app switch, and reports none of it back, so `Available` here means
  *the shell accepted the request*);
- that the shell's minimize and restore behave on a real desktop, that the taskbar button really
  leaves and returns, or any composited pixel, z-order or focus fact;
- **Linux**: nothing changed and nothing is claimed. `TrayPresenceFactory.LinuxManualGate` still
  names the exact three-part gate, and this machine still cannot discharge it.

The right-click route is proved to the **message** level (the shell's own notification posted to the
real owner window, pumped, real window proc) and the menu to the **OS** level (USER32 read back
through an independent second set of declarations). Neither is a claim about a human.

## Files changed

Product:
- `client/src/CcpClient.Desktop/Tray/TrayMenu.cs` (new), `TrayNotification.cs` (new),
  `ShellTray.cs` (new)
- `client/src/CcpClient.Desktop/Tray/ITrayPresence.cs`, `TrayReasonCodes.cs`,
  `UnsupportedTrayPresence.cs`, `Win32TrayInterop.cs`, `Win32TrayPresence.cs`
- `client/src/CcpClient.Desktop/Views/MainWindow.axaml.cs`
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhLaunch.cs`

Tests:
- `client/tests/CcpClient.Tests/TrayCapabilityTests.cs`, `TrayObservations.cs`, `TrayShellProbe.cs`
- `client/tests/CcpClient.HeadlessTests/ShellTrayHeadlessTests.cs` (new)

Docs:
- `client/docs/wpf-surface-reachability.md` — new §12 (D35–D39); §10 D20's close condition marked
  SUPERSEDED with a pointer to D35.

Packet: `plan.md`, `record.md`, `floor-delta.json`.

Nothing outside File Scope. `client/tests/floor/floor.json`, `client/docs/task-board.md`,
`client/tools/**`, `Entitlement/**`, `Features/Intake/**`, `ConditioningControlPanel/**` untouched.

## Spec-versus-code notes

1. **The packet says the WPF menu is "four items, including the companion wake entry"
   (`TrayIconService.cs:96-119`).** The source has four *entries* of which one is a separator, so
   three commands. Ported as four entries in WPF's order; the separator is real and the OS confirms
   it as `MFT_SEPARATOR`.
2. **The packet's balloon citation `TrayIconService.cs:152-157` is exact** — `if
   (!_hasShownFirstMinimizeNotification)` at `:152`, latch set at `:154`, `ShowBalloonTip` at
   `:155-156`. The code and record cite `:150-157`, a range two comment lines wider. No divergence,
   noted so the widening is deliberate rather than sloppy.
3. **The packet's restore citation `DtrhHostService.cs:995-998` is exact** and matches the landed
   `:998` in D20 (`if (_minimizedMainWindow)` at `:995`, `ShowFromTray()` at `:998`). Verified
   rather than assumed, because this is the third packet in a row carrying a citation trap.
4. **The packet's premise "a menu whose only proof is that a method returned rebuilds the hazard"
   was accepted and answered**, but the thing that actually blocked the tuck was none of the
   above — it was the Avalonia ownership seam, which no citation in the packet mentions because
   nothing had executed it before.
