# Daily Daze wheel station

One free spin a day (CONTRACT.md sections 2-7, owner decisions of 2026-09-13: wheel option A). Entry
`station.js`, loaded by the room from `stations.json`. The model is the owner-approved wheel
(`blender-scripting/wheel/APPROVED.md`), copied read-only by `Scripts/sync-backroom-wheel-assets.ps1`.

| File | What |
|---|---|
| `station.js` | `mount(ctx)` -> `{open, close, suspend, destroy}`. DOM, readouts, the spin flow, fx, the day's countdown. |
| `wheel.js` | Pure: slice layout from `state.slices`, `sliceAt`, the seeded landing inside `result.sliceIndex`, the deceleration plan, `countdown`, `readResult`, Law I `shownSp`. |
| `feel.js` | THE HOUSE BOOK, pure: tiers, fx ids per tier, the recipe, the 6 Hz tick gate and chime ladder, glance, token counts. |
| `scene.js` | three.js close-up. One WebGL context per `open`, freed in `close`. Runtime slices, drag, coast, landing, bulbs, star, screens. |
| `emi.js` | EMI on the perch: face atlas, spiral eyes while turning, win/jackpot/sleepy poses. |
| `readout.js` | The one SP readout: `ctx.spReadout` when the room has it, else the room chip, else the station's own chip. |
| `bank.js` / `sound.js` | THE BANK tokens (slice -> readout) and the cues (the race's clips, synth fallback, no new files). |
| `nodes.js` | The glb node names the page drives. |
| `mock-server.js`, `dev.html` | Standalone harness on the binding API. Not shipped behaviour. |

Shared code is imported only from stable paths that exist on every branch: `arcademy/shell/counterfx.js`
(THE THUD, THE SHIVER, THE BANK timings) and `dtrh/shared/audioSrc.js` (clip lookup). The slot's `feel.js`,
`bank.js` and `sound.js` are the pattern, not imports: they live on the slot branches and speak paylines.

## Server API (binding, CCP-Server `backroom-wheel-routes.js`)

- `GET state` -> `{ ok, sp, open, day, spun, result|null, snoozeCarry, nextResetAt, jackpot:{amount, odds, wonToday, eligible},
  slices:[{id, label, pay, width, odds}], floorMs }`. `width` is the drawn width in degrees (the picture); `odds` is
  `"1 in N"` or `"never"` (the ledger, printed in the Odds panel). The jackpot slice's `pay` is the day's pot.
  Slices carry no kind: `jackpot` and `snooze` are known by id.
- `POST spin {idem}` -> `{ ok, sp, result:{day, sliceId, sliceIndex, pay, snoozeCarryPaid, jackpot, jackpotFallback, snoozed,
  total, capped}, jackpot, snoozeCarry, nextResetAt }`. `total` is pay + carry before the SP cap; the page banks
  `sp - spBefore`, so a capped pay never shows more than landed.
- Refusals: `already_spun` (+ `result, sp, jackpot, snoozeCarry, nextResetAt`: the page lands on the stored result with no
  second bank), `busy` and `too_fast` as HTTP 200 (retried with the same idem, at most 3 times), `bad_request`,
  `closed` as 403 (the wheel winds down, a card says closed). A host `timeout` retries with the same idem.
- Host Ops row: `["wheel"] = { ("GET","state"), ("POST","spin") }`.

## The spin

- **Law VIII.** A drag on the rim follows the pointer at once; a release past 0.1 rad flings it (speed clamped to
  0.006-0.03 rad/ms, direction kept). The Spin button, Space or Enter start a clockwise coast on the same frame. Either
  way EMI glances and the button rings before the server answers.
- **The landing is the server's.** The reply retargets the coast onto `landingAngle(layout, sliceIndex, day)`: the
  middle 60% of the drawn slice, seeded by the day (Law V), so a reopen shows the same landing. A quartic ease-out
  whose start speed matches the coast, 3.2-5.6 s, at least one turn. Drag strength changes the picture, never the slice.
- **Nothing tells the result early.** The screens, the status and the jackpot chip adopt the reply on the landing
  frame; the SP readout owes the pay until THE BANK lands it (Law I).
- **Reopen after spinning** shows the stored landing, the result as text, and a countdown to `nextResetAt` on the
  status line, the button and `status_screen`. At zero the page refetches state (then every 30 s until the server's
  day has turned). The countdown reads the client clock for display only; the server owns the day.

## Feel (cited from house-book.md)

- **THE THUD** when the pointer settles: the landed slice flashes 2.2 -> 1 over 340 ms, the pointer knocks, the cue on
  the same frame (a muted, low-passed thud for Snooze).
- **THE CHIME LADDER** on peg crossings: a crossing plays (tick cue, pointer kick, highlight moves) only when the last
  played one is at least 167 ms old, so nothing changes faster than 6 Hz. Once crossings come slower than that the
  tick climbs a semitone per play, capped at 7.
- **THE BANK**: 3-7 tokens by tier (4 on Calm) from the face under the pointer to the SP readout, ticks per landing,
  mini-thud on the last, `+N SP` text.
- **THE MASCOT GLANCE**: press -> spirals (the eyes turn while the wheel does), landing -> hearts / jackpot / sleepy;
  never the same pose twice.
- **THE BREATH**: the jackpot star's glow only (3.2 s), held while the wheel turns or celebrates. EMI does not sway at rest.
- **Law IX**: 1-3 SP a chime and `fx.spiral_brief`; 5-20 two notes, EMI's hop, `fx.gif_burst`; 40 and 100 THE THUD
  chimes and `fx.gif_storm`; the pot THE REVEAL (EMI over-rotates, `status_screen` counts the pot up over 620 ms,
  sparks, gold bulbs) and `fx.jackpot`. No new fx ids. Gif fx carry the dealt GIF keys from `ctx.media()`.
- **Snooze** (Brake 6): THE SHIVER, a muted thud and a yawn, EMI droops and dozes (`z Z z`), `+2 tomorrow` as text.
- **Law VI**: reduced motion or Calm take the settled state: no coast, the wheel is on its landing once the server
  answers, no tokens (the readout lights), no shiver, no reveal travel; the cues still play. Back and suspend skip
  every ceremony; `close()` settles inside the room's 420 ms.
- **Brake 9**: every value is also text (status line, jackpot chip with odds, Odds panel, screens, `+N SP`).

## What the page needs from the room

- The import map for `three` and `three/addons/` (GLTFLoader imports `three` bare).
- `ctx.request`, `ctx.fx`, `ctx.media`, `ctx.sp`, `ctx.onSp`, `ctx.lex`, `ctx.standUp`, `ctx.reduced`, `ctx.intensity`.
- `ctx.hostBack === true`: the station hides its own Back buttons; Escape still stands up.
- `ctx.spReadout` (optional): used for the SP chip when present. Without it the station writes the room's
  `#br-sp-value` directly (an observer puts a room repaint back), or its own chip standalone.
- Lexicon: `br_wheel_*` keys with English fallbacks in the page.

## Checks

```
node --test ConditioningControlPanel/Resources/web/backroom/stations/wheel/tests/
node ConditioningControlPanel/Resources/web/backroom/stations/wheel/tests/nodes-check.mjs [wheel.glb]
node ConditioningControlPanel/Resources/web/backroom/stations/wheel/tests/wheel-check.mjs [evidenceDir]   (headless Chrome)
```

Dev harness: serve `ConditioningControlPanel/Resources` as the web root and open
`/web/backroom/stations/wheel/dev.html` (`?sp=57&next=jackpot|snooze|deep&pot=12&young&taken&carry=2&spun&reduced&calm&hook&day=<ISO>`).
`dev.station.debug()` shows the state, the readout, the feel log, the cue trace and the scene.
