/* ============================================================================
 * decode.js - files in, master clock strips out.
 *
 * Three rules hold for every kind of media:
 *   1. Decode straight to the output resolution, cover fitted. The compositor
 *      never sees a full size frame, so a 1080p drop costs the same as a
 *      thumbnail from there on.
 *   2. Decode onto the 15 fps master clock. `media.strip` is one loop of the
 *      source already resampled, so playback is an array index.
 *   3. Keep the original file, one Blob reference, nothing copied. Changing
 *      orientation, or a canvas that grows past the strip, re-decodes from it.
 *
 * Gifs go through a module worker where the browser has one; every path has a
 * main thread fallback, because iPhone Safari is the floor we build to. Any
 * trouble on the worker path (no OffscreenCanvas 2d, a worker that dies or
 * hangs) drops the worker for good and the main thread takes the job.
 * ==========================================================================*/

import { GifReader } from '../vendor/omggif.module.js';
import { resampleIndices, totalDuration, FPS } from './clock.js';
import { coverFit } from './layout.js';

export const OUTPUT_SIZES = {
  landscape: { w: 480, h: 270 },
  portrait: { w: 270, h: 480 },
  square: { w: 360, h: 360 },
};
export const MAX_STRIP = 120;
export const THUMB_W = 120;
export const UNSUPPORTED = 'That file is not a gif, video or image';

export function sizeFor(orientation) {
  return OUTPUT_SIZES[orientation] || OUTPUT_SIZES.landscape;
}

/** What we think this file is, or null when we cannot use it. */
export function kindForFile(file) {
  const type = String((file && file.type) || '').toLowerCase();
  const name = String((file && file.name) || '').toLowerCase();
  if (type === 'image/gif' || name.endsWith('.gif')) return 'gif';
  if (type.startsWith('video/')) return 'video';
  if (type.startsWith('image/')) return 'image';
  if (/\.(mp4|webm|mov|m4v|ogv)$/.test(name)) return 'video';
  if (/\.(png|jpe?g|webp|avif|bmp)$/.test(name)) return 'image';
  return null;
}

let mediaCounter = 0;
function nextMediaId() {
  mediaCounter += 1;
  return 'm' + mediaCounter.toString(36) + '-' + Math.floor(Math.random() * 1e6).toString(36);
}

/**
 * Decode a dropped file.
 *
 * opts: { size:{w,h}, frames, fps, kind, route, id }
 *
 * Returns { id, name, kind, w, h, srcW, srcH, srcFrames, srcDurMs, strip, thumb, file, type }
 * where `w`/`h` are the canvas the strip was drawn at and `srcW`/`srcH` are the
 * file's own size, the only place the source aspect survives the decode.
 * `file` is the Blob it came from, kept for re-decodes; the bytes read out of
 * it live only for the decode.
 */
export async function decodeMedia(file, opts = {}) {
  const size = opts.size || OUTPUT_SIZES.landscape;
  const frames = Math.max(1, Math.min(MAX_STRIP, opts.frames || 75));
  const fps = opts.fps || FPS;
  // `kind` is the label the media carries ('spiral' is a gif with a job);
  // `route` is the decoder it goes through.
  const route = opts.route || kindForFile(file);
  if (!route) throw new Error(UNSUPPORTED);
  const kind = opts.kind || route;

  let out;
  if (route === 'video') {
    out = await decodeVideo(file, size, frames, fps);
  } else {
    const bytes = await readBytes(file);
    out = route === 'gif' ? await decodeGif(bytes, size, frames, fps) : await decodeImage(file, bytes, size);
  }
  if (!out.strip || !out.strip.length) throw new Error('Nothing came out of that file');

  const thumb = makeThumb(out.strip[0], size);
  return {
    id: opts.id || nextMediaId(),
    name: (file && file.name) || 'clip',
    kind,
    w: size.w,
    h: size.h,
    // the worker calls them w0/h0; either way the file's own size, never the canvas
    srcW: out.srcW || out.w0 || size.w,
    srcH: out.srcH || out.h0 || size.h,
    srcFrames: out.srcFrames,
    srcDurMs: out.srcDurMs,
    strip: out.strip,
    thumb: thumb,
    colour: averageColour(thumb),
    file: file || null,
    type: (file && file.type) || '',
  };
}

/** The pool's thumb: a square, so a wide gif and a tall one sit in the same grid. */
export const PROBE_SIZE = { w: 144, h: 144 };

/**
 * A cheap look at a file for the pool: the first frame only, drawn small.
 * Returns { name, kind, srcW, srcH, srcFrames, srcDurMs, thumb, colour, file, type }
 * and holds no strip, so a hundred of these cost what one decoded clip does.
 */
export async function probeMedia(file, opts = {}) {
  const m = await decodeMedia(file, { size: opts.size || PROBE_SIZE, frames: 1, kind: opts.kind, route: opts.route });
  return {
    name: m.name, kind: m.kind, srcW: m.srcW, srcH: m.srcH, srcFrames: m.srcFrames, srcDurMs: m.srcDurMs,
    thumb: m.strip[0], colour: m.colour, file: m.file, type: m.type,
  };
}

/** Re-decode an existing media entry at a new output size or strip length. */
export async function redecode(media, opts = {}) {
  const source = media.file || null;
  if (!source) throw new Error('That clip cannot be read again, drop it once more');
  const next = await decodeMedia(source, Object.assign({
    kind: media.kind,
    route: media.kind === 'spiral' ? kindForFile(source) || 'gif' : media.kind,
    id: media.id,
  }, opts));
  next.name = media.name;
  return next;
}

function readBytes(file) {
  if (file && typeof file.arrayBuffer === 'function') return file.arrayBuffer();
  return new Promise((res, rej) => {
    const fr = new FileReader();
    fr.onload = () => res(fr.result);
    fr.onerror = () => rej(new Error('That file could not be read'));
    fr.readAsArrayBuffer(file);
  });
}

/* ------------------------------------------------------------- canvases --*/

function makeCanvas(w, h) {
  const c = document.createElement('canvas');
  c.width = w; c.height = h;
  return c;
}

/** Draw a source onto a fresh output-size canvas, cover fitted and centred. */
function coverDraw(src, sw, sh, size) {
  const c = makeCanvas(size.w, size.h);
  const ctx = c.getContext('2d', { alpha: false });
  ctx.imageSmoothingQuality = 'high';
  ctx.fillStyle = '#000';
  ctx.fillRect(0, 0, size.w, size.h);
  const fit = coverFit(sw, sh, size.w, size.h);
  ctx.drawImage(src, fit.x, fit.y, fit.w, fit.h);
  return c;
}

/** A 120 px wide thumb for the media strip. */
export function makeThumb(frame, size) {
  const w = THUMB_W;
  const h = Math.max(1, Math.round((w * size.h) / size.w));
  const c = makeCanvas(w, h);
  const ctx = c.getContext('2d', { alpha: false });
  ctx.fillStyle = '#14142B';
  ctx.fillRect(0, 0, w, h);
  if (frame) ctx.drawImage(frame, 0, 0, w, h);
  return c;
}

/** The average colour of a media, for the tint panel's custom hue chip. */
export function averageColour(thumb) {
  try {
    const ctx = thumb.getContext('2d');
    const d = ctx.getImageData(0, 0, thumb.width, thumb.height).data;
    let r = 0, g = 0, b = 0, n = 0;
    for (let i = 0; i < d.length; i += 16) { r += d[i]; g += d[i + 1]; b += d[i + 2]; n++; }
    if (!n) return null;
    // lift it out of the mud so a dark clip still gives a usable hue
    const lift = (v) => Math.min(255, Math.round((v / n) * 1.35 + 24));
    const hex = (v) => v.toString(16).padStart(2, '0');
    return '#' + hex(lift(r)) + hex(lift(g)) + hex(lift(b));
  } catch {
    return null;
  }
}

/* ---------------------------------------------------------- gif: worker --*/

export const WORKER_JOB_TIMEOUT_MS = 30000;

let worker = null;
let workerOk = null;
let jobId = 0;
const jobs = new Map();

/** `?noworker=1` builds the worker from a url that does not exist, so the whole fallback path runs for real. */
function workerUrl() {
  let broken = false;
  try { broken = new URLSearchParams(location.search).get('noworker') === '1'; } catch { /* no location */ }
  return new URL(broken ? './decode.worker.missing.js' : './decode.worker.js', import.meta.url);
}

class WorkerGone extends Error {
  constructor(why) { super(why || 'worker unavailable'); this.workerGone = true; }
}

/** The worker is no good: stop it, forget it, and fail every job it still held. */
function dropWorker(why) {
  workerOk = false;
  const pending = [...jobs.values()];
  jobs.clear();
  if (worker) { try { worker.terminate(); } catch { /* already gone */ } }
  worker = null;
  for (const j of pending) j.reject(new WorkerGone(why));
}

function canUseWorker() {
  if (workerOk !== null) return workerOk;
  workerOk = false;
  try {
    if (typeof Worker === 'undefined' || typeof OffscreenCanvas === 'undefined') return false;
    if (typeof createImageBitmap !== 'function') return false;
    worker = new Worker(workerUrl(), { type: 'module' });
    worker.onmessage = (e) => {
      const job = jobs.get(e.data && e.data.id);
      if (!job) return;
      jobs.delete(e.data.id);
      clearTimeout(job.timer);
      if (e.data.error) job.reject(new Error(e.data.error));
      else job.resolve(e.data);
    };
    // a browser that cannot start a module worker fails here, and so does a worker that dies mid-job
    worker.onerror = () => dropWorker();
    workerOk = true;
  } catch {
    workerOk = false;
    worker = null;
  }
  return workerOk;
}

async function decodeGif(bytes, size, frames, fps) {
  if (canUseWorker()) {
    try {
      return await decodeGifInWorker(bytes, size, frames, fps);
    } catch (err) {
      // whatever went wrong over there, the main thread can still do the job
      if (!err || !err.workerGone) dropWorker();
    }
  }
  return decodeGifMain(bytes, size, frames, fps);
}

function decodeGifInWorker(bytes, size, frames, fps) {
  const id = ++jobId;
  // the worker takes the buffer, so hand it a copy and keep the original
  const copy = bytes.slice(0);
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { if (jobs.has(id)) dropWorker('the decoder stalled'); }, WORKER_JOB_TIMEOUT_MS);
    jobs.set(id, { resolve, reject, timer });
    worker.postMessage({ id, buf: copy, w: size.w, h: size.h, frames, fps }, [copy]);
  });
}

/** The same decode, on the main thread, for browsers without module workers. */
async function decodeGifMain(bytes, size, frames, fps) {
  const reader = new GifReader(new Uint8Array(bytes));
  const w0 = reader.width, h0 = reader.height, count = reader.numFrames();
  if (!w0 || !h0 || !count) throw new Error('That gif has no frames in it');

  const durations = [];
  for (let i = 0; i < count; i++) durations.push(Math.max(20, (reader.frameInfo(i).delay || 10) * 10));
  const srcDurMs = totalDuration(durations);
  const stripLen = Math.max(1, Math.min(frames, Math.ceil(srcDurMs / (1000 / fps))));
  const wanted = resampleIndices(durations, stripLen, fps);
  const need = new Set(wanted);

  const rgba = new Uint8ClampedArray(w0 * h0 * 4);
  const idata = new ImageData(rgba, w0, h0);
  const full = makeCanvas(w0, h0);
  const fctx = full.getContext('2d');
  const bySource = new Map();
  let prevDisposal = 0, prevInfo = null;
  const last = Math.max(...wanted);
  for (let i = 0; i <= last; i++) {
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
    bySource.set(i, coverDraw(full, w0, h0, size));
    if (i % 8 === 7) await tick();
  }
  return { strip: wanted.map((k) => bySource.get(k)), srcFrames: count, srcDurMs, srcW: w0, srcH: h0 };
}

function tick() {
  return new Promise((r) => setTimeout(r, 0));
}

/* ----------------------------------------------------------- image ------ */

async function decodeImage(file, bytes, size) {
  let src = null;
  if (typeof createImageBitmap === 'function') {
    try { src = await createImageBitmap(new Blob([bytes], { type: (file && file.type) || 'image/png' })); } catch { src = null; }
  }
  if (!src) src = await loadImageElement(bytes, (file && file.type) || 'image/png');
  const w = src.width || src.naturalWidth;
  const h = src.height || src.naturalHeight;
  if (!w || !h) throw new Error(UNSUPPORTED);
  const frame = coverDraw(src, w, h, size);
  if (src.close) src.close();
  return { strip: [frame], srcFrames: 1, srcDurMs: 0, srcW: w, srcH: h };
}

function loadImageElement(bytes, type) {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(new Blob([bytes], { type }));
    const img = new Image();
    img.onload = () => { URL.revokeObjectURL(url); resolve(img); };
    img.onerror = () => { URL.revokeObjectURL(url); reject(new Error(UNSUPPORTED)); };
    img.src = url;
  });
}

/* ----------------------------------------------------------- video ------ */

const SEEK_TIMEOUT_MS = 4000;
const OPEN_TIMEOUT_MS = 15000;
const SEEK_STALLS = 3;

async function decodeVideo(file, size, frames, fps) {
  const url = URL.createObjectURL(file);
  const v = document.createElement('video');
  v.muted = true;
  v.defaultMuted = true;
  v.playsInline = true;
  v.setAttribute('playsinline', '');
  v.preload = 'auto';
  v.src = url;
  try {
    await once(v, 'loadedmetadata', 'error', OPEN_TIMEOUT_MS, 'That video would not open');
    // the first frame has to be there before a seek can paint anything
    if (v.readyState < 2) await once(v, 'loadeddata', 'error', OPEN_TIMEOUT_MS, 'That video would not open');
    // Safari will not paint a frame until the element has run once
    try { await v.play(); v.pause(); } catch { /* autoplay refused, seeks still work */ }
    const vw = v.videoWidth, vh = v.videoHeight;
    if (!vw || !vh) throw new Error('That video would not open');
    const dur = Number.isFinite(v.duration) && v.duration > 0 ? v.duration : 1 / fps;
    const stripLen = Math.max(1, Math.min(frames, Math.round(dur * fps)));
    const strip = [];
    let stalls = 0;
    for (let i = 0; i < stripLen; i++) {
      const t = Math.min(dur - 1e-3, i / fps);
      if (Math.abs(v.currentTime - t) > 1e-4) {
        v.currentTime = t;
        // a seek that never lands would repeat the last frame; three in a row and the file is not decoding
        const landed = await once(v, 'seeked', 'error', SEEK_TIMEOUT_MS, null).then(() => true, () => false);
        stalls = landed ? 0 : stalls + 1;
        if (stalls >= SEEK_STALLS) throw new Error('That video would not decode');
      }
      strip.push(coverDraw(v, vw, vh, size));
    }
    return { strip, srcFrames: stripLen, srcDurMs: Math.round(dur * 1000), srcW: vw, srcH: vh };
  } finally {
    v.removeAttribute('src');
    try { v.load(); } catch { /* teardown */ }
    URL.revokeObjectURL(url);
  }
}

function once(target, ok, bad, ms, message) {
  return new Promise((resolve, reject) => {
    let done = false;
    const clean = () => {
      done = true;
      target.removeEventListener(ok, onOk);
      target.removeEventListener(bad, onBad);
      clearTimeout(timer);
    };
    const onOk = () => { if (!done) { clean(); resolve(); } };
    const onBad = () => { if (!done) { clean(); reject(new Error(message || 'decode failed')); } };
    const timer = setTimeout(onBad, ms);
    target.addEventListener(ok, onOk);
    target.addEventListener(bad, onBad);
  });
}

/* ---------------------------------------------------------- teardown ----- */

/** Let go of a media entry's bitmaps. */
export function disposeMedia(media) {
  if (!media || !media.strip) return;
  const seen = new Set();
  for (const f of media.strip) {
    if (!f || seen.has(f)) continue;
    seen.add(f);
    if (typeof f.close === 'function') f.close();
  }
  media.strip = [];
}

/** Shut the shared worker down. Only the dev page needs this. */
export function stopDecoder() {
  if (worker) { try { worker.terminate(); } catch { /* gone */ } }
  worker = null;
  workerOk = null;
  jobs.clear();
}
