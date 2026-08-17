# SP-093 — A tray capability, honest on the platform that cannot have one yet

## Mission

When WPF opens the DTRH window it **minimizes the main window to the tray with no notification**, and restores it from the tray when that window closes (`ConditioningControlPanel/Services/Chaos/DtrhHostService.cs:156,998`). That is user-observable behaviour, so it is parity the port owes.

The port has **no tray code at all** — verified: a search of `client/src` for tray, notify-icon and minimize-to-tray returns nothing, and the nearest hit is Intake's own disclaimer at `Features/Intake/IntakeHostWindow.axaml.cs:131`, *"Plain MainWindow minimize (explicitly NOT tray tuck)"*.

Your outcome: **a tray capability with a working Windows implementation and an honest typed refusal wherever it cannot work — never a no-op that compiles.**

## Dependencies

Board row: "Tray icon is a platform capability the port does not have, and DTRH launch parity depends on it", P1, OPEN. **You do not edit the board.** SP-006's truthful runtime capability contract is the shape you must obey. No dependency on SP-091 or SP-092 — you register nothing and wire nothing (see Wiring).

## THE TRAP THAT DECIDES THE DESIGN, named at authoring

**A tray capability is the easiest thing in this codebase to fake.** Every failure mode is invisible: an icon that never appears, a restore that never fires, a Linux backend that swallows the call and returns success. The suite would be green, the build would be clean, and the behaviour would be absent. The constitution names this exact class: *a stub, a no-op fallback, or a Windows-only test never proves cross-platform support.*

So the disqualifying design is **any path that reports success without having done the thing.** If a backend cannot place an icon, the honest outcome is a typed `Unavailable` with the reason, and the caller decides — which for DTRH means falling back to a plain minimize, visibly and deliberately, exactly as Intake already does and documents.

The second trap: **do not let "hide the window" masquerade as "tuck to tray".** WPF's behaviour removes the window from the taskbar and gives the user an icon to bring it back. A hide with no icon strands the user with a running app they cannot reach, which is worse than not implementing it. If you cannot deliver the icon, deliver the plain minimize and say so.

## Linux, named up front rather than discovered

Linux tray is StatusNotifierItem/AppIndicator over DBus, and Wayland has no XEmbed tray at all. **This machine probably cannot discharge it**: the port's own WSLg observations already record that `_NET_CLIENT_LIST` is absent on the XWayland root, so there is no reliable way to prove a tray icon exists there from this box.

That is expected and it is not a failure. **The honest outcome is a `BLOCKED` or `WIP` line in `record.md` naming the exact manual gate a Linux box would have to run.** Do not fake it, do not skip a test to hide it, and do not claim Linux support because the code compiles for `linux-x64`.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Tray/**` (new), `client/tests/CcpClient.Tests/Tray*`, and `spine-tasks/SP-093-tray-capability/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/src/CcpClient.Desktop/Navigation/**`, `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/Entitlement/**`, `client/src/CcpClient.Desktop/App.axaml.cs`, `client/src/CcpClient.Desktop/Features/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Wiring: deliberately none

You produce a capability and its tests. **You register nothing and you change no window's behaviour**, because SP-091 owns the shell files in this wave. Per A-014 this is **infrastructure only** — say so in `record.md`, and do not claim DTRH's minimize-to-tray parity is delivered. It is delivered when a later packet consumes this.

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-093-tray-capability/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Tray` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/src/CcpClient.Desktop/Navigation/**`, `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/Entitlement/**`, `client/src/CcpClient.Desktop/App.axaml.cs`, `client/src/CcpClient.Desktop/Features/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-093-tray-capability/record.md`, `spine-tasks/SP-093-tray-capability/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`**, never from this packet. Your gate reports `observed == pin + your declared delta`.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. Confirm the absence yourself and report it: search `client/src` for tray/notify-icon/minimize-to-tray and state what you find. Read `Features/Intake/IntakeHostWindow.axaml.cs:131` for the existing plain-minimize precedent you must not duplicate differently.
2. Establish what Avalonia 12.1.1 actually offers here. **Do not guess the API** — verify against the packaged assemblies or the docs available to you, and record the version and the exact type you are using. If the framework's own support is partial, that is a finding to record, not to route around.
3. Design the typed capability: at minimum **Available**, and **Unavailable(reason)** distinguishing at least "platform has no tray mechanism" from "the mechanism exists but refused".
4. Implement Windows. Prove the icon is really placed and really removed, not merely that a method returned.
5. Implement the non-Windows answer as an honest typed `Unavailable`, and name the manual Linux gate in `record.md`.
6. **Prove it bites:** make the Windows backend return `Available` without placing an icon, in a scratch edit, and confirm a test reds. If no test reds, your tests are proving the method call rather than the effect, which is this packet's whole trap. Restore byte-identically and verify. Do not commit the mutation.
7. Answer in `record.md`: what should a caller do when the tray is `Unavailable`? Name the fallback and why it is better than hiding the window.

## Completion Criteria

- Typed capability, honest on both platforms, with reasons.
- A Windows test that fails when the icon is not actually placed.
- A `BLOCKED`/`WIP` line naming the exact Linux manual gate, with no test skipped to conceal it.
- `record.md` states the infrastructure-only status and the recommended `Unavailable` fallback.
- Build 0 warnings / 0 errors.

## Do NOT

- Return success from any path that did not place an icon.
- Ship a silent no-op backend for Linux or macOS.
- Hide a window without giving the user a way back to it.
- Claim cross-platform support because it compiles, or because a Windows test passed.
- Register or wire this capability, or change any window's close/minimize behaviour.
- Introduce a wall-clock wait. Use the shared `TestWait` helper.
- Claim `presentation-verified` for anything. A headed capture is the orchestrator's to run.

## Git Commit Convention

Conventional commit, `feat(SP-093): ...`. Create `.DONE` as your last action and do NOT commit it.

## Documentation Requirements

`record.md` only. The orchestrator writes the board and the digest at land.
