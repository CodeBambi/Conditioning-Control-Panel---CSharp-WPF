/* ============================================================================
 * gifWorker.js (Sissy Fall) - gif decoding OFF the main thread.
 *
 * The spawner used to run omggif's LZW decode + frame compositing on the main
 * thread (with setTimeout yields between frames). Each frame is still a few ms
 * of blocking work, and several gifs prefetching at once - a pool refill at
 * high speed - stacked those bursts into visible frame skips, worst on iPhone
 * where the JS decoder is the only path. Here the whole job (parse, composite,
 * downscale, ImageBitmap creation) runs in a module worker; the main thread
 * only receives ready-to-draw bitmaps via zero-copy transfer.
 *
 * Protocol (main -> worker):
 *   { id, buf, maxDim, maxFrames }  decode this gif
 *   { cancel: id }                  card despawned mid-decode: stop early
 * (worker -> main), per job:
 *   { id, frame: {bitmap, durMs}, w, h, aspect }  one frame, index order
 *   { id, done: true }                            all frames sent
 *   { id, error }                                 parse/context failure
 *
 * Same compositor rules as spawner.prefetchGifJS: one persistent RGBA buffer
 * accumulates patch frames, disposal 2 rects are cleared, disposal 3 (rare)
 * approximated as keep. Requires OffscreenCanvas 2D - the spawner feature-
 * checks before spawning the worker and keeps its main-thread fallbacks.
 * ==========================================================================*/

import { GifReader } from '/dtrh/vendor/omggif/omggif.module.js';

const cancelled = new Set();

self.onmessage = (e) => {
  const msg = e.data;
  if (msg.cancel != null) { cancelled.add(msg.cancel); return; }
  decode(msg)
    .catch((err) => { self.postMessage({ id: msg.id, error: String((err && err.message) || err) }); })
    .finally(() => cancelled.delete(msg.id));
};

async function decode({ id, buf, maxDim, maxFrames }) {
  const reader = new GifReader(new Uint8Array(buf));
  const w0 = reader.width, h0 = reader.height;
  const total = reader.numFrames();
  if (!w0 || !h0 || !total) { self.postMessage({ id, error: 'empty gif' }); return; }
  const count = Math.min(total, maxFrames);
  const shrink = Math.min(1, maxDim / Math.max(w0, h0));
  const sw = Math.max(2, Math.round(w0 * shrink));
  const sh = Math.max(2, Math.round(h0 * shrink));
  const aspect = w0 / h0;

  const rgba = new Uint8ClampedArray(w0 * h0 * 4);
  const idata = new ImageData(rgba, w0, h0);
  const work = new OffscreenCanvas(w0, h0);
  const wctx = work.getContext('2d');
  const scratch = new OffscreenCanvas(sw, sh);
  const sctx = scratch.getContext('2d');
  if (!wctx || !sctx) { self.postMessage({ id, error: 'no 2d context in worker' }); return; }

  let prevDisposal = 0, prevInfo = null;
  for (let idx = 0; idx < count; idx++) {
    if (cancelled.has(id)) return;
    if (prevDisposal === 2 && prevInfo) {
      for (let row = 0; row < prevInfo.height; row++) {
        const at = ((prevInfo.y + row) * w0 + prevInfo.x) * 4;
        rgba.fill(0, at, at + prevInfo.width * 4);
      }
    }
    const info = reader.frameInfo(idx);
    reader.decodeAndBlitFrameRGBA(idx, rgba);
    prevDisposal = info.disposal; prevInfo = info;
    wctx.putImageData(idata, 0, 0);
    sctx.clearRect(0, 0, sw, sh);
    sctx.drawImage(work, 0, 0, sw, sh);
    const bitmap = await createImageBitmap(scratch);
    const durMs = Math.max(20, (info.delay || 10) * 10);
    self.postMessage({ id, frame: { bitmap, durMs }, w: sw, h: sh, aspect }, [bitmap]);
  }
  self.postMessage({ id, done: true });
}
