# SP-095 — record

Branch `lane/SP-095-remaining-doors`, base `fbf61b17`.
Plan checkpoint: `spine-tasks/SP-095-remaining-doors/plan.md` (commit `7e9fd86b`).

## Outcome

Three subsystems were reachable only by typing a CLI flag. **Exactly one is a user surface in WPF,
so exactly one got a door.** The other two now have a recorded reason they are not user surfaces —
which is the second half of this packet's outcome, not an escape from it.

| Surface | Verdict | Where it is recorded |
|---|---|---|
| Graded Intake | **DOOR**, behind the weekly-pass gate | §11 D25, D26 (the gate, overturned in review), D27-D29, D32-D34 |
| Chaos tunnel | **NO DOOR** | §11 D30, plus the corrected header of `ChaosTunnelDemoDrive.cs` |
| AvatarTube | **NO DOOR** | §11 D31 |

The route that landed, from a cold start with **no command-line arguments**:
**`Graded Intake` rail door -> the Graded Intake page -> `Begin Intake` -> the weekly-pass gate ->
the one `IntakeLaunchCoordinator`.** On this build every user is REFUSED there, honestly and by
name: the port has no account, so it cannot determine the pass. Section 4 is why that is the right
answer and why the first submission's ungated version was not.

---

## 1. Graded Intake — the door it earned

WPF exposes this surface twice, and **both signposts navigate**:

- Rail sub-entry `BtnNavGradedIntake` — `MainWindow/MainWindow.xaml:811-812`, tooltip
  `tab_gradedintake` = "Graded Intake" (`Localization/Languages/en.json:802`) — handler
  `MainWindow/MainWindow.TabNavigation.cs:947`, a bare `ShowTab("gradedintake")`.
  The entry lives inside the **Play** door's list (`MainWindow.TabNavigation.cs:600-601`).
- Play-page card `BtnPlayGradedIntake` — `Views/Tabs/PlayTabView.xaml:1007-1010`
  ("Every state navigates to ShowTab(\"gradedintake\") - a Spent user has to be able to read why")
  -> `Views/Tabs/PlayTabView.Cards.cs:83`.

The destination page carries **exactly one** launcher — `BtnStartIntake`, WPF's own comment calls it
"the page's primary (and only visible) action" (`Views/Tabs/GradedIntakeTabView.xaml:151`) ->
`MainWindow/MainWindow.Lab.cs:108-167` -> `Services/Quiz/IntakeHostService.Launch`.

So it is a two-hop surface with a real destination page and one launcher on it — the same shape the
port already reproduces for the Loom and DTRH. **Why a door and not a card on the port's Play page:**
`ShellRouteBinding.ValidateOrThrow` refuses a mounted page no door reaches
(`Navigation/ShellRouter.cs:88-94`), and putting the launcher on the Play page itself would make a
card a launcher — the rule WPF states verbatim at `MainWindow/MainWindow.Presets.cs:1036`.

The page's strings are WPF's own, not invented: title `GradedIntakeTabView.xaml:60` (a deliberate
literal upstream), sub-line `:67-68`, section head `:125`, blurb `:127`, button `:152`
emoji-stripped per §9 D8, tooltip `:155`.

## 2. The Chaos tunnel — no door, and the port's comment was wrong

`Features/Chaos/ChaosTunnelDemoDrive.cs:12-13` said *"the greenfield dashboard has no Chaos game
entry point — typed named limit"*, which reads as a port gap awaiting a door. The WPF source says
the tunnel is not a destination at all:

- `Chaos/ChaosTunnelService.cs:20` — "the endless three.js 'rabbit hole' tunnel rendered **UNDER the
  whole Chaos game**".
- `:31-32` — `WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW`: it cannot take focus and never appears in Alt-Tab.
  A window that refuses focus is not somewhere a user navigates to.
- `:34`, `:58` — gated on `ChaosTunnelEnabled` only, **default OFF**.
- Every caller is the classic descent's own service: `Services/Chaos/ChaosModeService.cs:345`
  (preload under the countdown), `:518` (show at run start), `:3042` (zone hint), `:3246` (close).
- Its user-facing control is a **checkbox**, `ChkTunnel` (`Chaos/ChaosHubWindow.xaml.cs:1566` read,
  `:1667` write), on the Chaos **setup lobby** (the Warren) — a PRE-run screen. All three of its
  construction sites return early when a descent is already running:
  `MainWindow/MainWindow.Lab.cs:242` then `:262-264`; `Chaos/ChaosOverlayWindow.xaml.cs:873` then
  `:880`; `Services/Chaos/DtrhHostService.cs:879` then `:886`.

**WPF has no tunnel entry point either.** A setting on a setup screen is not a destination. The
comment is corrected rather than satisfied: what the port is missing is the Chaos RUN — and the
lobby that configures it — for the backdrop to sit under, a feature row and not a door.

> **CORRECTION, second review round.** An earlier revision of this section, of D30, and of
> `ChaosTunnelDemoDrive.cs` said the lobby was *"reachable only from inside a running classic
> descent"* and cited `MainWindow.Lab.cs:262-264`. **That is the inverse of what the citation
> does**: `:242` — twenty lines above it — is
> `if (!webPath && (App.Chaos == null || App.Chaos.IsRunning)) return;`, so the hub opens precisely
> when a descent is *not* running. "Only" was false twice over as well: `ChaosOverlayWindow.xaml.cs:880`
> and `DtrhHostService.cs:886` construct hubs too. The verdict was unaffected — the tunnel evidence
> above stands on its own — but the error made WPF's tunnel control look *less* reachable than it
> is, which is the direction that flattered the conclusion I had already reached. **That is the
> third time this project has produced a citation aimed at real lines while describing their
> opposite** (§8.5's occluded title; §10 D24's two-term grant recorded as one). The pattern is not
> line-number sloppiness: it is writing the conclusion first and attaching the nearest citation to
> it. It shipped inside product source, where the next reader would have treated it as verified
> evidence.

## 3. AvatarTube — no door

WPF never opens the companion from a dashboard gesture. It is created at startup when
`AvatarEnabled` is true (`MainWindow/MainWindow.xaml.cs:2912` -> `InitializeAvatarTube`,
`MainWindow/MainWindow.Companion.cs:145`; the setting defaults true), toggled from the Companion
page hero card (`MainWindow/MainWindow.CompanionRoom.cs:82` `SetAvatarEnabled`), and woken from the
tray. The port already carries the second of those behind its **Companion** door.

And `Features/AvatarTube/AvatarTubeDemonstratorWindow.axaml.cs:11-22` declares itself a
**DEMONSTRATOR** — "superseded by the first real AvatarTube feature, owner may async-veto" — with
demonstrator-valued constants and Mode/Talk/Pause/Pack/Attach harness controls. A rail door to it is
the packet's named trap.

What genuinely remains open is **not** a door problem and is recorded as such (§11 D31): the port's
companion window renders no avatar, and the startup-appearance half of §9 D11 is still divergent.

---

## 4. The overturn: D26, and why the first answer was wrong

**The door first landed ungated, and review overturned that. The correction is in this packet, not
deferred**, because it is this packet's door that made the hole reachable.

What I got wrong: I reasoned about the *wording* of a refusal and concluded that a locally-decided
"you already ran this week" would be a claim about the install nobody could lift — the mirror of
§10 D24. The reasoning about wording was sound. The **direction** was not. The source says plainly
what is being sold:

- `Services/Progression/IntakePassService.cs:13` — `Premium` is *"Patron. The pass system does not
  apply - **unlimited runs, no week, no door**"*.
- `:26-29` — *"The intake is a **premium Exclusive** … free accounts get **ONE run per week** …
  while **retakes stay a reason to subscribe**"*.
- `:140-146` — `CanStartIntake = Premium || Available`.

Unlimited retakes ARE the paid privilege, so an ungated door hands it to everyone. That is an
**over-grant**, the same class as the `(EntitlementTier)0` hole SP-094 closed at `DtrhGate` — not
the under-grant D24 records, which errs toward refusing something WPF would allow. And it was live
only because of this packet: while the intake was `--intake-demo` only it was unreachable, so
ungated cost nothing.

**The honest refusal was already in the source too.** A free, signed-out WPF user gets `NeedsLogin`
— *"The pass is per-account, so there is nothing to hand out yet"* (`:15`, branch at `:115`) — and
never `Spent`. The port has no account of any kind, so it cannot determine the pass at all. That is
SP-092's third answer rendered the SP-094 way: refuse out loud, in its own words, never wearing the
refusal's clothes.

### What landed

`Features/Intake/IntakePassGate.cs` — a pure function over `(state, reason)` with four closed
outcomes and no boolean anywhere:

| Input | Decision |
|---|---|
| `Premium` / `PremiumEntitled` | `Proceed` |
| `Available` / `AvailableThisWeek` (an authority answered) | `Proceed` |
| `Available` / `AvailableNoEntitlementProvider` — **every user of this build** | `RefusedUndeterminable` |
| `Spent` / `SpentThisWeek` or `SpentClockRollback` | `RefusedSpent`, WPF's copy verbatim |
| `Spent` / `SpentFailClosed` (evaluation threw) | `RefusedUndeterminable` — §11 D33 |
| `NeedsLogin` / `LoginRequired` (unreachable here) | `RefusedNeedsAccount`, WPF's copy verbatim |

`IntakeLaunch` asks it on every press, **after** the already-open refocus, which is WPF's own
ordering (`MainWindow.Lab.cs:112-117` returns before `:124`). `IntakeHostContext` gained a
`Prepare` / `StartTransport` split so the pass can be read before a run opens: **a refused press
binds no loopback origin and opens no window.** `--intake-demo` reaches the coordinator directly and
steps past the gate (§11 D32, the §10 D22 decision), because a gated evidence path would refuse on
every machine today and, once an authority exists, would depend on whether the developer ran an
intake earlier in the same ISO week.

### What a refused press leaves behind, named because nobody asked for it

`Prepare()` is real work: it starts the `intake_settings.json`, `intake_punchcard.json` and
`asset_selection.json` stores, and if a punch card needs the SP-054 load repairs it saves the healed
file. A press the gate refuses therefore leaves a prepared context **that is never `Dispose`d** —
only `EndFlow` disposes one, and a refused press never starts a flow — so those stores stay open
until the app exits. The `OperationRegistry` cancels their owners at teardown, so nothing outlives
the process, and the next press reuses the same context rather than building a second.

**The one part that reached the disk was fixed rather than recorded.** `Prepare()` used to mint
`intake_subject.txt` — a persistent local-fiction identity — for a user who had just been refused.
`SubjectId` is now lazy and its only reader is the page's boot config
(`IntakeHostWindow.axaml.cs:612`), so a refused press mints nothing. A user who has never taken an
intake should not acquire an intake subject id by pressing a button that told them no.

**Measured, not assumed.** Across every headless run in this packet the tests left 67 data roots
under `%TEMP%/ccp-sp095-headless-*`. Exactly 15 contain an `intake/` directory — the runs from
before this fix. Every post-fix root is **empty**: on a refused press the three stores load
`Missing`, stay clean, and write nothing at all. So the residue is in-memory and registry-owned
only.

What a user of this build now sees on pressing `Begin Intake`: *"This build could not determine your
Graded Intake pass, so it did not start a run. … That is a gap in the port, not a finding about your
access: nothing was decided about you. It closes when the port has an entitlement authority for the
intake."*

---

## Files changed

| File | Change |
|---|---|
| `client/src/CcpClient.Desktop/Navigation/ShellRoutes.cs` | the `intake` route, after Play; the class comment records why the count moved to five and which two surfaces were refused |
| `client/src/CcpClient.Desktop/Views/MainWindow.axaml` | `DoorIntake`; the intake gate scrim style; the rail comment names the UIA-name shape the headed harness derives its door set from |
| `client/src/CcpClient.Desktop/Views/MainWindow.axaml.cs` | mount `IntakePage`, register `DoorIntake`, build and expose `Intake`, accept `IntakeHarnessOptions` |
| `client/src/CcpClient.Desktop/Views/Pages/IntakePage.axaml{,.cs}` | NEW page, sibling of `StudioPage`/`PlayPage`; renders the gate decision by TYPE, over an opaque plate |
| `client/src/CcpClient.Desktop/Features/Intake/IntakeLaunch.cs` | NEW: the one construction site, the gate call, `IntakeHarnessOptions`, and the two test seams |
| `client/src/CcpClient.Desktop/Features/Intake/IntakePassGate.cs` | NEW: the pure four-way decision |
| `client/src/CcpClient.Desktop/Features/Intake/IntakeHostContext.cs` | `Prepare` split from `StartTransport`; the payload probe is taken at bind and throws if read before |
| `client/src/CcpClient.Desktop/Features/Intake/IntakeLaunchCoordinator.cs` | `EnsureContext()`, transport bound at open, optional data directory |
| `client/src/CcpClient.Desktop/App.axaml.cs` | `--intake-demo` uses the shell's ONE coordinator and steps past the gate, with the reason at the call site |
| `client/src/CcpClient.Desktop/Features/Chaos/ChaosTunnelDemoDrive.cs` | the stale "no Chaos game entry point" framing corrected, with the WPF citations |
| `client/docs/wpf-surface-reachability.md` | §11 (D25-D34), D26 rewritten by the overturn; §10's closing paragraph annotated |
| `client/tests/CcpClient.Tests/IntakePassGateTests.cs` | NEW, 10 results |
| `client/tests/CcpClient.HeadlessTests/IntakePageHeadlessTests.cs` | NEW, 6 facts |
| `client/tests/CcpClient.HeadlessTests/NavigationShellHeadlessTests.cs` | the rail pin gains `DoorIntake`, plus a mechanical "no door names the tunnel or the avatar" assertion |
| `spine-tasks/SP-095-remaining-doors/{plan.md,record.md,floor-delta.json}` | packet artifacts |

Untouched, as required: `client/tools/**`, `client/tests/floor/floor.json`,
`client/docs/task-board.md`, `Features/Dtrh/**`, `Entitlement/**`, `Tray/**`,
`ConditioningControlPanel/**`.

## Tests and floor

`IntakePassGateTests` (unit, 10 TRX results = 8 facts + one 2-row theory): only `Premium` and a READ
`Available` grant; the greenfield default is undeterminable rather than a grant; a spent week and a
rolled-back clock carry WPF's verbatim copy; the one-day key so nobody reads "unlocks in 1 days";
a thrown evaluation is undeterminable rather than WPF's fail-closed `Spent`; signed-out is its own
branch; the whole state-by-reason **cross product** lands on exactly one renderable decision with
exactly two grants; no refusal message carries another refusal's sentence; `Classify` never logs
authored copy.

`IntakePageHeadlessTests` (headless, 6 facts, real input, cold boot, no CLI args): the door-then-
button route refusing undeterminable and opening nothing; a patron authority reaching the ONE
coordinator with no band; the refused page taking the click with nothing disabled, a hit-test-
transparent band and an opaque plate (both alphas pinned); the door navigating without asking the
gate; WPF's own strings with none of the controls the port lacks; the rail order plus the
`"Intake door"` UIA name and per-door layout probe the headed harness derives its door set from.

Two seams are substituted and neither is the decision: the SP-054 entitlement source, and the data
root — the latter so the tests never write `intake_settings.json` into a developer's real
`%LOCALAPPDATA%/ConditioningControlPanel` (verified: the runs created their stores under
`%TEMP%/ccp-sp095-headless-*/intake`). The undeterminable branch needs no double at all; it is what
this build's shipped default produces on its own.

Declared delta: **unit +10, headless +6** (`floor-delta.json`).
Observed: **CcpClient.Tests 1110** (pin 1100 + 10) and **CcpClient.HeadlessTests 54** (pin 48 + 6).
The floor check reports both drifts, which is the designed state for a bound lane; `floor.json` was
never opened.

### Bite tests (none committed, all restored byte-identically)

Three scratch mutations, each built and run, each reverted with `git checkout --` and confirmed by
`git status --porcelain`:

| Mutation | Result |
|---|---|
| `IntakePage.axaml.cs`: click handler made a no-op | **2 failed / 51 passed** (pre-gate revision) — exactly the two launch facts |
| `MainWindow.axaml.cs`: the `intake` route mounts `SystemPage` | **4 failed / 49 passed** (pre-gate revision) — the four page facts; the rail-order fact correctly stayed green |
| `IntakePassGate.cs`: the `AvailableNoEntitlementProvider` branch returns `Proceed` — **the exact over-grant review overturned** | **unit 4 failed / 1104 passed**, **headless 2 failed / 52 passed**. The four unit facts are the greenfield-default fact, the cross-product closure, the message-boundary guard and `Classify`; the two headless are the cold-start refusal and the band behaviour. Nothing else in either assembly moved |

The third is the one that matters: had it existed at first submission, the over-grant could not have
shipped.

### Headed harness

`pwsh client/tools/verify/self-test.ps1` -> **SELF-TEST PASS** (seeded regression caught by the
specific named check `rail-door-selected-border` at exit 2; restored build 888/918 pixels, ALL
CHECKS PASSED). Re-run after the gate landed, same result.

`capture.ps1` **widened itself with no edit**, which was the packet's second trap:

```
shell mounted its default page; all 5 rail doors published a layout probe (Studio, Companion, Play, Intake, System)
```

It was four doors before this packet. The derivation at `capture.ps1:136-151` reads the rail's
`RadioButton` UIA names, matches the `<id> door` shape, and demands a `layout-probe: door <id>` line
for each — so the new door was picked up because its `AutomationProperties.Name` is `"Intake door"`.
Fact 6 of `IntakePageHeadlessTests` pins that name and the per-door probe line headlessly, so a
future door cannot be renamed out of the harness's sight without reddening a test.

`CCP_DATA_ROOT` was never exported process-wide; every headed run was a plain invocation.

## Divergences recorded (§11)

D25 sub-entry becomes a top-level door · **D26 the pass gate, rewritten by the overturn: gated, and
refusing `Undeterminable` because the port has no account** · D27 the AI-availability refusal is not
ported · D28 the first-ever-run duck exemption is not ported · D29 the pass banner, classic-quiz
controls, past-runs list, BETA pill and gate CTA are absent · D30 the Chaos tunnel gets no door and
WPF has no entry point either · D31 the AvatarTube gets no door and the Companion door already
answers it · D32 `--intake-demo` steps past the gate · D33 a thrown evaluation is undeterminable
rather than WPF's fail-closed `Spent` · D34 the port's band is hit-test transparent where WPF's is a
curtain.

## What this work does NOT prove

Everything asserted headlessly is **draw-level** (`verification-harness.md`): visual tree, arranged
bounds, real input routing, hit-test flags, style-resolved brushes, rendered strings. The gate
itself is a pure function proved in the unit suite. Specifically **undischarged**:

- that the intake host window ever presents, or that a run boots — no test here opens one;
- that a second press really *refocuses* a live host window (only "both presses reach one
  coordinator" is proved headlessly);
- that the shell's duck and restore behave on a real desktop;
- every composited pixel of the new door, the new page and the refusal band. The headed capture runs
  drove the **Companion** door (that is what `capture.ps1` captures) and exercised the door-set
  derivation; they did **not** capture the Intake door or the gate band. No `presentation-verified`
  claim is made anywhere in this packet;
- that `RefusedSpent` or `RefusedNeedsAccount` is ever reachable in the shipped build — neither is,
  today, and both are proved only at the pure-gate level.

Nothing about the Chaos tunnel or the AvatarTube was re-verified at runtime; those two verdicts rest
on WPF source reading, cited line by line above.
