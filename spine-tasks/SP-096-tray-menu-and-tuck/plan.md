# SP-096 — plan checkpoint

Branch `lane/SP-096-tray-menu-and-tuck`, worktree
`.claude/worktrees/agent-a2d700285a664b508`, base `f1a92764`.

## Short answer to the question the packet asks

**Menu: yes. Tuck: no — and the reason is NOT "I could not verify the menu".**

I can verify the menu end to end short of the human's hand, at OS level. The reason not to
tuck is a fact I measured during planning and did not expect:

> **In Avalonia 12.1.1, `Window.Hide()` on an owner also hides every window owned by it, and
> `Window.Show()` on the owner does NOT bring them back.**

The DTRH host window is owned by the shell (`DtrhLaunchCoordinator.cs:167`,
`window.Show(_owner)`). So a WPF-shaped tuck — `Hide()` the shell — would **hide the game
window the tuck exists to make room for**, and re-showing the shell would not bring it back.
That is a worse strand than the one SP-094 refused: not "hard to get back", but "the thing you
just launched disappeared".

## The measurements (executed, headless, Avalonia 12.1.1)

Run as a throwaway `[AvaloniaFact]` in `CcpClient.HeadlessTests`, deleted after reading; each
becomes a pinned fact in the implementation.

| # | Probe | Result |
|---|---|---|
| M1 | `owner.Show(); child.Show(owner); owner.Hide();` → `child.IsVisible` | **False** — hide propagates to owned windows |
| M2 | then `owner.Show();` → `child.IsVisible` | **False** — show does NOT propagate back |
| M3 | then `child.Show(owner)` explicitly | ok, visible again |
| M4 | `second.Show(hiddenOwner)` | **throws** `InvalidOperationException: Cannot show window with non-visible owner.` |
| M5 | `owner.WindowState = Minimized` → `child.IsVisible` | **True** — minimize does not propagate |
| M6 | `second.Show(minimizedOwner)` | ok |
| M7 | unowned window, `otherWindow.Hide()` | unaffected |
| M8 | `Maximized` → `Hide()` → `Show()` | WindowState survives as `Maximized` |

## Why WPF gets away with the same call and the port cannot

WPF's `MinimizeToTray()` is `_mainWindow.Hide()` (`Services/Notifications/TrayIconService.cs:145`).
WPF/Win32 do not propagate a hide to owned windows, so WPF's descent window survives it — and
WPF *needs* the descent window to stay owned. Its own words, at the construction site:

`Services/Chaos/DtrhHostService.cs:129-132`, `OwnedByMainWindow = true`:
> "Glue the descent above MainWindow via native ownership: main gets raised by plenty of things
> we don't control (avatar barks, a video window closing, a tray restore) and used to land on
> top of the game. Ownership makes the window manager keep the pair in order — without Topmost,
> which would cover other apps too."

So the port's `window.Show(_owner)` is **parity, not an accident**, and WPF names the defect
that appears if it is dropped. In Avalonia the two properties are mutually exclusive at
12.1.1: *keep the game owned* (so the shell can never land on top of it) **or** *hide the
shell*. They cannot both be had. Of the two, "the game window stays visible and above the
shell" is the load-bearing outcome; "the shell's taskbar button disappears" is not.

The only way to have both would be to unown the DTRH host window — which is the exact change
WPF's own comment refuses, would also need companion re-show handling (M1 hits the companion
window too), and would break the SP-027 watchdog relaunch (M4: `Show(_owner)` on a hidden
owner throws, so the one permitted relaunch would fault instead of relaunching). Every
consequence of that change is a **headed** claim I cannot discharge. So: no tuck.

**This is not SP-094's reason restated.** SP-094 refused because the icon had no menu. That
cause is removed by this packet. The new refusal has a different, narrower, and measured
cause, and it names its own close condition: *closes if Avalonia stops propagating hide to
owned windows, or if an owner-preserving hide lands.*

## What ships instead — the cause SP-094 named, actually removed

The tray icon and its menu become **real and wired**, on the same DTRH duck WPF tucks on. The
port keeps the plain minimize (taskbar button stays) **and additionally places the confirmed
icon with the menu on it**, for exactly the interval WPF's icon is up: placed when the hole
opens, removed when the flow ends.

So a port user during a descent has **three** ways back where WPF has two, and **zero** ways
to be stranded, because nothing is ever hidden.

### Menu items (WPF `TrayIconService.cs:96-109`, four entries)

| # | Port label | WPF source | Action |
|---|---|---|---|
| 1 | `Show Dashboard` | `:98` verbatim | restore the shell (`Show`/`WindowState`/`Activate`) |
| 2 | `Wake Up!` | `:103` — WPF picks `"Wake Bambi Up!"` when `App.Mods?.IsBambiMode == true`, else `"Wake Up!"`. The port has no mod system, so it is permanently the second branch; the label is WPF's own string for that branch, not an invention | open the companion window (`MainWindow.ShowCompanion`, the port's counterpart of `WakeBambiUp`, §5's third companion entry) |
| 3 | *(separator)* | `:107` | — |
| 4 | `Exit` | `:109` verbatim | `IClassicDesktopStyleApplicationLifetime.Shutdown()` → `desktop.Exit` → `ApplicationHost.ShutdownAsync()` (`App.axaml.cs:88-92`), the same guarded teardown the window-close path reaches |

### The balloon (code, not the comment)

`MainWindow/MainWindow.RemoteControl.cs:1515` says "No notification"; the call reaches
`MinimizeToTray()`, whose **first-ever** invocation fires
`ShowBalloonTip(2000, "Conditioning Control Panel", "Running in background. Click the tray icon to restore.", Info)`
(`TrayIconService.cs:150-157`), gated by `_hasShownFirstMinimizeNotification` (`:23`). The port
reproduces the **code**: first duck of the process fires that balloon with that title, that
text and that 2000 ms timeout; every later duck fires none. `NIF_INFO` on `NIM_MODIFY`.

## How the restore path is verified, and why that is strong enough for what ships

| Route back | How it is proved | Residue |
|---|---|---|
| Close the DTRH window | real coordinator `FlowEnded` → `ShellTray.Restore()` on a real Avalonia window (headless) | none at draw level |
| Taskbar button | never removed — the shell is minimized, never hidden; pinned by a fact asserting `IsVisible == true` after a duck on **both** the Available and the Unavailable branch | headed: that Windows really draws the button |
| Left-click the icon | SP-093's instrument posts the **shell's own callback message** to the **real owner window** and pumps it, so the real `WndProc` runs and `Activated` fires; SP-096 adds the wiring fact `Activated → shell restored` | headed: a human's click on a rendered icon |
| Menu → `Show Dashboard` | the **OS** is the oracle: `TrayNativeHandles` gains the `HMENU`, and `TrayShellProbe` grows its own independent `GetMenuItemCount` / `GetMenuStringW` / `GetMenuItemID` / `GetMenuItemInfoW` declarations (second copy, same rule as the NIM_MODIFY oracle) and reads back 4 entries, their labels, their ids and the separator at index 2. Dispatch is proved by driving the same `InvokeCommand(id)` the tracker's return value feeds, through a real synthetic `WM_RBUTTONUP` with the tracker seam substituted | headed: `TrackPopupMenu`'s modal loop and the human's right-click |

`TrackPopupMenu` is behind a `Func<nint,uint>` seam whose **product default is the real
`TrackPopupMenu`**, so the seam isolates exactly the one uninstrumentable OS call and nothing
else. Everything on either side of it — the shell notification, the real window proc, the real
OS-held menu, the id-to-action dispatch, the window restore — is exercised for real.

That verification would have been strong enough to justify a tuck. It is the window topology,
not the verification, that refuses.

## The `Unavailable` branch (every non-Windows run) — what it does and how it is proved

`TrayPresenceFactory.CreateFor(Linux)` → `UnsupportedTrayPresence`, which refuses `Place`,
refuses the new `SetMenu`, refuses `ShowNotification`, never reports `IsPlaced`, never raises
`Activated`. `ShellTray` then: plain minimize, **no** icon, **no** balloon, taskbar button
kept, one diagnostic line carrying the typed reason code. Identical window outcome to the
Available branch — which is the point: on this port the tray is **additive**, never
load-bearing, so the branch Linux users get is the branch everyone gets plus/minus an icon.

Proof: a headless fact drives `ShellTray` with a real `UnsupportedTrayPresence` and asserts
`IsVisible == true`, `WindowState == Minimized`, no balloon, no placement claim; and the
`LinuxManualGate` text stays named and untouched.

## Bite test (step 6)

Mutation: make `ShellTray.Duck()` call `shell.Hide()` on the Unavailable branch. Expected red:
the Unavailable headless fact (`IsVisible` false) **and** the Available one. Restored
byte-identically, not committed. A second seeded mutation — dropping one menu item — must red
the OS-readback fact.

## Files I intend to touch (all inside File Scope)

- `client/src/CcpClient.Desktop/Tray/TrayMenu.cs` (new) — `TrayMenuItem`, `TrayMenu`.
- `client/src/CcpClient.Desktop/Tray/ITrayPresence.cs` — `SetMenu`, `ShowNotification`.
- `client/src/CcpClient.Desktop/Tray/Win32TrayPresence.cs` — HMENU build + OS readback confirm, `WM_RBUTTONUP` → tracker seam → dispatch, `NIF_INFO` balloon, menu handle on `TrayNativeHandles`.
- `client/src/CcpClient.Desktop/Tray/Win32TrayInterop.cs` — menu/notification P/Invokes and constants.
- `client/src/CcpClient.Desktop/Tray/UnsupportedTrayPresence.cs` — refuse the two new members.
- `client/src/CcpClient.Desktop/Tray/TrayReasonCodes.cs` — a code for "menu not built".
- `client/src/CcpClient.Desktop/Views/ShellTray.cs` (new) — the shell-side duck/restore + tray owner.
- `client/src/CcpClient.Desktop/Views/MainWindow.axaml.cs` — build the `ShellTray` (companion + exit actions) and hand it to `DtrhLaunch`.
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhLaunch.cs` — `DuckOwner`/`RestoreOwner` delegate to `ShellTray`; the D20 comment block rewritten to the measured reason.
- `client/tests/CcpClient.Tests/TrayCapabilityTests.cs`, `TrayObservations.cs`, `TrayShellProbe.cs` — menu/balloon facts + the OS menu oracle.
- `client/tests/CcpClient.HeadlessTests/ShellTrayHeadlessTests.cs` (new) — duck/restore on a real window, the Unavailable branch, the Avalonia ownership propagation pins (M1/M2/M4/M5).
- `client/docs/wpf-surface-reachability.md` — §12, continuing at D35.
- `spine-tasks/SP-096-tray-menu-and-tuck/{plan.md,record.md,floor-delta.json}`.

Nothing under `client/tests/floor/`, `client/docs/task-board.md`, `client/tools/**`,
`Features/Intake/**`, `Entitlement/**`, `ConditioningControlPanel/**`.

## Estimated floor delta

Unit `+9..12`, headless `+6..8`. Declared exactly in `floor-delta.json` at implementation.

## Open question I am NOT deciding alone

If the owner would rather have WPF's literal tuck at the cost of unowning the descent window
(and therefore of WPF's own stated "main lands on top of the game" defect), that is an owner
call, not mine. I am recording the trade, not making it.
