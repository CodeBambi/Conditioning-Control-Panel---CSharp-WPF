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
 *      resolve is the worst of the three possible outcomes;
 *   5. the STANDALONE LIBRARY itself — files the player picks (or a zip of
 *      them), adopted here and sent straight from this page. Anything over the
 *      wire's exempt cap is COMPRESSED IN THE PAGE (ui/localCompress.js) into an
 *      artifact that fits, exactly as the C# app would have; see the adoption
 *      policy on adoptLocalFile.
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
import { ACCEPT_MIME, MAX_ARTIFACT_BYTES, MAX_EXEMPT_BYTES } from '../net/mediaChannel.js';
import { isZipFile, readZipMedia, ZIP_MAX_ENTRIES, ZIP_MAX_TOTAL_BYTES } from './zipReader.js';
import { createLocalCompressor, extForMime, sniffAnimated, MAX_DECODE_BYTES } from './localCompress.js';

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

/* ------------------------------------------------------- local (browser) files
 * Standalone has no host library, so the page adopts what the player picks. The
 * caps below are the wire's own (imported from mediaChannel, so they can never
 * drift from what the receiver's offer gate will actually accept) and there are
 * TWO of them, which is the whole shape of this tier:
 *
 *   ≤ MAX_EXEMPT_BYTES (8 MB)    sent AS-IS, on the exempt path. Never
 *                                recompressed: it already fits, and re-encoding
 *                                something that fits only loses quality.
 *   ≤ MAX_ARTIFACT_BYTES (64 MB) the ceiling for a NON-exempt artifact — what
 *                                the transfer queue allows once an item is a
 *                                compressed product rather than an original.
 *
 * Everything between those two, and everything above the second, used to be
 * "too big" and was skipped. That was wrong: a 1 GB zip of photos is the USE
 * CASE, and the page can put a 12 MB photo through a canvas the same way the C#
 * app puts it through its still lane. See ui/localCompress.js.
 * ------------------------------------------------------------------------- */

export const LOCAL_MAX_BYTES = MAX_EXEMPT_BYTES;

/** The wire ceiling for a COMPRESSED (non-exempt) artifact. */
export const LOCAL_ARTIFACT_MAX_BYTES = MAX_ARTIFACT_BYTES;

/**
 * The biggest SOURCE we will decode. A decode costs width*height*4 bytes of
 * RGBA no matter what the file weighs, so this is a phone-memory guard, not a
 * wire limit — past it a still is counted `tooBig` rather than attempted.
 */
export const LOCAL_DECODE_MAX_BYTES = MAX_DECODE_BYTES;

/**
 * A .zip is a pick too: phones cannot hand over a folder, so an archive is the
 * only way a player on a phone gives us a library. It is expanded INLINE by
 * ui/zipReader.js and every eligible entry walks the same adoption road as a
 * hand-picked file — same mime gate, same per-file cap, same sha, same dedup.
 *
 * THIS IS NOT AN ARCHIVE-SIZE LIMIT. There is none: zipReader reads a zip by
 * Blob range and never holds more than one entry, so a 1 GB library costs a
 * directory plus one photo. This is the ceiling on the INFLATED total taken out
 * of one archive — the zip-bomb guard, checked against declared sizes.
 */
export const LOCAL_ZIP_MAX_INFLATED_BYTES = ZIP_MAX_TOTAL_BYTES;

/** Entries taken from one archive before the rest is `trimmed` (and said so). */
export const LOCAL_ZIP_MAX_ENTRIES = ZIP_MAX_ENTRIES;

/** Phones love to hand over files with an empty `type`; the extension decides. */
export const LOCAL_MIME_BY_EXT = Object.freeze({
  png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', gif: 'image/gif',
  webp: 'image/webp', mp4: 'video/mp4', m4v: 'video/mp4', webm: 'video/webm',
  mov: 'video/quicktime',
});

/**
 * Which road a file of this mime and this size takes, judged on SIZE ALONE.
 *
 * Exported and pure because it is applied in TWO places that must not disagree:
 * adoptLocalFile (on the real bytes) and the zip reader's directory pass (on the
 * declared size, before anything is inflated).
 *
 * @returns {'take'|'too-big'|'too-big-video'} 'take' means "the store has a road
 *   for this" — exempt, as-is artifact, or compressed artifact; which one is
 *   adoptLocalFile's business.
 */
export function classifyLocalSize(mime, bytes) {
  const size = Math.max(0, Number(bytes) || 0);
  if (!size) return 'too-big';
  if (size <= LOCAL_MAX_BYTES) return 'take';
  if (String(mime || '').indexOf('video/') === 0) {
    return size <= LOCAL_ARTIFACT_MAX_BYTES ? 'take' : 'too-big-video';
  }
  return size <= LOCAL_DECODE_MAX_BYTES ? 'take' : 'too-big';
}

/**
 * The local library as exec/media.js DECK ENTRIES — `{kind, name, url}`.
 *
 * Standalone this is the player's whole library, and it is the ONLY thing the
 * media pool can draw from: bridge.js synthesizes an empty `manifest` frame out
 * here, so without this the practice session fires every effect against an empty
 * deck (which is exactly what it did). Pure, exported and driven by the selftest
 * because it is the join between two tiers that must not learn about each other.
 *
 *   - `kind` is TAKEN, not re-derived. A pick lives behind a blob: URL with no
 *     extension to sniff; the store decided image/video from the mime at
 *     adoption time (kindForMime) and that answer is the right one. The mime is
 *     only a fallback for a row that somehow carries none.
 *   - the URL follows the same rule the send path uses: an EXEMPT original IS
 *     the file (srcUrl), a compressed pick's bytes are the artifact (artUrl).
 *   - a row with no URL at all (node, where there is no createObjectURL) is
 *     skipped rather than pushed as an entry that can only fail to load.
 */
export function localPlayableEntries(items) {
  const out = [];
  for (const it of items || []) {
    if (!it) continue;
    const url = normalizeState(it.state) === 'exempt'
      ? (it.srcUrl || it.artUrl || '')
      : (it.artUrl || it.srcUrl || '');
    if (!url) continue;
    let kind = it.kind === 'video' ? 'video' : (it.kind === 'image' ? 'image' : '');
    if (!kind && it.mime) kind = kindOfMime(it.mime);
    if (!kind) continue;
    out.push({ kind, name: String(it.name || ''), url: String(url) });
  }
  return out;
}

/** The mime family the wire insists a `kind` agrees with (mediaChannel familyOf). */
function kindOfMime(mime) { return String(mime || '').indexOf('video/') === 0 ? 'video' : 'image'; }

/** The wire mime for a picked file, or '' when the wire would refuse it. */
export function localMimeOf(file) {
  const typed = String((file && file.type) || '').toLowerCase();
  if (ACCEPT_MIME.has(typed)) return typed;
  const m = /\.([a-z0-9]{2,5})$/i.exec(String((file && file.name) || ''));
  const byExt = m ? (LOCAL_MIME_BY_EXT[m[1].toLowerCase()] || '') : '';
  return ACCEPT_MIME.has(byExt) ? byExt : '';
}

/** Metadata that has not arrived by now is a busy device, not a verdict. */
export const VIDEO_PROBE_TIMEOUT_MS = 4000;

/**
 * Can THIS device actually decode this video? A real probe — load the blob into
 * an off-DOM <video> and wait for metadata — because with `video/quicktime` on
 * the accept list a string compare stops being an answer: the container says
 * nothing about whether the codec inside (HEVC, mostly, on an iPhone) has a
 * decoder here. `false` only on the element's own error/zero-size verdict;
 * anything ambiguous (no DOM under node, createObjectURL refused, the timeout)
 * answers `true`, because a wrongly-refused good clip is this feature's
 * founding bug and a wrongly-adopted bad one just plays black for its slot.
 *
 * KNOWN GAP, on purpose: the probe is LOCAL. Safari happily decodes its own
 * HEVC and will still ship it to a Windows peer that may not — a
 * peer-capability handshake is the real fix and is out of scope here.
 */
export function probeVideoDecodable(blob) {
  if (typeof document === 'undefined' || typeof URL === 'undefined'
    || typeof URL.createObjectURL !== 'function') return Promise.resolve(true);
  let url = '';
  try { url = URL.createObjectURL(blob); } catch (_e) { return Promise.resolve(true); }
  return new Promise((resolve) => {
    let v = null;
    let timer = 0;
    let done = false;
    const finish = (ok) => {
      if (done) return;
      done = true;
      try { clearTimeout(timer); } catch (_e) { /* ignore */ }
      // Detach before revoking, or Safari keeps the decode session alive.
      try { if (v) { v.removeAttribute('src'); v.load(); } } catch (_e) { /* ignore */ }
      try { URL.revokeObjectURL(url); } catch (_e) { /* ignore */ }
      v = null;
      resolve(ok);
    };
    timer = setTimeout(() => finish(true), VIDEO_PROBE_TIMEOUT_MS);
    try {
      v = document.createElement('video');
      v.preload = 'metadata';
      v.muted = true;
      v.onloadedmetadata = () => finish((v.videoWidth | 0) > 0);
      v.onerror = () => finish(false);
      v.src = url;
    } catch (_e) { finish(true); }
  });
}

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
 * THE HOST TAKES TWO PART STREAMS PER JOB: `cache-put {jobId, part:'art'|'prv',
 * seq, b64}` — 'art' is the artifact (mandatory, ≤16 parts), 'prv' the
 * micro-preview (optional, ≤4 parts / 2 MB, encoded from the same filmstrip).
 * Each stream has its own gapless seq from 0; `part` absent means 'art'.
 * The PREVIEW IS COSMETIC END TO END: the bridge drops a refused/oversize
 * preview stream without touching the artifact, so a prv refusal here is
 * counted as settled, never failed. `encode-done` claims both counts
 * ({parts, prvParts}); the host validates art strictly, preview leniently.
 *
 * PROGRESS IS NOT COSMETIC. TransferCompressionService.PageJobStaleMs is THREE
 * MINUTES: a job that stops reporting is requeued out from under us and encoded
 * twice. Every `encode-progress` frame resets that clock (it re-stamps
 * DispatchedUtc regardless of the percentage), so the driver keeps a heartbeat
 * running for the whole job, not just while the number moves.
 * ==========================================================================*/

/** GoonCacheBridge.MaxPutB64Chars — base64 CHARACTERS, not bytes. */
export const PUT_MAX_B64_CHARS = 4 * 1024 * 1024;
/** GoonCacheBridge.MaxPutParts. 16 x 3 MB = 48 MB — plenty for lane B's ~2 MB mp4s (raw sends never ride this put path). */
export const PUT_MAX_PARTS = 16;
/** Bytes per part: base64 is 4 chars per 3 bytes, and parts must not straddle. */
export const PUT_PART_BYTES = Math.floor(PUT_MAX_B64_CHARS / 4) * 3;
/** No `cache-put-result` in this long = the host stopped listening. */
export const PUT_RESULT_TIMEOUT_MS = 45000;
/** Re-send the last percentage this often, so a silent job is never requeued. */
export const ENCODE_HEARTBEAT_MS = 15000;
/** The realtime MediaRecorder fallback refuses to sit through longer than this. */
export const RECORDER_MAX_MS = 30000;
/** GoonCacheBridge.MaxPrvParts / MaxPrvBytes — the preview stream's own ceilings. */
export const PRV_MAX_PARTS = 4;
export const PRV_MAX_BYTES = 2 * 1024 * 1024;
/** The bridge carries the preview on the 'prv' part stream — see the header. */
export const WANT_PREVIEW = true;

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

    // The preview rides its own 'prv' stream, and it is COSMETIC: any reason it
    // can't go (absent, too big, not mp4) means "no preview", never a failed job.
    let prv = null;
    let prvPlan = { ok: false, parts: [] };
    try {
      const pb = meta && meta.prv ? new Uint8Array(meta.prv) : null;
      if (pb && pb.length >= 32 && pb.length <= PRV_MAX_BYTES
          && pb[4] === 0x66 && pb[5] === 0x74 && pb[6] === 0x79 && pb[7] === 0x70) {
        const pp = planPutParts(pb.length, { maxParts: PRV_MAX_PARTS });
        if (pp.ok) { prv = pb; prvPlan = pp; }
      }
    } catch (_e) { prv = null; }

    j.phase = 'put';
    j.parts = plan.parts.length;
    j.prvParts = prv ? prvPlan.parts.length : 0;
    j.ackedArt = 0;
    j.ackedPrv = 0;
    j.prvSettled = !prv;
    j.meta = {
      w: Math.max(0, Number(meta && meta.w) || 0),
      h: Math.max(0, Number(meta && meta.h) || 0),
      durMs: Math.max(0, Number(meta && meta.durMs) || 0),
    };
    for (const p of plan.parts) {
      send({ type: 'cache-put', jobId: j.id, part: 'art', seq: p.seq, b64: bytesToB64(bytes.subarray(p.start, p.end)) });
    }
    if (prv) {
      for (const p of prvPlan.parts) {
        send({ type: 'cache-put', jobId: j.id, part: 'prv', seq: p.seq, b64: bytesToB64(prv.subarray(p.start, p.end)) });
      }
    }
    log('job ' + j.id + ' encoded ' + bytes.length + ' bytes in ' + plan.parts.length + ' part(s)'
      + (prv ? ' + preview ' + prv.length + ' bytes in ' + prvPlan.parts.length : ''));
    try {
      j.putTimer = setTimeout(() => {
        if (job === j && j.phase === 'put') failJob(j, 'put-timeout');
      }, PUT_RESULT_TIMEOUT_MS);
    } catch (_e) { /* no timers here is not fatal */ }
  }

  /** All art parts acked and the preview stream settled (acked or refused)? Commit. */
  function maybeCommit(j) {
    if (j.phase !== 'put') return;
    if (j.ackedArt !== j.parts || !j.prvSettled) return;
    if (j.putTimer) { try { clearTimeout(j.putTimer); } catch (_e) { /* ignore */ } j.putTimer = null; }
    j.phase = 'commit';
    send({
      type: 'encode-done', jobId: j.id, ok: true, parts: j.parts, prvParts: j.prvParts, ext: 'mp4',
      w: j.meta.w, h: j.meta.h, durMs: j.meta.durMs,
    });
    // The commit reply (seq -1) clears the job; a host that never answers is
    // handled by its own 5-minute put timeout plus our heartbeat stopping.
    try {
      j.putTimer = setTimeout(() => { if (job === j) clearJob(); }, PUT_RESULT_TIMEOUT_MS);
    } catch (_e) { /* ignore */ }
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
    const part = m.part === 'prv' ? 'prv' : 'art';
    if (!m.ok) {
      if (part === 'prv') {
        // The bridge drops ONLY the preview stream on a prv refusal — the
        // artifact upload is untouched, so settle the preview and move on.
        warn('job ' + j.id + ' preview refused (' + String(m.error || '?') + ') — continuing without');
        j.prvParts = 0;
        j.prvSettled = true;
        maybeCommit(j);
        return;
      }
      // A refused ART part means the host dropped the whole buffer; tell it to
      // close the job rather than leaving it dispatched until the stale timer fires.
      failJob(j, String(m.error || 'put-failed'));
      return;
    }
    if (j.phase !== 'put') return;
    if (part === 'prv') {
      j.ackedPrv += 1;
      if (j.ackedPrv >= j.prvParts) j.prvSettled = true;
    } else {
      j.ackedArt += 1;
    }
    maybeCommit(j);
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
      parts: 0, prvParts: 0, ackedArt: 0, ackedPrv: 0, prvSettled: true,
      putTimer: null, meta: { w: 0, h: 0, durMs: 0 },
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
    // The transfer queue's wire identity — exempt originals have no artUrl to
    // recover it from, so dropping these makes them silently un-sendable.
    sha: typeof o.sha === 'string' ? o.sha : '',
    ext: typeof o.ext === 'string' ? o.ext : '',
    // Local (browser-picked) files live behind blob: URLs, which carry no
    // extension for boot's mimeOf() to sniff — the mime must ride the item.
    mime: typeof o.mime === 'string' ? o.mime : '',
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
 * @param {{compressImage:Function, compressGif:Function}} [o.compressor] the
 *   standalone compression lanes. Injectable so the node selftest can drive the
 *   ADOPTION POLICY (which lane, which counter, which sha) with a stub — there
 *   is no canvas, no createImageBitmap and no WebCodecs under node, so the real
 *   one is only ever exercised in a browser. Default: ui/localCompress.js,
 *   built lazily on the first file that actually needs it.
 */
export function createAssetsStore({ bridge = defaultBridge, session = null, logger = null,
  autoHello = true, autoEncoder = true, compressor: injectedCompressor = null } = {}) {
  const state = blankState();
  /** id -> item */
  const items = new Map();
  /** id -> item for files the player picked IN THE BROWSER. A separate map on
   * purpose: every hosted `cache-list` commit REPLACES `items` wholesale, and
   * local picks must survive that. Merged into `itemsArr` by rebuildArr(). */
  const localItems = new Map();
  /** Bumped on every MEMBERSHIP change of `localItems`. boot.js re-feeds the
   * media deck off this, so a hosted `cache-list` (which fires the same
   * `onItems` subscribers) cannot make it re-deal the pool for nothing. */
  let localVersion = 0;
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
  function rebuildArr() { itemsArr = Array.from(items.values()).concat(Array.from(localItems.values())); }

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

  /* ---------------------------------------------------- local (browser) files */

  const hex = (buf) => Array.from(new Uint8Array(buf), (b) => b.toString(16).padStart(2, '0')).join('');

  /* --------------------------------------------------- the adoption progress
   * Adding used to be instant, because adopting meant hashing. It is not any
   * more: a zip of 200 photos is 200 decodes and 200 encodes, and a card that
   * says nothing for four minutes reads as a broken button. This is the seam
   * the standalone card watches — one object, mutated in place, emitted on
   * every step. `total` grows when a zip's plan lands (an archive counts as one
   * pick until we know how many entries survived pass 1).
   * ---------------------------------------------------------------------- */

  const localProgress = { running: false, done: 0, total: 0, name: '', pct: 0 };
  const localProgressSubs = new Set();

  function emitLocalProgress() {
    for (const fn of Array.from(localProgressSubs)) {
      try { fn(localProgress); } catch (e) { warn('local-progress subscriber threw: ' + ((e && e.message) || e)); }
    }
  }

  /** One pick (or one zip entry) is finished, whatever the verdict. */
  function noteStep(name) {
    localProgress.done += 1;
    localProgress.name = String(name || '');
    localProgress.pct = 0;
    emitLocalProgress();
  }

  /** A long compression is talking. Only the percentage moves. */
  function noteWorking(name, pct) {
    if (!localProgress.running) return;
    localProgress.name = String(name || '');
    localProgress.pct = Math.min(100, Math.max(0, Math.round(Number(pct) || 0)));
    emitLocalProgress();
  }

  /* Items are emitted AS THEY LAND, so a long zip fills the list instead of
   * appearing all at once at the end — but coalesced, because a 500-entry
   * archive that repainted the list 500 times would spend longer in layout than
   * in the encoder. The final emit in addLocalFiles is unconditional, so the
   * last few always land. */
  const LOCAL_EMIT_MS = 200;
  let lastLocalEmit = 0;
  function noteItemLanded() {
    const now = Date.now();
    if (now - lastLocalEmit < LOCAL_EMIT_MS) return;
    lastLocalEmit = now;
    rebuildArr();
    emitItems();
  }

  /** Every object URL one local item owns, released. */
  function revokeLocal(it) {
    for (const u of [it && it.srcUrl, it && it.artUrl]) {
      try { if (u) URL.revokeObjectURL(u); } catch (_e) { /* ignore */ }
    }
  }

  /** sha-256 of a File/Blob's bytes, or '' when it could not be read. */
  async function shaOfBytes(buf) {
    return hex(await crypto.subtle.digest('SHA-256', buf));
  }

  /** The mime family the wire insists a `kind` agrees with (mediaChannel familyOf). */
  const kindForMime = kindOfMime;

  /**
   * Source shas already adopted (or already refused) this session. The OUTPUT of
   * a compression is what carries the wire identity, so dedup on the output sha
   * alone would still re-compress the same 40 MB photo every time the player
   * picked the same zip — minutes of a phone's CPU to arrive back at a dupe.
   */
  const localSrcShas = new Set();

  /** The compressor. Injectable — the node selftest drives the POLICY with a stub. */
  let compressor = injectedCompressor;
  function ensureCompressor() {
    if (!compressor) {
      try { compressor = createLocalCompressor({ logger }); } catch (e) {
        warn('no local compressor: ' + ((e && e.message) || e));
        compressor = null;
      }
    }
    return compressor;
  }

  /**
   * ONE file (picked by hand, or lifted out of a zip) down the ONE adoption
   * road. Every counter the summary line reads is written here, so a zip entry
   * and a hand-picked file can never be judged by two different rules.
   *
   * THE POLICY, in the order it is applied:
   *   1. not a mime the wire carries        -> badType
   *   2. video this device cannot decode    -> badCodec (probeVideoDecodable)
   *   3. ≤ 8 MB                             -> EXEMPT, as-is, untouched
   *   4. video 8..64 MB                     -> non-exempt artifact, as-is (no transcode)
   *   5. video > 64 MB                      -> tooBigVideo (its own sentence)
   *   6. still/animated > 80 MB source      -> tooBig (we will not decode that)
   *   7. animated gif/awebp > 8 MB          -> mp4 artifact, via lane B's worker
   *   8. still > 8 MB                       -> WebP (or JPEG) artifact
   *   9. any compressed output > 64 MB      -> failed (pathological; nothing to send)
   */
  async function adoptLocalFile(f, report) {
    if (!f || typeof f.arrayBuffer !== 'function') { report.failed++; return; }
    const mime = localMimeOf(f);
    if (!mime) { report.badType++; return; }
    const bytes = Math.max(0, Number(f.size) || 0);
    if (!bytes) { report.tooBig++; return; }
    if (bytes <= LOCAL_MAX_BYTES) return await adoptExempt(f, mime, bytes, report);
    return await adoptOversize(f, mime, bytes, report);
  }

  /** The original travels untouched. This is the path that has always existed. */
  async function adoptExempt(f, mime, bytes, report) {
    // The probe sits BEFORE the hash: refusing after paying for the sha would
    // only make the refusal slower. Images skip it — decode failures there
    // surface in the compressor lanes that already own them.
    if (kindForMime(mime) === 'video' && !(await probeVideoDecodable(f))) {
      report.badCodec++; return;
    }
    if (disposed) return;
    let sha = '';
    try {
      sha = await shaOfBytes(await f.arrayBuffer());
    } catch (e) {
      warn('could not hash "' + String(f.name || '?') + '": ' + ((e && e.message) || e));
      report.failed++; return;
    }
    if (disposed) return;
    const id = 'local:' + sha;
    if (localItems.has(id)) { report.dupes++; return; }
    let srcUrl = '';
    try { srcUrl = URL.createObjectURL(f); } catch (_e) { /* node selftest — no object URLs */ }
    const m = /\.([a-z0-9]{2,5})$/i.exec(String(f.name || ''));
    localItems.set(id, normalizeItem({
      id, name: String(f.name || sha.slice(0, 8)), kind: kindForMime(mime),
      state: 'exempt', srcUrl, sha, ext: m ? m[1].toLowerCase() : '', mime,
      bytes, srcBytes: bytes,
    }));
    localVersion++;
    localSrcShas.add(sha);
    report.added++;
    noteItemLanded();
  }

  /**
   * Everything over the exempt cap. What lands is a NON-EXEMPT ARTIFACT: state
   * 'ready' with an `artUrl`, which is exactly the shape boot.js listSendable()
   * offers under the 64 MB rail (`bytes <= MAX_ARTIFACT_BYTES`), and exactly the
   * shape the host's own compressed items have.
   */
  async function adoptOversize(f, mime, bytes, report) {
    const name = String(f.name || 'file');

    /* --- video: as-is up to the artifact cap, and honest above it ----------
     * A browser cannot re-encode an mp4 without decoding every frame through
     * WebCodecs, at a cost we cannot promise on a phone. So an 8..64 MB clip
     * rides the wire as the artifact it already is, and anything bigger is
     * COUNTED AND NAMED rather than quietly dropped. */
    if (kindForMime(mime) === 'video') {
      if (bytes > LOCAL_ARTIFACT_MAX_BYTES) { report.tooBigVideo++; return; }
      if (!(await probeVideoDecodable(f))) { report.badCodec++; return; }
      if (disposed) return;
      let buf = null;
      try { buf = await f.arrayBuffer(); } catch (e) {
        warn('could not read "' + name + '": ' + ((e && e.message) || e));
        report.failed++; return;
      }
      let sha = '';
      try { sha = await shaOfBytes(buf); } catch (_e) { report.failed++; return; }
      if (disposed) return;
      landArtifact({ name, blob: f, sha, mime, bytes, srcBytes: bytes, report });
      return;
    }

    if (bytes > LOCAL_DECODE_MAX_BYTES) { report.tooBig++; return; }

    let buf = null;
    try { buf = await f.arrayBuffer(); } catch (e) {
      warn('could not read "' + name + '": ' + ((e && e.message) || e));
      report.failed++; return;
    }
    if (disposed) return;

    // The SOURCE sha, so picking the same archive twice does not pay for the
    // same compression twice. The wire identity is still the OUTPUT's sha.
    let srcSha = '';
    try { srcSha = await shaOfBytes(buf); } catch (_e) { srcSha = ''; }
    if (disposed) return;
    if (srcSha && localSrcShas.has(srcSha)) { report.dupes++; return; }

    const comp = ensureCompressor();
    if (!comp) { report.failed++; return; }

    // GIF and WebP are both "maybe animated", and the answer decides the lane:
    // an animated file put through the still lane loses every frame but one.
    // 'unknown' is read as animated on purpose — a one-frame mp4 costs bytes, a
    // one-frame GIF costs the file.
    const animated = mime === 'image/gif' || mime === 'image/webp'
      ? sniffAnimated(new Uint8Array(buf), mime) !== 'still'
      : false;

    let out = null;
    try {
      out = animated
        ? await comp.compressGif(buf, mime, { onProgress: (pct) => noteWorking(name, pct) })
        : await comp.compressImage(buf, mime, {});
    } catch (e) {
      warn('could not compress "' + name + '" (' + mime + ', ' + formatBytes(bytes) + '): '
        + ((e && e.message) || e));
      if (srcSha) localSrcShas.add(srcSha);          // do not try it again this session
      report.failed++;
      return;
    }
    if (disposed) return;
    if (!out || !out.blob || !(Number(out.blob.size) > 0)) { report.failed++; return; }

    const outMime = String(out.mime || out.blob.type || '');
    if (!ACCEPT_MIME.has(outMime)) {
      warn('compressor produced "' + outMime + '" for "' + name + '", which the wire will not carry');
      report.failed++; return;
    }
    const outBytes = Number(out.blob.size) || 0;
    // The 64 MB rail is the transfer queue's, and an artifact past it is simply
    // un-offerable — adopting it would put an item on the screen that can never
    // be sent, which is worse than saying it did not work.
    if (outBytes > LOCAL_ARTIFACT_MAX_BYTES) {
      warn('"' + name + '" compressed to ' + formatBytes(outBytes) + ', past the '
        + formatBytes(LOCAL_ARTIFACT_MAX_BYTES) + ' wire cap');
      if (srcSha) localSrcShas.add(srcSha);
      report.failed++; return;
    }

    let sha = '';
    try { sha = await shaOfBytes(await out.blob.arrayBuffer()); } catch (e) {
      warn('could not hash the compressed "' + name + '": ' + ((e && e.message) || e));
      report.failed++; return;
    }
    if (disposed) return;
    if (srcSha) localSrcShas.add(srcSha);
    landArtifact({
      name, blob: out.blob, sha, mime: outMime, bytes: outBytes, srcBytes: bytes,
      w: out.w, h: out.h, durMs: out.durMs, compressed: true, report,
    });
  }

  /**
   * Commit one non-exempt artifact.
   *
   * `state: 'ready'` + `artUrl` is not decoration: listSendable() reads `artUrl`
   * for anything that is not exempt, and `bytes` must be the ARTIFACT's length
   * because that is the number the receiver's offer gate checks against its own
   * 64 MB cap. `kind` is derived from the OUTPUT mime, never carried over from
   * the source — the offer gate declines any offer whose kind and mime disagree,
   * and a GIF that became an mp4 changes family.
   */
  function landArtifact({ name, blob, sha, mime, bytes, srcBytes, w, h, durMs, compressed, report }) {
    const id = 'local:' + sha;
    if (localItems.has(id)) { report.dupes++; return; }
    let artUrl = '';
    try { artUrl = URL.createObjectURL(blob); } catch (_e) { /* node selftest — no object URLs */ }
    localItems.set(id, normalizeItem({
      id, name, kind: kindForMime(mime), state: 'ready',
      artUrl, sha, ext: extForMime(mime), mime,
      bytes, srcBytes: Math.max(bytes, Number(srcBytes) || 0),
      w: w || 0, h: h || 0, durMs: durMs || 0,
    }));
    localVersion++;
    localSrcShas.add(sha);
    report.added++;
    if (compressed) report.compressed++;
    noteItemLanded();
  }

  /**
   * Wrap extracted bytes in something the adoption road can read. A real File
   * is the good case (browsers, node 20+) because URL.createObjectURL on it
   * gives the row a working preview; the fallbacks only have to survive.
   */
  function fileFromBytes(name, bytes, mime) {
    const type = String(mime || '');
    try {
      if (typeof File === 'function') return new File([bytes], name, { type });
    } catch (_e) { /* no File constructor here */ }
    try {
      const b = new Blob([bytes], { type });
      b.name = name;                                   // Blob has none; the road reads f.name
      return b;
    } catch (_e) { /* no Blob either — node before the web streams landed */ }
    return {
      name, type, size: bytes.length,
      arrayBuffer: () => Promise.resolve(bytes.slice().buffer),
    };
  }

  /**
   * Expand one picked archive.
   *
   * THE FILE ITSELF GOES TO THE READER, never its bytes: readZipMedia slices
   * the ranges it needs, so nothing on this path ever calls arrayBuffer() on a
   * multi-gigabyte pick. (addLocalFiles routes on isZipFile BEFORE
   * adoptLocalFile, which is the only other place a pick gets read whole — it
   * hashes what it adopts.)
   *
   * PER-ENTRY refusals land in the same counters a hand-picked refusal would
   * (an entry over the exempt cap is `tooBig`, a bomb-guard truncation is
   * `failed`). An ARCHIVE-level failure gets its own counter and its own line,
   * because rendering it as `tooBig` is exactly how a 1 GB library once got
   * told "1 over 8 MB".
   */
  async function expandLocalZip(file, report) {
    if (!file || typeof file.slice !== 'function') { report.zipBad++; noteStep(file && file.name); return; }
    let res = null;
    let planned = 0;
    try {
      res = await readZipMedia(file, {
        // The mime table stays owned by this module: what the picker accepts,
        // what the zip adopts and what the wire allows are ONE decision.
        isEligible: (name) => !!localMimeOf({ name, type: '' }),
        // ...and so is the SIZE policy. This mirrors adoptLocalFile exactly,
        // from the DECLARED size in the directory, so an entry the store could
        // never use is never sliced, never inflated and never decoded — and the
        // two refusals stay distinguishable ("compressible" vs "un-sendable").
        classify: (name, size) => classifyLocalSize(localMimeOf({ name, type: '' }), size),
        maxEntryBytes: LOCAL_DECODE_MAX_BYTES,
        onPlan: (n) => {
          planned = Math.max(0, Number(n) || 0);
          // The archive counted as ONE pick until now; it is really `planned`.
          localProgress.total += Math.max(0, planned - 1);
          emitLocalProgress();
        },
        // STREAMED, not collected: each entry is adopted (hashed, compressed if
        // it must be, turned into a blob URL) and dropped before the next is
        // inflated, so an archive of five hundred photos never has five hundred
        // photos in the heap at once.
        onEntry: async (e) => {
          if (disposed) return;
          const emime = localMimeOf({ name: e.name, type: '' });
          noteWorking(e.name, 0);
          await adoptLocalFile(fileFromBytes(e.name, e.bytes, emime), report);
          noteStep(e.name);
        },
      });
    } catch (e) {
      warn('zip "' + String(file.name || '?') + '" threw: ' + ((e && e.message) || e));
      res = null;
    }
    if (!res || !res.ok) {
      warn('zip "' + String(file.name || '?') + '" (' + formatBytes(file.size) + ') could not be opened: '
        + String((res && res.reason) || 'threw'));
      // The plan's entries never happened; give the denominator back, then take
      // the archive's own one step.
      if (planned > 1) localProgress.total -= (planned - 1);
      report.zipBad++;
      noteStep(file.name);
      return;
    }
    // An archive that planned nothing still consumed a pick's worth of the bar.
    if (planned <= 0) noteStep(file.name);
    report.zips++;
    report.tooBig += res.tooBig;
    report.tooBigVideo += res.tooBigVideo || 0;
    report.failed += res.failed;
    // Entries the mime table refused are NAMED, not vanished: a zip of nothing
    // but foreign formats used to read "no media in that zip", which renders as
    // a broken button to the person who just watched their library go in.
    // `ineligible`, not `skipped` — junk sidecars and nested zips stay silent.
    report.badType += res.ineligible || 0;
    // NOT `failed`: entry 501, or the inflated-total backstop, means we TOOK THE
    // FIRST N of a real library. Rendering that as "unreadable" is the same
    // class of lie the per-file cap text was on an archive failure.
    report.trimmed += res.trimmed || 0;
    info('zip "' + String(file.name || '?') + '" (' + formatBytes(file.size) + '): ' + res.took
      + ' media entr(ies), ' + res.skipped + ' skipped'
      + (res.truncated ? ' (trimmed — the archive is past the ceilings)' : ''));
  }

  /**
   * Adopt player-picked files as sendable items. The sha-256 computed here is
   * only the OFFER identity — the receiving host re-hashes what actually
   * arrives, so lying about it buys nothing but a declined transfer.
   * Session-only: blob URLs do not survive a reload, and neither do these.
   *
   * A picked .zip is EXPANDED, not adopted: its eligible media becomes items,
   * one entry at a time, and the archive itself never becomes a sendable thing.
   *
   * SERIAL, deliberately. Compression is CPU, not IO: two encodes racing on a
   * phone finish later than the same two in a row and make the page unusable in
   * between. Progress is surfaced through `onLocalProgress` as each one lands.
   *
   * @param {ArrayLike<File>} files
   * @returns {Promise<{added:number, compressed:number, dupes:number, tooBig:number,
   *   tooBigVideo:number, trimmed:number, badType:number, badCodec:number,
   *   failed:number, zips:number, zipBad:number}>}
   */
  async function addLocalFiles(files) {
    const report = {
      added: 0, compressed: 0, dupes: 0, tooBig: 0, tooBigVideo: 0, trimmed: 0,
      badType: 0, badCodec: 0, failed: 0, zips: 0, zipBad: 0,
    };
    const list = Array.from(files || []);
    // Re-entrant adds (the player picks again while a zip is still expanding)
    // extend the same run rather than resetting the denominator under it.
    localProgress.total = (localProgress.running ? localProgress.total : 0) + list.length;
    if (!localProgress.running) { localProgress.running = true; localProgress.done = 0; }
    localProgress.name = '';
    localProgress.pct = 0;
    emitLocalProgress();
    try {
      for (const f of list) {
        if (disposed) break;
        if (isZipFile(f)) {
          // A zip's step accounting is its OWN: it counted as one pick until its
          // plan landed, and after that its entries are the steps. Stepping here
          // as well would push `done` past `total` on every archive.
          noteWorking(String(f.name || ''), 0);
          await expandLocalZip(f, report);
          continue;
        }
        noteWorking(String(f.name || ''), 0);
        await adoptLocalFile(f, report);
        noteStep(String(f.name || ''));
      }
    } finally {
      localProgress.running = false;
      localProgress.name = '';
      localProgress.pct = 0;
      emitLocalProgress();
    }
    // Unconditional: the coalescer may be holding the last few items back, and
    // "added 40" with 37 rows on screen is a bug report.
    rebuildArr();
    emitItems();
    return report;
  }

  function removeLocal(id) {
    const it = localItems.get(String(id));
    if (!it) return false;
    localItems.delete(String(id));
    localVersion++;
    // A compressed pick's bytes live behind `artUrl`, not `srcUrl` — revoking
    // only the latter leaks the whole artifact for the life of the page.
    revokeLocal(it);
    rebuildArr();
    emitItems();
    return true;
  }

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

    item(id) { return items.get(String(id)) || localItems.get(String(id)) || null; },

    /** Files the player picked in the browser (standalone's whole library). */
    get localCount() { return localItems.size; },

    /** Those files, as an array. A snapshot — mutating it changes nothing. */
    get localItems() { return Array.from(localItems.values()); },

    /**
     * Bumped whenever a local item is added or removed, and NEVER by a hosted
     * `cache-list`. boot.js watches it so the media deck is only re-dealt when
     * the player's own library actually moved (see localPlayableEntries above).
     */
    get localVersion() { return localVersion; },

    /** The local library as exec/media.js deck entries. Convenience over the pure fn. */
    localDeck() { return localPlayableEntries(Array.from(localItems.values())); },

    /**
     * ADOPTION PROGRESS — fires immediately with the current value, then on
     * every step of an `addLocalFiles` run. `{running, done, total, name, pct}`,
     * where `pct` is only meaningful during a compression. Read it, never mutate
     * it (the same contract as `state`).
     */
    onLocalProgress(fn) {
      if (typeof fn !== 'function') return () => {};
      localProgressSubs.add(fn);
      try { fn(localProgress); } catch (e) { warn('local-progress subscriber threw: ' + ((e && e.message) || e)); }
      return () => localProgressSubs.delete(fn);
    },

    /** Live adoption progress. Same object every time — do not keep a copy. */
    get localProgress() { return localProgress; },

    requestList, compressAll, compress, cancel, pause, resume, deleteOne, deleteAll, setCap,
    addLocalFiles, removeLocal,

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
      localProgressSubs.clear();
      for (const it of localItems.values()) revokeLocal(it);
      localItems.clear();
      localSrcShas.clear();
      encodeHandler = null;
      if (encoder) { try { encoder.dispose(); } catch (_e) { /* ignore */ } encoder = null; }
      // The compressor owns a Worker when the page ever built one; a disposed
      // store that leaves it running keeps a whole encoder thread alive.
      if (compressor && compressor !== injectedCompressor) {
        try { compressor.dispose?.(); } catch (_e) { /* ignore */ }
      }
      compressor = null;
      for (const t of OWNED_TYPES) { try { bridge.off(t); } catch (_e) { /* ignore */ } }
    },
  };
  return api;
}

export default createAssetsStore;
