/* ============================================================================
 * encode/gifDecode.js — an animated GIF / animated WebP, taken apart into a
 * FILMSTRIP of ready-to-encode ImageBitmaps.
 *
 * This is the front half of lane B. The host has no decoder for animated GIF or
 * animated WebP (SkiaSharp gives us the first frame and nothing else, which is
 * exactly why the C# side dispatches these to the page at all), so the page owns
 * the decode; `encodeWorker.js` owns everything downstream of it.
 *
 * WHY ImageDecoder AND NOT <img>: drawImage() of a GIF paints the container's
 * "default image" — frame 0, forever. The DtRH spawner learned that the hard way
 * (engine/spawner.js prefetchGif) and the recipe here is the same one, minus
 * three.js: open an ImageDecoder over the whole buffer, walk frameCount, and
 * downscale each frame through createImageBitmap's resize options with an
 * OffscreenCanvas fallback for runtimes that ship ImageDecoder but refuse them.
 *
 * TWO TIMING FACTS THAT ARE NOT NEGOTIABLE:
 *   1. WebCodecs reports VideoFrame.duration in MICROSECONDS. Treating it as ms
 *      makes every artifact 1000x too long, and the muxer will happily write it.
 *   2. A GIF frame delay of 0 (and, by ~25 years of browser convention, anything
 *      under 20 ms) means "as fast as you like", which every renderer since
 *      Netscape has interpreted as 100 ms. A filmstrip that believes the 0 makes
 *      a 400-frame GIF into a zero-length mp4.
 *
 * NODE-IMPORT-SAFE: not one browser global is touched at module scope. The whole
 * file is pure helpers plus one async function, and the selftest imports it under
 * node to prove exactly that (an import throw inside WebView2 is a silent
 * infinite loader — there are no devtools).
 * ==========================================================================*/

/** Absolute frame ceiling: 30 s at 30 fps. Past it we subsample with a stride. */
export const MAX_FRAMES = 900;

/** What a 0/absent/absurd frame delay actually means, in microseconds. */
export const FALLBACK_FRAME_US = 100_000;

/** Below this a delay is the "0 means 100 ms" convention, not a real 200 fps. */
export const MIN_FRAME_US = 20_000;

/** Nothing sane runs a single frame longer than this; a corrupt delay is capped. */
export const MAX_FRAME_US = 10_000_000;

/* ------------------------------------------------------------ pure helpers */

/** The ImageDecoder `type` for a host `kind`. Unknown kinds guess GIF. */
export function mimeForKind(kind) {
  const k = String(kind || '').toLowerCase();
  if (k === 'awebp' || k === 'webp' || k === 'image/webp') return 'image/webp';
  return 'image/gif';
}

/**
 * One frame's honest duration, in microseconds.
 *
 * `raw` is whatever `VideoFrame.duration` said: microseconds, or null, or 0.
 * See fact 2 in the header — this is where the 100 ms convention lives, and it
 * is a pure function precisely so the selftest can pin it.
 */
export function normalizeDurUs(raw) {
  const n = Number(raw);
  if (!isFinite(n) || n <= 0) return FALLBACK_FRAME_US;
  if (n < MIN_FRAME_US) return FALLBACK_FRAME_US;
  return Math.min(MAX_FRAME_US, Math.round(n));
}

/**
 * Uniform subsample stride for a track longer than the cap.
 *
 * Ceiling division, the same shape as the AnimatedWebp FramePlan: stride 2 over
 * 1000 frames yields 500, never 501. Always ≥ 1, so a short GIF is untouched.
 */
export function strideFor(total, maxFrames = MAX_FRAMES) {
  const t = Math.max(0, Math.floor(Number(total) || 0));
  const m = Math.max(1, Math.floor(Number(maxFrames) || MAX_FRAMES));
  if (t <= m) return 1;
  return Math.ceil(t / m);
}

/** The frame indices actually decoded. Always starts at 0 and never exceeds the cap. */
export function pickFrameIndices(total, maxFrames = MAX_FRAMES) {
  const t = Math.max(0, Math.floor(Number(total) || 0));
  const stride = strideFor(t, maxFrames);
  const out = [];
  for (let i = 0; i < t; i += stride) out.push(i);
  return out;
}

/**
 * Fit w×h inside a maxBox square, EVEN on both axes.
 *
 * H.264 chroma is subsampled 4:2:0; an odd dimension is either rejected outright
 * or silently rounded by the encoder, and "silently rounded" means the muxer's
 * declared size and the real one disagree. Round DOWN to even, never below 2.
 */
export function fitBox(w, h, maxBox) {
  const w0 = Math.max(1, Math.floor(Number(w) || 0));
  const h0 = Math.max(1, Math.floor(Number(h) || 0));
  const box = Math.max(2, Math.floor(Number(maxBox) || 0) || 720);
  const scale = Math.min(1, box / Math.max(w0, h0));
  const even = (v) => Math.max(2, Math.floor(v / 2) * 2);
  return { w: even(Math.round(w0 * scale)), h: even(Math.round(h0 * scale)) };
}

/** Fit w×h to a target HEIGHT (the micro-preview's rule), even on both axes. */
export function fitHeight(w, h, targetH) {
  const w0 = Math.max(1, Math.floor(Number(w) || 0));
  const h0 = Math.max(1, Math.floor(Number(h) || 0));
  const t = Math.max(2, Math.floor(Number(targetH) || 0) || 240);
  const scale = Math.min(1, t / h0);
  const even = (v) => Math.max(2, Math.floor(v / 2) * 2);
  return { w: even(Math.round(w0 * scale)), h: even(Math.round(h0 * scale)) };
}

/** Frames per second implied by a filmstrip. Never 0, never absurd. */
export function fpsOf(frameCount, durMs) {
  const f = Math.max(0, Math.floor(Number(frameCount) || 0));
  const d = Math.max(0, Number(durMs) || 0);
  if (f < 2 || d <= 0) return 1;
  return Math.min(120, Math.max(1, (f * 1000) / d));
}

/* ------------------------------------------------------- the decode itself */

/**
 * Downscale one decoded frame to w×h.
 *
 * createImageBitmap's resize options are the fast path; some runtimes ship
 * ImageDecoder and reject them (Safari 26 does), so the fallback draws through an
 * OffscreenCanvas. Both paths exist in the DtRH spawner for the same reason.
 */
async function resizeFrame(image, w, h, scratchRef) {
  try {
    return await createImageBitmap(image, {
      resizeWidth: w, resizeHeight: h, resizeQuality: 'medium',
    });
  } catch (_e) {
    const OC = globalThis.OffscreenCanvas;
    if (!OC) throw new Error('no-resize-path');
    if (!scratchRef.c || scratchRef.c.width !== w || scratchRef.c.height !== h) {
      scratchRef.c = new OC(w, h);
      scratchRef.ctx = scratchRef.c.getContext('2d');
    }
    if (!scratchRef.ctx) throw new Error('no-2d-context');
    scratchRef.ctx.clearRect(0, 0, w, h);
    scratchRef.ctx.drawImage(image, 0, 0, w, h);
    return await createImageBitmap(scratchRef.c);
  }
}

/** Close every bitmap in a filmstrip. Safe on a partial or already-closed strip. */
export function closeFilmstrip(strip) {
  const frames = (strip && strip.frames) || (Array.isArray(strip) ? strip : []);
  for (const f of frames) {
    try { f && f.bitmap && f.bitmap.close && f.bitmap.close(); } catch (_e) { /* already gone */ }
  }
}

/**
 * Decode an animated GIF / WebP into a filmstrip.
 *
 * @param {ArrayBuffer|Uint8Array} buf   the whole file (lane B never streams)
 * @param {string} mime                  'image/gif' | 'image/webp'
 * @param {object} [opts]
 * @param {number} [opts.maxBox=720]     long-edge ceiling for the output frames
 * @param {number} [opts.maxFrames=900]  decode ceiling; past it, uniform stride
 * @param {function} [opts.onProgress]   0..1, called a few times per second
 * @param {function} [opts.shouldStop]   returns true to abandon the decode
 * @returns {Promise<{frames:Array<{bitmap:ImageBitmap,tsUs:number,durUs:number}>,
 *                    w:number,h:number,durMs:number,fps:number,
 *                    srcW:number,srcH:number,srcFrames:number,stride:number}>}
 *
 * Throws (never returns a half-strip): 'no-image-decoder', 'no-frames', or
 * whatever the decoder itself raised. On a throw every bitmap taken so far is
 * closed — an ImageBitmap is GPU memory and the GC is in no hurry.
 */
export async function decodeFilmstrip(buf, mime, opts = {}) {
  const ID = globalThis.ImageDecoder;
  if (typeof ID !== 'function') throw new Error('no-image-decoder');

  const maxBox = Math.max(16, Math.floor(Number(opts.maxBox) || 720));
  const maxFrames = Math.max(1, Math.floor(Number(opts.maxFrames) || MAX_FRAMES));
  const onProgress = typeof opts.onProgress === 'function' ? opts.onProgress : null;
  const shouldStop = typeof opts.shouldStop === 'function' ? opts.shouldStop : null;

  const data = buf instanceof Uint8Array ? buf : new Uint8Array(buf);
  const decoder = new ID({ data, type: String(mime || 'image/gif') });
  const scratchRef = { c: null, ctx: null };
  const frames = [];

  try {
    await decoder.tracks.ready;
    // `completed` resolves when the decoder has consumed the whole input, which
    // is the only moment frameCount is trustworthy for a track it had to parse.
    // We already hold every byte, so this settles immediately — but a runtime
    // that does not expose it must not take the decode down.
    try { if (decoder.completed) await decoder.completed; } catch (_e) { /* frameCount below is still the best answer */ }

    const track = decoder.tracks.selectedTrack || null;
    const total = Math.max(1, Number(track && track.frameCount) || 1);
    const indices = pickFrameIndices(total, maxFrames);
    const stride = strideFor(total, maxFrames);

    let srcW = 0, srcH = 0, outW = 0, outH = 0;
    let tsUs = 0;

    for (let i = 0; i < indices.length; i++) {
      if (shouldStop && shouldStop()) break;
      let image = null;
      try {
        ({ image } = await decoder.decode({ frameIndex: indices[i] }));
      } catch (e) {
        // A track that lied about frameCount (or a truncated file) stops here
        // rather than failing the whole job — what we already have is playable.
        if (frames.length > 0) break;
        throw e;
      }
      if (!srcW) {
        srcW = image.displayWidth || image.codedWidth || 2;
        srcH = image.displayHeight || image.codedHeight || 2;
        const fit = fitBox(srcW, srcH, maxBox);
        outW = fit.w; outH = fit.h;
      }
      // Every frame this one stands in for contributes its delay: a strided
      // filmstrip must still run for the GIF's real wall-clock length.
      const durUs = normalizeDurUs(image.duration) * stride;
      let bitmap = null;
      try {
        bitmap = await resizeFrame(image, outW, outH, scratchRef);
      } finally {
        try { image.close(); } catch (_e) { /* ignore */ }
      }
      frames.push({ bitmap, tsUs, durUs });
      tsUs += durUs;
      if (onProgress && (i % 8 === 0 || i === indices.length - 1)) {
        try { onProgress((i + 1) / indices.length); } catch (_e) { /* a reporter must never fail a job */ }
      }
    }

    if (frames.length === 0) throw new Error('no-frames');

    const durMs = Math.max(1, Math.round(tsUs / 1000));
    return {
      frames,
      w: outW, h: outH,
      durMs,
      fps: fpsOf(frames.length, durMs),
      srcW, srcH,
      srcFrames: total,
      stride,
    };
  } catch (e) {
    closeFilmstrip(frames);
    throw e;
  } finally {
    try { decoder.close(); } catch (_e) { /* ignore */ }
  }
}

export default decodeFilmstrip;
