# Breakout (working title) - build spec v1

A niche-themed Breakout, a Back Room cabinet. Design by CodeBambi; master design doc lives in
Claude Docs ("Breakout - Master Design Doc"). This file is the build contract for the agents.

## The thesis in three lines
- The game is an eye-tracking guarantee: the player's eyes are on the ball, so payloads (GIFs, subliminal words, spirals) fire where the ball is.
- One variable, `saturation` (0..1), drives everything: colour, particles, trail, glow, music filter, sub cadence, paddle width, ball speed.
- Two states. COLOUR (saturation > 0, juiced) and GREY (the "Old Self"). Lose the ball -> RELAPSE: world snaps grey, ball becomes the grey OLD SELF ghost. Hit N bricks in grey -> BREAKOUT: freeze frame, ball shatters, reveals the coloured spiral ball, world snaps back to the saturation it had. You never lose. No lives, no game over.

## Folder
`Resources/web/backroom/stations/breakout/`
- `station.js`      CONTRACT section 7 module: `export async function mount(ctx)` -> `{ open, close, suspend, destroy }`. Builds a fullscreen canvas in `ctx.root`, runs the game, tears down on close. `export const roomStage = false` (2D canvas, NOT the 3D stage).
- `game.js`         pure-ish sim: paddle, ball(s), bricks, walls, colliders (gif circles), spiral wells, saturation, state machine. `createGame({ w, h, rng, audio, onEvent })`, `step(dt, input)`, `snapshot()`.
- `render.js`       canvas 2D renderer, everything visual keyed on saturation and state. Trail, glow, particles, screen shake, freeze, crack overlay, flash of words.
- `payloads.js`     the niche layer: GIF sources (decoded via `../../room/gif-decode.js` `decodedSource`, clip via `../../room/clip-source.js` when `isClip(url)`, else an <img> still), subliminal words scheduler, spiral drawing (a simple procedural spiral is fine for v1).
- `audio.js`        WebAudio: beat clock, music bed with low-pass driven by saturation, quantised hit sounds, relapse thud, breakout shatter. See interface below.
- `dev.html`        standalone harness like `stations/slot/dev.html`: mock ctx with `media()` from `../../../../avatar0_emotes/*.gif`, `sp()`, `fx()` logged, `lex(key, fallback) => fallback`, `gates: {subliminal: true}`, `variant: null`, `hostBack: null`. Query: `?sat=0.6&still&bpm=100`. Serve with `node ConditioningControlPanel/Resources/web/backroom/shared/sound/serve.mjs` then open `http://127.0.0.1:8940/backroom/stations/breakout/dev.html`.
- `station.css`     minimal chrome: a Back button when no `ctx.hostBack`, the SP readout, a small "dev toggles" details panel (toggle each juice rung on/off for tuning, like the talk's demo).

All modules are plain ES modules, no bundler, no three.js needed (2D canvas only). Import shared code by relative path only.

## Playfield
- Logical size 480 x 720 portrait-ish (fits desktop window and phone); the renderer letterboxes into whatever the canvas is. Input: mouse x / touch x / pointer; keyboard arrows as fallback.
- Paddle at the bottom, width = base 90 px * (1 + 0.6 * saturation). Ball radius 8. Bricks: rows of 10 across, 6 rows, each 42 x 18 px, 4 px gaps, top margin 80 px. When a wall (all bricks) is cleared: brief celebration, new wall descends (tween-in from the talk), `onEvent('wall')`.
- Ball speed = the beat: `speed = fieldHeight / secondsPerBeat` scaled so a bottom-to-top crossing is ONE beat at saturation 0 and 1.5 beats at saturation 1 (slower as it climbs, toward breathing pace). Ball never below a floor speed.
- Ball loses at bottom edge -> RELAPSE (see states). Paddle hit angle depends on where on the paddle it hits (classic).
- Physics: fixed timestep 1/120 accumulator, swept circle-vs-AABB against bricks and walls, circle-vs-circle against gif colliders. Multiball: `balls[]`, split power-up spawns a second coloured ball; on a split, spawn the GREY "OLD SELF" ghost that drifts off the top of the screen with the text OLD SELF inside it (cosmetic only, no collision).

## Saturation
- `saturation` in [0,1], starts 0.05 on a fresh session. Climbs per brick (+0.012), per gif collider hit (+0.03), per spiral orbit completed (+0.05), per wall cleared (+0.1). Never decays in COLOUR. Capped at 1.
- The juice ladder, rungs unlocked by saturation thresholds (each rung is a boolean the renderer/audio read; `dev toggles` can force them):
  1. 0.00  grey, flat colour, hard cuts, no trail. Music low-passed hard (cutoff ~300 Hz).
  2. 0.10  colour begins (lerp grey -> palette by saturation), block tween-in on wall spawn
  3. 0.20  ball trail (length 30 * saturation), paddle stretch on hit
  4. 0.30  particles on brick shatter, ball glow
  5. 0.40  block jelly (scale bounce on neighbours), bouncy lines (walls wobble on hit)
  6. 0.50  screen shake on brick + freeze frame 40 ms on gif collider hits, music filter open
  7. 0.60  subliminal words start flashing near the ball (see payloads), paddle grows an EMI-style face that looks at the ball
  8. 0.70  spirals spawn as gravity wells
  9. 0.80  gif colliders bounce the ball and pulse on hit, screen colour glitch on hit
  10. 0.90 the CRACK: a hairline fracture overlay across the field, the ball behaviour changes (curves slightly toward the nearest brick), everything at max juice
- THE CRACK also fires `onEvent('crack')` once per session.

## The two states
- COLOUR: as above. On losing the ball: `onEvent('relapse')`. Store `savedSaturation = saturation`. Set state GREY. Screen: hard wipe to grey (100 ms), thud, music cutoff slams shut, all particles/trails die instantly, a big faint word RELAPSE crosses the screen once. The ball respawns on the paddle as the OLD SELF ghost: grey, slightly translucent, the text OLD SELF inside it in tiny caps, launches on click/tap or after 1.2 s.
- GREY: generic Breakout. No payloads at all, no trail, no particles, everything grey. Bricks still break. Count `greyBricks`. When `greyBricks >= N` (N = 12 for v1, tune later; a dev toggle): BREAKOUT.
- BREAKOUT: freeze frame 100 ms, ball SHATTERS (particles fly, grey shards), reveals the coloured spiral ball underneath, a shockwave ring expands from it, the world snaps (not lerps) back to `savedSaturation`, music filter opens with a whoosh, the word BREAKOUT stamps big at centre, `onEvent('breakout')`. Then COLOUR continues from where it left off; bricks remain where they are (the grey stretch counts).
- Losing the ball while already GREY: just respawn the ghost, greyBricks keeps counting (never punishing).

## Payloads (COLOUR only)
- Media comes from `ctx.media({ count: 8 })` -> `{ gifs:[{key,url,src}], words:[{key,text}] }`. Load lazily, cap 8 resident decoded sources, 192 px long edge, 12 fps (same caps as `shared/hypno/media.js`, but v1 may just use `decodedSource` directly). Any url that fails -> skip.
- GIF bricks: from rung 2 onward, some bricks are GIF bricks (a brick that shows a gif face). From rung 9, gif COLLIDERS float in the field: round, 56 px, drift slowly, the ball bounces off them (circle collision), they pulse and play on hit, fade after 3 hits. Spawn cadence: variable ratio, mean every 8 s at sat 0.8, every 5 s at sat 1.
- Subliminals: from rung 7, a word from `words` flashes for 2-3 frames (~50 ms) NEAR THE BALL (offset so it is not under the ball) in the house colours, cadence variable ratio mean 4 s at rung 7 down to 2 s at sat 1. Also on every brick hit at sat > 0.6 with probability 0.25. Honour `ctx.gates.subliminal === false` -> no words. `ctx.fx('fx.sub_single', { s0: key })` may be called at most once per 10 s so the desktop host can also play its own flash; do not depend on it.
- Spirals: from rung 8, a spiral well spawns (radius 70, pull radius 110) every ~12 s, for 6 s. When the ball enters the pull radius it is captured: it orbits the centre at its current speed for 1..2 turns, the spiral fades, the ball leaves on its tangent. The spiral itself rotates and is drawn procedurally (arms of a logarithmic spiral). At most one spiral live.

## Audio interface (`audio.js`), built by the audio agent, consumed by game/render
```js
export function createAudio({ bpm = 96, master = 0.8 } = {})
// returns:
//   start()                    resume/create the AudioContext (call from a user gesture); idempotent
//   stop()                     suspend; destroy() closes
//   now()                      audio clock seconds
//   beat.spb                   seconds per beat
//   beat.phase(now)            0..1 within the current beat
//   beat.nextSixteenth(now)    audio time of the next sixteenth-note boundary
//   setSaturation(s)           0..1 -> low-pass cutoff (300 Hz .. 12 kHz), extra layers fade in above 0.4 and 0.7
//   setState('colour'|'grey')  grey = filter slammed shut + a dry, dull hit palette
//   hit(kind, { combo = 0, x = 0.5 })  kind: 'wall' | 'paddle' | 'brick' | 'gif' | 'spiral'; scheduled on the next sixteenth; pitch climbs a pentatonic scale with combo (reset by the caller on paddle hit); x pans
//   relapse()                  thud + filter slam (immediate, not quantised)
//   breakout()                 shatter + riser + whoosh, filter opens over 400 ms
//   crack()                    one deep low crack
//   wallCleared()              short arpeggio
//   split()                    the multiball split sound
```
- Everything synthesised with oscillators/noise, no sample files, so the dev harness works offline. Music bed: a soft 4-bar loop (pad + pulse on the beat) in C pentatonic (ROOT 523.25 Hz like `shared/sound/kit.js`), running from `start()`. Keep CPU tiny.
- Three gain buses like the room: `bed`, `sfx`, `sub`. Master 0.8.

## Rewards (v1)
- SP: 1 SP per wall cleared, capped at 20 SP per sit-down. v1: just `onEvent('wall')` and a local counter shown in the HUD ("+1 SP"), no server call. Wiring to the server comes later.
- HUD: bricks broken, walls cleared, a thin saturation bar along the top edge (NOT a number), SP earned this sit-down.

## station.js contract details (from CONTRACT section 7)
- `mount(ctx)` returns `{ open, close, suspend, destroy }`. `open()` creates the canvas in `ctx.root`, requests media, starts audio on the first pointer event. `close()` stops the loop, resolves after teardown. `suspend(on)` pauses/resumes (room hides the station). `destroy()` removes everything.
- ctx fields available: `root, request, fx, media({count}), sp, onSp, lex, variant, hostBack, gates, fxTunnel, fxRelease, bridge`. Use `ctx.hostBack` when present, else draw own Back button. Never touch WebGL.
- Reduced motion: if `matchMedia('(prefers-reduced-motion: reduce)')` or `?still` -> no screen shake, no glitch, gifs on frame one.

## Rules
- Plain modern JS, no TypeScript, no bundler. Files under 500 lines each where possible.
- No em-dashes in strings or comments.
- Speed over polish: get it playable, then tune. Tests: only a small `game.test.js` for the state machine (relapse/breakout/saturation) runnable with `node --test`.
