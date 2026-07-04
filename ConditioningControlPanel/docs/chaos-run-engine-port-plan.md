# Chaos Run-Engine Faithful Port — Plan & Slice Tracker

Created 2026-07-04. Claim: task-board ledger row `bac65e4a` (@fable). Goal:
`docs/skia-rebuild-goal.md` WP3/WS2. This replaces the simplified `AvaloniaChaosService`
stand-in with a faithful port of WPF `Services/Chaos/ChaosModeService.cs` (3275L).

---

## 🔖 HANDOFF NOTES (for a mechanical-tier model picking this up)

> **START HERE INSTEAD:** load the `mechanical-port-work` skill (`.pi/skills/mechanical-port-work/SKILL.md`)
> and take the top item of `docs/model-handoff-queue.md`. That skill + queue supersede the
> procedural parts of this section (gates, rules, escalation); this plan doc remains the
> per-slice SPEC source (S5-S9 targets, WPF citations, evidence table).

**Read this whole section first.** It was written 2026-07-04 when the smart-model session
handed the remaining work off. You do NOT need to redo archaeology — the 4 contract docs
below + this plan are the source of truth; do NOT re-read the WPF files end-to-end except
the exact line ranges each slice cites.

### Status (2026-07-04, commit `071a8d7e`)
- **S1 ✅** Core `ChaosSpawnCatalog` + 63 tests (`c11c23ce`)
- **S2 ✅** config/state parity, `ApplyTo`, sin ramp, computed mult stack + 38 tests (`b0aedbbd`)
- **S3 ✅** exact scoring + focus economy + detonation branches + 35 tests (`fc7589b8`)
- **S4 ✅** faithful spawn director + behavioral callbacks (THE HEART) + 51 tests (`071a8d7e`)
- Core tests 205 → **426**; all gates green every slice; smoke baseline held (Findings: 5).
- 2026-07-04: WPF 6.2.8 merged from main (`aba10210`) — verified ZERO impact on ported chaos
  internals; port heads bumped to 6.2.8; #480/#483 parity fixes landed (see task board
  "Sync-from-main 6.2.8" section). S4b WPF citations into BubbleService.cs drifted ~2 lines.
- The hard/architecture/JUDGMENT slices (S1-S4) are DONE, and **S4b-1/2/3 are DONE too**
  (commit after `42580d84`): bound enrage (fuse-halve w/ 600ms floor + ×1.4 Vx/Vy, survivor
  LIVES — WPF BubbleService.cs:2321-2335), treat-rot (`IsRottingTreat` mirrors WPF `_isTreat`
  :2516 — ordinary+golden+prism fire OnTreatExpired; heart/droplet/escort/tease/brittle never
  rot), darter spank (gated on `ChaosRunKnobs.SpankerOn` OR born-spanked sweepers — spank
  REPLACES catch per WPF :3706-3708, no rabbit_caller double-tick). A `ChaosRunKnobs` live-knobs
  seam now exists on the engine (`BubbleEngine.Knobs`, exposed via `IBubbleService.ChaosKnobs`
  DIM) — **S4b-4 DONE: all WPF live-lambda knobs threaded through it** (chainReach,
  hitboxScale, bubbleOpacity, cursorPull, rabbitHoming, spankGrow, liveMagnet,
  rabbitTrailSec, electrifiedRabbits; wandShimmer retired-not-ported). The engine work is
  FINISHED — no remaining slice touches BubbleEngine internals. What remains (S5-S9) is
  **MECHANICAL**: follow the steps literally, run every gate, STOP with a `BLOCKED:` note
  if a precondition fails or a step is ambiguous. Do NOT improvise.

### Remaining ladder, in order (one commit each)
1. **S5 — draft/boon pool extraction** (next up; the engine and knob seams are complete —
   `SyncKnobsFromState()` in AvaloniaHeadStubs.cs already routes every boon effect live).
2. **S5 — draft system** (mechanical: port BeginWaveTransition + ChaosBoonPool.Draft +
   OnBoonChosen; add Core tests).
3. **S6 — payload dispatch + heavy gate + Ambient fix** (mechanical: collapse BuildPayload into
   AvaloniaEffectPayloadFactory; port FireScaledPayload/FirePayloadForDetonation; close P3 row).
4. **S7 — lifecycle completion** (mechanical: EndRun order, AwardRunRewards faithful,
   sentinel cadence, SFX sweep; Core tests on the reward formula).
5. **S8 — layer production callers + hints** (mechanical: wire PopText + FieldFx layers;
   port ChaosBubbleHints).
6. **S9 — full-run verification + trackers** (run side-by-side with WPF, update all trackers).

### Hard rules for a mechanical executor (read before each slice)
- **Trust the WPF source over the contract docs** when they disagree; cite the WPF file:line
  in an XML doc or inline comment. Do NOT trust your memory of "how this usually works".
- **Do NOT modify**: `ConditioningControlPanel/Services/**` (WPF head), `tests/.../SmokeTestRunner.cs`,
  `CCP.Avalonia/Compositor/**` internals. New interface members = **DIMs** with safe no-op
  bodies (fakes keep compiling).
- **No `TODO` / `// ...` / placeholders.** If you cannot wire something, leave a
  `// (plan: chaos-run-engine-port-plan.md Sx) <reason>` comment AND list it in your report
  as a follow-up row. Never fake a seam.
- **Pure logic → `CCP.Core/Services/Chaos/`** (new file per concern) + unit tests in
  `tests/CCP.Core.Tests/`; the Avalonia service calls the Core function. This pattern is
  already established (ChaosSpawnCatalog, ChaosRunRules, ChaosScoring, ChaosSpawnDirector).
- **Gates before EVERY commit** (copy-paste; ALL must pass — if any fails, fix it or STOP):
  ```
  dotnet build ConditioningControlPanel/CCP.Desktop.slnf -clp:ErrorsOnly   # 0 errors
  dotnet build ConditioningControlPanel.sln -clp:ErrorsOnly                # 0 errors
  dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj   # ALL pass, count >= 426
  dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --smoke-test   # 44 tabs / Findings: 5 (first-chance 21 = known benign OAuth-cancel noise)
  ```
- **State-mutating slices (S5 OnBoonChosen, S7 EndRun/AwardRunRewards)** get an independent
  adversarial review (claim-verifier subagent or careful self-review line-by-line vs WPF)
  before commit, per goal rule 7.
- **One slice per commit** (`--no-verify`); update the slice's evidence row in this file in
  the same commit; never leave a red tree.
- **Record exact test counts** in each evidence row (floor is 392 now, not 205).

---

## Source-of-truth contracts (read FIRST, do not re-derive from WPF)

| Contract | Doc |
|---|---|
| Spawn system (pool, riders, behavioral, density, cadence, waves) | `docs/chaos-run-engine-contracts/spawn-system.md` |
| Economy + scoring (per-pop, sparks, XP, ranks, upgrades, boons, meta) | `docs/chaos-run-engine-contracts/economy-scoring.md` |
| Draft + payloads + lifecycle (draft gating, payload dispatch, run start/end, sentinel, lessons, SFX) | `docs/chaos-run-engine-contracts/draft-payloads-lifecycle.md` |
| Avalonia current state (what exists, seams, gaps) | `docs/chaos-run-engine-contracts/avalonia-current-state.md` |

## P0 invariants (survive every slice)

- P0-1 **Core `BubbleEngine` is already faithful — feed it, never rewrite it.** All behavioral
  bubbles, channel defuse, and field hazards are engine-side; the port supplies specs + callbacks.
- P0-2 **`IChaosService` (`CCP.Core/App.cs:418-437`) never narrows** — HUD/toys/hub/benchmark/smoke bind to it.
- P0-3 **XP is paid PRE-multiplier**: `AddXP(baseXp = min(Score, 250·durMin·diff), XPSource.Chaos)`;
  `AvaloniaProgressionService` applies skill mult internally. Never double-multiply (X1-2/X1-3).
- P0-4 **`chaos_meta.json` stays schema-2 additive** with the atomic tmp+Move save. No format drift.
- P0-5 **Crash sentinel**: `Mark` at run-start + ~15s cadence; `Clear` at BOTH EndRun and CleanupAfterRun (X1-13).
- P0-6 New interface members = DIMs with safe no-op bodies (fakes keep compiling).
- P0-7 Gates before every commit: slnf 0 err · WPF sln 0 err · Core tests all pass (count never
  decreases; baseline 205) · `--smoke-test` baseline (`Findings: 5`).
- P0-8 WPF head behavior/files untouched (reference implementation).
- P0-9 Layer registrations/z-band unchanged; new production callers go THROUGH the existing
  verify-layers-proven seams.

## Slices (one commit each; adversarial review on state-mutating slices per goal rule 7)

### S1 — Core spawn catalog: faithful variant pool + spec builders  ✅ contracts → code
Port `ChaosBubbleVariants` faithfully into **CCP.Core** (new `CCP.Core/Services/Chaos/ChaosBubbleVariantsCatalog.cs`
or upgrade the existing Avalonia catalog and relocate): the 8-row weighted table (exact
weights/MinIntensity/fuse bands/tints/labels), `Pick` (intensity gate + fallbacks),
`Build` (size t-bias, strength-pre-scale, GLOBAL/GIANT scale, motion resolution incl.
side-drift + freeze FloatUp remap, fuse formula), `BuildGolden/Prism/Heavy/Echo/EchoChild/
ChaperonePair/BoundPair/Tease/Brittle/Heart/GoldDroplet/Darter + RollDarter`, all §7
constants. **Unit tests** pin: pool table rows, Pick gating/fallback, Build size/
strength/fuse formulas, golden/prism/heavy/darter spec numbers.
SCOPE NOTE 2026-07-04: S1 is PURELY ADDITIVE (new `ChaosSpawnCatalog` + tests; at most one
additive `Strength` property on `ChaosBubbleSpec`). The parallel-`ChaosTuning` collapse and
the Avalonia `ChaosBubbleVariants` stub retirement move to S4, where the service internals
are rewritten to consume the Core catalog anyway (avoids touching the stand-in twice).
Acceptance: Core tests green with new coverage; no behavior change in the running app yet.

### S2 — Run state + config faithful port (models)
Bring `ChaosRunState`/`ChaosRunConfig` to WPF parity: `RunIntensity/RunProgress`,
`TotalMult` stack (BaseMult/ComboMult/DifficultyMult 1.0-1.3-1.7-2.2/HeatMult/BoonMult/UrgeMult),
`BenignBaseline`, `FuseTimeMult`, `BubbleScale`, `GoldenChance=0.005`, `PrismChance`,
`HeavyDropEvery`, `GgRabbitChance`, `RabbitRateMult`, `RerollsLeft`, `ChanceDoubleOdds`,
`PendulumPayMult`, `LastBreath*`, `BlindfoldPayMult`, `SinExtraMult`, `MaxedBoons`,
`ExtendOneLoop` (Relapse), `DraftChoices=3`, `SinChance` + `DefaultSinChance` ramp
(0 <2 runs, 0.25→0.5 linear 2..10), `DraftAutoResumeSec=15`, `FromSettings` (clamps +
`ClampVariants` reveal-locks + **`ChaosMeta.ApplyTo(cfg)` invocation**). Fix `ChaosRanks`:
enum exactly Curious0/Tempted3/Slipping10/Entranced25/Devoted50/Claimed100, no `Lost`,
`>=` lookup (X1-4). Upgrade effects per economy contract §6 (slow_fuses ×1.15 fuse,
silk_touch 1.25 hitbox+magnet, popup_notification heart, pendulum_swing, draft4=4,
extreme_tier purchase-side). **Unit tests** pin ranks, DifficultyMult, DefaultSinChance,
ApplyTo effects, FromSettings clamps.

### S3 — Scoring + focus economy
Replace the stand-in's flat formulas with the exact WPF paths (economy contract §1):
`BasePoints = 40 + strength*1.6`; treat pop `Base×BenignBaseline×PayMult×Pendulum×
ChanceFlip×TotalMult×BoonPayMult` (+Combo/Focus/Heat side-effects in order); defuse
(full base ×LastBreath×slowburn-capstone×…; Heat+0.07); prism `×10` path; tease-denied
flat 120; darter 120(+90 quick)×TotalMult (NO BoonPayMult); freeze 140×TotalMult;
golden/droplet/heart outside score. `ChanceFlip`/`PendulumFactor`/`BoonPayMult` helpers.
**Unit tests**: each pop path with fixed rng, order-of-operations pinned.

### S4 — Spawn loop faithful port (the heart)
Replace `SpawnTick`/spawn parts of `RunTick` per spawn contract: self-retuning interval
`(1000−intensity·680)/diff ÷ SpawnRateMult ÷ slow-mo, floor 280, ×perfBackoff`; density cap
`round((6+intensity·10)·√diff)` gating ordinary+behavioral only; effIntensity bias;
weighted Pick; golden/prism riders; Heavy Drop serial; video end-of-loop strip; freeze
cap re-pick; side-drift grace; behavioral rolls (Echo/Chaperone/Bound/Tease rank+chance
gates, gentleMult, debut Seen*+announce+DEBUT_FUSE_MULT, ScriptedFirstRun off-switch);
Brittle rider; darter roll (cap-independent); empty-field rescue; welcome shower;
once-per-loop heart arming. Wire the FULL behavioral callback set into
`BeginChaosMode` (route via `AvaloniaBubbleService`'s 16-arg overload; widen
`IBubbleService` with DIMs where needed — supply OnDarterCaught/OnFreezeCaught/
onChaperoneShieldBroken/onBoundEnraged/onTeaseTouched/onTeaseDenied/onBrittleShattered/
onTreatExpired/onDarterSpanked + lambdas: chainReach/hitboxScale/bubbleOpacity/cursorPull/
rabbitHoming/spankerOn/spankGrow/liveMagnet/rabbitTrailSec/electrifiedRabbits).
Handlers implement the contract semantics (echo split children, bound enrage, tease
touch/denied, brittle shatter w/ glass_shatter fallback cue, rabbit-spank lesson tick —
verify BubbleEngine fires onDarterSpanked on first smack).

### S4b — Engine seam gaps surfaced by S4 [MECHANICAL, but touches BubbleEngine — do each sub-step as its own commit]

S4 wired the spawn director but listed behaviors the Core `BubbleEngine` couldn't express.
Each sub-step is independent and small; audit each against WPF `Services/Chaos/ChaosModeService.cs`
+ `Services/Bubbles/BubbleService.cs` before commit. Add a Core test for the semantics.

- **S4b-1 Bound enrage = enrage, not detonate.** WPF `OnBoundEnraged` (ChaosModeService.cs:1395-1404)
  + BubbleService: when the second half of a bound pair does NOT complete within
  `BOUND_WINDOW_MS` (2500), the surviving half is ENRAGED (trance time halved, speed ×
  `BOUND_ENRAGE_SPEED_MULT` 1.4) and left live — NOT detonated. The current Core engine
  detonates the survivor. Fix `BubbleEngine`'s bound-pair-window-lapse path to enrage the
  survivor instead (halve remaining fuse, apply speed mult) and fire `onBoundEnraged`.
  Test: a bound pair where the second half times out → survivor stays alive, enraged.
- **S4b-2 Treat-rot streak cost (ordinary treats).** WPF `onTreatExpired` (ChaosModeService.cs:1901-1920)
  fires for rotting flash/subliminal treats too, halving combo (and swallowing heart/droplet
  expiry silently). The Core engine only fires `onTreatExpired` for golden/heart/droplet.
  Extend the engine to fire it for ordinary-treat rot as well, then ensure the service handler
  halves combo (it already does) and swallows heart/droplet (it already does). Test: a
  flash treat that expires un-popped halves combo.
- **S4b-3 Darter spank lesson hook.** WPF `BubbleService.cs:3789` fires the lesson tick on a
  darter's FIRST smack when The Spanker is equipped (rabbits can't be caught with Spanker on,
  so the first smack is the only path). The Core engine declares `onDarterSpanked` but never
  invokes it. Wire the engine to invoke `onDarterSpanked` exactly once per darter (first
  pointer-down on a darter bubble), and have the service route it to
  `ChaosLessonHooks.OnRabbitSpanked()` (add the hook if Avalonia's ChaosLessonHooks lacks it —
  mirror WPF `ChaosLessonHooks.cs:134`). Test (Core, via a fake callback): first spank fires
  the callback; subsequent spanks on the same darter do not.
- **S4b-4 Live-lambda knobs (silk_touch / magnet / blindfold-opacity / cursor-pull).** WPF
  BeginChaosMode takes live lambdas (ChaosModeService.cs:361-381) the engine reads per-frame:
  `hitboxScale` (silk_touch 1.25), `liveMagnet` (silk_touch), `bubbleOpacity` (Blindfold),
  `cursorPull`/`rabbitHoming`/`spankerOn`/`spankGrow`/`electrifiedRabbits`/`chainReach`/etc.
  The Core engine takes static values at BeginChaosMode. Change the engine to accept
  `Func<T>` (or read them off a small `ChaosRunKnobs` snapshot object the service updates) so
  owned upgrades/boons actually take effect mid-run (X1-6 was only config-side until this).
  This is the largest sub-step — if it's too big for one safe pass, do just `hitboxScale` +
  `liveMagnet` (silk_touch) + `bubbleOpacity` (Blindfold) first and file the rest as a
  follow-up row. Cite each knob's WPF source line.

  S4b acceptance: each sub-step builds green, gates pass, WPF-cited comments, Core test pins
  the new behavior. **Do not rewrite the engine** — extend it minimally at the specific
  handler/materialize sites.

### S5 — Draft system faithful port [MECHANICAL]
`BeginWaveTransition` full choreography (pause/stop/wipe/PlayWaveClear/pulse/pending);
no-draft inline path; `ChaosBoonPool.Draft` with duo/trio ReqMet gating, sin-slot
(includeCurse roll + Surrender guarantee), Unique-taken exclusion (`TakenBoonIds`),
clamp 2-4; reroll (`RerollsLeft`); `OnBoonChosen` exact semantics (sin shielding,
first-times, lesson ticks, ApplyBoon, skip=+1 shield, announce, `ShowReadyGo`,
auto-resume 15s auto-SKIP); scripted first-run draft; `ResumeAfterDraft` incl. deferred
lesson cards + `ui_unlock` cue. **Unit tests** on Draft gating/sin ramp/unique exclusion.
**Exact targets:** WPF ChaosModeService.cs BeginWaveTransition :1448-1487, OnBoonChosen :1531-1610,
ResumeAfterDraft :1620-1660, TakenBoonIds :1510, RerollDraft :1519; ChaosBoonPool.Draft in
WPF ChaosModels.cs (grep `static.*Draft`). Avalonia target: the stand-in's ShowDraft/
OnBoonPicked/TriggerScriptedDraft in AvaloniaHeadStubs.cs (grep them). Extract the pure
Draft logic (ReqMet, sin-slot fill, unique exclusion, clamp) into Core
`ChaosDraftPool` (or extend ChaosRunRules) for testability. Contract ref:
draft-payloads-lifecycle.md §1 (every value verbatim).

### S6 — Payload dispatch + heavy gate + Ambient fix [MECHANICAL]
Collapse the stand-in's `BuildPayload` into `AvaloniaEffectPayloadFactory` (single map).
Port `FireScaledPayload` (lesson hook + DetonationDurationMult wrap) and
`FirePayloadForDetonation` (ambient remap: HtLink-only intrusive → cascade/text coin;
heavy gate Video/GifCascade + `_heavyUntilUtc`/`_chaosVideoCapUtc`; stingers by variant).
RunTick video 15s cap enforcement + `OnVideoEndured` lesson ticks. **Close the P3 row**:
`VideoPayload.Fire` gates `ArmRandomSegment` on `!Ambient` (per-instance flag from the
builder; do NOT conflate with cfg.AmbientMode). ALSO port the WPF `Build(ambient:)` branch
(WPF ChaosBubbleVariants.cs:714,767-773: forces IsLive=false/FuseMs=0/FloatUp/
TreatLifeMs=7000/payload.Ambient=true) that S1 intentionally deferred — S1 audit EXTRA-1. Welcome-shower/heart/golden chimes.
**Exact targets:** WPF ChaosModeService.cs FireScaledPayload :2196-2210,
FirePayloadForDetonation :2205-2260, HeavyEffectActive :2192, video cap in RunTick :1050-1076,
StingerForVariant :2247. Avalonia target: BuildPayload/FirePayload in AvaloniaHeadStubs.cs
+ AvaloniaEffectPayloads.cs + AvaloniaEffectPayloadFactory.cs. Contract ref:
draft-payloads-lifecycle.md §2 (verbatim).

### S7 — Lifecycle completion: EndRun/Cleanup/sentinel/SFX sweep [MECHANICAL, state-mutating → review before commit]
EndRun exact order (loop tip on full course, lessons OnRunCompleted, teardown list,
XP §3, `AwardRunRewards` faithful sparks §2 incl. TrickleDrops/drip capstone/first-fall,
`RevealService.Sync("run_end")`, rank-up card, results w/ baseXp/skillMult/finalXp split);
Relapse loop extension; T-10s beat; panic pause→stop; ForceShutdown; CleanupAfterRun
funnel; sentinel Mark cadence (run-start + `_memSampleTick>=60` ~15s) + Clear both sites;
`AwardLoopTip` per-loop detonation counter (fix the whole-run proxy); engine-fired SFX
cue sweep per contract §5 (glass_shatter fallback, ui_unlock, streak milestones,
depth_change, time_slow in/out guards, freeze_shatter, …). **Unit tests**: AwardRunRewards
formula (incl. first-fall once-ever, capstone), XP cap.
**Exact targets:** WPF ChaosModeService.cs EndRun :3122-3200, CleanupAfterRun :3227-3274,
AwardLoopTip + per-loop detonation (grep `AwardLoopTip`/`_waveDetonations`),
LogMemSample/sentinel Mark :860 + RunTick 15s branch :1048, panic OnPanicKeyDuringRun :285,
ForceShutdown :3085, ExtendOneLoop (S2 already ported on the state). Reward formula in
WPF ChaosUpgrades.cs AwardRunRewards :495-521. Avalonia target: EndRun/CleanupAfterRun/
BeginRun sentinel arming in AvaloniaHeadStubs.cs. Extract `AwardRunRewards` math into Core
(`ChaosEconomy` or extend ChaosScoring) for the unit test. Contract ref: economy-scoring.md
§2-3 + draft-payloads-lifecycle.md §3.4/3.6 (verbatim order).

### S8 — Layer production callers + hints [MECHANICAL]
Wire `ChaosPopTextLayer` (score/effect floaters at pop sites per WPF) and
`ChaosFieldFxLayer` (player ripple/snap ripple/residue/trail dots/bound tethers from
BubbleEngine field-hazard state + bound pairs). Port `ChaosBubbleHints` (KeyFor/TextFor/
learned-set via ChaosMeta + HideChaosHints). Verify `--verify-layers` still 15/15.
**Exact targets:** WPF ChaosBubbleHints.cs (KeyFor/TextFor/MarkLearned/IsLearned) +
ChaosModeService pop-site `ShowPopScore`/pulse callers. Avalonia target: the existing
`ShowChaosPopText` / `ChaosFieldRipple`/`SnapRipple`/`SetTether` seams on AvaloniaChaosService
(proven by --verify-layers; just add production callers). ChaosMeta.State.BubbleHintsLearned
for the learned set. Contract ref: spawn-system.md §6 (hints, verbatim key/text tables).

### S9 — Full-run verification + trackers
Exercise a complete run on the Windows head side-by-side with WPF (spawn feel, draft,
scoring HUD, results screen, sparks/XP, meta persistence across restart). FPS gate:
`--benchmark` during a heavy chaos run — 60fps target / 30 floor. Smoke baseline.
Update: task-board row → `✅ done` w/ evidence; parity matrix chaos rows; goal doc
Current state; UCE plan queue rows for the 6 unmigrated overlays (follow-up).

### Follow-up rows (NOT this workstream; file/keep on the board)
- **FLAKY head crash (seen once, 2026-07-04, during a smoke run):** unhandled
  `InvalidOperationException: The calling thread cannot access this object` in
  `SolidColorBrush.SerializeChanges` → `Compositor.CommitCore` — a background thread mutating
  a brush while the compositor serializes. NOT caused by chaos-port changes (repro run after
  was clean, 44 tabs / Findings: 5). Needs a hunt for off-UI-thread brush mutation (grep
  timers/Tasks that set `Brush`/`Color` props without `Dispatcher.UIThread`).
- Spanker toy port: when it lands, arm `ChaosKnobs.SpankerOn` per-run + add the spank
  physical reaction (WPF Spank(): random-heading fling, one-time level-scaled swell
  `_spankGrowth`, hot-pink glow, "SPANKED" label — BubbleService.cs:3770-3796).
- Migrate + wire the 6 remaining passive overlays: EStimGlow, EStim (bolts), WaveTimer,
  VibeTrail, FxWindow (vignette), SkiaFxOverlay (default glow renderer) → compositor layers.
- Hook click-swallow decision (WP3 JUDGMENT row).
- E-Stim visual chain callers once EStim layers exist.
- Narrative/story mode remains kill-switched (`StoryModeEnabled=false`) — director port is a
  separate backlog row.

## Slice evidence log (append per slice)

| Slice | Commit | Gates | Review | Notes |
|---|---|---|---|---|
| S1 | (this commit) | slnf 0 · WPF sln 0 · Core **268/268** (+63, floor 205 held) · smoke 44 tabs / Findings: 5 baseline / exit 0 | claim-verifier adversarial audit: C1–C18 all Verified vs WPF source (C17 weakened only over a docs-file edit; EXTRA-1 = deferred ambient Build branch → folded into S6 scope) | `ChaosSpawnCatalog.cs` (new, faithful 8-row pool + Pick/Build + 14 special builders, injectable Random), `ChaosSpawnCatalogTests.cs` (new, 63 tests), `ChaosBubbleSpec.cs` +`Strength` (additive). Deviations documented in XML docs: tease lifetime + bound window stamped on spec; PayloadKind = variant-id strings (matches Avalonia consumers). |
| S2 | (this commit) | slnf 0 · WPF sln 0 · Core **306/306** (+38) · smoke 44 tabs / Findings: 5 baseline (21 first-chance = known benign OAuth-cancel harness noise, verified per-exception) | claim-verifier adversarial audit: C1–C15 ALL Verified (incl. no double/lost boon-tile push, no stale writers, null-variant safety) | New Core `ChaosRunRules` + tests; `ChaosRunConfig.FromSettings` WPF-shape (clamps, ClampDifficulty pills, ClampVariants null=all, MotionOverride parse, SinChance ramp, **ChaosMeta.ApplyTo both paths** = X1-6 fixed); computed mult stack + `ExtendOneLoop` + faithful `ApplyBoon`; 6 upgrade Apply effects; intended changes: SinChance ramp, DraftAutoResumeSec 15, PopupHeartEnabled default false, FIRST_FALL_BONUS 25. |
| S3 | (this commit) | slnf 0 · WPF sln 0 · Core **341/341** (+35) · smoke 44 tabs / Findings: 5 baseline (first-chance 21 = same benign OAuth noise) | claim-verifier audit: C1–C14 ALL Verified (incl. hand-recomputing test arithmetic); no extra divergences | New Core `ChaosScoring` (pure formulas, WPF cites) + 35 tests; OnBenignPopped/OnDefused/OnDetonated rewired to exact WPF semantics — stand-in bugs fixed: detonation bare-hit now Combo=0+**Heat=0** (was Heat-=0.15), pickup paths now early-return (were firing payloads + generic scoring), golden pay via GoldenPayRange (was hardcoded 12-24), heart/droplet focus grants added, snap-chain invuln + collar branches ported, BankDripFeed + frozen-channel-free semantics. Deferred sub-items noted in agent report (pop floaters → S8, cam-girl tips/EStim rolls/barks → S6/S7). |
| S4 | (this commit) | slnf 0 · WPF sln 0 · Core **392/392** (+51) · smoke 44 tabs / Findings: 5 baseline (first-chance 21 = same benign OAuth noise) | claim-verifier audit: C1,C3-C16 Verified; **C2 resolved** (golden/prism/brittle riders nest inside the cap-gated ordinary block — this is WPF-faithful, matching ChaosModeService.cs:1122/1168-1201; the contract's 'NOT capped' wording referred to darters, which ARE outside at :728). Out-of-scope could-not-wire gaps explicitly listed (follow-up rows). | THE HEART: new Core `ChaosSpawnDirector` (pure spawn math) + 51 tests; SpawnTick/TrySpawnBehavioralBubble/RunTick faithful (interval retune, density cap, effIntensity bias, video strip, freeze re-pick, heavy-drop serial, golden/prism/brittle/darter rolls, empty-field rescue, heart arming, welcome shower); full behavioral callback widening via IBubbleService DIMs → AvaloniaBubbleService 16-arg path; echo children via Core BuildEchoChild ±70/±50; darter-speed DIP/frame→DIP/sec ×31.25 fix; OnDetonated tease/brittle guard against double-fire. |
| S4b-1/2/3 | (this commit) | slnf 0 · WPF sln 0 · Core **398/398** (+6) · smoke 44 tabs / Findings: 5 / first-chance 21 (one FLAKY unrelated cross-thread brush crash on a prior run — filed as follow-up row; repro run clean) | Agent-implemented, then smart-model reworked S4b-3 per WPF :3706-3708 (spank gated on SpankerOn/sweeper, REPLACES catch — kills the rabbit_caller double-tick the agent flagged); 3 parity tests pin spanker-on/spanker-off/sweeper | Bound enrage LIVES (fuse/2 floor 600ms, Vx/Vy ×1.4 — WPF BubbleService.cs:2321-2335); `IsRottingTreat` mirrors WPF `_isTreat` :2516 (ordinary+golden+prism rot → OnTreatExpired; heart/droplet/escort/tease/brittle never); darter first-smack hook + born-spanked sweepers (:3787); NEW `ChaosRunKnobs` live-knobs seam (`BubbleEngine.Knobs` / `IBubbleService.ChaosKnobs` DIM / AvaloniaBubbleService override) ready for S4b-4; `ChaosLessonHooks.OnRabbitSpanked` added. |
| S4b-4 | (this commit) | slnf 0 · WPF sln 0 · Core **426/426** (+20) · smoke 44 tabs / Findings: 5 / chaos run score 1391, sparks+XP flowing | Smart-model spot-audit: TickSpankSweeps gated `_chaosActive && !_chaosFrozen` + spanked-darters-only; chain-off-by-default verified as WPF default (`ChainReactionReach ?? 0` → `<=1.0` off) | ALL live knobs threaded: chainReach (live per-pop), hitboxScale/liveMagnet/bubbleOpacity (SPAWN-SAMPLED per WPF :2539-2542 — stamps `HitSize`/`Opacity` at materialize; goldens excluded :2532), cursorPull (30-DIP dead zone / 260-DIP Cam-Girl flee, per-second converted), rabbitHoming (0.065 rad/frame turn cap), spankGrow (one-time swell + ×1.18 redirect capped 2.2×), rabbitTrailSec (thin wrappers kept; sweeper Max(0.5) clamp), electrifiedRabbits (NEW TickSpankSweeps mow + ≤3-arc EStimBurstAt, 620px, free arcs). `SyncKnobsFromState()` at run start + every ApplyBoon. Knobs Reset+seed at Begin/End. NO run-state fields missing — S5 unblocked. Deferred: E-Stim Strike visuals/stagger (board tracks), charged-pop ArmEStim/onEStimArc chain, tease/brittle excluded from sweep (engine PopBubble routes tease as TOUCH=punish where WPF rewards — documented). |
| S5 | (this commit) | slnf 0 · WPF sln 0 · Core **448/448** (+22) · smoke 44 tabs / Findings: 5 / 0 unhandled | port-parity-auditor adversarial audit: 30 CLAIM rows ALL Verified, 0 Broken, 5 intentional low-consequence deviations; **SHIP**. Freeze-hold deviation proven safe empirically (RunTick is `_paused`-gated so the clock cannot re-cross waves mid-draft; engine zeroes `dt` when `_chaosFrozen` so no fuse detonates behind the table). Engine diff 0 lines. NOTE: the smoke gate had been red on a PRE-EXISTING audio-ducking crash-recovery boot loop (`WindowsSystemAudioDucker`, fixed separately in `8f4db7ca`), NOT S5 — with that fix smoke passes reliably. | New Core `ChaosDraftPool` (pure dealer, verbatim WPF `ChaosBoonPool.Draft` ChaosModels.cs:404-431; injectable `Random` + `reqMet`, `IChaosDraftCard` seam) + 22 tests (clamp 2-4, RequiresAny/All duo/trio gating, sin-slot guarantee + strict-`<` roll boundary, Unique-taken exclusion, boon top-up, determinism). AvaloniaChaosService port: BeginWaveTransition choreography + no-draft inline path; `OnBoonPicked` (WPF `OnBoonChosen` :1531-1610 — Surrender/rig sin shield, first-times via `ChaosLessonHooks`, `ApplyBoon`→`SyncKnobsFromState()` on every path, skip=+1 shield, `sin_accept` 0.6f, `ShowReadyGo`→`ResumeAfterDraft`); `TriggerScriptedDraft` (`_pendingWave` re-apply); `ResumeAfterDraft` (deferred lesson cards + `ui_unlock` 0.55f); `RerollDraft`; `FireActChangedIfCrossed` (`depth_change` 0.55f); `ToRoman` byte-identical. 6 `IBarkService` DIM members (no-op defaults) → `AvaloniaBarkService`. Deviation: freeze-hold (`SetChaosFrozen`+`SetChaosInputLocked`) replaces WPF `PopAllBubbles` silent wipe (engine frozen for S5; frozen fuses don't tick → no behind-draft detonation); follow-up row filed for a `ClearChaosBubblesSilent` engine seam. |
| S6 | — | — | — | — |
| S7 | — | — | — | — |
| S8 | — | — | — | — |
| S9 | — | — | — | — |
