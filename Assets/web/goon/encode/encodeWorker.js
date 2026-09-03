/* ============================================================================
 * encode/encodeWorker.js — lane B's back half: filmstrip -> H.264 -> MP4.
 *
 * A module Worker. The host (TransferCompressionService) has no decoder for
 * animated GIF/WebP, so it hands the page an `encode-request`; the driver in
 * ui/assetsStore.js fetches the bytes and posts them here, and this file turns
 * them into an mp4 the C# side can magic-check, hash and cache like any other
 * artifact. The whole point of it being a worker is that a 400-frame GIF is
 * seconds of solid CPU and the duel runs in the same renderer.
 *
 * Protocol (driver -> worker):
 *   { kind:'encode', jobId, buf (TRANSFERRED), mime, cfg }
 *     cfg = { maxBox, bitrate, maxFps, prevHeight, prevMs, prevBitrate, wantPrev }
 *   { kind:'cancel', jobId }
 * (worker -> driver):
 *   { kind:'progress', jobId, pct }                       throttled
 *   { kind:'done', jobId, ok:true, art, prv, w, h, durMs } art/prv TRANSFERRED
 *   { kind:'fail',  jobId, reason }                        'unsupported' = try the
 *                                                          main-thread recorder
 *
 * THE CODEC STRING IS BY RESOLUTION, AND THAT IS A TRAP, NOT A PREFERENCE.
 * `avc1.42E01E` is Baseline LEVEL 3.0, whose macroblock budget stops at 720x480:
 * a 1280x720 configure() against it comes back unsupported on hardware that
 * encodes 720p all day. The probe in assetsStore.js already asks both ways for
 * exactly this reason; `codecForSize()` below is the same finding, applied.
 *
 * The muxer is VENDORED INSIDE goon/ (goon/vendor/mp4-muxer/) rather than in the
 * shared Resources/web/vendor/, because the headless harnesses serve `goon/` as
 * the document root — a '/vendor/...' specifier resolves in the app and 404s in
 * the harness. The import below is RELATIVE and must stay that way.
 *
 * NODE-IMPORT-SAFE: no browser global is touched at module scope, and the
 * `onmessage` wiring is guarded, so the selftest can import this file (and every
 * pure helper in it) under node without a Worker in sight.
 * ==========================================================================*/

import { Muxer, ArrayBufferTarget } from '../vendor/mp4-muxer/mp4-muxer.mjs';
import {
  decodeFilmstrip, closeFilmstrip, mimeForKind, fitHeight,
} from './gifDecode.js';

/* ------------------------------------------------------------- the codecs */

/** Baseline profile, level 3.0 — up to 720x480. */
export const CODEC_BASELINE_30 = 'avc1.42E01E';
/** Baseline profile, level 3.1 — up to 1280x720. The 720p answer. */
export const CODEC_BASELINE_31 = 'avc1.42E01F';
/** Main profile, level 3.1. A guard: at maxBox 720 nothing should reach it. */
export const CODEC_MAIN_31 = 'avc1.4D401F';

/** Ceiling for a lane-B artifact — the plan's `min(..., 1.8M)`. */
export const MAX_ART_BITRATE = 1_800_000;
/** Nobody wants a 40 kbps mosaic; a nonsense request floors out here. */
export const MIN_ART_BITRATE = 300_000;
/** The micro-preview never runs above this, whatever the source did. */
export const PREVIEW_MAX_FPS = 15;
/** Keyframe cadence, in seconds. Seek-ability costs bytes; 2 s is the trade. */
export const GOP_SECONDS = 2;

/** Level 3.0's MaxFS, in macroblocks (854x480 lands exactly on it). */
export const LEVEL_30_MAX_MBS = 1620;
/** Level 3.1's MaxFS (1280x720 lands exactly on it). */
export const LEVEL_31_MAX_MBS = 3600;

/** 16x16 macroblocks in a w×h frame — H.264 measures frames in these, not pixels. */
export function macroblocksFor(w, h) {
  const w0 = Math.max(1, Math.floor(Number(w) || 0));
  const h0 = Math.max(1, Math.floor(Number(h) || 0));
  return Math.ceil(w0 / 16) * Math.ceil(h0 / 16);
}

/**
 * The codec string a w×h encode must ask with.
 *
 * Judged on the MACROBLOCK BUDGET, not on "is the height 720", because that is
 * the constraint the level actually encodes — and a portrait GIF at 406x720 is
 * 1,170 macroblocks (comfortably level 3.0) while a 1280x720 landscape one is
 * 3,600 (level 3.1 exactly, and the reason a 3.0 config comes back unsupported).
 */
export function codecForSize(w, h) {
  const mbs = macroblocksFor(w, h);
  if (mbs <= LEVEL_30_MAX_MBS) return CODEC_BASELINE_30;
  if (mbs <= LEVEL_31_MAX_MBS) return CODEC_BASELINE_31;
  return CODEC_MAIN_31;
}

/** Clamp whatever the host asked for into the lane's honest range. */
export function clampArtBitrate(requested) {
  const n = Number(requested);
  const want = isFinite(n) && n > 0 ? n : MAX_ART_BITRATE;
  return Math.round(Math.min(MAX_ART_BITRATE, Math.max(MIN_ART_BITRATE, want)));
}

/** Frames between keyframes at a given fps (never 0 — that means "all delta"). */
export function gopFor(fps) {
  const f = Math.max(1, Number(fps) || 1);
  return Math.max(1, Math.round(f * GOP_SECONDS));
}

/** The encode's frame rate: the source's, capped by the host's ceiling. */
export function chooseFps(srcFps, maxFps) {
  const s = Math.max(1, Number(srcFps) || 1);
  const m = Math.max(1, Number(maxFps) || 30);
  return Math.max(1, Math.min(Math.round(s), Math.round(m)));
}

/**
 * Thin a filmstrip down to a frame-rate ceiling.
 *
 * Pure, and it takes plain `{tsUs, durUs}` records (the real one also carries a
 * bitmap) so the selftest can drive the maths without a decoder. A dropped
 * frame's time is GIVEN TO THE FRAME BEFORE IT — the strip must still run for
 * the source's wall-clock length or the artifact plays fast.
 *
 * @returns {Array<{i:number, tsUs:number, durUs:number}>} indices into `frames`
 */
export function selectByFps(frames, maxFps) {
  const list = Array.isArray(frames) ? frames : [];
  if (list.length === 0) return [];
  const cap = Math.max(1, Number(maxFps) || 30);
  const minUs = 1_000_000 / cap;
  const endUs = Number(list[list.length - 1].tsUs) + Number(list[list.length - 1].durUs);

  const kept = [];
  let lastTs = -Infinity;
  for (let i = 0; i < list.length; i++) {
    const ts = Number(list[i].tsUs) || 0;
    // 0.999 slack: a 100 ms strip against a 10 fps cap must keep EVERY frame,
    // and float division of 1e6/10 does not land exactly on 100000 every time.
    if (i > 0 && ts - lastTs < minUs * 0.999) continue;
    kept.push({ i, tsUs: ts, durUs: 0 });
    lastTs = ts;
  }
  for (let k = 0; k < kept.length; k++) {
    const next = k + 1 < kept.length ? kept[k + 1].tsUs : endUs;
    kept[k].durUs = Math.max(1, Math.round(next - kept[k].tsUs));
  }
  return kept;
}

/**
 * The preview's slice: the first `prevMs` of the strip, at ≤15 fps.
 *
 * The plan's micro-preview starts at 12% of duration for lane C, where seeking is
 * free. Here every frame before the start would have to be decoded anyway, so the
 * preview is the HEAD of the strip: same bytes, no second decode.
 */
export function planPreviewFrames(frames, prevMs, maxFps = PREVIEW_MAX_FPS) {
  const list = Array.isArray(frames) ? frames : [];
  const limitUs = Math.max(1, Math.round((Number(prevMs) || 2000) * 1000));
  const head = [];
  for (const f of list) {
    const ts = Number(f.tsUs) || 0;
    if (ts >= limitUs && head.length > 0) break;
    head.push(f);
  }
  const picked = selectByFps(head, Math.min(PREVIEW_MAX_FPS, Math.max(1, Number(maxFps) || PREVIEW_MAX_FPS)));
  // Trim the LAST kept frame so the preview stops at prevMs instead of running
  // on for whatever the final source frame's delay happened to be.
  if (picked.length > 0) {
    const last = picked[picked.length - 1];
    last.durUs = Math.max(1, Math.min(last.durUs, limitUs - last.tsUs));
  }
  return picked;
}

/** Where a job's percentage sits: decode 0-40, art 40-90, preview 90-100. */
export function stagePct(stage, frac) {
  const f = Math.min(1, Math.max(0, Number(frac) || 0));
  if (stage === 'decode') return Math.round(f * 40);
  if (stage === 'art') return 40 + Math.round(f * 50);
  if (stage === 'prev') return 90 + Math.round(f * 10);
  return 0;
}

/* ---------------------------------------------------------- the encode run */

/** True when this runtime can do the WebCodecs path at all. */
export function hasVideoEncoder() {
  const VE = globalThis.VideoEncoder;
  return typeof VE === 'function' && typeof globalThis.VideoFrame === 'function';
}

/** Ask the runtime, honestly, whether it will take this exact configuration. */
async function configSupported(cfg) {
  const VE = globalThis.VideoEncoder;
  if (!VE || typeof VE.isConfigSupported !== 'function') return true;   // old API: find out by trying
  try {
    const r = await VE.isConfigSupported(cfg);
    return !!(r && r.supported);
  } catch (_e) { return false; }
}

/** Let the encoder drain before we shovel more frames at it. */
async function drain(encoder) {
  if (!encoder || encoder.encodeQueueSize <= 8) return;
  if (typeof encoder.addEventListener === 'function') {
    await new Promise((res) => {
      let done = false;
      const fin = () => { if (!done) { done = true; res(); } };
      try { encoder.addEventListener('dequeue', fin, { once: true }); } catch (_e) { /* older API */ }
      setTimeout(fin, 50);
    });
    return;
  }
  await new Promise((res) => setTimeout(res, 10));
}

/**
 * Encode a selection of a filmstrip into one MP4 ArrayBuffer.
 *
 * @param {Array<{bitmap:ImageBitmap,tsUs:number,durUs:number}>} frames
 * @param {Array<{i:number,tsUs:number,durUs:number}>} plan   from selectByFps
 * @param {object} o  {w, h, bitrate, fps, onProgress, shouldStop}
 */
export async function encodeSelection(frames, plan, o) {
  const { w, h, bitrate, fps } = o;
  if (!plan.length) throw new Error('no-frames');
  if (!hasVideoEncoder()) throw new Error('unsupported');

  const codec = codecForSize(w, h);
  const cfg = {
    codec, width: w, height: h, bitrate,
    framerate: Math.max(1, Math.round(fps)),
    latencyMode: 'quality',
  };
  if (!(await configSupported(cfg))) throw new Error('unsupported');

  const target = new ArrayBufferTarget();
  const muxer = new Muxer({
    target,
    video: { codec: 'avc', width: w, height: h },
    // in-memory: the moov goes in FRONT, so the artifact seeks instantly when a
    // duel drops it into a <video> mid-match.
    fastStart: 'in-memory',
    // The first timestamp is 0 by construction, but a strided strip that started
    // late must not make the muxer throw in the player's face.
    firstTimestampBehavior: 'offset',
  });

  let encodeError = null;
  const VE = globalThis.VideoEncoder;
  const VF = globalThis.VideoFrame;
  const encoder = new VE({
    output: (chunk, meta) => { try { muxer.addVideoChunk(chunk, meta); } catch (e) { encodeError = e; } },
    error: (e) => { encodeError = e; },
  });
  encoder.configure(cfg);

  const gop = gopFor(fps);
  try {
    for (let k = 0; k < plan.length; k++) {
      if (encodeError) throw encodeError;
      if (o.shouldStop && o.shouldStop()) throw new Error('cancelled');
      const p = plan[k];
      const src = frames[p.i];
      if (!src || !src.bitmap) continue;
      const vf = new VF(src.bitmap, { timestamp: p.tsUs, duration: p.durUs });
      try {
        encoder.encode(vf, { keyFrame: k % gop === 0 });
      } finally {
        try { vf.close(); } catch (_e) { /* ignore */ }
      }
      await drain(encoder);
      if (o.onProgress && (k % 8 === 0 || k === plan.length - 1)) {
        try { o.onProgress((k + 1) / plan.length); } catch (_e) { /* a reporter must never fail a job */ }
      }
    }
    await encoder.flush();
    if (encodeError) throw encodeError;
    muxer.finalize();
  } finally {
    try { encoder.close(); } catch (_e) { /* already closed by an error */ }
  }

  const buf = target.buffer;
  if (!buf || buf.byteLength < 32) throw new Error('empty-output');
  return buf;
}

/**
 * The whole job: bytes in, {art, prv, w, h, durMs} out.
 *
 * Exported (rather than buried in the message handler) so the main-thread driver
 * can call it directly if a Worker cannot be constructed at all.
 */
export async function runEncodeJob(buf, mime, cfg, hooks = {}) {
  const onProgress = typeof hooks.onProgress === 'function' ? hooks.onProgress : () => {};
  const shouldStop = typeof hooks.shouldStop === 'function' ? hooks.shouldStop : () => false;
  if (!hasVideoEncoder()) throw new Error('unsupported');

  const strip = await decodeFilmstrip(buf, mime, {
    maxBox: cfg.maxBox,
    onProgress: (f) => onProgress(stagePct('decode', f)),
    shouldStop,
  });

  try {
    if (shouldStop()) throw new Error('cancelled');
    const fps = chooseFps(strip.fps, cfg.maxFps);
    const plan = selectByFps(strip.frames, fps);
    const art = await encodeSelection(strip.frames, plan, {
      w: strip.w, h: strip.h,
      bitrate: clampArtBitrate(cfg.bitrate),
      fps,
      onProgress: (f) => onProgress(stagePct('art', f)),
      shouldStop,
    });

    let prv = null;
    // The SHIPPED GoonCacheBridge takes exactly ONE blob per job (cache-put has
    // no art/prv discriminator and encode-done commits a single file), so the
    // driver leaves `wantPrev` off and this stays dead until the bridge grows a
    // second slot. The code is here, and correct, so that day is a one-line day.
    if (cfg.wantPrev) {
      const fit = fitHeight(strip.w, strip.h, cfg.prevHeight);
      const pplan = planPreviewFrames(strip.frames, cfg.prevMs, PREVIEW_MAX_FPS);
      if (pplan.length > 0) {
        // The preview is a SECOND encode of the SAME bitmaps at a smaller size.
        // VideoFrame does not scale, so the frames are re-fitted through
        // createImageBitmap first; they are closed again immediately after.
        const small = [];
        try {
          for (const p of pplan) {
            const srcF = strip.frames[p.i];
            const bitmap = await createImageBitmap(srcF.bitmap, {
              resizeWidth: fit.w, resizeHeight: fit.h, resizeQuality: 'medium',
            });
            small.push({ bitmap, tsUs: p.tsUs, durUs: p.durUs });
          }
          const splan = small.map((f, i) => ({ i, tsUs: f.tsUs, durUs: f.durUs }));
          prv = await encodeSelection(small, splan, {
            w: fit.w, h: fit.h,
            bitrate: Math.max(80_000, Math.round(Number(cfg.prevBitrate) || 350_000)),
            fps: PREVIEW_MAX_FPS,
            onProgress: (f) => onProgress(stagePct('prev', f)),
            shouldStop,
          });
        } catch (e) {
          // A missing preview is a cosmetic loss; a failed job is not.
          prv = null;
        } finally {
          closeFilmstrip(small);
        }
      }
    }

    onProgress(100);
    return { art, prv, w: strip.w, h: strip.h, durMs: strip.durMs, frames: plan.length, fps };
  } finally {
    closeFilmstrip(strip);
  }
}

/* ----------------------------------------------------------- worker wiring
 * Guarded so `import('./encodeWorker.js')` under node (the selftest sweep) does
 * not go looking for a message port that is not there. In a module worker `self`
 * IS the global scope and `window` does not exist — that pair is the test.
 * ------------------------------------------------------------------------ */

const inWorker = typeof self !== 'undefined'
  && typeof window === 'undefined'
  && typeof self.postMessage === 'function'
  && typeof self.addEventListener === 'function';

/** jobIds the driver cancelled while we were mid-decode. */
const cancelled = new Set();

/** Exported for the selftest: the handler, with `post` injected. */
export async function handleWorkerMessage(msg, post) {
  if (!msg) return;
  if (msg.kind === 'cancel') { if (msg.jobId) cancelled.add(String(msg.jobId)); return; }
  if (msg.kind !== 'encode') return;

  const jobId = String(msg.jobId || '');
  const cfg = msg.cfg || {};
  let lastPct = -1;
  let lastAt = 0;
  const emit = (pct) => {
    const p = Math.min(100, Math.max(0, Math.round(Number(pct) || 0)));
    const now = Date.now();
    // Every 1% OR every 2 s. The floor matters more than the ceiling: the
    // service's PageJobStaleMs is THREE MINUTES and a silent job gets requeued
    // out from under us, so a long encode must keep talking.
    if (p === lastPct && now - lastAt < 2000) return;
    lastPct = p; lastAt = now;
    try { post({ kind: 'progress', jobId, pct: p }); } catch (_e) { /* driver gone */ }
  };

  try {
    const out = await runEncodeJob(msg.buf, mimeForKind(msg.mime || msg.kindHint), cfg, {
      onProgress: emit,
      shouldStop: () => cancelled.has(jobId),
    });
    if (cancelled.has(jobId)) { post({ kind: 'fail', jobId, reason: 'cancelled' }); return; }
    const transfer = [out.art];
    if (out.prv) transfer.push(out.prv);
    post({
      kind: 'done', jobId, ok: true,
      art: out.art, prv: out.prv,
      w: out.w, h: out.h, durMs: out.durMs, frames: out.frames, fps: out.fps,
    }, transfer);
  } catch (e) {
    post({ kind: 'fail', jobId, reason: String((e && e.message) || e || 'encode-failed') });
  } finally {
    cancelled.delete(jobId);
  }
}

if (inWorker) {
  self.addEventListener('message', (e) => {
    handleWorkerMessage(e.data, (m, transfer) => {
      if (transfer && transfer.length) self.postMessage(m, transfer);
      else self.postMessage(m);
    });
  });
}
