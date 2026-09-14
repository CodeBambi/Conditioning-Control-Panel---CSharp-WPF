# Slot station

The Candy cabinet (CONTRACT.md sections 2-7). Entry `station.js`, loaded by the room from `stations.json`.

| File | What |
|---|---|
| `station.js` | `mount(ctx)` -> `{open, close, suspend, destroy}`. DOM, readouts, press flow, fx, melt. |
| `tape.js` | Pure tape client: state, buys, idem reuse, freeze, shownSp. No DOM. |
| `scene.js` | three.js cabinet. One WebGL context per `open`, freed in `close`. Plays the moves `feel.js` picks. |
| `pace.js` | THE PACE: about 4 s an outcome (spin, staggered stops, reveal, breath). Reduced motion keeps every duration. |
| `feel.js` | THE HOUSE BOOK, pure: tiers, the Brake's recipe per outcome, the chime ladder, glance chain, token counts. |
| `bank.js` | THE BANK tokens (DOM, rAF): payout_spawn -> SP readout, reversed for a debit. |
| `sound.js` | The cues, from the race's existing clips (`dtrh/assets/bubbles/sfx`) with a synth fallback. No new files. |
| `symbols.js` / `media.js` | Reel cell art and the sit-down deal (keys only leave the page). |
| `nodes.js` | The glb node names the page drives. |
| `mock-server.js`, `dev.html` | Standalone harness. Not shipped behaviour. |

## What the page needs from the room

- An import map for `three` and `three/addons/` pointing at `/vendor/three/` (GLTFLoader imports `three` bare).
- `ctx.request(op, body, idem)` resolving a `station-result`, `ctx.fx(fxId, symbols)`, `ctx.media()`,
  `ctx.onSp(fn)`, `ctx.sp()`, `ctx.lex(key, fallback)`, `ctx.standUp()`.
- The `melt` frame goes out through `ctx.melt(left)` when present, else `ctx.bridge.send({type:'melt', ...})`.
- `ctx.hostBack === true` (the room sets it): the station hides its own Back chip and the card's Back button, because
  the room's Back is the only one. Escape still stands up. Without it (dev.html) both buttons show.
- `ctx.variant` `{ id, name, palette }` or null: `palette` (material name -> `rrggbb`) recolours `candy_rose`,
  `candy_violet`, `candy_plum` and `wand_pink` through `palette.js` (one clone per name, the glb untouched), and
  `name` goes on the marquee. Null keeps the rose cabinet and the `br_slot_marquee` text.
- `ctx.intensity` `calm` flies at most 4 bank tokens (Brake 8, lite).

### One SP readout (in-room tidy)

Picked: **the room's chip is the only SP readout and THE BANK's target.** With `ctx.hostBack` the station's own
`.slot-sp` is hidden, tokens fly to the room's `#br-sp-value`, and the station writes what the readout SAYS there
(`shownSp`, ticking per landing; Law I, display only). A room repaint while seated is put back by a
MutationObserver. `close()` skips the bank and leaves the chip holding `ctx.sp()`, the room's own true value.
Standalone, `.slot-sp` is both readout and target. The station's jackpot and status chips stack under the room's
Back (left) and EMI's HUD face sits under the room's SP chip (right), so the marquee reads across the top;
`scene.js` frames the marquee and EMI's face into the play view from live bounds.

## Feel (lane F1, cited from house-book.md)

- **Law VIII** answer in 100 ms: a press leans the lever (`scene.answer`, 80 ms) and EMI glances before the tape or
  the server replies; a refusal lets go. Freeze buttons dip on light and clear. Standalone Back rings (`is-ringing`).
- **THE THUD** per reel stop, left to right, at 1800/2180/2560 ms: rotation overshoot on `cubic-bezier(.2,1.5,.4,1)`,
  reel brightness 2.2 -> 1 over 340 ms, the pitched cue (-2/0/+2 semis) on the same frame (`onReelStop`).
- **THE BANK** on a pay: 3-7 tokens by tier (4 on Calm), 560 ms each, 70 ms stagger, readout ticks per landing,
  mini-thud on the last, `payout_tray` thuds as the pay leaves it, `+N SP` text. Reversed for a tape or freeze
  debit: ticks down as each token leaves, lands in `payout_tray`.
- **THE CHIME LADDER**: one family (chime1..3), +1 semitone per consecutive win, cap 7, -12 while melted.
- **THE MASCOT GLANCE**: press -> spirals, landing -> jackpot / hearts / melt / idle, hold 600 ms (800 melted), then
  rest; `glance()` never repeats the current pose.
- **THE BREATH**: only the lever at rest, 3.2 s ease-in-out, paused while a party runs. The old screen_jackpot
  pulse (a second breather) is gone.
- **Law IX / THE MARQUEE**: tier 1 chime + win chase, tier 2 two notes + EMI jolt + `WIN +N` glow on
  screen_jackpot, tier 3 THE THUD on screen_jackpot + big chase, tier 4 THE REVEAL (EMI over-rotates, jackpot
  counts up 620 ms, sparks, gold). `marquee_glow` heat tracks the tier (in 80 ms, out 480 ms), gold at the top.
- **The Brake**: overlapping parties and pays merge (one hero per beat); Brake 3 fanfare x3, then chime + bead,
  from the 40th a thud and tokens; melted = no ceremonies, ladder down an octave, EMI slows; the jackpot REVEAL
  once per sit-down; a no-pay spin gets THE SHIVER (+-4 px, 250 ms) and a muted last thud; no move over 620 ms
  but the jackpot hero; chase steps never faster than 6 Hz; every value is also text.
- **Law VI**: reduced motion takes the state (reels on their stop at the thud frame, no tokens: the readout is on
  the settled value with a lit ring, no shiver, heat steps). Back and suspend skip every ceremony to settled.
- **A1 THE ANTICIPATION REEL** (playbook Tier A, CONTRACT 10.14): a live pair on reels 1 and 2 (the same gif 900 ms,
  two spirals or two subs 1,100 ms, two EMI 1,400 ms and gold) keeps reel 3 blurred past its normal stop before THE
  THUD. From reel 2's thud a tone climbs (`sound.rise`, a synth sweep), the marquee takes the `tease` mood
  (`tease_gold` on the EMI pair) and the bulbs drop to 0.62 emissive; THE BREATH is paused by the spin already.
  Brake 5 halves the hold while melted and never lets it go gold. Reduced motion and Calm KEEP the hold and the tone
  and drop the light change only: the hold is pace, not animation. A frozen reel 3 never holds. The outcome is on
  the tape before a reel moves, so the hold is purely a delay (`feel.anticipation`, `pace.ANTICIPATION`).
- **A2 THE ALMOST on the strip** (CONTRACT 10.14): a no-pay spin under a live pair, where the cell one above or one
  below the payline on reel 3 would have completed the line, ghosts that cell gold and snaps it back once (620 ms,
  gold in over 500, one 120 ms snap), on the same frame as the muted thud and THE SHIVER (Law X) with a ghost note
  under it. Once a spin, the cell below read first. The reel window shows about half of each neighbour (drum r 0.43,
  13 cells, window 0.39 tall), so nothing is nudged. Reduced motion: one 120 ms gold tint on the reel, no repaint.
  `feel.almost` reads the server's own strip and stop: no stop weighting, ever. Natural rate on table v5: 9 of the
  2,197 uniform rows, gif and EMI pairs only (a sub or spiral pair already pays).

## Checks

```
node --test ConditioningControlPanel/Resources/web/backroom/stations/slot/tests/
node --test ConditioningControlPanel/Resources/web/backroom/stations/slot/tests/*.test.mjs   # Node 24 on Windows
node ConditioningControlPanel/Resources/web/backroom/stations/slot/tests/nodes-check.mjs [slot.glb]
```

Run the node check on every new `slot.glb` drop. Required nodes (`cabinet`, `reel_1..3`, `lever`,
`cam_seat`, `cam_target`) fail it and show "Model missing X" in the page; the rest only warn
(`marquee_glow`, `payout_tray`, `payout_spawn`, `emi_topper` and the chase bulbs degrade quietly).

Dev harness: serve `ConditioningControlPanel/Resources` as the web root and open
`/web/backroom/stations/slot/dev.html` (`?sp=57&melt=0&floor=3000&latency=120&reduced&variant=violet`).
`dev.station.debug().feel` shows the feel log, cue trace, pose and scene state.

`mock-server.js` follows table v6 (CONTRACT 10.14: `emi3` pays 400, drawn at 154 per million on its own seeded
stream) and the 3000 ms slot and freeze floors (10.12).
