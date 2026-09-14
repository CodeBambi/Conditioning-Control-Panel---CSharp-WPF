# The Prize Parlour (counter) station

The Back Room's only drain: eight prizes bought once each with SP (CONTRACT.md sections 3, 7, 10.13 and 10.17,
binding spec 10.17.F). Entry `station.js`, loaded by the room from the `counter` row of `stations.json` once the
integration pass adds `"entry": "stations/counter/station.js"` and `"state": "live"`. v1 is DOM only: no WebGL canvas,
no fx ids, no hypno kit, so it adds no context and no gate changes its dress.

| File | What |
|---|---|
| `station.js` | `mount(ctx)` -> `{open, close, suspend, destroy}`. The DOM (createElement only), Back and Escape, the chime, painting `view()`. |
| `cards.js` | Pure, no DOM: `readState` / `readRow` (the server's body), `cardOf` (the five faces), `deliveryKeyOf`, `classify` (what a reply means), `createCounter` (open, confirm, one idem per confirm, refusals, the SP chip on success). |
| `station.css` | Plum glass, rose borders, gold prices, mint Owned; eight cards in an auto-fill grid. No `backdrop-filter`. |
| `art/<prizeId>.webp` | 320 px stills of the approved shelf props, rendered from the room's `counter.glb` by `Scripts/render-counter-prize-cards.mjs` (never at runtime). |
| `mock-server.js`, `dev.html` | Standalone harness on the 10.17.C shapes, every refusal, delivery states and a `BACKROOM_COUNTER_ON`-like sale list. Not the server's rules. |
| `tests/*.test.mjs`, `tests/counter-check.mjs` | Node tests (`tests/fake-dom.mjs` stands in for the DOM); the headless Chrome check (`COUNTER_PORT` default 8899, debug +500). |

## Server API (binding, CONTRACT 10.17.C, CCP-Server `backroom-counter-routes.js`)

- `GET state` -> `{ ok, open, sp, catalogVersion, discordLinked, prizes: {revision, grants}, catalog: [row], delivery }`.
  A row is `{ id, priceSp, grants, order, nameKey, blurbKey, noteKey?, sale: on|soon, owned: {at, paidSp}|null, needs? }`.
  `delivery` is `{}` or `{ high_roller: {status, tries, at} }`.
- Behind a shut door `state` answers HTTP 200 `{ ok: true, open: false }` (not 403): the page shows `br_counter_closed`.
  A 403 `closed` (a buy behind the door) does the same.
- `POST buy { prizeId, catalogVersion }` with the confirm's idem -> `{ ok, idem, prizeId, paidSp, spBefore, sp, catalogVersion,
  prizes, delivery }`; `delivery` is null except for `high_roller`.
- Host Ops row (integration pass): `["counter"] = { ("GET","state"), ("POST","buy") }`.

## Behaviour

- **Open.** The page (title, grid, standalone Back) is on screen before `request('state')` answers (Law VI). One card per
  catalog row in `order`. A failed or shut `state` shows `br_counter_closed` and Back.
- **Faces** (`cardOf`, first match wins): `owned` (badge, plus the Discord delivery line for `high_roller`), `soon` (dust
  sheet, no button), `discord` (`needs: "discord"`, no button), `short` (price over `ctx.sp()`, a disabled
  "Short by N" button), `buy`. Rows carrying `noteKey` (the Racing Thoughts rows) show `br_prize_rt_note`.
- **Art.** `art/<prizeId>.webp`; a missing file falls back to a CSS plate with the prize name.
- **Buy.** Buy opens an inline confirm on the card (name, price, balance after, Confirm / Cancel) with ONE fresh idem.
  Focus moves to Confirm. Confirm sends `request('buy', { prizeId, catalogVersion }, idem)`; the button shows pending
  (`aria-busy`), a second press sends nothing and Cancel is disabled until the reply.
- **Success.** The card flips to Owned, then `ctx.spReadout.set(sp)`, `set(null)` and `thud()` (skipped when the room has
  no `spReadout`), and one chime (the race's `chime1.mp3` through `dtrh/shared/audioSrc.js`, its AudioContext made inside
  the Confirm press). A fresh `state` follows for live delivery. A `state` sent before a buy settled that answers after
  it is dropped and read again, so an owned card never turns back into Buy.
- **Refusals** (all HTTP 200 unless noted):
  - `insufficient`, `owned`, `unavailable`, `discord_required`: the confirm closes and the cards repaint from the reply
    and a fresh `state`. Nothing is charged, no chip, no chime.
  - `catalog_changed`: the new `catalog` is adopted and, when the card is still buyable, a NEW confirm (new idem) asks
    again at the new price. Never an automatic buy. The same re-ask happens when any `state` refresh shows a new price
    or version under an open, idle confirm.
  - `busy`, `too_fast`, a host `timeout` or `offline` (a 5xx the host passes on with its body included): the confirm stays open with `br_counter_retry`; Confirm retries
    with the SAME idem (a lost reply replays the receipt, charged once).
  - `bad_input`, `idem_mismatch`, anything new: the confirm closes and `state` is read again.
  - 403 `closed`: the counter shows closed.
- **Back.** `ctx.hostBack === true` hides every station Back (the header chip and the closed card) and the station SP
  chip (`ctx.spReadout` owns the SP). Escape always stands up. Back during an in-flight buy leaves at once: `close()`
  bumps the session, so the late reply paints nothing and touches neither the chip nor the chime. The buy settles on
  the server and the next `open()` shows it owned.
- **Reduced / Calm** (`ctx.reduced`, `intensity: calm`, or `prefers-reduced-motion`): `data-still` on the root, no
  hover tilt, no flip, no pending stripe; the state swap is instant. `onSettings` repaints live.
- **suspend(on)** suspends the chime's AudioContext. **close()** removes the DOM, the keydown listener, `onSp` and
  `onSettings` subscriptions and closes the AudioContext; **destroy()** also removes the stylesheet link.

## Lexicon

English fallbacks live in `cards.js` `LEX`; the integration pass adds them to `en.json`.

`br_counter_title`, `br_counter_closed`, `br_counter_retry`, `br_counter_buy`, `br_counter_confirm`, `br_counter_cancel`,
`br_counter_after` ("Balance after: {0}"), `br_counter_owned`, `br_counter_soon`, `br_counter_short` ("Short by {0}"),
`br_counter_link_discord`, `br_counter_delivery_pending`, `br_counter_delivery_granted`,
`br_counter_delivery_not_in_guild`, `br_counter_delivery_failed`, `br_prize_rt_note`, and
`br_prize_<id>_name` / `br_prize_<id>_blurb` for `jackpot_remix`, `rt_demo`, `high_roller`, `flashes_v2`, `bubbles_v2`,
`rt_bundle_1`, `rt_bundle_2`, `rt_bundle_3`.

Not in the 10.17.F list: `br_counter_price` ("{0} SP", every price and the standalone chip) is new and needs an
`en.json` row too; `br_back` ("Back", the standalone Back) already exists.

## Art

```
node ConditioningControlPanel/Scripts/render-counter-prize-cards.mjs [--size 320] [--max-kb 60]
```

Headless Chrome, the vendored three.js (GLTFLoader + meshopt decoder), `room/assets/counter.glb`. Every `shelf_<prizeId>`
group found by its `prize_id` extras is shown alone, framed from a slight three-quarter angle and written as a
transparent square webp, quality stepped down until it is under the cap. Re-run after the counter props change.

## Tests

```
node --test ConditioningControlPanel/Resources/web/backroom/stations/counter/tests/*.test.mjs
node ConditioningControlPanel/Resources/web/backroom/stations/counter/tests/counter-check.mjs <evidenceDir>
```
