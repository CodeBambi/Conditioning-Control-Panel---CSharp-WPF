# Slot station

The Candy cabinet (CONTRACT.md sections 2-7). Entry `station.js`, loaded by the room from `stations.json`.

| File | What |
|---|---|
| `station.js` | `mount(ctx)` -> `{open, close, suspend, destroy}`. DOM, readouts, press flow, fx, melt. |
| `tape.js` | Pure tape client: state, buys, idem reuse, freeze, shownSp. No DOM. |
| `scene.js` | three.js cabinet. One WebGL context per `open`, freed in `close`. |
| `symbols.js` / `media.js` | Reel cell art and the sit-down deal (keys only leave the page). |
| `nodes.js` | The glb node names the page drives. |
| `mock-server.js`, `dev.html` | Standalone harness. Not shipped behaviour. |

## What the page needs from the room

- An import map for `three` and `three/addons/` pointing at `/vendor/three/` (GLTFLoader imports `three` bare).
- `ctx.request(op, body, idem)` resolving a `station-result`, `ctx.fx(fxId, symbols)`, `ctx.media()`,
  `ctx.onSp(fn)`, `ctx.lex(key, fallback)`, `ctx.standUp()`.
- The `melt` frame goes out through `ctx.melt(left)` when present, else `ctx.bridge.send({type:'melt', ...})`.
- `ctx.hostBack === true` (the room sets it): the station hides its own Back chip and the card's Back button, because
  the room's Back is the only one. Escape still stands up. Without it (dev.html) both buttons show.
- `ctx.variant` `{ id, name, palette }` or null: `palette` (material name -> `rrggbb`) recolours `candy_rose`,
  `candy_violet`, `candy_plum` and `wand_pink` through `palette.js` (one clone per name, the glb untouched), and
  `name` goes on the marquee. Null keeps the rose cabinet and the `br_slot_marquee` text.

## Checks

```
node --test ConditioningControlPanel/Resources/web/backroom/stations/slot/tests/
node ConditioningControlPanel/Resources/web/backroom/stations/slot/tests/nodes-check.mjs [slot.glb]
```

Run the node check on every new `slot.glb` drop. Required nodes (`cabinet`, `reel_1..3`, `lever`,
`cam_seat`, `cam_target`) fail it and show "Model missing X" in the page; the rest only warn.

Dev harness: serve `ConditioningControlPanel/Resources` as the web root and open
`/web/backroom/stations/slot/dev.html` (`?sp=57&melt=0&floor=3000&latency=120&reduced&variant=violet`).

`mock-server.js` follows table v5 (CONTRACT 10.1: `emi3` pays 400, drawn at 77 per million on its own seeded
stream) and the 3000 ms slot and freeze floors (10.12).
