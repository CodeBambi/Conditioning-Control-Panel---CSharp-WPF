# SP-096 — The tray menu, and the tuck that was refused without one

## Mission

WPF minimizes the main window to the tray when DTRH opens, and restores it when that window closes. The port does not: SP-093 landed the tray **icon** capability with no menu, and SP-094 deliberately chose plain-minimize instead of tucking, because **a tuck built on an icon with no right-click menu strands the user** — worse than not tucking at all.

Your outcome: **the tray menu that makes the tuck safe, then the tuck.**

This needs no owner decision. It is user-visible WPF behaviour the port is missing.

## Dependencies (LANDED — consume, do not rebuild)

- `Tray/ITrayPresence`, `TrayPresenceFactory`, `Win32TrayPresence` (SP-093). Windows places a real icon confirmed by a `Shell_NotifyIcon` round-trip; non-Windows returns a typed `Unavailable` carrying `LinuxManualGate`. **Nothing consumes any of it today — you are its first caller.**
- `Features/Dtrh/DtrhLaunch.cs` `DuckOwner`/`RestoreOwner` (SP-094), currently a plain minimize, reusing `Features/Intake/IntakeHostWindow.axaml.cs:134-162`.

## WPF ground truth

- The tuck: `Services/Chaos/DtrhHostService.cs:156` on launch, `:995-998` on close.
- The menu: `Services/Notifications/TrayIconService.cs:96-119` — **four items**, including the companion wake entry, which `client/docs/wpf-surface-reachability.md` §5 records as **one of the three ways a user reaches the companion** and the only one that works while the main window is tucked away.
- **The balloon fact, and do not take the comment for it:** `MainWindow/MainWindow.RemoteControl.cs:1515` says "no notification", but the call reaches the same `MinimizeToTray()` whose **first-ever invocation fires a balloon** (`TrayIconService.cs:152-157`). Reproduce the code, not the comment. This is already recorded as D20.

## THE TRAP, named at authoring

**A tuck whose way back you cannot exercise is the thing SP-094 refused to ship, and adding a menu does not automatically fix it.** `TrackPopupMenu` needs a real click on a real icon, which no headless test can drive. So if your menu's only proof is that a method returned, you have rebuilt the exact hazard with more code.

**What must be true before the tuck ships:** the restore path is reachable by something you can actually verify, and the taskbar button is never the only way back *and never removed without a replacement*. If you cannot verify the menu end-to-end, **say so and do not tuck** — SP-094's option (b) remains available and is not a failure.

Second trap: **`ITrayPresence` is Windows-only in practice.** `UnsupportedTrayPresence` refuses on Linux. A tuck wired to a capability that refuses means the shell must behave correctly when the tray is `Unavailable` — plain minimize, never a hide with no icon. **Prove that branch, because it is the one Linux users get.**

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Tray/**`, `client/src/CcpClient.Desktop/Features/Dtrh/**`, `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/Lifecycle/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-096-tray-menu-and-tuck/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `client/src/CcpClient.Desktop/Entitlement/**`, `client/src/CcpClient.Desktop/Features/Intake/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-096-tray-menu-and-tuck/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Tray` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `client/src/CcpClient.Desktop/Entitlement/**`, `client/src/CcpClient.Desktop/Features/Intake/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-096-tray-menu-and-tuck/record.md`, `spine-tasks/SP-096-tray-menu-and-tuck/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`** (1110 unit / 54 headless), never from this packet. **The gate now refuses to run against a stale build**, so build the solution before running it.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. Report at the plan checkpoint: the menu items you will add, how the restore path is verified, and **whether that verification is strong enough to justify tucking at all**. An honest "not yet, so no tuck" is a valid plan.
2. Add the menu surface to `ITrayPresence`. At minimum: restore the window, and exit. Add the companion wake entry if the port's Companion surface supports it.
3. Wire the tuck on DTRH host open and the restore on close, **only if step 1 justified it**.
4. **Prove the `Unavailable` branch**: when the tray refuses (every non-Windows run), the shell plain-minimizes and never hides without a way back.
5. **Prove the first-minimize balloon fact** matches the code rather than WPF's comment.
6. **Prove it bites:** make the tuck hide the window while the tray reports `Unavailable`, and confirm a test reds. Restore byte-identically; do not commit the mutation.
7. Record divergences in `wpf-surface-reachability.md`, continuing from D34.

## Completion Criteria

- A tray menu exists with a working restore item, or a recorded reason the tuck still cannot ship.
- The `Unavailable`/non-Windows branch never hides a window without a way back, and a test proves it.
- No test skipped to conceal the headed limit; the Linux manual gate stays named.
- Build 0 warnings / 0 errors.

## Do NOT

- Ship a tuck whose restore you cannot verify.
- Hide a window when the tray is `Unavailable`.
- Reproduce WPF's "no notification" comment over its code.
- Introduce a wall-clock wait. Use `TestWait`.
- Claim `presentation-verified` for anything; the headed capture is the orchestrator's.

## Git Commit Convention

Conventional commit, `feat(SP-096): ...`. Create `.DONE` as your last action and do NOT commit it.

## Documentation Requirements

`record.md`, plus divergence entries in `client/docs/wpf-surface-reachability.md`.
