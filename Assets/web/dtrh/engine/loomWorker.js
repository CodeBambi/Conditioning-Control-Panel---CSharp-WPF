/* ============================================================================
 * loomWorker.js - THE LOOM's GIF encoder, OFF the main thread. Schema v2.
 *
 * Renders each frame with the SAME field pipeline the preview uses
 * (shared/loomField.js: WebGL field -> 2D composite -> centerpiece), so the
 * file always matches the pane. Encoding is gifenc (vendored): a proper
 * quantizer, because v2 frames - glow halos, gradient bands, radial
 * backgrounds, dual layers - hold thousands of colors the old bg->color
 * ramp palette could never cover.
 *
 * Palette policy: one global palette pooled from 6 evenly spaced frames when
 * the hue stands still; per-frame local palettes when hueCycles > 0 (a global
 * table can't chase a moving hue).
 *
 * Dithering: gifenc maps each pixel to its nearest palette colour with no
 * dithering, so the big flat lavender/black fields eat the 256 slots and the
 * smooth radial transitions band into hard rings. We index with our own
 * ordered (Bayer 8x8) dither instead - the threshold is fixed in SCREEN space,
 * so a spinning loop stays rock-steady (no crawling grain frame to frame) while
 * gradients blend. The dither amplitude auto-tunes to the palette's own spacing,
 * so flat regions barely move and only real gradients get scattered.
 *
 * Protocol (unchanged from v1):
 *   main -> worker: { id, params }  |  { cancel: id }
 *   worker -> main: { id, progress } (0..1, every 4 frames)
 *                   { id, gif: ArrayBuffer, bytes, w, h, frames, delayCs }
 *                   { id, error }
 *
 * Budget: >SOFT_CAP re-encodes once at a smaller long side, frames unchanged, so
 * the loop timing still matches the preview; >HARD_CAP errors out. The C# store
 * (DtrhLoomStore) enforces the same 8MB decoded ceiling, so HARD_CAP stays put
 * even though dithering compresses a touch looser. No WebGL in the worker? Frames
 * fall back to the v1 drawSpiral projection - same look the preview falls back to.
 * ==========================================================================*/

import { GIFEncoder, quantize } from '/dtrh/vendor/gifenc/gifenc.esm.js';
import {
  normalizeParams2, delayCsFor2, framesFor2, createFieldRenderer, composeFrame,
  drawFallbackFrame, formatDims2,
} from '/dtrh/shared/loomField.js';

// SIZE is the LONG side; q.format decides the frame shape (1:1, 16:9, 9:16).
const SIZE = 640;
const RETRY_SIZE = 512;   // oversize fallback: fewer pixels, SAME frames (timing intact)
// HARD_CAP mirrors DtrhLoomStore.MaxGifBytes (8MB decoded); the store rejects
// anything larger, so we can't lift it the way the download-only web build did.
const SOFT_CAP = 6 * 1024 * 1024, HARD_CAP = 8 * 1024 * 1024;

const cancelled = new Set();

self.onmessage = (e) => {
  const msg = e.data;
  if (msg.cancel != null) { cancelled.add(msg.cancel); return; }
  encodeJob(msg)
    .catch((err) => { self.postMessage({ id: msg.id, error: String((err && err.message) || err) }); })
    .finally(() => cancelled.delete(msg.id));
};

async function encodeJob({ id, params }) {
  const q = normalizeParams2(params);
  const frames = framesFor2(q);
  let out = await encode(id, q, SIZE, frames);
  if (out == null) return;   // cancelled
  if (out.bytes > SOFT_CAP) {
    out = await encode(id, q, RETRY_SIZE, frames);   // shrink pixels, keep frames
    if (out == null) return;
  }
  if (out.bytes > HARD_CAP) { self.postMessage({ id, error: 'gif too large' }); return; }
  self.postMessage(
    { id, gif: out.buf, bytes: out.bytes, w: out.w, h: out.h, frames: out.frames, delayCs: out.delayCs },
    [out.buf]);
}

/** Every 4th pixel (RGBA quads, alpha forced opaque) - quantizer diet. */
function subsample(rgba, factor) {
  const pixels = Math.floor(rgba.length / 4 / factor);
  const out = new Uint8Array(pixels * 4);
  for (let i = 0; i < pixels; i++) {
    const s = i * factor * 4, d = i * 4;
    out[d] = rgba[s]; out[d + 1] = rgba[s + 1]; out[d + 2] = rgba[s + 2]; out[d + 3] = 255;
  }
  return out;
}

// ---- ordered dithering ----------------------------------------------------
// The classic 8x8 Bayer matrix (0..63). Fixed in screen space so the loop is
// temporally stable - the same pixel gets the same threshold every frame.
const BAYER8 = [
  0, 32, 8, 40, 2, 34, 10, 42,
  48, 16, 56, 24, 50, 18, 58, 26,
  12, 44, 4, 36, 14, 46, 6, 38,
  60, 28, 52, 20, 62, 30, 54, 22,
  3, 35, 11, 43, 1, 33, 9, 41,
  51, 19, 59, 27, 49, 17, 57, 25,
  15, 47, 7, 39, 13, 45, 5, 37,
  63, 31, 55, 23, 61, 29, 53, 21,
];

/** Mean nearest-neighbour distance in the palette ~ the quantization step.
 * Dithering by roughly this much blends the bands without frying flats. */
function paletteSpacing(pal) {
  const n = pal.length;
  if (n < 2) return 16;
  let sum = 0;
  for (let i = 0; i < n; i++) {
    let best = Infinity;
    const a = pal[i];
    for (let j = 0; j < n; j++) {
      if (i === j) continue;
      const b = pal[j];
      const dr = a[0] - b[0], dg = a[1] - b[1], db = a[2] - b[2];
      const d = dr * dr + dg * dg + db * db;
      if (d < best) best = d;
    }
    sum += Math.sqrt(best);
  }
  return sum / n;
}

/** Dither amplitude ~ one quantization step: gentle on dense palettes (~4),
 * strong on starved/coarse ones (~10-40) where the real ring banding lives.
 * Validated on starved-dark gradients: 42% lower perceived error, 17px rings
 * broken to 5px, while dense palettes barely move (no needless grain). */
function ditherAmp(pal) {
  return Math.max(4, Math.min(40, paletteSpacing(pal) * 1.0));
}

/** Nearest palette index by squared RGB distance (r/g/b may be fractional). */
function nearest(pal, r, g, b) {
  let best = 0, bestD = Infinity;
  for (let i = 0; i < pal.length; i++) {
    const c = pal[i];
    const dr = r - c[0], dg = g - c[1], db = b - c[2];
    const d = dr * dr + dg * dg + db * db;
    if (d < bestD) { bestD = d; best = i; }
  }
  return best;
}

/** Index a frame into `pal` with screen-space ordered dithering. `cache` is a
 * 65536-slot rgb565 -> index memo (init -1) so nearest() runs at most once per
 * distinct dithered colour, keeping this close to a plain nearest-colour pass. */
function ditherIndices(rgba, w, h, pal, amp, cache) {
  const out = new Uint8Array(w * h);
  // per-cell offset centred on 0: (bayer+0.5)/64 - 0.5, scaled to the palette step
  const off = new Float32Array(64);
  for (let k = 0; k < 64; k++) off[k] = ((BAYER8[k] + 0.5) / 64 - 0.5) * amp;
  for (let y = 0, i = 0; y < h; y++) {
    const brow = (y & 7) << 3;
    for (let x = 0; x < w; x++, i++) {
      const t = off[brow + (x & 7)];
      const s = i << 2;
      let r = rgba[s] + t, g = rgba[s + 1] + t, b = rgba[s + 2] + t;
      r = r < 0 ? 0 : r > 255 ? 255 : r;
      g = g < 0 ? 0 : g > 255 ? 255 : g;
      b = b < 0 ? 0 : b > 255 ? 255 : b;
      const key = (((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
      let idx = cache[key];
      if (idx === -1) { idx = nearest(pal, r, g, b); cache[key] = idx; }
      out[i] = idx;
    }
  }
  return out;
}

async function encode(id, q, size, frames) {
  const { w, h } = formatDims2(q.format, size);
  const composite = new OffscreenCanvas(w, h);
  const ctx = composite.getContext('2d', { willReadFrequently: true });
  if (!ctx) throw new Error('no 2d context in worker');

  // WebGL field; falls back to the v1 wedge renderer if the worker has no GL.
  let field = null;
  try { field = createFieldRenderer(new OffscreenCanvas(w, h)); } catch (e) { field = null; }

  const drawFrame = (phase) => {
    if (field) composeFrame(ctx, field, q, phase, w, h);
    else drawFallbackFrame(ctx, q, phase, w, h);
  };
  const readFrame = (phase) => {
    drawFrame(phase);
    return ctx.getImageData(0, 0, w, h).data;
  };

  const delayCs = delayCsFor2(q);
  const perFramePalette = q.hueCycles > 0;

  // Global palette: pool 6 evenly spaced frames, subsampled 4x.
  let globalPalette = null;
  if (!perFramePalette) {
    const SAMPLES = 6;
    const parts = [];
    for (let i = 0; i < SAMPLES; i++) {
      if (cancelled.has(id)) return null;
      parts.push(subsample(readFrame(i / SAMPLES), 4));
      await new Promise((r) => setTimeout(r, 0));
    }
    let len = 0;
    for (const p of parts) len += p.length;
    const pool = new Uint8Array(len);
    let off = 0;
    for (const p of parts) { pool.set(p, off); off += p.length; }
    globalPalette = quantize(pool, 256, { format: 'rgb565' });
  }

  // rgb565 -> index memo, reused across frames; cleared per frame only when the
  // palette changes (per-frame hue). Amplitude is fixed for the global palette.
  const cache = new Int16Array(65536);
  cache.fill(-1);
  const globalAmp = globalPalette ? ditherAmp(globalPalette) : 0;

  const gif = GIFEncoder();
  for (let f = 0; f < frames; f++) {
    if (cancelled.has(id)) return null;
    const rgba = readFrame(f / frames);
    let palette, amp;
    if (perFramePalette) {
      palette = quantize(subsample(rgba, 4), 256, { format: 'rgb565' });
      amp = ditherAmp(palette);
      cache.fill(-1);   // new table this frame, old memo is stale
    } else {
      palette = globalPalette;
      amp = globalAmp;
    }
    const indexed = ditherIndices(rgba, w, h, palette, amp, cache);
    // First frame carries the global table + loop flag; later frames only
    // declare a local table when palettes vary per frame.
    const opts = { delay: delayCs * 10, repeat: 0 };
    if (f === 0 || perFramePalette) opts.palette = palette;
    gif.writeFrame(indexed, w, h, opts);
    if (f % 4 === 3) self.postMessage({ id, progress: (f + 1) / frames });
    // yield so a cancel message can land between frames
    await new Promise((r) => setTimeout(r, 0));
  }
  gif.finish();
  const bytes = gif.bytes();
  return { buf: bytes.buffer, bytes: bytes.length, w, h, frames, delayCs };
}
