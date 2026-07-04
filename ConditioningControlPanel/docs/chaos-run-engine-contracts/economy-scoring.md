# WPF Chaos ECONOMY + SCORING — Behavior Contract

Extracted 2026-07-04 by archaeology agent for the chaos run-engine faithful port
(claim `bac65e4a`, plan `docs/chaos-run-engine-port-plan.md`). All file:line refs are
against `ConditioningControlPanel/Services/Chaos/`. Formulas verbatim. Known Avalonia
divergences (X1-*) flagged inline.

---

## 1. Per-pop SCORING

### CONTRACT: Base points per bubble — `ChaosModeService.cs:1670`
```csharp
private double BasePoints(int strength) => 40 + strength * 1.6; // 40..200
```
`strength` = `spec.Strength` = `Payload.Strength` (`ChaosBubbleVariants.cs:106`), 0–100 (strength keys off the classic unscaled size — `CHAOS_DESIGN.md:88`).

### CONTRACT: The score multiplier stack `TotalMult` — `ChaosModels.cs:524-531`
```csharp
public double BaseMult => Config.BaseMult;                       // default 1.0; Golden Touch 1.10/1.20/1.30/1.45
public double ComboMult => Math.Min(1.0 + Combo * 0.08, 6.0);
public double DifficultyMult => Config.DifficultyMult;           // §5
public double HeatMult => 1.0 + Heat * 1.0;                      // Heat 0..1 → up to ×2
// BoonMult default 1.0; ApplyBoon: BoonMult += boon.RunMultBonus. UrgeMult default 1.0; "The urge" sin = 3.0 (2.0 shielded)
public double TotalMult => BaseMult * ComboMult * DifficultyMult * HeatMult * BoonMult * UrgeMult;
```

### CONTRACT: Per-pop modifier helpers — `ChaosModeService.cs:1673, 1697-1702`
```csharp
private double BoonPayMult => _state?.BlindfoldPayMult ?? 1.0;   // Blindfold; 1.0 unworn
private double ChanceFlip() =>                                   // Taking Chances coin-flip
    _state?.ChanceDoubleOdds > 0 ? (rng < ChanceDoubleOdds ? 2.0 : 0.5) : 1.0;
private double PendulumFactor() =>                               // ×PendulumPayMult (3.0 w/ mantra) ONLY while _pendulumSlowActive
    _pendulumSlowActive && _state?.PendulumPayMult > 1 ? _state.PendulumPayMult : 1.0;
```
Darter slow-mo does NOT enable `_pendulumSlowActive` — only the pendulum's own 2.5s swing.

### CONTRACT: Treat pop scoring — `ChaosModeService.cs:1862-1869` (`OnBenignPopped`)
```
pts = BasePoints(strength)
      × BenignBaseline          // default 0.4 (ChaosModels.cs:651); Golden Touch 0.45/0.50/0.55/0.60
      × spec.PayMult            // 1.0 default; Heavy Drop = 3.0
      × PendulumFactor()        // 1.0 or 3.0
      × ChanceFlip()            // 1.0, or 2.0/0.5
      × TotalMult
      × BoonPayMult
```
Side effects before scoring: `EffectsFired++`, `Combo++`, `Focus += (PayMult>1 ? FOCUS_PER_HEAVY(15) : FOCUS_PER_POP(10))`, `Heat = min(1.0, Heat + 0.04)`. `BankDripFeed()` AFTER `Score += pts`.

### CONTRACT: Defuse (snap) scoring — `ChaosModeService.cs:2015-2021` (`OnDefused`)
```csharp
double lastBreath = LastBreathWindowSec > 0 && fuseSecLeft <= LastBreathWindowSec ? LastBreathPayMult : 1.0;
double slowburn = fuseSecLeft <= 1.5 && MaxedBoons.Contains("slowburner") ? 3.0 : 1.0;
pts = BasePoints(strength) * 1.0 * lastBreath * slowburn * PendulumFactor() * ChanceFlip() * TotalMult * BoonPayMult;
```
Defuse pays FULL base (1.0 where treat pop uses BenignBaseline). Focus deduction (`Focus -= DefuseCostFor(spec)`) earlier in OnDefused; `Combo++`, `Defused++`, `Heat += 0.07`.

### CONTRACT: Prism pop — `ChaosModeService.cs:1836-1837`
`prismPts = BasePoints(strength) * 10.0 * TotalMult * BoonPayMult;` — NO BenignBaseline/PayMult/Pendulum/ChanceFlip.

### CONTRACT: Tease-denied — `ChaosModeService.cs:1411-1413`
`pts = TEASE_DENIED_SCORE(120) * TotalMult * BoonPayMult;` flat, no base-points. Also gold 5–10 + `Focus += FOCUS_PER_DENIED(10)`.

### CONTRACT: Darter catch — `ChaosModeService.cs:2280-2281`
`pts = (DARTER_BASE_POINTS(120) + (quick ? DARTER_QUICK_BONUS(90) : 0)) * TotalMult;` — **NO BoonPayMult** (unlike treat/defuse/prism/tease).

### CONTRACT: Freeze catch — `ChaosModeService.cs:2311`
`Score += FREEZE_BASE_POINTS(140) * TotalMult;` — no BoonPayMult.

### CONTRACT: Golden/droplet/heart pops are OUTSIDE the score economy — `ChaosModeService.cs:1780-1828`
Early returns in OnBenignPopped: they bank gold or resistance; never touch Score/Combo, no mults/flips.

---

## 2. End-of-run SPARK reward — `ChaosUpgrades.cs:495-521` (`ChaosMeta.AwardRunRewards`)

```
durationMin      = max(0, run.RunDurationSec) / 60.0
completionBonus  = 35.0 × DifficultyMult × min(1.0, durationMin / 3.0)   // COMPLETION_BONUS_BASE=35, FULL_BONUS_MINUTES=3
scorePart        = 1.5 × sqrt(max(0, run.Score))                          // SCORE_SQRT_SCALE=1.5
sparks           = round((scorePart + completionBonus) × SparkGainMult)   // SparkGainMult ALWAYS 1.0 (retired spark_gain habit)
sparks          += max(0, run.TrickleDrops)                               // Drip Feed trickle, capped in-run
if MaxedBoons has "drip_feed":  sparks = round(sparks × 1.10)             // capstone +10% on WHOLE haul
if State.RunsCompleted == 0:    sparks += 25                              // FIRST_FALL_BONUS=25 (ChaosUpgrades.cs:106), once ever
State.Sparks += max(0, sparks); State.RunsCompleted += 1;
State.BestScore = max(BestScore, (long)Score); State.BestCombo = max; State.TotalDefused += Defused;
State.TotalRunSeconds += max(0, ElapsedSec); ChaosMetaStore.Save(State); return max(0, sparks);
```
- `SparkGainMult` multiplies ONLY `(scorePart + completionBonus)`, not trickle/capstone.
- No explicit cap on `sparks` itself: caps are √-compression, `min(1, durMin/3)` on completion bonus, and in-run `DripFeedCap(DropPerPop) = 30 + 30·clamp(dropPerPop,1,4)` = 60/90/120/150 (`ChaosLifetimeBoons.cs:412`). `DropPerPop` doubles during Relapse loop (`DropsPerPopNow`) but the cap bounds it.
- `run.RunDurationSec` is the planned duration; Relapse loop extends it (`ExtendOneLoop`).
- First-fall guard checked BEFORE `RunsCompleted += 1` → exactly once ever.

---

## 3. End-of-run XP award — `ChaosModeService.cs:3163-3170` (`EndRun`)

```csharp
double durMin = Math.Max(1, _state.RunDurationSec) / 60.0;   // NOTE Max(1,...) here vs Max(0,...) in sparks
double capBase = 250.0 * durMin * _state.Config.DifficultyMult;
double baseXp = Math.Min(_state.Score, capBase);
double skillMult = _state.SkillMult;
double finalXp = baseXp * skillMult;                          // DISPLAY ONLY
App.Progression?.AddXP(baseXp, XPSource.Chaos);               // ← sends baseXp (UNmultiplied)
```
- `finalXp` used only for bark (`NotifyChaosRunCompleted((int)finalXp, ...)` :3196) and results overlay (`ShowResults(_state, baseXp, skillMult, finalXp, ...)` :3198).
- **skillMult applied ONCE inside ProgressionService** (`Progression/ProgressionService.cs:56-58`): `adjustedAmount = amount * (App.SkillTree?.GetTotalXpMultiplier() ?? 1.0)`. PORTING TRAP: applying skillMult at the call site AND in the progression service double-multiplies. WPF passes UNmultiplied baseXp.
- `AddXP` gates: no XP unless logged-in OR offline mode+username (:34-38); idle-suppression does NOT apply to `XPSource.Chaos` (:41-48).

---

## 4. skillMult — `ChaosModels.cs:535`

`public double SkillMult => App.SkillTree?.GetTotalXpMultiplier() ?? 1.0;`
Source `SkillTreeService.cs:213` (aggregate XP multiplier: sparkle boost, time bonuses, Pink Rush, …). Not clamped in run state; fresh account = 1.0. "Informational; applied once at payout." OPEN-QUESTION: attainable upper bound — read `SkillTreeService.cs:213` when porting.

---

## 5. DifficultyMult — `ChaosModels.cs:267-274`

| Enum (`ChaosModels.cs:120`) | Display | Mult |
|---|---|---|
| `Easy` | Gentle | **1.0** |
| `Medium` | Teasing | **1.3** |
| `Hard` | Relentless | **1.7** |
| `Extreme` | Inescapable | **2.2** |

Used in: `TotalMult` (every pop), spark completion bonus (§2), XP cap (§3).

---

## 6. ChaosMeta.ApplyTo — the 6 purchasable upgrades

### CONTRACT: application seam — `ChaosUpgrades.cs:312-318`
```csharp
public static void ApplyTo(ChaosRunConfig config) {
    foreach (var id in State.PurchasedUpgrades)
        if (IsUpgradeActive(id)) ChaosUpgrades.ById(id)?.Apply(config);
}
```
`IsUpgradeActive(id)` = owned && not in `DisabledUpgrades`. **Invoked from `ChaosRunConfig.FromSettings()`** (both the `s == null` early path and the normal path end) — every fresh run config carries owned upgrades.

### CONTRACT: catalogue — costs `ChaosUpgrades.cs:37-42`, effects `:49-88`

| id | Branch | Cost | Apply (verbatim) | Runtime consumption |
|---|---|---|---|---|
| `slow_fuses` | Control | 120 | `c.FuseTimeMult *= 1.15` (:53) | seeds run FuseTimeMult → +15% trance on live fuses |
| `silk_touch` | Control | 180 | `c.HitboxScale = 1.25; c.MagnetEnabled = true;` (:60) | `hitboxScale: () => Config.HitboxScale` (`CMS:368`); `liveMagnet: () => _state?.MagnetEnabled` (`CMS:375`) |
| `popup_notification` | Control | 160 | `c.PopupHeartEnabled = true` (:64) | once/loop 60% heart roll (`CMS:2900-2913`); catch = +1 resistance + FOCUS_PER_HEART(10) |
| `pendulum_swing` | Control | 220 | `c.PendulumSwing = true` (:71) | once/loop at `0.15 + rng·0.65` progress → `ActivateSlowMo(2.5, "Pendulum")`, `_pendulumSlowActive=true` (`CMS:2877-2898`) |
| `draft4` | Depth | 200 | `c.DraftChoices = 4` (:86) | passed to `ChaosBoonPool.Draft` (`CMS:1481/1526`); default 3 |
| `extreme_tier` | Depth | 350 | no-op (:88) | purchase-time: `State.ExtremeUnlocked = true`; unlocks Inescapable; purchase rank-locked `AtLeast(Devoted)` |

Purchase flow `TryPurchase`: exists, not lesson-blocked, not owned, `Sparks >= Cost`, not rank-locked → `Sparks -= Cost; PurchasedUpgrades.Add(id)` (+ExtremeUnlocked) → Save.

OPEN-QUESTION: `silk_touch` sets `HitboxScale = 1.25` (a set, not multiply); verify BubbleService hit-test treats 1.25 as +25%.

---

## 7. Ranks — `ChaosRanks.cs:11-32`

```csharp
public enum ChaosRank { Curious=0, Tempted=1, Slipping=2, Entranced=3, Devoted=4, Claimed=5 }
public static readonly int[] Thresholds = { 0, 3, 10, 25, 50, 100 };   // lifetime RunsCompleted
```
`For(runs)`: scan high→low, first `runs >= Thresholds[i]` wins → **Claimed reachable at exactly 100** (`>=`). `Name`/`NameLower` per enum; default "Curious".
Consumers: `ChaosMeta.RankIndex => For(State.RunsCompleted)`; `AtLeast(rank) = RankIndex >= rank`.

**Avalonia bugs to fix:** Devoted must be index 4 (threshold 50), Entranced index 3 (threshold 25) — currently swapped; there is NO `Lost` rank in WPF (exactly 6 members); Claimed must use `>=` or it's unreachable.

Rank-gated economy: `extreme_tier` purchase needs Devoted; boon CAPSTONE purchase needs Devoted (`IsCapstonePurchaseRankLocked`); per-boon `RankFloor` gates unlock (`IsBoonRankLocked`).

---

## 8. Lifetime-boon economy — `ChaosLifetimeBoons.cs` + `ChaosMeta` (in `ChaosUpgrades.cs`)

Model (`ChaosLifetimeBoons.cs:19-50`): `Id, Category (Skill|Accessory|Utility), RankFloor, UnlockCost, UpgradeCosts[] (levels 2..Max), LevelValues[] (value per level), MaxLevel = LevelValues.Length, ValueAt(level), Apply(ChaosRunState, double), IsActiveUse, UseCooldownSec`.

Flows (all in `ChaosMeta`):
- `BoonLevel(id)` = `LifetimeBoonLevels[id]` (0 = locked); `IsBoonUnlocked >= 1`; `IsBoonActive = ActiveLifetimeBoons.Contains && unlocked`.
- `TryUnlockBoon`: blocks on `IsBoonRankLocked`/`ChaosLessons.IsLessonBlocked`/`IsAccessoryScriptLocked`; `Sparks >= UnlockCostOf`; `Sparks -= cost; LifetimeBoonLevels[id]=1;` auto-equip if free pocket; Save.
- `TryUpgradeBoon`: cost `UpgradeCosts[lvl-1]`; capstone level needs `AtLeast(Devoted)`; level = `min(lvl+1, MaxLevel)`.
- `SetBoonActive(id, active)`: fails if locked or pockets full; Utility pockets unlimited.
- Pockets: `SlotsFor` — Utility=∞; Skill=`min(ToyPockets,2)`; Accessory=`min(AccessoryPockets,2)` (`MAX_POCKETS_PER_CATEGORY=2`). Fresh save: 0/0 (bench sews). `SanitizePockets` on load.

`ApplyLifetimeBoons(run)`: for each active id with lvl≥1 → `b.Apply(run, b.ValueAt(lvl))`; `if (lvl >= MaxLevel) run.MaxedBoons.Add(id)` (drives `slowburner` 3× snap and `drip_feed` +10%).

Economy-relevant boons (verbatim):
- **golden_touch** (Utility, Tempted): Unlock 150, Upgrades {250,400,600}, Values {1.1,1.2,1.3,1.45}. Apply: `Config.BaseMult = v;` + `BenignBaseline = v>=1.45?0.60 : v>=1.3?0.55 : v>=1.2?0.50 : 0.45` (:344-360).
- **drip_feed** (Utility, Entranced): Unlock 250, Upgrades {400,650,1000}, Values {1,2,3,4}. Apply: `DropPerPop = (int)v` (:270-282). Cap 60/90/120/150 (:412).
- **rabbits_foot** (Utility): Unlock 200, Upgrades {350,600,900}, Values {0.010,0.015,0.020,0.020} = GoldenChance (base 0.005 unworn). `GoldenPayRange(level)` = (10,20)/(12,24)/(14,28)/(16,32)/(20,40) (:416) — banked as gold, outside score.
- **slowburner** (Utility): Unlock 150, Upgrades {250,400,600}, Values {10,20,30,40} (% slower fuse). Capstone → 3× snap in final 1.5s.
- **Blindfold**: `s.BlindfoldPayMult = v` (:182) → BoonPayMult.

Retired-boon refunds on load (`RefundRetiredBoons` from `ChaosMeta.Init`): muscle_memory/magic_wand cumulative tables {200,400,700,1150,1800}/{150,300,550,950,1550}; flat refunds bigger_hitboxes=80, magnet=150, shield_recharge=200, start_shield=100, collar=200, pendulum=220, base_mult=90, golden_touch=130, take_more=400, tunnel_vision=140, max_bubbles=110; `spark_gain` scrubbed no refund. Never reuse these ids.

---

## 9. Meta persistence — chaos_meta.json

Store (`ChaosMetaStore.cs`): Newtonsoft, `Path.Combine(App.UserDataPath, "chaos_meta.json")`, atomic `.tmp` + `File.Move(overwrite)`. `Load` never throws (fresh state on corrupt; null-coalesce sets). `ChaosMeta.Init` → Load → `RefundRetiredBoons` → `SanitizePockets`.

Fields (`ChaosMetaState.cs`, SchemaVersion=2, additive-only):
`Sparks` (✦ balance) · `PurchasedUpgrades` · `DisabledUpgrades` · `ExtremeUnlocked` · `EquippedStartBoon` · `DiscoveredCodexIds` · `LifetimeBoonLevels` (id→level) · `ActiveLifetimeBoons` · `Gold` (bench currency: goldens/droplets/tease denials/tips) · `ToyPockets`/`AccessoryPockets` (fresh 0, cap 2) · `BenchPurchases` · `GiftGiven` · `LessonProgress`/`LessonsComplete` · `FirstTimesAwarded` · `LastRankSeen` · lifetime stats: `RunsCompleted` (rank spine + first-fall guard), `BestScore`, `BestCombo`, `TotalDefused`, `TotalRunSeconds`, `TotalChannelSeconds`.

OPEN-QUESTION: `TotalChannelSeconds` written by lesson hooks, not AwardRunRewards — a Looking-Glass stat, not an economy input.

---

## Divergence anchors for the porter

| Divergence | WPF ground truth | file:line |
|---|---|---|
| X1-1 spark reward | `round((1.5·√score + 35·diff·min(1,durMin/3))·SparkGainMult) + TrickleDrops; ×1.10 drip capstone; +25 first-fall`; SparkGainMult always 1.0 | `ChaosUpgrades.cs:495-521` |
| X1-2 XP quantity | `baseXp = min(Score, 250·durMin·diff)` (capped score), NOT sparks | `ChaosModeService.cs:3163-3169` |
| X1-3 skillMult | `App.SkillTree.GetTotalXpMultiplier()`, applied ONCE inside `ProgressionService.AddXP` | `ChaosModels.cs:535`, `ProgressionService.cs:56-58` |
| X1-6 upgrades | `ChaosMeta.ApplyTo(config)` invoked from `ChaosRunConfig.FromSettings` | `ChaosUpgrades.cs:312-318` |
| X1-7 DifficultyMult | 1.0 / 1.3 / 1.7 / 2.2 | `ChaosModels.cs:267-274` |
| X1-4 Ranks | Curious0/Tempted3/Slipping10/Entranced25/Devoted50/Claimed100; 6 members; `>=` | `ChaosRanks.cs:11-32` |
