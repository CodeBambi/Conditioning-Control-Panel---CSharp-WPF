# Remix Room — build spec v1

A free, client-side gif remixer at `cclabs.app/remix`. Nothing leaves the browser. A user drops
up to 8 gifs and, in about a minute, exports one loop that carries the CCP stamp. It has to feel
novel (the mosaic + hypno effects) and be effortless (three rules below). UI and UX are the
product; the encoder is plumbing. Since 2026-09-07 the page opens on **Auto** (load your gifs,
press the button, roll, caption, save; see the Auto section) and the editor is one link away.

Unlisted for now: `<meta name="robots" content="noindex, nofollow">`, no nav link. No login, no
server, no analytics, no fetches except the page's own files.

## The three rules (every screen, every feature)

1. **Every panel = mode chips + at most two knobs.** No panel has a third slider. No typed numbers.
2. **Time lives only on the timeline.** An effect's start/end is the edge of its block. A one
   frame block is a flash. There is no "start at second N" field anywhere.
3. **Anything spatial is dragged on the canvas.** Caption position, focus path, tile order.
   Never typed in.

## Clock model

- Master clock: **15 fps** fixed. Default canvas length **5.0 s = 75 frames**. Length chip on the
  top bar offers 3 / 5 / 8 s (45 / 75 / 120 frames). Nothing else changes the clock.
- Every source is **resampled to the master clock** at import (nearest source frame by time) and
  **loops** to fill the canvas length. A still image is one frame repeated.
- Sources are **decoded to output resolution at import** (never keep full-size frames). Output
  sizes, fixed: Landscape 480x270, Portrait 270x480, Square 360x360. Changing orientation re-decodes
  from the kept original bytes (`File`/`ArrayBuffer` retained per media).
- Per-tile play mode (the play glyph on a tile's timeline strip): `forward` (default), `hold`
  (freeze on the tile's first frame until its enter frame, then play), `rewind`, `boomerang`.

## Canvas, tiles, layouts

- `tiles[]` are the media placed on the canvas, in order. Each tile: `{ id, mediaId, enterFrame,
  playMode, rect:{x,y,w,h} }` with rect in **fractions of the canvas** (0..1).
- **Fixed stage layouts** by tile count `n` (1..8), for landscape (flip rows/cols for portrait;
  square uses the landscape table):
  - 1: full. 2: split left/right. 3: big left + two stacked right. 4: 2x2. 5: 2 top + 3 bottom.
    6: 2x3. 7: 3 top + 4 bottom. 8: 2x4.
  - A new tile **splits an existing tile**; the layout table is the truth, `rect` is derived from
    `(n, index, orientation)` by `layout.rectFor(n, i, orientation)`.
- **Grow** (layout mode `grow`): stage k shows the first k tiles at `layout(k)` rects; stages
  advance every `stageMs` (default 600 ms, chips 400 / 600 / 900) until all tiles are on, then hold
  the full mosaic to the loop. Tile `enterFrame` is derived from the stage schedule. Rects
  **animate** between stages over 4 frames (ease-out) so tiles slide, not pop.
- **Flat** (layout mode `flat`): all tiles on from frame 0.
- Layout picker options (under the Layout button): Grow / Flat / **Shuffle** (tiles swap rects on
  every stage boundary, seeded) / **Mirror** (the first tile is repeated in every slot, each copy
  one stage further into its own loop — the droste look). Mirror ignores extra media.
- **Motion layouts** (one module each in `remix/engine/layouts/`, two gifs and up bar Mirror Tunnel and Pinwheel):
  **Stack** (the gifs drop onto a pile one at a time, each at its own spot and tilt; the pile
  rebuilds so the last state is frame 0 again), **Spread** (one gif fills the frame, it splits and
  the rest slide in from their nearest edge, then in the last five frames the first gif swallows the
  mosaic back to the start), **Slide** (rows of gifs drift past, every other row the other way, each
  row moving by exactly one repeat per loop),
  **Ripple** (a wave rolls across the mosaic from one corner, every tile puffing up as it passes
  and settling behind it, over by 55 percent of the loop), **Swallow** (one tile grows until it
  covers nearly everything, holds, and shrinks back into its slot, then another takes its turn),
  **Cover** (the first gif rides across a bed made of the others at 80 percent size, off canvas
  at both ends of the loop,
  **Mirror Tunnel** (one gif nested inside itself, the camera pushing in exactly one ring per
  loop; two or three gifs take turns on the rings and it is the one motion layout that runs on a
  single gif, so it caps at three), **Deal** (a deck in the corner dealt out into a fan, a hold,
  then gathered back in reverse), **Carousel** (the gifs ride a ring that turns towards you,
  one full turn per loop, size and alpha and draw order all off the depth), **Ring Hop** (every
  tile hops one slot around the mosaic each beat, n hops a loop, so the last one puts them home),
  **Ken Burns Wall** (the mosaic holds still while every tile's
  crop window walks a circle inside its own gif and its zoom breathes, both exactly once per
  loop), **Pinwheel** (the first gif on every spoke of a wheel that turns exactly one spoke per
  loop, around a hub that breathes and carries the second gif if there is one, so it runs on a
  single gif and caps at two).
- A motion layout is `{ id, name, minTiles, maxTiles, cost, rectsAt(cfg, frame) }`, listed in
  `MOTION_LAYOUTS` and merged into `LAYOUTS`. `cfg` is `{ n, frames, stageFrames, seed,
  orientation, w, h }`, built once per project state; `rectsAt` is pure and deterministic from it,
  and **the loop must close**: the list at frame `frames` draws the same picture as the list at
  frame 0. Motion layouts read the fixed table, not the dock tree.
- A slot may carry `alpha`, `rot` (radians about its centre), `scale` (about its centre), `z` (draw
  order), `crop` (`{cx,cy,z}`, pans and zooms inside the cover fit), `frameOffset` and `plate` (a
  ground border, so a card reads as a card), and `gap: false` to skip the mosaic gutters. The clip
  is taken after the transform, so a tilted slot is clipped to its tilted rect. With none of those
  present the compositor draws the tile it always drew.
- Gap between tiles: 2 px at output res, canvas ground `#14142B` behind.
- **Flip** (`layout.flip`, boolean, default false, a chip on the Layout panel and the `F` key): the
  slots for a frame are mirrored across the middle of the canvas after the mode has placed them, so
  what slid in from the right slides in from the left. `rot` is negated and `crop.cx` turns over
  with the slot. The tile pictures are never mirrored, only where they sit and which way they
  travel, and every mode gets it from the one place. Unrelated to the layout mode named `mirror`.
- A sparse layout (Stack, Deal, Carousel, Pinwheel) never shows bare ground: a soft, dim copy of
  the first gif fills the canvas under the tiles (`backdrop: true` on the layout module).

## Dock: drag a tile to swap or split (owner ask, 2026-09-07)

The mosaic is a **split tree**, not a fixed table. `layout.tree` is a nested node
`{ dir:'row'|'col', a:Node, b:Node, ratio:0.5 }` whose leaves are tile ids. The fixed stage table
above is only the **default tree** built for `n` tiles (`defaultTree(tileIds, orientation)` must
reproduce those exact rects). Rects are always computed from the tree (`rectsFor(tree, bounds,
gap)`), so a Grow stage `k` is the tree with the not-yet-entered leaves pruned (a node with one
missing child collapses to the other child). Removing a tile collapses the same way.

Engine ops (all commit an undo step):
- `p.swapTiles(aId, bId)` - the two leaves trade places.
- `p.dockTile(movingId, targetId, side)` - `side` in `left | right | top | bottom`: the moving leaf
  is removed from its place (collapsing its old parent), and the target leaf is replaced by a new
  split node `{dir: left/right ? 'row' : 'col', a/b ordered by side, ratio: 0.5}` holding both.
- `p.previewDock(movingId, targetId, side) -> rects` pure, no state change, for the UI ghost.
- `p.layout.tree` readable; `toJSON` carries it.

UI interaction (mouse and touch, the same state machine):
1. Press and hold a tile 350 ms (touch) or press and move 6 px (mouse) starts a drag. A
   translucent ghost of the tile follows the pointer; the source tile dims to 40 %.
2. Over another tile, that tile gets a pink outline and a hint pill `drop to swap`. Dropping now
   calls `swapTiles`.
3. **Hold still over the same tile for 2 s** (timer resets when the pointer enters a different
   tile) and the tile enters **split mode**: the pointer position inside the target picks the
   side (nearest edge; the centre 30 % square keeps `swap`). The preview shows the target
   shrinking to its half and a pink-tinted half-rect where the moving tile would land, animated
   over 120 ms as the side changes, with the hint pill reading `drop to split left/right/top/
   bottom` (or `drop to swap` in the centre). Dropping calls `dockTile`. This is the VS Code
   editor-dock feel.
4. Fast path: crossing into the outer 15 % edge band of the target and staying there 500 ms also
   enters split mode. Leaving the target cancels split mode; Esc cancels the whole drag.
5. Drop outside every tile or on the source itself: nothing happens, ghost snaps back.
6. Reduced motion: no ghost animation, previews still show.

The media strip keeps its simple reorder (drag thumbs to reorder the enter order for Grow).

## Loop marker (end of the canvas timeline strip, cycles on tap)

- `clean`: cut back to frame 0.
- `snap` (default): the last 3 frames cut to the tint colour with one word (the caption text if any,
  else `drop`) — the "snap clean at the loop" beat.
- `seamless`: the last 6 frames crossfade into the first 6.

## Effects (six, each a block on the timeline; target = canvas or one tile)

Each effect has **modes** (chips) and **at most two knobs** (0..100 sliders). Strength ramps in over
the first 3 frames of a block and out over the last 3, automatically. A block has
`{ id, effect, mode, target:'canvas'|tileId, start, end, params }` with `start`/`end` in frames,
`end` exclusive, `end - start >= 1`.

| effect | modes | knobs | look |
|---|---|---|---|
| `drain` | **shelved, engine only** (owner call, 2026-09-07): no HUD button, no panel, no help card, no vibe and no dice roll. `soft` / `drip` / `spread` | Strength |
| `tint` | `wash` / `creep` | Colour (hue chip row: pink / lavender / gold / custom-from-media-average) , Intensity | `wash` = flat multiply/screen blend. `creep` = the brand motif: colour bleeds in from one corner as a radial gradient that grows with block progress, screen blend, spiral faint behind it. |
| `spiral` | `over` / `through` | Strength, Speed | procedural spiral (canvas drawn, 3 arm styles seeded, dice picks) rotating; `over` = screen blend on top; `through` = the spiral is a mask, the tint colour shows through the arms. User may drop their own spiral image (goes in the media list as kind `spiral`). |
| `glitch` | `ghost` / `double` | Strength | the DtRH glitch bubble: a faded second loop over the whole target rect, cover fitted, read at the same clock time. `ghost` borrows another gif from the strip (seeded pick, the dice moves it, a strip of one falls back to `double`). `params.source` is the user's own answer: `dice` (the default, and what every project saved before this said) leaves it to the seed, a media id pins the ghost to that gif and a reroll no longer moves it, and an id that has left the strip falls back to the dice. The panel shows the choice as a dice chip and a scrolling row of the strip's thumbnails under the ghost chip. `double` is the picture underneath at 2x from its centre. Strength is peak opacity, about 0.12..0.6. Alpha is the 3 frame ramp times a life curve (in over 8 %, hold, out over 14 %); the colour pop is a soft-light second pass plus a thin pink wash, never `ctx.filter`. `tear`, the old sideways band cut, left the panel and the dice on 2026-09-07 (owner call). It sits in `EFFECTS.glitch.legacyModes`: nothing offers it or rolls it, `createBlock` and `validateBlock` still take it and the renderer still draws it, so a saved remix that has one plays. |
| `caption` | `text` / `window` / `flash` | Colour (chip row: pink / lavender / gold / white / black / custom-from-media-average), Glow, Size | **canvas only** (owner call, 2026-09-07): `addBlock` forces the target to the canvas, a word inside one tile is too small to read. Dragged on canvas; a new one starts centred across at 78 % of the height, clear of the stamp corner. The colour is the ink: the word is filled with it, the glow takes it, and the rim under the letters flips to whichever of ground and ink reads against it, so a black caption is legible on a bright frame. Captions get two colours a tint does not (`CAPTION_COLOURS` in `blocks.js` = the tint three plus white and black); a wash over the whole picture never wants either. `window` = the text is a cutout mask showing the media through it over a plate in that colour. `flash` = a one frame full-plate word (block length 1), the word in whichever of ground and ink reads on the plate. Fonts, seven chips: Display (Bahnschrift Condensed / Arial Black / Impact stack), Mono, Hand (`'Segoe Print', 'Bradley Hand', cursive`) are system stacks and never change, so an old remix draws as it did. Block (Anton), Script (Pacifico), Round (Fredoka) and Pixel (Press Start 2P) ship with the room as latin subset woff2 under the SIL Open Font License (`assets/fonts/`, about 80 KB, licence next to each file) so the word looks the same on the phone that made the gif and the phone that opens it. `engine/fonts.js` loads them with the `FontFace` API, cached and resolving either way; the preview repaints when a face lands and an export waits for them the way it waits for the decode. A face that will not load leaves the fallback in its stack drawing, never a blank frame. |
| `focus` | `ring` / `dark` | Radius | a soft circular focus that travels along a **path dragged on the canvas** over the block's duration; outside the ring is blurred (`ring`) or darkened (`dark`). |

Effects on a tile target render **inside that tile's rect** (clip). Canvas-target effects render on
the composite after tiles.

## Stamp + code

- Every export carries the **stamp**: a small "cclabs.app/remix" pill (pink on ground, bottom
  centre, 6 px inset at output res, opacity 0.9). It cannot be removed or moved. The **remix
  code** `CCP-XXXX` (4 chars from `23456789ABCDEFGHJKLMNPQRSTUVWXYZ`, seeded from the project)
  is NOT on the picture any more: it shows on the code chip in the app, and "same code = same
  dice rolls" still holds for anyone who types it in.

## Export

- **Gif**: `gifenc` (vendored ESM, `remix/vendor/gifenc.esm.js`, copied verbatim from
  `ConditioningControlPanel/Resources/web/dtrh/vendor/gifenc/gifenc.esm.js`) in a **classic
  Worker** file (`remix/engine/encode.worker.js`, `import` via `type:'module'` worker) — frames are
  rendered on the main thread at output res (`renderFrame`) and transferred as RGBA buffers. Global
  palette from a sample of 8 frames (quantize 256, `format:'rgb444'`), per-frame `applyPalette`.
  Delay = 1000/15 ≈ 67 ms (gif delays are in 10 ms units: alternate 60/70 so 15 fps averages).
- **Size meter**: `estimateSize()` encodes 6 evenly spaced frames and scales by frame count; shown
  as `x.x / 10 MB` (Discord's gif cap). Over 10 MB the meter turns gold and export offers
  "shrink" (drops to 12 fps, then 2/3 res) — a one-tap fix, never a settings page.
- **Video**: `canvas.captureStream()` + `MediaRecorder` (webm on Chromium, mp4 on Safari) playing
  the loop 3 times. Offered as the second button, gif is first.
- Output filename `remix-CCP-XXXX.gif`. Download via `<a download>` on desktop; on iOS the blob
  opens in a new tab with a "hold to save" hint (Safari refuses `download`).

## Vibes (the on-ramp after the first drop)

Shown once per session after media lands, skippable. Each fills the timeline; user tweaks after.

- **Grow**: layout grow, stageMs 600, tint creep block over the last 60 % of the canvas, loop snap.
- **Haunt**: grow, a ghost glitch block over the second half of the loop (strength 55), a tint creep
  over the last third, loop snap. (It replaced **Sink**, which was a stack of drains.)
- **Flash Deck**: flat, one tile at a time (each tile full frame for 15 frames, sequential), a
  caption flash between every cut using words from the caption text split on spaces (fallback
  `drop / obey / good girl`), loop clean.
- **Did You See It**: grow slow (900), one caption flash at ~62 % of the loop, focus ring drifting
  across the mosaic, loop seamless.
- **Surprise me** (dice): never rolls a shelved effect. Seeded from the remix code: it picks a vibe, then re-rolls every effect's
  mode + one knob, the spiral style and the tint colour. Dice on the top bar re-rolls
  (new code). Dice inside a panel re-rolls only that panel.

## Undo

Snapshot-based: `project.commit(label)` pushes a JSON snapshot; `undo()` / `redo()` restore.
Media bytes are not in the snapshot (referenced by id). Cap 50.

## Engine API (module `remix/engine/index.js`) — the contract between the two lanes

```js
import { createProject } from './engine/index.js';
const p = createProject({ orientation:'landscape' }); // 'portrait' | 'square'

// media
await p.addMedia(fileOrBlob)  // -> { id, name, kind:'gif'|'video'|'image'|'spiral', w, h,
                              //      srcFrames, srcDurMs, thumb: HTMLCanvasElement (120px wide) }
                              // decodes (worker where possible), rejects with a readable message
p.removeMedia(id)             // also removes its tiles + blocks
p.media                       // [] live array (read only)

// tiles + layout
p.addTile(mediaId)            // appends, re-derives rects, returns tile
p.removeTile(tileId); p.moveTile(tileId, toIndex); p.setPlayMode(tileId, mode)
p.setLayout({ mode:'grow'|'flat'|'shuffle'|'mirror', stageMs })
p.setOrientation('landscape'|'portrait'|'square')   // re-decodes, keeps everything else
p.setDuration(sec)            // 3|5|8, blocks are clamped/scaled proportionally
p.setLoop('clean'|'snap'|'seamless')
p.setStampCorner('br'|'bl'|'tr'|'tl')

// blocks
p.addBlock({ effect, mode, target, start, end, params })  // returns block (id assigned)
p.updateBlock(id, patch); p.removeBlock(id)
p.blocks; p.tiles; p.layout; p.loop; p.size {w,h}; p.frames; p.fps; p.code

// caption + focus geometry (rule 3: set from canvas drags)
p.setCaptionPos(blockId, {x,y})            // fractions
p.setFocusPath(blockId, [{x,y},...])       // fractions, resampled to the block length

// rendering
p.renderFrame(i, ctx2d)       // draws frame i at output size into the given 2d context
p.frameAt(i) -> ImageData     // for the encoder
p.play(); p.pause(); p.seek(i); p.playing; p.frame
p.on('frame', fn(i)); p.on('change', fn()); p.on('media', fn())   // returns unsubscribe

// vibes + dice
p.applyVibe('grow'|'haunt'|'flashdeck'|'didyouseeit')
p.surprise()                  // new code + full reroll
p.rerollBlock(id)             // reroll one panel

// export
await p.estimateSize()        // bytes
await p.exportGif({ onProgress }) // Blob
await p.exportVideo({ onProgress }) // Blob, or throws if MediaRecorder is missing
p.commit(label); p.undo(); p.redo(); p.canUndo; p.canRedo
p.toJSON(); createProject.fromJSON(json, mediaMap)
```

Pure modules (no DOM, node-testable): `engine/layout.js` (rects, adjacency), `engine/clock.js`
(resample, play modes), `engine/vibes.js` (recipe → state), `engine/rng.js` (seeded, mulberry32),
`engine/code.js` (remix code ↔ seed), `engine/blocks.js` (ramp, clamp, validation).
DOM modules: `engine/decode.js` (gif via omggif in a worker, video via `<video>` seek + canvas,
images via `createImageBitmap`), `engine/effects/*.js` (one file per effect,
`render(ctx, frameCtx, block, progress)`), `engine/render.js` (compositor),
`engine/encode.worker.js` + `engine/export.js`.

## Auto (the default page: add, roll, save; caption is opt in)

Owner pivot, 2026-09-07: "lets do the work for them ... 'load your gifs and press the button' ...
with an arrow to go back to the previous gen ... the last step must be the captioning (skippable).
The flow would be 1 add gifs or images, 2 roll till you like what you see, 3 caption and
eventually export." The page at `/remix/` IS this flow. The editor below is one link away.

### The four steps (`remix/ui/auto.js`, `remix/auto.css`, `#auto` in `index.html`)

1. **Add**: the page is the drop zone (drop anywhere, paste, or the Add button). Gifs, pictures,
   short videos, or a zip of them, as many as they like (the pool, below), nothing uploaded. The
   moment the first file is in the room rolls once by itself and moves to Roll; the rest of the
   batch comes in behind it and, once it is all in, the same code rolls again in place
   (`roll(seed, { replace: true })`, so the history keeps one entry). That first file also sets the canvas shape (`pickOrientation(w, h)`: over 1.2 wide
   is landscape, under 0.83 is portrait, the rest is square). Only ever the first one, silently,
   and a hand on the orientation chip beats it for good. A quiet "Open the editor instead" link
   sits under the card.
2. **Roll**: the stage plays the composition. Under it (phone) or beside it (desktop, landscape
   phone): the pink **Roll again** pill with the dice (56 px, full width), a back arrow and a
   forward arrow flanking it, then the code chip `CCP-XXXX` (tap copies), the orientation and
   length chips and the size chip. Under them a quiet "have a code?" link opens the code field (a
   friend's code on your gifs) with a close beside it; on a phone the open field rides `--kb` over
   the soft keyboard, and leaving Roll closes it again. "Next" goes straight to Save; "Caption it"
   above it opens the caption step first (opt in, off by default; it reads "Edit the caption" once a
   word is on); "Open the editor" is the quiet link. The step strip `Add  Roll  Save` sits in the
   header (its own row on a phone); the Caption frame appears in it only while you are on that step
   or a caption is on the picture. Press = one roll; **hold** = a roll every 700 ms until the
   finger lifts. **Swipe** the stage left for the next roll, right for the one before; **tap** it
   to play or pause. Arrow keys walk the history on a desktop.
   History: every roll pushes `{ seed, patch }`; back and forward walk it; cap 20; a new roll
   after going back drops the forward branch (browser history).
3. **Caption** (opt in, from the Caption it button on Roll): the word field (40 letters, `--kb` lifts it over the soft keyboard),
   text / window / flash chips, the three font chips, Skip and Done. The block is made on the way
   in with an empty word and dropped on the way out if the word is still empty, so Skip and an
   empty Done leave nothing behind. Always a canvas block, centre bottom by default, draggable on
   the stage (the same handle the editor uses). A roll keeps the caption.
4. **Save**: the existing export sheet. Closing it lands back on Roll with everything intact.

Escape hatch: "Open the editor" switches to the full HUD + timeline UI on the SAME project (every
block the roll produced is on the timeline and editable); a "Back to Auto" arrow sits top left
there. `?editor=1` boots into the editor. One stage serves both: `#stage-box` moves between the
editor's centre and the Auto page, so the clock never stops and a roll never shows an empty
canvas. Samples and the vibes screen belong to the editor only.

Feel: the old frame stays on a ghost canvas and fades over 200 ms while the stage settles in from
a hair smaller; the pill has a pressed state and the dice tumbles; the code chip flips. All of it
is off under `prefers-reduced-motion`.

### autoCompose(state, seed) -> patch (`remix/engine/auto.js`)

Pure and deterministic from `(seed, tile count, media aspects, canvas length, orientation)`: same
code, same media, same composition. Returns `{ layout: { mode, stageMs }, loop, blocks }`, the
shape every other patch has; the project's `roll()` applies it as one undo step and keeps any
caption block. Blocks only from tint, spiral, focus and glitch. **Never drain. Never caption.**
Layouts come from the registry `LAYOUTS` in `layout.js` (`[{ id, name, minTiles, maxTiles,
cost }]`): a new layout takes a row there and joins the roll at `layoutDefault` weight without an
auto.js edit; a layout the band tables know but a band leaves out does not roll in that band. A
motion layout takes its row by being listed in `MOTION_LAYOUTS`, which `LAYOUTS` merges in.

Weights, all in `AUTO_WEIGHTS` at the top of auto.js:

| gifs | layouts (weight)                                              | stage (ms) |
|------|------------------------------------------------------------------------------|------------|
| 1    | mirror 5, flat 2                                                              | 600 or 900 |
| 2-3  | grow 4, spread 4, stack 3, swallow 3, flat 2, mirror 2, slide 2, cover 2, ripple 1, shuffle 1 | 600 or 900 |
| 4-5  | grow 4, slide 4, ripple 3, shuffle 3, stack 3, spread 3, swallow 2, cover 2, flat 1, mirror 1 | 400 or 600 |
| 6-8  | grow 4, slide 4, ripple 3, shuffle 3, stack 3, spread 3, swallow 2, cover 2, flat 1, mirror 1 | 400 |

The motion layouts need two gifs, so one gif rolls none of them. **Cost:** every registry row
carries `cost` (`cheap` | `mid` | `dear`). A dear layout puts a lot of rects on every frame, and
rects cost bytes in the gif, so when the project's **Discord-safe** flag is on its weight is
halved (`AUTO_WEIGHTS.dearPenalty`, 0.5). Discord-safe is project state (`setDiscordSafe`), saved
with the project, not a variable inside the export sheet.

Aspect nudge +1: media shaped like the canvas push flat, the other shape pushes grow. Block count
1 : 2 : 3 = 3 : 4 : 2. Effects tint 4, glitch 3, spiral 2, focus 2, picked without repeats (so
never two spirals). Tint creep 3 : wash 1, colour pink 3 / lavender 2 / gold 1; glitch ghost 3 /
double 2; focus ring 3 / dark 1; spiral over 3 / through 1. Spans as a share of the
loop: creep .35-.6 (anchored to the end of its room), wash .25-.4, spiral .3-.5, glitch .3-.5,
focus .4-.6. Knobs roll 35..70, never the extremes. Focus and ghost need two gifs (ghost falls
back to double). Budget: covered frames at most 70 % of the loop and one clean second (at the
head 65 % of the time, else the tail); the longest block is trimmed from its start until the union
fits. Loop: clean 40, snap 30, seamless 30, snap only when a tint ends within 3 frames of the end.

Quality gate (`passesGate`): 1-3 blocks inside the frame count, no drain or caption, at most one
spiral, coverage under budget, a clean second. A failing candidate rerolls with `seed + 1` up to
three times, then the last candidate ships. Tests: `remix/test/auto.test.mjs`.

### The pool (owner ask, 2026-09-08: "load more than 8 gifs, even a zip")

"either click on some specific images to use them (highlight and checkmark, max 8) or just click
roll to gen at random picking random images from the loaded pool ... a dropdown that opens a grid
manager ... the selected images get shown on the top of the expandable section, like they are
pinned. If none are selected those spots stay empty."

- **Pool vs canvas.** Every file dropped lands in the pool (`remix/ui/pool.js`, cap `POOL_CAP`
  100) as one small frame (`probeMedia`: 144 px square, first frame only, a few KB each). Only
  the picks are decoded to the working set (about 39 MB per five second gif, so never more than
  eight). The engine never sees the pool; `pickSet` in `engine/pool.js` is a pure picker and
  `project.setMediaSet(ids)` swaps the canvas set (graveyard first, so a walk back is cheap).
- **Pins first, the rest roll.** A tap on a thumb (strip or grid) pins it: pink rim, pink tick.
  Pins take the first slots in pin order on every roll; the remaining slots up to the **count**
  roll from the rest of the pool, shuffled by the code. Eight pins at most. With eight or fewer
  gifs and no pins the room behaves exactly as before: the whole pool, drop order, every roll.
- **The count** ("use N", stepper in the grid): how many gifs each roll puts on the canvas.
  Defaults to min(pool, 8), never below the pins, never above the pool. A pin or a count change
  applies as a new roll on the same code after 220 ms, so quick taps become one roll.
- **The strip** under the stage: one slot per gif in use, pins first with the tick, then this
  roll's picks, then dashed empty seats (only while a count is higher than what is in; a tap
  opens the grid). Then the count button: "8 OF 12" when the pool is bigger than the count, "5
  GIFS" otherwise, chevron flips while the grid is open.
- **The grid** (`.pool-host`, bottom sheet on a phone, centred panel on a desktop, escape or the
  scrim closes it): the stepper, Add more, "Let them all roll (N kept)" when pins exist, then
  every gif in the pool as a square with the pink tick on pins and an "in this roll" foot on the
  current picks, and a remove x. Removing a gif that is on the canvas rolls the same code again.
- **The zip** opens in the browser (`engine/zip.js`, `DecompressionStream`, 100 entries at most,
  media only, `__MACOSX` and dot files skipped) and its files join the drop as if dropped one by
  one. Desktop file picker accepts `.zip`; the phone picker keeps images and videos so the Photo
  Picker still opens (a zip can still be dropped or shared in).
- **The arrows** still walk the history: each roll entry remembers its set, `peekRoll(dir)` lets
  the strip re-decode an evicted gif before `rollBack`/`rollForward`. The pill reads "Loading"
  (breathing label) while picks decode; a roll asked for mid-decode waits and goes last.
- **The editor** keeps its eight on the canvas: new drops there go straight on, the rest wait in
  the pool ("Eight on the canvas. The rest wait in the pool.").

### Phone first

Portrait phone: header (wordmark + the step strip on its own row), stage at its aspect (46dvh
cap), the pill row, the chips, Next. Everything above the fold on 390 x 844 with a landscape
stage. Landscape phone and desktop: stage left, the controls in a 300 px column on the right.
The caption sheet is a bottom sheet on a phone, the column on a desktop.

## Doors (`remix/ui/doors.js`, `remix/doors.css`)

Three ways out of the room and into the rest of the building, in this order: **the Loom** (free,
right now), **the Intake** (free weekly, with an account) and the **Conditioning Control Panel**
(free download). The data lives in `DOORS` and every link is absolute https on cclabs.app with
`from=remix` on it, so the visit can be counted where it lands.

One card, `doorCard(door, ctx)`: the poster with a shade at the bottom, the eyebrow (mono,
uppercase, pink), the title, the second line and a small round chevron. Room tokens, 14 px
radius, a 1 px `--line` border, no serif.

- **Desktop.** Hover or keyboard focus starts the clip inline (`<video muted loop playsinline
  preload="none">`, the `src` assigned on that first hover so nothing loads until then). Leaving
  pauses it and the poster is back. A click opens the link in a new tab (`rel="noopener"`).
- **Phone** (`html.is-mobile`). A tap opens a bottom sheet in the room's own sheet style: the clip
  playing, the title, the second line, one big pink button and a close. The scrim closes it.
- **Reduced motion.** No hover autoplay on a desktop, the poster stays and the click still works.
  The phone sheet still plays, because a tap asked for it.

Two placements:

- **The deck** (`mountDeck`, `#side-doors` at the foot of the Roll column). One door with three
  dots under it, starting on the Loom. Every roll walks to the next one, the pill and the arrows
  alike, with a 200 ms crossfade or a cut under reduced motion. It listens on `ctx.on('roll')`.
- **The after-save strip** (`doorStrip`, `export-sheet.js`). Once a save has finished, a strip
  appears under the two save buttons: the line "Your gif carries the address. This is where it
  leads.", a quiet "not now" and the three cards in a row that scrolls sideways on a phone with
  no bar on it. "not now" hides it and remembers that in `sessionStorage` under
  `remix.doors.notnow` for the session; every storage read and write is wrapped, so a locked jar
  just forgets.

Never a hostage: nothing opens by itself, nothing blocks, no timers and no "before you go". The
room works exactly the same with every door ignored. Tests: `remix/test/doors.test.mjs`.

## UI (module `remix/ui/*`, `remix/index.html`, `remix/remix.css`)

This section is the **editor**: the full HUD + timeline UI behind "Open the editor" and
`?editor=1`. It shares the project and the stage with the Auto page above.

- Tokens = the Arcademy shell's (`labs/deep-end/index.html` `:root` block is the exact copy):
  ground `#14142B`, navy `#1A1A2E`, panel `#252542`, panel2 `#2E2E55`, line `#3A3A5E`, ink
  `#F2EBDD`, ink-dim `#B9B3CE`, ink-faint `#8A84A8`, pink `#FF69B4`, pink-deep `#D4488F`, lav
  `#B8A6E8`, gold `#F0C24B`, slate `#7A7594`. Display face `'Bahnschrift Condensed','Arial
  Narrow','Arial Black',Impact,sans-serif`; body `'Segoe UI Variable Text','Segoe UI',system-ui,
  -apple-system,sans-serif`; mono `'Cascadia Mono',Consolas,monospace`. **No serif, ever.** No
  Google Fonts (system stacks only, the page must work offline).
- **Mockup is the law**: the four screens at
  `https://claude.ai/code/artifact/b6cc8132-37fc-4972-ac58-8c079273973d` (phone page) and the
  canvas at `https://claude.ai/code/artifact/ccb5cd26-519b-448f-b55a-85b6ed74ed10`. Working
  copies of the artboard HTML are in `remix/mockup/` (read them, do not ship them).
- Top bar: wordmark, "NOTHING LEAVES YOUR BROWSER" chip, orientation segmented control, length
  chip, size meter, dice, undo/redo, **Export** (pink primary).
- HUD: 7 large buttons (Add [+ bin], Tint, Spiral, Glitch / Caption, Focus, Layout), four on one rail and three on the other. Where they
  sit follows the canvas: a portrait canvas gets the two columns on the sides of the stage, a
  landscape or square canvas two rows of four above and below it. Phone: that rule as is. Desktop:
  the mockup's side rails for landscape and square; a portrait canvas takes the rows only when the
  rails would leave a smaller stage than the rows would (both measured, larger stage wins). A short
  crossfade on change, none under reduced motion. Buttons are 64 px min (56 px icon-only on phones
  under 400 px wide, label as tooltip / aria-label), 44 px touch floor everywhere.
- Canvas: the live render (`renderFrame` into a display canvas scaled to fit), tiles selectable by
  tap (pink outline), a small x on the selected tile, drag to reorder (swap rects on drop), drop
  zone for files everywhere on the page. Top-left of the stage: the **canvas tab** (bracket glyph +
  CANVAS, 44 px hit area, on every size): tap = select the whole canvas (tile deselected, panels
  target the canvas, pink outline for a moment); hold 350 ms = the canvas strip (phone: the strip
  sheet; desktop: the canvas lane lights up in the timeline). Beside it the stage badge
  (`GROW · 3 of 5 · frame 41`; phone and narrow stages: `GROW 3/5`, the frame number lives in the
  transport row). Caption drag, focus path draw (press and move), stamp corner tap.
- Media strip under the canvas: thumbs, dashed add tile, count chip, expandable (down arrow) to a
  scrollable list with per-item remove. Bin mode: tap bin, then tap a tile/thumb to remove.
- Timeline: ruler in seconds, playhead (drag to scrub), **canvas strip** (canvas-target blocks +
  loop marker at the end), and **the selected tile's strip** (filmstrip thumbs of that tile's
  frames + its blocks + play glyph). Blocks are drag to move, drag either edge to resize, tap to
  open the panel. A block one frame wide renders as a pin. Desktop only as a docked row; on a phone
  the strips live on the **strip sheet** (below).
- Panels: one floating panel at a time, anchored to the HUD button (desktop) or a bottom sheet
  (phone). Chips row + up to two sliders + a small dice + "apply to: this gif / whole canvas"
  toggle + a hint line. Opening a panel for an effect that has no block on the current target
  **creates a default block** (canvas: full length; tile: from its enter frame to the end;
  caption flash: 1 frame at 62 %).
- Vibes screen: after the first media lands in the editor, the four cards + Surprise me + skip.
  (Auto never shows it: the roll is the on-ramp there.)
- Export sheet: preview loop, size line, `Save gif` primary, `Save video`, `Discord-safe` toggle
  (caps the file at 8 MB via the shrink ladder, nothing else), share hint "paste it anywhere, the stamp travels with it".
- Keyboard: space play/pause, `[` `]` nudge block, delete removes block/tile, ctrl+z / ctrl+shift+z.
- Reduced motion: no ambient animation on the chrome; the render itself is the content.
- Phone: `100dvh`, safe-area insets, `?forcemobile=1` to preview the phone layout on desktop
  (coarse pointer detection otherwise). No docked timeline. A slim **transport row** under the top
  bar: play / pause, time + frame, undo, redo, loop chip. **Hold a gif** (350 ms, released without
  moving; a move over 14 px is the dock drag) and its strip rises on a bottom sheet over the dimmed
  page: ruler + scrub head, filmstrip + blocks + play glyph. **Hold the canvas tab** for the canvas
  strip (blocks + loop marker + loop chip). Tap a block and its knobs open inside the same sheet
  under the strip (no second sheet). Tap outside, the x, or Esc closes. While a sheet is open the
  HUD and media strip step aside and the stage takes what the sheet leaves (floor 130 px), so the
  selected gif stays in view where the height allows. The media strip stays, the tile x stays, bin
  mode stays.
- Empty state (the editor, before any drop): the canvas shows a sample mosaic built from 3 bundled tiny loops
  (`remix/assets/sample/*.gif`, ≤ 60 KB each, abstract pink/lavender gradients drawn by a script,
  no third-party content) so the first screen is never blank, with "drop your gifs" over it.
  The samples clear on the first real drop. Auto has no samples: its Add step is the empty state.

## Non-negotiables

- No third-party media ever bundled or fetched (no Scrolller, no feeds). Only the user's files.
- No network calls at all from the page (assert with a test: zero requests off-origin).
- Works on iPhone Safari (no `ImageDecoder`, no `download`, one hardware decoder at a time,
  `OffscreenCanvas` present since iOS 16.4). Every decoder path has a main-thread fallback.
- No dependency beyond `omggif` (`/assets/vendor/omggif/omggif.module.js`, already on the site)
  and `gifenc` (vendored). No bundler, no framework, plain ES modules.
- Copy: no "banked"/"bank". Diegetic, short, no AI tells, no em-dashes, no emoji as UI.
- Every PR ≤ 600 changed lines (split by module). Report `git diff --stat` in the PR body.
- Files: `remix/index.html`, `remix/remix.css`, `remix/ui/*.js`, `remix/engine/**`,
  `remix/vendor/gifenc.esm.js`, `remix/assets/**`, `remix/test/*.mjs` (node:test for pure
  modules; a Playwright/CDP smoke is optional and must kill its browser after each shot).

## RAM rules (owner rule, 16 GB shared machine)

One headless browser alive at a time and killed after every shot. Your own `python -m http.server`
or `npx serve` only, on your assigned port. No background loops. No subagents.
