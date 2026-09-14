/* ============================================================================
 * export.js - gif out, video out, and the size meter.
 *
 * Frames come from a draw callback so the exporter never has to know about
 * projects: it asks for frame i on a canvas and hands the pixels to gifenc.
 * Gif delays are whole centiseconds, which 15 fps does not divide into, so
 * the delay is carried with an accumulating error and comes out 70/60/70/70
 * and averages the real rate instead of drifting.
 *
 * The shrink ladder is one tap, not a settings page: full, then 12 fps, then
 * 12 fps at two thirds resolution.
 * ==========================================================================*/

import { GIFEncoder, quantize, applyPalette } from '../vendor/gifenc.esm.js';

export const DISCORD_CAP = 10 * 1024 * 1024;
export const DISCORD_SAFE = 8 * 1024 * 1024;
export const SAMPLE_FRAMES = 8;
export const ESTIMATE_FRAMES = 6;

export const LADDER = [
  { fps: 15, scale: 1, label: 'full' },
  { fps: 12, scale: 1, label: '12 fps' },
  { fps: 12, scale: 2 / 3, label: '12 fps, smaller' },
];

/** Whole centisecond delays that average out to the real frame rate. */
export function delaysFor(count, fps) {
  const step = 1000 / fps;
  const out = [];
  let want = 0, sent = 0;
  for (let i = 0; i < count; i++) {
    want += step;
    const d = Math.max(20, Math.round(want / 10) * 10 - sent);
    sent += d;
    out.push(d);
  }
  return out;
}

/** Which master frames a rung of the ladder actually writes. */
export function frameIndices(frames, fps, masterFps) {
  if (fps >= masterFps) {
    const all = [];
    for (let i = 0; i < frames; i++) all.push(i);
    return all;
  }
  const count = Math.max(1, Math.round((frames * fps) / masterFps));
  const out = [];
  for (let i = 0; i < count; i++) out.push(Math.min(frames - 1, Math.round((i * masterFps) / fps)));
  return out;
}

function makeCanvas(w, h) {
  const c = document.createElement('canvas');
  c.width = Math.max(1, Math.round(w));
  c.height = Math.max(1, Math.round(h));
  return c;
}

/* ------------------------------------------------------------- worker ----*/

/** The error an aborted export rejects with. `name` is 'AbortError' like fetch. */
export function abortError() {
  const err = new Error('Export cancelled');
  err.name = 'AbortError';
  return err;
}

function throwIfAborted(signal) {
  if (signal && signal.aborted) throw abortError();
}

/** `?noworker=1` builds the worker from a url that does not exist, so the whole fallback path runs for real. */
function workerUrl() {
  let broken = false;
  try { broken = new URLSearchParams(location.search).get('noworker') === '1'; } catch { /* no location */ }
  return new URL(broken ? './encode.worker.missing.js' : './encode.worker.js', import.meta.url);
}

export const READY_TIMEOUT_MS = 8000;

let worker = null;
let workerOk = null;
let jobSeq = 0;
// every open session, so a worker that dies takes none of them down silently
const sessions = new Set();

function dropWorker() {
  workerOk = false;
  const w = worker;
  worker = null;
  if (w) { try { w.terminate(); } catch { /* gone */ } }
  const open = [...sessions];
  sessions.clear();
  for (const s of open) s.fail(new Error('The encoder did not start'));
}

function getWorker() {
  if (workerOk === false) return null;
  if (worker) return worker;
  try {
    worker = new Worker(workerUrl(), { type: 'module' });
    worker.onerror = () => dropWorker();
    workerOk = true;
    return worker;
  } catch {
    workerOk = false;
    worker = null;
    return null;
  }
}

export function stopEncoder() {
  if (worker) { try { worker.terminate(); } catch { /* gone */ } }
  worker = null;
  workerOk = null;
  sessions.clear();
}

/**
 * One encode session, worker backed where possible.
 *
 * `start()` waits for the worker to say ready, racing an 8 s timeout; if the
 * worker never answers, errors or dies, the session quietly becomes a main
 * thread one and the export carries on. The sample is copied before it is
 * handed over so the fallback still has it.
 */
function openSession(w, h, sample) {
  const wk = getWorker();
  if (!wk) return mainThreadSession(w, h, sample);
  const id = ++jobSeq;
  const keep = sample.slice(0);

  const waiting = new Map();
  let done = null;
  let fallback = null; // the main thread session, once the worker has let us down
  const failAll = (err) => {
    if (done) { done.reject(err); done = null; }
    for (const p of waiting.values()) p.reject(err);
    waiting.clear();
  };
  const onMessage = (e) => {
    const m = e.data || {};
    if (m.id !== id) return;
    if (m.error) { failAll(new Error(m.error)); return; }
    if (m.ready) { const p = waiting.get('ready'); waiting.delete('ready'); if (p) p.resolve(); }
    if (m.wrote != null) { const p = waiting.get('f' + m.wrote); waiting.delete('f' + m.wrote); if (p) p.resolve(); }
    if (m.bytes) { if (done) done.resolve(new Uint8Array(m.bytes)); done = null; }
  };
  wk.addEventListener('message', onMessage);
  const entry = { fail: failAll };
  sessions.add(entry);
  const detach = () => { wk.removeEventListener('message', onMessage); sessions.delete(entry); };

  const expect = (key) => new Promise((resolve, reject) => waiting.set(key, { resolve, reject }));
  const ready = expect('ready');
  wk.postMessage({ id, cmd: 'begin', w, h, sample }, [sample]);

  let timer = 0;
  const timeout = new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('The encoder did not start')), READY_TIMEOUT_MS); });

  return {
    async start() {
      try {
        await Promise.race([ready, timeout]);
      } catch {
        // no worker for us: let it go and carry on here
        detach();
        if (worker === wk) dropWorker();
        fallback = mainThreadSession(w, h, keep);
        await fallback.start();
      } finally {
        clearTimeout(timer);
      }
    },
    async write(index, rgbaBuffer, delay) {
      if (fallback) return fallback.write(index, rgbaBuffer, delay);
      const p = expect('f' + index);
      wk.postMessage({ id, cmd: 'frame', index, data: rgbaBuffer, delay }, [rgbaBuffer]);
      await p;
    },
    finish() {
      if (fallback) return fallback.finish();
      return new Promise((resolve, reject) => {
        done = { resolve, reject };
        wk.postMessage({ id, cmd: 'finish' });
      }).finally(detach);
    },
    abort() {
      if (fallback) return fallback.abort();
      try { wk.postMessage({ id, cmd: 'abort' }); } catch { /* gone */ }
      detach();
    },
  };
}

/** The same encode without a worker, for browsers that refuse one. */
function mainThreadSession(w, h, sample) {
  const palette = quantize(new Uint8Array(sample), 256, { format: 'rgb444' });
  const gif = GIFEncoder();
  let first = true;
  return {
    async start() { /* nothing to wait for */ },
    async write(index, rgbaBuffer, delay) {
      const indexed = applyPalette(new Uint8Array(rgbaBuffer), palette, 'rgb444');
      gif.writeFrame(indexed, w, h, { palette: first ? palette : undefined, delay, repeat: 0, first });
      first = false;
      await new Promise((r) => setTimeout(r, 0));
    },
    async finish() { gif.finish(); return gif.bytes(); },
    abort() { /* nothing held */ },
  };
}

/* ---------------------------------------------------------------- gif ----*/

/**
 * Encode one rung.
 *
 * opts: { drawFrame(ctx, masterFrame), frames, masterFps, size, fps, scale,
 *         onProgress(0..1), only:[indices], signal:AbortSignal }
 * An aborted signal rejects with abortError() at the next frame.
 */
export async function encodeGif(opts) {
  throwIfAborted(opts.signal);
  const scale = opts.scale || 1;
  const w = Math.max(2, Math.round(opts.size.w * scale));
  const h = Math.max(2, Math.round(opts.size.h * scale));
  const canvas = makeCanvas(w, h);
  const ctx = canvas.getContext('2d', { alpha: false, willReadFrequently: true });
  ctx.imageSmoothingQuality = 'high';

  const list = opts.only || frameIndices(opts.frames, opts.fps || opts.masterFps, opts.masterFps);
  const delays = delaysFor(list.length, opts.fps || opts.masterFps);

  const draw = (masterFrame) => {
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, w, h);
    ctx.save();
    if (scale !== 1) ctx.scale(scale, scale);
    opts.drawFrame(ctx, masterFrame, { w: opts.size.w, h: opts.size.h });
    ctx.restore();
  };

  // global palette from frames spread across the loop
  const step = Math.max(1, Math.floor(list.length / SAMPLE_FRAMES));
  const sampleFrames = [];
  for (let i = 0; i < list.length && sampleFrames.length < SAMPLE_FRAMES; i += step) sampleFrames.push(list[i]);
  const sample = new Uint8Array(sampleFrames.length * w * h * 4);
  for (let i = 0; i < sampleFrames.length; i++) {
    throwIfAborted(opts.signal);
    draw(sampleFrames[i]);
    sample.set(ctx.getImageData(0, 0, w, h).data, i * w * h * 4);
  }

  const session = openSession(w, h, sample.buffer);
  try {
    await session.start();
    for (let i = 0; i < list.length; i++) {
      throwIfAborted(opts.signal);
      draw(list[i]);
      const data = ctx.getImageData(0, 0, w, h).data;
      const copy = new Uint8Array(data).buffer;
      await session.write(i, copy, delays[i]);
      if (opts.onProgress) opts.onProgress((i + 1) / list.length);
    }
    throwIfAborted(opts.signal);
    const bytes = await session.finish();
    return new Blob([bytes], { type: 'image/gif' });
  } catch (err) {
    session.abort();
    throw err;
  }
}

/**
 * Gif with the shrink ladder applied until it fits.
 * Returns { blob, rung } so the sheet can say which rung it landed on.
 */
export async function exportGifFitted(opts) {
  const cap = opts.maxBytes || Infinity;
  let last = null;
  const from = Math.max(0, Math.min(LADDER.length - 1, opts.startRung | 0));
  for (let i = from; i < LADDER.length; i++) {
    const rung = LADDER[i];
    const blob = await encodeGif(Object.assign({}, opts, { fps: rung.fps, scale: rung.scale }));
    last = { blob, rung };
    if (blob.size <= cap) return last;
    if (opts.onRung) opts.onRung(rung, blob.size);
  }
  return last;
}

/** Encode six frames and scale up: the size meter, not a promise. */
export async function estimateSize(opts) {
  const list = [];
  for (let i = 0; i < ESTIMATE_FRAMES; i++) {
    list.push(Math.min(opts.frames - 1, Math.round((i * opts.frames) / ESTIMATE_FRAMES)));
  }
  const blob = await encodeGif(Object.assign({}, opts, { only: list, onProgress: null }));
  // the first frame carries the palette and the headers; the rest are the rate
  const overhead = 1024;
  const perFrame = Math.max(256, (blob.size - overhead) / ESTIMATE_FRAMES);
  return Math.round(overhead + perFrame * opts.frames);
}

/* -------------------------------------------------------------- video ----*/

export const VIDEO_TYPES = [
  'video/webm;codecs=vp9',
  'video/webm;codecs=vp8',
  'video/webm',
  'video/mp4;codecs=avc1',
  'video/mp4',
];

export function videoMimeType() {
  if (typeof MediaRecorder === 'undefined') return null;
  for (const t of VIDEO_TYPES) {
    try { if (MediaRecorder.isTypeSupported(t)) return t; } catch { /* keep looking */ }
  }
  return '';
}

export const NO_VIDEO = 'This browser cannot record video, save a gif instead';

/** True when both halves of the recording path exist. */
export function canRecordVideo() {
  if (typeof MediaRecorder === 'undefined') return false;
  if (typeof HTMLCanvasElement === 'undefined') return false;
  return typeof HTMLCanvasElement.prototype.captureStream === 'function';
}

/**
 * Record the loop three times through captureStream.
 * opts: { drawFrame, frames, fps, size, loops, onProgress, signal }
 *
 * The stream runs at the master rate and the timer below paints one frame
 * per tick, which is all captureStream(fps) needs; requestFrame belongs to
 * captureStream(0) and is not mixed in.
 */
export async function exportVideo(opts) {
  if (!canRecordVideo()) throw new Error(NO_VIDEO);
  throwIfAborted(opts.signal);
  const size = opts.size;
  const canvas = makeCanvas(size.w, size.h);
  const ctx = canvas.getContext('2d', { alpha: false });
  const fps = opts.fps || 15;
  const loops = opts.loops || 3;
  const total = opts.frames * loops;

  const stream = canvas.captureStream(fps);
  const mime = videoMimeType();
  const rec = new MediaRecorder(stream, mime ? { mimeType: mime } : undefined);
  const chunks = [];
  rec.ondataavailable = (e) => { if (e.data && e.data.size) chunks.push(e.data); };

  const stopped = new Promise((resolve) => { rec.onstop = resolve; });
  const teardown = async () => {
    try { if (rec.state !== 'inactive') rec.stop(); } catch { /* already stopped */ }
    await stopped;
    for (const t of stream.getTracks()) t.stop();
  };
  rec.start();

  try {
    for (let i = 0; i < total; i++) {
      throwIfAborted(opts.signal);
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      opts.drawFrame(ctx, i % opts.frames, size);
      if (opts.onProgress) opts.onProgress((i + 1) / total);
      await new Promise((r) => setTimeout(r, 1000 / fps));
    }
  } catch (err) {
    await teardown();
    throw err;
  }

  await teardown();
  return new Blob(chunks, { type: (rec.mimeType || mime || 'video/webm').split(';')[0] });
}
