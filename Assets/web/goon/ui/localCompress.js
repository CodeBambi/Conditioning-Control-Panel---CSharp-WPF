/* ============================================================================
 * ui/localCompress.js — the STANDALONE page's own compression lanes.
 *
 * In the app, compression happens in C# (TransferCompressionService) and the
 * page only drives lane B for animated GIF/WebP. In a plain browser there is no
 * host at all, and until now that meant the picker adopted only files that were
 * already small enough to send as-is (≤ MAX_EXEMPT_BYTES, 8 MB). A player who
 * handed over a 1 GB zip of real photos got told everything in it was "too big",
 * which is not a limit — it is a missing feature.
 *
 * The wire's actual ceiling for a NON-exempt artifact is MAX_ARTIFACT_BYTES
 * (64 MB). So the page can adopt big media the same way the C# app does: by
 * compressing it into an artifact that fits. This module is the two lanes.
 *
 *   IMAGE  decode -> downscale to ≤1920 long edge -> WebP q0.80.
 *          WebP is the goal, not the promise: a runtime that cannot encode it
 *          (Safari before 14, and anything else whose canvas quietly answers
 *          PNG) is DETECTED BY THE BLOB IT PRODUCED, never by a user-agent
 *          sniff, and we re-encode as JPEG q0.82. Both mimes are in the wire's
 *          ACCEPT_MIME, so either answer is sendable.
 *
 *   ANIMATED  the SAME machinery lane B already uses: encode/gifDecode.js takes
 *          the file apart into a filmstrip and encode/encodeWorker.js muxes an
 *          H.264 mp4. What is different here is only the PLUMBING — the hosted
 *          driver in assetsStore.js answers a host `encode-request` and posts
 *          the result back over the bridge in base64 parts; this one is called
 *          directly and hands the bytes back to its caller. The worker itself is
 *          shared and untouched, so lane B cannot regress from anything here.
 *
 * WHAT IS NOT HERE, ON PURPOSE: video transcoding. An mp4/webm over 64 MB is
 * counted and said out loud, because a browser-side H.264 -> H.264 transcode
 * means decoding every frame through WebCodecs at real cost for a result we
 * cannot promise, and "the video is too big to send" is a better answer than a
 * phone that heats up for four minutes and then fails anyway.
 *
 * NODE-IMPORT-SAFE, and that is load-bearing: not one browser global is touched
 * at module scope, there are no static imports of the encode tier (its worker is
 * reached lazily), and every capability is probed AT CALL TIME. The selftests
 * import this file under node — where there is no canvas, no createImageBitmap
 * and no WebCodecs — and only the pure helpers run.
 * ==========================================================================*/

/** Long-edge ceiling for a compressed still. 1920 is the duel's own display box. */
export const IMAGE_MAX_EDGE = 1920;
/** WebP quality. 0.80 is the knee: past it the file grows faster than the image improves. */
export const IMAGE_WEBP_QUALITY = 0.80;
/** JPEG quality for the fallback. Slightly higher — JPEG needs it to match. */
export const IMAGE_JPEG_QUALITY = 0.82;

/**
 * Hard ceiling on what we will DECODE. Not a wire limit: a decode allocates
 * width*height*4 bytes of RGBA whatever the file weighs, and a 500 MB TIFF on a
 * phone is a tab crash, not a slow adopt. Anything past this is counted, not tried.
 */
export const MAX_DECODE_BYTES = 80 * 1024 * 1024;

/** Long-edge ceiling for an animated artifact — lane B's own default. */
export const ANIM_MAX_BOX = 720;
/** Bitrate ceiling for an animated artifact (encodeWorker clamps to the same). */
export const ANIM_BITRATE = 1_800_000;
/** Frame-rate ceiling for an animated artifact. */
export const ANIM_MAX_FPS = 30;
/** A single local encode that has said nothing for this long is abandoned. */
export const ANIM_TIMEOUT_MS = 180000;

/**
 * An <img> decode that has neither loaded nor errored in this long is given up
 * on. Adoption is a SERIAL loop: one file whose load event never arrives (a
 * truncated jpeg on some runtimes, a stub environment) would wedge the whole
 * queue and leave the picker disabled forever, with no error anywhere.
 */
export const IMAGE_DECODE_TIMEOUT_MS = 30000;

/* ------------------------------------------------------------ pure helpers */

/** Whatever we were handed, as a Uint8Array view (never a copy when avoidable). */
function asU8(src) {
  if (src instanceof Uint8Array) return src;
  if (src instanceof ArrayBuffer) return new Uint8Array(src);
  if (src && typeof src === 'object' && src.buffer instanceof ArrayBuffer
      && typeof src.byteOffset === 'number' && typeof src.byteLength === 'number') {
    return new Uint8Array(src.buffer, src.byteOffset, src.byteLength);
  }
  return new Uint8Array(0);
}

/**
 * Fit w×h inside a square of `maxEdge`, never scaling UP.
 *
 * Stills do not care about even dimensions (there is no 4:2:0 chroma), so this
 * is deliberately NOT gifDecode's fitBox — it keeps the aspect ratio exactly and
 * floors to whole pixels, minimum 1.
 */
export function fitDown(w, h, maxEdge = IMAGE_MAX_EDGE) {
  const w0 = Math.max(1, Math.floor(Number(w) || 0));
  const h0 = Math.max(1, Math.floor(Number(h) || 0));
  const box = Math.max(1, Math.floor(Number(maxEdge) || IMAGE_MAX_EDGE));
  const scale = Math.min(1, box / Math.max(w0, h0));
  return {
    w: Math.max(1, Math.round(w0 * scale)),
    h: Math.max(1, Math.round(h0 * scale)),
    scaled: scale < 1,
  };
}

/**
 * How many image descriptors are in a GIF, up to `limit`.
 *
 * A pure walk of the block structure — header, optional global colour table,
 * then extensions (label + length-prefixed sub-blocks) and image descriptors
 * (9 bytes + optional local colour table + LZW data). Nothing is decoded; we
 * only need to know whether there is more than one frame.
 *
 * @returns {number} frames found (capped at `limit`), or -1 if the walk hit
 *   something it did not understand — which is an answer of "do not guess".
 */
export function gifFrameCount(bytes, limit = 2) {
  const u8 = asU8(bytes);
  const cap = Math.max(1, Math.floor(Number(limit) || 2));
  if (u8.length < 13) return -1;
  if (!(u8[0] === 0x47 && u8[1] === 0x49 && u8[2] === 0x46)) return -1;   // 'GIF'

  const skipBlocks = (at) => {
    let p = at;
    while (p < u8.length) {
      const len = u8[p];
      if (len === 0) return p + 1;
      p += 1 + len;
    }
    return -1;                                        // ran off the end mid-stream
  };

  let p = 13;
  const packed = u8[10];
  if (packed & 0x80) p += 3 * (1 << ((packed & 7) + 1));   // global colour table

  let frames = 0;
  while (p < u8.length) {
    const b = u8[p];
    if (b === 0x3B) break;                            // trailer
    if (b === 0x21) {                                 // extension: 0x21, label, sub-blocks
      p = skipBlocks(p + 2);
      if (p < 0) return -1;
      continue;
    }
    if (b === 0x2C) {                                 // image descriptor
      frames++;
      if (frames >= cap) return frames;
      p += 10;                                        // 0x2C + left/top/w/h + packed
      if (p > u8.length) return -1;
      const lf = u8[p - 1];
      if (lf & 0x80) p += 3 * (1 << ((lf & 7) + 1));  // local colour table
      p += 1;                                         // LZW minimum code size
      p = skipBlocks(p);
      if (p < 0) return -1;
      continue;
    }
    return -1;                                        // not a structure we know
  }
  return frames;
}

/** Is this WebP animated? Read from the VP8X chunk's flag byte, nowhere else. */
export function webpIsAnimated(bytes) {
  const u8 = asU8(bytes);
  if (u8.length < 21) return 'unknown';
  if (!(u8[0] === 0x52 && u8[1] === 0x49 && u8[2] === 0x46 && u8[3] === 0x46)) return 'unknown';   // RIFF
  if (!(u8[8] === 0x57 && u8[9] === 0x45 && u8[10] === 0x42 && u8[11] === 0x50)) return 'unknown'; // WEBP
  // 'VP8X' is the only container that can be animated. Plain 'VP8 ' / 'VP8L' is one frame.
  if (u8[12] === 0x56 && u8[13] === 0x50 && u8[14] === 0x38 && u8[15] === 0x58) {
    return (u8[20] & 0x02) ? 'animated' : 'still';
  }
  return 'still';
}

/**
 * Which lane a file belongs in, from its BYTES rather than its extension.
 *
 * 'unknown' means the sniff could not tell, and the caller must treat it as
 * animated: sending a one-frame mp4 for a still costs a few kilobytes, while
 * sending frame 1 of a 400-frame GIF loses the whole point of the file.
 *
 * @returns {'animated'|'still'|'unknown'}
 */
export function sniffAnimated(bytes, mime) {
  const m = String(mime || '').toLowerCase();
  const u8 = asU8(bytes);
  if (m === 'image/gif' || (u8[0] === 0x47 && u8[1] === 0x49 && u8[2] === 0x46)) {
    const n = gifFrameCount(u8, 2);
    if (n < 0) return 'unknown';
    return n > 1 ? 'animated' : 'still';
  }
  if (m === 'image/webp' || (u8[8] === 0x57 && u8[9] === 0x45 && u8[10] === 0x42 && u8[11] === 0x50)) {
    return webpIsAnimated(u8);
  }
  return 'still';
}

/**
 * Can this runtime encode an animated file at all?
 *
 * The cheap, SYNCHRONOUS half of probeEncode — asked at call time, before a
 * Worker is constructed and before a 40 MB buffer is transferred into it, so a
 * runtime without WebCodecs fails in a microsecond rather than after paying for
 * both. The full async probe (codec strings, hardware hint) is the HOST's
 * question and lives in assetsStore.probeEncode; this one only needs to know
 * whether the three constructors exist.
 *
 * NOTE the deliberate absence of a MediaRecorder fallback here. The hosted lane
 * has one because the host has already committed to the job; standalone would
 * rather say "that gif is too big to send" instantly than spend thirty seconds
 * of a phone's main thread painting frames in real time for a maybe.
 */
export function canEncodeAnimated() {
  return typeof globalThis.VideoEncoder === 'function'
    && typeof globalThis.VideoFrame === 'function'
    && typeof globalThis.ImageDecoder === 'function';
}

/** The file extension a compressed artifact should carry. */
export function extForMime(mime) {
  switch (String(mime || '').toLowerCase()) {
    case 'video/mp4': return 'mp4';
    case 'video/webm': return 'webm';
    case 'image/webp': return 'webp';
    case 'image/jpeg': return 'jpg';
    case 'image/png': return 'png';
    case 'image/gif': return 'gif';
    default: return '';
  }
}

/* ------------------------------------------------------- runtime plumbing */

/** Bytes out of a File/Blob/ArrayBuffer/Uint8Array, as an ArrayBuffer. */
async function toArrayBuffer(src) {
  if (src instanceof ArrayBuffer) return src;
  if (src && typeof src.arrayBuffer === 'function') return await src.arrayBuffer();
  const u8 = asU8(src);
  return u8.buffer.slice(u8.byteOffset, u8.byteOffset + u8.byteLength);
}

/** A Blob view of the source, for the decoders that want one. */
async function toBlob(src, mime) {
  const type = String(mime || '');
  if (src && typeof src.arrayBuffer === 'function' && typeof src.size === 'number') {
    // Already a File/Blob. Keep it: createImageBitmap(File) skips a copy.
    if (!type || String(src.type || '') === type) return src;
  }
  const B = globalThis.Blob;
  if (typeof B !== 'function') return null;
  const buf = await toArrayBuffer(src);
  return new B([buf], { type });
}

/** OffscreenCanvas when it exists, a real <canvas> when it does not, null in node. */
function makeCanvas(w, h) {
  const OC = globalThis.OffscreenCanvas;
  if (typeof OC === 'function') {
    try {
      const c = new OC(w, h);
      if (c && typeof c.getContext === 'function') return c;
    } catch (_e) { /* fall through to the DOM canvas */ }
  }
  const doc = globalThis.document;
  if (doc && typeof doc.createElement === 'function') {
    try {
      const c = doc.createElement('canvas');
      if (c && typeof c.getContext === 'function') { c.width = w; c.height = h; return c; }
    } catch (_e) { /* no DOM canvas either */ }
  }
  return null;
}

/** One blob out of a canvas, whichever of the two APIs this runtime has. */
async function canvasToBlob(canvas, type, quality) {
  if (typeof canvas.convertToBlob === 'function') {
    try { return await canvas.convertToBlob({ type, quality }); } catch (_e) { return null; }
  }
  if (typeof canvas.toBlob === 'function') {
    return await new Promise((res) => {
      let settled = false;
      const fin = (b) => { if (!settled) { settled = true; res(b || null); } };
      try { canvas.toBlob(fin, type, quality); } catch (_e) { fin(null); }
      // A canvas that refuses the type outright can drop the callback entirely.
      setTimeout(() => fin(null), 15000);
    });
  }
  return null;
}

/**
 * Decode one still into something drawable.
 *
 * EXIF ORIENTATION IS HANDLED THE CHEAP WAY: `imageOrientation:'from-image'`.
 * Without it a phone photo taken in portrait draws sideways, and the only other
 * fix is parsing the EXIF block by hand. Runtimes that reject the option get a
 * plain createImageBitmap, then an <img> — which applies orientation itself,
 * since `image-orientation: from-image` has been the CSS default for years.
 */
async function decodeStill(blob) {
  const CIB = globalThis.createImageBitmap;
  if (typeof CIB === 'function') {
    try {
      const bm = await CIB(blob, { imageOrientation: 'from-image' });
      return { image: bm, w: bm.width, h: bm.height, close: () => { try { bm.close(); } catch (_e) { /* ignore */ } } };
    } catch (_e) { /* the option is not universal — try without it */ }
    try {
      const bm = await CIB(blob);
      return { image: bm, w: bm.width, h: bm.height, close: () => { try { bm.close(); } catch (_e) { /* ignore */ } } };
    } catch (_e) { /* fall through to <img> */ }
  }

  const doc = globalThis.document;
  const U = globalThis.URL;
  if (!doc || typeof doc.createElement !== 'function' || !U || typeof U.createObjectURL !== 'function') {
    throw new Error('no-image-decoder');
  }
  const url = U.createObjectURL(blob);
  try {
    const img = await new Promise((res, rej) => {
      let settled = false;
      const el = doc.createElement('img');
      const fin = (fn, arg) => { if (!settled) { settled = true; fn(arg); } };
      el.onload = () => fin(res, el);
      el.onerror = () => fin(rej, new Error('decode-failed'));
      el.decoding = 'sync';
      // See IMAGE_DECODE_TIMEOUT_MS: a load event that never comes must not be
      // able to stop the adoption loop.
      try {
        const t = setTimeout(() => fin(rej, new Error('decode-timeout')), IMAGE_DECODE_TIMEOUT_MS);
        if (t && typeof t.unref === 'function') t.unref();
      } catch (_e) { /* no timers — the events are all we have */ }
      el.src = url;
    });
    const w = img.naturalWidth || img.width || 0;
    const h = img.naturalHeight || img.height || 0;
    if (!w || !h) throw new Error('decode-failed');
    return { image: img, w, h, close: () => { try { U.revokeObjectURL(url); } catch (_e) { /* ignore */ } } };
  } catch (e) {
    try { U.revokeObjectURL(url); } catch (_e) { /* ignore */ }
    throw e;
  }
}

/* ============================================================================
 * THE ANIMATED LANE — the same worker lane B drives, without the bridge.
 *
 * The hosted driver (createEncodeDriver in ui/assetsStore.js) exists to answer a
 * host `encode-request`: fetch by URL, encode, base64-chunk the result into
 * `cache-put` frames, heartbeat so the service's 3-minute stale timer never
 * fires. NONE of that applies to a file the player just picked in a browser —
 * there is no host, no job id the host knows about and no cache to put into.
 *
 * So this driver is the same worker with the protocol stripped to what it is:
 * post bytes, get an mp4. It never touches `bridge`, so the two drivers cannot
 * contend for anything, and a worker failure here falls back to running the very
 * same `runEncodeJob` on the main thread rather than to a different encoder.
 * ==========================================================================*/

export function createLocalEncodeDriver({ workerFactory = null, logger = null } = {}) {
  let worker = null;
  let workerDead = false;
  let seq = 0;
  let disposed = false;
  /** jobId -> {resolve, reject, onProgress, timer} */
  const jobs = new Map();

  const warn = (m) => { try { logger?.warn?.('[GG local-encode] ' + m); } catch (_e) { /* ignore */ } };

  function settle(id, fn, arg) {
    const j = jobs.get(id);
    if (!j) return;
    jobs.delete(id);
    if (j.timer) { try { clearTimeout(j.timer); } catch (_e) { /* ignore */ } }
    try { fn(j, arg); } catch (_e) { /* a settled promise cannot be settled twice */ }
  }

  function onMessage(m) {
    if (!m || disposed) return;
    const id = String(m.jobId || '');
    const j = jobs.get(id);
    if (!j) return;
    if (m.kind === 'progress') {
      j.touched = Date.now();
      try { j.onProgress?.(Number(m.pct) || 0); } catch (_e) { /* a reporter never fails a job */ }
      return;
    }
    if (m.kind === 'fail') { settle(id, (job, why) => job.reject(new Error(why)), String(m.reason || 'encode-failed')); return; }
    if (m.kind === 'done') {
      settle(id, (job, out) => job.resolve(out), {
        art: m.art, w: Number(m.w) || 0, h: Number(m.h) || 0, durMs: Number(m.durMs) || 0,
      });
    }
  }

  function ensureWorker() {
    if (worker || workerDead) return worker;
    try {
      if (typeof workerFactory === 'function') worker = workerFactory();
      else if (typeof Worker === 'function') {
        // RELATIVE, for the same reason the hosted driver's is: the app serves
        // Resources/web (so this is /goon/encode/...) and the headless harness
        // serves goon/ as the root (so it is /encode/...). An absolute path
        // resolves in exactly one of the two.
        worker = new Worker(new URL('../encode/encodeWorker.js', import.meta.url), { type: 'module' });
      }
    } catch (e) {
      warn('worker construction failed: ' + ((e && e.message) || e));
      worker = null;
    }
    if (!worker) { workerDead = true; return null; }
    try {
      worker.onmessage = (e) => onMessage(e && e.data);
      worker.onerror = (e) => {
        warn('worker error: ' + ((e && e.message) || e));
        // A worker that threw is not trusted for the next job either; every job
        // waiting on it is failed rather than left hanging on a dead port.
        try { worker.terminate(); } catch (_e) { /* ignore */ }
        worker = null;
        workerDead = true;
        for (const id of Array.from(jobs.keys())) settle(id, (job) => job.reject(new Error('worker-error')));
      };
    } catch (_e) { /* a stub worker without the setters is fine */ }
    return worker;
  }

  /**
   * Encode one animated file into an mp4.
   *
   * @param {ArrayBuffer} buf  TRANSFERRED to the worker — do not read it after.
   * @returns {Promise<{art:ArrayBuffer, w:number, h:number, durMs:number}>}
   */
  async function encode(buf, mime, cfg = {}, onProgress = null) {
    if (disposed) throw new Error('disposed');
    const w = ensureWorker();
    if (!w) return await encodeOnMainThread(buf, mime, cfg, onProgress);

    seq += 1;
    const id = 'local-' + seq;
    const job = { onProgress, touched: Date.now(), resolve: null, reject: null, timer: null };
    const p = new Promise((res, rej) => { job.resolve = res; job.reject = rej; });
    jobs.set(id, job);
    try {
      job.timer = setTimeout(() => settle(id, (jj) => jj.reject(new Error('encode-timeout'))), ANIM_TIMEOUT_MS);
      if (job.timer && typeof job.timer.unref === 'function') job.timer.unref();
    } catch (_e) { /* no timers is not fatal */ }

    try {
      w.postMessage({ kind: 'encode', jobId: id, buf, mime, cfg }, [buf]);
    } catch (e) {
      settle(id, (jj) => jj.reject(new Error('post-failed')));
      warn('postMessage failed: ' + ((e && e.message) || e));
      throw new Error('post-failed');
    }
    return await p;
  }

  /**
   * No Worker (or it died): run the identical job here. Slower and it blocks the
   * page, but "this browser cannot send GIFs" is a much worse answer, and the
   * import is dynamic so a runtime that never needs it never pays for the muxer.
   */
  async function encodeOnMainThread(buf, mime, cfg, onProgress) {
    let mod = null;
    try { mod = await import('../encode/encodeWorker.js'); } catch (e) {
      warn('encode module unavailable: ' + ((e && e.message) || e));
      throw new Error('no-encoder');
    }
    if (!mod || typeof mod.runEncodeJob !== 'function') throw new Error('no-encoder');
    const out = await mod.runEncodeJob(buf, mime, cfg, {
      onProgress: (pct) => { try { onProgress?.(pct); } catch (_e) { /* ignore */ } },
      shouldStop: () => disposed,
    });
    return { art: out.art, w: out.w, h: out.h, durMs: out.durMs };
  }

  return {
    encode,
    get busy() { return jobs.size > 0; },
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const id of Array.from(jobs.keys())) settle(id, (job) => job.reject(new Error('disposed')));
      if (worker) { try { worker.terminate(); } catch (_e) { /* ignore */ } worker = null; }
    },
  };
}

/* --------------------------------------------------------------- the lanes */

/**
 * Compress one still into a ≤1920-edge WebP (or JPEG where WebP is refused).
 *
 * @param {File|Blob|ArrayBuffer|Uint8Array} src
 * @param {string} mime  the SOURCE mime — only used to build a Blob when needed
 * @param {object} [o]   {maxEdge, quality, jpegQuality}
 * @returns {Promise<{blob:Blob, mime:string, w:number, h:number, kind:'image'}>}
 */
export async function compressImage(src, mime, o = {}) {
  const maxEdge = Math.max(16, Math.floor(Number(o.maxEdge) || IMAGE_MAX_EDGE));
  const q = Math.min(1, Math.max(0.1, Number(o.quality) || IMAGE_WEBP_QUALITY));
  const jq = Math.min(1, Math.max(0.1, Number(o.jpegQuality) || IMAGE_JPEG_QUALITY));

  const blob = await toBlob(src, mime);
  if (!blob) throw new Error('no-blob');
  const dec = await decodeStill(blob);
  try {
    const fit = fitDown(dec.w, dec.h, maxEdge);
    const canvas = makeCanvas(fit.w, fit.h);
    if (!canvas) throw new Error('no-canvas');
    // OffscreenCanvas takes its size at construction; a DOM canvas needs it set.
    try { canvas.width = fit.w; canvas.height = fit.h; } catch (_e) { /* read-only on some stubs */ }
    const ctx = canvas.getContext('2d');
    if (!ctx || typeof ctx.drawImage !== 'function') throw new Error('no-2d-context');
    ctx.drawImage(dec.image, 0, 0, fit.w, fit.h);

    // WEBP IS ASKED FOR, NEVER ASSUMED. Safari's canvas answers a PNG for an
    // unsupported type instead of failing, so the produced blob's own type is
    // the only trustworthy signal — and a PNG of a photo is bigger than the
    // source we were trying to shrink.
    let out = await canvasToBlob(canvas, 'image/webp', q);
    let type = String((out && out.type) || '').toLowerCase();
    if (!out || type.indexOf('image/webp') !== 0) {
      const jpg = await canvasToBlob(canvas, 'image/jpeg', jq);
      if (jpg && jpg.size > 0) { out = jpg; type = String(jpg.type || 'image/jpeg').toLowerCase(); }
    }
    if (!out || !out.size) throw new Error('encode-failed');

    const outMime = type.indexOf('image/webp') === 0 ? 'image/webp'
      : type.indexOf('image/png') === 0 ? 'image/png'
        : 'image/jpeg';
    return { blob: out, mime: outMime, w: fit.w, h: fit.h, kind: 'image' };
  } finally {
    dec.close();
  }
}

/**
 * Compress one animated GIF / animated WebP into an mp4, via lane B's worker.
 *
 * `codec` is reported because the transfer lane's HEVC handshake needs to know what this artifact
 * actually IS: encode/encodeWorker.js only ever asks with an `avc1.*` string (codecForSize picks
 * the level, never the family), so H.264 is a fact here, not a guess — and a peer with any video
 * decoder at all has that one.
 *
 * @returns {Promise<{blob:Blob, mime:'video/mp4', w:number, h:number, durMs:number,
 *                    kind:'video', codec:'avc1'}>}
 */
export async function compressGif(src, mime, o = {}) {
  const driver = o.driver || createLocalEncodeDriver({ logger: o.logger || null });
  const owned = !o.driver;
  const type = String(mime || '').toLowerCase() === 'image/webp' ? 'image/webp' : 'image/gif';
  const cfg = {
    maxBox: Math.max(64, Number(o.maxBox) || ANIM_MAX_BOX),
    bitrate: Math.max(100000, Number(o.bitrate) || ANIM_BITRATE),
    maxFps: Math.max(1, Number(o.maxFps) || ANIM_MAX_FPS),
    // The micro-preview is a HOSTED concept (a second part stream on the cache
    // bridge). Standalone has nowhere to put one, so it is not encoded at all.
    wantPrev: false,
  };
  try {
    const buf = await toArrayBuffer(src);
    const out = await driver.encode(buf, type, cfg, o.onProgress || null);
    const B = globalThis.Blob;
    if (typeof B !== 'function') throw new Error('no-blob');
    const blob = new B([out.art], { type: 'video/mp4' });
    if (!blob.size) throw new Error('empty-output');
    return { blob, mime: 'video/mp4', w: out.w, h: out.h, durMs: out.durMs, kind: 'video', codec: 'avc1' };
  } finally {
    if (owned) { try { driver.dispose(); } catch (_e) { /* ignore */ } }
  }
}

/**
 * The compressor the store adopts through.
 *
 * A FACTORY, not a module-level singleton, because the whole point of the seam
 * is that the node selftest hands the store a stub with the same two methods and
 * drives the POLICY without a canvas, a decoder or a Worker in sight. The real
 * one is exercised in the browser only.
 *
 * @param {object} [o] {logger, workerFactory}
 * @returns {{compressImage:Function, compressGif:Function, dispose:Function}}
 */
export function createLocalCompressor(o = {}) {
  let driver = null;
  let disposed = false;

  function ensureDriver() {
    if (!driver && !disposed) {
      driver = createLocalEncodeDriver({ workerFactory: o.workerFactory || null, logger: o.logger || null });
    }
    return driver;
  }

  return {
    compressImage(src, mime, opts = {}) {
      return compressImage(src, mime, opts);
    },
    compressGif(src, mime, opts = {}) {
      // Probed HERE, not inside compressGif: the primitive is also the seam a
      // test drives with an injected driver, and a global-sniffing guard inside
      // it would make that untestable. This is the production entry point, and
      // it is the one that must not build a worker it cannot use.
      if (!canEncodeAnimated()) return Promise.reject(new Error('no-encoder'));
      return compressGif(src, mime, Object.assign({ logger: o.logger || null }, opts, { driver: ensureDriver() }));
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      if (driver) { try { driver.dispose(); } catch (_e) { /* ignore */ } driver = null; }
    },
  };
}

export default createLocalCompressor;
