/* ============================================================================
 * decode.worker.js - gif decode off the main thread.
 *
 * Same approach as js/sissy-fall/gifWorker.js: omggif parses and LZW decodes,
 * one persistent RGBA buffer accumulates the delta frames, disposal 2 rects
 * are cleared, disposal 3 is approximated as keep. Two differences here: we
 * resample onto the 15 fps master clock inside the worker so only the frames
 * the canvas will actually show get rasterised, and every frame comes out
 * already cover fitted to the output size, so the compositor never touches a
 * full size bitmap.
 *
 * Protocol (main -> worker):
 *   { id, buf, w, h, frames, fps }   decode this gif into a `frames` long strip
 *   { cancel: id }                   stop early
 * (worker -> main):
 *   { id, strip:[ImageBitmap], srcFrames, srcDurMs, w0, h0 }   transferred
 *   { id, error }
 *
 * Needs OffscreenCanvas 2D and module workers. The caller feature checks and
 * keeps a main thread path for browsers without them.
 * ==========================================================================*/

import { GifReader } from '../../assets/vendor/omggif/omggif.module.js';
import { resampleIndices, totalDuration } from './clock.js';
import { coverFit } from './layout.js';

const cancelled = new Set();

self.onmessage = (e) => {
  const msg = e.data;
  if (msg && msg.cancel != null) { cancelled.add(msg.cancel); return; }
  decodeGif(msg)
    .catch((err) => { self.postMessage({ id: msg.id, error: String((err && err.message) || err) }); })
    .finally(() => cancelled.delete(msg.id));
};

async function decodeGif({ id, buf, w, h, frames, fps }) {
  const reader = new GifReader(new Uint8Array(buf));
  const w0 = reader.width, h0 = reader.height, count = reader.numFrames();
  if (!w0 || !h0 || !count) throw new Error('That gif has no frames in it');

  const durations = [];
  for (let i = 0; i < count; i++) durations.push(Math.max(20, (reader.frameInfo(i).delay || 10) * 10));
  const srcDurMs = totalDuration(durations);

  // one loop of the source on the master clock, never longer than the canvas
  const stripLen = Math.max(1, Math.min(frames, Math.ceil(srcDurMs / (1000 / fps))));
  const wanted = resampleIndices(durations, stripLen, fps);
  const need = new Set(wanted);

  const rgba = new Uint8ClampedArray(w0 * h0 * 4);
  const idata = new ImageData(rgba, w0, h0);
  const full = new OffscreenCanvas(w0, h0);
  const fctx = full.getContext('2d');
  const out = new OffscreenCanvas(w, h);
  const octx = out.getContext('2d', { alpha: false });
  if (!fctx || !octx) throw new Error('This browser will not give the decoder a canvas');
  octx.imageSmoothingQuality = 'high';
  const fit = coverFit(w0, h0, w, h);

  const bySource = new Map();
  let prevDisposal = 0, prevInfo = null;
  const last = Math.max(...wanted);
  for (let i = 0; i <= last; i++) {
    if (cancelled.has(id)) return;
    if (prevDisposal === 2 && prevInfo) {
      for (let row = 0; row < prevInfo.height; row++) {
        const at = ((prevInfo.y + row) * w0 + prevInfo.x) * 4;
        rgba.fill(0, at, at + prevInfo.width * 4);
      }
    }
    const info = reader.frameInfo(i);
    reader.decodeAndBlitFrameRGBA(i, rgba);
    prevDisposal = info.disposal; prevInfo = info;
    if (!need.has(i)) continue;
    fctx.putImageData(idata, 0, 0);
    octx.fillStyle = '#000';
    octx.fillRect(0, 0, w, h);
    octx.drawImage(full, fit.x, fit.y, fit.w, fit.h);
    bySource.set(i, await createImageBitmap(out));
  }

  const strip = wanted.map((srcIndex) => bySource.get(srcIndex));
  // a repeated source frame shares one bitmap; only transfer each one once
  const transfer = [...new Set(strip)];
  self.postMessage({ id, strip, srcFrames: count, srcDurMs, w0, h0 }, transfer);
}
