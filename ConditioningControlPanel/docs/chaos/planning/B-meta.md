# Chaos Mode — Agent B: Meta-Progression, Economy & Cold Start

Planning only. Code is source of truth. Verified against `Services/Chaos/*` and the main
progression/gamification stack (`ProgressionService`, `SkillTree`, `GamificationBridge`,
`AchievementService`) on this branch. No code changed.

---

## 0. Ground-truth corrections (read first)

A few things diverge from the brief / `CHAOS_DESIGN.md`. These shape every proposal below.

| Claim | Reality in code | Impact |
|-------|-----------------|--------|
| Spark formula uses `SparkGainMult` | `AwardRunRewards` (`ChaosUpgrades.cs:171`) is `round((score/100·diffMult + 25·diffMult)·SparkGainMult)`. ✅ matches. | none |
| XP payout = `baseXp·skillTreeMult` | `EndRun` (`ChaosModeService.cs:459-466`) computes `finalXp = baseXp·skillMult` **for display only**, then calls `App.Progression.AddXP(baseXp, …)`. **AddXP itself re-applies the skill mult** (`ProgressionService.cs:55-56`). So the real awarded XP = `baseXp·skillMult` — applied **once**, correctly. `finalXp` is just shown in results/bark. | XP interop is already correct; do not double-multiply. |
| Chaos has its own XP source | `EndRun` passes `XPSource.Bubble` — there is **no `XPSource.Chaos`**. | Companion XP modifiers treat Chaos as bubble-popping. Minor; flag only. |
| Hub gates exist in code | `ChaosMeta` defines the `UNLOCK_*_RUNS` consts + `Rank`, but **nothing reads them at run-build time** — they are UI-facing only. Cold-start gating below is therefore *new* enforcement. | Most of the unlock spine is new code. |
| `EquippedStartBoon` is applied | Field exists in `ChaosMetaState`; the brief says BeginRun applies it, but no read path was found in `ChaosModeService` for `EquippedStartBoon`. Treat Loadout apply as **partially wired / to verify by Agent owning the service**. | Loadout gate may need wiring, not just gating. |
| `extreme_tier` Apply | Purchase sets `ExtremeUnlocked`; `Apply` is a no-op. Difficulty selection must be gated by `ExtremeUnlocked` in the setup UI (Agent A territory). | cross-ref |

**Invariant to protect:** every meta knob on `ChaosRunConfig` has a neutral default, so a
fresh `ChaosMetaState` + no settings overrides = byte-for-byte unchanged run
(`ChaosModels.cs:39-60`). All cold-start gating MUST preserve this.

---

## 1. COLD START — the unlock spine

### Current state
There is **no cold start**. A brand-new player on run 1 already gets: all 8 bubble variants,
darters, freeze, the full 8-entry boon/curse pool, 3-option drafts, curses, full duration/wave
config, and Easy–Hard difficulty. The only thing actually locked is Extreme (via purchase) and,
nominally, hub tabs (consts that nothing enforces). That is the opposite of barebones — it dumps
the whole toybox on run 1 and leaves nothing to discover.

### Proposal — gate by lifetime `RunsCompleted` (already persisted), drip the toybox

Philosophy: run 1 is a **clean, legible tutorial-by-doing** — only benign pops + one threat type +
one defense. Each subsequent gate adds exactly one *named* toy so the player can feel it arrive.
Gating is by lifetime runs (cheap, monotonic, already in `ChaosMetaState.RunsCompleted`), NOT by
Sparks — Sparks are for the *shop*; runs are for *content unlocks*. This separates "I grinded
currency" from "the game is teaching me more."

| Gate (lifetime runs) | Unlocks (the toy) | New vs code-can-gate |
|---|---|---|
| **Run 1 (cold)** | Benign only: **Flash** + **Subliminal** bubbles. **1 threat: Pink Filter** (live). **Shields on.** Boon draft = **2 options, boons-only (no curses)**. Duration forced **90s**, **3 waves**. No darters, no freeze, no Spiral/BrainDrain/Video/HT, Easy only. | **New** (run-build clamp by RunsCompleted) |
| **After 1 run** | Hub opens: **Upgrades + Stats + Codex** tabs. **Spiral** threat unlocks. Drafts go to **3 options**. | tabs = code consts (need wiring); content = new |
| **After 3 runs** | **Loadout** tab (equip start boon). **Darters** (slow-mo power-up). **Curses** enter the draft pool. Medium difficulty. | Loadout = const; darters/curses = new gating |
| **After 5 runs** | **Freeze** bubble (freeze power-up). **BrainDrain** threat. Full duration/wave sliders unlock (60–900s, 1–12). | **New** |
| **After 10 runs** | **Video** + **HT Link** rare threats. Hard difficulty. Full boon pool active. Rank → *Novice*. | difficulty = setup gate; rest new |
| **After 25 runs** | "Initiate" rank. Reserved for **future toys** (new boons/curses added by content packs auto-discover here). Optional: unlock a *modifier mutator* (e.g. daily-seed). | new (future) |
| **After 50 runs** | "Adept". Cosmetic milestone reward (see §4) + future Prestige hook. | new (future) |

**Why these toys at these gates:** threats escalate from 1 → 2 → 3 → rares so the player learns
each defuse pattern before the next arrives; *defensive/utility* toys (darters, freeze) arrive
after the player has felt a few detonations and wants relief; difficulty tiers trail the content
so a new player can't faceplant into Hard before they've seen all the variants.

**Code reality check:**
- Variant gating is *almost free*: `ChaosRunConfig.EnabledVariants` is already a filter list
  (`ChaosModels.cs:29`). Cold start = set `EnabledVariants` to the unlocked subset when the
  player hasn't manually chosen, instead of `null`.
- Draft option count: `DraftChoices` already drives `ChaosBoonPool.Draft(choices)`. Set it to 2
  at run 1, 3 after run 1; `draft4` upgrade still raises to 4 (clamped 2–4 already).
- Curses: `AllowCurses` already exists — force `false` until 3 runs.
- Darters: `DartersEnabled` already exists — force `false` until 3 runs.
- Duration/waves: clamp `DurationSec`/`WaveCount` to the cold value until 5 runs.
- Boon *pool* gating (only some boons at run 1) is **new** — `ChaosBoonPool.Draft` has no
  "available ids" parameter today; add an optional `IEnumerable<string>? allowedIds`.

So ~70% of the spine is *flipping existing config fields by RunsCompleted*; the genuinely new
pieces are (a) a boon-pool allow-list and (b) reading `RunsCompleted` at run-build time.

---

## 2. Spark economy — target & back-solve

### Current state
Branch totals (sum of all 4 rows each): **Control 500, Greed 550, Depth 1060** Sparks.
Per-run Sparks: `round((score/100·diffMult + 25·diffMult)·SparkGainMult)`.
Because score = sum of (BasePoints · actionMult · **TotalMult**) and TotalMult is
fully multiplicative + uncapped (combo ≤6, heat ≤2, boon ~1.5, diff ≤2.2, ×2 DoN), Spark
output is wildly run-dependent. A clean Easy 90s cold-start run scores low; a juiced Hard run
scores 10–50× more. That's *fine for excitement* but makes "N runs to a branch" undefined.

### What an "average" run actually pays (worked example)
Cold-start Easy run (run 1), realistic newbie: ~25 pops/defuses, modest combo, low heat.
- Say final `Score ≈ 1,500` (Easy, diff 1.0, BaseMult 1.0, combo settling ~×2, heat ~×1.3).
- Sparks = `round((1500/100·1.0 + 25·1.0)·1.0)` = `round(15 + 25)` = **40 Sparks**.

A competent Medium run a few sessions in: `Score ≈ 8,000`, diff 1.3, `SparkGainMult` still 1.0.
- Sparks = `round((8000/100·1.3 + 25·1.3))` = `round(104 + 32.5)` = **137 Sparks**.

A strong Hard run with spark_gain owned: `Score ≈ 25,000`, diff 1.7, gain 1.2.
- Sparks = `round((250·1.7 + 25·1.7)·1.2)` = `round((425+42.5)·1.2)` = **561 Sparks**.

### Target
> **Clear the cheapest branch (Control, 500) in ~6–8 runs of *average early* play; the
> deepest branch (Depth, 1060) in ~15–20 runs.** First *single* upgrade should be buyable after
> 2–3 runs so the new player gets a purchase dopamine hit fast.

### Back-solve verdict
At the current payout, an *early* player nets ~40–80 Sparks/run (Easy/early-Medium). 500 ÷ 60 ≈
**8 runs to first branch** — already close to target on the low end, but the cheapest *single*
upgrade (Bigger Hitboxes, 80) takes ~2 runs → good. The problem is the **floor is too swingy and
slightly low for the very first purchase**; and the completion bonus (25·diff) is the only
predictable component, which is healthy.

**Recommended tuning (small, surgical):**

| Knob | Now | Proposed | Rationale |
|---|---|---|---|
| `COMPLETION_BONUS_BASE` | 25 | **35** | Raises the *predictable* floor so even a flubbed cold run banks ~45–50, guaranteeing a purchase by run ~2. Reduces variance dependence. |
| `SPARK_SCORE_DIVISOR` | 100 | **100 (keep)** | The score term should stay the *variable, skill-rewarding* part. Don't flatten it. |
| First-purchase nudge | — | **+25 one-time "First Spark" bonus** on `RunsCompleted == 0→1` | Cold-start kindness; banked in `AwardRunRewards` when `RunsCompleted` was 0. |
| `spark_gain` upgrade | ×1.2 | keep | Stays the economy accelerator the dedicated player buys. |

With bonus=35: cold run ≈ `round(15 + 35)` = **50** → first cheap upgrade (80) in 2 runs,
Control branch in ~6–7 average runs. Depth (1060) at ~80–137/run = **8–15 runs** once the player
is on Medium. This hits the target without touching the exciting variable term.

**Do NOT** add a flat per-defuse Spark drip — it would let players AFK-farm benign pops and
decouple Sparks from skill. Keep Sparks = f(score) + flat completion bonus only.

---

## 3. Currency design — one vs two

### Recommendation: **TWO currencies.**

| Currency | Role | Earned by | Spends on |
|---|---|---|---|
| **Sparks** (existing) | Soft-grind **power** currency | run end, `f(score)` + completion bonus | the 3 upgrade branches (`ChaosUpgrades`) — power |
| **Glow** (new) | **Cosmetic-only** currency | *milestones*, not score: +1 per first-time Codex discovery, +N at rank-ups, +N for "first clear of difficulty X", daily-first-run bonus | cosmetics only (§4) — bubble skins, HUD themes, FX, companion portraits |

### Why two
1. **Healthy grind.** If cosmetics and power share one pool, every cosmetic purchase is a power
   tax (or vice versa), and min-maxers never buy cosmetics. Splitting means the player who wants
   to look cool never feels they're "wasting power Sparks."
2. **Glow rewards *breadth*, Sparks reward *depth/skill*.** Glow comes from doing new things
   (discovering variants/boons, hitting ranks) — it naturally fills the cold-start arc and gives
   the barebones early player a *second* reward track while their Spark balance is tiny. This is
   the cold-start engagement engine.
3. **No inflation risk.** Glow is milestone-gated and finite-ish (you can only discover each
   Codex entry once), so cosmetics stay aspirational rather than trivially farmable.
4. Cheap to add: one `int Glow` on `ChaosMetaState`, awarded in the same `AwardRunRewards`
   path + in `ChaosMeta.MarkDiscovered` (which already persists on first sighting).

**Interop with main XP:** unchanged. Sparks and Glow are Chaos-local; main XP still flows via
`AddXP(baseXp, XPSource.Bubble)`. Add a dedicated `XPSource.Chaos` so companion modifiers can
treat Chaos distinctly later — low-risk, additive enum value. Chaos meta does **not** duplicate
the XP/level system; it banks its own two currencies and feeds the shared XP curve once per run.

---

## 4. Cosmetics — catalogue & persistence

### Where cosmetics live (all NON-POWER)

| Slot | What | Hook point |
|---|---|---|
| **Bubble skin** | Replaces the per-variant sprite tint/art | `ChaosArt` already resolves `assets/Chaos/bubbles/{id}.png`; a skin = an alternate asset folder selected at render. Pure visual. |
| **HUD theme** | Recolor the HUD strip / results (e.g. accent palette swap) | `ChaosHudWindow` resource brushes |
| **Pop / trail FX** | Particle burst color, trail style on pops/catches | `ChaosFxWindow` / `BubbleService` pop animation |
| **Companion portrait/outfit** | Which Circe portrait/emote pack shows in results & barks | reuse avatar emote registry; cosmetic-only pointer |

These are strictly visual — none touch `ChaosRunConfig` power fields, so the byte-for-byte
invariant is irrelevant to them (they affect rendering, not run math).

### Starter Glow catalogue (small, ships v1)

| Id | Slot | Name | Glow cost | Notes |
|---|---|---|---|---|
| `skin_neon` | bubble skin | Neon Pop | 30 | first affordable |
| `skin_glass` | bubble skin | Crystal | 60 | |
| `hud_synthwave` | HUD theme | Synthwave | 40 | |
| `hud_mono` | HUD theme | Monochrome | 40 | |
| `fx_sparkle` | pop FX | Sparkle Burst | 50 | |
| `fx_ember` | trail FX | Ember Trail | 70 | |
| `portrait_circe_gold` | portrait | Gilded Circe | 100 | rank-up flex unlock |

Plus **rank-locked freebies** (auto-granted, 0 Glow) at ranks Novice/Adept/Paragon so progression
itself hands out cosmetics — reinforces the long tail.

### Persistence (extend `ChaosMetaState`, additive, neutral defaults)
```csharp
public int Glow { get; set; } = 0;
public HashSet<string> OwnedCosmetics { get; set; } = new();
public string? EquippedBubbleSkin { get; set; } = null;   // null = default art
public string? EquippedHudTheme   { get; set; } = null;   // null = default theme
public string? EquippedPopFx      { get; set; } = null;
public string? EquippedPortrait   { get; set; } = null;
```
All null/0 defaults → unchanged visuals for a fresh player; `ChaosMetaStore` already round-trips
unknown-but-defaulted fields via Newtonsoft. Bump `SchemaVersion` to 2 (the field is reserved
for migrations; no migration logic needed since additions are default-safe).

---

## 5. Enforcing cold-start gating without breaking the invariant

The danger: if we *write* clamped values into `ChaosRunConfig` for new players, an existing
player's settings could get stomped, and the "neutral default" promise is about *defaults*, not
*overrides*. The clean approach keeps the neutral path neutral:

**Where:** a new step in `ChaosRunConfig.FromSettings()`, applied **after** settings are read but
**after `ChaosMeta.ApplyTo` too is fine** — gating is a *cap*, upgrades are *buffs*; order:
1. read AppSettings into cfg (as today)
2. `ChaosMeta.ApplyTo(cfg)` (owned upgrades) — as today
3. **new:** `ChaosMeta.ApplyColdStartGates(cfg)` — clamps for unlock state

**How `ApplyColdStartGates` preserves the invariant:**
- It reads `State.RunsCompleted`. For a returning player past all gates (RunsCompleted ≥ 10),
  the method is a **no-op** — every branch checks `if (runs >= gate) return;`-style and falls
  through to leaving cfg untouched. So a veteran's run is byte-for-byte what it is today.
- For a fresh player, it **only narrows**: it sets `EnabledVariants` to the unlocked subset
  *iff the player hasn't explicitly chosen variants* (i.e. settings gave `null`); forces
  `AllowCurses=false`, `DartersEnabled=false`, lowers `DraftChoices`, and clamps
  `DurationSec`/`WaveCount` down. It never *raises* anything.
- The "neutral default = unchanged run" invariant is about the *engine* with empty meta. Cold
  start is a deliberate, run-count-driven *content* layer on top — it's the one sanctioned place
  that reads lifetime state to shape a run. Because it only *removes* toys a brand-new player
  hasn't earned, and self-disables once earned, it's monotonic and reversible-by-progress.
- Boon-pool gating: pass the unlocked boon-id allow-list into `ChaosBoonPool.Draft(...)` from
  the run loop (new optional param), defaulting to "all" when the player is past the gate.

**Edge case to flag:** if the player manually disables variants in setup *and* is past the
cold-start gates, we must not re-narrow — gating only fills `EnabledVariants` when it's `null`
(the "I haven't chosen" sentinel). Confirm setup UI writes `null` (not an all-ids list) when the
player leaves variants untouched, or the gate's "respect explicit choice" check breaks.

---

## Open questions

1. **Loadout apply path:** is `EquippedStartBoon` actually consumed in `BeginRun`? No read found
   in `ChaosModeService`. If not wired, the Loadout gate needs *implementation*, not just gating.
2. **`XPSource.Chaos`:** add a dedicated enum value (additive) so companion XP modifiers can
   treat Chaos distinctly, or keep folding into `Bubble`? Recommend adding it.
3. **Glow award rates:** exact Glow per Codex discovery / rank-up / daily-first needs a quick
   sim once the Codex discovery firing rate is known (how many entries a typical player hits per
   run). Placeholder: 1/discovery, 25/rank-up, 5/daily-first-run.
4. **Cold-start "respect explicit choice" sentinel:** confirm setup UI semantics for "no variant
   chosen" = `null` vs full list (affects whether gating can safely narrow).
5. **Should Extreme also gate behind RunsCompleted (e.g. ≥10) in addition to the 350-Spark
   purchase?** Currently purchase-only; a run-count co-gate would stop a lucky-rich newbie from
   buying into a tier they'll faceplant in.
6. **Prestige hook at 50/100 runs** — out of scope here, but the spine leaves room. Decide whether
   Prestige spends Sparks, Glow, or resets one for the other.
