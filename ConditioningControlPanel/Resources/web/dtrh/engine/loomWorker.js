/* ============================================================================
 * loomWorker.js - THE LOOM's GIF encoder, OFF the main thread (Part 2).
 *
 * Renders the spiral frames with the SAME drawSpiral the preview uses
 * (shared/loomSpiral.js), quantizes each frame against a small global palette
 * built from the params' own colors (bg -> color ramps, so banding is
 * invisible on this kind of art), and streams them into omggif's GifWriter.
 *
 * Protocol (main -> worker):
 *   { id, params }        encode this spiral
 *   { cancel: id }        pane closed / re-tweaked mid-encode: stop early
 * (worker -> main), per job:
 *   { id, progress }                                    0..1, every 4 frames
 *   { id, gif: ArrayBuffer, bytes, w, h, frames, delayCs }  done (buffer transferred)
 *   { id, error }
 *
 * Budget: 640px / 36 frames lands ~1.5-3.5MB for typical params. A pathological
 * palette that still overshoots 6MB re-encodes once at 512/30; >8MB errors out
 * (the C# store enforces the same ceiling).
 * ==========================================================================*/

import { GifWriter } from '/dtrh/vendor/omggif/omggif.module.js';
import { drawSpiral, normalizeParams, delayCsFor } from '/dtrh/shared/loomSpiral.js';

const SIZE = 640, FRAMES = 36;
const RETRY_SIZE = 512, RETRY_FRAMES = 30;
const SOFT_CAP = 6 * 1024 * 1024, HARD_CAP = 8 * 1024 * 1024;

const cancelled = new Set();

self.onmessage = (e) => {
  const msg = e.data;
  if (msg.cancel != null) { cancelled.add(msg.cancel); return; }
  encodeJob(msg)
    .catch((err) => { self.postMessage({ id: msg.id, error: String((err && err.message) || err) }); })
    .finally(() => cancelled.delete(msg.id));
};

const hexToRgbInt = (h) => parseInt(h.slice(1), 16) & 0xffffff;

/** Global palette: for each params color, a 24-step bg->color ramp + the pure
 *  color; plus bg itself. Deduped, padded to the next power of two (omggif
 *  requires 2..256 pow2). Small on purpose - this art IS flat ramps. */
function buildPalette(q) {
  const bg = hexToRgbInt(q.bg);
  const set = new Set([bg]);
  for (const c of q.colors) {
    const rgb = hexToRgbInt(c);
    const r0 = (bg >> 16) & 255, g0 = (bg >> 8) & 255, b0 = bg & 255;
    const r1 = (rgb >> 16) & 255, g1 = (rgb >> 8) & 255, b1 = rgb & 255;
    for (let i = 1; i <= 24; i++) {
      const t = i / 24;
      set.add(((Math.round(r0 + (r1 - r0) * t) << 16)
        | (Math.round(g0 + (g1 - g0) * t) << 8)
        | Math.round(b0 + (b1 - b0) * t)) & 0xffffff);
    }
  }
  let palette = [...set];
  if (palette.length > 256) palette = palette.slice(0, 256);
  let pow = 2;
  while (pow < palette.length) pow <<= 1;
  while (palette.length < pow) palette.push(palette[0]);
  return palette;
}

/** 15-bit RGB -> palette index LUT: one Uint8Array(32768) filled by nearest
 *  match, then O(1) per pixel. Plenty for ramp art (5 bits/channel). */
function buildLut(palette) {
  const lut = new Uint8Array(32768);
  const pr = new Uint8Array(palette.length), pg = new Uint8Array(palette.length), pb = new Uint8Array(palette.length);
  for (let i = 0; i < palette.length; i++) {
    pr[i] = (palette[i] >> 16) & 255; pg[i] = (palette[i] >> 8) & 255; pb[i] = palette[i] & 255;
  }
  for (let key = 0; key < 32768; key++) {
    const r = ((key >> 10) & 31) << 3, g = ((key >> 5) & 31) << 3, b = (key & 31) << 3;
    let best = 0, bestD = Infinity;
    for (let i = 0; i < palette.length; i++) {
      const dr = r - pr[i], dg = g - pg[i], db = b - pb[i];
      const d = dr * dr + dg * dg + db * db;
      if (d < bestD) { bestD = d; best = i; }
    }
    lut[key] = best;
  }
  return lut;
}

async function encodeJob({ id, params }) {
  const q = normalizeParams(params);
  let out = await encode(id, q, SIZE, FRAMES);
  if (out == null) return;   // cancelled
  if (out.bytes > SOFT_CAP) {
    out = await encode(id, q, RETRY_SIZE, RETRY_FRAMES);   // defensive re-encode
    if (out == null) return;
  }
  if (out.bytes > HARD_CAP) { self.postMessage({ id, error: 'gif too large' }); return; }
  self.postMessage(
    { id, gif: out.buf, bytes: out.bytes, w: out.size, h: out.size, frames: out.frames, delayCs: out.delayCs },
    [out.buf]);
}

async function encode(id, q, size, frames) {
  const canvas = new OffscreenCanvas(size, size);
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  if (!ctx) throw new Error('no 2d context in worker');

  const palette = buildPalette(q);
  const lut = buildLut(palette);
  const delayCs = delayCsFor(q);
  const indexed = new Uint8Array(size * size);
  // worst case ~= raw indexed data + headers; LZW only shrinks it
  const buf = new Uint8Array(size * size * frames + 4096);
  const writer = new GifWriter(buf, size, size, { loop: 0, palette });

  for (let f = 0; f < frames; f++) {
    if (cancelled.has(id)) return null;
    drawSpiral(ctx, size, q, f / frames);
    const px = ctx.getImageData(0, 0, size, size).data;
    for (let i = 0, j = 0; j < indexed.length; i += 4, j++) {
      indexed[j] = lut[((px[i] & 0xf8) << 7) | ((px[i + 1] & 0xf8) << 2) | (px[i + 2] >> 3)];
    }
    writer.addFrame(0, 0, size, size, indexed, { delay: delayCs });
    if (f % 4 === 3) self.postMessage({ id, progress: (f + 1) / frames });
    // yield so a cancel message can land between frames
    await new Promise((r) => setTimeout(r, 0));
  }
  const bytes = writer.end();
  return { buf: buf.buffer.slice(0, bytes), bytes, size, frames, delayCs };
}
