# SP-095 — The doors that are still command-line only

## Mission

Waves 36 and 37 made two surfaces reachable: the Loom and the DTRH card. Three landed subsystems are still reachable **only by typing a CLI flag**: Graded Intake (`--intake-demo`), the Chaos tunnel backdrop (`--tunnel-demo`), and the AvatarTube demonstrator (`--avatartube-demo`).

Your outcome: **every landed surface is either reachable by a real user gesture, or has a recorded reason it is not a user surface at all.**

That second clause is not an escape hatch — it is half the work. Read the Decision section before you plan.

## Dependencies (all LANDED — consume, do not rebuild)

- **The shell** (SP-091, SP-094): `Navigation/ShellRoutes.cs`, `ShellRouter`, `ShellRouteBinding.ValidateOrThrow`, `Views/Pages/*`. Four doors exist: Studio, Companion, Play, System. You add routes the same way.
- **The single-construction-site pattern**: `Navigation/LoomLaunch.cs` and `Features/Dtrh/DtrhLaunch.cs`. Every launcher has exactly one construction site and the CLI demo flag routes through it rather than building its own. Follow this; it is now the port's convention and two waves depend on it.
- **Graded Intake**: `Features/Intake/IntakeLaunchCoordinator.cs`, `IntakeHostWindow`. Already working under `--intake-demo`.
- **The chaos tunnel**: `Features/Chaos/ChaosTunnelDemoDrive.cs` and its service. Already working under `--tunnel-demo`.
- **AvatarTube**: `Features/AvatarTube/AvatarTubeDemonstratorWindow.axaml`.

## THE DECISION YOU MUST MAKE AND JUSTIFY, per surface

**A door is owed only where WPF has a user surface.** Inventing a door for something WPF does not expose as a destination is not parity, it is decoration — and SP-091's trap ("a route with no working destination must not appear in the rail as though it did") applies to routes with destinations nobody in WPF can reach either.

For **each** of the three, decide and defend in your plan:

1. **Graded Intake.** WPF exposes it: the rail's `BtnNavGradedIntake` sub-entry, tooltip "Graded Intake" (`client/docs/wpf-surface-reachability.md` §8.1). **This one almost certainly earns a door.** Confirm against the survey and the source.
2. **The chaos tunnel.** The port's own code says `Features/Chaos/ChaosTunnelDemoDrive.cs:12-13`: *"the greenfield dashboard has no Chaos game entry point."* Find out what WPF actually does — is the tunnel a destination a user navigates to, or a backdrop that appears underneath another surface? **If it is a backdrop, it does not get a door, and the comment gets corrected rather than satisfied.**
3. **AvatarTube.** In WPF the companion window is **not** opened from a dashboard gesture at all — it appears at startup when `AvatarEnabled` is true, and is toggled from the Companion page's hero card (survey §5). The port already has a Companion door. **So the honest answer may be that AvatarTube is a demonstrator, not a user surface, and the reachability question is already answered by the Companion door.** Decide, and if it gets no door, say why in the divergence record.

**A packet that adds three doors because three flags exist has misunderstood the task.**

## Context to Read First

- `client/docs/wpf-surface-reachability.md` — §1 (tiles navigate, never launch), §5 (the companion), §8 (the live survey and the six-door WPF rail), §9-§10 (the twenty-four divergences already recorded).
- `client/docs/evidence/wpf-ui-v681/*.jpg` — captures of the real app.
- `Views/Pages/StudioPage.axaml` and `PlayPage.axaml` — your pages are siblings; match their shape rather than inventing a third idiom.

## THE TRAP, named at authoring

**The cheap wrong answer is three doors that each open a demonstrator.** A demonstrator flag exists to produce evidence on a developer's machine; it is not a product surface, and wiring one to a rail door dresses harness scaffolding as a feature. If a surface's only current entry is a demo flag *because nobody has built its real entry yet*, the honest outcome is a door to a real destination — not a door that runs the demo.

Second trap: **do not let a new door break the capture harness.** `client/tools/verify/capture.ps1` now derives the rail door set from the running app and demands a probe per door (fixed 2026-08-18 after a hard-coded list went blind to SP-094's new door). Adding a door should widen it automatically. **Run it and confirm that; do not edit it.**

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Navigation/**`, `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/Features/Intake/**`, `client/src/CcpClient.Desktop/Features/Chaos/**`, `client/src/CcpClient.Desktop/Features/AvatarTube/**`, `client/src/CcpClient.Desktop/App.axaml.cs`, `client/src/CcpClient.Desktop/Lifecycle/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-095-remaining-doors/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `client/src/CcpClient.Desktop/Features/Dtrh/**`, `client/src/CcpClient.Desktop/Entitlement/**`, `client/src/CcpClient.Desktop/Tray/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-095-remaining-doors/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Navigation` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `client/src/CcpClient.Desktop/Features/Dtrh/**`, `client/src/CcpClient.Desktop/Entitlement/**`, `client/src/CcpClient.Desktop/Tray/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-095-remaining-doors/record.md`, `spine-tasks/SP-095-remaining-doors/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`** (1100 unit / 48 headless as of wave 37), never from this packet.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. Investigate all three surfaces against WPF and report, at the plan checkpoint, **which earn a door and which do not, with your reason for each**. Expect the answer to be fewer than three.
2. For each door you add: route, page, and a working destination reached by a real gesture from a cold start with **no CLI arguments**.
3. Route each CLI demo flag through the same launcher the gesture uses. One construction site per surface.
4. Prove each new route headlessly with real input, the way `NavigationShellHeadlessTests` and `PlayPageHeadlessTests` do.
5. **Prove it bites:** break one new route's wiring in a scratch edit, confirm its test reds and that the others do not. Restore byte-identically. Do not commit the mutation.
6. Run `pwsh client/tools/verify/self-test.ps1` and confirm the derived door check counts your new doors without being edited. Report the door list it prints.
7. Record every divergence in `wpf-surface-reachability.md` §10, continuing the D-numbering (the last is D24).
8. Correct `ChaosTunnelDemoDrive.cs:12-13` if your investigation makes it stale.

## Completion Criteria

- Every landed surface is reachable by a gesture **or** has a recorded reason it is not a user surface.
- No door opens a demonstrator.
- Each new route proven by real input from a cold start; the bite test reds only its own route.
- `self-test.ps1` passes and reports the new door count without being edited.
- Build 0 warnings / 0 errors.

## Do NOT

- Add a door per demo flag.
- Wire a rail door to a demonstrator window.
- Touch `client/tools/**` — the harness must widen itself, and if it does not, that is a finding to report, not to patch.
- Introduce a wall-clock wait. Use `TestWait`.
- Claim `presentation-verified` for anything; the headed capture is the orchestrator's.

## Git Commit Convention

Conventional commit, `feat(SP-095): ...`. Create `.DONE` as your last action and do NOT commit it.

## Documentation Requirements

`record.md`, plus divergence entries in `client/docs/wpf-surface-reachability.md`.
