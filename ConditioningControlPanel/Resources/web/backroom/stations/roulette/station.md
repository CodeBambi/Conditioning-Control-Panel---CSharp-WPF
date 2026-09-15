# Velvet Vortex roulette station

Single-zero roulette for SP (CONTRACT.md sections 2-7 and 10.13, binding spec `hypno-spins-v3.html`). Entry
`station.js`, loaded by the room from `stations.json`. No glb with a page node contract exists for the roulette
(the room's `roulette.glb` only drives the idle hub), so the station draws a **2D canvas bowl and mat** (10.13.F).

| File | What |
|---|---|
| `station.js` | `mount(ctx)` -> `{open, close, suspend, destroy}`. DOM, the chip count, the spins picker (1-5), Spin, tape playback (about 8 s a spin), the cursor, the moments. |
| `tape.js` | Pure: chips to bets, the client cover-all check (only disables Spin, with the server's word `covers_all`), Law I `shownSp`, reading an outcome for the text, `classify` (retry with the same idem, adopt `tape_unplayed`, refusals). |
| `feel.js` | Pure: outcome -> moment id, the Lighthouse clock (law 4), the ball's run planned backwards from `outcome.pocket`, timings. |
| `bowl.js` | The canvas bowl: drifting rim cache, rotor, pockets, lighthouse, the run, fret rattle and sparks, turret whirl (Loom), velvet wake. |
| `mat.js` | The canvas mat (37 straights, sip, sink, deep, rose, plum) and the chips: chip vortex, chips in, the pulled pair. |
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
- **Law VIII.** Spin, Space or Enter: the rotor picks up and the button rings on the press frame.
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
| Host | only through `createMoments`: `fx-tunnel`, `fx.haze {hold}` at Full, `fx.loom_spiral {wake, hold, 0.65}`, `fx.wash` in the pocket colour (0.6 or 1), `fx.gif_from {from pocket, ms 3600}` |

- **Calm / reduced** (`ctx.intensity === 'calm'`, `ctx.reduced`, or `prefers-reduced-motion`): page strengths x0.5,
  the rotor eases to a stop and the beam holds at rest, the rattle floor is 0.7, chips appear or fade where they end
  (no travel). The ball still runs its plan (the mockup's reduced motion). Host args stay Normal (the host halves).
- **Gates** are read live every frame: `flash`, `brainDrain` (haze) and `tunnel` (the run's fx-tunnel) off simply drop host steps (moments); `spiral` off keeps
  the dish velvet. A Spiral Wake always shows as text.
- **Suspend** cancels the moments, pauses the station clock and disposes the Loom kit and the deck (their keys still
  pick); resuming makes a new kit and replays the running spin's holds.

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
`br_roulette_odds_row`, `br_roulette_odds_color`, `br_roulette_odds_pays`, `br_roulette_odds_woken`, `br_roulette_odds_note`.

## Checks

```
node --test ConditioningControlPanel/Resources/web/backroom/stations/roulette/tests/*.test.mjs
node ConditioningControlPanel/Resources/web/backroom/stations/roulette/tests/roulette-check.mjs [evidenceDir]   (headless Chrome)
```

Dev harness: serve `ConditioningControlPanel/Resources/web` as the web root and open
`/backroom/stations/roulette/dev.html` (`?sp=40&next=17w,5,0&floor=0&calm&reduced&full&gates=off&hook&tape=3`).
`dev.station.debug()` shows the phase, Law I, the tape, the bowl (lit numbers, plan), the mat and the feel log.


## Seated 3D view (draft amendment)

When ctx.stage is present, the room renderer owns the bowl and authored mat.
`bet_hit_<spot>` anchors provide spot id, hit_width, hit_depth and chip_radius.
All 42 hit targets route into the existing place/remove action. Chip-count opens
an optional bet picker for cells outside a narrow viewport; its chips still land
on the physical mat. The camera pose never changes. Without a stage the canvas
view remains available. No new lexicon keys or server operations.

Rebuild the model variant from the separate Blender workspace's
`roulette/add_play_anchors.py`, then use the room asset pipeline with
`--only roulette.glb --asset-source <roulette/play-out/roulette.glb>`.
Runtime resources are disposed by the stage subscription.
