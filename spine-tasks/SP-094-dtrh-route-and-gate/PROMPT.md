# SP-094 — The Play door, the DTRH card, and a gate that refuses honestly

## Mission

The port's flagship surface is unreachable. `DtrhLaunchCoordinator`, `DtrhSlotPickerWindow` and `DtrhHostWindow` are all landed and working, and the only way to reach them is `--dtrh-demo` on a command line.

Your outcome: **a user can walk from the port's rail to Down The Rabbit Hole and fall in — and when they are not entitled, the card refuses out loud instead of lying or going dead.**

This is the wave-36 shell's second real destination and the port's most important single route.

## Dependencies (all LANDED — you consume, you do not rebuild)

- **The navigation shell** (SP-091): `Navigation/ShellRoutes.cs`, `ShellRouter`, `ShellRouteBinding.ValidateOrThrow`, `Views/Pages/*`. You add a route the same way Studio/Companion/System are added.
- **The entitlement capability** (SP-092): `Entitlement/HostLoginEntitlement.cs`, `EntitlementOutcome` = `Entitled(tier)` | `NotEntitled` | `Unavailable(reason)`. **Nothing wires it today. You are its first consumer.**
- **The tray capability** (SP-093): `Tray/ITrayPresence`, `TrayPresenceFactory`, `Win32TrayPresence`. **Nothing wires it today.** See the tray section — it has a hole you must not fall into.
- **The DTRH launch path**: `Features/Dtrh/DtrhLaunchCoordinator.cs` — `LaunchWithPickerAsync` (the FALL IN path, includes the slot picker) and `QuickStartAsync` (the Quick Drop path). **Call these. Do not write a second launcher** — SP-091 established `LoomLaunch` as the single-construction-site pattern; follow it.

## Context to Read First

- `client/docs/wpf-surface-reachability.md` **§3 and §8.5** — the DTRH route, the gate, the lock band, the slot picker, the tray tuck, and the live capture of the real Play page. `client/docs/evidence/wpf-ui-v681/play-page.jpg` is that page.
- `client/docs/task-board.md` — the DTRH-gate row and the tray-parity row. Both contain findings you must honour.
- The Studio page and its rack (`Views/Pages/StudioPage.axaml`) — your Play page is a sibling, built the same way.

## THE THREE TRAPS, named at authoring

### 1. `Unavailable` is not `NotEntitled`, and the UI must not collapse them

SP-092's entire point is that "I could not tell" and "you are not a patron" are different answers. **The UI is where that distinction gets destroyed**, because both look like "no" to a card.

Today the tier authority is unconfigured, so a real run returns **`Unavailable(tier-authority-absent)`** — the port cannot resolve anyone's tier until an owner permission decision lands. **So your gate's `Unavailable` branch is not a rare edge case; it is the ONLY branch a user will hit today.** Get it right or the whole packet is a lie:

- `NotEntitled` -> WPF's behaviour: refuse out loud, naming the tier and offering the upgrade route (`Services/TierGate.cs:128,133`).
- `Unavailable(reason)` -> refuse out loud with an **honest, different message** that says the port could not verify entitlement and why. **Never "you are not a patron". Never a silent no-op. Never a dead control.**
- `Entitled` -> proceed.

### 2. The card must take the click. A disabled button is the wrong answer.

WPF's gated card is **present, fully readable, and still clickable** — a lock band paints over it at ~66% alpha and is `IsHitTestVisible="False"` so the click passes through and gets refused out loud (`Views/Tabs/PlayTabView.xaml:508-512`). A gated click **arrives**.

**Greying the button out is disqualified.** It is the shape that swallows the gesture and tells the user nothing, and it is the opposite of what the app does. The one genuinely disabled part in WPF is the pair of checkboxes, because they write settings through a binding with no handler to refuse in.

### 3. THE TRAY TUCK: do not ship it half-built

WPF minimizes the main window to the tray when DTRH opens and restores it on close. **SP-093 landed the icon capability but NOT a menu**, and the board row on it says plainly: a tuck built on that as it stands **hides the main window and leaves the user an icon that does nothing when right-clicked — strictly worse than WPF, and worse than not tucking at all.**

**You have two honest options and must pick one in your plan:**
- **(a)** Add a menu surface to `ITrayPresence` (at minimum: restore the window, and exit), then tuck. This is the parity answer and it is more work.
- **(b)** Do not tuck. Use a plain minimize, or leave the main window alone, and record the divergence in `wpf-surface-reachability.md` naming what a user sees differently.

**A tuck with no way back is not an option.** Also note, from a code review of SP-093: WPF's DTRH tuck comment says "no-notification variant", but the call reaches the same `MinimizeToTray()` whose **first-ever** invocation fires a balloon (`TrayIconService.cs:152-157`). Do not reproduce the comment; reproduce the code.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Navigation/**`, `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/Features/Dtrh/**`, `client/src/CcpClient.Desktop/Tray/**`, `client/src/CcpClient.Desktop/Entitlement/**`, `client/src/CcpClient.Desktop/App.axaml.cs`, `client/tests/CcpClient.Tests/**`, `client/tests/CcpClient.HeadlessTests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-094-dtrh-route-and-gate/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

**If you touch `client/tools/verify/**` you have gone wrong** — SP-091 re-anchored that harness onto `rail-door`/`selected` and adding a door must not break it. Run it; do not edit it.

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-094-dtrh-route-and-gate/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-094-dtrh-route-and-gate/record.md`, `spine-tasks/SP-094-dtrh-route-and-gate/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`** (it is 1090 unit / 39 headless as of wave 36), never from this packet. Your gate reports `observed == pin + your declared delta`.

## AMENDMENTS (orchestrator, at the plan checkpoint)

### Amendment 1 — tray option (b) APPROVED, and reason 3 is the one that decides it

Do not tuck. Plain minimize and restore, `ITrayPresence` left unwired and stated.

All four of your reasons hold, but **reason 3 is the one I would have overruled you on if you had picked (a)**: option (a) ships a hide-the-window path whose only way back you cannot exercise, because `TrackPopupMenu` needs a real click on a real icon and "left the taskbar and came back" is composited-window evidence. **You would be shipping the stranding the packet disqualifies, and calling it parity on the strength of a test that cannot reach it.** Reason 2 is the honest killer of (a) as a parity claim: WPF's tuck fires a balloon on first use and its menu has four items, so menu-only (a) buys a bigger partial, not a smaller gap.

Reusing `IntakeHostWindow.axaml.cs:120-162` — the port's own landed precedent for exactly this situation, which shipped through six headed runs — is better than inventing a second minimize path. Record it as a divergence naming what a user sees differently.

### Amendment 2 — close the over-grant hole. Board-authorised, and you were right to ask.

Take it. `Enum.IsDefined` plus tests feeding `(EntitlementTier)0` and `(EntitlementTier)99`.

The board row's acceptance already names the wiring packet as the place this closes, `Entitlement/**` is in your File Scope, and **it is the only hole in that capability that leans toward GRANTING paid content rather than refusing** — every other one was closed toward refusal. Asking rather than doing it silently was correct; the answer is yes.

### Amendment 3 — §8.5 IS WRONG AND THE ERROR IS MINE. Fix it, and fix it as a correction, not a divergence.

You are right and I am not. `client/docs/wpf-surface-reachability.md` §8.5 records the title as `THE RABBIT HOLE`; the source says `🐇 DOWN THE RABBIT HOLE` (`Views/Tabs/PlayTabView.xaml:426`) and §3 already agreed. **I wrote §8.5 from a screen capture in which the onboarding card and the tier badge occluded the left of the title, and I transcribed what was visible as though it were what was there.**

That is a real failure of the rule I set myself. I wrote "observation beats source where they disagree" and then applied it to an observation that was **partially hidden**. **An occluded observation is not an observation; it is a guess with a photograph attached.** Your resolution — source wins, emoji stripped per D8 — is correct.

**You are authorised to edit §8.5 directly to fix this**, beyond the divergences-only limit, and to add a one-line note in that section recording why the error happened, so the next reader knows the survey's captures can be occluded and must be cross-checked against source. Keep it short.

### Amendment 4 — scope widened to `Lifecycle/**`: register the capability properly

Your plan constructs `HostLoginEntitlement` in the shell because `Lifecycle/CompositionRoot.cs` was outside scope. **This is a single-lane wave, so there is no sibling to collide with — take the scope.** `client/src/CcpClient.Desktop/Lifecycle/**` is added to your File Scope.

Register the entitlement capability the way the other five are registered, **and give it a capability probe**, so its state appears on the System page alongside `display-session`, `atomic-filesystem` and the DTRH/tunnel probes. That matters for a reason bigger than tidiness: today the honest answer for every user is `Unavailable(tier-authority-absent)`, and **the System page is where this port tells the truth about what it cannot do.** A capability that refuses everyone while being invisible in the one place that reports capability states is the shape the truthful-capability contract exists to prevent.

### Amendment 5 — `--dtrh-demo` reaching past the gate: APPROVED, and say so in the code

Your reasoning is right: gating the headed-evidence path would make DTRH evidence depend on the developer's Patreon tier, which would make the demonstrator useless on exactly the machines that need it. Put that sentence in a comment at the call site so the next reader does not "fix" it.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. Read the reachability doc §3 and §8.5 and the Play page capture. Report the route you will build and **which tray option (a) or (b) you are taking, with your reason**, at the plan checkpoint.
2. Add the Play route and page the way SP-091 added Studio. `ShellRouteBinding.ValidateOrThrow` must still pass — a door with no page is a composition-time throw.
3. Build the DTRH card: title, blurb, `FALL IN`, `Quick Drop`, and the tier badge. The badge wording in the live app is **`PRIME SUBJECT`**, not the literal string "TIER 2" — take the observed wording.
4. Wire `FALL IN` -> entitlement check -> `DtrhLaunchCoordinator.LaunchWithPickerAsync`, and `Quick Drop` -> `QuickStartAsync`. Both refuse before launching when the gate says no.
5. **Prove all three gate branches drive different observable outcomes**, headlessly, with real input: `Entitled` launches; `NotEntitled` refuses with the tier message and does not launch; `Unavailable` refuses with a DIFFERENT, honest message and does not launch. **The Unavailable case is the one a user hits today — give it a named test.**
6. **Prove it bites:** collapse `Unavailable` into the `NotEntitled` branch in a scratch edit and confirm a test reds. Restore byte-identically and verify. Do not commit the mutation.
7. Re-run `pwsh client/tools/verify/self-test.ps1` — adding a rail door must not break the harness SP-091 re-anchored.
8. Record every divergence into `wpf-surface-reachability.md`.

## Completion Criteria

- From a cold start with no arguments, a user gesture reaches DTRH (when entitled) or an out-loud refusal (when not).
- The three gate branches are distinguishable and tested, with `Unavailable` never presented as `NotEntitled`.
- The gated card takes the click; nothing is greyed out that WPF leaves live.
- The tray decision is (a) or (b), implemented and recorded — never a tuck with no way back.
- `self-test.ps1` still passes. Build 0 warnings / 0 errors.

## Do NOT

- Grey out or disable the FALL IN button as the gating mechanism.
- Report `NotEntitled` when the capability said `Unavailable`.
- Write a second DTRH launcher, or duplicate the slot-picker flow.
- Stub the entitlement authority to return `Entitled` so the happy path can be demoed. That is the fake-available shape the capability contract bans, and it would defeat SP-092 entirely.
- Introduce a wall-clock wait. Use `TestWait`.
- Claim `presentation-verified` for anything — a headed capture is the orchestrator's to run.

## Git Commit Convention

Conventional commit, `feat(SP-094): ...`. Create `.DONE` as your last action and do NOT commit it.

## Documentation Requirements

`record.md`, plus divergence entries in `client/docs/wpf-surface-reachability.md`.
