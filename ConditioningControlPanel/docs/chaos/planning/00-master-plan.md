# Chaos Mode — Master Plan

Synthesis of the six area docs (`A-loop`, `B-meta`, `C-voice`, `D-art`, `E-flavour`, `F-systems`).
**Planning only — nothing here is built.** Code in `Services/Chaos/` is source of truth; where
`CHAOS_DESIGN.md` disagrees, code wins.

Goal: take Chaos from a working shell to a repeatable, compulsive loop — tight enough to run on top
of another game — with cold-start progression, persona-flavored voice + emotes, art, mod-agnostic
flavour, and additive retention systems.

---

## 0. Ground-truth corrections the agents surfaced (read first)

These reshape the work and override the brief / design doc:

| Finding | Reality in code | Owner |
|---|---|---|
| **`LiveBubbleShare` is inert** | Read into `ChaosRunConfig` but **no consumer** — live/benign split comes entirely from `ChaosBubbleVariants` weights + `MinIntensity`. | A/F |
| **Entry is not one-click** | Lab hero → `ChaosHubWindow.ShowDialog()` (modal lobby) → Begin → 3s countdown. Only *restart* ("Run It Back") is one-click and reuses config. | A |
| **Boon draft is modal + steals focus** | `ShowBoonDraft` calls `SetClickThrough(false)` + `BringToFront()`/`Activate()`/`Focus()` every wave boundary → 4 forced interrupts/run at default 5 waves; an unattended draft freezes the run forever. **Biggest background-play wart.** | A |
| **Detonate collapses 3 outcomes into 1 bark** | clutch-absorb / real-hit / combo-break all fire `NotifyChaosBubbleDetonated`. The no-fallthrough matcher can't prioritize a clutch-save line. | A/C |
| **XP interop is already correct** | `EndRun` shows `finalXp = baseXp·skillMult` but awards `AddXP(baseXp)`, and `AddXP` re-applies skillMult internally → applied once. Do **not** double-multiply. | B |
| **Hub-unlock consts are UI-only** | `ChaosMeta.UNLOCK_*_RUNS` + `Rank` exist but **nothing reads them at run-build time**. Cold-start gating is therefore *new* enforcement. | B |
| **`EquippedStartBoon` IS wired** | *Correcting B's open-Q #1:* `BeginRun` (ChaosModeService.cs:102-106) reads `ChaosMeta.State.EquippedStartBoon`, resolves it in `ChaosBoonPool.All`, applies it, marks it discovered. Loadout apply works today; only the *gate* (≥3 runs) and the equip UI are new. | verified by lead |
| **Guardrail violation already present** | Chaos calls `App.Achievements.TrackBubblePopped()` **directly** in `OnBenignPopped/OnDefused/OnDarterCaught/OnFreezeCaught` — the exact bypass the GamificationBridge pattern forbids. New quests/achievements should go through emit→bridge, and this existing call is worth migrating. | F |
| **9 bubble sprites already exist** | Finals under `assets/Chaos/bubbles/`; the nano-banana→`_staging`→manual-gate workflow is already realized (tooling in gitignored `Tools\asset_gen\`). Remaining art classes extend that precedent. | D |
| **`Assets` vs `assets` casing** | Resolver uses `Assets/Chaos/...`; csproj globs `assets/Chaos/**`. Case-insensitive on Windows today; flag before it bites a case-sensitive path or pack. | D |
| **No `XPSource.Chaos`** | `EndRun` passes `XPSource.Bubble`; companion modifiers treat Chaos as bubble-popping. Additive enum value recommended. | B |
| **Spawn RNG is a single static `Random`** | `ChaosBubbleVariants._rng` feeds Pick/Build/RollDarter — daily-seeded runs are cheap to thread, but it's static state (set at BeginRun, restore at EndRun) and `BubbleService` must be audited for other score-affecting RNG. | F |

---

## 1. The reconciled canonical event surface

Everything downstream (barks, meta juice, quests, music) hooks this. Full table lives in
`A-loop.md §5`; this is the **shared contract**: the set of `Notify*`/emit hooks to add, all via the
existing `BarkService.Raise(trigger, ctx => ctx.Set(...))` seam — **no new event bus** (guardrail).

**Already hooked (7):** `ChaosRunStarted(difficulty)`, `ChaosWaveEscalated(wave)`,
`ChaosBubbleDefused`, `ChaosBubbleDetonated(payload)`, `ChaosBoonPicked(boon)`,
`ChaosComboMilestone(combo)`, `ChaosRunCompleted(xp)`.

**Gaps to add (priority-ordered by value):**

| New hook | Fires at | Ctx fields | Why it matters |
|---|---|---|---|
| **Split detonate** → `DetonateAbsorbed` + keep `Detonated` for unshielded | `OnDetonated` both branches | `payload`, `strength`, `shield_absorbed`(bool), `run_detonations`, `combo`, `difficulty` | lets the matcher gloat on a real hit vs praise a clutch save (C needs `shield_absorbed`, `run_detonations`, per-fire `difficulty`/`combo`) |
| `CursePicked` | `OnBoonChosen`, `boon.IsCurse` | `boon`, `rarity`, `runMultBonus` | "risk taken" line must outrank generic pick |
| `BoonSkipped` | `OnBoonChosen`, null | `shieldsNow` | +shield juice + bark |
| `DarterCatch` / `DarterCatchQuick` | `OnDarterCaught` | `points`, `combo`, `quick` | power-up has juice, no voice |
| `FreezeCatch` | `OnFreezeCaught` | `points`, `combo` | "" |
| `BenignPop` | `OnBenignPopped` | `variantId`, `payload`, `combo` | most frequent action; **also add a small FX pulse (none today)** |
| `ComboBig` | combo crosses 25/50 (edge-detected) | `combo`, `threshold` | distinct big-combo moment beyond every-10 |
| `ActChange` | `ActIndex` advances | `act`, `wave` | bigger beat than a wave tick |
| `ResultsShown` | `ShowResults` | RunEnd fields + **PB delta** (new) | bark over results + compulsion hook |

Notes for hookers: `ComboTick`/`HeatFull`/`ComboBroken` need **previous-value edge detection** (track
last value, fire on crossing — not every 250ms tick). `RunAbandoned` (overlay closed mid-run, no
payout) should be distinct from `RunEnd`.

---

## 2. The cold-start progression spine (reconciled)

Run 1 is a clean tutorial-by-doing; each gate adds exactly one *named* toy. Gating is by lifetime
`RunsCompleted` (already persisted), **not** Sparks — runs unlock *content*, Sparks buy the *shop*.
Enforced by a new **narrowing-only** `ChaosMeta.ApplyColdStartGates(cfg)` step in
`ChaosRunConfig.FromSettings()` (after `ApplyTo`), which **only removes** toys and **no-ops for
veterans** → the "neutral default = byte-for-byte unchanged run" invariant holds.

| Gate | Unlocks | Mechanism |
|---|---|---|
| **Run 1 (cold)** | Flash + Subliminal + **Pink Filter** only; shields on; draft = 2 options, **no curses**; forced 90s / 3 waves; no darters/freeze; Easy | clamp `EnabledVariants`, `AllowCurses=false`, `DartersEnabled=false`, `DraftChoices=2`, clamp duration/waves |
| **After 1** | Hub (Upgrades/Stats/Codex tabs) + **Spiral**; drafts → 3 | wire the existing UI-only consts; add Spiral to allow-set |
| **After 3** | **Loadout** tab + **Darters** + **curses** in pool + Medium | Loadout equip already works (`BeginRun` applies `EquippedStartBoon`); add the equip UI + gate |
| **After 5** | **Freeze** + **BrainDrain**; full duration/wave sliders | allow-set + unclamp |
| **After 10** | **Video** + **HT Link**; Hard; full boon pool; rank Novice | allow-set + difficulty gate |
| **After 25 / 50** | Initiate/Adept ranks; content-pack boons auto-discover; mutator + cosmetic milestones; Prestige hook (future) | reserved |

~70% of this is *flipping existing `ChaosRunConfig` fields by `RunsCompleted`*. Genuinely new: (a) a
boon-pool allow-list param on `ChaosBoonPool.Draft(..., IEnumerable<string>? allowedIds)`, (b) reading
`RunsCompleted` at run-build, (c) the `null` "player hasn't chosen variants" sentinel check so an
explicit setup choice isn't re-narrowed (confirm setup UI writes `null`, not an all-ids list).

**Economy:** keep Sparks as the skill-rewarding variable term; bump only the *predictable* floor —
`COMPLETION_BONUS_BASE` 25→35 + a one-time +25 "First Spark" on run 1 → first cheap upgrade by run ~2,
cheapest branch (Control, 500) in ~6–7 average runs, Depth (1060) in ~15. Add a **second currency
"Glow"** (cosmetic-only, milestone-earned: Codex discoveries, rank-ups, daily-first) so the
barebones early player has a *breadth* reward track and cosmetics never tax power. Both are
Chaos-local; main XP unchanged.

---

## 3. Phased build order (with cross-area dependencies)

```
PHASE 1 — FEEL & VOICE  (no new systems, no art dep, no server)   ◀ smallest shippable slice (§5)
  A: benign-pop pulse · skippable/short countdown on RunAgain · Quick Start (skip hub) · PB-delta on results
  A+C: event-surface gaps that already have juice → split detonate (absorbed/unshielded) + ctx fields,
       CursePicked, BoonSkipped, Darter/Freeze/BenignPop barks
  C: base neutral bark pool for the new triggers + emotion/emote moods (no voicelines yet — text first)
  B: completion-bonus 25→35 + first-run +25
  → DEPENDS ON: nothing external. Establishes the canonical Notify* surface everyone else hooks.

PHASE 2 — COLD START & ECONOMY
  B: ApplyColdStartGates (run-build narrowing) + boon-pool allow-list + RunsCompleted gating
  B: wire the UI-only hub-unlock consts (tabs) + Loadout equip UI (apply path already works)
  A: gate Extreme difficulty by ExtremeUnlocked in setup UI; non-modal QuickDraft + DraftAutoResumeSec
  B: second currency "Glow" + cosmetic persistence fields on ChaosMetaState (additive, SchemaVersion→2)
  → DEPENDS ON: Phase 1 event surface for Glow-on-discovery; independent otherwise.

PHASE 3 — FLAVOUR (mod-agnostic skin layer)
  E: ChaosFlavor resolver (override ?? neutral literal) + id-keyed `chaos` ModManifest block + ModChaos classes
     covering currencyName, boons[id], bubbles[id], upgrades[id], ranks[], hud, results
  → DEPENDS ON: stable boon/bubble/upgrade ids (all exist). Feeds bark copy (C) + art labels (D).
     Can start in parallel with Phase 2; merge before voicelines so persona text is skinnable.

PHASE 4 — ART  (fully parallel; start anytime)
  D: P0 boon/curse icons + HUD glyphs (shields/heat/combo/act) → P1 power-up FX, branch crests,
     banner, results art → P2/P3 cosmetic skin sets
  → DEPENDS ON: nothing in code; consuming UI slots can land independently. Resolver + 9 sprites exist.

PHASE 5 — VOICE PRODUCTION
  C: ElevenLabs voicelines for the new + existing triggers, ~65 lines/persona, neutral base + per-mod skins,
     naming `chaos_<trigger>[_<cond>]_<n>.mp3` matching ResolveBarkAudio
  → DEPENDS ON: Phase 1 (trigger set frozen) + Phase 3 (persona flavour decided).

PHASE 6 — RETENTION SYSTEMS  (ranked; cut from the bottom)
  F: daily seeded run (thread the static RNG seed) · Chaos quests/achievements via emit→bridge
     (+ migrate the direct TrackBubblePopped call) · ChaosMusic intensity-reactive layer ·
     Season feed (one SeasonFeatureKeys.Chaos) · XP-rank leaderboard (free today) · weekly mutator
  cut candidates: dedicated server-backed Chaos leaderboard (needs server + anti-cheat), Loadout expansion
  → DEPENDS ON: Phase 1 event surface (quests), Phase 2 (Glow/mutator unlocks).
```

**The critical-path dependency is the Phase-1 canonical event surface** — barks (C), meta juice/Glow
(B), and quests/season (F) all consume the same new `Notify*`/emit hooks. Build it once, correctly,
first.

---

## 4. Smallest shippable first slice (Phase 1 — "Feel & Voice")

Ship something tight before boiling the ocean. This slice needs **no art, no server, no new systems,
no manifest schema** — only the existing `Raise` seam, `ChaosFxWindow.Pulse`, and small config tweaks:

1. **Close the juice/voice gaps that already have juice or are one-liners:** benign-pop pulse;
   split detonate into absorbed vs unshielded (+ `shield_absorbed`/`run_detonations`/`difficulty`/`combo`
   ctx); add `CursePicked`, `BoonSkipped`, `DarterCatch(+quick)`, `FreezeCatch`, `BenignPop` triggers.
2. **Neutral base bark lines** for those triggers in the base manifest with resistance↔surrender moods
   (text-only first — voicelines are Phase 5). Honor no-fallthrough: intersectional rules (clutch-save,
   unshielded-on-Extreme, first-detonation) carry higher `priority`.
3. **Restart/entry tightening:** skip-to-GO on click/key + 1s flash on `RunAgain`; "Quick Start" on the
   hero card that bypasses the modal hub with saved settings.
4. **Compulsion hook:** PB / delta-vs-last-run line on the results screen (needs a best-score read from
   `ChaosMetaState`, already persisted).
5. **Economy kindness:** `COMPLETION_BONUS_BASE` 25→35 + one-time +25 first run.

Outcome: the existing loop *feels* dramatically better and the companion *reacts* to the moments that
currently pass in silence — with zero new infrastructure. Everything else builds on the event surface
this slice establishes.

---

## 5. Open decisions for you

**Loop / cadence (A)**
1. Drop the default to **120s / 4 waves** and auto-derive wave count from length (~30s/wave), keeping
   180/5 as a named "Standard" preset? (Currently 180/5; 300/5 feels slack.)
2. Boon draft: ship **non-modal QuickDraft + auto-resume** as the *default* (best for background play),
   or keep the centered modal default and make QuickDraft opt-in?
3. Background profile: should `video`/`htlink` **detonations be downgradable to a flash** in an
   "ambient/background" run mode, or is the intrusion the point?
4. Restart: keep HUD/overlay/FX windows **alive across `RunAgain`** for instant restart (more state to
   reset, small leak risk) vs. current clean close-and-rebuild (re-pays 3s)?
5. `LiveBubbleShare` is **inert** — wire it into the variant picker (bias live weights) or **remove the
   knob**?

**Meta / economy (B)**
6. Confirm the **cold-start spine gates** (run 1/1/3/5/10) and the toy at each — this is the backbone.
7. **Two currencies** (Sparks = power, Glow = cosmetic) — approve, or keep single Sparks?
8. Add **`XPSource.Chaos`** (additive enum) so companion modifiers can treat Chaos distinctly?
9. Should **Extreme** also co-gate behind `RunsCompleted ≥ 10` in addition to the 350-Spark purchase?
10. **Prestige** at 50/100 runs — in scope later? If so, does it spend Sparks, Glow, or reset one?

**Voice / emote (C)**
11. Target the **Circe 8-clip animated emote vocabulary** (idle/talkA/talkB/denial/comehere/entrancing/
    greeting/praise) for Chaos reactions — accepting there's **no dedicated gloat/smug/disappointed/
    triumphant clip** (old `disappointed*.gif` were deleted)? Or commission new clips for gloat/disappointed?
12. **Per-persona line budget** (~65 lines/persona ≈ ~195 voiced mp3s across Bambi/Sissy/Circe) — approve
    scope, or trim to a smaller climactic-only set first?

**Flavour (E)**
13. Approve the **id-keyed `chaos` manifest block + `ChaosFlavor` resolver** approach (neutral literals
    stay as base; skins override display strings only, never `Apply` lambdas or numeric knobs)?
14. Where do built-in personas' `chaos` blocks live, given Drone ships as a bundled `.ccpmod`?

**Art (D)**
15. Confirm the **`{kind}` folder names** for boon/curse/HUD/FX under `Assets/Chaos/` (only `bubbles`/
    `banner` are wired today) so generation targets the right paths.
16. Resolve the **`Assets` vs `assets` casing** mismatch now (low risk on Windows, but a latent trap)?

**Systems (F)**
17. Minimum retention set to build first — recommended: **daily seeded run + Chaos quests/achievements +
    music layer + Season feed + XP-rank leaderboard**, deferring the server-backed Chaos leaderboard.
    Approve, or re-rank?
18. OK to **migrate the direct `App.Achievements.TrackBubblePopped()` call** to the emit→bridge pattern
    while we're in there (fixes an existing guardrail violation)?
