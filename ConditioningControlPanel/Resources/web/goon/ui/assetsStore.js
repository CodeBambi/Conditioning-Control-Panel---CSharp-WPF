/* ============================================================================
 * ui/assetsStore.js — the page's view of the HOST's transfer cache.
 *
 * The compression queue lives in C# (TransferCompressionService, App-lifetime);
 * this module is the only thing on the page that talks to it. It owns:
 *   1. the bridge handlers for every cache verb — registered EXACTLY ONCE, from
 *      buildApp(). bridge.on() throws on a duplicate, which is precisely why the
 *      screen may not register anything: a screen mounts many times per session;
 *   2. the item table, assembled from paged `cache-list` frames and kept fresh by
 *      coalesced `cache-progress` frames;
 *   3. the capability probe the host needs before it can plan lane B, and the
 *      `hello` that carries it;
 *   4. a STANDALONE answer. In a plain browser there is no host and therefore no
 *      cache, and the screen must say so immediately — a spinner that can never
 *      resolve is the worst of the three possible outcomes.
 *
 * NOTHING here touches the DOM, and nothing throws at import: the module is
 * imported by the node selftests with a fake bridge injected.
 *
 * LANE B SEAM: the page-side gif/awebp encoder plugs in through
 * `onEncodeRequest(fn)`, which is fed both verbs that drive it
 * (`encode-request`, `cache-put-result`); until something registers, the frames
 * are dropped with ONE log line. The handlers themselves are registered here
 * because bridge.on() is one-shot per type and the encoder must not have to
 * fight this module for them — `createEncodeDriver()` below is a CONSUMER of the
 * seam and never touches bridge.on().
 * ==========================================================================*/

import * as defaultBridge from '../bridge.js';
import { decodeFilmstrip, closeFilmstrip, mimeForKind, fitBox } from '../encode/gifDecode.js';

/* ----------------------------------------------------------------- units */

export const KB = 1024;
export const MB = 1024 * 1024;
export const GB = 1024 * 1024 * 1024;

/** The cap slider's rails — AppSettings.TransferCacheCapBytes clamps the same. */
export const CAP_MIN_BYTES = 1 * GB;
export const CAP_MAX_BYTES = 64 * GB;
export const CAP_DEFAULT_BYTES = 8 * GB;

/** The host's `compress` verb takes at most this many ids per frame. */
export const MAX_COMPRESS_IDS = 500;

/** Above either of these, "compress everything" asks first. */
export const CONFIRM_ETA_MS = 30 * 60 * 1000;
export const CONFIRM_INPUT_BYTES = 20 * GB;

/** Item states the host can report. Anything else is treated as `pending`. */
export const ITEM_STATES = Object.freeze(['pending', 'queued', 'working', 'ready', 'failed', 'exempt']);

/** The segmented filter, in render order. */
export const FILTERS = Object.freeze(['all', 'needs', 'ready', 'failed', 'exempt']);

/** "needs compressing" — the three states that are not a finished answer. */
export const NEEDS_STATES = Object.freeze(['pending', 'queued', 'working']);

/* -------------------------------------------------------- pure helpers
 * Exported because the selftest drives them directly: they are the only logic
 * in the assets tier that can be wrong without the screen looking wrong.
 * ------------------------------------------------------------------------ */

/** Clamp anything to the slider's rails; junk becomes the default. */
export function clampCapBytes(bytes) {
  const n = Number(bytes);
  if (!isFinite(n) || n <= 0) return CAP_DEFAULT_BYTES;
  return Math.min(CAP_MAX_BYTES, Math.max(CAP_MIN_BYTES, Math.round(n)));
}

/** Whole gigabytes for the slider position (always inside 1..64). */
export function capGb(bytes) {
  return Math.min(64, Math.max(1, Math.round(clampCapBytes(bytes) / GB)));
}

/**
 * "3.1 GB" / "820 MB" / "24 KB" / "0 B". One decimal only above a gigabyte,
 * because the stat chip is read at a glance and 3.14159 GB is noise.
 */
export function formatBytes(bytes) {
  const n = Number(bytes);
  if (!isFinite(n) || n <= 0) return '0 B';
  if (n >= GB) return (n / GB).toFixed(1) + ' GB';
  if (n >= MB) return Math.round(n / MB) + ' MB';
  if (n >= KB) return Math.round(n / KB) + ' KB';
  return Math.round(n) + ' B';
}

/** "3.1 / 8.0 GB" — used vs cap, one unit, so the pair compares by eye. */
export function formatUsage(usedBytes, capBytes) {
  const used = Math.max(0, Number(usedBytes) || 0);
  const cap = clampCapBytes(capBytes);
  return (used / GB).toFixed(1) + ' / ' + (cap / GB).toFixed(1) + ' GB';
}

/** Whole minutes for the ETA line: never 0, never fractional. */
export function etaMinutes(ms) {
  const n = Number(ms);
  if (!isFinite(n) || n <= 0) return 0;
  return Math.max(1, Math.ceil(n / 60000));
}

/** Does an item survive the segmented filter + the search box? */
export function matchesFilter(item, filter, query) {
  if (!item) return false;
  const state = normalizeState(item.state);
  const f = String(filter || 'all');
  let pass = true;
  if (f === 'needs') pass = NEEDS_STATES.indexOf(state) >= 0;
  else if (f === 'ready') pass = state === 'ready';
  else if (f === 'failed') pass = state === 'failed';
  else if (f === 'exempt') pass = state === 'exempt';
  if (!pass) return false;
  const q = String(query || '').trim().toLowerCase();
  if (!q) return true;
  return String(item.name || '').toLowerCase().indexOf(q) >= 0;
}

/** Unknown states read as "not compressed yet" — never as "ready". */
export function normalizeState(state) {
  const s = String(state || '').toLowerCase();
  return ITEM_STATES.indexOf(s) >= 0 ? s : 'pending';
}

/** The bytes "compress everything" would have to chew through. */
export function pendingInputBytes(items) {
  let total = 0;
  for (const it of items || []) {
    const s = normalizeState(it && it.state);
    if (NEEDS_STATES.indexOf(s) < 0) continue;
    total += Math.max(0, Number(it.srcBytes) || 0);
  }
  return total;
}

/* ------------------------------------------------------------- the probe */

/**
 * What this runtime can encode, asked politely.
 *
 * EVERY line is guarded: on an older WebView2 `VideoEncoder`, `ImageDecoder` and
 * `MediaRecorder` are all absent, and a probe that throws would take the whole
 * assets screen (and therefore the menu item that reaches it) down with it.
 *
 * The 720p codec string is load-bearing. `avc1.42E01E` is Baseline LEVEL 3.0,
 * whose macroblock budget stops at 720x480 — a 1280x720 config against it comes
 * back `supported:false` even where 720p encoding plainly works. Level 3.1
 * (`42E01F`) is the one to ask with, so we ask BOTH: 640x360 at 3.0 and 1280x720
 * at 3.1, and report `videoEncoder` if either answers yes.
 */
export async function probeEncode() {
  const out = { videoEncoder: false, hw: false, gif: false, awebp: false, recorderMp4: false };

  try {
    const VE = globalThis.VideoEncoder;
    if (VE && typeof VE.isConfigSupported === 'function') {
      const cfgs = [
        { codec: 'avc1.42E01E', width: 640, height: 360, bitrate: 900000, framerate: 30 },
        { codec: 'avc1.42E01F', width: 1280, height: 720, bitrate: 1800000, framerate: 30 },
      ];
      for (const cfg of cfgs) {
        try {
          const r = await VE.isConfigSupported(cfg);
          if (r && r.supported) out.videoEncoder = true;
        } catch (_e) { /* one config refusing is an answer, not a failure */ }
      }
      try {
        const r = await VE.isConfigSupported({
          codec: 'avc1.42E01F', width: 1280, height: 720, bitrate: 1800000, framerate: 30,
          hardwareAcceleration: 'prefer-hardware',
        });
        out.hw = !!(r && r.supported);
      } catch (_e) { /* no hw hint available */ }
    }
  } catch (_e) { /* no WebCodecs at all */ }

  try {
    const ID = globalThis.ImageDecoder;
    if (ID && typeof ID.isTypeSupported === 'function') {
      try { out.gif = !!(await ID.isTypeSupported('image/gif')); } catch (_e) { /* no */ }
      try { out.awebp = !!(await ID.isTypeSupported('image/webp')); } catch (_e) { /* no */ }
    }
  } catch (_e) { /* no ImageDecoder */ }

  try {
    const MR = globalThis.MediaRecorder;
    if (MR && typeof MR.isTypeSupported === 'function') {
      out.recorderMp4 = !!MR.isTypeSupported('video/mp4;codecs=avc1.42E01E');
    }
  } catch (_e) { /* no MediaRecorder */ }

  return out;
}

/* ============================================================================
 * LANE B — the encode driver.
 *
 * The host cannot decode animated GIF/WebP, so it dispatches those assets to the
 * page one at a time as `encode-request`. This driver is the page's answer:
 *
 *   encode-request -> fetch the source (ccp.assets, CORS-clean by mapping)
 *                  -> hand the bytes to encode/encodeWorker.js (transferred)
 *                  -> forward its progress as cache-req {op:'encode-progress'}
 *                  -> b64-chunk the mp4 into cache-put parts
 *                  -> encode-done, and let the host hash/name/commit it.
 *
 * THE SHIPPED HOST CONTRACT TAKES EXACTLY ONE BLOB PER JOB. GoonCacheBridge's
 * `cache-put` is {jobId, seq, b64} with NO art/prv discriminator; the parts are
 * concatenated in order, magic-checked as ISO-BMFF once, and committed by
 * `CompletePageJob(..., previewSource: null)`. So the micro-preview the plan
 * describes has no channel to arrive on and `WANT_PREVIEW` is false — the worker
 * implements it, the wire cannot carry it, and lane-B items simply have no
 * `prevUrl` (the assets grid already treats that as "no preview", because exempt
 * originals do the same). Flip the constant the day the bridge grows a slot.
 *
 * PROGRESS IS NOT COSMETIC. TransferCompressionService.PageJobStaleMs is THREE
 * MINUTES: a job that stops reporting is requeued out from under us and encoded
 * twice. Every `encode-progress` frame resets that clock (it re-stamps
 * DispatchedUtc regardless of the percentage), so the driver keeps a heartbeat
 * running for the whole job, not just while the number moves.
 * ==========================================================================*/

/** GoonCacheBridge.MaxPutB64Chars — base64 CHARACTERS, not bytes. */
export const PUT_MAX_B64_CHARS = 4 * 1024 * 1024;
/** GoonCacheBridge.MaxPutParts. 16 x 3 MB is far past the 24 MiB wire cap. */
export const PUT_MAX_PARTS = 16;
/** Bytes per part: base64 is 4 chars per 3 bytes, and parts must not straddle. */
export const PUT_PART_BYTES = Math.floor(PUT_MAX_B64_CHARS / 4) * 3;
/** No `cache-put-result` in this long = the host stopped listening. */
export const PUT_RESULT_TIMEOUT_MS = 45000;
/** Re-send the last percentage this often, so a silent job is never requeued. */
export const ENCODE_HEARTBEAT_MS = 15000;
/** The realtime MediaRecorder fallback refuses to sit through longer than this. */
export const RECORDER_MAX_MS = 30000;
/** See the header: the shipped bridge has no second slot for the preview. */
export const WANT_PREVIEW = false;

/**
 * Split an artifact into `cache-put` parts.
 *
 * Pure, and the ONLY place the ceilings are applied: a >16-part artifact must
 * fail HERE, before a single frame goes out, because the host refuses part 17
 * and would leave the job hanging until its stale timer fires.
 *
 * @returns {{ok:boolean, reason:string, parts:Array<{seq:number,start:number,end:number}>}}
 */
export function planPutParts(byteLength, o = {}) {
  const per = Math.max(1, Math.floor(Number(o.partBytes) || PUT_PART_BYTES));
  const maxParts = Math.max(1, Math.floor(Number(o.maxParts) || PUT_MAX_PARTS));
  const total = Math.max(0, Math.floor(Number(byteLength) || 0));
  if (total === 0) return { ok: false, reason: 'empty', parts: [] };
  const parts = [];
  for (let start = 0, seq = 0; start < total; start += per, seq++) {
    parts.push({ seq, start, end: Math.min(total, start + per) });
  }
  if (parts.length > maxParts) return { ok: false, reason: 'too-big', parts: [] };
  return { ok: true, reason: '', parts };
}

/** Base64 for a byte range. Uses Buffer under node (the selftest), btoa in the page. */
export function bytesToB64(bytes) {
  const u8 = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes || 0);
  const B = globalThis.Buffer;
  if (B && typeof B.from === 'function') return B.from(u8).toString('base64');
  // 32 KB at a time: String.fromCharCode.apply blows the argument limit on a
  // multi-megabyte artifact, and the failure mode is a RangeError mid-upload.
  let s = '';
  const STEP = 0x8000;
  for (let i = 0; i < u8.length; i += STEP) {
    s += String.fromCharCode.apply(null, u8.subarray(i, Math.min(u8.length, i + STEP)));
  }
  return globalThis.btoa(s);
}

/**
 * The lane-B driver. Register it on a store: `store.onEncodeRequest(driver.handle)`.
 *
 * @param {object} [o]
 * @param {object} [o.bridge]         injectable for the selftest
 * @param {object} [o.logger]
 * @param {object} [o.caps]           the probeEncode() result — decides worker vs recorder
 * @param {function} [o.workerFactory] () => Worker, injectable
 * @param {function} [o.fetchImpl]    injectable fetch
 */
export function createEncodeDriver({ bridge = defaultBridge, logger = null, caps = null,
  workerFactory = null, fetchImpl = null } = {}) {
  const probe = caps || {};
  let worker = null;
  let job = null;
  let beat = null;
  let disposed = false;
  const log = (m) => { try { logger?.info?.('[GG lane-B] ' + m); } catch (_e) { /* ignore */ } };
  const warn = (m) => { try { logger?.warn?.('[GG lane-B] ' + m); } catch (_e) { /* ignore */ } };

  function send(m) { try { bridge.send(m); } catch (_e) { /* host gone */ } }

  function armBeat() {
    if (beat) return;
    try {
      beat = setInterval(() => {
        if (!job || disposed) return;
        send({ type: 'cache-req', op: 'encode-progress', jobId: job.id, pct: job.pct });
      }, ENCODE_HEARTBEAT_MS);
    } catch (_e) { beat = null; }
  }
  function clearBeat() { if (beat) { try { clearInterval(beat); } catch (_e) { /* ignore */ } beat = null; } }

  function progress(j, pct) {
    if (!j || j !== job) return;
    const p = Math.min(100, Math.max(0, Math.round(Number(pct) || 0)));
    if (p === j.pct) return;
    j.pct = p;
    send({ type: 'cache-req', op: 'encode-progress', jobId: j.id, pct: p });
  }

  function clearJob() {
    if (job && job.putTimer) { try { clearTimeout(job.putTimer); } catch (_e) { /* ignore */ } }
    job = null;
    clearBeat();
  }

  /** Give up on a job — the host's ONLY way out of a dispatched lane-B slot. */
  function failJob(j, reason) {
    if (!j || j !== job) return;
    warn('job ' + j.id + ' failed: ' + reason);
    send({ type: 'encode-done', jobId: j.id, ok: false, fail: String(reason || 'encode-failed').slice(0, 64) });
    clearJob();
  }

  /* --------------------------------------------------------- the worker */

  function ensureWorker() {
    if (worker) return worker;
    try {
      if (typeof workerFactory === 'function') worker = workerFactory();
      else if (typeof Worker === 'function') {
        // RELATIVE to this module: resolves to /goon/encode/encodeWorker.js when
        // the app serves Resources/web, and to /encode/encodeWorker.js when a
        // harness serves goon/ as the root. An absolute path would work in
        // exactly one of the two.
        worker = new Worker(new URL('../encode/encodeWorker.js', import.meta.url), { type: 'module' });
      }
    } catch (e) {
      warn('worker construction failed: ' + ((e && e.message) || e));
      worker = null;
    }
    if (worker) {
      try {
        worker.onmessage = (e) => onWorkerMessage(e && e.data);
        worker.onerror = (e) => {
          warn('worker error: ' + ((e && e.message) || e));
          // A worker that threw is not trustworthy for the next job either.
          try { worker.terminate(); } catch (_e2) { /* ignore */ }
          worker = null;
          if (job && job.phase === 'encode') fallbackOrFail(job, 'worker-error');
        };
      } catch (_e) { /* ignore */ }
    }
    return worker;
  }

  function onWorkerMessage(m) {
    if (!m || disposed) return;
    const j = job;
    if (!j || String(m.jobId || '') !== j.id) return;
    if (m.kind === 'progress') { progress(j, m.pct); return; }
    if (m.kind === 'fail') {
      const why = String(m.reason || 'encode-failed');
      if (why === 'unsupported' || why === 'no-image-decoder') { fallbackOrFail(j, why); return; }
      failJob(j, why);
      return;
    }
    if (m.kind === 'done') { deliver(j, m.art, m); return; }
  }

  /* ------------------------------------------------------- the delivery */

  function deliver(j, artBuf, meta) {
    if (!j || j !== job) return;
    let bytes;
    try { bytes = new Uint8Array(artBuf); } catch (_e) { bytes = null; }
    if (!bytes || bytes.length < 32) { failJob(j, 'empty-output'); return; }
    // The host magic-checks this anyway; saying so here turns a mystery
    // "bad-format" three frames later into one honest log line now.
    if (!(bytes[4] === 0x66 && bytes[5] === 0x74 && bytes[6] === 0x79 && bytes[7] === 0x70)) {
      failJob(j, 'not-mp4');
      return;
    }
    const plan = planPutParts(bytes.length);
    if (!plan.ok) { failJob(j, plan.reason); return; }

    j.phase = 'put';
    j.parts = plan.parts.length;
    j.lastSeq = plan.parts.length - 1;
    j.meta = {
      w: Math.max(0, Number(meta && meta.w) || 0),
      h: Math.max(0, Number(meta && meta.h) || 0),
      durMs: Math.max(0, Number(meta && meta.durMs) || 0),
    };
    for (const p of plan.parts) {
      send({ type: 'cache-put', jobId: j.id, seq: p.seq, b64: bytesToB64(bytes.subarray(p.start, p.end)) });
    }
    log('job ' + j.id + ' encoded ' + bytes.length + ' bytes in ' + plan.parts.length + ' part(s)');
    try {
      j.putTimer = setTimeout(() => {
        if (job === j && j.phase === 'put') failJob(j, 'put-timeout');
      }, PUT_RESULT_TIMEOUT_MS);
    } catch (_e) { /* no timers here is not fatal */ }
  }

  /** `cache-put-result` — per part, then once more with seq -1 for the commit. */
  function onPutResult(m) {
    const j = job;
    if (!j || !m || String(m.jobId || '') !== j.id) return;
    const seq = Number(m.seq);
    if (seq === -1) {
      // The commit answer. Either way this job is over for us.
      if (m.ok) log('job ' + j.id + ' committed');
      else warn('job ' + j.id + ' refused at commit: ' + String(m.error || '?'));
      clearJob();
      return;
    }
    if (!m.ok) {
      // A refused part means the host dropped the whole buffer; tell it to close
      // the job rather than leaving it dispatched until the stale timer fires.
      failJob(j, String(m.error || 'put-failed'));
      return;
    }
    if (j.phase === 'put' && seq === j.lastSeq) {
      if (j.putTimer) { try { clearTimeout(j.putTimer); } catch (_e) { /* ignore */ } j.putTimer = null; }
      j.phase = 'commit';
      send({
        type: 'encode-done', jobId: j.id, ok: true, parts: j.parts, ext: 'mp4',
        w: j.meta.w, h: j.meta.h, durMs: j.meta.durMs,
      });
      // The commit reply (seq -1) clears the job; a host that never answers is
      // handled by its own 5-minute put timeout plus our heartbeat stopping.
      try {
        j.putTimer = setTimeout(() => { if (job === j) clearJob(); }, PUT_RESULT_TIMEOUT_MS);
      } catch (_e) { /* ignore */ }
    }
  }

  /* --------------------------------------------------------- the source */

  async function fetchBytes(url) {
    const f = fetchImpl || (typeof fetch === 'function' ? fetch : null);
    if (!f) throw new Error('no-fetch');
    const r = await f(String(url));
    if (!r || r.ok === false) throw new Error('http-' + ((r && r.status) || 0));
    return await r.arrayBuffer();
  }

  /* ------------------------------------------- the main-thread fallback
   * No WebCodecs: draw the filmstrip onto a canvas in REAL TIME and let
   * MediaRecorder produce the mp4. It is slow and it is lossy about timing, but
   * it is the difference between "this machine cannot send GIFs" and "GIFs take
   * a few seconds each". OffscreenCanvas.captureStream is not reliable inside a
   * worker, which is why this leg lives on the main thread.
   * ------------------------------------------------------------------- */

  function recorderSupported() {
    try {
      const MR = globalThis.MediaRecorder;
      return !!(MR && typeof MR.isTypeSupported === 'function'
        && MR.isTypeSupported('video/mp4;codecs=avc1.42E01E')
        && typeof document !== 'undefined');
    } catch (_e) { return false; }
  }

  function fallbackOrFail(j, why) {
    if (!j || j !== job) return;
    if (j.fellBack || !recorderSupported()) { failJob(j, why === 'unsupported' ? 'no-encoder' : why); return; }
    j.fellBack = true;
    log('job ' + j.id + ' falling back to MediaRecorder (' + why + ')');
    runRecorder(j).catch((e) => failJob(j, String((e && e.message) || e || 'recorder-failed')));
  }

  async function runRecorder(j) {
    // The bytes went to the worker by transfer, so they have to come back off
    // the wire. A GIF the host wants compressed is on local disk; this is cheap.
    const buf = await fetchBytes(j.srcUrl);
    if (job !== j) return;
    progress(j, 5);

    const strip = await decodeFilmstrip(buf, j.mime, {
      maxBox: j.cfg.maxBox,
      onProgress: (f) => progress(j, 5 + Math.round(f * 25)),
    });
    if (job !== j) { closeFilmstrip(strip); return; }

    try {
      const fit = fitBox(strip.w, strip.h, j.cfg.maxBox);
      const canvas = document.createElement('canvas');
      canvas.width = fit.w; canvas.height = fit.h;
      const ctx = canvas.getContext('2d');
      if (!ctx || typeof canvas.captureStream !== 'function') throw new Error('no-capture');

      const fps = Math.max(1, Math.min(Number(j.cfg.maxFps) || 30, Math.round(strip.fps)));
      const stream = canvas.captureStream(fps);
      const rec = new globalThis.MediaRecorder(stream, {
        mimeType: 'video/mp4;codecs=avc1.42E01E',
        videoBitsPerSecond: Math.min(1800000, Math.max(300000, Number(j.cfg.bitrate) || 1800000)),
      });
      const chunks = [];
      rec.ondataavailable = (e) => { if (e && e.data && e.data.size) chunks.push(e.data); };
      const stopped = new Promise((res) => { rec.onstop = res; });
      rec.start();

      // Real time, frame by frame, with a hard ceiling: a 4-minute GIF is not
      // worth four minutes of the player's main thread.
      const budgetMs = Math.min(RECORDER_MAX_MS, Math.max(200, strip.durMs));
      let elapsed = 0;
      for (let i = 0; i < strip.frames.length && elapsed < budgetMs; i++) {
        if (job !== j) break;
        const f = strip.frames[i];
        ctx.drawImage(f.bitmap, 0, 0, fit.w, fit.h);
        const holdMs = Math.max(16, Math.round(f.durUs / 1000));
        await new Promise((res) => setTimeout(res, holdMs));
        elapsed += holdMs;
        progress(j, 30 + Math.round((elapsed / budgetMs) * 60));
      }
      try { rec.stop(); } catch (_e) { /* ignore */ }
      await stopped;
      if (job !== j) return;

      const blob = new globalThis.Blob(chunks, { type: 'video/mp4' });
      const out = await blob.arrayBuffer();
      progress(j, 95);
      deliver(j, out, { w: fit.w, h: fit.h, durMs: Math.round(elapsed) });
    } finally {
      closeFilmstrip(strip);
    }
  }

  /* ------------------------------------------------------------ the entry */

  async function start(m) {
    const id = String((m && (m.jobId || m.id)) || '');
    if (!id) return;
    if (job) {
      // The host dispatches serially and re-sends the SAME job until it is
      // answered, so a different id here means it gave up on the old one.
      if (job.id === id) return;
      warn('job ' + job.id + ' abandoned — the host moved on to ' + id);
      clearJob();
    }
    const cfg = {
      maxBox: Math.max(64, Number(m.maxBox) || 720),
      bitrate: Math.max(100000, Number(m.bitrate) || 1800000),
      maxFps: Math.max(1, Number(m.maxFps) || 30),
      prevHeight: Math.max(16, Number(m.prevHeight) || 240),
      prevMs: Math.max(200, Number(m.prevMs) || 2000),
      prevBitrate: Math.max(50000, Number(m.prevBitrate) || 350000),
      wantPrev: WANT_PREVIEW,
    };
    const j = {
      id, cfg, pct: 0, phase: 'fetch', fellBack: false,
      srcUrl: String(m.srcUrl || ''), mime: mimeForKind(m.kind),
      parts: 0, lastSeq: -1, putTimer: null, meta: { w: 0, h: 0, durMs: 0 },
    };
    job = j;
    armBeat();
    log('job ' + id + ' (' + j.mime + ') starting');

    // No WebCodecs at all: skip the worker entirely rather than paying a fetch,
    // a transfer and a round-trip to be told what the probe already knew.
    if (probe.videoEncoder === false && probe.recorderMp4 === true) { fallbackOrFail(j, 'unsupported'); return; }

    let buf = null;
    try { buf = await fetchBytes(j.srcUrl); } catch (e) {
      failJob(j, 'fetch-failed');
      return;
    }
    if (job !== j) return;
    progress(j, 2);

    const w = ensureWorker();
    if (!w) { fallbackOrFail(j, 'no-worker'); return; }
    j.phase = 'encode';
    try {
      w.postMessage({ kind: 'encode', jobId: id, buf, mime: j.mime, cfg }, [buf]);
    } catch (e) {
      warn('postMessage failed: ' + ((e && e.message) || e));
      fallbackOrFail(j, 'no-worker');
    }
  }

  return {
    /** The seam handler. Hand this to `store.onEncodeRequest`. */
    handle(m) {
      if (disposed || !m) return;
      if (m.type === 'encode-request') { start(m).catch((e) => warn('start threw: ' + ((e && e.message) || e))); return; }
      if (m.type === 'cache-put-result') onPutResult(m);
    },
    get jobId() { return job ? job.id : null; },
    get busy() { return !!job; },
    dispose() {
      if (disposed) return;
      disposed = true;
      if (job) { try { worker?.postMessage({ kind: 'cancel', jobId: job.id }); } catch (_e) { /* ignore */ } }
      clearJob();
      if (worker) { try { worker.terminate(); } catch (_e) { /* ignore */ } worker = null; }
    },
  };
}

/* ------------------------------------------------------------- the store */

function blankState() {
  return {
    /** page-side: false = there is no host cache to talk to (standalone / caps off) */
    available: true,
    /** page-side: a cache-state has landed at least once */
    loaded: false,
    capBytes: CAP_DEFAULT_BYTES,
    usedBytes: 0,
    overCap: false,
    ready: 0,
    pending: 0,
    queued: 0,
    working: 0,
    failed: 0,
    exempt: 0,
    total: 0,
    paused: false,
    pausedBy: '',
    etaMs: 0,
    throughputBps: 0,
    hw: false,
    presetId: '',
    presetName: '',
    presetChanged: false,
    lanes: { video: 0, still: 0, page: 0 },
  };
}

function normalizeItem(raw) {
  const o = raw || {};
  return {
    id: String(o.id || ''),
    name: String(o.name || ''),
    rel: String(o.rel || ''),
    kind: String(o.kind || ''),
    state: normalizeState(o.state),
    pct: Math.min(100, Math.max(0, Number(o.pct) || 0)),
    srcUrl: typeof o.srcUrl === 'string' ? o.srcUrl : '',
    artUrl: typeof o.artUrl === 'string' ? o.artUrl : '',
    prevUrl: typeof o.prevUrl === 'string' ? o.prevUrl : '',
    bytes: Math.max(0, Number(o.bytes) || 0),
    srcBytes: Math.max(0, Number(o.srcBytes) || 0),
    w: Math.max(0, Number(o.w) || 0),
    h: Math.max(0, Number(o.h) || 0),
    durMs: Math.max(0, Number(o.durMs) || 0),
    fail: String(o.fail || ''),
  };
}

/**
 * @param {object} [o]
 * @param {object} [o.bridge]  injectable for the node selftest (defaults to the real one)
 * @param {object} [o.session] boot's session — read for `hosted` and `caps.assetCache`
 * @param {object} [o.logger]
 * @param {boolean} [o.autoHello] send the hello on construction (default true)
 * @param {boolean} [o.autoEncoder] build the lane-B driver when the probe says
 *   this runtime can encode (default true). Off, the seam stays empty and the
 *   host's encode-requests are dropped with one log line, exactly as before.
 */
export function createAssetsStore({ bridge = defaultBridge, session = null, logger = null,
  autoHello = true, autoEncoder = true } = {}) {
  const state = blankState();
  /** id -> item */
  const items = new Map();
  let itemsArr = [];
  let listLoaded = false;
  let listRequested = false;

  const stateSubs = new Set();
  const itemSubs = new Set();

  /* list-frame assembly */
  let buf = null;         // Map being assembled
  let expectSeq = 0;
  let desyncs = 0;

  let encodeHandler = null;
  let warnedEncode = false;
  let disposed = false;
  /** The lane-B driver, built only if the probe says this runtime can encode. */
  let encoder = null;

  const warn = (m) => { try { logger?.warn?.('[GG assets] ' + m); } catch (_e) { /* ignore */ } };
  const info = (m) => { try { logger?.info?.('[GG assets] ' + m); } catch (_e) { /* ignore */ } };

  /* --------------------------------------------------------- emitters */

  function emitState() {
    for (const fn of Array.from(stateSubs)) { try { fn(state); } catch (e) { warn('state subscriber threw: ' + ((e && e.message) || e)); } }
  }
  function emitItems() {
    for (const fn of Array.from(itemSubs)) { try { fn(itemsArr); } catch (e) { warn('items subscriber threw: ' + ((e && e.message) || e)); } }
  }
  function rebuildArr() { itemsArr = Array.from(items.values()); }

  /* ---------------------------------------------------- frame handlers */

  function onCacheState(m) {
    if (disposed || !m) return;
    const lanes = m.lanes || {};
    state.available = true;
    state.loaded = true;
    state.capBytes = clampCapBytes(m.capBytes);
    state.usedBytes = Math.max(0, Number(m.usedBytes) || 0);
    state.overCap = !!m.overCap;
    state.ready = Math.max(0, Number(m.ready) || 0);
    state.pending = Math.max(0, Number(m.pending) || 0);
    state.queued = Math.max(0, Number(m.queued) || 0);
    state.working = Math.max(0, Number(m.working) || 0);
    state.failed = Math.max(0, Number(m.failed) || 0);
    state.exempt = Math.max(0, Number(m.exempt) || 0);
    state.total = Math.max(0, Number(m.total) || 0);
    state.paused = !!m.paused;
    state.pausedBy = String(m.pausedBy || '');
    state.etaMs = Math.max(0, Number(m.etaMs) || 0);
    state.throughputBps = Math.max(0, Number(m.throughputBps) || 0);
    state.hw = !!m.hw;
    state.presetId = String(m.presetId || '');
    state.presetName = String(m.presetName || '');
    state.presetChanged = !!m.presetChanged;
    state.lanes = {
      video: Math.max(0, Number(lanes.video) || 0),
      still: Math.max(0, Number(lanes.still) || 0),
      page: Math.max(0, Number(lanes.page) || 0),
    };
    emitState();
  }

  /**
   * A `cache-list` is paged: {seq, last, items[≤500]}. seq 0 always STARTS a new
   * assembly (the host re-lists on every `list` op), and only `last` commits it,
   * so a half-delivered list can never replace a good one on screen.
   */
  function onCacheList(m) {
    if (disposed || !m) return;
    const seq = Number(m.seq) || 0;
    if (seq === 0) { buf = new Map(); expectSeq = 0; }
    if (!buf) { desyncs++; warn('cache-list seq ' + seq + ' with no open assembly — ignored'); return; }
    if (seq !== expectSeq) {
      desyncs++;
      warn('cache-list out of order (got ' + seq + ', wanted ' + expectSeq + ') — frame dropped');
      return;
    }
    expectSeq = seq + 1;
    for (const raw of (m.items || [])) {
      const it = normalizeItem(raw);
      if (it.id) buf.set(it.id, it);
    }
    if (!m.last) return;

    items.clear();
    for (const [id, it] of buf) items.set(id, it);
    buf = null;
    expectSeq = 0;
    listLoaded = true;
    rebuildArr();
    emitItems();
  }

  /** Coalesced 4 Hz deltas. Only the fields present move; unknown ids are dropped. */
  function onCacheProgress(m) {
    if (disposed || !m) return;
    let touched = 0;
    for (const raw of (m.items || [])) {
      const id = String((raw && raw.id) || '');
      if (!id) continue;
      const cur = items.get(id);
      if (!cur) continue;                        // a list we have not assembled yet
      if (raw.state !== undefined) cur.state = normalizeState(raw.state);
      if (raw.pct !== undefined) cur.pct = Math.min(100, Math.max(0, Number(raw.pct) || 0));
      if (typeof raw.artUrl === 'string') cur.artUrl = raw.artUrl;
      if (typeof raw.prevUrl === 'string') cur.prevUrl = raw.prevUrl;
      if (raw.bytes !== undefined) cur.bytes = Math.max(0, Number(raw.bytes) || 0);
      if (typeof raw.fail === 'string') cur.fail = raw.fail;
      touched++;
    }
    if (touched) emitItems();
  }

  /** Lane B's two verbs, parked until an encoder driver claims them. */
  function onEncodeFrame(m) {
    if (disposed) return;
    if (typeof encodeHandler === 'function') {
      try { encodeHandler(m); } catch (e) { warn('encode handler threw: ' + ((e && e.message) || e)); }
      return;
    }
    if (!warnedEncode) {
      warnedEncode = true;
      warn('host sent "' + ((m && m.type) || '?') + '" but no page encoder is registered — lane B is not built yet; dropping (this logs once)');
    }
  }

  /* ------------------------------------------------- handler registration
   * ONE registration each, at construction, and nowhere else on the page.
   * bridge.on() throws on a duplicate — that alarm is the whole point.
   * ------------------------------------------------------------------- */
  const OWNED_TYPES = ['cache-state', 'cache-list', 'cache-progress', 'encode-request', 'cache-put-result'];
  try {
    bridge.on('cache-state', onCacheState);
    bridge.on('cache-list', onCacheList);
    bridge.on('cache-progress', onCacheProgress);
    bridge.on('encode-request', onEncodeFrame);
    bridge.on('cache-put-result', onEncodeFrame);
  } catch (e) {
    // A duplicate registration means someone else already owns a cache verb.
    // Say so loudly and keep the page alive: the screen degrades to "unavailable".
    warn('bridge handler registration failed: ' + ((e && e.message) || e));
  }

  /* -------------------------------------------------------- the requests */

  function req(op, extra) {
    if (!state.available) return false;
    try { bridge.send(Object.assign({ type: 'cache-req', op }, extra || {})); } catch (_e) { return false; }
    return true;
  }

  /** Ask for the full list. Idempotent-ish: repeated calls just re-list. */
  function requestList() { listRequested = true; return req('list'); }

  /** Compress the whole active pool (the host recomputes what that means). */
  function compressAll() { return req('compress-all'); }

  /** Compress specific ids — chunked to the host's 500-per-frame ceiling. */
  function compress(ids) {
    const list = (ids || []).map(String).filter(Boolean);
    if (!list.length) return false;
    let sent = false;
    for (let i = 0; i < list.length; i += MAX_COMPRESS_IDS) {
      sent = req('compress', { ids: list.slice(i, i + MAX_COMPRESS_IDS) }) || sent;
    }
    return sent;
  }

  function cancel(ids) {
    const list = (ids || []).map(String).filter(Boolean);
    if (!list.length) return false;
    return req('cancel', { ids: list.slice(0, MAX_COMPRESS_IDS) });
  }

  /**
   * `reason` is NOT decoration: the host keeps the user pause and the match
   * pause as separate flags, so a match ending can never resume a queue the
   * player parked by hand.
   */
  function pause(reason) { return req('pause', { reason: reason === 'match' ? 'match' : 'user' }); }
  function resume(reason) { return req('resume', { reason: reason === 'match' ? 'match' : 'user' }); }

  function deleteOne(ids) {
    const list = (ids || []).map(String).filter(Boolean);
    if (!list.length) return false;
    return req('delete', { ids: list.slice(0, MAX_COMPRESS_IDS) });
  }

  function deleteAll() { return req('delete-all'); }

  function setCap(capBytes) { return req('set-cap', { capBytes: clampCapBytes(capBytes) }); }

  /* --------------------------------------------------------- lifecycle */

  /**
   * The standalone answer, delivered on a MICROTASK rather than never: the
   * screen subscribes during mount and must be able to paint "compression lives
   * in the app" on its first frame. A spinner here would be unresolvable.
   */
  function markUnavailable(why) {
    state.available = false;
    state.loaded = true;
    listLoaded = true;
    rebuildArr();
    info('cache unavailable (' + why + ')');
    emitState();
    emitItems();
  }

  const hostedCache = !!bridge.isHosted && (!session || !session.caps || session.caps.assetCache !== false);

  if (!hostedCache) {
    Promise.resolve().then(() => { if (!disposed) markUnavailable(bridge.isHosted ? 'host says caps.assetCache=false' : 'standalone'); });
  } else if (autoHello) {
    // Probe first, THEN say hello: the caps block is what lets the host plan
    // lane B (or decide it has to do everything itself).
    Promise.resolve().then(async () => {
      let caps = { videoEncoder: false, hw: false, gif: false, awebp: false, recorderMp4: false };
      try { caps = await probeEncode(); } catch (_e) { /* the defaults are the honest answer */ }
      if (disposed) return;
      info('probe: encoder=' + caps.videoEncoder + ' hw=' + caps.hw + ' gif=' + caps.gif
        + ' awebp=' + caps.awebp + ' mp4rec=' + caps.recorderMp4);
      // Claim the seam BEFORE the hello: the host starts dispatching lane-B jobs
      // the moment it hears `_pageCanEncode`, and a frame that arrives with no
      // driver registered is DROPPED (there is no re-delivery — the job just
      // sits dispatched until its 3-minute stale timer requeues it).
      if (autoEncoder && (caps.videoEncoder || caps.recorderMp4)) {
        try {
          encoder = createEncodeDriver({ bridge, logger, caps });
          api.onEncodeRequest(encoder.handle);
        } catch (e) { warn('lane-B driver unavailable: ' + ((e && e.message) || e)); encoder = null; }
      }
      req('hello', { caps });
    });
  }

  const api = {
    /** Live state object — read it, never mutate it. */
    get state() { return state; },
    get items() { return itemsArr; },
    get isAvailable() { return state.available; },
    get isLoaded() { return state.loaded; },
    get isListLoaded() { return listLoaded; },
    get listRequested() { return listRequested; },
    get desyncs() { return desyncs; },
    get ownedTypes() { return OWNED_TYPES.slice(); },

    /** Subscribe. Fires immediately with the current value, then on every change. */
    onState(fn) {
      if (typeof fn !== 'function') return () => {};
      stateSubs.add(fn);
      try { fn(state); } catch (e) { warn('state subscriber threw: ' + ((e && e.message) || e)); }
      return () => stateSubs.delete(fn);
    },
    onItems(fn) {
      if (typeof fn !== 'function') return () => {};
      itemSubs.add(fn);
      if (listLoaded) { try { fn(itemsArr); } catch (e) { warn('items subscriber threw: ' + ((e && e.message) || e)); } }
      return () => itemSubs.delete(fn);
    },

    /** LANE B SEAM — see the header. Returns an unregister function. */
    onEncodeRequest(fn) {
      encodeHandler = (typeof fn === 'function') ? fn : null;
      return () => { if (encodeHandler === fn) encodeHandler = null; };
    },

    /** The lane-B driver, once the probe has answered. null = this runtime cannot encode. */
    get encoder() { return encoder; },

    item(id) { return items.get(String(id)) || null; },

    requestList, compressAll, compress, cancel, pause, resume, deleteOne, deleteAll, setCap,

    /** Test seam: feed a frame as if the host had sent it. */
    _inject(m) {
      if (!m) return;
      if (m.type === 'cache-state') onCacheState(m);
      else if (m.type === 'cache-list') onCacheList(m);
      else if (m.type === 'cache-progress') onCacheProgress(m);
      else if (m.type === 'encode-request' || m.type === 'cache-put-result') onEncodeFrame(m);
    },

    dispose() {
      if (disposed) return;
      disposed = true;
      stateSubs.clear();
      itemSubs.clear();
      encodeHandler = null;
      if (encoder) { try { encoder.dispose(); } catch (_e) { /* ignore */ } encoder = null; }
      for (const t of OWNED_TYPES) { try { bridge.off(t); } catch (_e) { /* ignore */ } }
    },
  };
  return api;
}

export default createAssetsStore;
