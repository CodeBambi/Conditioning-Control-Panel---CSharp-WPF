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
 * Protocol (unchanged from v1):
 *   main -> worker: { id, params }  |  { cancel: id }
 *   worker -> main: { id, progress } (0..1, every 4 frames)
 *                   { id, gif: ArrayBuffer, bytes, w, h, frames, delayCs }
 *                   { id, error }
 *
 * Budget (unchanged): >6MB re-encodes once at 512/30; >8MB errors out (the
 * C# store enforces the same ceiling). No WebGL in the worker? Frames fall
 * back to the v1 drawSpiral projection - same look the preview falls back to.
 * ==========================================================================*/

import { GIFEncoder, quantize, applyPalette } from '/dtrh/vendor/gifenc/gifenc.esm.js';
import {
  normalizeParams2, delayCsFor2, createFieldRenderer, composeFrame,
  drawFallbackFrame, formatDims2,
} from '/dtrh/shared/loomField.js';

// SIZE is the LONG side; q.format decides the frame shape (1:1, 16:9, 9:16).
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

async function encodeJob({ id, params }) {
  const q = normalizeParams2(params);
  let out = await encode(id, q, SIZE, FRAMES);
  if (out == null) return;   // cancelled
  if (out.bytes > SOFT_CAP) {
    out = await encode(id, q, RETRY_SIZE, RETRY_FRAMES);   // defensive re-encode
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

  const gif = GIFEncoder();
  for (let f = 0; f < frames; f++) {
    if (cancelled.has(id)) return null;
    const rgba = readFrame(f / frames);
    const palette = perFramePalette ? quantize(subsample(rgba, 4), 256, { format: 'rgb565' }) : globalPalette;
    const indexed = applyPalette(rgba, palette, 'rgb565');
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
