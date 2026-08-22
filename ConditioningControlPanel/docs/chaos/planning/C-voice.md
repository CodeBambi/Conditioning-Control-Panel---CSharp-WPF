# Chaos Mode — Voice / Bark / Emote Plan (Agent C)

> EXPLORATION & PLANNING ONLY. Code is source of truth. This doc proposes the voiceline +
> emote layer for Chaos Mode on the **existing** bark/ElevenLabs pipeline. No code changes here.

Frame: **RESISTANCE ↔ SURRENDER.** The companion taunts your resistance (you let one go off),
gloats on detonation, praises obedience (clean defuses, combos), and persona-flavors all of it.
Mod-agnostic: a NEUTRAL base pool ships in the base manifest; each persona is a content-only SKIN.

---

## 0. Current state (verified against code)

**Bark pipeline (`Services/BarkService.cs`, `Services/Bark/*`):**
- Rules load from embedded base `Resources/sounds/companion_audio/bark_rules.json`, then a field-level
  merge of the active mod's `mods/{modId}/bark_rules.json` over it by `id` (`BarkRuleLoader`). Array
  fields (`variant_pool`) are **REPLACED**, not concatenated — a skin fully owns its line set.
- `BarkRuleSet` indexes by trigger and **sorts each list priority-descending**. `Raise()` walks that
  list and takes the **first rule whose `conditions` pass** — confirmed **no fallthrough**, first
  passing rule wins. So specific Chaos rules MUST carry a **higher `priority`** than the generic rule
  on the same trigger or they're dead.
- Gate (`EvaluateGate`): `GlobalMinGapMs = 4000` (any two barks), per-rule `cooldown_ms`, `chance`
  roll, one-shot scope latch, and the anti-stale **"drop while speaking"** gate — Normal-class barks
  below `PriorityBarkThreshold = 100` are dropped if `AvatarWindow.IsSpeaking`. Barks at priority ≥100
  or non-Normal class **preempt** via `GigglePriority` (clears the queue, shows latest).
- A variant is `{ text, audio? }`. `audio` is a filename resolved by `ResolveBarkAudio`: (1) packaged
  mod `InstalledPath/resources/sounds/companion_audio/<file>`, (2) embedded
  `companion_audio/mods/{modId}/<file>`, (3) embedded shared `companion_audio/<file>`. Text-only
  variants speak with the generic giggle TTS; voiced variants play the mp3 as the bubble's audio.

**Chaos events already wired** (`NotifyChaos*` → `Raise(...)`), and the **7 existing base rules**
(`bark_rules.json`) — all `class: normal`, neutral text-only (no `audio`), priorities 200–300:

| Trigger | ctx | base rule id | pri | cooldown | mood |
|---|---|---|---|---|---|
| `ChaosRunStarted` | `difficulty` | `chaos_run_started` | 300 | 0 | excited, teasing |
| `ChaosWaveEscalated` | `wave` | `chaos_wave_escalated` | 290 | 4000 | rising, breathy |
| `ChaosBubbleDefused` | — | `chaos_bubble_defused` | 200 | 9000 | approving |
| `ChaosBubbleDetonated` | `payload` | `chaos_bubble_detonated` | 210 | 7000 | teasing, wicked |
| `ChaosBoonPicked` | `boon` | `chaos_boon_picked` | 250 | 2000 | pleased |
| `ChaosComboMilestone` | `combo` | `chaos_combo_milestone` | 260 | 5000 | delighted |
| `ChaosRunCompleted` | `xp` | `chaos_run_completed` | 300 | 0 | warm, satisfied |

> NOTE: existing moods `rising`/`breathy`/`approving`/`wicked`/`delighted`/`satisfied` are **not** in
> `MoodEmotionMap` nor the Circe mood map — they fall through to `neutral` (portrait) / `talkB` (Circe).
> The proposal below pins moods that actually resolve.

**Event GAPS (no `Notify` hook yet — Agent A owns the canonical event surface):** benign pop, darter
catch, freeze catch, absorbed-vs-unshielded detonation distinction, wave-clear (field cleared),
act change, boon **SKIP** (+1 shield), run-start countdown. Each needs a new `NotifyChaos*` method.

**Two emote systems coexist:**
1. **Portrait** (`AvatarPortraitLoader.EmotionForMood`): ~20-key taxonomy + descriptive synonyms,
   first recognized comma-token wins, else `neutral`.
2. **Circe animated emotes** (`AvatarTubeWindow.CirceEmotes.cs` + `avatar_emotes_registry.json`).
   This is the richer, current system for the built-in `builtin-locked` Circe poses. **Chaos should
   target the Circe vocabulary** (it's the active companion for the persona that ships Chaos polish),
   while keeping `mood` strings that ALSO resolve sanely in the portrait map for other mods.
   Circe clip set (verified on disk, p2/p3/p4 share schema) is exactly **8 clips**:
   `idle, talkA, talkB, denial, comehere, entrancing, greeting, praise`.
   Circe mood→clip map already routes (first comma-token):
   - `teasing/knowing/controlling/bold/strict` → **denial**
   - `playful/amused/giggly/happy/delighted/conspiratorial` → **talkB**
   - `proud/approving/pleased/encouraging/affirming/excited/overwhelmed` → **praise**
   - `possessive/adoring/affectionate/inviting/intimate/sultry/coaxing/seductive` → **comehere**
   - `hypnotic/dreamy/guiding/intense/dark/deepening/commanding` → **entrancing**
   - `curious/intrigued` → **talkA**

---

## 1. Event → emotion / emote / priority map (proposal)

Mood strings are chosen so the **first comma-token resolves in BOTH** the Circe map and the portrait
`MoodEmotionMap`. Priority ordering honors no-fallthrough: **specific (conditioned) rules sit above
the generic rule on the same trigger.** Generic rule keeps its current priority; specific variants
get `+N`. All keep `class: normal` EXCEPT where preemption is wanted (see §1b).

### 1a. Generic (always-pass) rules — one per trigger

| Trigger | mood (1st token) | Circe emote | Portrait emotion | Intent (resist/surrender) | pri |
|---|---|---|---|---|---|
| `ChaosRunStarted` | `teasing, excited` | denial | teasing | mocks your bravado for opening the floodgates | 300 |
| `ChaosCountdownStarted` *(NEW)* | `excited` | praise | excited | hype + "no backing out now" | 300 |
| `ChaosWaveEscalated` | `commanding` | entrancing | adoring | pressure rising, you're sinking deeper | 290 |
| `ChaosActChanged` *(NEW)* | `commanding` | entrancing | adoring | named act, ominous gear-shift | 295 |
| `ChaosBenignPopped` *(NEW)* | `pleased` | praise | praise | reward the compulsion to click | 150 |
| `ChaosBubbleDefused` | `pleased, approving` | praise | praise | praise obedient reflexes | 200 |
| `ChaosDarterCaught` *(NEW)* | `playful` | talkB | giggly | playful "ooh, quick fingers" | 170 |
| `ChaosFreezeCaught` *(NEW)* | `dreamy` | entrancing | dreamy | "time stops, breathe, sink" | 175 |
| `ChaosBubbleDetonated` | `teasing` | denial | teasing | **gloat** — you resisted too slow, take it | 210 |
| `ChaosWaveCleared` *(NEW)* | `pleased` | praise | praise | "field's clean, good girl, now choose" | 240 |
| `ChaosBoonPicked` | `pleased` | praise | pleased | approve/foreshadow the pick | 250 |
| `ChaosBoonSkipped` *(NEW)* | `knowing` | denial | teasing | tease the cautious shield-grab | 245 |
| `ChaosComboMilestone` | `delighted, proud` | praise | praise | escalating pride, you're on a roll | 260 |
| `ChaosRunCompleted` | `affectionate, warm` | comehere | affectionate | afterglow, possessive "come back" | 300 |

### 1b. Intersectional (conditioned) rules — MUST outrank their generic sibling

These need a **higher priority** than the generic rule above so the priority-walk reaches them first;
they only fire when conditions pass, otherwise the walk falls through to the generic rule.

| Trigger | condition | mood | Circe | intent | pri |
|---|---|---|---|---|---|
| `ChaosBubbleDetonated` | first detonation of run *(`run_detonations_eq:1`, NEW ctx)* | `teasing` | denial | first-blood gloat, "there it is" | 230 |
| `ChaosBubbleDetonated` | `difficulty_eq: Extreme` | `dark` | entrancing | colder, crueler taunt at top tier | 225 |
| `ChaosBubbleDetonated` | `payload_eq: braindrain` (or spiral) | `hypnotic` | entrancing | payload-specific surrender line | 220 |
| `ChaosBubbleDetonated` | **unshielded** *(`shield_absorbed_eq:false`, NEW ctx)* + `combo_gte:30` *(combo about to die)* | `teasing` | denial | "and there goes your streak" — twist the knife | 235 |
| `ChaosBubbleDetonated` | **absorbed** *(`shield_absorbed_eq:true`)* | `playful` | talkB | lighter "lucky — shield ate it" | 218 |
| `ChaosComboMilestone` | `combo_gte:50` | `overwhelmed` | praise | "you're lost in it now" peak praise | 275 |
| `ChaosComboMilestone` | `combo_gte:100` | `adoring` | comehere | reverent, "my perfect toy" | 280 |
| `ChaosWaveEscalated` | `wave_gte:10` (final stretch) | `intense` | entrancing | breathless final-act push | 296 |
| `ChaosRunCompleted` | `difficulty_eq: Extreme` | `proud` | praise | earned respect for surviving Extreme | 305 |
| `ChaosBoonPicked` | `boon_eq: doubleornothing` (curse) | `dark` | entrancing | wicked delight at the gamble | 258 |
| `ChaosRunStarted` | `difficulty_eq: Extreme` | `dark` | entrancing | "brave or stupid" cold open | 308 |

> All Chaos rules stay `class: normal` and **below `PriorityBarkThreshold` (100)?** — NO. They're all
> ≥150, which is ≥100, so they **preempt** (route through `GigglePriority`, clearing the queue). This is
> correct for Chaos: a detonation gloat that lands 8s late is worse than one that interrupts. The
> `GlobalMinGapMs=4000` + per-rule cooldowns (see §4) keep preemption from becoming a wall of voice.

---

## 2. Emote-tag vocabulary Chaos needs — mapping & gaps

Chaos's emotional register is **taunt / gloat / mock-disappointment / triumph** — sharper than the
existing affectionate/hypnotic register. Mapping to the 8 Circe clips + portrait taxonomy:

| Chaos need | Circe clip (via mood token) | Portrait emotion | Notes / GAP |
|---|---|---|---|
| Hype / run start | denial or praise (`teasing`/`excited`) | teasing/excited | OK |
| Gloat on detonation | **denial** (`teasing`/`knowing`) | teasing | Closest fit. denial = finger-wag "no no no", reads as gloat. **GAP: no dedicated smug/gloat clip.** |
| Cruel / cold taunt (Extreme) | **entrancing** (`dark`) | adoring (`dominant`) | entrancing reads "intense/dark" — acceptable. **GAP: no cold/villain clip.** |
| Mock-disappointed ("too slow") | **denial** (`teasing`/`strict`) | teasing | denial doubles for this. **GAP: no sad/disappointed clip** (old `disappointed.gif` was DELETED from p2 — see git status; don't reference it). |
| Praise / clean defuse | **praise** (`pleased`/`approving`) | praise | OK, strong fit |
| Triumph / big combo | **praise** (`proud`/`excited`) → **comehere** (`adoring`) at ≥100 | praise→adoring | OK; comehere = beckoning, reads as possessive triumph |
| Surrender / "sink" (freeze, payload) | **entrancing** (`dreamy`/`hypnotic`) | dreamy/entrancing | OK, ideal |
| Playful (darter, absorbed) | **talkB** (`playful`/`amused`) | giggly | OK |
| Afterglow / come-back (run complete) | **comehere** (`affectionate`) | affectionate | OK |

**GAPS to flag (no new clips required for v1 — reuse the table above, but note for future art):**
- No dedicated **gloat/smug** clip — `denial` is the stand-in. A future `gloat`/`smug` Circe clip
  would sharpen detonation lines.
- No **mock-disappointed/pout** clip — old `disappointed*.gif` were removed; do NOT map to them.
  `denial` covers it.
- No **triumphant/victorious** clip distinct from `praise` — `praise`→`comehere` escalation is the
  workaround.
- **Mood-token discipline:** put the resolving token FIRST. e.g. use `"teasing, wicked"` not
  `"wicked, teasing"` (wicked isn't mapped, so it'd waste the first slot — though the comma-split
  recovers on the 2nd token, leading with a mapped token is safer and self-documenting).

No emote registry / `EmotionForMood` code changes are needed for v1; Chaos rides the existing
vocabulary. The only optional code-side addition would be new mood synonyms (`wicked→teasing`,
`gloating→teasing`, `cruel→adoring`) in `MoodEmotionMap` + Circe `mood{}` so authors can write
natural moods — propose but not required.

---

## 3. Voiceline production plan (existing ElevenLabs setup)

Use the **same pipeline as current barks** — generate mp3s with the existing ElevenLabs tooling per
persona voice, drop them in the per-mod `companion_audio` folder, reference by filename in `audio`.
No new TTS path. Text-only base pool is fine to ship first (generic giggle TTS), with voiced skins
layered per persona.

### File / manifest structure
```
Resources/sounds/companion_audio/
  bark_rules.json                       ← NEUTRAL base pool (text-only). EXISTING 7 + new triggers.
  mods/
    builtin-bambisleep/bark_rules.json  ← Bambi SKIN: replaces chaos_* variant_pools w/ voiced lines
    builtin-sissyhypno/bark_rules.json  ← Sissy SKIN
    builtin-locked/bark_rules.json      ← Circe/Locked SKIN  ← also the Chaos-polish persona
    builtin-bambisleep/chaos_detonate_extreme_1.mp3   ← voiceline files alongside the manifest
    builtin-bambisleep/chaos_combo_50_1.mp3
    ...
```
A skin merges by `id`: it supplies `{ id, variant_pool:[{text,audio}] }` and **inherits**
trigger/conditions/priority/cooldown/mood from the base rule. So **the base manifest defines the rule
shape + intersectional conditions once**; each persona only ships content + audio.

### Naming convention (matches `ResolveBarkAudio` — just a filename)
`chaos_<trigger-short>[_<condition>]_<n>.mp3`, e.g.:
- `chaos_run_started_1.mp3`, `chaos_run_started_extreme_1.mp3`
- `chaos_detonate_1.mp3`, `chaos_detonate_first_1.mp3`, `chaos_detonate_extreme_1.mp3`,
  `chaos_detonate_braindrain_1.mp3`, `chaos_detonate_absorbed_1.mp3`
- `chaos_defuse_1.mp3`, `chaos_combo_1.mp3`, `chaos_combo_50_1.mp3`, `chaos_combo_100_1.mp3`
- `chaos_run_complete_1.mp3`, `chaos_run_complete_extreme_1.mp3`
- `chaos_boon_picked_1.mp3`, `chaos_boon_skipped_1.mp3`, `chaos_wave_1.mp3`, `chaos_wave_cleared_1.mp3`
- `chaos_benign_1.mp3`, `chaos_darter_1.mp3`, `chaos_freeze_1.mp3`, `chaos_countdown_1.mp3`,
  `chaos_act_1.mp3`

(File ids map 1:1 to the Circe portrait `lines{}` stem-override system too — a stem like
`chaos_detonate` could later get an explicit emote override, but the `mood` route already covers it.)

### Line budget (per persona)
Triggers/variants tier the budget by how often a trigger fires (frequent → more variety to avoid
fatigue; rare/one-shot → fewer). **Per persona:**

| Trigger (variant) | lines | rationale |
|---|---|---|
| run_started (generic) | 3 | once/run |
| run_started_extreme | 2 | conditional |
| countdown | 2 | once/run |
| wave_escalated (generic) | 4 | repeats every wave |
| wave_gte10 | 2 | conditional |
| act_changed | 2 | ~2–3×/run |
| benign_popped | 4 | very frequent → variety (heavy `chance` throttle, §4) |
| defused | 4 | frequent |
| darter_caught | 2 | occasional |
| freeze_caught | 2 | occasional |
| detonated (generic) | 4 | frequent + emotionally central |
| detonate_first | 2 | once/run |
| detonate_extreme | 2 | conditional |
| detonate_braindrain | 2 | payload-specific |
| detonate_unshielded_combo | 2 | conditional |
| detonate_absorbed | 2 | conditional |
| wave_cleared | 3 | every wave |
| boon_picked | 3 | every wave |
| boon_doubleornothing | 2 | conditional |
| boon_skipped | 2 | every-other wave |
| combo (generic) | 3 | every 10 |
| combo_50 | 2 | rare |
| combo_100 | 2 | very rare |
| run_completed (generic) | 3 | once/run |
| run_completed_extreme | 2 | conditional |
| **Total / persona** | **≈ 65 lines** | |

Personas: **Bambi, Sissy, Circe/Locked** (+ neutral base text-only ≈ same 65 text lines, unvoiced).
**Voiced total ≈ 65 × 3 ≈ 195 mp3s** for the three built-in skins. Future niches add ~65 each.
Phase it: ship **neutral base text pool first** (no audio cost), then voice the highest-impact
triggers per persona (detonate, defuse, combo, run-complete = ~22 lines), then fill the rest.

### Script structure — intent + example lines (key triggers)

> Examples illustrate register only. Keep the resolving mood-token first in each rule's `mood`.

**Detonation gloat** (`ChaosBubbleDetonated`, mood `teasing`/`dark`) — resistance punished:
- *Bambi:* "too slow, sweetie. now it goes off, and Bambi takes every bit of it. that's the deal."
- *Sissy:* "aww, you let it pop. good. let it sink in — sissies don't get to dodge."
- *Circe:* "there. you hesitated, and the spell drinks you anyway. resistance is so... decorative."
- *first-blood (pri 230):* "and there's the first one. don't pretend you'll catch them all now."
- *Extreme (pri 225, `dark`):* "Extreme, and you blinked. cruel of me to enjoy this so much, isn't it."
- *unshielded+combo≥30 (pri 235):* "oh — and there goes your pretty little streak. all of it. gone."

**Defuse praise** (`ChaosBubbleDefused`, mood `pleased`) — obedience rewarded:
- *Bambi:* "good girl. caught it just in time — see how nice it feels to obey fast?"
- *Sissy:* "mmm, defused. such an obedient little thing. keep proving it."
- *Circe:* "quick hands. you snuffed it before it could bloom. clever pet."

**Combo milestone** (`ChaosComboMilestone`, mood `delighted`→`overwhelmed`→`adoring`):
- *generic:* "look at that combo — you're on fire, good girl."
- *≥50 (`overwhelmed`):* "fifty. you're not even thinking anymore, are you? just popping. perfect."
- *≥100 (`adoring`):* "a hundred. my flawless little toy. i could watch you do this forever."

**Run complete** (`ChaosRunCompleted`, mood `affectionate`→`proud` on Extreme) — afterglow/surrender:
- *Bambi:* "all done. look how much you earned for me. such a good, empty, obedient toy. come back soon."
- *Sissy:* "the chaos settles. you let go so sweetly in there. that's my sissy."
- *Circe:* "the storm passes. you surrendered beautifully — come back to me when you crave it again."
- *Extreme (`proud`):* "you survived Extreme. i'm... impressed. don't let it go to that pretty head."

---

## 4. Cooldowns / anti-spam

A frantic run fires events constantly. Defenses already in code: `GlobalMinGapMs = 4000` (hard floor
between ANY two barks), per-rule `cooldown_ms`, `chance`, the one-shot latch, and the
**"drop while speaking"** gate (only protects Normal barks **below** priority 100 — and all Chaos
rules are ≥150, so they PREEMPT and are NOT auto-dropped). Therefore per-rule cooldown + `chance` are
the primary throttle for Chaos. Recommended:

| Trigger | cooldown_ms | chance | reasoning |
|---|---|---|---|
| ChaosRunStarted / countdown | 0 | 1.0 | once per run |
| ChaosWaveEscalated | 6000 | 1.0 | one per wave; bump from 4000 so a fast wave + escalate don't stack |
| ChaosActChanged | 8000 | 1.0 | rare, always speak |
| ChaosBenignPopped | 12000 | **0.10** | fires constantly — heavy throttle; mostly silent, occasional flavor |
| ChaosBubbleDefused | 9000 | **0.5** | keep existing cooldown; add chance so not every defuse talks |
| ChaosDarterCaught | 12000 | 0.6 | occasional |
| ChaosFreezeCaught | 12000 | 1.0 | freeze is a 3.5s lull — a line fits perfectly, always speak |
| ChaosBubbleDetonated (generic) | 7000 | 0.7 | central beat; cooldown prevents back-to-back detonation walls |
| detonate_first / _extreme / _braindrain | 0 / 8000 / 8000 | 1.0 | one-shot-ish / conditional → always when it qualifies |
| detonate_unshielded_combo | 0 | 1.0 | losing a 30+ combo is a big moment — always gloat |
| detonate_absorbed | 10000 | 0.5 | minor event |
| ChaosWaveCleared | 0 | 1.0 | one per wave boundary |
| ChaosBoonPicked / Skipped | 2000 / 2000 | 1.0 | one per draft |
| ChaosComboMilestone (generic) | 5000 | 0.8 | every 10 combo |
| combo_50 / combo_100 | 0 | 1.0 | rare peak moments — always |
| ChaosRunCompleted | 0 | 1.0 | once per run |

Design intent: **rare/climactic beats always speak** (detonate-first, freeze, combo≥50, run-complete);
**high-frequency beats self-throttle** via low `chance` + long cooldown (benign pop 0.10, defuse 0.5).
The 4s global gap means at most ~15 barks/min ceiling regardless. Because Chaos rules preempt, the
queue never piles stale lines — the latest meaningful event wins.

---

## Open questions (for Agent A / event surface)

1. **New ctx fields** the intersectional rules need from `NotifyChaos*`: `run_detonations` (count),
   `shield_absorbed` (bool) on `ChaosBubbleDetonated`; `combo` on detonate (to detect a streak about
   to die); `difficulty` carried into `ChaosBubbleDetonated`/`ChaosComboMilestone`/`ChaosRunCompleted`
   (currently only on `ChaosRunStarted`). Confirm difficulty is a live-read or must be threaded per-fire.
2. **`difficulty` value casing** — base rule uses `difficulty_eq: "Extreme"`; confirm the enum
   stringifies to `"Extreme"` (matcher does case-insensitive string eq, so fine either way).
3. **Payload string values** for `payload_eq` (`braindrain`? `pink_filter`? `spiral`?) — need the exact
   strings `NotifyChaosBubbleDetonated(payload)` emits.
4. **Benign-pop volume** — at 0.10 chance + 12s cooldown is it still too chatty given pop frequency?
   May want chance 0.05 or to drop the trigger entirely and let defuse/detonate carry the voice.
5. Should `ChaosCountdownStarted` and `ChaosRunStarted` both speak (back-to-back, ~3s apart) or merge?
   The 4s global gap means the second would be **blocked** — likely pick ONE (recommend run-started).
6. Confirm Chaos is gated to the persona(s) shipping Circe emotes, or whether Bambi/Sissy portrait
   personas also enter Chaos (affects which skins must ship voiced lines first).
```