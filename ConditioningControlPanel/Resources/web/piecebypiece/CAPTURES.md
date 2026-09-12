# Piece by Piece - the captures

Twelve ways to take a man, two per piece. One is built (the bishop's whip, `board/whip.js`);
the rest are specified to the same envelope so any of them can be built next without
re-opening the timing argument.

## The envelope every one of these obeys

From PBP-BEATS beat 3, and not negotiable here:

- **The 440 line.** `clock.press(opponent)` runs synchronously inside `tryMove`, so the
  opponent's clock is running for the whole capture. The taker is ON the victim's square,
  `land {capture:true}` fired, by **300 ms** (a slide) or **440 ms** (a knight's hop, or any
  taker that stops short first). Everything after that is scenery: the board is pickable,
  nothing is gated, the next move is already legal.
- **Flourish lives in the victim's tail**, 300 to 1420 ms (tip, roll, sink), and in the
  taker's landing itself. Never in the approach. If a piece needs longer, it borrows from the
  victim's tail, never from its own arrival.
- **The victim's SHIVER is mandatory**: a forced tremor of ~0.05 local units, three cycles
  over 250 ms, from the frame his fall begins. No colour change, no emissive. A flinch, not a
  highlight. R5: failure gets sympathy, never silence, never shame.
- **`land` is the seam.** Every choreography emits `land {square, piece, side, capture:true,
  height, world, screen}` at the contact frame, before it spends `tookOne`. The recap still
  (beat 13) is read off the WebGL canvas three frames after it; nothing here touches the
  renderer, and `preserveDrawingBuffer` stays off.
- **No camera move on a capture.** Thirty a game; a frame that reacts to each one is never
  still (Law III).
- **Sound budget.** The thud is already pitched by the taker's height. One new cue in the
  ladder (the queen's chime, rung 2). The bishop's crack is the exception argued below: it
  *replaces* the squelch on his landing rather than adding to it, so a whip costs the ear the
  same two voices as any capture (thud + one). Nothing else adds a voice.
- **Ceiling 2110 ms** from `capture` to a settled parade man. The mesh must still be a live
  object with its materials at `sunk`; parade.js re-parents it.
- **Reduced motion**: the taker slides and lands, the victim tips and vanishes, no roll, no
  shiver, no flourish, parade men appear at their slots. Sound survives.
- **Skippable** (Law VI): `anim.skip()` lands whatever a choreography still owes. A tap on
  the board or Space/Enter mid-whip does it; a take-back does it on its own.

Timings below are ms from the move being played. "[E]" is what the board already does,
"[N]" is new. Named moves are the house book's.

---

## Pawn

### P1 - The Plug (rung 0, the envelope) [E]
The plain capture, and the baseline the others are measured against.

| t | what |
|---|---|
| 0 | `capture`; victim tip begins; SHIVER on the victim |
| 300 | pawn lands, `land{capture}`, THUD x1.75, dust x1.6, squelch |
| 600 | victim rolls 0.32u away from the pawn |
| 1020 | victim sinks |
| 1420 | `sunk`; parade rise |
| 1970 | parade man standing |

Moves: THUD, SHIVER. Sound: `capture`. Camera: none. Reduced: tip and vanish.

### P2 - The Stomp [N]
The little man makes sure. He lands, then hops once in place and comes down harder, and the
victim's roll is the shove that hop gives him.

| t | what |
|---|---|
| 0 | as P1 |
| 300 | pawn lands, `land{capture}` (the seam, the still, the thud) |
| 380 | pawn hops 0.12u straight up, 120 ms, no travel (`slide` with from == to is a no-op today: this is a `jiggle.impulse` squash -2.0 then +5.0, a stretch then a squat, not a flight) |
| 500 | second squash lands: dust puff at `refusedGain` size, no ring, no flash, no sound |
| 500 | victim roll begins from the second squat, `rollPush` 0.32 -> 0.45 |
| 1420 | `sunk` unchanged |

Moves: THUD, BOUNCE (the hop), SHIVER. Sound: `capture` at 300 and nothing at 500 (the eye
gets the second beat, the ear does not need it). Camera: none. Reduced: P1. Cost: two
constants and one timer in anim.js; the roll delay reads `stompSec`.

## Knight

### N1 - The Hop (rung 0) [E]
Already the knight's own capture: he hops (knightSec 440, knightHop 0.9, lean 0.30) and lands
with `knightLand` 1.6 extra squash; the victim holds 140 ms before he starts to tip so he
still hits the board as the knight lands.

| t | what |
|---|---|
| 0 | `capture`; victim HOLDS |
| 140 | victim tip begins; SHIVER |
| 440 | knight lands, `land{capture}`, squash x1.6 on top of x1.75 |
| 740 / 1160 / 1560 / 2110 | roll / sink / `sunk` / standing |

Moves: THUD, SHIVER. Sound: `capture`. Camera: none. Reduced: slide, not hop.

### N2 - The Pounce [N]
The same hop with intent: a higher arc (1.2), a steeper lean (0.42) that holds through the
top, and a landing that puts the victim straight down instead of tipping him over. He lands on
the man, and the man is under him.

| t | what |
|---|---|
| 0 | `capture`; victim HOLDS |
| 240 | victim SHIVER begins (he sees it coming: 200 ms early, the only pre-contact beat in the set) |
| 440 | knight lands, `land{capture}`; victim `tipSec` 0.14 with the axis across the knight's travel, so he goes flat away from the hooves |
| 440 | balls squash: `jiggle.impulse` squash 6.5 on the knight (the base takes it, `squashWeightPow` 1.0 already loads the bottom) |
| 620 | victim roll begins, 0.40u |
| 1560 | `sunk` |

Moves: THUD (heavier), SHIVER (early), BOUNCE (the recovery ring of the spring). Sound:
`capture`. Camera: none. Reduced: N1's reduced cut. Cost: three constants on the knight's
slide entry; the early shiver is one `delay` on the shiver list.

## Bishop

### B1 - The Whip (BUILT but GATED OFF by default: `settings.whip` / `?whip=1`; `board/whip.js`, `anim.js`) [N]
The tentacle takes the man. The bishop stops one stand-off short on the diagonal, drops,
draws back, cracks across the victim, and strides onto the square while the tentacle rings.

| t | what |
|---|---|
| 0 | `capture`; victim HOLDS (`tumble.hold`) |
| 200 | bishop lands at the stand-off (0.62u short): `land{capture:false, manner:'whip', stage:'standoff'}`, THUD, dust, honest squash; `tookOne` kept |
| 200-300 | wind: tip draws back 0.26 away from the victim (forced bend, `windAmp`) |
| 300-380 | snap: back to 0.46 past the victim, most of it in the last half |
| 336 | **crack**: `hit {square, piece, side, victim, victimSide, world, screen, height}`; sfx `whip` (a 5.2k -> 700 Hz crack, a 170 -> 55 Hz slap); dust `hit` puff at the victim, no flash, no ring; victim flinch impulse (bend 2.2 away, squash 2.0) and SHIVER (0.05, 3 cycles, 250 ms, across the whip line); bishop lunge squash 2.4; victim fall begins, `tipSec` 0.10 |
| 336 | stride begins (`stepSec` 0.10, hop 0.10) |
| 436 | bishop on the square: `land{capture:true, manner:'whip', stage:'square'}`, THUD, dust x1.6, **no squelch** (sfx skips it for manner whip: the crack was that voice); victim hits the board on the same frame |
| 380-800 | ring: the tentacle rings out at 4.2 Hz, decay 8.5/s, over the stride and after it (scenery) |
| 716 | victim has rolled 0.42u away from the bishop |
| 1420 | `sunk`; parade rise |
| 1970 | parade man standing |

Moves: THUD (twice, the drop and the square), SHIVER, BOUNCE (the ring). Sound: `land` +
`whip` + `land`; the squelch is not played. Camera: none. Reduced: the plain capture (P1's
path), no stand-off, no whip. Skip: tap or Space/Enter lands the bishop and sinks the victim
at once (`anim.skip()`); a take-back does the same. Proof: `smoke/whip-smoke.mjs` (the curve,
278 checks) and `smoke/whip-shot.mjs` (headless Edge: the beats above measured off the bus,
the skip, the reduced cut).

### B2 - The Coil [N]
The bishop lands on the square the plain way (300), and the tentacle wraps: the forced bend
rotates a full turn around the axis over 420 ms while the victim rolls, so the roll reads as
being wound off the tentacle.

| t | what |
|---|---|
| 0 | `capture`; victim tip begins; SHIVER |
| 300 | bishop lands, `land{capture}`, THUD, dust, squelch (this one keeps the squelch: no crack) |
| 300-720 | coil: forced bend of 0.22 whose direction turns 360 degrees, starting toward the victim; `rippleGain` doubled for the duration so the suckers travel |
| 600 | victim roll begins, his axis turning with the coil (the roll's axis is re-aimed each frame from the coil angle) |
| 1420 | `sunk` |

Moves: THUD, SHIVER, DRIFT (the coil is a drift on the man, not a breath). Sound: `capture`.
Camera: none. Reduced: P1. Cost: one timeline like whip.js (`coil.js`), a per-frame re-aim of
the tumble axis.

## Rook

### R1 - The Dome Drop (rung 1) [N]
The heavy one comes straight down. Lower arc (hop 0.08), the landing at `captureGain` 1.9
with one extra low tap under the squelch, and the victim is flattened before he tips: a squash
of 4.0 on him at contact, then the tip.

| t | what |
|---|---|
| 0 | `capture`; victim SHIVER; victim tip HOLDS 120 |
| 120 | victim tip begins (he goes over as the dome arrives) |
| 300 | rook lands, `land{capture}`, THUD, dust x1.9, squelch + low tap (rung 1's one constant) |
| 300 | victim squash 4.0 (flattened), then his roll from 600 |
| 1420 | `sunk` |

Moves: THUD, SHIVER. Sound: `capture` + one low tap. Camera: none. Reduced: P1. Cost: rung 1
is one constant; the flatten is one impulse.

### R2 - The Steamroll [N]
The rook lands plain (300) and the victim rolls twice: `rollSec` 0.42 -> 0.60, the roll angle
2pi -> 4pi, the push 0.32 -> 0.50, and the rook's ribs ripple (`rippleGain` x2 for 400 ms) as
if the man went under them.

| t | what |
|---|---|
| 0 | `capture`; victim tip; SHIVER |
| 300 | rook lands, `land{capture}` |
| 600-1200 | the double roll |
| 1200 | sink begins (borrowed 180 ms from the sink's start, still `sunk` by 1600) |
| 1600 | `sunk` (inside the ceiling: standing by 2150 - **over by 40 ms**; take it from `sinkSec` 0.7 -> 0.66) |

Moves: THUD, SHIVER, DRIFT. Sound: `capture`. Camera: none. Reduced: P1. Cost: three per-tumble
overrides (already supported: `tipSec`/`rollSec` are per-entry since the whip) plus a roll
angle multiplier.

## Queen

### Q1 - The Tiara Sparkle [N]
She lands plain (300), and the sparkle is hers: the dust's king-crown spark path fired from
her tiara at the landing (8 sparks, 450 ms, gold `0xFFE39A`), the victim's tip slowed to 0.36
so he goes over under the sparks.

| t | what |
|---|---|
| 0 | `capture`; victim tip begins (0.36); SHIVER |
| 300 | queen lands, `land{capture}`, THUD, dust, squelch, SPARKLE BURST off the tiara |
| 660 | victim roll begins (the slower tip pushes the roll 60 ms; sink and `sunk` shift with it, `sunk` at 1480) |

Moves: THUD, SPARKLE BURST, SHIVER. Sound: `capture` (no chime: rung 2's chime is for the
queen as *victim*, see below). Camera: none. Reduced: P1, no sparks. Cost: the spark path
already exists for `type === 'k'` in dust.js; this is a second trigger keyed on `piece === 'q'`.

### Q2 - The Curtsey [N]
She lands plain (300) and bends once toward the fallen man, a bow of 0.18 over 300 ms that
holds 120 and returns, while he rolls away from her. Read as a courtesy, not a gloat: the
bend is slow and the return is slower.

| t | what |
|---|---|
| 0 | `capture`; victim tip; SHIVER |
| 300 | queen lands, `land{capture}` |
| 420-720 | bow toward the victim (forced bend 0.18, ease in-out) |
| 720-840 | hold |
| 840-1200 | return |
| 600 | victim roll begins (unchanged) |
| 1420 | `sunk` |

Moves: THUD, SHIVER, MASCOT GLANCE (a body doing it, not a face). Sound: `capture`. Camera:
none. Reduced: P1. Cost: one forced-bend timeline on the taker, keyed on `type === 'q'`.

## King

### K1 - The Crown Drop (rung 0 + sparks) [E, mostly]
The king lands plain and dust.js already throws gold sparks off his crown on any landing
(`sparks` 8, `sparkLife`). Add: `knightLand`-class extra squash of 1.4 so the biggest man
comes down like the biggest man.

| t | what |
|---|---|
| 0 | `capture`; victim tip; SHIVER |
| 300 | king lands, `land{capture}`, THUD (lowest pitch, `kingHz` 95), dust x1.6, crown sparks, squash x1.4 |
| 600 / 1020 / 1420 | roll / sink / `sunk` |

Moves: THUD, SPARKLE BURST (existing), SHIVER. Sound: `capture`. Camera: none. Reduced: P1, no
sparks. Cost: one constant.

### K2 - The Lean [N]
He lands, and looks down at what he did: a forced bend of 0.14 toward the fallen victim over
400 ms, held 200, returned over 500 - slower than the queen's curtsey, and lower, a weight
shifting rather than a bow. The victim rolls out from under the lean.

| t | what |
|---|---|
| 0 | `capture`; victim tip; SHIVER |
| 300 | king lands, `land{capture}`, crown sparks |
| 380-780 | lean toward the victim (0.14) |
| 780-980 | hold |
| 980-1480 | return |
| 600 | victim roll begins, 0.36u (a touch further: he is getting out of the way) |
| 1420 | `sunk` |

Moves: THUD, SHIVER, MASCOT GLANCE. Sound: `capture`. Camera: none. Reduced: P1. Cost: shares
Q2's forced-bend timeline with different constants.

---

## Cross-cutting, from the ladder (owned by the beats spec, noted so nothing here fights it)

- **Rung 2, the queen as victim**: SPARKLE BURST from *her* tiara as she goes over, plus one
  `chime`. Fires whoever took her; the taker's own choreography above still plays.
- **Rung 3, tally up 5+**: one GLOW on the HUD tally word, 480 ms. HUD, not board.
- **Rung 1, the rook**: dust `captureGain` 1.9 and one low tap. R1 above is that rung with the
  flatten added.

## What the whip changed in the engine, for whoever builds the next one

- `anim.slide(piece, from, to, hop, opts)`: `opts.dur` and `opts.finish` (a finish slide does
  not re-read the victim).
- Tumble entries carry `hold`, `tipSec`, `rollSec` per victim; `hold` freezes him upright
  until a choreography releases him.
- `landed(piece, at, refused, extra)`: extra fields ride the `land` payload; every land now
  says `manner` ('plain' | 'whip').
- `anim.skip()`, `anim.whipping()`; boot.js taps and Space/Enter call skip while a bishop is
  owed his square.
- The bus has `hit`; dust puffs on it (`hitGain`, no flash, no ring), sfx plays `whip` on it
  and skips the squelch for a `land` whose manner is whip.
- `board/whip.js` is the model for a choreography file: pure functions of time, its own
  frozen TUNING, inlinable by the preview builder (`smoke/whip-preview.mjs`).
