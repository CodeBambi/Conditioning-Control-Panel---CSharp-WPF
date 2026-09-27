# Back Room rendering and hosting budget

Historical measurements from the earlier room build at 1440 x 900, same entrance pose, September 2026. These are not measurements of the September 17 shared-station preview and do not establish minimum-spec FPS.

| Measurement | Before | After |
| --- | ---: | ---: |
| Draw calls | 492 | 249 |
| Submitted triangles | 1,132,268 | 566,728 |
| Cold startup response bodies | 12.10 MB | 9.94 MB |
| Vending and three sculpture GLBs | 3.12 MB | 0.96 MB |
| Six decoration prop GLBs (placed 2026-09-15) | 1.60 MB authored | 0.56 MB |

The cold total includes JS and art from the local no-cache test server, before HTTP compression. It excludes player media and game assets fetched on entry to a game. About 2.16 MB is saved per cold room load, roughly 216 GB per 100,000 cold loads. This is bandwidth, not a currency estimate.

## Device limits

- Room and the catalogue close-up: 30 FPS at rest, on one renderer (the close-up is a scissored second pass on the room's context, not a context of its own). Desktop movement/look may draw up to 60 FPS; phones stay at 30. Slot and wheel presentation is capped at 60/30 without changing settlement timelines.
- Room, slot and wheel: at most 1.5 million pixels on desktop, 900,000 on phones/low-memory devices. DPR is capped at 1.25/1. The room samples frame times and gradually lowers resolution to 60% after sustained slow frames. Shared seated views use that renderer; standalone station renderers retain their opening resolution. UI text stays at native resolution. Anti-aliasing remains enabled.
- Hidden room tabs stop their animation loop. Shared seated games keep the room running on the same renderer; only an exclusive held view stops it. Resuming resets input and clocks.
- Two minor cartridge transmission materials use their opaque reflective finish, avoiding a second whole-room refraction pass.
- All wall and ceiling screens share media sources. Output canvases are at most 384 px, with each source playing at its own frame delays up to 30 frames per second (was 12 until 2026-09-18: a 25-33 fps spiral lurched, and the next frame was timed from decode completion rather than from its due time, so it really ran at 8-9 uneven fps) and up to eight decode starts per rendered frame across visible sources in Full, four per 1/6 s in Performance. This is not a global decode-per-second limit. Original images are decoded before output downscaling. DOM effect images are outside this budget. Still images are capped at 512 px. At most two media files initialize concurrently. Exiting closes decoders.
- Sculpture lever previews share the already loaded catalogue geometry/materials. Swapping all three requires zero further asset requests.
- The coin shower is every fixture's from the reward pass (CONTRACT 10.22.A), not the three slot cabinets' alone, so seven idle instanced meshes stand where three used to: four more draw calls at instance count 0 until a fixture pays. They are built at boot rather than on the winning frame on purpose - a material three.js has never rendered is a material it has not compiled, and that compile belongs anywhere but the frame a jackpot lands on. The shower itself is one instanced draw of at most 64 coins for four seconds, and it draws nothing at all while the room is still.
- The six decoration props (three plants, three wall frames) are baked to one mesh per material when they are placed, so the ivy's 85 authored mesh nodes cost six draw calls, not eighty-five. All six together: 30 draw calls and 60,428 triangles. The entry pose at 1280 x 720 measures 277 draw calls with every prop on. Their two media frames feed from the same wall-picture sources and the same per-frame decode-start budget as the room's own screens; nothing new is fetched for them. Switching them off from the panel removes their draws. There is no new resolution scaler: the room's own frame-time sampler (render-budget.js) is what drops resolution on a slow device.

## Website hosting boundary

The room has a hosted playtest. The following publication recommendations are not a claim about its current response headers:

1. Serve room JS, GLBs, textures and media directly as static CDN files. Do not route asset downloads through an authenticated server function. Keep authoritative SP/game endpoints separate.
2. For the current stable asset filenames, emit ETag and `Cache-Control: public, max-age=0, must-revalidate`. A repeat visit can reuse unchanged browser bytes following a 304. Do not use year-long immutable caching on these mutable filenames.
3. When the web build adopts content-hashed filenames, use `Cache-Control: public, max-age=31536000, immutable` for those files. Keep HTML and the asset manifest revalidated. Enable Brotli/gzip for JS/CSS/JSON and verify real response headers and a warm reload in the deployed browser.
4. Package only referenced assets. Every GLB in room/assets/customization is referenced and placed; see that folder's README for the spot each one occupies. Player GIF sizes and traffic volume are separate inputs to a hosting estimate.

Animations make no server calls. Room media is dealt once; the floor bell reads on entry and station return, never by polling. Existing slot tapes batch game requests. Real hosting cost still needs traffic, CDN pricing, player-media volume and game endpoint measurements; a local GPU test cannot establish it.

## Rebuild and validation

Build-only compression tool: `../smoke/asset-build/compress-customization.mjs`. Install its pinned dependencies in that folder and pass separate original/output directories; every .glb in the original directory is rebuilt. Do not simplify the shipped outputs again. It preserves named interaction/lever nodes; the runtime uses the existing Meshopt decoder. Pass `--no-simplify` for a model whose flat media planes have to keep their exact UVs: the three gallery frames were packed that way, the three plants with the standard decimating pipeline. The flag applies to the whole run, so the frames and the plants have to be rebuilt from separate input directories.

Regression checks: room/slot Node suites plus `smoke/room-check.mjs`. Validate all three compressed sculptures on both room and active-slot pivots after rebuilding. A 390 x 844 browser check passes, but physical low-end Android/iPhone thermal, memory and touch-navigation testing remains necessary before claiming phone support.

## September 17 candidate optimization pass

Auto / Full / Performance is a presentation policy independent of Motion and Calm.
Auto starts conservatively on device hints, reduces work after a five-second
window with sustained misses, and recovers only after longer stability outside a
seat. Hidden-tab gaps reset sampling. Full retains the original pixel safety caps.

Performance caps room/cards presentation at 30 fps. Wall sources share four animation
advances per batch, at most six batches per second, with fair rotation. Browser
reward media caps overlap at four, Loom uses 384 px / 20 fps, and flowing SVG melt uses
one noise octave with 20 fps parameter updates. This is not a measured phone FPS gain.

Settled slot strips retain unchanged pixels and only paint visible neighbours.
Static content no longer uploads every 100 ms. The slot's live cells are patched into
the strip texture in place since September 18 (below); the room's idle reels still
upload a whole strip when they repaint.
Off-camera cosmetic room reel/bulb work is skipped without skipping event clocks.

## September 18 frame-cost pass (desktop testers reporting low FPS)

Measured with `smoke/perf-bench.mjs` (real Chrome off-screen, the smoke checks' fake
host, 1727 x 942 at DPR 1.25, the same desk both times: RTX 5080, so the numbers rank
costs, they are not a tester's FPS). Chrome's GPU process and the renderer's main
thread are the two things a weak desktop runs out of first, and both were being spent
on texture traffic rather than on triangles:

| Scenario | Before | After |
| --- | --- | --- |
| Rest, draw calls per frame | 394 | 361 |
| Rest, texture upload | 11.9 Mpx/s | 1.4 Mpx/s |
| Rest, GPU process CPU | 45% of a core | 22% |
| Rest, main thread busy | 19% | 11% |
| Slot seat, texture upload | 32 Mpx/s | 11 Mpx/s |
| Slot seat, GPU process CPU | 71% of a core | ~40-50% |
| Slot seat, main thread busy | 24% | 16% |
| Wheel seat, draw calls per frame | 217 | 195 |

What changed, in order of measured weight:

1. **Prize marquee** (`room/prize-marquee.js`): the scrolling patter repainted a 2048 x 256
   canvas 20 times a second with a blurred-shadow `fillText` and re-uploaded it. Hiding that
   one board took the GPU process from 54% to 24% of a core at rest. The patter is now painted
   once onto a strip one period wide and scrolled by UV offset, under a static frame plane.
2. **Loom backing canvas** (`shared/hypno/loom.js`, `arcademy/engine/loom/loomField.js`): the
   page's one field canvas was resized to every caller's backing, and the slot's 256 px spiral
   tiles against the room's 128 px loom discs resized it back and forth every paint (a WebGL
   drawing-buffer reallocation each time; 12% of the main thread on the seat). It now only
   grows, and each size renders into its own viewport.
3. **Slot reel strips** (`stations/slot/scene.js`): a live cell re-uploaded its whole
   3328 x 304 strip. Changed cells (up to six) are now written in place with `texSubImage2D`.
   Not through three's `copyTextureToTexture`: it reads unpack state back with `getParameter`
   on every call, and each read is a synchronous round trip to the GPU process (~1 ms).
4. **Bulb batches** (`room/fixtures.js`): keyed on geometry uuid, and the GLBs give every lamp
   its own geometry, so 25 + 28 one-instance batches. Keyed on station and lamp shape now.

Still on the table, by size: the stations' own draw calls (counter 94, wheel 90, cards 73,
EMIs ~60 at rest; a mesh-merge-by-material job in the GLB pipeline), the slot's apron ticker
(2048 x 88 repainted at 20 Hz; the marquee's strip trick applies) and the crown display
(512 x 128 at 20 Hz), and the room's idle reel strips. `perf-bench.mjs probe` is the tool.

Media transfer is streamed with 32 MiB/source, 4M pixel and 2,000 frame limits and a 10 s
load deadline. Header dimensions are checked before native decoding. Still fallback
reuses validated bytes and releases its blob URL. Card decks load visible ranks
first and retain at most eight 4 MiB sources, with unseen cache eviction. The 32 MiB
card bound counts compressed source bytes, not total browser/GPU memory; wall/slot
sources have separate budgets. Oversized or unsupported images use built-in art.

Soundtrack uses one streaming media element with a gain control, starts after a
user gesture, and pauses while suspended/hidden. It never decodes all five songs
into PCM buffers or preloads the playlist. Default music level is 15 percent of master.

The candidate has automated coverage; physical device, thermal, touch and visual
acceptance remain the owner's testing step. See the task handoff for exact results.
