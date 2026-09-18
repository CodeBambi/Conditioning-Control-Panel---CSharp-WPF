# Slot station

The Candy cabinet (CONTRACT.md sections 2-7). Entry `station.js`, loaded by the room from `stations.json`.

| File | What |
|---|---|
| `station.js` | `mount(ctx)` -> `{open, close, suspend, destroy}`. DOM, readouts, press flow, fx, melt. |
| `tape.js` | Pure tape client: state, buys, idem reuse, freeze, shownSp. No DOM. |
| `scene.js` | three.js cabinet. One WebGL context per `open`, freed in `close`. Plays the moves `feel.js` picks. |
| `pace.js` | THE PACE: about 4 s an outcome (spin, staggered stops, reveal, breath). Reduced motion keeps every duration. |
| `feel.js` | THE HOUSE BOOK, pure: tiers, the Brake's recipe per outcome, the glance chain, the playbook's Tiers A/B/C. The chime ladder and the bank maths are re-exported from `shared/win/` (10.22.C), not written here. |
| `bank.js` | THE BANK tokens: the `slot-token` elements, the layer, the rect and the rAF. The clock, the value ladder, the merge and the skip are `shared/win/bank.js`'s `createBankRun`. payout_spawn -> SP readout, reversed for a debit. |
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
  mini-thud at the end of the count, `payout_tray` thuds as the pay leaves it, `+N SP` text. Reversed for a tape
  or freeze debit: ticks down as each token leaves, lands in `payout_tray`.
- **Proportional rollup** (casino playbook A3): the count-up is scaled to the win, not flat 500 ms. The whole
  table is `feel.ROLLUP_MS`, by tier: `[0, 500, 1200, 2000, 6000]` ms. The TOKENS never move for it (the House
  Book caps stand: 3-7 tokens, 4 on Calm, 560 ms each, 70 ms stagger), only how long the READOUT counts. The
  tokens tick it as they land exactly as before, and where the rollup outlasts their flight (tiers 2, 3 and 4,
  whose flights are 770, 840 and 980 ms) the readout carries on counting from the last landing to the settled
  value over the rest of the window, on a shallow ease-out and silently; the mini-thud and the readout THUD wait
  for the end of the count (Law X: one gesture, one beat). The `+N SP` text goes up when the tokens land and
  stays up until the count settles. Law I: the value it lands on is the tape's, `bank.js` only paints it.
- **THE CHIME LADDER**: one family (chime1..3), +1 semitone per consecutive win, cap 7, -12 while melted. Across
  a rollup it CLIMBS with the count (`feel.ladderPlan` -> `sound.climb`): 1 step at tier 1, then 3, 5 and 7,
  spread over the rollup window a semitone apart and never closer than the 6 Hz strobe floor, so a big win
  rises instead of ringing once. A melted win gets the landing note and no climb (Brake 5).
- **THE PAYLINE FRAME** (casino playbook A6): after a win the winning row is framed for exactly the rollup
  length, then THE GLOW goes out over 480 ms. It is a DOM frame (`.slot-payline`) that `scene.js` puts on the
  reel window's live projected bounds every frame, one drum cell tall (the chord of a `2PI/13` cell on the
  drum's own radius), so nothing is hard-coded and nothing is added to the glb. `scene.js` drives the pulse
  itself on a cosine, never faster than 2 Hz (`feel.PAYLINE_PULSE_MIN_MS`), and it never goes dark before the
  fade (Brake 9). Tier 1 takes a single soft pulse (Law IX: a small win is not confetti); a melted spin takes a
  steady frame (Brake 5). It rides the reveal's beat, it does not add one (Law X).
- **THE MASCOT GLANCE**: press -> spirals, landing -> jackpot / hearts / melt / idle, hold 600 ms (800 melted), then
  rest; `glance()` never repeats the current pose.
- **THE BREATH**: only the lever at rest, 3.2 s ease-in-out, paused while a party runs. The old screen_jackpot
  pulse (a second breather) is gone.
- **ATTRACT** (playbook A4, House Book deck II): 25 s seated with nothing running and the cabinet attracts itself.
  The reels drift (about a cell every 3 s, the three drums at slightly different speeds), one chase sweeps the
  bulbs every 8 s (brightness only, 1.4 s a pass), EMI winks at 4 s and then every 12 s. Nothing lands: the stops
  never move, no SP moves and nothing is read from the tape. The lever keeps breathing (Law III: the drifting
  reels are scenery, attract is not a celebration). Lever, freeze, Back, Escape, any key or any pointer press
  ends it on that frame (Law VIII) and the reels ease home over 420 ms, never a thud (Brake 1, Law XI). Off on
  Calm, off under reduced motion and off while `meltLeft > 0` (Brake 5). One `setTimeout` re-armed on input
  (no polling) plus the self-re-arming wink; both are cleared by any input, by `suspend` and by `close`, and the
  drift itself rides the render loop, so `destroy` leaves no rAF and no timeout alive.
- **THE EMI LAND-WIGGLE** (playbook A5): EMI landing on any reel, a losing spin included, wiggles that cell once
  after its own thud (`scene.wiggle(i, 340)`: two oscillations, 300 ms, 0.17 of a cell, back to the same stop) and
  EMI's HUD face glances. It rides after the thud and holds no timer, so the next reel's thud is untouched
  (Law X). A 3-EMI line is already THE REVEAL, so that spin wiggles nothing (Brake 2: one hero per beat). Calm
  keeps it (it is tiny); reduced motion takes the settled state: no wiggle, no glance. `feel.emiLandings(outcome)`
  is the pure list of reels, and a held column never thuds, so a frozen EMI never wiggles either.
- **Law IX / THE MARQUEE**: tier 1 chime + win chase, tier 2 two notes + EMI jolt + `WIN +N` glow on
  screen_jackpot, tier 3 THE THUD on screen_jackpot + big chase, tier 4 THE REVEAL (EMI over-rotates, jackpot
  counts up 620 ms, sparks, gold). `marquee_glow` heat tracks the tier (in 80 ms, out 480 ms), gold at the top.
- **The Brake**: overlapping parties and pays merge (one hero per beat); Brake 3 fanfare x3, then chime + bead,
  from the 40th a thud and tokens; melted = no ceremonies, ladder down an octave, EMI slows; the jackpot REVEAL
  once per sit-down; a no-pay spin gets THE SHIVER (+-4 px, 250 ms) and a muted last thud; no move over 620 ms
  but the jackpot hero; chase steps never faster than 6 Hz; every value is also text.
- **THE SPINE** (CONTRACT 10.22.C, the reward pass). Every sentence above still holds; what changed is WHERE it
  is decided. `shared/win/plan.js` owns Law IX and Brakes 2, 3 and 5 for all four stations, and this one asks
  rather than re-derives:
  - `land()` builds ONE plan a landing: `sitPlan(houseTier({ station: 'slot', tier: tierOf(o) }), sit, { reduced,
    lite, still, melted })`. `plan.bank` is the token count, `plan.partyMs` the rollup AND the ladder's span,
    `plan.shower` what `ctx.revealedWin` is told, `plan.sparkle` and `plan.glow` the two new moves. The station
    may spend LESS than the plan; it may never spend more.
  - Brake 3's memory is the spine's `freshSit()` ledger, one per `open()`, `seen` per RUNG. `afterParty` replaces
    it (never mutates) for every party that played, and counts a hero only when THE REVEAL actually fired - so a
    jackpot that was quieted does not spend the sit-down's one.
  - `feel.recipe` still says what the CABINET does, and asks `winPlan` BARE (no reduced, no Calm, no lite) for
    which restraint applies. A motion level strips the decoration on top of the recipe; it does not change it.
  - `reduced` and `still` are two flags. Reduced motion is the settled state, `bank: 0` and `partyMs: 0`. Calm
    (`stillFx()`) strips the shower and the sparks and KEEPS the tokens flying: a number that just changes is a
    Law XII break at every motion level.
- **10.22.B the announcement**: `ctx.revealedWin(o.pay, plan.shower, 'WIN +N')`, once, on the landed line's own
  frame. It is SKIPPED when `plan.shower` is 0 - a tier 1, a melt, Calm, reduced motion - because
  `room/coin-shower.js` clamps 1..4 and would otherwise throw a shower at a two-spiral line (Law IX).
- **10.22.D THE SPARKLE BURST**: `counterfx.sparkBurst('.slot-callout', { count: plan.sparkle })` on the first
  callout of the landing frame, 7 sparks at tier 3 and 9 at the jackpot. One burst a moment (Brake 2), never on
  a small win, never on Calm, lite, a melt or reduced motion - `plan.sparkle` is 0 for every one of those.
- **10.22.D THE GLOW**: `counterfx.warmGlow` on the SP chip as THE BANK's last token lands, sharing the
  mini-thud's frame (Law X), for `plan.glow` (480 ms). Calm keeps it - a warm cut is not travel - and it is 0
  while melted (Brake 5) and under reduced motion. A spend never glows: Law IX sizes a party, and money leaving
  is not one.
- **Law VI**: reduced motion takes the state (reels on their stop at the thud frame, no tokens: the readout is on
  the settled value with a lit ring, no shiver, heat steps; no rollup, so no chime climb either, and the payline
  frame is steady for the reveal beat with no pulse). Back and suspend skip every ceremony to settled.
- **A1 THE ANTICIPATION REEL** (playbook Tier A, CONTRACT 10.15): a live pair on reels 1 and 2 (the same gif 900 ms,
  two spirals or two subs 1,100 ms, two EMI 1,400 ms and gold) keeps reel 3 blurred past its normal stop before THE
  THUD. From reel 2's thud a tone climbs (`sound.rise`, a synth sweep), the marquee takes the `tease` mood
  (`tease_gold` on the EMI pair) and the bulbs drop to 0.62 emissive; THE BREATH is paused by the spin already.
  Brake 5 halves the hold while melted and never lets it go gold. Reduced motion and Calm KEEP the hold and the tone
  and drop the light change only: the hold is pace, not animation. A frozen reel 3 never holds. The outcome is on
  the tape before a reel moves, so the hold is purely a delay (`feel.anticipation`, `pace.ANTICIPATION`).
- **A2 THE ALMOST on the strip** (CONTRACT 10.15): a no-pay spin under a live pair, where the cell one above or one
  below the payline on reel 3 would have completed the line, ghosts that cell gold and snaps it back once (620 ms,
  gold in over 500, one 120 ms snap), on the same frame as the muted thud and THE SHIVER (Law X) with a ghost note
  under it. Once a spin, the cell below read first. The reel window shows about half of each neighbour (drum r 0.43,
  13 cells, window 0.39 tall), so nothing is nudged. Reduced motion: one 120 ms gold tint on the reel, no repaint.
  `feel.almost` reads the server's own strip and stop: no stop weighting, ever. Natural rate on the strips (tables v5 and v6): 9 of the
  2,197 uniform rows, gif and EMI pairs only (a sub or spiral pair already pays).
- **B1 THE SPIRAL JAR** (playbook Tier B, CONTRACT 10.16.A): the melt is the carried debt, the jar is the carried
  credit. A narrow upright tube beside the cabinet fills one notch per spiral SHOWN, on that reel's own THUD
  (Law X, never before it and never all at once), and at `table.jar.size` it spills `table.jar.free` free spins.
  The size is the owner's decided **100 / 3**: a jar is a come-back reason, not a rhythm (about 155 outcomes, 10
  minutes of play). Law I throughout: the count is `outcome.jarN`, the server's own, and the tube's last tick is
  that number whatever the page's arithmetic said (`feel.jarPlan`); the page keeps no copy of the size or the
  count. A freeze earns nothing and neither does anything a freeze expanded into, which the tape says plainly by
  carrying the same `jarN` back, so a held spiral can never farm free spins (it is the same seal that keeps
  `rtpFrozen` at exactly 1.0200). There is no glb node for a jar and no model request is allowed (section 9.6),
  so `.slot-jar` is DOM on live projected bounds, exactly the pattern the payline frame uses: `scene.jarRect()`
  takes `payout_tray`'s projected middle (the cabinet's own box when the tray is absent) at the CABINET's left
  edge in screen space, one `reel_window` tall, every frame. Nothing is added to the glb, no new material and no
  new geometry. **Decision the contract could not know**: the play camera frames the marquee and the reels, so
  the tray's projected middle is BELOW the viewport at 16:9 (measured at 1280x720: y 930 of 720), which would
  have left the tube off screen. Brake 9 says the count has to be readable, so the tray is where the tube wants
  to stand and the canvas is where it has to: it is held inside the canvas and never lower than the reel
  window's own bottom, which reads as a jar standing beside the reels. Brake 9: the count is printed inside the tube (`17 / 100`), so it survives motion level 0.
  A spill is a tier 2 party (`feel.jarParty`: two notes, a jolt, a chase, the screen) plus `fx.spiral_full`, and
  THEN the `kind: 'jar'` outcomes play exactly as free spins do, one lever press each. Brake 2: an outcome that
  also won tier 2 or better keeps its own party and the jar's note is dropped. Brake 5: melted takes the melt
  party. Brake 3 counts the spill against the tier 2 party budget. Calm: the fill only, no party at all, though
  `fx.spiral_full` still fires at its own Calm recipe. Reduced motion: the settled fill and the settled count,
  no travel (the CSS transition is off under `[data-reduced]`) and no flash.
- **B3 THE WELCOME-BACK COMP** (CONTRACT 10.16.C): EMI hands a returning player five spins on the house. There is
  no ceremony, because arriving is not an earned moment (Brake 1): on sit-down the HUD status line reads "On the
  house: {n} spins", a `.slot-comp` chip sits beside the SP readout until it is spent, EMI takes the `hearts`
  face for one `glanceHoldMs` and ONE chime sounds. No fanfare, no REVEAL, no tokens. The next tape BUY of the
  sit-down is the comp (`request('tape', { comp: id })`, no `count`), and the spin button's `<small>` reads
  "Free" for exactly that one press; every buy after it is a normal paid tape at `defaultTapeCount(sp)`. It costs
  0 SP, so `insufficient` cannot happen and a player at 0 SP can still play, which is the whole point. The five
  spins are drawn from the NORMAL plain table: melt still halves them, they still consume melt in draw order and
  they can still land the malus (a gift, not a cleanse), they fill the jar and they expand into free spins,
  re-spins, jar spins and an `emi_respin` like any paid spin. `comp_none` and `comp_used` fall back to a paid
  tape on the spot; `tape_unplayed` leaves the comp standing (finish the tape you have) and the offer waits.
  10.16.C now says this outright: the comp is spent on the first tape BUY of the sit-down, not on the first
  press, because a press that only plays an outcome already on the tape buys nothing and must not burn a comp.
  A refused buy does not eat it either.
- **C1 THE EMI PAIR FREE RE-SPIN** (CONTRACT 10.16.D): two EMI on reels 1 and 2, and reel 3 comes back for a
  second look. Two beats, ONE press. The `emi2` landing plays first with A1's EMI-pair anticipation already on it
  (1,400 ms, bulbs gold, halved and never gold while melted, Brake 5) and lands as a muted thud and nothing else:
  `feel.recipe` answers `tier: 0, party: 'hold'`, with NO SHIVER and NO ALMOST tell, because A2 is the release
  when the pair MISSES and here it has not missed yet. Then, with no second lever press and nothing bought (the
  outcome is already on the tape, queued by the server), reels 1 and 2 stay exactly where they are and reel 3
  alone spins again for the **full 1,400 ms gold hold, never halved, not even while melted**: Brake 5's halving
  in 10.15 applies to A1's anticipation, not to this beat, because this beat IS the event. Then THE THUD, and on
  `emi3` THE REVEAL plays exactly as any jackpot does (Law IX, once per sit-down). Reel 3 is alone, so it takes
  no stagger (`pace.respinStopMs`: `SPIN_MS` then the hold) and the rising tone runs from the start of its travel
  instead of from a reel 2 thud that never comes. Reduced motion and Calm keep the hold and the tone and drop the
  light change, exactly as A1 does. The published `emi3` row prints the TOTAL jackpot odds (1 in 6,494, both
  paths), never the direct draw alone, and the `emi2` row says what it buys: "One more look".
  Both of these were open questions in the contract and are now settled in it (10.16.D): the re-spin stays GOLD
  while melted as well as unhalved, because Brake 5 governs A1's anticipation and not this beat (A1's own melted
  EMI hold is still halved and still never gold), and the beat is **4,660 ms**
  (`SPIN_MS 1800 + 1400 + THUD 340 + REVEAL 600 + BREATH 520`), which is what
  `pace.outcomeMs(PACE, 0, 'emi_respin')` returns; the contract's earlier "about 4,380" was a bad total.
- **Table v7, and what the mock does with it** (CONTRACT 10.16.A, 10.16.D). `mock-server.js` carries the
  server lane's FINAL solved numbers (CCP-Server #176) in one block: per million, `emi3` 92, `emi2` 8,207,
  `gif3same` 6,784, `sub3` 8,479, `spiral3` 8,479, `gif3` 83,746, `sub2` 67,829, `spiral2` 67,829, `melt`
  58,900 and `none` 689,655, plus `respin: { emi: 7555 }` and `jar: { size: 100, free: 3 }`. The factor is
  0.977450 (the decided 100 / 3 row) and `gif3` is then polished ALONE to land the exact 1.0200
  (83,759 -> 83,746), the same move v6 made on v5. The published jackpot is the TOTAL of both paths,
  **1 in 6,493** (154.004 per million). The mock still DRESSES everyday lines off `STRIPS`, which is what the
  page's A1 and A2 tells are sized against, so these weights are shapes and published odds for the page, not
  a paytable, and nothing on the page reads an RTP. **The mock carries no frozen tables at all**: a sealed row
  is dressed off the same strips and only its LINE is overridden (`melt` and `emi2` both read `none`), so the
  server's five conditional tables (four unchanged from v6, the held-SPIRAL one retuned in #176 because its
  sealed free spins read the plain table: `spiral2` 173,593 -> 177,589 and `sub2` 86,779 -> 88,795, `spiral3`
  pinned and the 2:1 kept) have nothing here to land in. Held-class RTP is the server's to prove.
- **The expansion queue's order.** An `emi_respin` goes to the FRONT, so it plays immediately after its parent
  even when other spins are already queued; line free spins keep their place; jar spins queue BEHIND them. A
  `spiral3` that also fills the jar therefore drains `free, free, free, jar, jar, jar`. A jar fill on an
  `emi_respin` spills like any other, because the re-spin descends from a plain-band spin.
- **Skip to settled** (Law VI, Brake 7): one lever press, Back, suspend or a new spin puts a running rollup
  straight on the settled value. `bank.skip()` reads the run's `settled` (the tape's number), never the tick
  ladder, so a cut-short count can never leave the readout short or count a value twice; it is idempotent, and
  `onDone` then hands the readout back to `tape.snapshot().shownSp`. A press takes the whole settled state
  (`skip({ land: true })`: the mini-thud, the readout THUD and the `+N`), hushes the rest of the chime climb and
  sends the frame into its fade. Back and suspend settle the number and leave quietly (leaving is a fade).

## Lexicon keys this lane adds

English fallbacks ship in the page (Law VII); the integration pass adds them to `Localization/Languages/en.json`
and the host sends every `br_*` key in `init.lex` (10.13). This lane touches no C# and no `en.json`.

| Key | English |
|---|---|
| `br_slot_jar_count` | `{n} / {m}` (the count inside the tube) |
| `br_slot_jar_full` | `The jar spills: {n} free spins` |
| `br_slot_jar_odds` | `The jar pays {n} free spins every {m} spirals.` |
| `br_slot_comp` | `On the house: {n} spins` |
| `br_slot_comp_chip` | `On the house` |
| `br_slot_comp_cost` | `Free` |
| `br_slot_line_emi2` | `2 EMI` |
| `br_slot_respin` | `One more look` |

`br_slot_jar` ("Spiral jar") from 10.16.A is NOT used: the tube is `aria-hidden` and carries its own count as
text, so there is nothing for a label to add. It is left to the integration pass to drop or keep.

## dev.html query flags

| Flag | What |
|---|---|
| `?sp=57` `?melt=0` `?floor=3000` `?latency=120` | as before |
| `?reduced` `?variant=violet\|mint` | as before |
| `?jar=97` | seeds the stored spiral jar (0 .. size - 1, clamped like `ensureSlot`) |
| `?comp=1` | mints a welcome-back comp; `?comp=3` mints one of 3 spins |
| `?lever=A..D` `?reel=A..D` | the lever and the drum voice (shared/sound/README.md); without them, the saved `br.sfx.variant` pair, else lever B over reel A |

Dev buttons: **next: 2 EMI (re-spin)** scripts `emi, emi, melt` (the chase that does not start the melt),
**next: 2 EMI, re-spin hits** also forces reel 3 back as EMI, **jar to 99** puts the jar one spiral from
spilling, **give a comp** stores one (sit down again to be handed it).

## Follow-ups

- The room's unattended cabinet marquee (attract seen from across the 3D room, playbook A4) is NOT part of this:
  the station page owns only the seated cabinet. Room-side work is a separate pass.

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

## The flow on the landing frame (shared/hypno/callout.js, 2026-09-15)

Law I: the tape holds the outcome before a reel moves, so everything below is dressing on a known row. `feel.flowPlan`
decides (pure, node-tested), `station.js flow()` schedules, `scene.highlight` lights. The timings are the shared
module's constants (`HIGHLIGHT_MS` 400, `HIGHLIGHT_GAP_MS` 80, `FX_DELAY_MS` 400, `CALLOUT_MS` 1600), never the slot's.

| ms from the landing | What |
|---|---|
| 0 | THE THUD (unchanged); the winning glyphs start glowing in reel order, 80 ms apart (rim pulse + 6% cell pop on the drum canvas; the root carries `br-glyph-hit` for the window) |
| 400 | the callout `.show()` and the row's own host fx fire TOGETHER (`fx.*` ids moved here from the landing frame); the GIF tease's flash rides the same frame |
| 2000 | the next spin may start on a paid line or a row with a word (a loss keeps today's 1120 ms); the jackpot holds 3400 ms |

| Line | Glyphs lit | Callout (key, tier) | Unlock |
|---|---|---|---|
| `none` | none | none (a single sub is the other lane's word) | 0 (quick) |
| `sub2` | the two subs | `br_callout_echo` Echo, small | 2000 |
| `sub3` | all three | `br_callout_chorus` Chorus, big | 2000 |
| `gif3` | all three | `br_callout_picture_show` Picture Show, small | 2000 |
| `gif3same` | all three | `br_callout_storm` Storm, big | 2000 |
| `spiral2` | the two spirals | `br_callout_double_spin` Double Spin, small | 2000 |
| `spiral3` | all three | `br_callout_sinking_down` Sinking Down, big | 2000 |
| `melt` | the melt cell | `br_callout_brain_melt` Brain Melt, big | 2000 |
| `emi2` (the chase) | reels 1+2 | `br_callout_emi_chase` Emi Chase, big, before reel 3's re-spin | 0 (the re-spin follows on the pace's beat) |
| `emi3` | all three | `br_callout_emi_jackpot` Emi Jackpot, hero | 3400 |
| the jar fills | (the jar's own tick) | `br_callout_overflow` Overflow, big, on the tick that fills it; a small landing word yields to it | as the row |
| a `respin` row (spiral2's grant) | as its line | `br_callout_respin` Respin, small, on the landing frame; its own word replaces it at 400 ms | as its line |

- **THE GIF TEASE** (owner: a couple of points on the GIF visuals, the economy untouched): a row reading `none` that
  shows exactly two GIF symbols fires ONE host GIF flash, `fx.gif_burst` with `args { count: 1 }` and the two GIF
  keys, at 400 ms. No callout, no SP, no pay. Rate on the mock strips: about 1 row in 6 (see the PR).
- **A1's hold** (reels 1 and 2 a live pair, reel 3 still travelling, CONTRACT 10.15's 900-1,400 ms) now also pulls
  `ctx.fxTunnel` to 0.4 with the riser and stretches reel 3's slow-down across the hold (it crawls into its stop);
  the landing frame releases the tunnel. Page dressing of a known row, no host fx.
- **THE ATTRACT HAZE**: 8 s idle (the drift's own gate: seated, quiet, not melted, not Calm, not reduced motion)
  breathes a spiral plane behind the cabinet at 12% over 6 s (`scene.haze`) until the next press; off while a
  result plays, off on suspend and Back.
- The callout is created once per open (`createCallout({ mount: .slot-callout, lex: t })`), the layer sits at
  z-index 7 so a phone in landscape never puts a reel over the word, it is cancelled on suspend and close (Law VI),
  disposed with the station, and `debug().callout` / `debug().flow` carry what showed and when.
