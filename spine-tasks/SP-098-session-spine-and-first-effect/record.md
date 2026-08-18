# SP-098 — record

Branch `lane/SP-098-session-spine-and-first-effect`, base `feat/crossplatform` at `237a2156`,
worktree `.claude/worktrees/agent-aa7e6dc0ec3d394c1`.

The plan checkpoint is `plan.md` in this folder (committed before the first product edit).

---

## 1. What this packet built

**A conditioning session, and one effect that really runs under it.**

The spine, in `client/src/CcpClient.Desktop/Session/`:

| file | what it is |
|---|---|
| `SessionClock.cs` | `ISessionClock` + `SystemSessionClock`. The pacing seam every effect takes, so no effect ever reaches for `DateTime.Now` or a bare `Task.Delay` and no test ever waits out an interval. Shape from the established `ISoundClock` precedent (`Audio/AudioSeams.cs:118-137`), declared in `Session/` so an effect owning a timer need not depend on the audio stack |
| `SessionEffect.cs` | `ISessionEffect` (stable `Id`, display `Title`, persisted `Enabled`, truthful `Dot`, owned `Completion`, `Arm`/`Disarm`/`SetEnabled`) and `EffectDotState` (`Off`/`Armed`/`Live`). **This is what the other fourteen rack modules implement.** |
| `SessionEngine.cs` | `Start`/`Stop`/`Toggle`/`QuickToggle`, in WPF's order, with WPF's re-entrancy guard |
| `SessionParticipant.cs` | The composition point and the lifecycle participant: owns the preset store, the asset-selection reader and the effect rack. Construction starts nothing; phase-3 start loads the preset and starts **no session** |

The effect, in `client/src/CcpClient.Desktop/Effects/`:

| file | what it is |
|---|---|
| `FlashSchedule.cs` | WPF's pacing law as a pure function: `3600.0 / max(1, perHour)`, uniform ±30 %, then the 3-second floor — in that order |
| `FlashImagePool.cs` | `IFlashImagePool` + the product pool over `<dataDir>/assets/images`, through the port's ONE active-pool seam (`DtrhUserMedia.BuildDisabledSet`/`IsAssetActive`, SP-055) |
| `FlashImagesEffect.cs` | The effect: one owned operation per arm, a clock one-shot re-armed at the tail of every firing, `Live` derived from the operation authority |

Persistence: `Persistence/SessionPresetDocument.cs` (`session_preset.json`).

UI: a shell action bar with the ONE `START`/`STOP` button (`Views/MainWindow.axaml(.cs)`), and a
`Flash Images` rack row with a live dot, a right-click quick-toggle and a module panel with three
real dials, a live account of what the effect did, and a plain statement of what is missing
(`Views/Pages/StudioPage.axaml(.cs)`).

Composition: `Lifecycle/CompositionRoot.cs` registers the participant last, hooks its preset flush
into the reserved pre-drain slot, and gains two test-only seams (`SessionClockFactory`,
`FlashImagePoolFactory`) so a headless test can drive the REAL shell on a manual clock.

---

## 2. Which effect, and why

**Flash Images**, as nominated. `ConditioningControlPanel/Services/` was read for a materially
better first (see `plan.md` §2 for the full table). Summary:

* Every EFFECTS/GAMES row draws through an overlay window or the `Services/Compositor/` layers.
* **Mind Wipe** is the only rack module with no window at all (`Services/LockCard/MindWipeService.cs`
  is pure NAudio). It has fewer *drawing* seams — and it was rejected: its whole observable output is
  audio, which no gate in this port can verify and which a CI box may have no device for. It would
  have traded a **named, honest gap** for an **unfalsifiable claim**, which is the worse of the two.
* Scheduler and Intensity Ramp are pure logic but *meta* — the ramp ramps other effects' dials and
  the scheduler drives the engine rather than running under it. Neither is a first proof that an
  effect runs under a session.

Flash Images is WPF's first EFFECTS row, the first service `StartEngine` starts
(`MainWindow.StartStop.cs:178`) and the first `StopEngineCore` stops (`:305`, "Stop flash first"),
and it carries the most citable parity surface in the rack.

---

## 3. What the effect can honestly do without an overlay

**Ported exactly:** the interval formula with its variance band and floor, the enable gate, the
per-flash draw of `ImagesPerFlash` independent uniform picks with replacement, the empty-pool
outcome, the active-pool deselection seam, the dials and their clamps, and the arm/disarm lifecycle.

**Not built, and named as a separate platform packet:** putting the images on screen above other
applications. WPF does that with one layered always-on-top `WS_EX_TRANSPARENT` click-through window
per flash, re-asserted to `HWND_TOPMOST` (`FlashService.cs:3615`, `:3667-3668`, `:3862-3868`,
`:206-240`). That is a compositor. `docs/constitution.md` classes the previous port attempt as
failure evidence largely because of overlay work, so **no overlay, no compositor, and no in-window
imitation of one was built here.**

The module panel says so in words, before the user presses anything:

> Showing the images over your other windows is not ported yet: that needs an always-on-top
> click-through surface this build does not have. The schedule above is real and runs - it just has
> nowhere to draw.

---

## 4. WPF session semantics, and what the spine guarantees

Citations from `ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs` unless noted.

| WPF fact | citation | port |
|---|---|---|
| ONE button, branching on `_isRunning` | `:34,50,105` | `SessionEngine.Toggle`, one `Button`, never disabled |
| Start saves the dials FIRST | `:161` | `Start()` saves before arming anything |
| Start gates each service on its own flag | `:181,200,206,…` | each effect's `ScheduleNext` refuses while its dial is off (`FlashService.cs:541-546`) |
| The running flag flips AFTER the work starts | `:268-269` | asserted by `TheRunningFlagFollowsTheWork_OnBothEdges` |
| Stop has a re-entrancy guard | `:292-296` | `_stopInProgress`, kept and documented |
| Stop stops the work BEFORE clearing the flag | `:305`, `:385-387` | asserted by the same fact |
| Flash first on both edges | `:178`, `:305` | registration order in `SessionParticipant` |
| The effect schedules its first tick synchronously in `Start()` | `FlashService.cs:352` | `Arm()` schedules inline — load-bearing, or a stop/advance ordering becomes a race |
| Right-click quick-toggle: flip the flag, then start/stop only if running, then save | `Presets.cs:1250,1264` | `SessionEngine.QuickToggle`, one dispatch entry shared with the panel checkbox |
| A frequency change re-paces the live schedule | `FlashService.cs:527-531`, `FlashFeatureControl.xaml.cs:186` | `FlashImagesEffect.RefreshSchedule` |

**What the spine guarantees.** A session's stop is not a flag flip: every effect owns an
`AsyncOperationOwner`, arming begins a generation and registers an operation, and disarming cancels
that generation AND tears down the pending clock handle synchronously. So "did it really stop" is a
question the **operation registry** answers — outstanding zero, unobserved zero, typed `Cancelled` —
rather than a boolean anyone could set. The host's single teardown entry point kills a running
session by the same mechanism without anyone pressing STOP, and that path is tested separately.

---

## 5. How stop was proved to really stop, and that the proof bites

The proof (`SessionSpineTests.AfterStop_NoAmountOfClockMakesTheEffectWorkAgain`, and its sibling
`Stop_TerminatesTheOwnedOperation_Cancelled_WithNothingLeftOutstanding`): start, advance the manual
clock twice and watch two flashes really come due, press stop, then assert
(a) the pending one-shot is gone from the clock, (b) **ten further clock windows produce no flash and
no draw**, (c) the owned completion terminates `OperationOutcome.Cancelled`, (d) the registry has zero
outstanding and zero unobserved operations. `HostTeardown_WithASessionStillRunning…` proves the same
through the teardown path, which is a different path. The headless
`ColdStart_PressingSTART_ReallyRunsTheEffect_AndPressingSTOP_ReallyStopsIt` proves it through real
input on the real button.

**Step 6 — the bite.** `SessionEngine.Stop()` was edited to flip `Running` and skip
`effect.Disarm()` — "a session that only sets a flag". Result:

```
CcpClient.Tests           4 FAILED of 17
  AfterStop_NoAmountOfClockMakesTheEffectWorkAgain
  Stop_TerminatesTheOwnedOperation_Cancelled_WithNothingLeftOutstanding
  TheRunningFlagFollowsTheWork_OnBothEdges
  TheDotHasThreeStates_AndEachOneIsTrueWhenItIsShown
CcpClient.HeadlessTests   1 FAILED of 11
  ColdStart_PressingSTART_ReallyRunsTheEffect_AndPressingSTOP_ReallyStopsIt
```

Restored with `git checkout --` (byte-identical; `git diff HEAD` on that file is empty). **Not
committed.**

---

## 6. D5 and D6

**Closed for the `Flash Images` row.** It has a real effect, so it has a dot that reports truthfully
and a right-click that really toggles.

**Still open for the `Spiral Overlay` row**, which still has no ported effect. WPF's own rule for a
row it cannot wire honestly is to omit the dot (`StudioTabView.xaml.cs:494-496`) and leave the
gesture unhandled (`:659`); a dot that always read "off" would be the fake-available shape the
capability contract bans. `StudioRackHeadlessTests.TheSpiralRow_StillHasNoDotAndNoToggle_AndThatIsTheHonestState`
holds that line, and the existing SP-091 fact `RightClickOnTheRackRow_OpensNoMenu_AndSelectsNothing`
(which targets that row) still passes unchanged.

The gaps close per row, when an effect lands behind that row, and for no other reason.

---

## 7. Divergences

Seven, recorded in `client/docs/wpf-surface-reachability.md` §14 as **D45-D51**:

* **D45** the three-state dot (WPF's copy and WPF's code disagree about what a dot means; the port
  says both rather than picking one).
* **D46** a module switched on mid-session really arms (WPF's own path is inert there).
* **D47** nothing is drawn; the on-screen half is a platform packet.
* **D48** the action bar carries START alone.
* **D49** three dials, not a dozen; the rest absent rather than disabled.
* **D50** a new `session_preset.json` document, and two renamed members.
* **D51** STOP always stops — no scripted-session confirmation, no lockdown refusal, no
  session-locked toggle refusal, because none of those subsystems is ported.

---

## 8. Spec-versus-code discrepancies found

1. **WPF's rack dot is documented as one thing and implemented as another.** The Studio onboarding
   card says the dot shows "everything that is currently running"; the mechanism reads the persisted
   enable flag. Source wins per the §8 rule, but neither reading alone is honest for the port, so the
   dot got a third state. Recorded as D45.
2. **WPF's mid-session enable is inert.** `FlashService.Start()` returns early on its own
   already-set `_isRunning`, so a module that was off at engine start can never be turned on during
   a session by the gesture the app documents. The port implements the documented outcome and records
   the departure as D46 rather than filing a bug against upstream's tree.
3. **The packet said the Studio rack's one row "has no dials and no on/off state".** True at
   authoring; that row (`Spiral Overlay`) still has neither. The new row is a second one, added above
   it in WPF's rack order, rather than a conversion of it.
4. **`AppSettings` lives under `ConditioningControlPanel/CCP.Core/Models/`.** The shipping WPF app
   references `CCP.Core` (`ConditioningControlPanel.csproj:52`), so that file is the shipping
   product's real settings model and was read as behavioural evidence of the shipping app. No
   `CCP.*` class, interface, timer or DI topology was imported into `client/`; only WPF-observable
   values (defaults and clamps) were cited.

---

## 9. Tests and floor numbers

| project | pin | added | observed |
|---|---|---|---|
| `CcpClient.Tests` | 1128 | **+47** | **1175** |
| `CcpClient.HeadlessTests` | 70 | **+11** | **81** |

Observed == pin + declared delta, in both projects. Declared in
`spine-tasks/SP-098-session-spine-and-first-effect/floor-delta.json`. `client/tests/floor/floor.json`
was **not opened and not edited**.

Final floor run: `CcpClient.Tests` 1173 passed / 0 failed / 2 allowed skips (the two OS-gated Linux
rows), `CcpClient.HeadlessTests` 81 passed / 0 failed. The only reported violations are the two
expected count drifts.

New files:

* `client/tests/CcpClient.Tests/SessionSpineTests.cs` — 17 facts. The spine: the work really starts,
  **stop really stops** (four of them), teardown kills a running session, the flag follows the work
  on both edges, arm/disarm idempotence, the dot's three states, a dial-off module arming nothing,
  the quick-toggle in and out of a session, an unknown row id as a silent no-op (4-row theory), the
  dial surviving a restart through the real store, the empty-pool firing, and a mid-session re-pace.
* `client/tests/CcpClient.Tests/FlashEffectTests.cs` — 30 results. The pacing law (base interval,
  the `max(1,…)` guard, the ±30 % band really used, the floor applied last, the advertised bounds),
  the dials' defaults and clamps, an out-of-range file corrected on load with unknown members
  surviving, and the real pool over a real folder (missing folder, with-replacement draw, image
  extensions only, deselection honoured through the SP-055 seam, the whitelist gate, the empty-folder
  re-read).
* `client/tests/CcpClient.HeadlessTests/StudioRackHeadlessTests.cs` — 11 facts on the real shell with
  real input: the cold-start START/STOP user story, the one-control-two-states button, the dot's
  three states, the dot after teardown, the right-click toggle (opens no menu, selects nothing), the
  toggle and the checkbox being one path, off-and-on mid-session, the module opening with real
  persisted dials, a moved dial changing the next flash, the overlay-gap and empty-pool notices, and
  the spiral row still having neither dot nor toggle.

Two existing facts were amended (no count change): `IntegrationProofTests` and
`CompositionRootValidationTests` pin the participant roster, which grows from 7 to 8. Both now also
assert the new participant starts **no session** at phase 3.

**Zero wall-clock waits added.** Both new clock doubles are manual; the only waits are
`TestWait.Until` on a deterministic completion or a posted UI projection. `TestTimingGuardTests` and
`VacuousShapeGuardTests` pass unmodified — one lexical vacuous-shape site was found in review and
removed by restructuring the test (population asserted with `Assert.NotEmpty` before the sweep)
rather than by adding a ledger entry.

---

## 10. What this does NOT prove

* **No composited pixel is claimed.** The headless facts are draw-level: visual tree, style-resolved
  classes, arranged bounds, real input routing. The action bar's real placement, the dot's legibility
  at real scaling and the button's colours as a human sees them belong to a headed capture, which is
  the orchestrator's.
* **No flash has been shown on a screen.** The on-screen half of Flash Images is not built (D47) and
  no test here should be read as evidence that it is.
* **No audio, focus, window or animation behaviour is touched or claimed** by this packet.
* **Linux is unproven for this row beyond compilation.** Nothing in the session spine is
  platform-specific — the clock is a `System.Threading.Timer`, the pool is a directory walk — but no
  Linux run was performed here.
* **The other fourteen rack modules do not exist.** The spine is the pattern they will follow; one
  effect having proved it is not fourteen.

## 11. Stopped early?

No. Every step of the packet was executed, including step 6's seeded-defect check and its
byte-identical restore.
