# Daily Daze wheel station

One free spin a day (CONTRACT.md sections 2-7, owner decisions of 2026-09-13: wheel option A). Entry
`station.js`, loaded by the room from `stations.json`. The model is the owner-approved wheel
(`blender-scripting/wheel/APPROVED.md`), copied read-only by `Scripts/sync-backroom-wheel-assets.ps1`.

| File | What |
|---|---|
| `station.js` | `mount(ctx)` -> `{open, close, suspend, destroy}`. DOM, readouts, the spin flow, fx, the day's countdown. |
| `wheel.js` | Pure: slice layout from `state.slices`, `sliceAt`, the seeded landing inside `result.sliceIndex`, the deceleration plan, `countdown`, `readResult`, Law I `shownSp`. |
| `feel.js` | THE HOUSE BOOK, pure: tiers, the recipe, the 6 Hz tick gate and chime ladder, glance, token counts. No fx ids: the fullscreen moment is the kit's. |
| `hypno.js` | Hypno v3, pure (CONTRACT 10.13.F): the long last turn's time warp, the stage dim, the quiet room curve, the taffy shear and ghosts, the hub angle, the moire rings, the dress for intensity and gates. |
| `scene.js` | three.js close-up. One WebGL context per `open`, freed in `close`. Runtime slices, drag, coast, landing, bulbs, star, screens, and the v3 page effects (Loom hub disc, long last turn, quiet room, moire rim, taffy). |
| `emi.js` | EMI on the perch: face atlas, spiral eyes while turning, win/jackpot/sleepy poses. |
| `readout.js` | The one SP readout: `ctx.spReadout` when the room has it, else the room chip, else the station's own chip. |
| `bank.js` / `sound.js` | THE BANK tokens (slice -> readout) and the cues (the race's clips, synth fallback, no new files). |
| `nodes.js` | The glb node names the page drives. |
| `mock-server.js`, `dev.html` | Standalone harness on the binding API with the kit's mock host (`shared/hypno/tests/mock-host.js`). Not shipped behaviour. |
| `tests/host-screen.js` | Harness only: draws the host's fullscreen primitives from what the mock host recorded, so screenshots show a whole moment. |

Shared code is imported only from stable paths that exist on every branch: `arcademy/shell/counterfx.js`
(THE THUD, THE SHIVER, THE BANK timings) and `dtrh/shared/audioSrc.js` (clip lookup). The slot's `feel.js`,
`bank.js` and `sound.js` are the pattern, not imports: they live on the slot branches and speak paylines.

## Server API (binding, CCP-Server `backroom-wheel-routes.js`)

- `GET state` -> `{ ok, sp, open, day, spun, result|null, snoozeCarry, nextResetAt, jackpot:{amount, odds, wonToday, eligible, mustHit},
  slices:[{id, label, pay, width, odds}], floorMs }`. `width` is the drawn width in degrees (the picture); `odds` is
  `"1 in N"` or `"never"` (the ledger, printed in the Odds panel). The jackpot slice's `pay` is the day's pot.
  Slices carry no kind: `jackpot` and `snooze` are known by id.
- `POST spin {idem}` -> `{ ok, sp, result:{day, sliceId, sliceIndex, pay, snoozeCarryPaid, jackpot, jackpotFallback, snoozed,
  total, capped}, jackpot, snoozeCarry, nextResetAt }`. `total` is pay + carry before the SP cap; the page banks
  `sp - spBefore`, so a capped pay never shows more than landed.
- Refusals: `already_spun` (+ `result, sp, jackpot, snoozeCarry, nextResetAt`: the page lands on the stored result with no
  second bank), `busy` and `too_fast` as HTTP 200 (retried with the same idem, at most 3 times), `bad_request`,
  `closed` as 403 (the wheel winds down, a card says closed). A host `timeout` retries with the same idem.
- Host Ops row: `["wheel"] = { ("GET","state"), ("POST","spin") }`.

## The spin

- **Law VIII.** A drag on the rim follows the pointer at once; a release past 0.1 rad flings it (speed clamped to
  0.006-0.03 rad/ms, direction kept). The Spin button, Space or Enter start a clockwise coast on the same frame. Either
  way EMI glances and the button rings before the server answers.
- **The landing is the server's.** The reply retargets the coast onto `landingAngle(layout, sliceIndex, day)`: the
  middle 60% of the drawn slice, seeded by the day (Law V), so a reopen shows the same landing. A quartic ease-out
  whose start speed matches the coast, 3.2-5.6 s, at least one turn. Drag strength changes the picture, never the slice.
- **Nothing tells the result early.** The screens, the status and the jackpot chip adopt the reply on the landing
  frame; the SP readout owes the pay until THE BANK lands it (Law I).
- **Reopen after spinning** shows the stored landing, the result as text, and a countdown to `nextResetAt` on the
  status line, the button and `status_screen`. At zero the page refetches state (then every 30 s until the server's
  day has turned). The countdown reads the client clock for display only; the server owns the day.

## Feel (cited from house-book.md)

- **THE THUD** when the pointer settles: the landed slice flashes 2.2 -> 1 over 340 ms, the pointer knocks, the cue on
  the same frame (a muted, low-passed thud for Snooze).
- **THE CHIME LADDER** on peg crossings: a crossing plays (tick cue, pointer kick, highlight moves) only when the last
  played one is at least 167 ms old, so nothing changes faster than 6 Hz. Once crossings come slower than that the
  tick climbs a semitone per play, capped at 7.
- **THE BANK**: 3-7 tokens by tier (4 on Calm) from the face under the pointer to the SP readout, ticks per landing,
  mini-thud on the last, `+N SP` text.
- **THE MASCOT GLANCE**: press -> spirals (the eyes turn while the wheel does), landing -> hearts / jackpot / sleepy;
  never the same pose twice.
- **THE BREATH**: the jackpot star's glow only (3.2 s), held while the wheel turns or celebrates. EMI does not sway at rest.
- **Law IX**: 1-3 SP a chime; 5-20 two notes, EMI's hop; 40 and 100 THE THUD chimes; the pot THE REVEAL (EMI
  over-rotates, `status_screen` counts the pot up over 620 ms, sparks, gold bulbs). The old size-based fx table
  (`FX_BY_TIER`, `fxFor`, `usesGifs`) is gone: the fullscreen moment is Hypno v3's, below.
- **Snooze** (Brake 6): THE SHIVER, a muted thud and a yawn, EMI droops and dozes (`z Z z`), `+2 tomorrow` as text.
- **Law VI**: reduced motion or Calm take the settled state: no coast, the wheel is on its landing once the server
  answers, no tokens (the readout lights), no shiver, no reveal travel; the cues still play. Back and suspend skip
  every ceremony; `close()` settles inside the room's 420 ms.
- **Brake 9**: every value is also text (status line, jackpot chip with odds, Odds panel, screens, `+N SP`).

## Hypno v3 (CONTRACT 10.13, the v3 mockup)

Every fullscreen effect goes through the kit (`shared/hypno/`): `createMoments(ctx, {station: 'wheel'})`. The page
never names a section 4 id.

| Moment | When | Host (kit table, Normal args) | Page |
|---|---|---|---|
| `wheel.turn` | once, the frame the long last turn starts; then `moments.tunnel(wheelTurnLevel(dim))` every frame | `fx-tunnel` at dim x 0.85 | stage edges, `s l o w l y` |
| `wheel.land.quiet` | Snooze, 1, 2, 3 | none | quiet room |
| `wheel.land.flash` | 5, 8, 12 | `fx.wash` slice colour, 0.55 | quiet room |
| `wheel.land.gif` | 20, 40, 100 (Dazed and the jackpot fallback) | `fx.wash` slice colour 0.9, `fx.gif_from` from the slice, 3400 ms | quiet room |
| `wheel.land.jackpot` | the pot | `fx.loom_spiral` screen 4200 ms alpha 0.9, `fx.gif_from` scale 0.46 4600 ms, `fx.wash` #e8c27a 1 | quiet room, THE REVEAL |

- The size is the kit's `wheelSize(result)`. The landing moment fires in `land()` on the frame the pointer settles
  (Law I), never on the reply; a reopen after spinning and an `already_spun` landing fire nothing.
- `from` is `scene.project('landed')` as a 60 x 44 CSS px box. The picture is `deck.pickKey('<day>|<sliceId>|<index>')`
  from a 4-GIF deal (`createDeck(ctx, {count: 4})`), so one result always names the same key.
- **Loom hub**: a CircleGeometry disc on `wheel_rotor`, radius from `hub_lip` (0.166) at its top face, a 256 px
  CanvasTexture painted by `kit.paint('hub', {now, angle})`. The angle accumulates, as the mockup's `hubRot`:
  `angle += (|rotor speed| x 0.9 + 0.35 x timeScale) x dt` (`hypno.stepHub`), so it turns clockwise whichever way the
  wheel is flung (an angle read off `-rotation.z` ran backward on an anticlockwise drag and read outward). The disc
  holds still against the rotor, so that angle is its whole turn on screen. At rest it repaints at 30 Hz.
  `wheel-check.mjs` measures it on screenshots at rest, turning clockwise, and in the long last turn after a clockwise
  and an anticlockwise drag: clockwise, arms leading at the rim, so it reads inward (law 3). Spiral gate off: a brass star painted once. The neon tube only stays for a model with neither node.
- **The long last turn**: the landing plan's own clock is read at `lerp(0.32, 1, speed / 1.6)` while the planned
  speed is under 1.6 rad/s (0.6 floor under Calm). Same path, same landing angle, same slice (hypno.test). The spin
  runs about 6-7 s instead of 4. The edges (`.wheel-edges`, a radial gradient centred on the rotor) take opacity
  0.65 x dim x k; no backdrop-filter. They stay with brainDrain off; the tunnel does not.
- **Quiet room**: slice colours mix toward #2a2238 by `min(0.78, clamp(since x 3) x k x 1.6)`, colour flows back from
  1.1 s by angular distance over 0.8 s, labels of grey slices at 0.75; the landed slice keeps its colour with a mint
  band outline.
- **Moire rim** (Full only): two `LineSegments` rings of 60 lines between r 0.745 and 0.79, at the rotor angle and at
  0.9 x angle + 0.3.
- **Taffy slices** (Full only): a vertex twist on the slice materials (`onBeforeCompile`, one uniform), labels and pegs
  moved to match, shear `min(speed x 0.11, 1.9) x k` easing at 2.5/s, bending against the motion. Four smear ghosts (one
  merged vertex-coloured geometry, 4 draw calls) at alpha 0.16 x k where the wheel was 2 to 5 frames ago, drawn over
  the slices so the smear shows (the mockup drew them under opaque slices, where they could not be seen).
- **Dress**: `hypno.dressOf({intensity, reduced, gates})` at open and on every `ctx.onSettings` frame (live): the
  frame re-reads `ctx.reduced` and `ctx.intensity`, so taffy, ghosts, moire, the hub, the edges, the quiet room and
  the landing moment's k follow a MotionLevel or intensity change at once. Reduced motion and Calm keep the base
  station's settled landing (no travel), so the 0.6 floor is there for a travel that Calm never starts; the quiet room
  runs at k 0.5, the Loom hub holds still (owner to confirm for Calm intensity; the mockup's OS reduced motion still
  turned it at half strength). A live switch hands the scene the travel flag too (`scene.setReduced`): the next spin,
  landing and sink take the new state, a landing already turning finishes its path, the token bank reads it per run.
- A landing with no slice to point at (`resultIndex < 0`, the wheel winds down) plays no moment (Law I).
- **Lifecycle**: `suspend(true)` and `close()` call `moments.cancel()` (tunnel 0, holds released) and dispose the kit
  and the deck; resuming re-deals and the hub paints again on the next frame.

## What the page needs from the room

- The import map for `three` and `three/addons/` (GLTFLoader imports `three` bare).
- `ctx.request`, `ctx.fx` (with `args`, the promise carrying `.token`), `ctx.fxRelease`, `ctx.fxTunnel`, `ctx.media({count})`,
  `ctx.gates`, `ctx.onSettings`, `ctx.sp`, `ctx.onSp`, `ctx.lex`, `ctx.standUp`, `ctx.reduced`, `ctx.intensity`. A missing
  hypno member is feature-detected by the kit and its effect skipped.
- `ctx.hostBack === true`: the station hides its own Back buttons; Escape still stands up.
- `ctx.spReadout` (optional): used for the SP chip when present. Without it the station writes the room's
  `#br-sp-value` directly (an observer puts a room repaint back), or its own chip standalone.
- Lexicon: `br_wheel_*` keys with English fallbacks in the page. New in v3, for the integration pass into `en.json`:
  `br_wheel_slowly` ("s l o w l y"). New with C2 must-hit-by (10.16.E): `br_wheel_must_hit` ("MUST HIT") and
  `br_wheel_must_hit_room` ("The pot has to fall today").

## C2 must-hit-by (CONTRACT 10.16.E)

`state.jackpot.mustHit` is a ROOM fact: the pot is at the must-hit line (`mustHitBy === cap === 1,000`) and nobody
has taken it today, whatever THIS account's age. `eligible` still says whether this account can win it.

- `readout.js` `jackpotChip(jackpot, t, fmt)` is the whole rule, pure: while `mustHit` is true the chip prints
  `br_wheel_must_hit` ("MUST HIT") **in place of the odds**, and the amount is still shown (Law I). Only a real
  boolean `true` turns it on, so a server that has not shipped the field reads as false.
- `.wheel-jackpot` takes `is-must-hit`: the gold the jackpot slice already has, and nothing else. **No new fx, no
  new sound, no new colour** (10.16.E), and the v3 hypno moments are untouched.
- The Odds panel adds `br_wheel_must_hit_room` ("The pot has to fall today"), after the young-account and
  pot-taken notes, so a young account still reads why the star is shut to it.
- `mock-server.js` carries `TABLE_V3`'s jackpot (`start 250, perDay 25, cap 1000, mustHitBy 1000`), answers
  `mustHit`, forces the draw for an eligible spinner on the must-hit day (spending the same rng call, so a seeded
  re-draw stays aligned) and never for a young one. `server.mustHit()` and `?musthit` park the pot on the line.
- The room's own MUST HIT (the wheel fixture's screen and the bell's standing line) is `room/main.js`, off the
  bell's `state` body, not this station.

## Checks

```
node --test ConditioningControlPanel/Resources/web/backroom/stations/wheel/tests/*.test.mjs   (the glob: a directory argument fails on Node 24)
node ConditioningControlPanel/Resources/web/backroom/stations/wheel/tests/nodes-check.mjs [wheel.glb]
node ConditioningControlPanel/Resources/web/backroom/stations/wheel/tests/wheel-check.mjs [evidenceDir]   (headless Chrome, WHEEL_PORT 8897)
node ConditioningControlPanel/Resources/web/backroom/stations/wheel/tests/room-mount-check.mjs [evidenceDir]   (the real room, WHEEL_ROOM_PORT 8894)
```

Dev harness: serve `ConditioningControlPanel/Resources` as the web root and open
`/web/backroom/stations/wheel/dev.html` (`?sp=57&next=jackpot|snooze|deep&pot=12&musthit&young&taken&carry=2&spun&reduced&calm&full&gates=off&hook&day=<ISO>`).
`dev.station.debug()` shows the state, the readout, the feel log, the cue trace and the scene.
