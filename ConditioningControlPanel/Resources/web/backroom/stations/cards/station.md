# Soft Hand (Twenty-One) station

Classic Twenty-One against the house, dressed in the player's own pictures (CONTRACT.md sections 2-7 and 10.13; the
binding look is the owner-approved mockup `hypno-spins-v3.html`). Entry `station.js`, loaded by the room from the
`cards` row of `stations.json`. There is no station glb: the `cards` row's `card-table.glb` is a room copy with no
page node contract, so the station draws a **2D canvas table** filling the station view (10.13.F).

| File | What |
|---|---|
| `station.js` | `mount(ctx)` -> `{open, close, suspend, destroy}`. DOM, controls, the request flow, the step queue, moments, the SP chip. |
| `hand.js` | Pure: `readHand` / `readState` (publicHand), `controls` (which buttons are live), `classify` (what a reply means), `createIntent` + `mayRetry` (one idem per press), `owedFor` (Law I). |
| `feel.js` | Pure: `planSteps` (a reply's hand against the felt -> timed steps), `momentOf`, `isBloom`, `bestCard`, `vortexOf`, `resultLines`, `fanCard`, `lampBreath`, `TIMING`. |
| `table.js` | The canvas: lamp, felt weave, printed arc, shoe, chip spot, hands, and the page effects. |
| `reward.js` | Pure: what a settled hand is WORTH and what its party may SPEND, over `shared/win/` (`settleTier`, `bloomTier`, `settleCue`, `ladderRoot`, `climbSteps`, `joinParty`, `calloutTier`, `showsRoom`). |
| `bank.js` | THE BANK at the table: the tokens, the layer and the rAF loop over `shared/win/bank.js` (the maths, the clock and the event order are the shared engine's). |
| `mock-server.js`, `dev.html` | Standalone harness on the 10.13.E shapes with scripted fixture shoes. Not shipped behaviour, never the server's rules. |
| `tests/*.test.mjs`, `tests/cards-check.mjs` | Node tests; the headless check (`CARDS_PORT` default 8898, debug +500). |

Shared code comes only through the hypno kit (`shared/hypno/index.js`): `createLoomKit` (every card back is the Loom's
`backs` preset through the real `loomField.js`), `createDeck` (13 dealt pictures, decoded by the room's
`gif-decode.js`), `createMoments` (every host effect), `strengthK`, `viewportRect`, `DECK_VALUES`.

## Server API (binding, CONTRACT 10.13.E, CCP-Server `backroom-cards-routes.js`)

- `GET state` -> `{ ok, sp, open, hand: publicHand | null, legal, hint, autoStandAt, rules, floorMs }`. The page
  dropped its basic-strategy hint, so `hint` is ignored wherever the server sends it.
- `POST deal {idem, stake}`; `POST hit | stand | double | split {idem, handId, step}` -> `{ ok, idem, sp, spBefore, cost,
  returned, capped, hand, legal, hint, autoStood?, autoStandAt }`.
- Refusals and what the page does (`hand.js` `classify`):
  - `busy`, a host `timeout`: the same idem again after 650 / 250 ms, at most 3 times.
  - `too_fast`: "Shuffling. The next deal is ready in N s.", then the same idem after `retryInMs` (capped 9 s), at most twice.
  - `stale`, `illegal`, `hand_open`: the returned hand is adopted and animated from what is on the felt.
  - `auto_stood`: the returned hand is adopted quietly (no moments) with a line saying it was stood.
  - `no_hand`, `bad_request`: GET state again. `insufficient`: a line. `closed` (403) and a host `bad_op`: a card, the
    controls close. `offline` and anything else: a line.
- Host Ops row (H1): `["cards"] = { ("GET","state"), ("POST","deal"), ("POST","hit"), ("POST","stand"), ("POST","double"), ("POST","split") }`.
- Rules, odds and pays are the server's (RULES_V1). The page never computes a pay or a legal move; the only
  arithmetic is the display total of face-up cards while the dealer's hand is being turned.

## The table

- **Controls.** Bet chip 1 or 2 SP (`rules.stakes`), starting on 1 below 30 SP and on 2 from 30. Deal (Space or Enter).
  Hit, Stand, Double, Split exactly as `legal` says, shown only while a hand is open. "Stand up, sit back down"
  while no hand is open; it latches on the press (Deal, the moves and a second
  Sit are refused while the 13 pictures are dealt), and each sit owns its deck (the last one is disposed when the new one
  is in; a late deck from an older sit is disposed, never adopted). Moves have no letter keys (the room walks on WASD).
- **Law VIII.** Every press rings its button on the frame. **Law VI.** Back (the room's, or the station's own
  standalone) and Escape work at every frame; `close()` is synchronous and hands the room the plain server number.
- **Law I.** A reply that settles a hand owes its `returned` (never more than the balance really moved) until the
  settle frame shows it; the stake shows at once.
- **Resume.** Opening with an open hand plays the sit fan, then deals the hand out and reopens its decisions. A finished
  last hand, a `no_hand` re-read and an auto-stood hand go down settled on one frame, with no moments.
- **Deal floor.** Deal waits out `floorMs` locally after a deal. The server's floor is the authority (`too_fast`).
- **Nothing fullscreen over a new decision (2026-09-14).** A moment that put something fullscreen holds the next deal
  until it has ended (`hand.controls` reason `screen`, `feel.screenHoldMs`): the bloom's picture (4 s, 2.4 s Calm),
  the win wash (900 ms), the losing edges (2600 ms breath plus its closing post). The bloom's 2.4 s follows the host's
  Calm (`reduced` or `intensity: calm`) only, never the OS `prefers-reduced-motion` alone, which the host is not told.
  Deal cannot be pressed by any path while a fullscreen moment runs; presses are dropped, not queued (owner, CONTRACT
  10.14 item 10). The button is disabled, marked `data-held` and reads `br_cards_moment` ("One moment") from the
  frame the moment fires, even while the hand is still being laid down; `deal()` itself refuses first (no ring, no
  note), so Space, Enter, repeated or scripted clicks and a direct call all drop; a tunnel breath that outlives its
  timed hold keeps the hold (`moments.breathing()`). The room relays no input into a station (no gamepad, no HUD or
  host message presses Deal); cards-check 1b tries every path during a bloom. A gate
  that sent nothing holds nothing; `suspend(true)` cancels the moments and the hold with them (the host's suspend
  stops its overlays).

## Effects (10.13.F)

| Effect | When | Where |
|---|---|---|
| Loom backs | always | `kit.draw('backs', ..., { backing: 'small' })`, one render a frame for every back (the check measures it); `spiral` off: brass crosshatch |
| Your deck | always | value `i` wears `deck.keyFor(value)` at 85% under #f7f0fb 0.3 and a corner halo, rank and suit on top with a white glow; `flash` off: plain faces |
| Breathing lamp | always, held during the win tunnel and the ace glow | 10 s period, amplitude x k; card shadows follow |
| Ripple felt | a card lands, Full only | one ripple per landing, 3.2 s (the weave is a cached image otherwise) |
| Chip vortex | `cards.win`, `cards.lose` | winnings spiral to the chip spot, a lost bet to the dealer, 2 s, the path trails the chip |
| Win tunnel | `cards.win` | seven nested frames, 3 s, `lighter`, alpha 0.5 x k |
| Ace glow | `cards.bloom` | rose glow around the ace, 1.4 s |
| Sit fan | `cards.sit` (open, "Stand up, sit back down") | 13 values out of the shoe face down, face up in a row, back into the shoe, 4.4 s |

Moments, through `createMoments` only, on the frame the page shows them:

- `cards.sit` on every sit-down.
- `cards.bloom` for a paid player blackjack, the frame the second player card finishes turning (`fx.gif_from` from the
  ace's viewport rect with the ace's key, then the rose wash).
- `cards.win` / `cards.lose` / `cards.push` on the settle frame by `result.net`; the win wash carries the key of the
  highest card (ace highest) over the winning hands, or no picture after a bloom.
- `moments.holdScreen(true)` whenever a reply shows `hand.done === false`, `holdScreen(false)` on the settle frame.

The table beats (2026-09-15). `planSteps(..., { beats: true })` adds `beat` steps for a reply to the player's own press
(Deal, a move); a hand put back on the felt (resume, refresh, `illegal`, `hand_open`, `auto_stood`) has none, and a
quiet flush (suspend, Back) plays none (Law VI). Each fires on the frame its card SHOWS, never on the reply (Law I). Host
args are Normal (the host applies Calm); `light` steps are the app's small overlays and may play while a decision is
open, everything fullscreen still waits for the settle. Gates dress plain: `subliminal` off drops the whispers, `flash`
off the bursts and washes, `spiral` off the streak's spiral (host side), `tunnel` off the losing edges.

| Moment | Fires on | Host, same frame, in order | Page |
|---|---|---|---|
| `cards.deal` | the first card of a fresh deal shows | `fx.sub_single` (one dealt word, light) | none |
| `cards.hit` | each hit card shows, at most one per 1.2 s (`COOLDOWN_MS`) | `fx.sub_single` (one word, light) | none |
| `cards.double` | the doubled card shows | `fx.gif_burst` (light) | none |
| `cards.split` | the pair slides apart (the two cards that follow are silent) | `fx.sub_single` (two words, light) | none |
| `cards.bust` | the card that busts a hand shows | none: the whisper is withheld, the loss lands at the settle | none |
| stand | the press | none: the reveal that follows is the beat | none |
| `cards.reveal` | the hole card turns | `fx.gif_burst` (light) | none |
| `cards.bloom` | a paid blackjack (as before) | `fx.gif_from` from the ace, `fx.wash` rose | `ace_glow` |
| `cards.win` | settle, `net > 0`, the dealer stood | `fx.wash` mint 0.7 with the best card (0.9, no picture after a bloom) | `win_tunnel`, `chip_vortex` |
| `cards.dealer_bust` | settle, `net > 0`, `dealerTotal > 21` | `fx.gif_burst`, `fx.wash` mint 0.8 with the best card | `win_tunnel`, `chip_vortex` |
| `cards.streak` | settle, the third win in a row and on (`streakAfter`, a push keeps it) | `fx.sub_pair` (two words), `fx.wash` mint 0.9 | `win_tunnel`, `chip_vortex` |
| `cards.sweep` | settle, a split with every hand won | `fx.gif_storm`, `fx.wash` gold 1 | `win_tunnel`, `chip_vortex` |
| `cards.lose` | settle, `net < 0` (as before) | the tunnel breath 0.75 over 2.6 s | `chip_vortex` |
| `cards.push` | settle, `net === 0` | none | none |

One settle beat per hand (Brake 2): sweep > streak > dealer_bust > win (`settleMoment`). `screenHoldMs` holds the next
deal for the wash (win, dealer_bust), the streak's words and spiral (1.7 s) and the storm's rain (2 s). The whispers
rotate through the four dealt words (`wordKeys`). The streak counter resets on `open()`.
- `suspend(true)` lays every queued step down quietly, cancels the moments and frees the Loom context; `close()` cancels
  and disposes the moments, the kit and the deck. `suspend(true)` keeps the deck (confirmed 2026-09-14, against 10.13.F's
  dispose): re-dealing on resume would ask the host for 13 new pictures and swap them under an open hand. Leaving the
  station (close) and sitting down again re-deals. cards-check asserts both.

## The reward pass (CONTRACT 10.22)

The table used to take `spReadout.set` and `.owe` and never `.thud()`, and a winning hand paid by having the SP number
change - Law XII broken outright. Every paid hand now asks `shared/win/` for a plan and obeys it. **Nothing in this
station re-decides Law IX or Brakes 2, 3, 5 or 8**: `shared/win/plan.js` owns all of it and `reward.js` only asks.

- **The rung** (`reward.settleTier`). `feel.settleMoment` through `shared/win/tier.js`, raised by the settled net:
  `cards.win` / `cards.dealer_bust` **1**, `cards.bloom` / `cards.streak` **3**, `cards.sweep` **4** (this table's hero),
  `cards.lose` / `cards.push` **0**. Those are the `CALLOUTS` sizes this file already had; the table hands out no bare 2.
- **THE BANK** (`bank.js` over `shared/win/bank.js`, Law XII). On the settle frame of a paid hand, beside the winning
  cards' glow: `plan.bank` tokens (3-7, 4 under Calm) leave THE POT (`table.potRect`, the chip spot on the felt or the
  authored `bet_spot` anchors in the room view) and arc to `ctx.spReadout.target()`. The chip is pinned to the
  pre-settle number until the first token lands, ticks a rung per landing (Law X, never before), keeps counting over
  `plan.partyMs`, and takes its thud at the END of the count, not the end of the flight. Reduced motion takes the STATE:
  no tokens, the settled number and the cue at once (`plan.bank === 0`). A loss and a push keep the plain chip thud.
- **THE CHIME LADDER** (`reward.ladderRoot` / `climbSteps` over `shared/win/ladder.js`). The streak is the root: the
  first win of a run is the root note and every win after it starts a semitone higher, capped at seven. The landing cue
  is step 0 (Law X) and only the steps after it are scheduled, across `plan.partyMs`. A skip, a suspend, Back and a new
  deal all `stop('ladder')`: the climb is silenced, never played faster (Law VI).
- **The cue** (`reward.settleCue`). Chosen by what the plan SPENT, not by the moment id: `small` / `mid` / `big` /
  `hero`. A worn-down streak (Brake 3) sounds like the chime it has become. A loss is still THE SETTLE and a push still
  a sigh, whatever the plan says - neither is a party and both always sound.
- **THE GLOW and THE SPARKLE BURST** (10.22.D, `arcademy/shell/counterfx.js`). `warmGlow` on the SP chip at every paying
  rung (`plan.glow`, 480 ms; 0 while melted and under reduced motion) and `sparkBurst` into `.cards-tokens` at
  `plan.sparkle` (7 at tier 3, 9 at tier 4; never under Calm, lite or reduced motion).
- **The room** (10.22.B). `ctx.revealedWin(net, plan.shower, name)` fires ONCE a result, on the settle frame, and only
  when `plan.shower > 0` - a tier 1 is a close-up event and does not show from across the room, and a loss or a push
  tells the room nothing. `net` is `hand.result.net`; the name is the callout's own localised text.
- **THE REVEAL** (`reward.calloutTier`). The hero callout (14vh, a rim and a short shake) is the declared hero move and
  plays once a sit-down: a second sweep, a sweep under Calm and a sweep in a trance all name themselves at `big`. A
  callout never shouts above what the brakes left the rung.
- **Brake 2** (`reward.joinParty`). A beat that lands while an earlier party still owns the station merges into the
  HIGHER plan and throws no second ceremony - in practice a blackjack's settle inside its own bloom, which pays but
  says nothing more. THE BANK ignores the merge: a pay must be seen to move at every rung.
- **Brake 5**. This table's focus state is a fullscreen hypno moment from an EARLIER beat still on the screen (the
  bloom's four seconds of picture). A settle under it is `melted`: rung 1, no shower, no sparkle, no glow, no reveal,
  the ladder an octave down - and the tokens still fly.
- **Brake 3 and the sit-down**. `freshSit` / `sitPlan` / `afterParty`, one ledger per sitting, counted PER RUNG.
  "Stand up, sit back down" is a new sitting: the worn rungs come back and the once-a-sitting hero is owed again.
  `afterParty` only counts a hero the frame it actually fired, so a sweep under Calm does not burn it.
- `debug().reward` carries the last plan, the live party and its ms left, the sit ledger and the bank's state.


**Calm and reduced motion** (`reduced`, `intensity: calm`, or `prefers-reduced-motion`): every step lands at once in
order (cards on their spots, face up), except that a blackjack keeps the bloom's 1.6 s to the reveal and 0.9 s to the
settle, so the win wash is never inside the host's 360 ms wash gap and the bloom never shares a frame with the result.
The fan fades in place for 2.4 s, the Loom and the pictures hold still, chips fade in place, page strengths x0.5. Host
args stay Normal (the host halves). The OS `prefers-reduced-motion` alone halves page strength but the host is not told,
so host washes and tunnels stay Normal there (as the wheel does). **Gates** are read live on every frame.

## Lexicon (`br_cards_*`, English fallbacks in the page, the same text in `en.json`; `smoke/lexicon.test.mjs` keeps them equal)

| Key | English |
|---|---|
| `br_cards_stage` | Soft Hand, a Twenty-One table. |
| `br_cards_print` | BLACKJACK PAYS 2 TO 1 · DEALER STANDS ON ALL 17s · SIX CARDS WIN |
| `br_cards_back` | Back |
| `br_cards_sp` / `br_cards_stake` / `br_cards_bet_line` | {n} SP |
| `br_cards_bet` | Bet |
| `br_cards_deal` / `br_cards_hit` / `br_cards_stand` / `br_cards_double` / `br_cards_split` | Deal / Hit / Stand / Double / Split |
| `br_cards_wait` / `br_cards_moment` | {s} s / One moment |
| `br_cards_sit` | Stand up, sit back down |
| `br_cards_sitting` | Sitting {n} |
| `br_cards_loading` | Shuffling the deck |
| `br_cards_sit_intro` | Sitting down. Your pictures are dealt to the thirteen values for this sitting. |
| `br_cards_sit_again` | Sitting back down. Your pictures are re-dealt to the thirteen values. |
| `br_cards_sit_plain` | Sitting down at the table. |
| `br_cards_dealer` / `br_cards_you` / `br_cards_hand_short` | Emi / You / Hand {i} |
| `br_cards_ready` | Pick a bet and deal. |
| `br_cards_dealing` | Dealing... |
| `br_cards_decide` | {p} against {dealer} showing {d}. Your move. |
| `br_cards_decide_split` | Hand {i}: {p} against {dealer} showing {d}. Your move. |
| `br_cards_hand_n` | Hand {i}: |
| `br_cards_res_blackjack` | Blackjack! +{n} SP. |
| `br_cards_res_charlie` | Six cards without busting. +{n} SP. |
| `br_cards_res_dealer_bust` | {dealer} busts at {d}. +{n} SP. |
| `br_cards_res_win` | {p} beats {d}. +{n} SP. |
| `br_cards_res_push` | Push at {p}. Bet returned. |
| `br_cards_res_push_bj` | Blackjack each. Bet returned. |
| `br_cards_res_bust` | Bust at {p}. {dealer} takes {n} SP. |
| `br_cards_res_dealer_bj` | {dealer} has blackjack. {dealer} takes {n} SP. |
| `br_cards_res_lose` | {d} beats {p}. {dealer} takes {n} SP. |
| `br_cards_res_net_up` / `br_cards_res_net_down` / `br_cards_res_net_even` | Up {n} SP overall. / Down {n} SP overall. / Even overall. |
| `br_cards_shuffling` | Shuffling. The next deal is ready in {s} s. |
| `br_cards_auto_stood` | Your last hand was stood for you after a day away. |
| `br_cards_illegal` | That move is not open on this hand. |
| `br_cards_hand_open` | Your open hand is back on the table. |
| `br_cards_insufficient` | You need {n} SP for that bet. |
| `br_cards_closed` | The table is closed for a moment. |
| `br_cards_offline` | The house is not answering. Try again in a moment. |

## Checks

```
node --test ConditioningControlPanel/Resources/web/backroom/stations/cards/tests/
node ConditioningControlPanel/Resources/web/backroom/stations/cards/tests/cards-check.mjs [evidenceDir]   (headless Chrome)
```

Dev harness: serve `ConditioningControlPanel/Resources/web` as the web root and open `/backroom/stations/cards/dev.html`
(`?sp=57&latency=80&floor=5000&calm&reduced&full&off=flash,spiral,brainDrain,tunnel&nopics&hook&script=As.9d.Kh.7c,Th.9d.8c.8s`).
`dev.station.debug()` shows the state, the felt, the kit, the deck, the moments and the feel log; `dev.host` is the
kit's mock host (what fired), and the page's `#screen` draws what the host would.

## Draft seated 3D view

When the room provides `ctx.stage`, cards render on the existing room renderer and seated camera. Standalone mounts keep the canvas view. The state machine, server calls, card order, moments and Deal guard are shared. The shoe raycast calls the same guarded Deal action as the button and keyboard.

`room/nodes-cards.js` requires eighteen `card_slot_{dealer,p0,p1}_{0..5}` anchors plus `deck_shoe_mouth`, `deck_shoe_base`, `bet_spot_0/1`, `table_lamp` and `felt_surface`. Slots carry `card_width` and `card_height` extras in model metres. Missing anchors fail visibly instead of guessing placement. The runtime owns card quads/textures, chip meshes and the breathing light; it restores hidden decorative cards and disposes those resources on close.

No new lexicon keys. Existing card status and controls remain the accessible text view. Verification: `node tests/cards-3d-check.mjs <evidence-directory> [--phone]` exercises the real room at 1280x720 or 400x800, with a local mock host. Phone results are headless emulation, not device measurements.
