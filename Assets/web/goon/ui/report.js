/* ============================================================================
 * ui/report.js — filing an abuse report on something a duel partner SENT you
 * (spec §7.2 for the wire, §7.5 for where the UI lives).
 *
 * Two jobs, kept apart on purpose:
 *
 *   1. EVIDENCE. A moderator cannot ask the reporter for the file — the file
 *      never touched a server and by the time anyone reads the queue the peer
 *      is gone. So the page renders ONE small still (an image) or ONE vertical
 *      three-frame strip at 10/50/90% (a video), base64s it, and ships it with
 *      the report. 320 px on the long edge, WebP q0.7, stepping down until it
 *      fits EVIDENCE_MAX_B64.
 *
 *   2. SUBMISSION. `submitReport` builds exactly the body proxy/goon-routes.js
 *      `POST /v2/goon/report` reads and posts it through bridge.postNet, which
 *      NEVER rejects (status 0 = "no answer"). Nothing here throws at a caller.
 *
 * THREE THINGS THAT LOOK LIKE BUGS AND ARE NOT
 *
 *   EVIDENCE IS OPTIONAL, SERVER-SIDE AND HERE. goon-routes.js only validates
 *   `evidence` when it is present (`if (ev != null)`), so a canvas that would
 *   not encode, a clip that would not seek, a tainted cross-origin frame and a
 *   runtime with no `toBlob` all resolve to `null` and the report still files.
 *   An evidence failure must never be the reason an abuse report does not get
 *   sent. There is no placeholder frame: a grey rectangle in a moderation queue
 *   is worse than an honest blank, because it looks like evidence.
 *
 *   THE 700 KiB CAP IS NOT ABOUT THE PICTURE. server.js:94 installs
 *   `express.json({limit:'1mb'})` GLOBALLY, before every route — Vercel's 4.5 MB
 *   is NOT the wall. A 3-frame 320 px WebP strip is 30-80 KB, so the ladder
 *   below almost never runs past its first rung; it exists so a pathological
 *   frame cannot 413 a report.
 *
 *   THE CROSS-ORIGIN DANCE. Received artifacts are served from
 *   https://ccp.cache/recv/… while the page is https://ccp.game/goon/ — a
 *   DIFFERENT origin. Drawing one into a canvas without CORS taints it and
 *   `toBlob` throws SecurityError. So every element is loaded with
 *   `crossOrigin='anonymous'` first and retried bare only if that fails to
 *   load at all (the standalone memory backend hands out same-origin `blob:`
 *   URLs, where neither applies). A taint still ends as `null`, never a throw.
 *
 * Everything above the DOM line is a PURE FUNCTION so test/selftest-report.js
 * can prove the size ladder, the body shape and the response parsing under
 * plain node with no canvas at all.
 * ==========================================================================*/

import * as defaultBridge from '../bridge.js';

/* ----------------------------------------------------------------- contract */

/** The wire codes, EXACTLY as `REPORT_REASONS` in proxy/goon-routes.js. */
export const REPORT_REASONS = Object.freeze(['csam', 'nonconsensual', 'gore', 'illegal', 'other']);

/** goon-routes.js REPORT_NOTE_MAX. */
export const NOTE_MAX = 500;

/** goon-routes.js REPORT_EVIDENCE_MAX_B64 — CHARS of base64, not bytes. */
export const EVIDENCE_MAX_B64 = 700 * 1024;

/** The one `evidence.kind` the route accepts. */
export const EVIDENCE_KIND = 'thumb_strip';

/** goon-routes.js EVIDENCE_MIMES, in preference order. */
export const EVIDENCE_MIME = 'image/webp';
export const EVIDENCE_MIME_FALLBACK = 'image/jpeg';

/** Long-edge budgets and encoder qualities, biggest/best first. */
export const EVIDENCE_DIMS = Object.freeze([320, 240, 160]);
export const EVIDENCE_QUALITIES = Object.freeze([0.7, 0.5, 0.35]);

/** Where in a clip the three strip frames come from. */
export const VIDEO_FRAME_POINTS = Object.freeze([0.10, 0.50, 0.90]);

/** The route's own sanity clamps on `evidence` (w/h/frames). */
export const EVIDENCE_MAX_W = 4096;
export const EVIDENCE_MAX_H = 8192;
export const EVIDENCE_MAX_FRAMES = 8;

/**
 * How long one media element gets to become drawable / finish one seek.
 *
 * These timers are deliberately NOT `unref()`d, unlike everything in exec/.
 * That tier builds background renderers whose timers must never hold node's
 * loop open; this module runs once, from a deliberate press on the recap
 * screen, and these are DEADLINES — a load that never fires an event has to
 * resolve or the report button spins forever. The selftest passes short
 * overrides instead (see `evidenceFor`'s second argument).
 */
export const LOAD_TIMEOUT_MS = 8000;
export const SEEK_TIMEOUT_MS = 4000;

const SHA_RE = /^[0-9a-f]{64}$/;

/* ============================================================================
 * PURE — no DOM, no network, no clock. All of this is what the selftest reads.
 * ==========================================================================*/

/**
 * The encode ladder, in attempt order: spend QUALITY first at the full 320 px
 * (a softer thumbnail is still a recognisable thumbnail), and only then start
 * losing PIXELS. The last rung is the smallest thing this module will ever
 * offer; if that still does not fit, the report goes without evidence.
 *
 * Quality is not re-raised after a dimension drop — a smaller frame at q0.7
 * would often be BIGGER than the previous frame at q0.35, so the ladder would
 * stop being monotonic and the first-fit search would stop being meaningful.
 *
 * @returns {ReadonlyArray<{maxDim:number, quality:number}>}
 */
export function evidenceSteps() {
  const out = [];
  const floor = EVIDENCE_QUALITIES[EVIDENCE_QUALITIES.length - 1];
  for (const q of EVIDENCE_QUALITIES) out.push({ maxDim: EVIDENCE_DIMS[0], quality: q });
  for (let d = 1; d < EVIDENCE_DIMS.length; d++) out.push({ maxDim: EVIDENCE_DIMS[d], quality: floor });
  return Object.freeze(out);
}

/** Fit w×h inside a `maxDim` box, preserving aspect. Never returns 0. */
export function fitDims(w, h, maxDim) {
  const sw = Math.max(1, Math.floor(Number(w) || 0));
  const sh = Math.max(1, Math.floor(Number(h) || 0));
  const cap = Math.max(1, Math.floor(Number(maxDim) || 1));
  const long = Math.max(sw, sh);
  if (long <= cap) return { w: sw, h: sh };
  const k = cap / long;
  return { w: Math.max(1, Math.round(sw * k)), h: Math.max(1, Math.round(sh * k)) };
}

/**
 * The strip canvas for `n` frames of `frameW`×`frameH`, stacked vertically.
 * Clamped to the route's own w/h ceilings so a pathological source can never
 * produce evidence the server will reject on geometry alone.
 */
export function stripDims(frameW, frameH, n) {
  const count = Math.max(1, Math.min(EVIDENCE_MAX_FRAMES, Math.floor(Number(n) || 1)));
  const w = Math.max(1, Math.min(EVIDENCE_MAX_W, Math.floor(Number(frameW) || 1)));
  const h = Math.max(1, Math.min(EVIDENCE_MAX_H, Math.floor(Number(frameH) || 1) * count));
  return { w, h, frames: count };
}

/** Does this base64 payload fit under the route's cap? */
export function fitsEvidence(b64) {
  return typeof b64 === 'string' && b64.length > 0 && b64.length <= EVIDENCE_MAX_B64;
}

/** `data:image/webp;base64,AAAA` -> `AAAA`. Anything else comes back as-is. */
export function stripDataPrefix(dataUrl) {
  const s = String(dataUrl || '');
  const at = s.indexOf(',');
  return (at >= 0 && /^data:/i.test(s)) ? s.slice(at + 1) : s;
}

/** Newlines out, 500 chars max — the same shape goon-routes.js stores. */
export function clampNote(note) {
  return String(note == null ? '' : note).replace(/[\r\n\t]+/g, ' ').trim().slice(0, NOTE_MAX);
}

/** Is this an `evidence` object the route will accept? Used before sending. */
export function validEvidence(ev) {
  if (!ev || typeof ev !== 'object') return false;
  if (ev.kind !== EVIDENCE_KIND) return false;
  if (ev.mime !== EVIDENCE_MIME && ev.mime !== EVIDENCE_MIME_FALLBACK) return false;
  if (!fitsEvidence(ev.b64)) return false;
  if (/[^A-Za-z0-9+/=]/.test(ev.b64) || ev.b64.length % 4 !== 0) return false;
  if (!(ev.w > 0 && ev.w <= EVIDENCE_MAX_W)) return false;
  if (!(ev.h > 0 && ev.h <= EVIDENCE_MAX_H)) return false;
  if (!(ev.frames > 0 && ev.frames <= EVIDENCE_MAX_FRAMES)) return false;
  return true;
}

/**
 * THE BODY, and it is the whole contract with proxy/goon-routes.js.
 *
 * `code`, `role` and `token` go out ALWAYS, not only while the room is alive:
 * the route tries `roomAuth` first and falls back to the 14-day pair record
 * once `goon:room:<code>` has expired, so a report filed minutes after the
 * player left the recap still lands. Sending the token costs nothing and is
 * the difference between working and working-for-thirty-minutes.
 *
 * `evidence` is OMITTED (not null) when there is none — the route branches on
 * `ev != null` and a null would be an object-shaped 400 waiting to happen.
 *
 * @returns {object|null} null when the identifiers are not reportable
 */
export function buildReportBody({ session, artifact, reason, note, atMatchMs, evidence } = {}) {
  const s = session || {};
  const room = s.room || {};
  const a = artifact || {};

  const unifiedId = String((s.identity && s.identity.unifiedId) || '');
  const code = String(room.code || '');
  const role = room.role === 'host' ? 'host' : (room.role === 'guest' ? 'guest' : '');
  const sha256 = String(a.sha || a.sha256 || '').toLowerCase();

  if (!unifiedId || !code || !role) return null;
  if (!SHA_RE.test(sha256)) return null;
  if (REPORT_REASONS.indexOf(reason) < 0) return null;

  const body = {
    unified_id: unifiedId,
    code,
    role,
    token: String(room.token || ''),
    sha256,
    mime: String(a.mime || '').slice(0, 64),
    bytes: Math.max(0, Math.floor(Number(a.bytes) || 0)),
    reason,
    note: clampNote(note),
    at_match_ms: Math.max(0, Math.floor(Number(atMatchMs) || 0)),
  };
  if (validEvidence(evidence)) {
    body.evidence = {
      kind: EVIDENCE_KIND,
      mime: evidence.mime,
      w: evidence.w,
      h: evidence.h,
      frames: evidence.frames,
      b64: evidence.b64,
    };
  }
  return body;
}

/**
 * `{status, body}` from bridge.postNet -> the four states the card renders.
 *
 * status 0 is not an error CODE, it is the absence of an answer (postNet never
 * rejects), and it is the one case where "try again" is honest advice.
 *
 * @returns {{ok:boolean, id:string, deduped:boolean, status:number, error:string}}
 */
export function parseReportResponse(res) {
  const status = (res && res.status) | 0;
  let parsed = null;
  try { parsed = res && res.body ? JSON.parse(res.body) : null; } catch (_e) { parsed = null; }

  if (status === 200 && parsed && parsed.ok) {
    return {
      ok: true,
      id: typeof parsed.id === 'string' ? parsed.id : '',
      deduped: parsed.deduped === true,
      status,
      error: '',
    };
  }
  const error = (parsed && typeof parsed.error === 'string' && parsed.error)
    || (status === 0 ? 'network' : ('http_' + status));
  return { ok: false, id: '', deduped: false, status, error };
}

/* ============================================================================
 * DOM — evidence generation. Everything below degrades to `null`.
 * ==========================================================================*/

const hasDom = () => typeof document !== 'undefined' && !!document;

/** A canvas, or null on a runtime that has none (node, a stub DOM). */
function makeCanvas(w, h) {
  if (!hasDom()) return null;
  let c = null;
  try { c = document.createElement('canvas'); } catch (_e) { return null; }
  if (!c) return null;
  try { c.width = w; c.height = h; } catch (_e) { return null; }
  let ctx = null;
  try { ctx = typeof c.getContext === 'function' ? c.getContext('2d') : null; } catch (_e) { ctx = null; }
  return ctx ? { canvas: c, ctx } : null;
}

/** Blob -> base64 (no `data:` prefix). Resolves '' rather than rejecting. */
function blobToBase64(blob) {
  return new Promise((resolve) => {
    if (!blob || typeof FileReader !== 'function') { resolve(''); return; }
    let fr = null;
    try { fr = new FileReader(); } catch (_e) { resolve(''); return; }
    fr.onerror = () => resolve('');
    fr.onabort = () => resolve('');
    fr.onload = () => resolve(stripDataPrefix(fr.result));
    try { fr.readAsDataURL(blob); } catch (_e) { resolve(''); }
  });
}

/**
 * canvas -> {mime, b64} at one rung of the ladder, or null.
 * WebP first; a runtime whose `toBlob` answers null for image/webp (or hands
 * back a PNG because it did not understand the type) falls to JPEG, which is
 * the route's other accepted mime.
 */
async function encodeCanvas(canvas, quality, timeoutMs) {
  for (const mime of [EVIDENCE_MIME, EVIDENCE_MIME_FALLBACK]) {
    const blob = await new Promise((resolve) => {
      if (!canvas || typeof canvas.toBlob !== 'function') { resolve(null); return; }
      let settled = false;
      const done = (b) => { if (!settled) { settled = true; resolve(b || null); } };
      // A toBlob that never calls back would hang the whole report.
      const t = setTimeout(() => done(null), timeoutMs);
      try { canvas.toBlob((b) => { clearTimeout(t); done(b); }, mime, quality); }
      catch (_e) { clearTimeout(t); done(null); }
    });
    if (!blob) continue;
    if (blob.type && blob.type !== mime) continue;       // the runtime substituted a format
    const b64 = await blobToBase64(blob);
    if (fitsEvidence(b64)) return { mime, b64 };
    if (b64) return { mime, b64, tooBig: true };         // the caller steps down
  }
  return null;
}

/** Load one <img>/<video>, CORS-clean if the host allows it. Never throws. */
function loadElement(tag, url, readyEvent, timeoutMs) {
  return new Promise((resolve) => {
    if (!hasDom()) { resolve(null); return; }

    const attempt = (withCors) => {
      let node = null;
      try { node = document.createElement(tag); } catch (_e) { resolve(null); return; }
      let settled = false;
      let timer = 0;
      const finish = (ok) => {
        if (settled) return;
        settled = true;
        try { clearTimeout(timer); } catch (_e) { /* ignore */ }
        if (ok) { resolve(node); return; }
        try { node.removeAttribute('src'); } catch (_e) { /* ignore */ }
        // ONE retry without CORS: a host that serves the cache origin without
        // Access-Control-Allow-Origin refuses the request outright, and a
        // TAINTED frame we can at least try to encode beats no frame at all
        // (the encode throws SecurityError and we end at null either way).
        if (withCors) attempt(false); else resolve(null);
      };
      try {
        if (withCors) node.crossOrigin = 'anonymous';
        if (tag === 'video') {
          node.muted = true;
          node.playsInline = true;
          node.preload = 'auto';
        }
        node.addEventListener(readyEvent, () => finish(true));
        node.addEventListener('error', () => finish(false));
        timer = setTimeout(() => finish(false), timeoutMs);
        node.src = url;
        if (tag === 'video') { try { node.load(); } catch (_e) { /* optional */ } }
      } catch (_e) { finish(false); }
    };
    attempt(true);
  });
}

/** Seek a loaded <video> and resolve once a frame at that time is drawable. */
function seekTo(video, seconds, timeoutMs) {
  return new Promise((resolve) => {
    let settled = false;
    let timer = 0;
    const done = (ok) => {
      if (settled) return;
      settled = true;
      try { clearTimeout(timer); } catch (_e) { /* ignore */ }
      try { video.removeEventListener('seeked', onSeeked); } catch (_e) { /* ignore */ }
      resolve(ok);
    };
    const onSeeked = () => done(true);
    try {
      video.addEventListener('seeked', onSeeked);
      timer = setTimeout(() => done(false), timeoutMs);
      video.currentTime = Math.max(0, seconds);
    } catch (_e) { done(false); }
  });
}

/** Let go of a media element the moment we are done with it. */
function releaseElement(node) {
  if (!node) return;
  try { node.pause?.(); } catch (_e) { /* not a video */ }
  try { node.removeAttribute('src'); } catch (_e) { /* ignore */ }
  try { node.load?.(); } catch (_e) { /* not a video */ }
}

/**
 * ONE image -> one 320 px still. Returns the frames as canvases to encode.
 * @returns {Promise<{frames:HTMLCanvasElement[], w:number, h:number}|null>}
 */
async function imageFrames(url, maxDim, t) {
  const img = await loadElement('img', url, 'load', t.load);
  if (!img) return null;
  const sw = Number(img.naturalWidth || img.width) || 0;
  const sh = Number(img.naturalHeight || img.height) || 0;
  if (!(sw > 0 && sh > 0)) { releaseElement(img); return null; }
  const d = fitDims(sw, sh, maxDim);
  const c = makeCanvas(d.w, d.h);
  if (!c) { releaseElement(img); return null; }
  try { c.ctx.drawImage(img, 0, 0, d.w, d.h); }
  catch (_e) { releaseElement(img); return null; }
  releaseElement(img);
  return { frames: [c.canvas], w: d.w, h: d.h };
}

/**
 * ONE clip -> up to three frames at 10/50/90%, drawn into ONE vertical strip.
 * A clip that only yields one frame still makes a legitimate one-frame strip —
 * `frames` tells the moderator what they are looking at.
 */
async function videoStrip(url, maxDim, t) {
  const video = await loadElement('video', url, 'loadedmetadata', t.load);
  if (!video) return null;
  const sw = Number(video.videoWidth) || 0;
  const sh = Number(video.videoHeight) || 0;
  const dur = Number(video.duration);
  if (!(sw > 0 && sh > 0)) { releaseElement(video); return null; }

  const d = fitDims(sw, sh, maxDim);
  const shots = [];
  const usable = (isFinite(dur) && dur > 0) ? dur : 0;
  const points = usable ? VIDEO_FRAME_POINTS : [0];

  for (const p of points) {
    // The frame element is reused; each seek overwrites it, so every shot is
    // drawn into its OWN canvas the moment it lands.
    if (usable) {
      const ok = await seekTo(video, Math.min(usable - 0.05, usable * p), t.seek);
      if (!ok) continue;
    }
    const c = makeCanvas(d.w, d.h);
    if (!c) continue;
    try { c.ctx.drawImage(video, 0, 0, d.w, d.h); shots.push(c.canvas); }
    catch (_e) { /* one unreadable frame is not the whole strip */ }
    if (shots.length >= EVIDENCE_MAX_FRAMES) break;
  }
  releaseElement(video);
  if (!shots.length) return null;

  const strip = stripDims(d.w, d.h, shots.length);
  const out = makeCanvas(strip.w, strip.h);
  if (!out) return null;
  try {
    for (let i = 0; i < shots.length; i++) out.ctx.drawImage(shots[i], 0, i * d.h);
  } catch (_e) { return null; }
  return { frames: [out.canvas], w: strip.w, h: strip.h, count: shots.length };
}

/**
 * THE ONE ENTRY POINT for evidence.
 *
 * @param {{url:string, kind:string, mime?:string}} view a receivedStore view row
 * @param {{loadTimeoutMs?:number, seekTimeoutMs?:number}} [opts] TEST AFFORDANCE
 * @returns {Promise<{kind:string, mime:string, w:number, h:number, frames:number,
 *                    b64:string}|null>} null is a normal, non-blocking outcome
 */
export async function evidenceFor(view, opts) {
  const v = view || {};
  const o = opts || {};
  const t = {
    load: Number.isFinite(o.loadTimeoutMs) ? o.loadTimeoutMs : LOAD_TIMEOUT_MS,
    seek: Number.isFinite(o.seekTimeoutMs) ? o.seekTimeoutMs : SEEK_TIMEOUT_MS,
  };
  const url = String(v.url || '');
  if (!url || !hasDom()) return null;
  const isVideo = v.kind === 'video' || /^video\//.test(String(v.mime || ''));

  // One decode per DIMENSION, not per rung: the three quality attempts at 320 px
  // all draw the same pixels, and re-seeking a clip three times to re-encode it
  // softer would be three times the work for an identical canvas.
  const drawn = new Map();

  for (const step of evidenceSteps()) {
    let built = drawn.get(step.maxDim) || null;
    if (!built) {
      try {
        built = isVideo ? await videoStrip(url, step.maxDim, t) : await imageFrames(url, step.maxDim, t);
      } catch (_e) { built = null; }
      if (built) drawn.set(step.maxDim, built);
    }
    if (!built) return null;                     // the source is unreadable at ANY size

    let enc = null;
    try { enc = await encodeCanvas(built.frames[0], step.quality, t.load); }
    catch (_e) { enc = null; }                   // SecurityError on a tainted canvas lands here
    if (!enc) return null;
    if (enc.tooBig) continue;                    // next rung of the ladder

    const ev = {
      kind: EVIDENCE_KIND,
      mime: enc.mime,
      w: built.w,
      h: built.h,
      frames: built.count || 1,
      b64: enc.b64,
    };
    return validEvidence(ev) ? ev : null;
  }
  return null;                                   // even the smallest rung was too big
}

/* ============================================================================
 * SUBMISSION
 * ==========================================================================*/

/**
 * File the report. Resolves — never rejects, never throws.
 *
 * @param {object} o
 * @param {object} o.session       boot's `session` ({identity, room})
 * @param {{sha:string, mime?:string, bytes?:number}} o.artifact
 * @param {string} o.reason        one of REPORT_REASONS (the WIRE code)
 * @param {string} [o.note]        clamped to NOTE_MAX
 * @param {number} [o.atMatchMs]
 * @param {object|null} [o.evidence]
 * @param {(path:string, body:object) => Promise<{status:number, body:string}>} [o.post]
 * @returns {Promise<{ok:boolean, id:string, deduped:boolean, status:number, error:string}>}
 */
export async function submitReport({
  session, artifact, reason, note, atMatchMs, evidence, post,
} = {}) {
  const body = buildReportBody({ session, artifact, reason, note, atMatchMs, evidence });
  if (!body) return { ok: false, id: '', deduped: false, status: 0, error: 'incomplete' };

  const send = typeof post === 'function'
    ? post
    : (p, b) => defaultBridge.postNet(p, b);

  let res = null;
  try { res = await send('/v2/goon/report', body); }
  catch (_e) { res = null; }                     // postNet does not reject; a fake might
  return parseReportResponse(res || { status: 0, body: '' });
}

export default { evidenceFor, submitReport, buildReportBody, parseReportResponse };
