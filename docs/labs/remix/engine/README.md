# Remix engine

The whole remixer, minus the interface. Only `decode.js`, `render.js`,
`stamp.js`, `export.js`, the two workers and `effects/*` touch the DOM; the rest
is plain data and runs in node. `index.js` is the only file the UI imports, and
`API-NOTES.md` lists where this differs from `REMIX-SPEC.md` and what the UI
asked for on top. The UI imports it through `../ui/engine-bridge.js`.

`fonts.js` holds the four caption faces the room ships with (`assets/fonts/`,
latin subset woff2 under the OFL) and loads them with the `FontFace` API.
`ensureFonts` is cached and never rejects, so a missing file leaves the fallback
stack drawing instead of stopping a frame; the export path awaits it.

`layout.js` holds the split tree and the stage schedule; `rects.js` under it
holds the fixed table and the easings, so `layouts/` can reach them without
importing `layout.js`, which imports `layouts/` back. `layouts/` is one module
per motion layout.

`project.js` holds the state, events, media, playback, rendering, history and
export; `ops.js` holds the edits that are nothing but a change to that state
(tiles, dock, layout, blocks, caption, focus, vibes, dice) and is wired in by
`createOps()`. Both land on the one api object, so the split is invisible to
callers.

## Tests

    node --test "remix/test/*.test.mjs"

256 tests, about 1.5 s. The quotes matter: `node --test remix/test/` fails on
Node 24. `make-samples.mjs` regenerates the sample gifs in `remix/assets/sample/`.

## Motion layouts

The old four (grow, flat, shuffle, mirror) place tiles at stage boundaries.
A motion layout moves every frame. Each one is a module in `layouts/`:

    export const stack = {
      id, name, minTiles, maxTiles,
      cost: 'cheap' | 'mid' | 'dear',
      rectsAt(cfg, frame),
    };

`cfg` is built once per project state by `motionCfg`:
`{ n, frames, stageFrames, seed, orientation, w, h }`. `rectsAt` is pure and
deterministic from `cfg` alone, and returns one entry per thing to draw this
frame, in canvas fractions:

    { g, x, y, w, h, a?, rot?, sc?, z?, ph?, crop?, gap?, plate? }

`g` is the tile index. **The loop must close**: the list at frame `frames` has
to draw the same picture as the list at frame 0. `remix/test/motion.test.mjs`
checks exactly that, across 2 to 8 gifs, both orientations, three lengths and
three seeds.

To add one: drop the file in `layouts/`, add it to `MOTION_LAYOUTS` in
`layouts/index.js`, and give it a weight in `AUTO_WEIGHTS.layouts`. Nothing
else: `LAYOUTS`, `LAYOUT_MODES`, the picker chips and the roll all follow.
Motion layouts read the fixed table, not the dock tree.

The maths is written in a virtual 480x270 landscape space (`layouts/util.js`,
`V`), which `V` turns into fractions and stands on end for portrait, so one
set of numbers serves both. `remix/docs/motion-pitch.html` is the reference
the shipped twelve were ported from (Stack, Spread, Slide, Ripple, Swallow, Cover, Mirror Tunnel,
Deal, Carousel, Ring Hop, Ken Burns Wall, Pinwheel); nothing waits there now.

A layout that leaves ground showing (Stack, Deal, Carousel, Pinwheel) sets `backdrop: true`
on its module; the compositor then paints the first gif cover fitted over the whole canvas,
softened through a tiny scratch canvas (no `ctx.filter`, iOS) and washed with the ground,
under the tiles. The plates keep the cards reading as cards on top of it.

## Dev page

Serve the repo root (`python -m http.server 8901`) and open
`http://localhost:8901/remix/engine/dev.html`. Buttons for the vibes, effects,
layout and export. Query params drive it for screenshots and benchmarks:
`?probe=drain&mode=drip&frame=40&tiles=4&strength=70`, `?probe=perf&fx=drain`,
`?probe=gifbench`, `?dock=0,3,bottom`.

## Numbers

Headless Chrome, GPU off, so a real browser is quicker. 480x270, four tiles,
effect over the whole canvas, averaged across 120 frames:

| what | ms/frame |
|---|---|
| mosaic, no effects | 0.05 |
| tint 0.1, glitch 0.6, caption 1.7, spiral 2.5, focus 4.2, drain 7.3 | each |
| mosaic + drain + tint + spiral | 10.0 |

Playback is 15 fps, a 66 ms budget. A 75 frame 480x270 gif encodes in about
1.3 s and lands around 2.5 MB, under the 10 MB cap.

Measured through the real UI (`?debug=1`, headless Chrome, 480x270, four
tiles, grow): tint + spiral + glitch on the canvas 5.6 ms/frame; all six
effects on the canvas and again on one gif (12 blocks) 31.6 ms/frame. The
75 frame export came out at 2,515,926 bytes, GIF89a, with Discord-safe on
or off.

## Fit ladder

`exportGif({ shrink: true })` re-encodes down the `LADDER` (12 fps, then two
thirds size) until the gif fits `maxBytes` (10 MB, 8 MB with `discordSafe`).
`onRung(rung, bytes)` reports each step. Without `shrink` the cap is off.
`setShrink(level)` applies a rung ahead of time and `shrink` reads it back.

## Events

`on(name, fn)`: `change` (any edit), `frame` (playhead), `play` (playing
flag, also when a non-looping preview stops on its own), `history` (after
commit, undo, redo), `media` (add, remove, on/off canvas).

## Limits
Safari has no canvas `filter`, so blur falls back to a downscale and upscale.
Video import needs a codec the browser plays; there is no transcoder. Without
module workers, decode and encode run on the main thread and hitch.
