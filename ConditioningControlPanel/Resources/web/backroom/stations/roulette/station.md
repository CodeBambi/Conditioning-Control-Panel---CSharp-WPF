# Velvet Vortex roulette station

Single-zero roulette for SP (CONTRACT.md sections 2-7 and 10.13, binding spec `hypno-spins-v3.html`). Entry
`station.js`, loaded by the room from `stations.json`. No glb with a page node contract exists for the roulette
(the room's `roulette.glb` only drives the idle hub), so the station draws a **2D canvas bowl and mat** (10.13.F).

| File | What |
|---|---|
| `station.js` | `mount(ctx)` -> `{open, close, suspend, destroy}`. DOM, the chip count, the spins picker (1-5), Spin, tape playback (about 8 s a spin), the cursor, the moments. |
| `tape.js` | Pure: chips to bets, the client cover-all check (only disables Spin, with the server's word `covers_all`), Law I `shownSp`, reading an outcome for the text, `classify` (retry with the same idem, adopt `tape_unplayed`, refusals). |
| `flick.js` | Pure: THE THROW. An angular drag on the wheel -> one signed rotor speed for `planRun`'s `rotVel0`, or nothing. Minimum travel, minimum release speed, a stale grab, the direction, the clamped band. |
| `glyphs.js` | Pure (GLYPHS.md): THE POCKET GLYPHS, four faded marks on the wheel keyed by the pocket number (`glyphFor`, `glyphFx`), one section 4 id each; `paintGlyph` draws one on a canvas. |
| `feel.js` | Pure: outcome -> moment id, the Lighthouse clock (law 4), the ball's run planned backwards from `outcome.pocket`, timings, the host recipe (`FX_RECIPE`: beat -> section 4 ids, gates, Calm, the streak, cooldowns). |
| `bowl.js` | The canvas bowl: drifting rim cache, rotor, pockets, lighthouse, the run, fret rattle and sparks, turret whirl (Loom), velvet wake. |
| `mat.js` | The canvas mat (37 straights, sip, sink, deep, rose, plum) and the chips: chip vortex, chips in, the pulled pair. |
| `bowl-3d.js`, `mat-3d.js` | The seated view on the room's fixture (below): the authored bowl driven by the same core, the authored mat with printed cells, pick planes and live chips. |
| `mock-server.js`, `dev.html` | Standalone harness on the 10.13.E shapes with seeded fixture outcomes (not the table) and the kit's mock host. Not shipped behaviour. |
| `tests/*.test.mjs`, `tests/roulette-check.mjs` | Node tests; the headless check (`ROULETTE_PORT` default 8899, debug +500). |

## Server API (binding, CCP-Server `backroom-roulette-routes.js`, 10.13.E)

- `GET state` -> `{ ok, sp, open, tape | null (only while unplayed), table: publicTable(), wheel, rose, spots, floorMs }`.
  Pocket order and colours are painted from `wheel` and `rose`, never a page copy. The Odds panel prints `table.kinds`.
- `POST spin {idem, count, bets:[{spot, amt}], cursor?}` -> the receipt `{ ok, idem, sp, spBefore, cost, won, capped,
  tape:{id, bets, played:0, outcomes:[{i, pocket, color, wake, pay, fx}]} }`. `outcomes[].fx` is not fired (v3).
- `POST cursor {tapeId, played}` -> `{ ok }`. Sent when a tape has played out and on close; also folded into the next spin.
- Refusals: `bad_layout` + `why` (text, `covers_all` in the player's words), `tape_unplayed` (the page adopts the tape
  and plays it; nothing was bought), `too_fast` (waited once when under 6.5 s, else text), `insufficient`,
  `bad_request`, `busy` (retried with the same idem), `closed` (403, a card). A host `timeout` retries with the same idem.
  At most three sends per press.
- Host Ops row (H1): `["roulette"] = { ("GET","state"), ("POST","spin"), ("POST","cursor") }`.

## The spin

- **Bets.** Click a spot to add 1 SP (right click or Shift takes one off), 3 SP a spin in all, 1 to 5 spins (buttons or
  keys 1-5), Clear (or Backspace). The Spin button stays off with a reason for an empty layout, a cover-all layout
  (rose + plum, sip + sink + deep) and a cost above the shown balance; the server decides everything else.
- **THE THROW.** The wheel is spun by hand: press on it, swing, let go (`flick.js`). The mat has first claim on a
  press, so a chip is still a chip; what is left over, on the wheel, is a hand on the rotor, and the wheel follows the
  finger. On the lift, a swing past `MIN_TRAVEL` that is still moving past `MIN_OMEGA` sends the SAME request the
  Spin button sends; a shorter, slower or stale one just leaves the wheel turning. Before the bets are down the hand
  may still push the wheel round, and nothing is sent. Pointer events, so a finger throws it too; the press is
  `preventDefault`ed and both canvases are `touch-action: none`, so no scroll and no look-around.
- **The strength.** The flick's direction and speed become `rotVel0` for the FIRST launch of the tape it bought,
  clamped into `FLICK.VEL_MIN..VEL_MAX` (either sign; the house's own kick, 1.5, sits inside it). Law I: `planRun`
  simulates forward from it and then turns the whole ball path onto the server's pocket, so a hard throw and a soft
  one land in the same place and take the same time. The spins after it leave on the house kick.
- **The hint.** While the throw is armed (exactly when the Spin button is live) a curved, semi-transparent arrow sits
  around the rim the way the rotor turns, breathing on a 1.2 s cycle: a `TorusGeometry` arc and a flat head on the
  rotor, counter-turned so it stands still in the room (`bowl-3d.js`), and the same arrow stroked on the canvas
  (`bowl.js`). It is gone while the wheel spins, while a hand is on it, and before the bets are down. Still (Calm,
  reduced motion): the arrow holds, no breath.
- **Law VIII.** The throw, Spin, Space or Enter: the rotor picks up and the button rings on the press frame. The Spin
  button is the keyboard's and the screen reader's way in and reads as the second one; the throw is the first.
- **Law I.** The receipt's `sp` is adopted at once; the SP chip (`ctx.spReadout.owe(reader)`) owes every unplayed pay,
  so the stake leaves on the reply and each spin's pay lands on the frame the ball drops into its pocket.
- **Playback.** Each spin: `roulette.run` (and `roulette.wake` on a Spiral Wake) at launch; `moments.tunnel(rouletteRunLevel(ball speed))`
  each frame while the ball runs; exactly one `roulette.land.*` on the landing frame (`pocketColor`, the pocket as a 40 x 30
  viewport box, `deck.pickKey(tapeId + ':' + i)`). The next spin launches 8 s after the last one (at least 1.5 s after
  the ball rests).
- **The run is planned backwards.** `feel.planRun` simulates the mockup's run, drop and rattle forward on a seed from
  the tape id and spin index, keeps a run with two or three fret clips that rests within 6.4 s, then turns the whole
  ball path by a whole number of pockets so it settles in the server's pocket. Frets stay frets; the landing never
  changes; a reopen replays the same choreography.
- **Back** (Law VI) cancels the moments (tunnel 0, holds released), flushes the cursor for the spins that landed and
  hands the room a plain owed number. A spin still running stays unplayed; reopening shows "Watch the rest".

## Effects (10.13.F)

| Effect | Rule here |
|---|---|
| Drifting rim | two print rings drawn once into an offscreen canvas per radius and DPR, blitted each frame |
| Lighthouse turret | `beamAngle = -0.7 x t` on the station clock (never the rotor), half-width 0.24; the check asserts at least 30 numbers lit in 10 s (headless: 37) |
| Fret rattle | slow motion 0.42 (Calm 0.7), up to three clips, a single 0.5 s spark per clip at least 340 ms apart, "s l o w l y" |
| Velvet wake | Full only (the `velvet_wake` page effect), 1.3 s settle |
| Turret whirl | a wake spin: `kit.draw('whirl')` clipped to the dish at 0.85 x fade x k, `angle = rotor x 2.2`; the arms trail the rotor (`a = base - 1.1 u`); `spiral` gate off: velvet dish and a gold rim glow |
| Chips | a lost chip spirals into the bowl; a win slides two chips in per winning spot; a `pulled_pair` (wake win) pulls two more out of the whirlpool |
| THE BANK | `plan.bank` tokens leave the paying chips and arc to the SP chip over 560 ms each, 70 ms apart, the readout ticking per landing (THE REWARD below) |
| Host (moments) | through `createMoments`: `fx-tunnel`, `fx.haze {hold}` at Full, `fx.loom_spiral {wake, hold, 0.65}`, `fx.wash` in the pocket colour (0.6 or 1), `fx.gif_from {from pocket, ms 3600}` |
| Host (recipe) | `feel.FX_RECIPE`, fired through `ctx.fx` the way the slot fires section 4 ids (`beat()` in `station.js`), never awaited; on a landing the recipe fires first so the moment's wash and pocket GIF close the frame |

**The host recipe** (`feel.fxPlan(beat, {gates, calm, full, streak})`, `tests/feel.test.mjs`):

| Beat | When | Host ids (Normal) | Notes |
|---|---|---|---|
| `nomore` | the press frame, before any reply | `fx.sub_single` (one word key) | Law I: the same for every press |
| `launch` | each spin's launch | `fx.gif_burst` (the spin's picture key) | wake or not, the same (Law I); Calm strips it |
| `wake` | a Spiral Wake's launch | `fx.sub_single` | the spiral hold itself is `roulette.wake` (moments); the wake is on screen as text from this frame |
| `rattle` | the ball's first fret clip (phase `rattle`), once a spin | `fx.sub_single` | |
| `run` | the ball run | none here | `moments.tunnel(rouletteRunLevel)` each frame |
| `near` | a miss with a straight chip on a WHEEL neighbour of the pocket (`nearMisses`, from `state.wheel`) | `fx.spiral_brief` | on top of the glyph |
| `glyph` | every landing, on the thud frame (Law X), win or lose | the landed pocket's glyph (`glyphs.js`): spiral `fx.spiral_brief`, eye `fx.gif_burst`, bubble `fx.sub_pair`, drop `fx.melt`, 0 nothing | the mark on the wheel goes hot on the same frame; the callout still names the pay |
| `land.miss` | `pay === 0`, no near miss | none | the page's chip vortex |
| `land.win` | an outside bet pays, no wake, no straight | none of its own (the glyph is its effect) | plus the moment's wash 0.6 |
| `land.straight` | a straight-up hit | none of its own (the glyph again) | plus the moment's wash 1 and pocket GIF |
| `land.wake` | a woken win, no straight | `fx.spiral_full` | the turret's spiral goes full |
| `land.full` | a straight-up hit on a wake | Full: `fx.jackpot` (the hero); below Full or Calm: `fx.sub_cascade` | Brake 2: the streak storm stays off the hero's frame |
| `streak` | rides any paying landing at 2+ paying spins in a row (a miss resets) | `fx.gif_storm` | Calm strips it |
| `skip` | Back, suspend | none | Law VI: cooldowns reset, `moments.cancel()` releases the holds and the tunnel, the host lets one-shots settle |

- **Gates** (page side, the host still enforces per primitive): `flash` -> `gif_burst`, `gif_storm`; `subliminal` -> `sub_*`; `spiral` -> `spiral_brief`, `spiral_full`; `fx.jackpot` fires while any of the three is on. A gate the host does not send reads as on.
- **Calm / reduced** strips the motion steps (`gif_burst`, `gif_storm`, `jackpot`) and keeps the words and spirals at Normal values (the host halves, law 6).
- **Cooldowns** (`FX.COOLDOWN_MS`, one clock per id): `sub_single` 4 s, `sub_pair` 8 s, `spiral_brief` 6 s, `gif_burst` 6 s, `sub_cascade` 12 s, `spiral_full` 12 s, `gif_storm` 20 s, `jackpot` 60 s. The rattle word is once a spin.
- **Symbols**: gif steps carry `deck.pickKey(tapeId + ':' + i)` (the same 4-GIF deal as `fx.gif_from`); word steps carry `s<n>` keys turned by the spin index (the host resolves them against its own dealt words, a missing one is a random dealt word). Nothing else ever goes back.

- **Calm / reduced** (`ctx.intensity === 'calm'`, `ctx.reduced`, or `prefers-reduced-motion`): page strengths x0.5,
  the rotor eases to a stop and the beam holds at rest, the rattle floor is 0.7, chips appear or fade where they end
  (no travel). The ball still runs its plan (the mockup's reduced motion). Host args stay Normal (the host halves).
- **Gates** are read live every frame: `flash`, `brainDrain` (haze) and `tunnel` (the run's fx-tunnel) off simply drop host steps (moments); `spiral` off keeps
  the dish velvet. A Spiral Wake always shows as text.
- **Suspend** cancels the moments, resets the recipe's cooldowns, pauses the station clock and disposes the Loom kit and the deck (their keys still
  pick); resuming makes a new kit and replays the running spin's holds (the recipe fires nothing on a resume: its one-shots settled).

## THE REWARD (CONTRACT 10.22)

A paying landing is a party the house sizes, not a number that changes. Before this pass the station took only
`ctx.spReadout.owe()`: a pay made the SP chip redraw where it stood, which is Law XII broken outright, and the
win voice was one flat `sound.play('win', { tier })` by band whatever the win, the streak or the sit-down.

Every SIZE comes from `shared/win/plan.js` and nowhere else. The station's own half is the bottom of `feel.js`
(pure, `tests/reward.test.mjs`) and `bank.js` (the elements); `station.js` only plays what the plan bought.

| Piece | Where | Rule here |
|---|---|---|
| The rung | `feel.rewardTier(read, near)` | `houseTier({ station: 'roulette', moment: landBeat(read, near), pay })`. The roulette has no numeric recipe: its rung IS its callout size (`CALLOUTS` - small 1, big 3, hero 4), RAISED by the pay (`tier.PAY_STEPS`), which is the only way this table reaches a bare 2. A miss and a near miss are 0; the room is never told about either. |
| THE BANK | `bank.js` on `shared/win/bank.js` | `plan.bank` tokens (3-7, 4 on lite, 0 under reduced motion) leave the PAYING CHIPS on the mat, dealt round-robin by `feel.tokenSpots` so every chip that paid sends something, and arc to `ctx.spReadout.target()`. The chip ticks on each LANDING (Law X) and thuds on the last, after the count. `plan.partyMs` is the rollup. |
| THE CHIME LADDER | `shared/win/ladder.js` | `sound.play('win', { tier: winSound(plan.spent), semis: ladderRoot(streak) })`, then `ladderPlan(plan.spent, plan.partyMs)` minus its landing note through `sound.play('ladder')`. A streak raises the root a semitone a spin, capped at 7. |
| THE GLOW | `counterfx.warmGlow` | every paying rung, 480 ms, on the +N badge. Calm keeps it; a melt pocket and reduced motion do not. |
| THE SPARKLE BURST | `counterfx.sparkBurst` | `plan.sparkle`: 7 sparks at a 3, 9 at the hero, never under Calm, lite, melt or reduced motion. |
| THE REVEAL | `.roul-gain[data-reveal]` + a 620 ms `animate()` | `plan.reveal`: the first Full Wake of a sit-down. The second is a very good tier 3 (`why: 'capped'`). |
| The room | `ctx.revealedWin(pay, plan.shower, '+N SP')` | 10.22.B. Once a result, never on a miss, never before the player knows (Law I). `plan.shower` is 0 for a small win and under Calm, lite and melt, and `room/coin-shower.js` clamps a tier up into 1-4 - so a 0 SKIPS the call. That guard is the station's. |
| The +N | `.roul-gain`, `br_roulette_gain` | Brake 9: the value is text before it is anything else, and it stands for the whole count. |

- **Brake 3** is `freshSit()` / `sitPlan()` / `afterParty()`, one ledger per SIT-DOWN (reset in `open()`, so standing
  up and sitting back down is a new one). `seen` is per RUNG, not per station. `afterParty` counts a hero only when
  THE REVEAL actually fired, so a Full Wake under Calm does not burn the once-a-visit hero.
- **Brake 5** at this table is THE POCKET GLYPHS: a landing on a `drop` pocket (`feel.meltedBy`, nine of the
  thirty-seven) fires `fx.melt` on that very frame, so the beat the melt arrives on is a melted beat. The rung
  drops to 1, the ladder flattens to one note an octave down, the shower, sparkle, glow and reveal all go - and
  the tokens still fly. A melted win is quiet, never invisible.
- **Law VI.** `reduced` and `still` are passed to the plan SEPARATELY. `reduced` is reduced motion: the settled
  state, `bank: 0`, `partyMs: 0`, and `bank.js` hands that straight to the engine, which takes the state and the
  cue instead of a faster flight. `still` is Calm or Motion off: the decoration goes and the value still moves.
  Back, suspend and close settle THE BANK QUIETLY (`settleBank()`); only a new spin takes the mini-thud with it.
- **Law X.** The whole party is ONE frame - `landFx` at `FX_DELAY_MS`, where the moment, the host beat, the chips
  and the callout already land together. `feel.nextLaunchAt` takes `partyMs` so the next spin waits the party out:
  a 6 s hero climb is never cut off by a launch, and a `partyMs` of 0 leaves the old hold exactly as it was.
- **The chip is PINNED on the landing frame.** `tape.played` moves there, so the Law I rule would repaint the chip
  a whole pay higher 400 ms before a token has left. `land()` holds it at what it said and THE BANK carries it the
  rest of the way. A Back or a suspend inside that 400 ms window lets the pin go by hand (`settleBank`).
- **Law XIII.** `plan.emi` is decided and logged (`debug().reward.last.emi`). The roulette has no EMI of its own
  over the felt yet, so nothing plays it; the pose is right the day the room gives this table a face.
- **Not spent here.** `mergePlans` (Brake 2 across two parties) is unused: the launch hold means two parties can
  never share a frame, and THE BANK's own `merge()` covers a pay landing inside a flight. `lite` reads `ctx.lite`,
  which the room does not send today.

## Lexicon (`br_roulette_*`, English fallbacks in the page, the same text in `en.json`; `smoke/lexicon.test.mjs` keeps them equal)

`br_roulette_back`, `br_roulette_stage`, `br_roulette_loading`, `br_roulette_closed`, `br_roulette_offline`,
`br_roulette_ready`, `br_roulette_resume`, `br_roulette_watch`, `br_roulette_left`, `br_roulette_spin`,
`br_roulette_spinning`, `br_roulette_no_more`, `br_roulette_waking`, `br_roulette_progress`, `br_roulette_slowly`,
`br_roulette_sp`, `br_roulette_chips`, `br_roulette_clear`, `br_roulette_spins`, `br_roulette_cost`,
`br_roulette_history`, `br_roulette_history_resume`, `br_roulette_pocket`, `br_roulette_pocket_zero`,
`br_roulette_row_sip`, `br_roulette_row_sink`, `br_roulette_row_deep`, `br_roulette_spot_rose`, `br_roulette_spot_plum`,
`br_roulette_spot_sip`, `br_roulette_spot_sink`, `br_roulette_spot_deep`, `br_roulette_mat_sip`, `br_roulette_mat_sink`,
`br_roulette_mat_deep`, `br_roulette_wake`, `br_roulette_won`, `br_roulette_lost`, `br_roulette_why_empty`,
`br_roulette_why_covers_all`, `br_roulette_why_stake_cap`, `br_roulette_why_insufficient`, `br_roulette_why_too_fast`,
`br_roulette_why_bad_layout`, `br_roulette_why_bad_request`, `br_roulette_odds`, `br_roulette_odds_straight`,
`br_roulette_odds_row`, `br_roulette_odds_color`, `br_roulette_odds_pays`, `br_roulette_odds_woken`, `br_roulette_odds_note`,
`br_roulette_glyphs_note` (the four marks, under the Odds panel; the one roulette row carried in all nine locales),
`br_roulette_gain` (THE REWARD's +N badge, 10.22).

## Checks

```
node --test ConditioningControlPanel/Resources/web/backroom/stations/roulette/tests/*.test.mjs
node ConditioningControlPanel/Resources/web/backroom/stations/roulette/tests/roulette-check.mjs [evidenceDir]   (headless Chrome)
```

Dev harness: serve `ConditioningControlPanel/Resources/web` as the web root and open
`/backroom/stations/roulette/dev.html` (`?sp=40&next=17w,5,0&floor=0&calm&reduced&full&gates=off&hook&tape=3`).
`dev.station.debug()` shows the phase, Law I, the tape, the bowl (lit numbers, plan), the mat and the feel log.


## Seated 3D view (CONTRACT 10.18)

When ctx.stage is present, the room renderer owns the bowl and the authored mat (`bowl-3d.js`, `mat-3d.js`):
the station's DOM keeps only the controls, the room camera seats the player over the fixture and the same
`place` / `remove` action takes every tap on the mat.

- **Targets.** `bet_hit_<spot>` anchors provide spot id, hit_width, hit_depth and chip_radius; all 42 become hidden
  pick planes. A ray hit wins; a miss still picks the nearest cell whose 40 px pad (round cells narrower than a
  fingertip) holds the pointer, so a tap just off a narrow cell lands where it looks. There is no DOM betting grid.
- **Prints.** While seated the authored cream glyph mesh (`bet_number_assembly`) is hidden and one 1024 px atlas
  (`roulette_mat_prints`, one draw call) prints bold outlined digits and the outside labels onto the cells at their
  own proportions; leaving restores the glyphs. The wheel's own glyphs are batched by `room/roulette-surfaces.js`,
  grown 1.9x inside their bands with a faint emissive, on every view of the room.
- **Camera.** `room/seat-camera.js` seats a mouse viewport (width > 800 and height > 500) at the table and never
  moves it: THE PC SEAT leans in (`PC_TILT` 1.35, about 53 degrees, from the old top-down 3) and solves the distance
  from two edges instead of fitting the table's box - the mat's near edge sits on the floor of the controls' band and
  the wheel's far rim just under its ceiling, so the board reads across the bottom of the screen and the wheel, the
  flick hint and the room fill the rest. On 1920x1080 that is 2.22 away at [3.27, 2.77, 3.11] (was 2.83 at
  [3.70, 3.67, 3.11]) and the board covers 47% of the width, up from 29%; every bet cell roughly doubles in area, so
  nothing gets harder to hit. A phone (width <= 800 or
  height <= 500) frames the mat while bets are open (`mat.setFrame('mat')`, the wheel above it, its offset side
  cropped) and eases out to the whole table for 700 ms on Spin (`'table'`, every pocket in view), back to the mat
  once bets reopen; `room/scene.js` reads `userData.frame` on `roulette_runtime_mat` and moves only when the pose
  differs. Portrait keeps 150 px clear above and 108 px below for the control bar; landscape keeps 200 px at the
  right for the status and controls column and lets the wheel's far side leave the top.
- Without a stage the canvas view (`bowl.js`, `mat.js`) remains. No new lexicon keys or server operations.

Rebuild the model variant from the separate Blender workspace's
`roulette/add_play_anchors.py`, then use the room asset pipeline with
`--only roulette.glb --asset-source <roulette/play-out/roulette.glb>`.
Runtime resources are disposed by the stage subscription.

Checks: `tests/room-3d-check.mjs` (both phone orientations through the real room: 42 taps, the pick pad, the
camera frames, a landing), `tests/bowl-3d-check.mjs`, and `smoke/seat-camera-check.mjs` for the seated fit.
