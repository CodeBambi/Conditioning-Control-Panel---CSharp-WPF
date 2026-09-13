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

## Checks

```
node --test ConditioningControlPanel/Resources/web/backroom/stations/slot/tests/
node ConditioningControlPanel/Resources/web/backroom/stations/slot/tests/nodes-check.mjs [slot.glb]
```

Run the node check on every new `slot.glb` drop. Required nodes (`cabinet`, `reel_1..3`, `lever`,
`cam_seat`, `cam_target`) fail it and show "Model missing X" in the page; the rest only warn.

Dev harness: serve `ConditioningControlPanel/Resources` as the web root and open
`/web/backroom/stations/slot/dev.html` (`?sp=57&melt=0&floor=800&latency=120&reduced`).
