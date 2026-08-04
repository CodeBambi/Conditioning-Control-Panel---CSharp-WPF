# THE LOOM — Feature Primer (for maintenance + the mobile port)

> **Purpose.** One-load orientation for the whole Loom (the spiral-GIF creator). Written to be
> read standalone — @-mention it. §1–2 = what it is + where the code lives. §3–6 = the **portable
> core** (schema, render math, GIF encode) you re-implement 1:1 on any platform. §7 = the **host
> contract** (bridge + file store) — the only part that is platform-specific and must be rebuilt
> for mobile. §8 = a concrete mobile-port plan + gotchas.
>
> **Freshness.** Tracks the code as of 2026-07-23. The Loom is schema-**v2**. Confirm file:line
> with a quick read before quoting — but the *algorithms* here are load-bearing and stable.

---

## 0. What the Loom is, in one paragraph

The Loom is a **seamless-loop spiral-GIF creator**. The user designs a hypnotic spiral with live
dials (arms, turns, colors, glow, pulse, wobble, hue drift, a second counter-rotating layer, a
centerpiece), watches it spin in a live preview at exactly the speed it will play, and hits **SAVE**
to bake it into a **perfectly-looping GIF** that lands in the app's Spiral library. Saved spirals
then feed the spiral overlays elsewhere in the app. The whole visual is computed **per-pixel in one
GLSL fragment shader**; the GIF is encoded **100% client-side** in a Web Worker. **No server, no
upload, no network** — producing/saving a spiral is pure local compute.

The single most important invariant: **preview == file**. The live pane and the GIF encoder push
frames through the *same* pipeline (`composeFrame`), so what you see spinning is exactly what lands
on disk, down to the dither.

---

## 1. Where it lives — file map

Everything is under `Resources/web/dtrh/` (it was born inside the DTRH game; see that folder's
`CLAUDE.md` §for the game). The Loom is a **self-contained sub-feature** — it needs none of the
three.js game engine. Two layers:

### The portable core (JS — ports as-is to any web/RN-WebView target)
| File | Role |
|---|---|
| `shared/loomField.js` | **The heart.** Schema-v2 params + normalizer, the WebGL field shader (`createFieldRenderer`), the 2D centerpiece composite, `composeFrame`, seamless-loop timing math, the no-WebGL fallback, and `randomParams2` (🎲). |
| `shared/loomSpiral.js` | Schema-**v1** 2D-canvas wedge renderer (`drawSpiral`). The no-WebGL fallback path and the legacy schema. v2 is a strict superset. |
| `game/loomStudio.js` | **The studio UI** (`createLoomStudio`) — the whole editor DOM: preview `<canvas>`, dials, presets, 🎲, undo, fullscreen, the save flow, the saved-spiral "rack". Mounted in two homes (see §2). |
| `engine/loomWorker.js` | **The GIF encoder**, off the main thread. Same field pipeline per frame → Bayer-dithered palette indexing → `gifenc`. |
| `vendor/gifenc/gifenc.esm.js` | Vendored GIF encoder + quantizer (`GIFEncoder`, `quantize`). |
| `engine/loomSpirals.js` | The picker every "spiral" overlay effect shares — mixes saved spirals ~50/50 with a bundled pool. (Consumer side, not the editor.) |

### The host coupling (this is what you REPLACE on mobile — §7)
| File | Role |
|---|---|
| `bridge.js` | The `postMessage` contract with the WPF host (WebView2). Protocol v1. |
| `loomBoot.js` | Boot for the **standalone window**: mounts `createLoomStudio` + wires the bridge. |
| `loom.html` | The standalone window's HTML shell + the two-pane "studio-split" grid CSS. |
| `Services/Chaos/LoomHostService.cs` | C# window host: one WebView2 pointed at `loom.html`, speaks the loom bridge subset. |
| `Services/Chaos/DtrhLoomStore.cs` | **File authority** — validates + writes `loom_<slug>.gif` (+ `.json` sidecar), slug whitelist, 12-file cap, size ceilings, GIF magic check. |
| `Features/SpiralFeatureControl.xaml(.cs)` | The **Spiral Overlay option card** in the app — its "open the Loom" button calls `LoomHostService.Launch()`, and its library grid enumerates the same Spirals folder. |

---

## 2. The two homes + the option-card entry

`createLoomStudio({ bridge, sfx, previewSize, hotkeys })` emits the **same** studio DOM in both homes;
only layout + a couple of options differ:

1. **The standalone Loom window** (`loom.html` → `loomBoot.js`) — the main-app path, **open to
   everyone**. Launched from the **Spiral Overlay feature card** (`SpiralFeatureControl.xaml.cs
   BtnOpenLoom_Click` → `LoomHostService.Launch()`). Mounted with `previewSize: 768` (crisp backing)
   and `hotkeys: true` (F / R / ctrl+Z / ctrl+S / 1–6). `loom.html` lays the studio out as a
   two-pane grid (stage | dials, rack across the bottom).

2. **The Warren's Boudoir pane** (`game/warren.js:~1453` → `createLoomStudio({ bridge, sfx })`) —
   the in-DTRH way in (a crafted unlock). Lean defaults: `previewSize: 288`, no hotkeys (the game
   owns its own keys and must not be shadowed). The pane just stacks the same cards vertically.

Both write to the **same** C# store, so a spiral woven in either home appears in the app's Spiral
library and in the other home's rack identically.

**The option card ↔ library loop.** `SpiralFeatureControl` is the settings card that (a) launches the
Loom and (b) shows a live library grid of saved spirals for picking one as the active overlay. The
store raises `DtrhLoomStore.Changed` after every save/delete, so the card refreshes live without
polling. Deleting the spiral that's currently selected as the overlay resets `Settings.SpiralPath` to
"" (falls back to the built-in) — done in `DtrhLoomStore.Delete`.

---

## 3. The data model — schema v2 (THE portable contract)

One plain-JSON object fully describes a spiral. It is persisted verbatim in the `.json` sidecar so a
saved spiral can be re-edited. `normalizeParams2(p)` coerces **anything** (a v2 object, a v1 sidecar,
or junk) into a valid v2 object with every field clamped — call it after every mutation and on every
load. `defaultParams2()` is the neutral starting point.

```
{ schema: 2,
  format: 'square'|'wide'|'tall',        // 1:1 · 16:9 · 9:16 (tiktok)
  speed: 1..5,                           // → frame delay + frames-per-loop (§4 tables)
  bg: { kind:'solid'|'radial', color:'#rrggbb', outer:'#rrggbb' },
  layer:  {                              // the primary weave
    arms: 1..12, turns: 0.5..6, duty: 0.2..0.8,   // duty = lit fraction of each arm band
    style: 'log'|'arch'|'ribbon'|'golden'|'tunnel'|'petal',
    direction: 1|-1,                     // inward / outward spin
    colors: ['#rrggbb' × 1..6],          // "threads"
    bandMode: 'hard'|'gradient',         // candy-stripe vs blended
    speedMul: 1..3 },
  layer2: layer + { enabled: bool },     // a counter-rotating second weave
  glow: 0..1,
  pulse:  { amp: 0..0.25, cycles: 1..4 },        // radial "breathing" zoom
  wobble: { amp: 0..0.35, freq: 1..6, cycles: 1..3 },  // liquid distortion
  hueCycles: 0..2,                       // whole-loop hue rotations (0 = still)
  centerpiece: { kind:'none'|'dot'|'star'|'cross'|'x'|'mantra',
                 color:'#rrggbb', sizeFrac:0.08..0.4,
                 text:<=12ch, flashCycles:0..4 } }
```

**v1 compatibility.** Schema v1 (`loomSpiral.js`) is the old flat shape: `{schema:1, arms:2-8,
turns, style:'log'|'arch'|'ribbon', duty, speed, direction, colors×1-2, bg:'#rrggbb'}`. `normalizeParams2`
detects a v1 sidecar and lifts it into v2 with every v2-only feature at its **neutral default**, so
old spirals re-render byte-identically. Don't drop v1 support on mobile if any user has old sidecars.

---

## 4. The render pipeline — `composeFrame` (preview == file)

One function renders one frame at phase `∈ [0,1)`, and **every** surface calls it (live preview,
fullscreen mirror, worker encoder, preset thumbnails):

```
composeFrame(ctx2d, field, q, phase, w, h):
  field.render(q, phase)                 // 1) WebGL field → the field's own canvas
  ctx2d.drawImage(field.canvas, …)       // 2) blit onto the visible/target 2D canvas
  drawCenterpiece(ctx2d, q, phase, w, h) // 3) 2D-canvas centerpiece on top
```

### 4a. The field shader (`createFieldRenderer` in `loomField.js`)
A single fullscreen-triangle WebGL1 program. The fragment shader (`FRAG_SRC`) computes the **entire**
field per pixel: background (solid/radial), up to two layers, each layer's six styles, duty bands,
twist, 1–6-thread hard/gradient blending, glow halo, pulse zoom, and liquid wobble. `layerGlsl(S)`
is stamped out twice (`'1'` and `'2'`) because GLSL can't take array-of-struct params.

Key coordinate setup (matches the old 2D `drawSpiral` exactly for squares):
- Origin top-left (`xy = (fragX, res.y - fragY)`), centered, scaled by the **half-diagonal**
  (`length(c) * 1.0253048`) so any aspect keeps its corners inside `r ≤ ~0.975` — the field is only
  defined to `r=1`, and the corners overdraw. For a square this is exactly the old `min(c)*1.45`.
- `zoom = 1 + pulseAmp*sin(2π·phase·pulseCycles)`, `r = length(p)/zoom`, `th = atan(p.y,p.x)`.
- Styles: `log` (`t=r^1.75`), `arch` (`t=r`), `ribbon` (inverse-smoothstep), `golden` (log growth),
  `tunnel` (bands ride the radius, `turns` shears them into a helix — the atan branch-cut must snap
  to a whole number of colour cycles or a seam shows), `petal` (rose-curve mask).
- Antialiasing is analytic (`u_px`-scaled smoothstep on the band edges), so it's crisp at any size.

WebGL context flags that matter: `preserveDrawingBuffer:true` (the 2D composite `drawImage`s it right
after render), `alpha:false`, `antialias:false`. Works on `HTMLCanvasElement` **and**
`OffscreenCanvas` — that's how the worker reuses it.

### 4b. The centerpiece (`drawCenterpiece`) — plain 2D canvas
dot / star / cross / x are filled paths; `mantra` draws up-to-12-char text (font `600 <px> "Segoe UI"`
— **swap this for a bundled font on mobile**, don't rely on a system face). `mantra.flashCycles`
pulses opacity with `cos(2π·phase·flashCycles)`. Hue drift also rotates the centerpiece color.

### 4c. Seamless looping (the whole reason this feels right)
**Every animated quantity completes an integer number of cycles over the master phase `u∈[0,1)`**, so
frame 0 == frame N by construction — the editor **cannot** produce a non-looping GIF:
- Rotation moves an exact **symmetry span**: `symmetrySpanRad(layer) = (2π/arms) · (arms%n==0 ? n :
  arms)` where `n = colors.length` (clamped 1–6). One arm-span rotation just swaps colours; the true
  period is `n` arm-spans when the colour cycle divides the arm count, else a full turn.
- `layerRotationRad = direction · phase · symmetrySpan · speedMul`.
- hue / pulse / wobble / mantra-flash all use `sin/cos(2π·phase·<wholeCycles>)`.

### 4d. Timing tables (preview speed MUST equal GIF speed)
Both the preview clock and the encoder read the **same** tables in `loomField.js`:
```
DELAY_CS   = {1:5, 2:4, 3:3, 4:2, 5:2}   // GIF frame delay, centiseconds
FRAME_TABLE= {1:72,2:63,3:60,4:54,5:36}  // frames per loop
loopMs2(p) = frames · delayCs · 10       // one loop in ms
```
Preview phase = `((now - t0) % loopMs2) / loopMs2`. The tables are tuned so `frames·delay` is constant
per perceived spin speed (≈2× the frames of the old desktop table for smoother motion, same duration).

### 4e. No-WebGL fallback
`drawFallbackFrame` projects v2 → v1 (`projectToV1`: layer 1 only, first two threads, nearest v1
style) and renders with the 2D wedge `drawSpiral`, then the same centerpiece composite. Non-square
frames render a `max(w,h)` square offscreen and crop the center band. Preview **and** worker both use
this, so even the fallback stays preview == file.

---

## 5. The GIF encode pipeline (`engine/loomWorker.js`)

Off the main thread. Protocol: main → `{id, params}` or `{cancel:id}`; worker → `{id, progress}`
(0..1 every 4 frames), `{id, gif:ArrayBuffer, bytes, w, h, frames, delayCs}`, or `{id, error}`.

Per job (`encode`):
1. `SIZE = 640` **long side**; `formatDims2` gives w×h from `q.format`. Render each frame with the
   same `composeFrame` (WebGL field on an `OffscreenCanvas`, or the v1 fallback), read RGBA via
   `getImageData`.
2. **Palette policy.** Hue still (`hueCycles==0`) → one **global** 256-color palette pooled from 6
   evenly-spaced frames (subsampled 4×) via `gifenc.quantize(..., {format:'rgb565'})`. Hue moving →
   a **per-frame** local palette (a global table can't chase a moving hue).
3. **Dithering.** `gifenc` does nearest-color with no dither, so flat lavender/black fields eat the
   256 slots and radials band into rings. Instead we index with our own **Bayer 8×8 ordered dither**,
   fixed in **screen space** (same pixel → same threshold every frame → no crawling grain across a
   spin). Amplitude auto-tunes to the palette's own spacing (`ditherAmp` ≈ one quantization step:
   gentle ~4 on dense palettes, up to 40 on starved ones). A 65536-slot rgb565→index memo keeps it
   near a plain nearest-color pass.
4. Write frames with `gifenc.GIFEncoder` (`repeat:0` = infinite loop; frame 0 carries the global
   table, per-frame mode declares a local table each frame).
5. **Size budget.** `> SOFT_CAP (6MB)` → re-encode once at `RETRY_SIZE=512`, **frames unchanged** so
   loop timing still matches the preview. `> HARD_CAP (8MB)` → error. `HARD_CAP` mirrors the C# store's
   `MaxGifBytes` — keep them in lockstep.
6. Yields (`setTimeout(0)`) between frames so a `{cancel}` lands mid-encode.

`loomStudio` drives it: `startSave` posts `{id, params}`; on `{gif}` it base64s the buffer and sends
`loom-save` to the host; `{progress}` updates the button; the pane stays live during encode, so it
snapshots the params/name/overwrite **at click time** (`pendingJob`) and sends *those*, not whatever
drifted since.

---

## 6. The studio UI (`createLoomStudio`) — behaviors worth preserving

- **One write path:** `patch(fn)` = pushHistory → mutate `params` → `normalizeParams2` → `redraw`.
  Sliders snapshot the pre-drag value once for undo; color inputs stream live on `input` (no redraw —
  rebuilding DOM mid-drag would tear down the OS color dialog) and commit with a redraw on `change`.
- **Full-rebuild render.** `render(body)` wipes `body.innerHTML` and rebuilds every card on every
  state change; module state (`params`, `name`, armed flags, history) lives in the closure and
  survives. It carefully **saves + restores every scroll position** (own scrollers + the ancestor +
  document) across the wipe, or releasing a slider yanks the dials to the top.
- **Presets** (`PRESETS[]`, 8 CCP-themed) + **🎲** (`randomParams2`) restyle the weave but **keep the
  current `format`**. Preset chips wear tiny thumbnails rendered once through the real field pipeline.
- **Undo** stack (40 deep, ctrl+Z / ↩). **Fullscreen** preview (⛶ / F): native Fullscreen API with a
  fixed-overlay fallback, its own hi-res GL context freed on exit.
- **The rack** = saved spirals: thumb, ✎ re-edit (loads sidecar params, arms overwrite), 📂 reveal in
  folder, 🗑 two-click forget. **12-spiral cap** (`MAX_SPIRALS`, mirrors C#).
- **Overwrite handshake:** saving onto an existing slug returns `error:'exists'`; the button becomes
  "overwrite?" and a second SAVE passes `overwrite:true`.

---

## 7. The host contract — THE PART YOU REPLACE ON MOBILE

This is the ONLY platform-specific coupling. On desktop it's WebView2 `postMessage` ↔ C#. The JS core
knows nothing about C# — it only knows the `bridge` object's shape and the message types.

### 7a. The bridge object (`bridge.js`, Protocol v1)
`createLoomStudio` needs a `bridge` with exactly: `bridge.send(msg)`, `bridge.on(type, fn)`,
`bridge.log(msg)`, `bridge.announceReady()`. Handlers are a `Map` (one per type) with in-order
pre-buffer replay, so neither side races the other's boot.

### 7b. Messages (the whole loom subset)
**Page → Host:**
| type | payload | meaning |
|---|---|---|
| `ready` | `{protocol}` | boot done; host flushes queued `loom-list` |
| `loom-save` | `{name, overwrite, params, gifBase64}` | persist a woven GIF |
| `loom-delete` | `{slug}` | forget one |
| `loom-reveal` | `{slug}` | show the file in a folder (desktop-only nicety) |
| `sfx` | `{name, scale}` | play a UI sound (optional) |
| `log` | `{msg}` | diagnostics |

**Host → Page:**
| type | payload | meaning |
|---|---|---|
| `loom-list` | `{spirals:[{slug, url, params}]}` | the saved library (on ready + after each save/delete) |
| `loom-result` | `{op:'save'|'delete', ok, slug, error}` | outcome of the last write |

`error` codes the page renders as friendly copy: `bad-name`, `exists`, `cap-reached`, `too-big`,
`bad-gif`, `io-failed`.

### 7c. The file store (`DtrhLoomStore.cs`) — authority the page trusts
- Files: `%APPDATA%/ConditioningControlPanel/Spirals/loom_<slug>.gif` + `loom_<slug>.json` sidecar.
- **Slug whitelist** `^[a-z0-9_-]{1,24}$` (traversal impossible), **12-file cap**, `MaxBase64Chars`
  8M, `MaxGifBytes` 8MB, **GIF magic + `0x3B` trailer** validation. Validation is integrity, not
  anti-cheat.
- `url` handed back is `https://ccp.spirals/loom_<slug>.gif` (a WebView2 virtual-host mapping →
  the Spirals folder). Raises `Changed` after save/delete so the option card + library refresh live.

---

## 8. Porting to mobile — the plan

The visual + encode core is **directly portable**; only §7 changes. Recommended split:

**Keep verbatim (the algorithm is the product):**
- The schema + `normalizeParams2` / `defaultParams2` / `randomParams2` (§3).
- The seamless-loop math + timing tables (§4c/§4d) — non-negotiable for clean loops.
- The GLSL field shader source + `composeFrame` logic (§4a/§4b). The math is API-agnostic.
- The palette + Bayer-dither + size-budget logic (§5).

**Replace / re-target per platform:**
1. **Rendering surface.** In a React-Native **WebView**, the entire JS core runs unchanged — cheapest
   path, and you keep preview == file for free. For **native** RN you'd port the shader to
   `expo-gl`/`react-native-webgl` (WebGL1-compatible) and the 2D centerpiece + composite to Skia
   (`@shopify/react-native-skia`) or a GL text pass. Prefer the WebView unless perf demands native.
2. **The Worker.** RN has no `Worker`/`OffscreenCanvas`. In a WebView, encode on the page (it already
   works); consider chunking to keep the UI thread breathing. Native RN: run `gifenc` on the JS thread
   with yields, or a native GIF encoder — the frame RGBA source is the same.
3. **The bridge.** Swap `bridge.js` for a shim exactly like the web-Loom shim
   (`cclabs-site/loom` did this): on `loom-save`, write the GIF to app storage (or share-sheet /
   camera-roll) instead of C#, then emit a synthetic `loom-result{ok:true, slug}` so the UI unlocks,
   and re-emit `loom-list`. `loom-reveal`/`sfx`/`log` become no-ops or platform equivalents.
4. **The store.** Re-implement `DtrhLoomStore`'s **validation contract** (slug whitelist, 12-cap, size
   ceilings, GIF magic) in whatever owns files on mobile — the page *depends* on those error codes.
5. **Fonts.** Bundle the mantra font; don't assume "Segoe UI".
6. **Layout.** `loom.html`'s two-pane grid is desktop-shaped. Mobile wants the **stacked** layout
   (the studio already emits stack-friendly DOM; `loom.html`'s `@media (max-width:980px)` block is a
   ready reference) — likely 9:16 (`tall`) as the default `format`.

**Precedent to copy:** `C:\Projects\cclabs-site\loom\` already ported this exact core to a
**download-only web page** with a bridge shim (no server, no C#) — see the `web-loom-public-page`
memory. That is the closest existing template for the mobile shim; the only real deltas are storage
target and the encode thread.

### Gotchas (carry these over)
- **preview == file is sacred.** If the mobile preview and encoder ever diverge (different size, font,
  dither, timing table), users see one thing and get another. Route both through one `composeFrame`.
- **Size caps are paired.** Worker `HARD_CAP` == store `MaxGifBytes` (8MB). ~2× frame-doubling can push
  a busy hue-cycling spiral past the store's base64 gate even after the 6MB retry (the web/app split
  differs here — the download-only web build used looser 12/22MB caps). Pick your ceiling and pair both
  ends.
- **A JS module that throws at import = silent infinite loader** (a stray backtick in a template
  literal did it once). Diagnose in a real browser/WebView console, not `node --check`.
- **Web-only JS changes need a rebuild** to copy into `bin/` before they run in the desktop app (the
  source tree and the `bin/.../Resources/web` copy are separate).
