# Remix Room UI

Client-side gif remixer at `/remix/`. Plain ES modules, no bundler, no framework. Contract: `../REMIX-SPEC.md`;
where the engine differs, `../engine/API-NOTES.md`.

The page opens on the **Auto page** (`auto.js`): add gifs, roll, save (caption is opt in). The **editor** (everything
else in this file) is behind the "Open the editor" link and `?editor=1`, on the same project.

## Files
| File | What |
|------|------|
| `../index.html` | Shell: `#app` (the editor: top bar with the Back to Auto arrow, transport row (phone), HUD rails, stage, media strip, timeline), `#auto` (the Auto page), vibes screen, export sheet, drop veil |
| `../remix.css` | Editor styles. Desktop grid first; `html.is-mobile` block turns it into the phone layout (no media queries for that, so `?forcemobile=1` works) |
| `../auto.css` | Auto page styles, same tokens, phone first (`html.is-mobile` again); hides `#app` while `data-screen="auto"` |
| `auto.js` | The Auto page: steps (add, roll, save; caption opt in via the Caption it button), the Roll pill (press / hold), arrows, swipe and tap on the stage, code chip + the "have a code?" field, caption sheet, mount / unmount of the shared stage |
| `app.js` | Boot, shared `ctx` (selection, panel state, commit/undo, toasts), top bar, keys, drop, sample loops, `?debug=1` handle |
| `engine-bridge.js` | The one import line for the engine: re-exports `createProject`, `canRecordVideo`, `codeToSeed`, `normalizeCode` from `../engine/index.js` |
| `hud.js` | The eight HUD buttons + icons; active / has-block / disabled states; `place()` picks rails / rows / cols from the canvas orientation |
| `canvas-view.js` | Display canvas + DOM overlay: tile hits (tap / hold / drag), the canvas tab + stage badge, dock drag, stamp corner, caption handle, focus path drawing, empty prompt |
| `media-strip.js` | Thumbnails under the stage, on/off canvas, media list |
| `timeline.js` | `initLanes`: the shared strip kit (ruler, lane with filmstrip + blocks, block drag / resize / pins, scrub, play glyph menu, loop chip). `initTimeline`: the docked timeline (desktop) and the phone transport row |
| `strip-sheet.js` | Phone: the strip sheet that a hold on a gif or the canvas tab brings up; a block's knobs open inside it |
| `panels.js` | Effect panels (desktop anchored, phone bottom sheet, or inline inside the strip sheet), target pill, modes, knobs, dice |
| `vibes.js` | First-drop vibe picker (Grow, Haunt, Flash Deck, Did You See It, Surprise) |
| `export-sheet.js` | Preview, size line + shrink ladder, Save gif / Save video, Discord-safe, iOS hold-to-save |
| `doors.js` | The three doors (Loom, Intake, desktop panel). One `doorCard` in two places: `mountDeck` under the Roll column (one door, three dots, the next one on every roll) and `doorStrip` under the export sheet's save buttons once a save has landed. Desktop hovers a card and the clip runs inline; a phone taps it and a bottom sheet comes up with the clip and one button. "not now" on the strip is remembered in `sessionStorage` (`remix.doors.notnow`) for the session. Nothing opens by itself. Styles in `../doors.css` |
| `dropzone.js`, `keys.js`, `toast.js` | Drop veil + paste, shortcuts, toasts |

## Run
From the site root: `python -m http.server 8902` then open `http://127.0.0.1:8902/remix/`.
Phone layout on desktop: `?forcemobile=1`. The editor straight away: `?editor=1`. `?debug=1` puts `window.__remix = { project, ctx, state }` on the page
for harnesses (`hud()` returns the HUD placement and the stage sizes it measured). Engine tests: `node --test "remix/test/*.test.mjs"` (quotes matter on Node 24). The pool (`pool.js`: the strip under the Auto stage, the grid, pins and the count) owns every dropped file as a 144 px probe and decodes only the picks; see REMIX-SPEC "The pool".

## Auto page (auto.js, ../auto.css)
`state.screen` is `'auto'` (default), `'editor'` (`?editor=1`, the link, the arrow) or `'vibes'`. `ctx.setScreen`
switches: `#app[data-screen="auto"]` is `display:none` and `#auto` is a fixed full-page flex column. One stage:
`auto.mount()` moves `#stage-box` into `.auto-stage`, `unmount()` puts it back at the top of `.centre`;
`canvas-view.js` keeps its references and its ResizeObserver, so the same canvas, overlay, stamp button and
caption handle serve both screens (the tile hits, canvas tab and badge are hidden by `#auto` rules).

Steps live in `auto.js` (`go(step)`, `render()`): **add** (the page is the drop zone; `ctx.addFiles` takes the canvas
shape from the first decoded file with `pickOrientation(w, h)` unless a hand has been on the chip, rolls on
the first decoded file, goes to roll, and rerolls the same code with `{ replace: true }` once the batch is in),
**roll** (`p.roll()` on pointerdown, a 450 ms delay then every 700 ms while held; `p.rollBack/rollForward` on the
arrows, on a 50 px horizontal swipe over the stage, and on the arrow keys; a tap under 8 px on the stage toggles
play; the code chip copies; the quiet "have a code?" link opens the field, which rolls `codeToSeed(value)`
and folds away on load, on its close, on Esc and on leaving the step), **caption** (the block is made on entry
with an empty word and removed on exit if still empty; `state.selectedBlockId` is set to it so the stage handle
shows; the sheet is the right column on a desktop and a fixed bottom sheet at `bottom: var(--kb)` on a phone),
**save** (`exportSheet.open()`; when it closes `render()` sees it shut and lands on roll). Every roll snapshots
the old frame onto `.roll-ghost` inside `#stage-fit` and fades it over 200 ms while the stage settles in
(skipped under reduced motion). Esc on the caption step returns to roll. Keys: in Auto, Delete and `[` `]` do
nothing, Space on a focused button is the button's own press, arrows walk the roll history.

## Before the first drop (editor)
The engine's sample loops (`../assets/sample/*.gif`) play on the canvas under the prompt so the first screen is
never blank (`state.sample`). They load only in the editor (boot with `?editor=1`, or switching to it on an empty
project). They are removed on the first real drop, paste or pick; the vibe screen then opens (editor only).

## Dock drag (canvas-view.js)
Pointer Events with `setPointerCapture`; the stage has `touch-action: none`. Editor-docking feel, like moving a tab
in VS Code.

- Lift: touch holds 350 ms (a move over 14 px first is a flick and cancels); mouse travels 6 px. A plain tap or
  click still selects the tile. Fewer than two tiles: no drag, the hold just outlines the tile.
- Hold vs drag (touch): at 350 ms the tile lifts (haptic, ghost). If the finger then moves over 14 px it is the
  drag below. If it is released without moving, the ghost is dropped silently, playback comes back as it was, and
  `ctx.holdTarget(tileId)` fires: on a phone that opens the tile's strip sheet, on desktop it selects the tile and
  lights its lane in the docked timeline. Lifting pauses playback and, in grow, seeks past the
  last tile's entrance so every tile is on screen; play resumes after the drop.
- Ghost: a snapshot of the tile region follows the pointer; the source tile dims to 40 %.
- Over another tile: pink outline plus a `drop to swap with gif N` pill. Drop swaps (`p.swapTiles`).
- Split: still over the same tile for 2 s (the timer restarts on entering a different tile), or 500 ms inside the
  target's outer 15 % band. The pointer picks the side; the 30 % centre square keeps swap. The 120 ms preview comes
  from `p.previewDock` (tile hits animate, a dashed landing box marks the moving tile). Pill reads
  `drop to dock left of gif N`. Drop docks (`p.dockTile`). Leaving the target drops back to swap mode.
- Cancel: Esc, or a drop outside the stage or on the source. The ghost snaps back (no animation under reduced
  motion; previews still show).
- Every drop is one undo step (`Docked gif 1 above gif 4`, `Swapped gif 1 and gif 4`).

## Canvas tab
Top-left of the stage, beside the badge: a bracket glyph + CANVAS, a 44 px hit area (`::before` halo), visible on
every size. Tap = select the whole canvas (tile deselected, panels target the canvas, the stage outlines pink for a
moment). Hold 350 ms = `ctx.holdTarget('canvas')`: the canvas strip sheet on a phone, the canvas lane scrolled
into view and lit in the docked timeline on desktop. A move over 14 px cancels. Enter / Space = tap.

## Stage badge
`GROW · 3 of 5 · frame 41` on desktop. On a phone, or on any stage narrower than 420 px, the short form
`GROW 3/5` (the frame number is in the transport row / timeline head). Under 200 px (`#stage-box.tight`, a strip
sheet with its knobs open) the badge hides and the tab is icon-only.

## HUD placement (hud.js `place()`, `#app[data-hud]`)
The buttons go where the canvas has the least use for the pixels. Phone: a portrait canvas gets two columns of
four beside the stage (`cols`, 64 px buttons, 56 px icon-only under 400 px wide with the label as `title` /
`aria-label`); a landscape or square canvas gets two rows of four above and below it (`rows`, 44 px targets).
Desktop: side rails (`rails`) for landscape and square; for a portrait canvas both layouts are measured
(`stageFor` with the rail width 132, row height 80, and the strip) and the larger stage wins, so `rows` only
appears when the rails would squeeze the stage narrower than the rows would. The mode changes with a short
crossfade of the buttons (`.hud-swap`, nothing under reduced motion). `?debug=1`: `__remix.hud()`.

## Timeline (desktop)
Lanes size to their content (blocks stack when they overlap), the timeline scrolls when it outgrows its row, and
the head and ruler stay sticky. Lanes are `touch-action: pan-y`: a finger scrolls the timeline, and a block drag or
scrub only claims the pointer once the touch has moved sideways (a mouse scrubs from the press). Lane rows are 40 px
on phones (30 on desktop) with 20 px edge handles that reach 8 px outside the block. Flash pins keep label room so neighbours do not overprint. The play-mode glyph menu
is fixed-position and flips above the block when it would run off the window.

## Phone
`html.is-mobile` is set on real phones and with `?forcemobile=1`. The page never scrolls; only an open sheet does.
There is no docked timeline on a phone. Under the top bar sits the transport row (`#transport`: play / pause,
`2.73s · f41`, undo, redo, loop chip). The stage, HUD and media strip share one grid (`.centre` is
`display: contents`, `grid-template-areas` per `data-hud`), the media strip always last.

Strips live on the strip sheet (`strip-sheet.js`): hold a gif, or hold the canvas tab, and a bottom sheet rises
over a dimmed page with that target's ruler + scrub head, and its lane (filmstrip + blocks + play glyph for a gif;
blocks + loop marker + loop chip for the canvas). It is the same renderer as the docked timeline (`initLanes`), so
block drag / trim / pins behave the same. Tap a block and its knobs open inside the same sheet under the strip
(`panels.open(effect, { inline })`; the target pill is static there). The sheet follows the selection: tap another
gif on the stage and its strip comes up. Tap outside, the x, or Esc closes it (the transport row, glyph menu and a
toast's undo stay live).

While a sheet is open (`#app.panel-open` or `#app.strip-open`) the HUD and strip step aside and the stage takes
whatever the sheet leaves: the sheet reports its height as `--sheet-h` on `<html>` (ResizeObserver) and the stage
is `100dvh - top bar - transport - --sheet-h`, floor 130 px. Toasts move up under the transport row. Safe-area
insets pad the top bar, the sides of the app, sheets and toasts. The soft keyboard sets `--kb` on `<html>` from `visualViewport` and the panel rides up by it. Add opens the file picker; the empty prompt shows the sample loops.

## Export
The sheet always runs the engine's fit ladder (`exportGif({ shrink: true })`), so a gif never lands over 10 MB;
Discord-safe lowers the cap to 8 MB. The shrink button under the size line steps `p.setShrink` (12 fps, then two
thirds size) ahead of time. While an encode runs the close button reads Cancel and aborts it (an `AbortController`
signal the engine checks every frame); Esc does the same. Save video is hidden when `canRecordVideo()` is false.
On iOS the encode runs first, then an "Open to save" button opens the result in its own tab (gif in an `<img>`,
video in a `<video>`) with the hold-to-save hint; the blob urls are let go after five minutes or on the next save.
`?noworker=1` points both module workers at a url that does not exist, so decode and encode take their main
thread fallbacks for real (the worker's error event fires, pending jobs reject, the main thread path runs).

## Escape ladder
Export sheet, then on the Auto page: caption back to roll. In the editor: vibes, glyph menu, strip sheet, strip menu, bin mode, open panel, selected block, selected tile. One Esc closes a
panel but keeps its block selected, so a harness needs two to get back to a clean stage.

## Help (help.js, help-content.js, ../help.css)
`help.js` injects `../help.css` itself and touches no other file's markup except to add its `?` buttons.

- **Tooltips.** One element, ink on panel, 12 px mono, arrow under the control. 350 ms on hover, at once on
  keyboard focus, gone on leave, blur, Esc, scroll or a pointer press. Never on a touch device
  (`(pointer: fine)` plus `is-mobile`). Nothing is wired to an element: `tipFor(el)` walks up from the event
  target and reads what the control is, so every re-render keeps its tips. It also parks the control's own
  `title` while a tip is up, and hands it back after, so the two never stack.
- **Hooks, in order.** `data-effect` or `data-hud`, then `aria-label`, then the printed words (`hudKey`,
  `panelKey`). Chips and knobs inside a panel resolve by their own text against `HELP[effect].modes` and
  `.knobs`. No lookup is by index, so reordering a rail or a chip row changes nothing.
- **Cards.** A `?` sits in every open panel header, one by the wordmark (the index of all cards, the `auto` card
  first), one by the Auto page's wordmark (the `auto` card) and a small one on the vibes screen. Touch: hold a HUD button 500 ms for its card, and the tap that ends the hold does not open
  the panel. Desktop: right click a HUD button. The card is a centred modal, a full screen sheet on a phone.
  Esc closes, Tab stays inside, focus goes back to whatever opened it.
- **Preview.** A throwaway project from `createProject` on the bundled sample loops (fetched once, decoded per
  card because `dispose()` lets go of a project's frames). Two tiles flat, three in grow for the layout card,
  the card's effect over the whole length. Mode rows switch it live with no commit. It plays while the card is
  open and is disposed on close: ten opens and closes leave the rAF count and the node count where they started.
- **Marks.** `decorate()` is idempotent and runs from a `MutationObserver` on the body plus `ctx.on('state')`,
  so a panel rebuild gets its `?` back on the next frame.
- `?debug=1` puts `window.__remixHelp = { tips, cards, tipFor }` on the page for harnesses.
