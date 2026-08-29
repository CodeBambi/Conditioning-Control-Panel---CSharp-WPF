# EMI Desk - lines file schema and draw algorithm (2026-08-29)

The one file the C# desk line engine loads: `Resources/emi/desk-lines.json` (UTF-8, no BOM,
2-space indent, LF; ships via the `Resources\emi\**\*.json` Content item in the csproj). It is
GENERATED: `python docs/emi-desk/tools/merge-lines.py` rebuilds it from `docs/emi-desk/lines/*.json`
(the writers' files plus `round1.json`) and the editor's cut/fix tables inside that script;
`python docs/emi-desk/tools/check-lines.py` audits it and exits 1 on any error. Edit the source
files and the merge script, never the generated file.

Voice, fence and spice rules live in `VOICE.md`; the moment catalogue and hook plan in
`MOMENTS.md`; the widget contract in `BRIEF.md`. This document only says what the JSON means and
how the engine must draw from it.

## 1. Top level

```json
{
  "version": 1,
  "generated": "2026-08-29",
  "moments":  { "<momentId>": <Moment>, ... },     // 90 v1 moments
  "pools":    { "<poolId>": [ <Line>, ... ], ... },  // 90 pools, 1691 lines + 30 hold rows
  "asks":     [ <Ask>, ... ],                        // 67
  "dork":     { "pool": "common.dork", "odds": 0.08, "limit": { "per": "launch", "max": 1 } },
  "deferred": [ "<momentId>", ... ]                  // 40 MOMENTS.md DEFER ids, no pools, never fire
}
```

Unknown keys anywhere are ignored (forward compatibility). `version` bumps only when a shape
changes in a way an older engine would misread.

## 2. Moment

```json
"ringOpen": {
  "pools": ["ringOpen", "common.attention"],   // specific pool first, then commons in attachment order
  "odds": 0.08,                                // P(speak) per fire, after cooldown/limit/floor
  "cooldownMs": 180000,                        // per-moment; 0 = none
  "priority": 2,                               // 1 filler, 2 normal, 3 ceremony
  "mix": 0.65,                                 // P(draw from the specific pool) when both sides have unseen lines
  "spiceCeiling": 2,                           // max spice this moment may speak (0..2)
  "hold": false,                               // true = wordless face hold, see 2.2
  "askOdds": 0.15,                             // P(ask instead of a line) when the ask gates pass; 0 = never
  "limit": { "per": "launch", "max": 1, "perTarget": "ever" },   // optional, see 2.1
  "cooldownKey": "ringOpen",                   // optional: share another moment's cooldown clock
  "poolWhen": { "common.win": ["passed"], "common.loss": ["!passed"] },   // optional: gate a whole pool
  "holdMs": 1400, "tailMs": 20000, "holdUntilReleased": true             // hold moments only
}
```

### 2.1 `limit`

`per` is the bucket the counter lives in; `max` is fires allowed per bucket (a fire that loses
the odds roll still counts as a fire, so "1/launch" means one chance, as MOMENTS.md intends).

| per | bucket resets when |
|---|---|
| `ever` | never (persisted in user data) |
| `launch` | the process starts (MOMENTS.md says "sitting" and "session" for the same thing) |
| `day` / `night` | the local date turns at 06:00 (a night = 22:00..06:00 of one date) |
| `video` | each `VideoStarted` |
| `run` | each Session preset run (`SessionStarted`) |
| `lockdown` | each lockdown arm |
| `rush` | each Pink Rush |
| `target` | per `{target}` value, per launch |
| `featureDay` | per `{target}` value, per day |
| `version` | per app version string |

`perTarget: "ever"` is an extra, persisted, per-target once (suggestionIgnored3x: once per card
ever, on top of once per launch).

### 2.2 Hold moments

`hold: true` moments never speak. They draw a row from `common.hold` (or nothing when `pools` is
`[]`, e.g. `appClosing`: the flinch is in code) and hold that face:

- `holdMs`: fixed hold (effectDeclined, askIgnored: 1400 then `...` and back to idle).
- `holdUntilReleased`: hold until the source releases (avatarSpeaking, awarenessReaction,
  attentionCheckShown, intakeRunning, lockdownCountdown, emergencyExitOpened, firstLaunchEver).
- `tailMs`: silence after release (20000 for the avatar/awareness holds; 300000 for panicPressed:
  she stays silent for five minutes after the panic key, no exceptions, no ceremonies).

While a hold is active nothing speaks, priority 3 included. `askAnswered` is listed with
`pools: []` and `odds: 0` so the engine knows the id: the ask's own yes/no reaction is the line.

## 3. Line

```json
{ "id": "ringOpen.007", "t": "pick one. no wrong answers. some wronger.", "face": "^_~",
  "spice": 0, "chain": "cool", "typo": true, "double": true, "when": ["channel:spiral"] }
```

| field | meaning |
|---|---|
| `id` | `<pool>.<3-digit>`; commons are `common.idle.001`. Stable across regenerations only if the source order does not change; the engine must not persist ids across versions except in the recent ring. |
| `t` | the line, <= 60 chars, lowercase, plain ascii. Tokens `{target} {n} {minutes} {level} {streak} {via} {channel}` are substituted from ctx; see 5.3. |
| `face` | reaction face. Any `chains.js` FACES string, plus `(´∀`)` (VOICE.md lists it; render it as KAO class: +10% size). |
| `chain` | optional `chains.js` CHAINS id, played before the bubble lands. `pet` and `sleepy` do not exist in chains.js; the writers' `pet` is stored as `love` and `sleepy` as `blink`. |
| `spice` | 0 innocent, 1 suggestive, 2 lewd. |
| `typo` | informational (the line carries a deliberate typo); the engine ignores it. |
| `double` | a "suspiciously human" line: rationed to ONE per launch across all pools (5.5). |
| `when` | gates, all must pass (5.4). Absent = always eligible. |

Hold rows (only in `common.hold`): `{ "id", "t": "", "face", "chain": null | "<id>", "holdMs", "spice": 0 }`.

## 4. Ask

```json
{ "id": "ask.glassOffer.001", "moment": "glassOffer", "when": ["channel:spiral"],
  "q": "spiral?", "face": "@_@", "chips": ["spin", "nah"],
  "yes": { "t": "hold still. or don't. it works either way.", "face": "@_@", "double": true },
  "no":  { "t": "ok. i'll keep it warm.", "face": "._." },
  "effect": "spiral", "effectNo": "video", "spice": 1 }
```

- `q` <= 30 chars (tokens allowed), `chips` exactly two, <= 12 chars each. Chip 1 is YES.
- `effect` is what a YES does: `spiral | video | rain | burst | shrink | bedtime | none |
  pinTop:<targetId> | open:<targetId>`. `{target}` and `{channel}` inside an effect are
  substituted from ctx like a line token (`pinTop:{target}`, `{channel}` = repeat the channel that
  just fired). `effectNo` (only `ask.glassOffer.005` "spin or show?") makes chip 2 a second action
  instead of a decline; both chips then count as answered.
- `yes` / `no` reactions are lines (same text rules); a `double: true` reaction spends the launch's
  one double (5.5) and is skipped for a plain sibling... there is none, so the engine simply plays it
  and marks the double spent.
- `spice` is checked against the moment's ceiling and the user's ceiling like a line.
- An ask whose `moment` is in `deferred` (only `ask.dragged3x.001`) is inert until that moment lands.
- Effect feasibility, checked at draw time and failing silently: `open:arcademy` requires the
  Arcademy door to be available; `open:<id>` / `pinTop:<id>` require a known EmiTargets id
  (`companion`, `fyp`, ring card ids); `pinTop` is skipped when the target is already pinned;
  `video` requires a video library; `bedtime` is skipped when bedtime is already set.

## 5. The draw algorithm (owner-locked, BRIEF §7/§8)

`Fire(momentId, ctx)`:

1. **Unknown or deferred moment** -> return. **Hold moment** -> 2.2, return.
2. **Active hold** (any hold moment holding, or a hold tail running) -> return, ceremonies included.
3. **Panic silence**: 300 s after `panicPressed` nothing speaks.
4. **Limit** (2.1) exhausted -> return. Increment the bucket counter now.
5. **Cooldown**: `cooldownMs` since this moment (or its `cooldownKey`) last SPOKE -> return.
6. **Global floor**: 45 000 ms since ANY line or ask was shown -> return, unless `priority == 3`.
   (MOMENTS.md §3 says 180 s; BRIEF §8 says 45 s and the owner locked the BRIEF, so 45 s it is.
   Priority 3 bypasses odds and the floor but never a hold.)
7. **Odds**: `priority == 3` or `rand < odds`, else return.
8. **Bedtime**: while bedtime is set (until 06:00) asks and glass offers are muted; lines still
   play at their odds, and `lateNight`/`smallHours` never offer bed again that night.
9. **Ask?** if `askOdds > 0`, ask gates pass (5.6) and `rand < askOdds`: pick an eligible ask for
   this moment (gates 5.4 vs ctx, spice <= min(ceiling, user), effect feasible, not the last ask
   asked), show it, return.
10. **Dork?** if `dork.limit` not spent, the moment is not a hold, `priority < 3` and
    `rand < dork.odds`: draw from `common.dork` (its own shuffle bag) INSTEAD of the moment's
    pools, spend the dork limit, go to 13.
11. **Pool choice**: build the eligible set of each pool in `pools` (5.2 filter). `poolWhen` drops
    whole pools whose gates fail. If both the specific pool and at least one common pool have
    unseen eligible lines: specific with P = `mix`, else a common pool (uniform among the commons
    that still have unseen lines). If only one side has unseen lines, that side. If neither has
    unseen lines the bags reshuffle (5.1) and the choice repeats once.
12. **Deal** one line from the chosen pool's bag (5.1).
13. **Speak**: play `chain` if any, then the bubble with `t` after token substitution (5.3), face =
    `face`. Record the id in the recent ring, stamp the moment's cooldown and the global floor,
    spend the double if `double`.

### 5.1 Shuffle bags

Every pool has its own bag: the eligible ids shuffled, dealt without replacement. When the bag is
empty it is reshuffled with one constraint: the last three dealt ids may not land in the first
three positions of the new bag. Bags live for the launch (not persisted). A line filtered out at
draw time (spice, gate, missing token) is skipped, not consumed: put it back at the end of the bag.

The **global recent ring** keeps the last 40 ids spoken (persisted across launches). Common pools
avoid ids in the ring while they still have a line outside it; specific pools ignore the ring
(their bags already do the job and the small ones would starve).

### 5.2 Per-fire filtering

A line is eligible when all of: `spice <= min(moment.spiceCeiling, settings.EmiDeskSpice)`
(`EmiDeskSpice` 0..2, default 2); every `when` gate passes (5.4); every token in `t` is present
and non-empty in ctx (5.3); if `double`, the launch's double is not yet spent (5.5).

Common pools carry spice 1 and 2 lines even though some of their attachment moments have a
ceiling of 0 or 1 (e.g. `common.encourage` after `tierLapse`, `common.afterEffect` after
`lockdownEnded`). That is by design: the ceiling is applied here, per fire, so one pool serves
every moment it is attached to. The checker only enforces ceilings on specific pools.

### 5.3 Tokens

`{target} {n} {minutes} {level} {streak} {via} {channel}`. Substitute from ctx verbatim; when the
key is missing or empty the line is skipped (every token pool has plain siblings, the checker
enforces it). Values must already be lowercase display text: `featureOpened`'s tab key
(`gradedintake`, `bambitakeover`) and `videoRunning`'s file name must be mapped to a display name
(and the extension stripped) by the hook, not by the engine, and `appOpened`'s `{target}` (the
away bucket) is never used by a line. `{ask}` exists in the askAnswered/askIgnored ctx but no line
uses it. `{name}` (VOICE.md) is not used by any line and the engine need not support it.

### 5.4 Gates (`when`)

Three forms, all evaluated against ctx: `flag` (ctx value truthy), `!flag` (missing or falsy),
`key:value` (string-equal, case-insensitive). Gates used in the file and the ctx field they read:

| gate | moments | ctx field |
|---|---|---|
| `pickIsTop` | ringPick lines, ask.ringPick.001 | bool: the picked card is the most-used |
| `bigger` / `!bigger` | resized | bool |
| `channel:spiral|video|burst|rain` | glassOffer, effectFired lines and asks | string |
| `melt` | brainDrainOn | bool |
| `running` / `!running` | blinkTrainerStarted | bool |
| `passed` / `!passed` | intakeClosed (lines, poolWhen, ask) | bool |
| `topUnpinned` | ask.ringOpen.001 | bool: the most-used card is not pinned (editor addition; the ring owner passes it) |

`ringDismissed` must carry the same `{target}` (top card) as `ringOpen` for `ask.ringDismissed.002`
(`open:{target}`); MOMENTS.md listed no ctx for it.

### 5.5 Doubles

At most ONE `double: true` line (or ask reaction) per launch across all pools. Once spent, doubles
are filtered out for the rest of the launch. The frequent pools (common.idle, common.attention,
idleShort, appIdleLong, summoned, ringOpen, petted, featureOpened) carry none.

### 5.6 Ask gates

An ask may be shown only when all hold: `askOdds > 0`; at least 10 minutes since the last ask;
this is the third or later summon of the launch; no ask is currently on the glass (an ask waits
until answered or until it gives up: 40 s, then `askIgnored` hold `-_-` 1400 ms, `...`, idle);
fewer than three unanswered asks in a row this launch (the third unanswered = no more asks this
launch); bedtime is not set (mutes asks and glass offers until 06:00); no session is running, no
video is playing, the avatar is not speaking, the app is not minimised (BRIEF §7). `askOdds` is 0
on videoRunning, intakeRunning, lockdownCountdown, sessionStarted/Halfway/LastMinute/Phase and
attentionCheckShown regardless.

When an ask's effect fires a moment of its own (bedtime -> `bedtimeSet`, spiral -> `effectFired`,
open:arcademy -> `arcademyOpened`), the hook passes `ctx.fromAsk = true` and the engine returns
without speaking: the ask's reaction already spoke. The moment's limit/cooldown are still stamped.

### 5.7 Priority

1 filler, 2 normal, 3 ceremony (desktopFirstBoot, bedtimeSet, sessionCompleted, levelUp,
streakMilestone, and every hold). Priority 3 bypasses odds and the global floor; it never bypasses
a hold, the panic silence, a limit, or bedtime muting of asks. Two fires in the same tick: the
higher priority wins, ties go to the first.

### 5.8 Dork channel

`dork.odds` (0.08) is rolled at step 10 for any speaking moment with `priority < 3`; a hit draws
from `common.dork` instead of the moment's pools and spends `dork.limit` (once per launch). The
dork pool has its own bag and ignores the moment's spice ceiling only in the sense that every dork
line is spice 0 anyway.

## 6. Ctx payload per moment

Tokens a line may use and flags a gate may read, per v1 moment (MOMENTS.md tables; `-` = none).
The checker's `CTX` table in `check-lines.py` is the machine copy of this list.

| moment | tokens | flags |
|---|---|---|
| desktopFirstBoot, bedtimeSet, sheListeningOn, avatarMuted, avatarKept, flashesStarted, attentionCheckPassed, subliminalsStarted, bubbleCountLost, mantraCompleted, arcademyOpened, dtrhOpened, fypOpened, intakeOpened, sessionLastMinute, pinkRushStarted, morningFirst, crashRecovered | - | - |
| summoned | via (rail/hotkey), minutes | - |
| dismissed | minutes | - |
| ringOpen | n (cards), target (top card) | topUnpinned |
| ringDismissed | target (top card) | - |
| ringPick, arcademyFromRing | target, n (lifetime opens) | pickIsTop |
| pinAdded, suggestionIgnored3x | target | - |
| resized | n (width) | bigger |
| petted | n (pets this launch) | - |
| glassOffer | channel, target (video name) | channel |
| effectFired, effectDeclined | channel | channel |
| askAnswered, askIgnored | ask | yes |
| bedtimeBroken | n (skips) | - |
| takeoverEnded | minutes | - |
| featureOpened | target (display name) | - |
| featureOpenedRepeat | target, n | - |
| flashesStopped | minutes | - |
| videoRunning | target (display name), minutes | - |
| videoEnded | minutes | passed |
| attentionCheckFailed | n | - |
| overlaySpiralUp | channel, n (seconds) | channel |
| brainDrainOn | n (intensity) | melt |
| bubbleCountWon | n (xp) | - |
| blinkTrainerStarted | minutes | running |
| intakeClosed | - | passed |
| emergencyExitOpened | target, n | - |
| lockdownArmed, lockdownEnded | minutes | - |
| engineStarted | - | systemInitiated |
| engineStopped | minutes | - |
| rampStepUp | n | - |
| sessionFeatureArrived | target, n (start minute) | - |
| sessionStarted | target, minutes | - |
| sessionPaused | n (pause count) | - |
| sessionResumed | minutes (remaining) | - |
| sessionPhaseChanged | target, n | - |
| sessionHalfway | minutes (left) | - |
| sessionCompleted | target, minutes, n (xp) | - |
| sessionAbandoned | minutes | - |
| levelUp | level | - |
| xpBigAward | n, target (source) | - |
| achievementUnlocked, questCompleted, skillUnlocked | target | - |
| streakMilestone, streakKept | streak | - |
| streakBroken | streak, target (reason) | - |
| lateNight, smallHours | n (hour) | - |
| appIdleLong, idleShort, longSitting, backSoon | minutes | - |
| premiumTeaseSeen, lockedCardTapped, tierUp, tierLapse, dailyFreeToday | target | - |
| appOpened | target (away bucket; unused by lines) | - |
| updateAvailable, afterUpdate | target (version) | - |
| holds (avatarSpeaking, awarenessReaction, attentionCheckShown, intakeRunning, lockdownCountdown, panicPressed, effectDeclined, askIgnored, appClosing, firstLaunchEver, emergencyExitOpened) | - | - |

## 7. Settings the engine reads

`EmiDeskSpice` (0..2, default 2, BRIEF §5). Everything else (odds, cooldowns, floor, ask cadence)
is data in this file or a constant in the engine; there is no per-moment user setting.
