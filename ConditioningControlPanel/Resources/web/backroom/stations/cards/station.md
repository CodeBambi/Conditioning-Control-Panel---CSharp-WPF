# Soft Hand (Twenty-One) station

Classic Twenty-One against the house, dressed in the player's own pictures (CONTRACT.md sections 2-7 and 10.13; the
binding look is the owner-approved mockup `hypno-spins-v3.html`). Entry `station.js`, loaded by the room from the
`cards` row of `stations.json`. There is no station glb: the `cards` row's `card-table.glb` is a room copy with no
page node contract, so the station draws a **2D canvas table** filling the station view (10.13.F).

| File | What |
|---|---|
| `station.js` | `mount(ctx)` -> `{open, close, suspend, destroy}`. DOM, controls, the request flow, the step queue, moments, the SP chip. |
| `hand.js` | Pure: `readHand` / `readState` (publicHand), `controls` (which buttons are live), `classify` (what a reply means), `createIntent` + `mayRetry` (one idem per press), `owedFor` (Law I), the hint preference. |
| `feel.js` | Pure: `planSteps` (a reply's hand against the felt -> timed steps), `momentOf`, `isBloom`, `bestCard`, `vortexOf`, `resultLines`, `fanCard`, `lampBreath`, `TIMING`. |
| `table.js` | The canvas: lamp, felt weave, printed arc, shoe, chip spot, hands, and the page effects. |
| `mock-server.js`, `dev.html` | Standalone harness on the 10.13.E shapes with scripted fixture shoes. Not shipped behaviour, never the server's rules. |
| `tests/*.test.mjs`, `tests/cards-check.mjs` | Node tests; the headless check (`CARDS_PORT` default 8898, debug +500). |

Shared code comes only through the hypno kit (`shared/hypno/index.js`): `createLoomKit` (every card back is the Loom's
`backs` preset through the real `loomField.js`), `createDeck` (13 dealt pictures, decoded by the room's
`gif-decode.js`), `createMoments` (every host effect), `strengthK`, `viewportRect`, `DECK_VALUES`.

## Server API (binding, CONTRACT 10.13.E, CCP-Server `backroom-cards-routes.js`)

- `GET state` -> `{ ok, sp, open, hand: publicHand | null, legal, hint, autoStandAt, rules, floorMs }`.
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
- Rules, odds and pays are the server's (RULES_V1). The page never computes a pay, a legal move or a hint; the only
  arithmetic is the display total of face-up cards while the dealer's hand is being turned.

## The table

- **Controls.** Bet chip 1 or 2 SP (`rules.stakes`), starting on 1 below 30 SP and on 2 from 30. Deal (Space or Enter).
  Hit, Stand, Double, Split exactly as `legal` says, shown only while a hand is open. The basic-strategy hint is off by
  default, remembered in `localStorage` `br_cards_hint`, and shows the server's `hint` as a line and a mint ring on
  that button. "Stand up, sit back down" while no hand is open; it latches on the press (Deal, the moves and a second
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
- `suspend(true)` lays every queued step down quietly, cancels the moments and frees the Loom context; `close()` cancels
  and disposes the moments, the kit and the deck. `suspend(true)` keeps the deck (confirmed 2026-09-14, against 10.13.F's
  dispose): re-dealing on resume would ask the host for 13 new pictures and swap them under an open hand. Leaving the
  station (close) and sitting down again re-deals. cards-check asserts both.

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
| `br_cards_hint_toggle` | Basic-strategy hint |
| `br_cards_hint_is` | Hint: {move}. |
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
(`?sp=57&latency=80&floor=8000&calm&reduced&full&off=flash,spiral,brainDrain,tunnel&nopics&hook&script=As.9d.Kh.7c,Th.9d.8c.8s`).
`dev.station.debug()` shows the state, the felt, the kit, the deck, the moments and the feel log; `dev.host` is the
kit's mock host (what fired), and the page's `#screen` draws what the host would.
