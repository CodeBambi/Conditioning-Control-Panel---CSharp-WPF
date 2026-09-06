# The Caucus Race - module contract

Single-player, no-lose kart run through the DtRH tube. Sibling entry to `index.html` / `loom.html`:
`race.html` + `raceBoot.js` + this `race/` folder. Nothing in `game/chaosRun.js` or `engine/scene.js`
is touched. Everything below is the agreed interface between modules that are built in parallel.
If you need to change a signature here, change this file in the same PR and say so in the PR body.

## Coordinate system (track space)

Every gameplay object lives in **track space** `(d, x, h)`:

| axis | meaning | range |
|------|---------|-------|
| `d`  | depth along the spine, metres, wraps at `layout.totalDepth` | `0 .. totalDepth` |
| `x`  | lateral offset on the road, metres, +x = kart's right | `-ROAD_HALF_W .. +ROAD_HALF_W` |
| `h`  | height above the road surface, metres | `0` on the road, ceiling about `2*RADIUS - ROAD_DROP` |

`layout.toWorld(d, x, h, out)` is the ONLY way to turn track space into a `THREE.Vector3`. Never
compute world positions yourself. The frames are parallel-transported so the Big Wheel loop inverts
the world correctly. `layout.frameAtDepth(d)` returns `{pos, tangent, up, right}` (unit vectors, `up`
points from the road toward the tube centre).

Constants live in `race/consts.js` (import them, do not redeclare).

## Modules and their exports

### `race/consts.js` (PR 1)
`RADIUS`, `ROAD_DROP`, `ROAD_HALF_W`, `KART_BASE_SPEED`, `KART_MAX_SPEED`, `KART_MIN_SPEED`,
`GRAVITY`, `POP_HIT_D`, `POP_HIT_X`, `POP_HIT_H`, `MULT_LADDER`, `COMBO_HOLD_SEC`, `INTENSITY_RAMP_SEC`.

### `race/spine.js` (PR 1)
```js
import { createSpine } from './spine.js';
const layout = createSpine({ seed, roomOrder });   // roomOrder: array of room ids from rooms.js
```
Returns a `layout` object that is ALSO a valid argument to `engine/tunnel.js createTunnel(layout)`:
- `RADIUS`, `totalDepth`, `loopDepth` (= totalDepth), `spine` (a closed `THREE.Curve`), `pointAt(t)`,
  `frameAt(t)` - `t` is the NORMALIZED 0..1 parameter exactly as `buildLoopLayout` (fx.js calls
  `layout.frameAt(Math.random())` and reads `pos/normal/binormal`); tunnel.js only needs `spine`.
- `frameAtDepth(d)` -> `{pos, tangent, up, right, normal, binormal}` (`normal`/`binormal` alias
  `up`/`right`; parallel transport through the wheel, re-levelled to world up elsewhere so the
  road never stays banked; cached per 0.5 m).
- `toWorld(d, x, h, out = new THREE.Vector3())` -> `out`.
- `wrap(d)` -> d folded into `[0, totalDepth)`.
- `chunks` -> ordered array of `{ id, kind, d0, d1, room, features }`.
  `kind` in `straight | bendL | bendR | sCurve | climb | dip | ramp | chicane | loop | gate`.
  `features` is an array of:
  - `{ type:'ramp', d, airLen, height }` - lip at `d`, air line from `d` to `d + airLen`, apex `height`
  - `{ type:'boost', d, x }` - boost pad centre
  - `{ type:'loop', d0, d1 }` - the Big Wheel occupies `d0..d1`
  - `{ type:'gate', d, room }` - room boundary; MARQUEE fires here
  - `{ type:'itembox', d, x }` - sugar cube (item roll)
- `featuresBetween(d0, d1)` -> features whose `d` (or `d0`) falls in the wrapped range.
- `roomAtDepth(d)` -> room id.
- `rampAt(d)` -> the ramp feature whose air line covers `d`, else `null`.

Track shape rules: one Tea Garden start straight, then the rooms in `roomOrder`, each room = 4..7
chunks, exactly one loop somewhere after the first two rooms, at least one ramp per room, the whole
thing closes back onto the start (closed curve, `spine.closed = true`). The loop must sidestep
laterally by more than `2*RADIUS` so the tube never self-intersects (see the demo in the pitch).
Each room's FIRST chunk is its `gate` chunk (the gate feature sits mid-chunk); the Tea Garden gate
is `chunks[0]` at `d = 0`, so THE BANK fires on every lap crossing, and the start straight follows.
Every room also carries at least one `itembox`, and a `boost` pad sits on the run-up to the loop.

### `race/rooms.js` (PR 1)
```js
export const ROOMS;          // array of 8 room specs, first is always 'teagarden'
export function rollRoomOrder(seed) -> id[]     // teagarden first, then shuffled
export function createRoomDresser({ scene, layout, rooms }) -> { update(d), applyRoom(fx, roomId, fadeSec), dispose }
```
Room spec: `{ id, name, tagline, biome, colors:{ road, edge, prop, fog, banner }, propKind,
bubbleBias:{ [bubbleKindId]: weightMult }, ambient:{ kind, colors } }`. `biome` is a biome ID from
`game/biomes.js` (`BIOMES_BY_ROOM` is keyed by chamber index 1..4, so the id is resolved with
`biomeById`); `applyRoom` calls `fx.applyRegionGrade(biomeById(biome).style, fadeSec)`. The Tea
Garden borrows `mirrorlake` (no teagarden biome exists); the other seven map to their namesakes.
Rooms: `teagarden, toybox, casino, undertow, mirrors, chapel, greyward, coronation`; each spec also
carries `loud` (rollRoomOrder deals loud/soft alternately after the Tea Garden). `rooms` may be
specs or ids. The dresser adds its own hemisphere + directional light (the props are Lambert; the
tunnel shader ignores lights) and exposes `group`, `spans` (`{id, d0, d1}` per room) and `rooms`.

### `race/bubbles.js` (PR 2)
```js
export const BUBBLE_KINDS;   // see table below
export function createBubbleField({ scene, layout, media, getIntensity, getRoom }) -> field
field.seedChunk(chunk)                  // place lane/air/itembox-adjacent bubbles for a chunk (idempotent per chunk id)
field.spawnAhead(kartD, n)              // 'spawn' placement: appear 35..60 m ahead on the road
field.rain(kartD, n)                    // 'rain' placement: fall from the ceiling ahead of the kart
field.update(dt, t, kart)               // kart = { d, x, h, speed }; runs motion + collision
field.onPop(cb)                         // cb(popEvent)
field.onMiss(cb)                        // cb(missEvent) when a bubble passes behind the kart unpopped (treats only)
field.setDensity(mult)
field.dispose()
```
Pop event: `{ id, kind, payload, strength, points, placement, x, d, worldPos }`.
`kind` is `'treat'` or `'effect'`. `payload` is the `payloadFx` spec name (`flash`, `subliminal`,
`overlay`, `glitch`, `bambiFreeze`, `gifCascade`, `video`, or `null` for treats) plus `overlayKind`
where relevant (`spiral | braindrain | pink_filter`). `strength` is 0..1.

Bubble kinds (mirror `game/variants.js` and `engine/bubbles.js`; sprites from
`/dtrh/assets/bubbles/effects/*.png` and `https://ccp.art/bubbles/*.png`):

| id | kind | payload | points | notes |
|----|------|---------|--------|-------|
| treat | treat | null | 10 | the common bubble, plain sprite |
| golden | treat | null | 50 | rare, JACKPOT chime |
| lucky | treat | null | 25 | rolls an item |
| prism | treat | null | 30 | rainbow, pops neighbours |
| flash | effect | flash | 15 | flash media from the pool |
| subliminal | effect | subliminal | 15 | |
| pink | effect | overlay/pink_filter | 20 | |
| spiral | effect | overlay/spiral | 20 | |
| braindrain | effect | overlay/braindrain | 25 | minIntensity 0.4 |
| glitch | effect | glitch | 20 | |
| freeze | effect | bambiFreeze | 25 | minIntensity 0.15 |
| gifrain | effect | gifCascade | 25 | minIntensity 0.45 |
| video | effect | video | 40 | rare, minIntensity 0.45, host plays it |

Placements: `lane` (rests on the road, h ~0.9, bobbing), `air` (along a ramp air line, h rises
2..5), `spawn` (materialises ahead, wobbles laterally), `rain` (falls from `h = 9` to the road in
about 4 s, rests 2 s, then fizzles). Collision is pass-through: pop when
`|dd| < POP_HIT_D && |dx| < POP_HIT_X && |dh| < POP_HIT_H`.

### `race/score.js` (PR 2)
```js
export function createScore() -> { state, pop(points, kindId), miss(), nearMiss(), bank(), jackpot(), tick(dt), onEvent(cb), reset() }
```
`state = { score, combo, mult, bank, banked, best, popped, treats, effects, nearMisses }`.
Multiplier ladder = `MULT_LADDER` from consts (`[[0,1],[5,2],[12,3],[22,4],[36,6],[50,8]]`).
Combo drops to 0 after `COMBO_HOLD_SEC` without a pop. `bank()` moves `score` into `banked` at the
Tea Garden gate (THE BANK). Events: `{ type:'pop'|'miss'|'combo'|'mult'|'bank'|'jackpot'|'almost', ... }`.

### `race/kart.js` (PR 3)
```js
export function createKart({ scene, layout, reducedMotion }) -> kart
kart.state           // { d, x, h, vh, speed, steer, drift, airborne, boostSec, slowMult, slowSec, lap }
kart.update(dt, input, layout)   // input = { steer:-1..1, accel:0..1, brake:0..1, drift:bool }
kart.applyBoost(sec)             // boost pad / item
kart.applySlow(mult, sec)        // effect pops slow, never stop (speed floor = KART_MIN_SPEED)
kart.setMood(id)                 // 'calm' | 'streamed' | 'fraught' | 'smug' | 'shock' | 'jackpot'
kart.setFraught(v)               // 0..1, drives sweat + antenna kink
kart.camera(out)                 // out = { pos: Vector3, look: Vector3, up: Vector3, roll: number }; up follows the
                                 // transported frame (inverts through the loop); level + roll 0 under reducedMotion
kart.group                       // THREE.Group (cup + EMI back view)
kart.dispose()
```
Speed: cruise `KART_BASE_SPEED`, cap `KART_MAX_SPEED`, floor `KART_MIN_SPEED`. Ramps: when
`layout.rampAt(d)` matches the lip, give `vh` an upward impulse scaled by speed; `GRAVITY` pulls
back; `airborne` while `h > 0.05`. Steering moves `x` with inertia, clamped to `ROAD_HALF_W`
(soft wall, no bounce-off shock). Drift = tighter steer + sparks, no penalty. EMI: CRT body seen
from behind, gloves on the rim, bead antenna with the six mood states, sweat particles when
fraught, her face only as a mirrored emoticon in the tea (`:3`, `>_<`, `o_o`, `^_^`, `$_$`). Text
emoticons only, never a drawn face, never a speech line.

### `race/items.js` (PR 4)
```js
export const ITEMS;   // 10 items: { id, name, glyph, desc, durationSec }
export function createItems({ kart, bubbles, score, fx, hud, payload }) -> { roll(position), current, use(), update(dt), onEvent(cb) }
```
Items: `sugar_rush` (boost), `tea_time` (slow-mo 4 s, world slows, kart does not), `magnet`
(bubbles drift to the lane), `bubble_wand` (rain 12 treats), `lucky_star` (x2 mult 8 s),
`parasol` (next effect pop is a treat instead), `mirror` (Hall of Mirrors flip 6 s: canvas flips,
input does not), `spring` (instant ramp), `pocket_watch` (freeze the combo timer 10 s),
`rabbit_foot` (jackpot chance up). `roll(position)` biases toward catch-up items when the
multiplier is low (position-aware, Mario Kart style). `position` is a multiplier number or
`{ mult }`; omitted, it reads `score.state.mult`. Extra options: `rng` (seeded, Law V) and
`autoUseSec` (default 1.5, 0 = never auto-use). The cube rolls 0.9 s then arms.
Items only touch `kart.applyBoost` and `bubbles.rain`; everything else is an event the run brain
must handle: `{type:'itemRoll'|'itemArm'|'itemUse'|'itemEnd', id}`, `{type:'timeScale', value, sec}`
(tea_time), `{type:'magnet', sec}`, `{type:'multBoost', mult, sec}` (lucky_star),
`{type:'parasol', armed:true}` (run brain owns the flag: next effect pop scores as a treat, no
payload), `{type:'flip', sec}` (mirror), `{type:'jump', vh}` (spring), `{type:'comboFreeze', sec}`
(pocket_watch), `{type:'jackpotBias', mult, sec}` (rabbit_foot).

### `race/hud.js` + `race/race.css` (PR 4)
```js
export function createRaceHud(root) -> hud
hud.setScore(n) hud.setCombo(combo, mult) hud.setSpeed(ms) hud.setBank(n)
hud.banner(name, tagline, colorHex)   // MARQUEE, once per gate
hud.item(glyph | null, name)
hud.toast(text, kind)                 // kind: 'pop' | 'almost' | 'jackpot' | 'bank' | 'item' | 'effect'
hud.flicker()                         // Stat Flicker under glitch
hud.setFraught(v)
hud.setPaused(bool) -> Promise<'resume' | 'end'>   // the Brake; resolves on the player's pick, or 'resume' on setPaused(false)
hud.showEnd(summary) -> Promise<'again' | 'exit'>  // summary = { score, banked, bestCombo, popped, laps, durationSec, personalBest, title? }
hud.dispose()
```
All HUD text is in the DtRH voice: lowercase, short, no em-dashes. `root` is the `.race-hud` div
that `race.html` provides; `payloadFx` gets its own `.sf-hud` sibling so overlays never clip the HUD.
`.race-hud` must stay unpositioned (no `position`/`z-index` of its own): the chrome rides at z3,
below every `.sf-pfx` layer, and the Brake/End screens at z20 pick their own stacking.

### `race/run.js` + `raceBoot.js` + `race.html` (PR 5, integration)
```js
export function createRace({ root, bridge, media, settings, seed }) -> { start(), setPaused(b), dispose() }
```
Composes everything: renderer, `createSpine`, `createTunnel(layout)` from `engine/tunnel.js`,
`createFx` from `engine/fx.js`, `createRoomDresser`, `createBubbleField`, `createKart`,
`createScore`, `createItems`, `createRaceHud`, `createPayloadFx` from `game/payloadFx.js`,
`createScreenShake` from `game/screenShake.js`. Intensity ramps 0..1 over `INTENSITY_RAMP_SEC`
and gates which bubble kinds may appear. Treat pops go to score; effect pops call
`payloadFx.applyPayload({ payload, strength }, { durationMult })`, `video`/`audio` go to the host
through the `fire-payload` bridge message exactly like `chaosRun.js` does today. ESC = Brake
(pause + end screen). Run end sends `run-ended` (below).

## Host protocol (bridge.js, Protocol v1)

Page -> host (`bridge.send(type, data)`): `ready` (announceReady), `heartbeat`, `pong`, `sfx {name, scale}`,
`fire-payload {kind:'video'|'audio', strength, durationMult}`, `run-started {seed}`,
`run-ended {score, banked, durationSec, bestCombo, popped, treats, effects, laps, seed}`,
`boot-error {message}`, `report-bug {text}`, `fullscreen-set {on}`, `exit`.

Host -> page: `init {protocol, settings:{masterVolume, reducedMotion}, modId, modContent}`,
`manifest {images:[{name,url}], videos, skipped, truncated}`, `favorites {names}`,
`payout-result {baseXp, skillMult, finalXp, sparksEarned, previousBest, dryRun}`, `pause {on}`,
`ping`, `exit-request`.

`sfx` names must exist in `Resources/sounds/chaos/*.mp3` (C# `ChaosSfx.Play(name, scale)`), e.g.
`tunnel_powerup_collect`, `golden_pop`, `chain_pop`, `streak_milestone`, `pb_fanfare`,
`rank_up`, `ui_click`, `ui_denied`, `surface`, `depth_change`, `time_slow_in`, `time_slow_out`.
Bubble pops themselves play in-page from `/dtrh/assets/bubbles/sfx/` via `engine/audioBus.js`.

## Rules for every PR in this stack
- 600 changed lines max per PR. Split if you are over.
- Only add files under `Resources/web/dtrh/race/` (plus `race.html`, `raceBoot.js` in PR 5 and the
  C# host in its own PR). Do not edit `chaosRun.js`, `scene.js`, `tunnel.js`, `fx.js`, `payloadFx.js`.
- Remote media (Scrolller, CDN) is DOM-only via `hostMedia.drawDom()`. Never a WebGL texture.
- EMI design is locked: no generated faces, text emoticons only, she never mouths words.
- No em-dashes anywhere (code comments, HUD copy, PR bodies).
- Every module gets a small `// self-check` block that can run in node with a THREE stub only if it
  costs nothing; otherwise `node --check` clean is the bar.
