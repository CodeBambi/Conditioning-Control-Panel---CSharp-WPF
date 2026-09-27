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
- `saturation` in [0,1], starts at 0 in GREY on every sit-down, with saved saturation 0.15 restored by the first breakout. Climbs per brick (+0.012), per gif collider hit (+0.03), per spiral orbit completed (+0.05), per wall cleared (+0.1). Never decays in COLOUR. Capped at 1.
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
- GREY: generic Breakout. No payloads at all, no trail, no particles, everything grey. Bricks still break. Count `greyBricks`. When `greyBricks >= N`: BREAKOUT. Per sit-down, N follows 20, 15, 11, 8, 6, then 5 for every later relapse. Specials count +3, plain bricks +1. Wall clears do not reset this sequence. An explicit dev N override stays fixed.
- BREAKOUT: freeze frame 100 ms, ball SHATTERS (particles fly, grey shards), reveals the coloured spiral ball underneath, a shockwave ring expands from it, the world snaps (not lerps) back to `savedSaturation`, music filter opens with a whoosh, the word BREAKOUT stamps big at centre, `onEvent('breakout')`. A full-size OLD SELF shell floats upward for 3.6 seconds above the renderer-owned picture flash, without adding a gameplay ball. Reduced motion uses a stationary fading shell and a colour cut. Then COLOUR continues from where it left off; bricks remain where they are (the grey stretch counts).
- Losing the ball while already GREY: just respawn the ghost, greyBricks keeps counting (never punishing).

## Payloads (COLOUR only)
- Media comes from `ctx.media({ count: 8 })` -> `{ gifs:[{key,url,src}], words:[{key,text}] }`. Load lazily, cap 8 resident decoded sources, 192 px long edge, 12 fps (same caps as `shared/hypno/media.js`, but v1 may just use `decodedSource` directly). Any url that fails -> skip.
- GIF bricks: from rung 2 onward, some bricks are GIF bricks (15% of a wall, a brick that shows a gif face). THE GIF BRICK IS A SPAWNER: broken in COLOUR its face pops out of the wall along the ball direction, tumbles for 0.5-0.8 s and bursts inside the band (x 110..370, y 280..520) into an OG soap bubble (`assets/bubble.png`) holding the picture: a gif COLLIDER bubble (round, 92 px, the animated picture fills it to a slim band at the rim, drifts, the ball bounces off, pulses on hit, fades after 3 hits, at most 3 live; past that the burst is only particles). No timer spawns. Events: `popOut`, `burst`. Reduced motion: no tumble, the bubble appears at once. In GREY nothing spawns; a special brick (gif, spiral, split, jackpot) is +3 on the breakout counter (`plus` on the `brick` event).
- SPIRAL bricks: about 7% of the plain bricks (`brick.spiral`, a Loom preset with its own hue and spin) show their field turning inside the brick (render-well.js `tile`, a small disc recomposed every third frame). Broken in COLOUR the spiral pops out like a GIF face, swelling into a disc, and bursts into the whirlwind well at any saturation (the brick is the gate, not the rung); a live empty well is replaced by the new one, a well holding a ball keeps it and the burst is only particles (`burst.kind` 'none'). A spiral never becomes a collider and a picture never becomes a well (owner, 2026-09-19: the well comes out of the brick it was in, no bubble).
- Subliminals: from rung 7, a word from `words` flashes for 2-3 frames (~50 ms) NEAR THE BALL (offset so it is not under the ball) in the house colours, cadence variable ratio mean 4 s at rung 7 down to 2 s at sat 1. Also on every brick hit at sat > 0.6 with probability 0.25. Honour `ctx.gates.subliminal === false` -> no words. `ctx.fx('fx.sub_single', { s0: key })` may be called at most once per 10 s so the desktop host can also play its own flash; do not depend on it.
- Spirals: a spiral brick's pop bursts into a spiral well (radius 70, pull radius 110, 6 s, no bubble skin, no halo, no rim, no glow: the field's own wide feather fades it straight into the screen). When the ball enters the pull radius it is captured: it orbits the centre counter-clockwise at its current speed for 1..2 turns, the spiral fades, the ball leaves on its tangent. The swirl is a Loom field (shared/hypno/loom.js, render-well.js) and it is the brick's own: preset, spin factor and hue ride in on the pop, so the well is the spiral the player saw in the wall. No pictures in the whirlwind. At most one spiral live.

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

## Word triggers (owner, 2026-09-19: "map effects to specific triggers")
- About one plain brick in six carries a subliminal word (`brick.word`, dealt in turn from the word list, never on a picture brick). Breaking it in COLOUR fires the word's diegetic effect; in GREY it is a special (+3) and nothing fires.
- A word brick swaps its word on its own clock (every 1.7 to 2.6 s, each brick out of step with the others, COLOUR only, never the same word twice running; `wordSwap` event) with a 0.36 s glitch (`brick.glitch` 1 -> 0): the plate tears into offset slices, the text splits into mint and violet and its letters scramble before the new word settles. The label itself is a dark plate with gradient text, a pink glow and a slow sheen (owner, 2026-09-19).
- `word-fx.js` is the contract and the registry; one module per word under `words/` (`sink`, `drop`, `relax`, `let-go`, `deeper`, `blank`), each with sim, render (world / over / post) and sound hooks. Effects run on wall-clock and set per-frame mods on `g.mod` (timeScale, paddleW, ballSpeed, safe, autopilot, hideBricks, zoom).
- Rules: a heavy word ducks the running soft ones and the music (audio `duck`); one heavy every 4 s, a second heavy inside the gap is a stamp only; a soft word under a heavy is a stamp only; the ball never dies during a word; reduced motion keeps the sound and the tint only.
- A mod's own words land on the six by keyword family (`wordKey`: sleep -> SINK, freeze -> BLANK, surrender -> LET GO ...); an unknown word is a plain stamp. The dev panel's Words row fires each one (`game.fireWordNow`).

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

### Grey World noise dial and visibility
- Word bricks draw at 1 in 8. Voice rolls on half of colour word hits, with a 2.5 s minimum gap and volume 0.35. Near-ball text is silent.
- Old Self keeps the ball radius, with a faint grey shell and smaller wobble. The playable ball draws above the breakout flash with a dark edge.
- DROP camera shake and tilt strength are halved; timing and gameplay stay unchanged.

### Responsiveness pass
- SINK lasts 0.8 s, slows to 0.75x and shrinks the field by 6%; its eight-strip melt has no canvas blur.
- Combo hit stops cap at 20 ms. DROP shake and tilt are half the previous preview strength.
- Canvas rendering caps at 1.5 million pixels; DROP echoes avoid full-frame tint passes.
- BLANK uses one quiet synthesized finger snap, with no swell, hard audio cut or return click.

### Follow-up: snap, DROP and grey identities

BLANK cuts immediately to its veil. A predecoded CC0 finger-snap recording plays after that first frame is drawn, without beat quantisation or a fetch on the hit. See assets/CREDITS.md.
DROP no longer freezes play or copies the frame for tilt and echoes. It uses only a downward nudge of 0.8 field pixels; other camera shake is suppressed while DROP runs. The vertical ball lane stays.
Visible picture bricks are pinned for animation regardless of ball distance. Decoder limits remain 8 sources, 192 px and 12 fps; reduced motion still holds pictures.
Grey bricks hide all words, letters, spirals, split marks and pictures. Specials use a darker dense bevel; game data and hit rewards are unchanged.

### Scrolller animation and LET GO follow-up

The shared image decoder retains same-origin credentials so protected preview media can be decoded instead of falling back to frozen images. Cross-origin requests still receive no cookies.
LET GO guides the paddle for 3 seconds (previously 1.5); its glow and return bell follow that duration.

Breakout requests the canvas compatibility decoder for animated pictures. This avoids native VideoFrame drawing on its 2D surfaces; frame size and playback limits stay at 192 px and 12 fps. Verified nontransparent, changing pixels in an eight-clip browser test. The reported embedded-browser failure still needs confirmation on the owner's session.

### LET GO shield, 2026-09-19
Owner confirmed animated pictures now work. LET GO replaces automatic paddle guidance with a full-width shield below the paddle for five seconds. The last second flickers and fades (smooth fade under reduced motion); protection remains active through that warning. Paddle input stays manual. Nested bubble tiers are proposed next, not implemented in this pass.

### Bubble tiers and shield visibility, 2026-09-19
Picture frequency stays 15%; picture bricks carry tiers at 70/20/10. Bubbles take 1/2/3 hits, award saturation +0.03/+0.06/+0.10 only on the final hit, and stop colliding immediately when popped. Maximum three live colliders; nested skins have no timeout. Tier 1 sends one fading picture forward; tier 2 sheds a skin then releases two distinct resident pictures together; tier 3 covers the canvas with 0.4 s fade in, 3 s hold, 0.4 s fade out. The ball and paddle remain visible above rewards. Rewards use the local canvas decoder rather than the throttled host picture calls; a one-picture pool falls back to one picture for tier 2. Grey pool remains separate unfinished work. LET GO now has a brighter mint and white barrier, brief arrival flicker and sparks, and final-second flicker; reduced motion uses steady arrival and smooth expiry.

### Bubble identity, procedural fullscreen and crack polish, 2026-09-19
Colour-world picture bricks and bubbles use cyan for one picture, gold for two, violet for fullscreen. Pips show remaining bubble hits; reward labels remain visible before media loads. Grey identities stay hidden. The web preview now requests a fresh recipe from the actual Loom randomParams2 generator per fullscreen showing, retaining that recipe across frames and keeping inward motion. Named wake remains unchanged. Crack dressing uses shorter edge fractures, fine refractive lips and tip glints, with a weaker flash and camera kick. Shattering the grey background into many pieces at breakout remains TBD.


### Sequential walls: Spell, 2026-09-19

Owner decision: formations are levels in the normal wall-clear sequence. Spell is wall three, reached after clearing two walls; there is no player-facing level selector. Other planned levels remain pending. The existing dev gear includes a wall-three shortcut.

Spell varies DROP, SINK, RELAX and LET GO. Letter tiles cycle with hits; a broken tile fills a matching unfilled position in the large background target. Useful letters dominate, with occasional reordered letters. Completed slots remain earned. A completed word gets a short echo/particle celebration and its spoken audio, then a different word begins. Ball motion continues throughout; reduced motion keeps the reward readable without zoom or flicker. Grey conceals identities.

Picture tiers use cyan, gold and violet auras only. Hard tier outlines, badges, hit pips and reward labels are removed. Cached glow sprites pulse in normal motion and stay still with reduced motion.


### Landscape and bubble stages (2026-09-19)
Arena is 1280x720 (16:9), with wider classic bricks and a 170-unit base paddle. Vertical travel speed is unchanged. Spell tiles cap at 32 units. Bubble aura is 40% dimmer; remaining hits use deep purple (3), violet (2), pink (1), matching brick tiers. Original tier still determines the reward. Soap rims use a cached softened sprite, without per-frame blur.

Landscape wall refinement: 10 columns of 82x27 bricks, 6-unit gaps, starting 42 units from the top. Narrower, taller bricks form a centered wall.

Visual refinement: broken bricks shed ten irregular grey fragments plus dust, capped and fading within 0.9 seconds. Reduced motion skips the burst. Spell tiles widen to 44 units with 25px heavy letters. Broken-pane dressing uses sparse hairline branches and local impact splinters, without concentric rings. Fullscreen preview spirals and GIFs reduce transparency by 20%; fully opaque bubble rewards remain opaque.

Landscape media scale: GIF bubbles and spiral wells, including collision/pull radii, are 33% larger. Single and duo picture rewards grow 33%; fullscreen media already fills the viewport.

Brick height follow-up: classic bricks are 82x34; Spell tiles rise to 48 units, constrained proportionally for longer words.

Fullscreen spiral follow-up: transparency is 30% higher than the preceding preview. While the host spiral is visible, a pointer-transparent foreground canvas redraws the ball above that overlay using the game camera transform. Removed when the effect or station closes.

GIF progression: multiply the landscape GIF bubble radius and single/duo reward size by 0.8 on wall 1, increasing linearly to 1.7 on wall 6, capped thereafter. Fullscreen rewards remain viewport-sized; spiral size stays fixed.

Every grey-to-colour breakout grants a shield for 2 seconds, followed by 0.6 seconds of flicker-out protection. Manual paddle control and the separate five-second LET GO shield remain unchanged.

Default landscape wall: 18 columns by 8 rows of 64x36 bricks (16:9), with 6-unit gaps and 42-unit top margin. Spell keeps its separate letter formation.

Default wall spacing refinement: 18 columns by 5 rows, bricks 57.6x32.4 (10% smaller, still 16:9), top margin 26 units.

Main wall refinement: 16 columns by 5 rows, bricks 48.96x27.54 (another 15% smaller, still 16:9), centered with the existing 26-unit top margin.


## Wall 4: dome (owner, 2026-09-19)

The fourth cleared-wall formation is a dome of normal bricks around a central Loom spiral. It follows the letter wall in sequence and has a developer-only jump shortcut.

The central well persists for the entire wall and morphs between procedurally generated Loom recipes. Its idle rotation follows the soundtrack beat. Ball hits add temporary, bounded spin energy that decays between hits. More energy strengthens the well and speeds up the captured ball while slightly shortening the capture. Release must leave enough cooldown to escape the well. Grey mode retains the existing hidden-effect rules; breakout restores the central well.

Keep the ball visible over the spiral, retain reduced-motion behavior, and reuse bounded rendering resources rather than generating new canvases each frame.


## Wall 2: The Tide (owner, 2026-09-19)

The second sequential wall has four curved ribbons of twenty normal bricks. Alternating rows move in opposing currents with gentle tilt; gaps open naturally as bricks are destroyed. Brick identities and GIF, spiral, word and split payloads persist. No respawning, new gravitational force or mid-flight aim assistance. The formation stays in the upper playfield, and reduced motion holds it still. Developer shortcut: wall 2: tide.


## Wall 8 opening: interruption (owner, 2026-09-19)

Enter in colour, assemble three closed brick rings, and hold the ball on the paddle for explicit tap/click/Space launch. First ring contact freezes before reflection or damage; the full-width defensive row below the smaller raised ring breaks normally before that contact and does not trigger the interruption. ENOUGH then YOU GOTTA STOP cover the field in black, followed by a fade into forced grey. The ring is unbreakable and sounds metallic. The first 30 seconds of grey have no words. Then up to four discouragement words appear in the high side lanes, staggered roughly three seconds apart, lasting five to six seconds and glitching away on expiry; each broken word glitches away and earns one toward the established shrinking breakout counter. Lost balls re-serve without erasing earned word progress. Breakout removes the lock and words, leaving playable rings around a grey glitching Loom core. Destroying an inner-ring brick near the core brings in stage two: a flared spiral continuously fed with bricks from offscreen. Breaching its inner ring stops replenishment. This proximity trigger replaces the earlier 75%-clear handoff proposal. Later stages and the final outro remain pending design approval.

The preceding wall final brick survives grey impacts; these earn one counter step so colour remains reachable. Mobile play permits landscape, respects safe areas, and captures touch paddle drags.


## Polish pass (2026-09-21)

Five stacked PRs on a small scaffold: `cues.js` (name -> sound, offered every game event before station.js runs its own switch), `reactions.js` (name -> visual, called at the end of render.js onEvent), and the `haptics.js` / `gamepad.js` hooks. A lane adds a sound or a reaction by adding a key, never by editing audio.js, station.js or the render switch.

- Hit-stop happens at exactly three rare events, all through `rareStop()`: the last brick of a wall 90 ms, a jackpot brick 60 ms, a bubble's final pop 40 ms. Routine hits never stall the ball.
- The last brick: `lastBrick` is emitted once per wall and always before `wall`. In COLOUR with motion on, the hit-stop is followed by 0.45 s at 0.35 time scale (the bed pitches down with it) and a camera push-in to 1.06; `g.clearing` holds the next wall back about 0.54 s so the breath cue leads the bells. GREY and reduced motion emit the event and skip the theatrics. The iris wall counts its last core.
- Impact squash: any bounce sets `ball.squash` 1 -> 0 over 90 ms with the surface normal; the permanent velocity stretch is 0.07 because the comet trail carries speed.
- Perfect streak: `perfect` carries `streak`; the text grows to a cap, the audio stamp's second note climbs the pentatonic (six steps at most), +0.003 saturation per step capped at 0.012.
- `layer` {name} fires at 0.4 (melody) and 0.7 (arp), COLOUR only, one per saturation gain, re-armed by a relapse.
- The stuck ball pulses with the beat and the AUTO launch lands on the first downbeat at or after 1.2 s; a manual launch is immediate.
- Paddle english is at most 8 degrees from the smoothed `paddle.vx`, never on top of the end-of-wall assist or the finale help. Keys (arrows and A / D, newest wins) ease in over 120 ms; touch is a relative drag at 1.15x; mouse and pen stay absolute.
- Power-ups: the laser fires one volley per eighth note of the bed (`beatTime`), led by 0.16 of an eighth so the quantised pluck lands on the grid; the pluck is the chord tone beside the arp's. Each kind has its own catch motif. Events: `powerDrop`, `powerCatch`, `powerMiss`, `powerWarn` (2 s left, once per catch), `powerExpire`, `laserShot`, `laserHit`. A spent shield or an empty multiball zeroes its timer silently.
- Audio: a held brick is the dry `damage` tink; pause is a 150 ms low-pass sweep before the context suspends and 200 ms back; new cues never retune the old ones.
- Haptics: `planPulse` maps events to flat pulses (about three a second, priority interrupts, level 0 = stop) and sends them to three sinks: the host frame `{type:'haptic', station, level, ms, tag}` (CONTRACT 10.23), `navigator.vibrate`, and gamepad rumble. GREY sends a faint brick tick and the relapse / breakout pulses only. `?nohaptics` turns it off.
