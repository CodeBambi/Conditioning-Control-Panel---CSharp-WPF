# Chaos Run-Engine Faithful Port — Plan & Slice Tracker

Created 2026-07-04. Claim: task-board ledger row `bac65e4a` (@fable). Goal:
`docs/skia-rebuild-goal.md` WP3/WS2. This replaces the simplified `AvaloniaChaosService`
stand-in with a faithful port of WPF `Services/Chaos/ChaosModeService.cs` (3275L).

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
constants. Collapse the parallel `Avalonia.Chaos.ChaosTuning` into Core `ChaosTuning`
(single source). **Unit tests** pin: pool table rows, Pick gating/fallback, Build size/
strength/fuse formulas, golden/prism/heavy/darter spec numbers.
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

### S5 — Draft system faithful port
`BeginWaveTransition` full choreography (pause/stop/wipe/PlayWaveClear/pulse/pending);
no-draft inline path; `ChaosBoonPool.Draft` with duo/trio ReqMet gating, sin-slot
(includeCurse roll + Surrender guarantee), Unique-taken exclusion (`TakenBoonIds`),
clamp 2-4; reroll (`RerollsLeft`); `OnBoonChosen` exact semantics (sin shielding,
first-times, lesson ticks, ApplyBoon, skip=+1 shield, announce, `ShowReadyGo`,
auto-resume 15s auto-SKIP); scripted first-run draft; `ResumeAfterDraft` incl. deferred
lesson cards + `ui_unlock` cue. **Unit tests** on Draft gating/sin ramp/unique exclusion.

### S6 — Payload dispatch + heavy gate + Ambient fix
Collapse the stand-in's `BuildPayload` into `AvaloniaEffectPayloadFactory` (single map).
Port `FireScaledPayload` (lesson hook + DetonationDurationMult wrap) and
`FirePayloadForDetonation` (ambient remap: HtLink-only intrusive → cascade/text coin;
heavy gate Video/GifCascade + `_heavyUntilUtc`/`_chaosVideoCapUtc`; stingers by variant).
RunTick video 15s cap enforcement + `OnVideoEndured` lesson ticks. **Close the P3 row**:
`VideoPayload.Fire` gates `ArmRandomSegment` on `!Ambient` (per-instance flag from the
builder; do NOT conflate with cfg.AmbientMode). Welcome-shower/heart/golden chimes.

### S7 — Lifecycle completion: EndRun/Cleanup/sentinel/SFX sweep
EndRun exact order (loop tip on full course, lessons OnRunCompleted, teardown list,
XP §3, `AwardRunRewards` faithful sparks §2 incl. TrickleDrops/drip capstone/first-fall,
`RevealService.Sync("run_end")`, rank-up card, results w/ baseXp/skillMult/finalXp split);
Relapse loop extension; T-10s beat; panic pause→stop; ForceShutdown; CleanupAfterRun
funnel; sentinel Mark cadence (run-start + `_memSampleTick>=60` ~15s) + Clear both sites;
`AwardLoopTip` per-loop detonation counter (fix the whole-run proxy); engine-fired SFX
cue sweep per contract §5 (glass_shatter fallback, ui_unlock, streak milestones,
depth_change, time_slow in/out guards, freeze_shatter, …). **Unit tests**: AwardRunRewards
formula (incl. first-fall once-ever, capstone), XP cap.

### S8 — Layer production callers + hints
Wire `ChaosPopTextLayer` (score/effect floaters at pop sites per WPF) and
`ChaosFieldFxLayer` (player ripple/snap ripple/residue/trail dots/bound tethers from
BubbleEngine field-hazard state + bound pairs). Port `ChaosBubbleHints` (KeyFor/TextFor/
learned-set via ChaosMeta + HideChaosHints). Verify `--verify-layers` still 15/15.

### S9 — Full-run verification + trackers
Exercise a complete run on the Windows head side-by-side with WPF (spawn feel, draft,
scoring HUD, results screen, sparks/XP, meta persistence across restart). FPS gate:
`--benchmark` during a heavy chaos run — 60fps target / 30 floor. Smoke baseline.
Update: task-board row → `✅ done` w/ evidence; parity matrix chaos rows; goal doc
Current state; UCE plan queue rows for the 6 unmigrated overlays (follow-up).

### Follow-up rows (NOT this workstream; file/keep on the board)
- Migrate + wire the 6 remaining passive overlays: EStimGlow, EStim (bolts), WaveTimer,
  VibeTrail, FxWindow (vignette), SkiaFxOverlay (default glow renderer) → compositor layers.
- Hook click-swallow decision (WP3 JUDGMENT row).
- E-Stim visual chain callers once EStim layers exist.
- Narrative/story mode remains kill-switched (`StoryModeEnabled=false`) — director port is a
  separate backlog row.

## Slice evidence log (append per slice)

| Slice | Commit | Gates | Review | Notes |
|---|---|---|---|---|
| S1 | — | — | — | — |
| S2 | — | — | — | — |
| S3 | — | — | — | — |
| S4 | — | — | — | — |
| S5 | — | — | — | — |
| S6 | — | — | — | — |
| S7 | — | — | — | — |
| S8 | — | — | — | — |
| S9 | — | — | — | — |
