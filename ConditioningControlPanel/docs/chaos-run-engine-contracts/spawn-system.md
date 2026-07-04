# WPF Chaos Bubble Spawn System — Behavior Contract

Extracted 2026-07-04 by archaeology agent for the chaos run-engine faithful port
(claim `bac65e4a`, plan `docs/chaos-run-engine-port-plan.md`). All references are
`file:line` in `ConditioningControlPanel/Services/Chaos/`: `ChaosModeService.cs` (CMS),
`ChaosBubbleVariants.cs` (CBV), `ChaosTuning.cs` (CT), `ChaosBubbleHints.cs` (CBH),
`ChaosModels.cs` (CM). Constants/formulas verbatim from WPF as of `feat/crossplatform`.

---

## Section 1 — The Spawn Loop: Cadence, Intensity, Density, Waves

### 1.1 Timers

**CONTRACT: Two DispatcherTimers drive the run.**
- `CMS:504-506` — `_runTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }`; `Tick += RunTick`. Fires 4×/sec.
- `CMS:507-509` — `_spawnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) }`; `Tick += SpawnTick`. Initial interval 800ms; **re-armed every tick** (see 1.4).

**CONTRACT: RunTick advances the clock by a fixed 0.25s.** `CMS:949-950` — `double dt = 0.25; double elapsed = _state.ElapsedSec + dt;`. The run clock is wall-clock-independent — pure tick-count × 0.25s. Power-ups (slow-mo/freeze) run on the real clock and do NOT extend run length (`CMS:963-975`).

### 1.2 Intensity escalation curve

**CONTRACT: `RunIntensity` is a linear 0→1 ramp over the whole run.**
`CM` (ChaosRunState) — `RunProgress => Clamp(ElapsedSec / RunDurationSec, 0, 1)`; `RunIntensity => Math.Clamp(RunProgress, 0, 1)`. No easing.

**CONTRACT: `effIntensity` folds difficulty into intensity for size/strength/behavioral rolls.**
`CMS:1111` — `double effIntensity = Math.Clamp(intensity + (cfg.DifficultyMult - 1.0) * 0.15, 0, 1);`
- `DifficultyMult` (`CM` ChaosRunConfig.DifficultyMult): Easy=1.0, Medium=1.3, Hard=1.7, Extreme=2.2.
- Flat intensity bias: Easy +0.0, Medium +0.045, Hard +0.105, Extreme +0.18.

**CONTRACT: `intensity` (raw, NOT eff) drives density cap and refill cadence.** `CMS:1107-1109`.

### 1.3 Density cap (max concurrent bubbles)

**CONTRACT: max concurrent = `round((6 + intensity*10) * sqrt(DifficultyMult))`.**
`CMS:1117` — Easy: 6→16; Extreme (√2.2 ≈ 1.483): 9→24.

**CONTRACT: cap gates ordinary + behavioral spawns only; darters, golden/prism/brittle riders, and pair-spawners are NOT capped.**
- `CMS:1120` — behavioral roll gated: `(App.Bubbles?.ActiveBubbles ?? 0) < maxConcurrent && TrySpawnBehavioralBubble(...)`.
- `CMS:1122` — ordinary spawn gated: `if (!behavioralSpawned && (App.Bubbles?.ActiveBubbles ?? 0) < maxConcurrent)`.
- Darter roll (`CMS:1206-1214`), golden (`1170`), prism (`1178`), brittle (`1188`) fire outside/independent of the cap check.

OPEN-QUESTION: exact population `ActiveBubbles` counts lives in `BubbleService`; port must mirror ("count of live chaos bubbles currently on the field").

### 1.4 Refill cadence (interval evolution)

**CONTRACT: base interval `= (1000 - intensity*680) / DifficultyMult`.** `CMS:1220` — Easy 1000→320ms; Extreme 454→145ms (before floor).
**CONTRACT: SpawnRateMult divides the interval.** `CMS:1223` — `interval /= Math.Clamp(cfg.SpawnRateMult, 0.1, 10.0);` default 1.0; scripted first descent ≈0.6.
**CONTRACT: slow-mo stretches cadence.** `CMS:1224` — `if (_slowMoRemainingSec > 0) interval /= SLOWMO_FACTOR;` `SLOWMO_FACTOR = 0.12` (`CMS:2323`).
**CONTRACT: floor 280ms, then perf-governor backoff.** `CMS:1227` — `interval = Math.Max(280, interval) * _perfBackoff;` then re-arm `CMS:1228`.
**CONTRACT: freeze halts new spawns.** `CMS:1107` — `if (_freezeRemainingSec > 0) return;`. Full guard `CMS:1106` — `if (!_spawning || _state == null || _paused || _manualPaused) return;`
**CONTRACT: empty-field rescue.** `CMS:978-980` (RunTick) — bare field triggers immediate SpawnTick.

### 1.5 Wave structure

**CONTRACT: waves are equal time-slices; count from `Config.WaveCount`.**
- `CMS:1093-1095` — `waveLen = RunDurationSec / WaveCount; newWave = Min(WaveCount, 1 + (int)(elapsed/waveLen)); WaveProgress = (elapsed % waveLen)/waveLen;`
- `CMS:1101` — `if (newWave > WaveIndex) BeginWaveTransition(newWave);`
- `WaveCount` clamp `1..12` (`CM` FromSettings), default 5. DurationSec default 180, clamp 60–900.
- `ActIndex = 1 + (newWave - 1) / 5` (`CMS:1451`).

**CONTRACT: wave boundary (drafts enabled) PAUSES field, clears all bubbles, opens boon draft.**
`CMS:1464-1486` — `_paused = true; _spawnTimer?.Stop(); App.Bubbles?.PopAllBubbles();` → `ChaosBoonPool.Draft(...)` → `_overlay?.ShowBoonDraft(...)`. With `BoonDraftEnabled == false` (`CMS:1450-1455`): inline advance, no pause.

**CONTRACT: no per-wave variant reweighting.** Pool weights static; escalation only via RunIntensity + drafts. `AllLiveNextWave` is a DEAD flag (assigned `CMS:1454/1475`, never read) — do-not-port unless WPF wires it.

**CONTRACT: Welcome Shower at run start and each loop GO.** `CMS:435/1623/202` — `if (_state.WelcomeShowerEnabled) SpawnWelcomeShower();` (§5.7).

**CONTRACT: Relapse sin can bolt one extra loop.** RunTick end-of-run: if `RelapseLoopArmed && !RelapseLoopActive`, `_state.ExtendOneLoop()` (`WaveCount += 1; RunDurationSec += waveLen; RelapseLoopActive = true`); else `EndRun()`.

---

## Section 2 — The Weighted Variant Pool

### 2.1 The base pool table (`CBV` `All`)

Columns: `(Id, Name, PayloadKind, OverlayKind, IsLive, MinSize, MaxSize, Motion, Tint(RGB), Label, Weight, MinIntensity, FuseMinMs, FuseMaxMs)`:

| Id | IsLive | MinSize | MaxSize | Motion | Tint | Label | Weight | MinIntensity | Fuse Min | Fuse Max |
|---|---|---|---|---|---|---|---|---|---|---|
| `flash` | false | 150 | 210 | FloatUp | `FFD0E8` | (empty) | **3.0** | 0.00 | 0 | 0 |
| `subliminal` | false | 170 | 220 | FloatUp | `B080FF` | `♥` | **3.0** | 0.00 | 0 | 0 |
| `pink` | true | 180 | 240 | RainDown | `FF3DA5` | `◑` | **2.0** | 0.10 | 3500 | 5000 |
| `spiral` | true | 180 | 240 | RoamBounce | `40D0C0` | `◎` | **2.0** | 0.15 | 3500 | 5000 |
| `braindrain` | true | 240 | 320 | RoamBounce | `4060C0` | `☁` | **1.4** | 0.25 | 4500 | 6500 |
| `bambifreeze` | false | 190 | 250 | FloatUp | `8AE6FF` | `❄` | **0.5** | 0.15 | 0 | 0 |
| `video` | true | 240 | 300 | RainDown | `E0404D` | `▶` | **0.5** | 0.50 | 5000 | 7000 |
| `htlink` (Gif Rain) | true | 200 | 280 | FloatUp | `FFC83D` | `▼` | **0.45** | 0.60 | 4500 | 6500 |

PayloadKind per row: flash=Flash, subliminal=Subliminal, pink=Overlay("pink_filter"), spiral=Overlay("spiral"), braindrain=Overlay("braindrain"), bambifreeze=BambiFreeze, video=Video, htlink=GifCascade. Notes: bambifreeze weight halved 2026-06-12 + hard cap 2 on screen; htlink renamed "Gif Rain" 2026-06-10, id kept for persistence.

### 2.2 Weighted picker with intensity gating (`CBV` `Pick`)

```
pool = All.Where(v => intensity >= v.MinIntensity && v.Weight > 0
                      && (enabledIds == null || enabledIds.Contains(v.Id)));
if (pool.Count == 0) pool = All.Where(v => v.Weight > 0 && enabled-filter);  // fall back past intensity gate
if (pool.Count == 0) pool = { All[0] };  // last-ditch: flash
weight-roll: roll = rng.NextDouble()*Sum(Weight); walk & subtract; default pool[^1]
```
`Pick` receives `effIntensity` (`CMS:1148-1149`) so difficulty bias surfaces gated variants earlier. **Weights DO NOT change with intensity, upgrades, or boons.**

### 2.3 Golden bubble roll

**CONTRACT:** every ordinary spawn rolls `_state.GoldenChance` (`CMS:1170-1175`), default 0.005 (Rabbit's Foot boon raises). `BuildGolden` (`CBV`): size 110–140, `SpeedMult=2.8`, Tint `FFD700`, Label `🍀`, `IsGolden=true`, payload `FlashPayload{Strength=0}` (benign), Motion 50/50 FloatUp/RainDown. Pop (`OnBenignPopped` `CMS:1803-1830`): `Focus += FOCUS_PER_GOLDEN(12)`; gold = `Random.Next(gMin,gMax+1)` from `ChaosLifetimeBoons.GoldenPayRange(rabbits_foot level)` (10–20 … 20–40 capstone) via `BankGold`. `GoldDiggerEnabled` → 3 `BuildGoldDroplet` at pop point. Chime 0.30f.

### 2.4 Prism roll

**CONTRACT:** per-ordinary-spawn rider at `_state.PrismChance` (`CMS:1178-1183`), default 0; `bright_colors` sin sets 0.05 (shielded variant also `PrismTreatOnly=true`). `BuildPrism` (`CBV`): pool = `All.Where(v => v.Id != "video" && v.PayloadKind != BambiFreeze && (!treatOnly || !v.IsLive))`, uniform pick. Size 165–215 × `GLOBAL_SIZE_SCALE`, `SpeedMult=0.7`, Tint `C8A8FF`, Label `❂`, `IsPrism=true`, `MimicVariantId`, Motion 50/50 RainDown/RoamBounce. Payload = copied variant's; `Strength = clamp(round(clamp((size-150)/170,0,1)*100) * effectIntensity, 0,100)`. Pop (`CMS:1829-1852`): payload fires, `Focus += FOCUS_PER_PRISM(10)`, `Combo++`, `Heat += 0.05`, score `= BasePoints(Strength) * 10.0 * TotalMult * BoonPayMult` (**10× pay**).

### 2.5 Heavy Drop

**CONTRACT:** `CMS:1138-1141` — `if (HeavyDropEvery > 0 && ++_spawnSerial % HeavyDropEvery == 0) spec = BuildHeavy(...)`. `HeavyDropEvery` default 0; `heavy_drop` boon sets 10. `_spawnSerial` reset at BeginRun (`CMS:401`). `BuildHeavy` (`CBV`): variant = `All[rng.Next(2)]` (flash/subliminal), `SizePx = classicMax * GLOBAL_SIZE_SCALE * Max(0.5,sizeScale) * HEAVY_SIZE_MULT(1.55)`, `SpeedMult=0.45`, `PayMult=3.0`, `TreatLifeMs=9000`, RainDown, no fuse. Focus on pop: `FOCUS_PER_HEAVY(15)` (chosen when `spec.PayMult>1`).

### 2.6 Video end-of-loop exclusion

**CONTRACT:** `CMS:1127-1134` — strip `video` from `enabled` when `HeavyEffectActive || waveLeft < 14 || runLeft < 18` (waveLeft/runLeft computed from waveLen/elapsed). `HeavyEffectActive` (`CMS:2161`): `App.Video?.IsPlaying == true || ChaosGifCascadeOverlay.IsRaining || DateTime.UtcNow < _heavyUntilUtc`.

### 2.7 Freeze on-screen cap re-pick

**CONTRACT:** `CMS:1155-1162` — if picked spec `IsFreeze` and `ActiveFreezeBubbles >= FREEZE_MAX_ON_SCREEN(2)`, re-pick with `bambifreeze` excluded.

---

## Section 3 — Behavioral Bubbles

**CONTRACT: rolled in `TrySpawnBehavioralBubble` (`CMS:1244-1329`), REPLACE the ordinary spawn slot; debut spawns alone.** Roll order: Echo → Chaperone → Bound → Tease; first hit wins. Brittle is a separate rider in the ordinary block (§3.5). Gating is by **RANK** with `gentleMult = (Difficulty == Easy) ? 0.5 : 1.0` (`CMS:1247`). `ScriptedFirstRun` disables all (`CMS:1246`). First encounter: sets `Seen*` (persisted `ChaosMeta.Save()`), `ChaosAnnouncerOverlay.Announce`, feed line, spawn with `DEBUT_FUSE_MULT(1.5)`.

### 3.1 Echo — rank Tempted+, chance `0.05 × gentleMult` (`CMS:1249-1250`)
`BuildEcho` (`CMS:1261`, `CBV`): size 180–240, `t = clamp(rng*0.7 + intensity*0.45,0,1)`, fuse `= max(1200, (3500 + rng.Next(1500)) * (1 - intensity*0.25) * fuseTimeMult * max(0.1, fuseMult))`, Tint `C9C4E8`, Label `◌`, `IsLive=true`, `IsEcho=true`, FloatUp, payload Strength 0 (never fires). Trigger (timeout/click/early release/no-focus touch) → `SpawnEchoChildren`: 2× `BuildEchoChild(parent.SizePx, ChaosLastPopXPx±70, ChaosLastPopYPx±50, EffectIntensity)`. Completed hold-defuse deflates cleanly. `BuildEchoChild`: `v = All[2+rng.Next(3)]` (pink/spiral/braindrain), `size = max(60, parent*0.6)`, fuse 2500 + rng.Next(500), `SpeedMult=1.5`, RoamBounce, live, never re-splits. Split cue: `Pulse(C9C4E8, 0.30)`.

### 3.2 Chaperone — rank Tempted+, chance `0.04 × gentleMult` (`CMS:1267-1268`)
`BuildChaperonePair` → `App.Bubbles?.SpawnChaosChaperone(live, escort)` (`CMS:1278`). Live = pink/spiral/braindrain, `IsChaperoneLive=true`, RoamBounce, standard live fuse. Escort = flash/subliminal treat, size 95–120, `IsEscort=true`, strength floored `Max(10, estrength)`. Orbit (`CT`): radius 80 DIP + gap 18 + period 2.5s. Pop escort first (normal treat: score AND focus) → live becomes standard defusable; pops bounce off live while escort alive; escort never rots. OPEN-QUESTION: shield/orbit enforcement lives in `BubbleService.SpawnChaosChaperone` — port there.

### 3.3 Bound — difficulty Hard+ OR rank Entranced+, chance `0.03 × gentleMult` (`CMS:1287-1288`)
`BuildBoundPair` → `SpawnChaosBoundPair(a,b)` (`CMS:1299`). Shared `PairId`; each half pink/spiral/braindrain, `IsBoundHalf=true`, RoamBounce, standard fuse. Placed `BOUND_SEPARATION_DIP(250)` apart, mirrored drift, elastic thread in field-FX. Each half costs `DEFUSE_COST_BOUND(15)`; second must complete within `BOUND_WINDOW_MS(2500)`; one triggering enrages the other: remaining trance halves, speed ×1.4, `ChaosSfx.Play("toy_denied", 0.5f)` + `Pulse(FF4A4A,0.30)` + feed "⛓ the tether snaps — it enrages".

### 3.4 Tease — rank Slipping+, chance `0.03 × gentleMult` (`CMS:1306-1307`)
Debut fires `App.Bark?.NotifyChaosTeaseDebut()`. `BuildTease`: pool excludes video + BambiFreeze, uniform pick, size 170–210 × scale, Tint `B30E2E`, Label `✖`, `IsTease=true`, not live, RoamBounce (center-pull + wiggle), lifetime `TEASE_LIFE_MS(6000)`. Touched (`OnTeaseTouched`): payload fires (resistance absorbs payload only), `Detonated++`, `Combo = Combo>1 ? Combo/2 : 0`, `Pulse(FF3D5A,0.38)`, bark. Denied (`OnTeaseDenied`): gold `GoldScaled(rng 5..10)`, score `= 120 * TotalMult * BoonPayMult`, `Focus += 10`, announce "DENIED", `Pulse(FFD700,0.25)`; after 5 denials one-shot streak bark. Perf: max 2 animated decodes, 3MB cap. Immune to toys/chains.

### 3.5 Brittle — rank Tempted+, rider `0.035 × (Easy?0.5:1.0)` (`CMS:1188-1201`)
Debut announce "◇ THE BRITTLE — don't even hover". `BuildBrittle`: pool = ALL live rows (incl. video/htlink), uniform; size 150–185 × `GLOBAL_SIZE_SCALE` × `Max(0.5,sizeScale)`, Tint `D9EFFF`, Label `◇`, `IsBrittle=true`, not live (no fuse), Motion 50/50 FloatUp/RainDown vertical-only, `SpeedMult=0.85`, `MimicVariantId`. Hover shatters (`OnBrittleShattered`): `ChaosSfx.Play(ResolvePath("glass_shatter").Length>0 ? "glass_shatter" : "trigger", 0.55f)`; resistance can absorb payload (streak SPARED); never a missed trance; `Pulse(BFE6FF,0.32)`. `BRITTLE_ARM_MS(900)` spawn grace; immune to toys/chains/sweeps; safe when field frozen.

### 3.6 Rank gates
Echo/Chaperone/Brittle = Tempted; Bound = Entranced (or ≥Hard); Tease = Slipping. Docstring `CMS:1234-1242`.

---

## Section 4 — EnabledVariants Semantics

**CONTRACT: `Config.EnabledVariants` is `List<string>?`; null = ALL.** (`CM` ChaosRunConfig.)
- From settings: `cfg.EnabledVariants = ClampVariants(s.ChaosEnabledVariants)` (`CM` FromSettings).
- `ClampVariants`: if both `video` and `htlink` reveals unlocked → saved list untouched (may stay null). Else returns NEW list = `saved ?? AllIds()` minus locked ids (`RevealService.IsUnlocked(VariantVideo/VariantHtlink)`). Saved setting never mutated.
- `Pick()` treats null as no filter; freeze re-pick uses `enabled ?? AllIds()` (`CMS:1157`); video-strip only when `enabled != null && Contains("video")` (`CMS:1131`).
- **Boons/sins never filter the pool** — they add riders or flip run-state knobs. Only narrowing: MinIntensity gate, end-of-loop video strip, freeze cap re-pick, reveal-lock ClampVariants.
- Presets (`CBV`) are UI conveniences only: Balanced=AllIds, Tease={flash,subliminal,pink,spiral,bambifreeze}, Flash-only={flash,subliminal}.

---

## Section 5 — Bubble Spec Creation (`EffectBubbleSpec` + `Build`)

### 5.1 Global constants (`CBV`)
`SizeMinGlobal = 150`, `SizeMaxGlobal = 320`, `GLOBAL_SIZE_SCALE = 0.75`, `GIANT_SIZE_SCALE = 0.70` (video+htlink only).

### 5.2 Size formula
```
t = Math.Clamp(rng.NextDouble() * 0.7 + intensity * 0.45, 0, 1);
size = variant.MinSize + (variant.MaxSize - variant.MinSize) * t;   // "classic" size
```

### 5.3 Strength formula (keyed to CLASSIC size, pre-scale)
```
strength = round(Clamp((size - 150) / 170, 0, 1) * 100);
visual = 0.75 * Max(0.5, sizeScale);  if (video|htlink) visual *= 0.70;
size *= visual;
payload.Strength = Clamp(strength * effectIntensity, 0, 100);
```
`sizeScale` = `_state.BubbleScale` (default 1.0; Breast Enlargement raises; floor 0.5). `effectIntensity` = `cfg.EffectIntensity` (clamp 0.2–1.5, default 0.85).

### 5.4 Payload build
`payload = (PayloadKind == Overlay && OverlayKind != null) ? new OverlayPayload(OverlayKind) : EffectPayloadFactory.Build(PayloadKind);`

### 5.5 Motion resolution
```
motion = motionOverride ?? variant.Motion;
if (isFreeze && motion == RoamBounce) motion = FloatUp;
if (motionOverride == null && motion != RoamBounce && rng < sideDriftChance) motion = SideDrift;
```
`sideDriftChance` = 0 for first `SIDE_DRIFT_GRACE_SPAWNS(5)` ordinary spawns, then `SIDE_DRIFT_CHANCE(0.30)` (`CMS:1147-1148`).

### 5.6 Fuse formula (live only)
```
baseFuse = FuseMinMs + rng.Next(Max(1, FuseMaxMs - FuseMinMs));
fuse = (int)Max(1200, baseFuse * (1.0 - intensity * 0.25) * fuseTimeMult);
```
`fuseTimeMult` = `_state.FuseTimeMult` (default 1.0; Hair Trigger ×0.75; seeded from owned upgrades).

### 5.7 Speed / lifetime / positions / special specs
- SpeedMult: golden 2.8, heart 0.8, droplet 2.2, heavy 0.45, prism 0.7, brittle 0.85, echo child 1.5; ordinary 1.0.
- Global motion multipliers applied in BubbleService (NOT Build): `CT.CHAOS_SPEED_MULT = 1.4375`, `CT.FIELD_PACE = 0.8`.
- `TreatLifeMs`: 0 = standard 5s rot; heavy 9000; ambient reuse 7000. Lives use fuse.
- Spawn position defaults per motion (resolved in BubbleService); `SpawnAtPxX/Y` pins: echo children `±70/±50` from `ChaosLastPopXPx/YPx`, gold droplets `±50/±20`, Rabbit Caller.
- Welcome Shower (`CMS:1649-1665`): 6× flash/subliminal treats, RainDown, chime 0.25f; at run start + each loop GO when `WelcomeShowerEnabled`.
- Darter (`CBV` RollDarter/BuildDarter): chance `(0.0125 + Clamp(intensity,0,1)*0.03) * Max(0, RabbitRateMult)`; per SpawnTick when `cfg.DartersEnabled`, cap-independent (`CMS:1206-1214`). Spec: size 72–96 (×1.15 spotlight), `IsDarter=true`, RoamBounce, `LifetimeMs=8000` backstop (real: 3 bounces `DARTER_MAX_BOUNCES=3`), Telegraph 400ms (sweeper 150), `QuickWindowMs=500`, `DarterSpeed=9.0` DIP/frame, payload FlashPayload{8}. Tint `FF4DC4`. Score: base 120 + quick bonus 90.
- GG-rabbit sweepers (`gg_rabbits` boon, `GgRabbitChance=0.15`): popped treat births 3 sweeper darters (`BuildDarter(..., sweeper:true)`, `IsSweeper=true`); spawn path in treat-pop handler.
- Heart (RunTick `CMS:2900-2911`, when `PopupHeartEnabled`): per new wave arm at 60% + fire progress `0.20 + rng*0.60`; on `WaveProgress >= fireAt` spawn `BuildHeart()` + chime 0.22f. Size 88–110, `SpeedMult=0.8`, RainDown, `IsHeart=true`; catch = +1 resistance + `FOCUS_PER_HEART(10)`.

---

## Section 6 — ChaosBubbleHints

**CONTRACT:** first-contact verb hint pill under every chaos bubble whose interaction isn't learned; first correct play marks learned forever (`CBH`).
- `KeyFor(spec)` priority: IsSweeper→null; IsDarter→"rabbit"; IsFreeze→"freeze"; IsTease→"tease"; IsBrittle→"brittle"; IsEscort||IsChaperoneLive→"chaperone"; IsEcho→"echo"; IsBoundHalf→"bound"; IsGolden→"golden"; IsHeart→"heart"; IsDroplet→"droplet"; IsPrism→"prism"; PayMult>=2.0→"heavy"; else `(IsLive?"live:":"treat:") + VariantId`. Null = no hint.
- `TextFor`: chaperone live "pop my escort first" / escort "pop me first"; `live:*` "hold to snap"; `treat:*` "click to pop"; rabbit "click to catch"; freeze "click to freeze"; tease "don't touch. let it leave"; brittle "glass. dodge it"; echo "hold fully or it splits"; bound "hold both. fast"; golden "pop for gold"; heart "click. +1 resistance"; droplet "catch the gold"; prism "pop. pays 10x"; heavy "click. pays x3".
- Learned-set persisted in `ChaosMeta.State.BubbleHintsLearned`; `MarkLearned` → add + `ChaosMeta.Save()` + `App.Bubbles?.HideChaosHints(key)`. Fails toward NO hint.

---

## Section 7 — ChaosTuning Constants Read by the Spawn Path

Behavioral chances (CT): `ECHO_SPAWN_CHANCE=0.05`, `CHAPERONE_SPAWN_CHANCE=0.04`, `TEASE_SPAWN_CHANCE=0.03`, `BOUND_SPAWN_CHANCE=0.03`, `BRITTLE_SPAWN_CHANCE=0.035`, `DEBUT_FUSE_MULT=1.5`.

Field pace (CT): `SIDE_DRIFT_CHANCE=0.30`, `SIDE_DRIFT_GRACE_SPAWNS=5`, `FREEZE_MAX_ON_SCREEN=2`, `CHAOS_SPEED_MULT=1.4375`, `FIELD_PACE=0.8`.

Echo children (CT): `ECHO_CHILD_SCALE=0.6`, `ECHO_CHILD_SPEED_MULT=1.5`, `ECHO_CHILD_FUSE_MIN_MS=2500`, `ECHO_CHILD_FUSE_MAX_MS=3000`.

Bound (CT): `BOUND_WINDOW_MS=2500`, `DEFUSE_COST_BOUND=15`, `BOUND_SEPARATION_DIP=250`, `BOUND_ENRAGE_SPEED_MULT=1.4`.

Chaperone (CT): `CHAPERONE_ORBIT_RADIUS_DIP=80`, `CHAPERONE_ORBIT_GAP_DIP=18`, `CHAPERONE_ORBIT_PERIOD_SEC=2.5`.

Tease (CT): `TEASE_LIFE_MS=6000`, `TEASE_GOLD_MIN=5`, `TEASE_GOLD_MAX=10`, `TEASE_DENIED_SCORE=120`, `FOCUS_PER_DENIED=10`, `TEASE_CENTER_PULL_DIP=0.55`, `TEASE_DENIED_STREAK_COUNT=5`, `TEASE_MAX_ANIMATED=2`, `TEASE_ANIMATED_MAX_BYTES=3_000_000`.

Brittle (CT): `BRITTLE_ARM_MS=900`, `BRITTLE_SPEED_MULT=0.85`.

Focus economy (CT): `FOCUS_MAX=100`, `FOCUS_START=50`, `FOCUS_PER_POP=10`, `FOCUS_PER_GOLDEN=12`, `FOCUS_PER_DROPLET=4`, `FOCUS_PER_HEART=10`, `FOCUS_PER_RABBIT=15`, `FOCUS_PER_PRISM=10`, `FOCUS_PER_HEAVY=15`; `DEFUSE_COST=30`, `DEFUSE_HOLD_MS=1000`, `CLICK_THRESHOLD_MS=180`, `CHANNEL_MIN_SCALE=0.55`.

Size/scale (CBV): `SizeMinGlobal=150`, `SizeMaxGlobal=320`, `GLOBAL_SIZE_SCALE=0.75`, `GIANT_SIZE_SCALE=0.70`.

Special bands (CBV): Darter `LIFETIME_MS=8000/QUICK_WINDOW_MS=500/TELEGRAPH_MS=400/SPEED=9.0/MAX_BOUNCES=3/SIZE 72-96/BASE_POINTS=120/QUICK_BONUS=90`; Golden `110-140/SPEED 2.8`; Heart `88-110/0.8`; Droplet `58-74/2.2`; Heavy `SIZE_MULT 1.55/SPEED 0.45/PAY 3.0`; Prism `165-215/0.7`; Brittle `150-185`; Echo `180-240`; Tease `170-210`; Escort `95-120`.

Power-ups (CMS): `SLOWMO_FACTOR=0.12` (:2323), `SLOWMO_DURATION_SEC=6.0` (:2324), `FREEZE_DURATION_SEC=3.5` (:2957), `FREEZE_DURATION_MULT=2.5`, `FREEZE_VIBRATE_MS=200`, `FREEZE_BASE_POINTS=140`, `VIDEO_HARD_CAP_SEC=15` (:2156), `VIDEO_TEARDOWN_QUARANTINE_SEC=3`.

---

## Appendix — Run-state defaults read by the spawn path (`CM`)

`GoldenChance=0.005`; `PrismChance=0` (0.05 via `bright_colors`), `PrismTreatOnly=false`; `HeavyDropEvery=0` (10 via `heavy_drop`); `GgRabbitChance=0` (0.15 via `gg_rabbits`); `RabbitRateMult=1.0`; `FuseTimeMult=1.0`; `BubbleScale=1.0`; `WelcomeShowerEnabled=false`; `PopupHeartEnabled=false`. Config defaults: `WaveCount=5`, `DurationSec=180`, `EffectIntensity=0.85`, `SpawnRateMult=1.0`, `DartersEnabled=true`, `EnabledVariants=null`.

## Delta vs the current Avalonia stand-in

WPF is NOT "1 bubble / 900ms at flat 45% from 3 ids". It is: an 800ms self-retuning
timer with interval `(1000 − intensity·680)/DifficultyMult` (floor 280ms, ×perfBackoff,
÷SpawnRateMult, ÷slow-mo); density cap `round((6+intensity·10)·√diff)` (6→24); an 8-row
weighted pool (0.45–3.0) with MinIntensity gates; golden/prism/brittle/heavy riders;
five rank-gated behavioral bubbles replacing the ordinary slot; darters on an
independent roll; welcome-shower + once-per-loop heart; equal-time-slice waves with
between-wave drafts; size/strength/fuse/motion keyed to linear RunIntensity.
