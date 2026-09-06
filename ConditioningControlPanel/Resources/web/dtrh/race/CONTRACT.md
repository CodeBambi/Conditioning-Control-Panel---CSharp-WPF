# Racing Thoughts - module contract

> Named **The Caucus Race** until 2026-09-06, so older notes, memory files and PR titles
> that say "caucus" mean this game. The folder, the module names and the host message types
> never changed.

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

`ROAD_HALF_W` (3.2) is the lateral EXTENT of track space, not the edge of the asphalt: the road
ribbon rooms.js draws runs out to `KERB_INNER_W` (2.875) and the kerb steps up from there to
`KERB_OUTER_W` (3.5). Two derived limits fall out of that and both live in `consts.js`:
`KART_X_MAX` is as far as the kart CENTRE may go before the saucer's rim would leave the asphalt,
and `LANE_X_MAX` is as far out as a bubble may sit and still be poppable from there.
| `h`  | height above the road surface, metres | `0` on the road, ceiling about `2*RADIUS - ROAD_DROP` |

`layout.toWorld(d, x, h, out)` is the ONLY way to turn track space into a `THREE.Vector3`. Never
compute world positions yourself. The frames are parallel-transported so the Big Wheel loop inverts
the world correctly. `layout.frameAtDepth(d)` returns `{pos, tangent, up, right}` (unit vectors, `up`
points from the road toward the tube centre).

Constants live in `race/consts.js` (import them, do not redeclare).

## Camera aspect rule

**Every base fov in this game is a HORIZONTAL fov wearing a vertical number, and the number is
measured at 16:9.** `THREE.PerspectiveCamera` takes a vertical fov, so a hard-coded one keeps the
same height of picture at every window shape and lets the WIDTH go wherever the aspect drops it.
That is fine on a desktop and wrong on a phone: the run's 72 collapses to about 37 horizontal at
390x844, which turns the road into a slit and puts both side lanes off-screen, and blows out to
about 118 at 844x390.

`race/viewport.js` owns the fix and nothing else may re-derive it:

```js
import { vFovForAspect, hFovFor, bindViewportResize } from './viewport.js';
vFovForAspect(baseVFov, aspect, maxVFov = MAX_VFOV) -> vertical fov, degrees
hFovFor(vFov, aspect)                               -> the horizontal fov that pair covers
bindViewportResize(fn)                              -> dispose()
```

- `hFov = 2 * atan(tan(base / 2) * 16 / 9)` is the constant being protected; the vertical is solved
  back from it per aspect and clamped to `[MIN_VFOV, maxVFov]`.
- **At 16:9 the two cancel exactly, so `vFovForAspect(base, 16 / 9) === base` and no desktop frame
  moves by a pixel.** Every change to this file must keep that identity.
- Narrower than 16:9 the vertical opens up to the clamp; wider it closes down, which is what stops
  an ultrawide from being a fisheye.
- `MAX_VFOV` is 102 for the road (picked on a 390x844 shot: wide enough for both lanes, short of a
  tunnel that reads as a lens). The menu stage passes its own 86, because a character reads wrong at
  a fov a tunnel still survives.
- The kicks stay ADDITIVE on top of the solved base: `run.js` adds boost / drift / tea_time to
  `fovBase`, never to `FOV_BASE`.
- `bindViewportResize` is the only resize path. `resize` alone misses an iOS orientation flip (it
  reports the OLD size) and misses a mobile URL bar sliding away (that only moves `visualViewport`),
  so it binds `resize` + `orientationchange` + `visualViewport` and re-runs one frame after a flip.
- Anything reading `camera.fov` per frame (`race/speed.js`) already follows for free: read it, never
  assume a number.
- `race/smoke/fov-check.mjs` is the guard: `node race/smoke/fov-check.mjs`.

## Modules and their exports

### `race/consts.js` (PR 1)
`RADIUS`, `ROAD_DROP`, `ROAD_HALF_W`, `KERB_INNER_W`, `KERB_OUTER_W`, `SAUCER_R`, `SAUCER_R_ROAD`,
`KART_X_MAX`, `LANE_X_MAX`, `KART_BASE_SPEED`, `KART_MAX_SPEED`, `KART_MIN_SPEED`,
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
A crossed cube hides, throws its splits and puts a BILLBOARD flash on its spot: never a solid mesh,
which used to read as a second, empty white box standing beside the shards.
Road furniture (item box and its twelve splits, boost pad, ramp lip, air marker) takes its geometry
from `props.glb` through `race/propPack.js` once the pack resolves; before that, and forever if the
pack or a node is missing, the hand-built voxel primitives stay. Placement, physics and animation
are untouched by the swap: only geometry and material change. `roadMatrix` builds a LEFT handed
basis, so pack geometry driven by it is mirrored on x to keep its winding (and its outline hull)
the right way round.

### `race/bubbles.js` (PR 2)
```js
export const BUBBLE_KINDS;   // see table below
export function createBubbleField({ scene, layout, media, getIntensity, getRoom }) -> field
field.seedChunk(chunk)                  // place lane/air/itembox-adjacent bubbles for a chunk (idempotent per chunk id)
field.spawnAhead(kartD, n)              // 'spawn' placement: appear 35..60 m ahead on the road
field.rain(kartD, n)                    // 'rain' placement: fall from the ceiling ahead of the kart
field.update(dt, t, kart)               // kart = { d, x, h, speed }; runs motion + collision
field.onPop(cb)                         // cb(popEvent)
field.onMiss(cb)                        // cb({ id, points, d, x, h }) when a treat passes behind the kart unpopped
field.setDensity(mult)
field.spawnAt({ kindId, placement, d, x, h, eventId })   // PR c2: an explicit placement from a track cue; the slot id, or -1 when the pool is full
field.setTracked(on)                    // PR c2: a track is loaded, so setDensity gates the CUE spawns and leaves the seeded lanes alone
field.dispose()
```
Pop event: `{ id, kind, payload, strength, points, placement, x, d, eventId, worldPos }`.
`eventId` is the chart event the bubble came from, or null; `onMiss` events carry it too.
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
| video | effect | video | 40 | DARK since 2026-09-06, never spawns (see below) |

A row may carry `spawn: false`. Video bubbles are dark since 2026-09-06: `rollKind` leaves the row out
of every pool and `field.spawnAt` returns -1 for it, so no roll, lane line, rain or track cue can put
one on the road, and `CaucusHostService` refuses a `fire-payload {kind:'video'}` as well. The row, its
sprite and its THE MIX `video` slot stay put for a later use.

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
kart.emiModel()                  // the mounted race/assets/emi.glb root, or null while it loads
kart.emiReady(cb)                // cb(root) when she is mounted (fires at once if she already is)
kart.setFace(i)                  // face atlas frame 0..4 (menus and results; never seen in race)
kart.pose(name, opts)            // the pose layer, race/emiPoses.js
kart.dispose()
```
`createKart` also takes `pixel` (race/pixel.js): the glb's textures land after the run's one
`retexture(scene)` pass, so the rig walks them through `preparePixel` when the model mounts. EMI is
the Blender glb from the moment `emi.glb` resolves; the primitive CRT is the fallback and rides on
if it never does.
The cup and the saucer come from `props.glb` (`kart_cup`, `kart_saucer`) on the same terms: the
lathe cup, its rim torus, the handle tube, the saucer cylinder and its rim are the fallback, and
the tea disc, the pink saucer mark, `cupLight`, the seat and `TEA_Y` are shared by both paths.
Speed: cruise `KART_BASE_SPEED`, cap `KART_MAX_SPEED`, floor `KART_MIN_SPEED`. Ramps: when
`layout.rampAt(d)` matches the lip, give `vh` an upward impulse scaled by speed; `GRAVITY` pulls
back; `airborne` while `h > 0.05`. Steering moves `x` with inertia, clamped to `KART_X_MAX` (soft wall,
no bounce-off shock): THE KERB HOLDS THE SAUCER, NOT THE CUP, so the limit is measured from the
saucer's outer rim (`KERB_INNER_W - SAUCER_R_ROAD - KERB_KISS` = 1.775 m) and the dish stops on the
kerb line instead of hanging a metre and a half past it. Drift = tighter steer + sparks, no penalty. EMI: CRT body seen
from behind, gloves on the rim, bead antenna with the six mood states, sweat particles when
fraught, her face only as a mirrored emoticon in the tea (`:3`, `>_<`, `o_o`, `^_^`, `$_$`). Text
emoticons only, never a drawn face, never a speech line.

### `race/emiPoses.js` (pass four, EMI's body)
```js
export const POSES, PIVOTS;   // the pure preset table, and the four glb pivots a preset may name
export function resolvePose(name, opts) -> flattened target
export function createPoseLayer(model) -> { set(name, opts), update(dt, ctx), dispose, fraught, name }
```
Poses: `cruise` (the rest), `drift`, `boost` -> `boostOut`, `air`, `landing` / `landingKerb`,
`grab`, `clamp`, `tuck`, `throw`, `cheer`. `opts` = `{ side:-1|1, tier:1..3, hold:sec }`; sided
presets are authored for +1 and mirrored for -1. Every value is an offset on the pack's authored
rest rotation, blended on damped springs (Law XI, never a linear tween), and a pose with a `hold`
falls back to `next` on its own. `clamp` and `landingKerb` report `fraught` and emi.js takes the
max of that and the run brain's. run.js only ever calls `kart.pose(...)`; the layer exists only
while the glb is mounted (the primitive EMI has no limbs to pose).

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
hud.pickupRoll()                      // THE PICKUP card: pops near the cup with the rolling '?' (on itemRoll)
hud.pickupArm(glyph, name, onLand)    // flips to the decided item (on itemArm), holds, flies into the slot, onLand as it lands
hud.pickupClear()                     // on itemUse or a reset: the card and the slot's highlight both go
hud.toast(text, kind)                 // kind: 'pop' | 'almost' | 'jackpot' | 'bank' | 'item' | 'effect'
hud.flicker()                         // Stat Flicker under glitch
hud.setFraught(v)
hud.mixer(state)                      // THE MIXER rail: state = cocktail.state(); chips per live category + the served recipe
hud.strobe(charges)                   // white edge blink on a flash charge / roll
hud.setTint(depth)                    // 0 | 1 | 2: the chrome goes pink with the wash
hud.setPaused(bool) -> Promise<'resume' | 'end'>   // the Brake; resolves on the player's pick, or 'resume' on setPaused(false)
hud.showEnd(summary) -> Promise<'again' | 'exit'>  // summary = { score, banked, bestCombo, popped, laps, durationSec, personalBest, title? }
hud.dispose()
```
All HUD text is in the DtRH voice: lowercase, short, no em-dashes. `root` is the `.race-hud` div
that `race.html` provides; `payloadFx` gets its own `.sf-hud` sibling so overlays never clip the HUD.
Player-facing copy never says bank/banked: THE BANK reads as `kept` in the score block, the toast
and the end card. Identifiers, event types and css classes keep `bank`.
`.race-hud` must stay unpositioned (no `position`/`z-index` of its own): the chrome rides at z3,
below every `.sf-pfx` layer, and the Brake/End screens at z20 pick their own stacking.

### `race/run.js` + `raceBoot.js` + `race.html` (PR 5, integration)
```js
export function createRace({ root, bridge, media, settings, seed }) ->
  { start(), setPaused(b), dispose(), setCameraOverride(fn), setStage(s), reseed(seed), renderer, pixel, audio, hud, camera,
    setTrack(chart | null), replaceTrack(chart), trackClock(t, playing), trackEnded(), track }
```
Track charts (PR c2, CHART.md): `setTrack` before `start()`; `replaceTrack` when the words pass lands
on a live run; `trackClock` is the host's tick and `race.track` the CHART.md track object or null.
With a track set, intensity follows the chart's energy curve (smoothed 2 s, floor 0.05), the random
`spawnAhead` / `rain` timers stand down while `seedChunk` keeps dressing the road, every cue spawn
goes in at `d = kart.d + kart.speed * max(dueIn + at, 0.25)`, gates and act changes take the act's
room and name, and the run ends at `durationSec - 0.25`. Without one nothing changes.
Composes everything: renderer, `createSpine`, `createTunnel(layout)` from `engine/tunnel.js`,
`createFx` from `engine/fx.js`, `createRoomDresser`, `createBubbleField`, `createKart`,
`createScore`, `createItems`, `createRaceHud`, `createPayloadFx` from `game/payloadFx.js`,
`createScreenShake` from `game/screenShake.js`. Intensity ramps 0..1 over `INTENSITY_RAMP_SEC`
and gates which bubble kinds may appear. Treat pops go to score; effect pops call
`payloadFx.applyPayload({ payload, strength }, { durationMult })`, `video`/`audio` go to the host
through the `fire-payload` bridge message exactly like `chaosRun.js` does today. ESC = Brake
(pause + end screen). Run end sends `run-ended` (below).

As built (PR 5 reality notes):
- `race/input.js` is the single reader of keyboard + gamepad + touch:
  `createInput({ root }) -> { read(), onAction(cb), flush(), dispose(), touchEl }`,
  `read()` = `{ steer, accel, brake, drift, jump }` with `accel` defaulting to 1 when nothing is pressed.
  The three sources are ADDITIVE and never remap one another (Law II): `steer` takes whichever source
  has the larger magnitude (an analog source, stick or thumb, also turns the digital easing off),
  `accel` / `brake` take the max, `drift` is an OR, and `jump` plus the ACTIONS are edges, so any
  source may raise one and each is exactly one press. `flush()` clears all three.
- `race/touch.js` is that third source and the ONLY pointer path into the run:
  `createTouch({ root, fire }) -> null | { el, read(), flush(), dispose() }`. It returns null unless the
  page is touchable (`(pointer: coarse)` or `navigator.maxTouchPoints > 0`), so a mouse desktop and the
  WebView2 desktop build build not one node of it; `?touch=1` forces it on (the headless screenshot aid)
  and `?touch=0` forces it off. The layer is `.rh-touch` at z12 inside `.race-hud`: above the chrome (z3)
  and the payload layers (z4 / z9), below the Brake and End screens (z20), the countdown (z21), the menu
  (z25), the cards (z26) and the splash (z30), so every card still takes the tap first. Left `STEER_SIDE`
  (55%) of the width steers by horizontal drag from the touch-down point (`STEER_DEAD_PX` 6 dead,
  `STEER_LOCK_PX` 72 to full lock, zero on release, a ring-and-dot thumb pad while held); the rest of the
  width taps (under `TAP_MS` 220 and `TAP_PX` 14 of travel) for one jump press and holds for drift.
  A DOUBLE TAP is a jump on EITHER half: two taps inside `DOUBLE_TAP_MS` (320), so a thumb already
  mid-corner still has one. `TAP_ECHO_MS` (60) folds a lift reported twice into one tap. A press that
  lands on a button is never a tap, so double tapping `use` spends the item twice and never jumps.
  Accel is never touched: nothing pressed is cruise, and there is no brake pedal on glass. Three buttons
  fire existing actions through `input.onAction`: pause (`'brake'`), mute (`'mute'`) and a use button
  (`'item'`) that only appears while `.rh-item` carries `is-held`. Pointer Events drive steer and drift;
  TouchEvent is a FLOOR under them, never a second scheme: `touchstart` preventDefault takes the gesture
  away from WebKit (without it Safari cancels our pointers to run its own and no tap ever lands), a
  `touchend` that looks like a tap raises the same tap, and the fingers still on the glass at a
  `touchend` end any held id they do not account for (per half, so a right thumb lifting while a left
  one steers still ends its drift) so a swallowed `pointerup` cannot strand one. `pointerup` /
  `pointercancel` are heard on the window as well as the layer, and ONLY the wheel pointer is captured:
  the jump hand is not, because `setPointerCapture` is one of the things WebKit hands back as a
  `pointercancel` and an `inset: 0` layer had nowhere to lose that finger to anyway. `#race-root` carries
  `touch-action: manipulation` (the layer itself `none`), so a double tap is a jump or a pick, never an
  iOS zoom. None of this is verified on a real iPhone: `race/smoke/touch-check.mjs` walks the logic only.
- The how-to card (`race/menu.js`) leads with the THUMB rows on a touchable page and keeps the keys and
  the pad below them, and `race/hud.js` drops the `E` badge from the item hint there: an empty slot reads
  `no item yet`, a full one `<name> tap to use`.
- Space (pad B) is the jump: `read().jump` is true for exactly the frame of a fresh press, never on hold,
  and `kart.stepJump` turns it into 1.1 m of real height (`state.h`, so the pop box goes up with it).
  A press within 4 m of a ramp lip, or inside 0.12 s of one firing, boosts that launch by 1.3 and hands
  out 0.5 s of speed once per ramp, and emits `{ type:'jump', big:true }`. Space is also the cards' "next",
  so `run.js start()` calls `input.flush()`. `?jump=<ms>` fires one synthetic press: a screenshot aid.
- `bridge.isHosted` is a BOOLEAN export, not a function. `raceBoot.js` reads it to pick standalone dev mode
  (synthesised `init`, every would-be host message logged as `[race->host]`).
- `run.js` registers the `pause` and `payout-result` bridge handlers itself; `raceBoot.js` owns `init`,
  `manifest`, `favorites`, `ping`, `exit-request`, `fullscreen`.
- Only `video` pops go to the host (`fire-payload {kind:'video', strength 0..100, durationMult}`);
  `payloadFx` never sends it. There is no `audio` bubble kind. Since 2026-09-06 no video bubble spawns
  and the host refuses the message, so this path is dark at both ends.
- The first Tea Garden gate sits at d = 9 (mid gate chunk), so it crosses ~0.4 s after start: that crossing
  shows the opening MARQUEE and never banks. Later Tea Garden gates bank only when the road score is > 0.
- Magnet (the wand) is a known gap: the field's hit box is fixed at `POP_HIT_*` and exposes no widen API,
  so `magnet` only flips a flag in the run brain. Wiring it needs a `field.setReach(mult)` in a later PR.
- The mirror item flips the `<canvas>` only (`scaleX(-1)`); screen shake transforms the root, so the two
  never fight over one `style.transform`.
- `again` on the end screen rebuilds the world in place (spine, tunnel, fx, dresser, field, kart, score,
  items) with a fresh seed; renderer, HUD, input, payloadFx and shake persist for the page's life.
- Extra `run-ended` fields: `nearMisses`, `personalBest`. `exit` is followed by `exit-done` once torn down.
- Boot and reduced motion: `raceBoot.js` calls `detectMode({ reducedIs3d: true })`, so `prefers-reduced-motion: reduce`
  boots the 3D race and only turns motion down through `settings.reducedMotion`; a boot error is reserved for a real
  hard wall (no WebGL, no import maps).

### `race/cocktail.js` (pass three, THE MIX)
```js
import { createCocktail, CATEGORIES, RECIPES } from './cocktail.js';
const mix = createCocktail({ now });   // pure state, no DOM, no scoring
mix.add(kindId, { durationMult }) -> { action, category, kindId, charges, depth, recipe, prevKindId, reason }
mix.tick(dt) -> events        // pulse (burst | roll), decay, expire, recipeEnd
mix.state() -> { live: [{ category, kindId, glyph, label, charges, max, depth, sec, total, frac }], recipe, video, load }
```
Replaces pass two's "one screen effect at a time". Every effect kind carries a `category` in
`bubbleKinds.js`; one live effect per category with its own rule for a re-pop: `strobe` (flash)
stacks to 5 charges that decay one at a time, `tint` (pink) extends and deepens to 2, `overlay`
(spiral / braindrain) replaces, `corruption` (glitch) refreshes, `cards` (subliminal / gif rain)
add to 4, `freeze` and `video` are solo (`video` holds everything else). `action: 'held'` means the
pop scores as a treat. Live category sets match `RECIPES` (first row whose `needs` are all live);
run.js maps a served recipe to `score.boostMult` (never below x1), a toast, a mood poke and, for
`marquee` rows, the banner. Durations for effects live in `CATEGORIES`, not run.js.

### `race/gltf.js` (pass four, the Blender packs)

`loadPack`, `toInstanceGeometry`, `setFace`/`FACES`, `preparePixel`, `disposePack` for `race/assets/emi.glb` + `props.glb`; the node names, clip names and the linear colour rule live in that file header, `byName` returns null for anything missing and the voxel kit stays the fallback.

### `race/propPack.js` (pass four, the Blender packs)

```js
export const PROPS_URL;                       // '/dtrh/race/assets/props.glb'
export function propPack(opts) -> Promise<Pack|null>          // one shared, never-rejecting request
export function packGeo(pack, name, off, scaleX) -> BufferGeometry|null
export function geoSize(geo) -> { w, h, d, cy }
```
`rooms.js` and `roomProps.js` dress from this one handle, so the pack is fetched and parsed once. A
null from either function always means the same thing: keep the voxel fallback.

`roomProps.js` names a pack node per `ROOM_PROPS` slot (`node`) and, for the four screen rooms, the
fixture that mounts around the quad (`frame` + `frameY`, the authored opening centre). Wall props
are centred on their mounting plate and keep it by merging it in; shoulder props and extras move to
`PROP_X` and are mirrored on x because `shoulderMatrix` is left handed like `roadMatrix`.

### `race/menu.js` + `race/intro.js` (pass five, the front door)
```js
createMenu({ root, renderer, pixel, audio, settings, log }) -> { show(), hide(), onPick(cb), options, hideVerb(id), stage: { update(dt), render(), dispose(), live }, dispose() }
createIntro({ stage, hud, audio, reducedMotion, log }) -> { play(): Promise, skip(), update(dt), render(), dispose() }
cameraWhip(sec) / resultsCamera({ tier, reducedMotion }) / preRollCamera() -> fn(camera, dt, w, camOut), `false` when done
resultTier(total, best, personalBest) -> 0..4 (the face index)
```
- Boot order: splash (a 1 s title flash) -> menu (the resting state) -> `race` -> intro on the menu stage -> run under the
  camera whip. `?autostart=1` skips menu and intro (the headless checks depend on it), `?intro=0` skips the intro only,
  `?scene=intro` boots straight into the intro. `surface` from the menu sends the same `exit` + `exit-done` the End screen does.
- `surface` UNHOSTED: nothing takes the window away in a plain browser, so raceBoot picks a destination once at load.
  `?back=<path>` wins when it resolves same origin, then a same-origin `document.referrer` (`history.back()`), and with
  neither the boot calls `menu.hideVerb('surface')` so the verb is not on the list at all. Cross-origin values are ignored:
  the switch is never an open redirect. `?panel=howto` (or `options`) opens the menu on that panel, a screenshot aid.
- TOUCH: every keyboard path has a target under it. The key card carries a `back` row and a tap off the panel closes it;
  each value row wears real `‹` / `›` buttons around the number, so music and sfx come DOWN by touch as well as up (a
  whole-row press still steps up, the way the pad does). `.rm-col` / `.rc-col` scroll inside themselves and pad off
  `env(safe-area-inset-*)`; `@media (max-height: 480px)` packs the seven verbs into a 390 px landscape phone and
  `@media (pointer: coarse)` puts every verb, value arrow and roster arrow on a 44 px target.
- The stage is a second `THREE.Scene` drawn by the race's renderer + pixelizer through `race.setStage(s)`: while set,
  `frame()` calls `s.update(dt)` + `s.render()` and the world is not drawn. `setStage(null)` retextures the world for any
  pixel block the menu changed. No second canvas.
- Camera overrides run in `frame()` after `step()`, so they work while the run is stopped (results) and while it drives
  (the whip). `start()` clears the override; the boot installs the whip after `start()`.
- `w.kart.emiModel()`, `w.kart.emiReady(cb)`, `w.kart.setFace(i)` are the kart's contract for the run's EMI; every call
  is guarded for null (the stage carries its own clone with its own glass material, so the two faces never fight).
- The stage camera follows the **Camera aspect rule** above (base 42 at 16:9, its own 86 cap). Two columns
  park her right of the menu with a horizontal `setViewOffset`, exactly as they always did.
- **THE BAND**, one column only (`(max-width: 720px)`, where menu.css hides `.rm-stage`): there is no right
  half to park her in on a phone, so the column hands her the middle of the screen instead. The title rides
  the top, the verbs ride the bottom (`.rm-list { margin-top: auto }`), and `measureBand()` gives the strip
  left between them to `stage.setBand({ top, bottom })` in css pixels; an open panel gives the room under it
  instead, and null (no menu on screen, a strip under `BAND_MIN` 150, or a column scrolling past the bottom)
  keeps the centred framing the stage always had. `applyView()` spends ONE `setViewOffset` on it, reading a
  window of a `zoom` times bigger virtual frame, so the same call shifts AND magnifies: the silhouette (her
  antenna down to the near foot of the saucer, both projected through the live camera) fills `BAND_FILL`
  0.62 of the band with her eye line `BAND_EYE` 0.58 down it, the house camera law, clamped so the antenna
  never crosses the title and the saucer never crosses the verbs. `setMode(m)` with anything but `'menu'`
  drops the band, so race/intro.js drives the camera on a clean frame. The menu sets `--rm-scrim-t` /
  `--rm-scrim-b` to the band's complement and `is-band` on `.rm-root`: menu.css draws those two scrims
  instead of its one sheet, so no dark plate and no verb ever lies over her.
- Options persist under the single localStorage key `race.options` (`pixel, music, sfx, motion, seed, seedValue`), not in
  `engine/settings.js`. Precedence for the block: `?pixel` > `race.options.pixel` > host `settings.pixel` > `PIXEL_DEFAULT`.
  The seed rule (daily / random / custom) sets `settings.seedLock`, which `again` honours; a change in the menu calls `reseed`.
- Music / sfx sliders store their values and call `audio.setLevels({ music, sfx })` when audio.js grows one; until then
  the rows are dimmed with a note. Reduced motion from the menu drives the stage and the intro at once, the run on the next launch.
- props.glb (`podium`, `kart_cup`, `kart_saucer`, `floor_tile`, `gantry`) dresses the stage when it resolves; lathe
  placeholders otherwise. `emi.glb`'s `EMI_glass` carries the atlas as `emissiveTexture` only, so the stage sets the face
  on `emissiveMap` (and `map` when present); gltf.js `setFace` shifts whichever of the two the material carries.

### `race/cards.js` (the introduction cards, PR c8)
```js
createCards({ root, audio, reducedMotion, log, start }) -> { show(): Promise<void>, dispose(), index }
cardsSeen() -> bool | markCardsSeen() -> void
export const CARDS_KEY = 'race.cards', CARDS;
```
- Four cards read one at a time on a `.rc-root` DOM layer at z26: above the menu (z25), below the boot
  splash (z30). Same two-column layout as `.rm-root`, so EMI keeps the right half of the frame while
  they read. The boot hides the menu around them and calls `menu.refreshView()`, which parks the stage
  at the column framing that `hide()` would otherwise reset to 0.5.
- Enter / space / right / d / a pointer press / pad A advances, left / a / pad B goes back (nothing on
  card 1), esc ends it. The last card's advance ends it too. `show()` resolves either way.
- Gate: its OWN localStorage key `race.cards` = `'1'`, written by `show()` the moment they go up, so
  read through, escaped and abandoned all count. `race.options` keeps the shape menu.js documents; the
  cards never widen it. Every read and write is wrapped, a storage that refuses shows them again.
- The boot shows them once after the splash and again from the menu's `the story` verb. `?cards=1`
  forces them, `?cards=0` skips them, `?card=N` opens on card N (screenshot aid). `?autostart=1` and
  `?scene=intro` never reach them.

### `race/chart.js` (track charts, PR c1)
```js
normalizeChart(json) -> chart | demoChart({ seed, durationSec }) -> chart | createScheduler(chart, { leadSec }) -> sched
export const CHART_VERSION, STRUCTURE_WORDS, DROP_WORDS, EVENT_KINDS, ACT_KINDS, ACT_ROOM;
```
```js
// race/track.js (PR c2): the loaded track's clock, energy and acts. No three, no DOM.
createTrackState({ leadSec }) -> st
st.setTrack(chart | null) -> track | null      // track = { chart, sched, t, playing, name, durationSec }
st.replace(chart)                              // the words pass landing on a partial chart, clock kept
st.clock(t, playing)                           // the host's 250 ms tick: the only thing that SETS the second
st.step(dt) -> { t, intensity, act, actChanged, ended } | null   // one frame of real time between ticks
st.due(kartD, kartSpeed) / st.taken(id) / st.stats() / st.summary() / st.end()
st.track / st.act / st.intensity / st.triggerKinds / st.ended
```
```js
// race/cues.js (PR c2): one chart event -> the cue run.js spends on the world. Pure, node-checkable.
cueFor(event, { energy, act, room, intensity, rng, triggerKinds }) -> cue | null
```
Pure data: no three, no DOM, no clock, so it runs under node (`node race/smoke/chart-check.mjs`,
`node race/smoke/track-run-check.mjs`). The chart
is the analysed hypno file (energy bins, acts, spoken events); the scheduler hands run.js each event `leadSec`
before its second (2.5 s, floor 1.2) at `d = kartD + speed * dueIn`, so the pop lands on the spoken word
whatever the throttle did, and anything already spoken is dropped. Full shape and protocol in `CHART.md`.

## Host protocol (bridge.js, Protocol v1)

Page -> host (`bridge.send(type, data)`): `ready` (announceReady), `heartbeat`, `pong`, `sfx {name, scale}`,
`fire-payload {kind:'video'|'audio', strength, durationMult}`, `run-started {seed}`,
`run-ended {score, banked, durationSec, bestCombo, popped, treats, effects, laps, seed}`,
`boot-error {message}`, `report-bug {text}`, `fullscreen-set {on}`, `exit`, `exit-done`,
and with a track loaded (PR c2): `track-play {name}` (the run started, start the audio),
`track-pause {on}` (the Brake, a host pause, a video pop), `track-stop` (run end or exit).
`track-pick` and `track-cancel` come with the menu in PR c7.

Host -> page: `init {protocol, settings:{masterVolume, reducedMotion}, modId, modContent}`,
`manifest {images:[{name,url}], videos, skipped, truncated}`, `favorites {names}`,
`payout-result {baseXp, skillMult, finalXp, sparksEarned, previousBest, dryRun}`, `pause {on}`,
`ping`, `exit-request`, and the track messages (PR c2): `track-progress {stage, pct, name}`,
`track-chart {chart, partial}`, `track-clock {t, playing, durationSec}`, `track-ended`,
`track-error {message}`. Standalone, `?chart=demo&dur=N` / `?chart=<url>` load a chart and
`?audio=<url>` makes an `<audio>` element the clock (wall time without it).

### The media surface (web only)

The desktop host enumerates the user's preset and posts a `manifest`. A browser has no library to
enumerate, so the player hands it one and the same `manifest` frame carries it. Extra frames:

Page -> host: `set-setting {key, value}` with `key` `media.pickLocal` (`'gallery' | 'zip' | 'folder'`)
or `media.clearLocal`. Posted from inside the click's own turn, because a file picker opens only
while the tap's activation is alive.

Host -> page: `setting {key, value}` (the echo; an action echoes `value: null`, and only the echo
takes a button's pending paint back off), `local-media {images, videos, skipped, active}` after every
ingest, a cancelled picker and a clear, plus a fresh `manifest` carrying the whole pile.

Capability keys on `init.settings`, all absent (and so unchanged) on the desktop:

| Key | Meaning |
| --- | --- |
| `mediaControls: true` | build the menu's `your media` panel. Strictly `true` or nothing renders |
| `localMedia {images, videos, skipped, active, folder}` | the pile at boot; `folder:false` hides the folder button (iOS) |
| `trackPick: false` | this host has no file dialog: take `load a track` off the menu, and leave the `?chart=` road open |
| `canSurface: false` | this host cannot take the window away: take `surface` off |
| `hostSfx: false` | this host cannot play `Resources/sounds/chaos`: play the vendored cue in page |

The menu's `your media` panel goes: heading, the count line, the pickers, the two promises, then an
empty `.rm-media-more` node (`menu.mediaSlot`) and `back`. The online-feed group builds into that
node, so it lands under the pickers and `back` stays the last row a thumb or an arrow reaches.

The browser host lives in the site repo at `scripts/race-web-ext/`; its `RACE-MEDIA-CONTRACT.md` is
the other half of this table and owns the source-registry seam remote media plugs into.

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
