# SP-091 — A navigation shell, and the first landed surface a user can actually reach

## Mission

The port has substantial working subsystems and **no way to reach any of them**. Its entire user-facing shell is a 93-line demonstrator window holding one demo card, one button, and diagnostic text. Six landed surfaces are reachable only by typing a CLI flag.

Your outcome: **a navigation shell with a rail and pages, and THE LOOM reachable by a real user gesture from a cold start with no command-line arguments.**

The Loom is chosen deliberately as the first real route because it is the only landed surface with **no entitlement gate anywhere on its WPF path** (verified: `Services/Chaos/LoomHostService.cs:30-77` has no tier check; the rack entry carries none). DTRH is Tier-2 gated and its gate lands in SP-092; **you do not wire a DTRH launch button** (see Do NOT).

## Dependencies

Board row: "Dashboard entry points for landed surfaces", P1, OPEN. **You do not edit the board.** The landed Loom window (`Features/Dtrh/DtrhLoomWindow.axaml`) and its host are already built and proven by `--loom-demo`; you are giving them a door, not rebuilding them.

## The owner decision that sets your design freedom, and its one condition

**Owner, 2026-08-18, asked which WPF page the port's dashboard corresponds to, answered "Neither, improve on both."** Combined with the standing goal (*"we dont care how it is done under the hood in avalonia. but it should keep the same behaviour as the wpf build"*), "same behaviour" is settled as **reach the same destinations in the same states — NOT reproduce the same page topology.**

So the layout is yours to design. **What is NOT yours to change is the state grammar**, because that is what a user observes:

- a gated control is **present and readable and takes the click**, then refuses out loud. It is never a dead greyed control that swallows the gesture.
- a door the server has not opened is **collapsed, not locked** — a lock advertises something buyable, and that distinction is doctrine in the WPF source (`MainWindow/MainWindow.PlayTab.cs:117-125`).
- an active feature reads as active.

**The condition attached to the freedom: every divergence from `client/docs/wpf-surface-reachability.md` is written INTO that document, in the same commit that creates it.** An unrecorded divergence is indistinguishable from a bug. This is the one doc you may edit.

## The design target, observed live rather than inferred

The orchestrator drove the **running shipping v6.8.1 app** and captured it. `client/docs/wpf-surface-reachability.md` **§8** records the survey and `client/docs/evidence/wpf-ui-v681/*.jpg` are the captures. Read both. The owner's instruction was explicit: *"since the new ui in wpf has improved a lot you can take insparation from it."*

**The shape to take inspiration from is the Studio rack, and the app states it in its own words:**

> The dashboard popups are gone. Flashes, subliminals, bubbles, bouncing text and the rest are all rows in the list down the left. Left-click a row to open its panel. Right-click it to flip that effect on or off without opening anything at all. **The dot on each row is live: at a glance you can see everything that is currently running.** Dashboard tiles land here too, on the module you clicked. Same dials, one room.

That is a **list with grouped rows, a live state dot per row, and one panel** — far cheaper to build well in Avalonia than a bespoke mosaic of art tiles, and it is where the product itself has moved. It also preserves the gesture grammar exactly: left-click opens, right-click toggles.

**Observed rail: six doors**, by `AutomationId` — `DoorHome`, `DoorStudio`, `DoorCompanion`, `DoorPlay`, `DoorYou`, `DoorLibrary`. **Observed rack: four groups, fifteen rows** (EFFECTS / GAMES & CARDS / IMMERSION / TIMING; full list in §8.3).

**A NAME COLLISION YOU MUST NOT WALK INTO, found by the survey:** the rail's **"The Spiral"** (`BtnNavSpiral`, tooltip *"Where your descent is drawn"*) is **THE DESCENT** — a day-by-day tracker page — **not** the Spiral Overlay effect. The Loom lives on the **Spiral Overlay** module inside the Studio rack. Two surfaces, near-identical names. Routing "spiral" to one of them is wrong for the other.

**Your verified target route** (§8.4, confirmed at runtime, not only in source): `DoorStudio` -> rack row **Spiral Overlay** -> module panel -> the full-width outlined button whose UIA name is exactly `THE LOOM — weave your own spiral`. On that panel the button sits below the settings card (Opacity, Randomize spiral, Display monitor) and above the SPIRAL LIBRARY card. It is **not gated and not session-locked**, confirmed live on an entitled account.

## Context to Read First

Verified by the orchestrator at authoring, at HEAD `7679d5a3`:

- `client/docs/wpf-surface-reachability.md` — **read this first and completely, §8 included.** It is the cited, v6.8.1-anchored evidence base for every route, gesture and state below. It was written because this row's original premise was wrong.
- `client/src/CcpClient.Desktop/Views/MainWindow.axaml` (93 lines) and `MainWindowViewModel.cs` (124 lines) — the whole current shell. Note `MainWindow.axaml:53`: the demo card's own text says *"not a product feature; the first real feature card supersedes it."* **You are that supersession.** Retire the demo card; keep the SP-003/SP-006 startup-trace and capability proofs (they may shrink and move, never be deleted — that is a standing rule on that markup).
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhLoomWindow.axaml` and its host wiring — what your button must open. Find the existing launch path used by `--loom-demo` and **call it**; do not write a second one.
- `client/src/CcpClient.Desktop/App.axaml.cs` and `Program.cs` — the composition root. `App` and `MainWindow` are constructor-injected and deliberately have no parameterless constructor (AVLN3001 is suppressed for this reason). Respect that; do not introduce a runtime-XAML-loaded window.
- `client/docs/architecture.md` A-012 (Avalonia styling: selectors and pseudo-classes, never WPF triggers) and A-014 (unwired code is infrastructure only).

## THE TRAP THAT DECIDES THE DESIGN, named at authoring

**The cheap wrong answer is a shell that navigates between pages which are all empty except the one you wired.** It would demo beautifully, pass every test you would think to write, and deliver nothing: the row's whole point is that landed surfaces are unreachable, and a rail of dead doors is the same unreachability with better decor.

So: **a route that has no working destination this packet must NOT appear in the rail as though it did.** Either its destination works, or the door is honestly absent or honestly marked as not-yet-built. A door that looks live and goes nowhere is disqualified, and "it is a placeholder" is not a defence — the port already has six of those and they are called demonstrator flags.

The second cheap answer is **testing the navigation model and not the navigation.** A unit test proving `Router.Navigate("spiral")` sets `Current == "spiral"` is a test of a dictionary. What must be proven is that a **gesture** reaches a **destination**.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Navigation/**` (new), `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/App.axaml.cs`, `client/tests/CcpClient.HeadlessTests/**`, `client/tests/CcpClient.Tests/Navigation*`, `client/tools/verify/**` (**added by amendment 1**), `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-091-navigation-shell/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/src/CcpClient.Desktop/Entitlement/**`, `client/src/CcpClient.Desktop/Tray/**`, `client/src/CcpClient.Desktop/Features/**`, `client/src/CcpClient.Desktop/Program.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

`client/src/CcpClient.Desktop/Features/**` is **read-only for you**: the Loom host already works and you are calling it, not changing it. If you believe you must change it, that is a stop condition — report it instead.

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-091-navigation-shell/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Views` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/src/CcpClient.Desktop/Entitlement/**`, `client/src/CcpClient.Desktop/Tray/**`, `client/src/CcpClient.Desktop/Features/**`, `client/src/CcpClient.Desktop/Program.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-091-navigation-shell/record.md`, `spine-tasks/SP-091-navigation-shell/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`**, never from this packet. Your gate reports `observed == pin + your declared delta` and exits non-zero on that drift; that is the designed state for a bound lane.

## AMENDMENTS (orchestrator, at the plan checkpoint)

### Amendment 1 — the verify harness comes WITH you. Option (a), not (b).

You found that retiring the demo card breaks `client/tools/verify/**` and proposed to proceed on (b), leave it broken and record it. **Overruled: take option (a).** `client/tools/verify/**` is added to your File Scope and re-anchoring it is now a completion criterion.

**Why (b) is not survivable, in this packet's own terms.** That harness is the port's tier-2/tier-3 verification: it is what turns a `presentation-verified` claim from an assertion into evidence, and it is the only way anything you build gets looked at rather than merely tested. Leaving it broken means a `FAIL` from `capture.ps1` would no longer distinguish a broken app from a broken harness — **which is precisely the failure this harness suffered hours ago** (a rotted fixed sleep made it report `window not found` on a perfectly healthy app, and the honest reading of that output was "the shell is broken"). Knowingly re-introducing that ambiguity to stay inside a scope line is the wrong trade, and a scope line is the cheaper thing to move.

Re-anchor in the SAME commit as the retirement, so the tree is never in a state where the harness is broken:
- `capture.ps1:11` `ValidateSet` and `:101` UIA needles, `:131` surface branch — move off `Demo: Status Ticker` / `layout-probe: card` onto the shell's own anchors. `dashboard-card` -> `rail-door`, `lit` -> `selected` is the right rename; keep `dashboard` as the whole-window surface.
- `self-test.ps1:15,34,37,40,54,57` and the CcpVerify manifest's named check `dashboard-card-lit-border` follow the same rename.
- **`self-test.ps1` must still PASS at the end** — its seeded regression must red the specific named check and the restore must go green. That is your proof the re-anchor is real and not merely renamed strings. Run it and report its output.
- Keep the polled-window wait at `capture.ps1` as it is now. Do not reintroduce a fixed sleep.

### Amendment 2 — your dot/toggle omissions are RIGHT, your reason for them is WRONG

You cited `StudioTabView.xaml.cs:494-496` ("A dot that cannot be wired honestly is omitted") and `:657-660` (toggle-less rows' right-clicks "fall through unhandled") as WPF parity for a rack row with no live dot and no right-click toggle. **I verified both citations: they are exact. But both describe `Visuals`, the ONE row that has no master toggle.** Every other row passes a state lambda — `Add("spiral", ..., () => App.Settings?.Current?.SpiralEnabled)` at `:490-491` — so **in WPF the Spiral Overlay row DOES carry a live dot and DOES toggle on right-click.** The live capture in `client/docs/evidence/wpf-ui-v681/studio-rack-spiral-overlay.jpg` shows lit dots on running rows.

**Keep the omissions** — the port has no spiral-overlay effect whose state could be reported, and inventing a dot that always reads "off" is the fake-available shape. **But record them as DIVERGENCES in `wpf-surface-reachability.md`, not as parity**, and say the true reason: *the port has nothing to wire yet*, not *WPF omits it here*. Generalising a rule written for the one exceptional row into a general parity claim is how a gap gets recorded as a feature.

### Amendment 3 — approvals, so you are not blocked on them

- **The `Present` seam: approved.** Constructing the real `DtrhLoomWindow` and handing it to an injectable presenter is honest, the `FeaturePopupManager` factory seam at `MainWindow.axaml.cs:58-63` is the right precedent, and avoiding real audio init and WebView2 navigation in a headless test is correct. **Condition: the test must assert the concrete `DtrhLoomWindow` type is what reaches the seam**, so the seam can never be satisfied by a stand-in.
- **The `LoomLaunch` single-launcher lift: approved, and it is the best part of your plan.** One construction site, both callers routed through it, the four diagnostic log strings byte-identical. That is reuse rather than a second path, which is exactly what was wanted.
- **`MainWindowViewModel` surviving as an A-014 residue: accepted.** Nine call sites in test files outside your scope. Retire the card, keep the class, name it in `record.md`.
- **Three doors with real destinations, and Home/Play/You/Library/The Spiral honestly absent: approved.** That is the packet's trap answered correctly.
- **Taking the live UIA name (no emoji) over the XAML `Content` string: correct.** Observation wins over source where they disagree.

### Amendment 4 — declared delta

Your declared **unit +3, headless +2** stands. Retiring 8 headless facts while adding 10 nets +2; retargeting their mechanism proofs onto product controls rather than dropping them is the right call and must be visible in `record.md`.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. Read the reachability doc and the current shell. Report what you measure: how `--loom-demo` reaches the Loom today, and the exact call you will reuse.
2. Design the shell. State your route set and **justify every door against the trap above** — for each, name its working destination or say why the door is absent. Fewer honest doors beats more decorative ones.
3. Build the navigation model and the shell chrome. Avalonia selectors and pseudo-classes for state, never WPF-style triggers.
4. Make the Loom reachable: a real gesture, from a cold start, with **no command-line arguments**.
5. Retire the demo card. Preserve the startup-trace and capability-state proofs.
6. **Prove the gesture, not the model.** A headless Avalonia test that dispatches the actual input on the actual control and observes the launch request reaching the Loom host seam. A test that calls the view-model method directly does not discharge this.
7. **Prove it bites:** break the route wiring in a scratch edit and confirm your test reds. Restore byte-identically and verify. Do not commit the mutation.
8. Record every divergence from `wpf-surface-reachability.md` into that document, in this commit.

## Completion Criteria

- From a cold start with no arguments, a user gesture reaches the Loom window.
- No door in the rail lacks a working destination.
- The demo card is gone; the SP-003/SP-006 proofs survive.
- A headless test drives real input and fails when the wiring is broken.
- Divergences are in `wpf-surface-reachability.md`, not only in `record.md`.
- Build 0 warnings / 0 errors.

## Do NOT

- **Wire a DTRH launch button.** DTRH is Tier-2 gated in WPF and fails closed with no entitlement service; the port has none until SP-092. An ungated DTRH button hands out paid content, and a stubbed always-allowed gate is the fake-available shape the truthful-capability contract bans.
- Add a door whose destination does not work.
- Touch `Features/**`, the floor pin, or the board.
- Introduce a wall-clock wait. Use the shared `TestWait` helper; `Thread.Sleep`, bare `Task.Delay` and tick-count polls fail the timing guard.
- Leave a TODO. If something cannot be finished, it is a `record.md` limit with a named gate, never a comment.
- Claim `presentation-verified` for anything. Headless frames are `draw-verified`; composited pixels, geometry, scaling, occlusion and z-order need a headed capture the orchestrator runs.

## Git Commit Convention

Conventional commit, `feat(SP-091): ...`. Create `.DONE` as your last action and do NOT commit it.

## Documentation Requirements

`record.md`, plus divergence entries in `client/docs/wpf-surface-reachability.md`. The orchestrator writes the board and the digest at land.
