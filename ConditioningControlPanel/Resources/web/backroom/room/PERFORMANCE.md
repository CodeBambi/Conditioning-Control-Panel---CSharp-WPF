# Back Room rendering and hosting budget

Measured locally at 1440 x 900, same entrance pose, September 2026. This is a browser workload comparison, not a minimum-spec FPS claim.

| Measurement | Before | After |
| --- | ---: | ---: |
| Draw calls | 492 | 249 |
| Submitted triangles | 1,132,268 | 566,728 |
| Cold startup response bodies | 12.10 MB | 9.94 MB |
| Vending and three sculpture GLBs | 3.12 MB | 0.96 MB |

The cold total includes JS and art from the local no-cache test server, before HTTP compression. It excludes player media and game assets fetched on entry to a game. About 2.16 MB is saved per cold room load, roughly 216 GB per 100,000 cold loads. This is bandwidth, not a currency estimate.

## Device limits

- Room and the catalogue close-up: 30 FPS at rest, on one renderer (the close-up is a scissored second pass on the room's context, not a context of its own). Desktop movement/look may draw up to 60 FPS; phones stay at 30. Slot and wheel presentation is capped at 60/30 without changing settlement timelines.
- Room, slot and wheel: at most 1.5 million pixels on desktop, 900,000 on phones/low-memory devices. DPR is capped at 1.25/1. Only the room samples its own frame times, so only the room's resolution falls, gradually, to 60% after sustained slow frames; the slot and the wheel keep the resolution they opened with. UI text stays at native resolution. Anti-aliasing remains enabled.
- Hidden room tabs stop their animation loop. Opening a game holds the room. Resuming resets input and clocks.
- Two minor cartridge transmission materials use their opaque reflective finish, avoiding a second whole-room refraction pass.
- All wall and ceiling screens share media sources. GIFs are at most 384 px and there is one global budget of 12 new frame decodes per second, shared fairly among visible sources. Still images are capped at 512 px. At most two media files initialize concurrently. Exiting closes decoders.
- Sculpture lever previews share the already loaded catalogue geometry/materials. Swapping all three requires zero further asset requests.

## Website hosting boundary

No website configuration or deployment is included. The room is currently a local preview. For web publication:

1. Serve room JS, GLBs, textures and media directly as static CDN files. Do not route asset downloads through an authenticated server function. Keep authoritative SP/game endpoints separate.
2. For the current stable asset filenames, emit ETag and `Cache-Control: public, max-age=0, must-revalidate`. A repeat visit can reuse unchanged browser bytes following a 304. Do not use year-long immutable caching on these mutable filenames.
3. When the web build adopts content-hashed filenames, use `Cache-Control: public, max-age=31536000, immutable` for those files. Keep HTML and the asset manifest revalidated. Enable Brotli/gzip for JS/CSS/JSON and verify real response headers and a warm reload in the deployed browser.
4. Package only referenced assets. The retired ivy, terrarium and other old catalogue GLBs are gone from the collection. Player GIF sizes and traffic volume are separate inputs to a hosting estimate.

Animations make no server calls. Room media is dealt once; the floor bell reads on entry and station return, never by polling. Existing slot tapes batch game requests. Real hosting cost still needs traffic, CDN pricing, player-media volume and game endpoint measurements; a local GPU test cannot establish it.

## Rebuild and validation

Build-only compression tool: `../smoke/asset-build/compress-customization.mjs`. Install its pinned dependencies in that folder and pass separate original/output directories. Do not simplify the shipped outputs again. It preserves named interaction/lever nodes; the runtime uses the existing Meshopt decoder.

Regression checks: room/slot Node suites plus `smoke/room-check.mjs`. Validate all three compressed sculptures on both room and active-slot pivots after rebuilding. A 390 x 844 browser check passes, but physical low-end Android/iPhone thermal, memory and touch-navigation testing remains necessary before claiming phone support.
