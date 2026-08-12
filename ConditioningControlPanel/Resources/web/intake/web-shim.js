/* ============================================================================
 * web-shim.js — the bridge swap for "Graded Intake" (mirrors DtRH's bridge.js).
 *
 * ONE module, TWO transports, ONE interface:
 *   - HOSTED (WebView2 in the Lab): talks to C# IntakeWindow / host service via
 *     window.chrome.webview.postMessage, exactly like dtrh/bridge.js. The host
 *     sends `init` (BootConfig + auth) and receives `quiz-result` (QuizRunResult)
 *     to feed C# GenerateSession. Optional: `manifest`, `payload-state`, `ping`.
 *   - STANDALONE (a plain browser, e.g. harness.html or a future gated web app):
 *     no C#. The shim synthesizes a BootConfig from URL params + localStorage +
 *     defaults, persists results locally, and lets stats.js own persistence.
 *
 * boot.js / harness.html depend ONLY on this module's exported surface, so the
 * page is identical in both worlds and Phase-1 agents never branch on host.
 *
 * Protocol (host <-> page), v1 (see contracts.PROTOCOL):
 *   Host -> Page:  init {config, ai} · manifest · payload-state · meta · ping
 *   Page -> Host:  ready · log · boot-error · heartbeat/pong · quiz-result · exit
 * ==========================================================================*/

import { PROTOCOL, defaultBootConfig, DEFAULT_CAPS, NICHES, Niche } from './core/contracts.js';

/* ----------------------------------------------------------------------------
 * TRANSPORT DETECTION — first match wins: WebView2 (desktop Lab) -> RN WebView
 * (mobile host) -> standalone (plain browser). Both hosted worlds set
 * isHosted === true, so every hosted-mode path (init handshake, result flow,
 * save buttons, heartbeat) is transport-agnostic. PROTOCOL 1 is identical on the
 * wire; only the byte-level carrier differs — WebView2 posts a structured-clone
 * OBJECT, react-native-webview's postMessage is STRING-only so we JSON round-trip.
 * -------------------------------------------------------------------------- */
const win = (typeof window !== 'undefined') ? window : null;
const webview = win && win.chrome && win.chrome.webview;
// react-native-webview injects window.ReactNativeWebView.postMessage (string only)
// onto the page. Host->page frames arrive by the RN side injecting a call to the
// window.__ccpRnPush global installed below.
const rnwv = (!webview && win && win.ReactNativeWebView &&
              typeof win.ReactNativeWebView.postMessage === 'function')
             ? win.ReactNativeWebView : null;

export const isHosted = !!(webview || rnwv);

const handlers = new Map();  // type -> fn
const preBuffer = [];        // host messages that arrived before their handler

/** Demux one host->page message OBJECT into its handler (or pre-buffer it). The
 *  SINGLE dispatch path shared by both hosted transports, so the handler-Map +
 *  pre-buffer semantics are byte-identical whichever carrier delivered the frame. */
function dispatchHostMessage(m) {
  if (!m || typeof m.type !== 'string') return;
  const h = handlers.get(m.type);
  if (h) h(m); else preBuffer.push(m);
}

if (webview) {
  // WebView2 host->page (UNCHANGED): structured-clone objects arrive on `message`.
  webview.addEventListener('message', (e) => dispatchHostMessage(e.data));
} else if (rnwv) {
  // RN host->page: the RN side calls window.__ccpRnPush(jsonString). Install it
  // SYNCHRONOUSLY at module evaluation so it already exists before RN can inject,
  // and route it through the SAME dispatch/pre-buffer path WebView2 uses.
  try {
    win.__ccpRnPush = function (json) {
      let m;
      try { m = JSON.parse(json); } catch (_e) { return; }
      if (!m || typeof m.type !== 'string') return;
      dispatchHostMessage(m);
    };
  } catch (_e) { /* frozen window: never throw at import */ }
}

/** Register a handler; replays any buffered messages of that type in order. */
export function on(type, fn) {
  handlers.set(type, fn);
  for (let i = 0; i < preBuffer.length; i++) {
    if (preBuffer[i].type === type) { const m = preBuffer.splice(i, 1)[0]; i--; fn(m); }
  }
}

/** Post a message to the host (no-op when standalone). */
export function send(msg) {
  try {
    if (webview) webview.postMessage(msg);                 // WebView2: structured clone
    else if (rnwv) rnwv.postMessage(JSON.stringify(msg));  // RN: STRING only
  } catch (_e) { /* host gone */ }
}

export function log(msg) {
  const s = String(msg).slice(0, 400);
  if (isHosted) send({ type: 'log', msg: s });
  else if (typeof console !== 'undefined') console.log('[intake]', s);
}

/** Announce boot completion — the host flushes queued init/manifest on receipt. */
export function announceReady() { send({ type: 'ready', protocol: PROTOCOL }); }

/* ----------------------------------------------------------------------------
 * HIGH-LEVEL BOOT — resolves a BootConfig once, from whichever world we're in.
 * boot.js calls onBoot(cb); cb fires exactly once with a ready BootConfig.
 * -------------------------------------------------------------------------- */
let bootFired = false;
let bootCb = null;

export function onBoot(cb) {
  bootCb = cb;
  if (isHosted) {
    // Wait for the host's `init` (carries config + ai auth). announceReady()
    // (called by boot.js) triggers the host to send it.
    on('init', (m) => fireBoot(fromHostInit(m)));
  } else {
    // Standalone: resolve immediately from URL + localStorage + defaults.
    // Defer a tick so callers can finish wiring handlers first.
    Promise.resolve().then(() => fireBoot(standaloneConfig()));
  }
}

function fireBoot(config) {
  if (bootFired) return;
  bootFired = true;
  try { bootCb && bootCb(config); } catch (e) { log('onBoot cb failed: ' + (e && e.message || e)); }
}

/** Map a host `init` message to a BootConfig. */
function fromHostInit(m) {
  const c = (m && m.config) || {};
  const media = normalizeMedia(c.media, !!c.remoteMedia);
  return defaultBootConfig({
    niche: NICHES.includes(c.niche) ? c.niche : Niche.Bambi,
    caps: Object.assign({}, DEFAULT_CAPS, c.caps || {}),
    endless: !!c.endless,
    steerValve: numOr(c.steerValve, 1),
    priorRun: c.priorRun || null,
    ai: (m.ai && m.ai.serverBase) ? { serverBase: m.ai.serverBase, authToken: m.ai.authToken || '' } : null,
    hosted: true,
    m2Test: !!c.m2Test,
    // Additive (protocol unchanged): host MAY ride reward media + a stable
    // fiction subject id on the same `init` config it already sends.
    media,
    // The remote pool is a LATER arrival (see the remote-media block below), so the
    // page has to know at boot whether asking is worth it at all.
    remoteMedia: !!c.remoteMedia,
    subjectId: (typeof c.subjectId === 'string' && c.subjectId) ? c.subjectId : null,
    // Additive: the user's ENABLED subliminal pool as { text, audio? } entries, replicated
    // in-page by render/subliminals.js (audio is a data: URI when a connected clip exists).
    subliminals: Array.isArray(c.subliminals) ? c.subliminals : null,
  });
}

/** Synthesize a BootConfig for a standalone browser (harness / future web app). */
function standaloneConfig() {
  const q = new URLSearchParams((typeof location !== 'undefined' && location.search) || '');
  const saved = readLocal('intake.bootConfig') || {};
  const niche = q.get('niche') || saved.niche || Niche.Bambi;
  return defaultBootConfig({
    niche: NICHES.includes(niche) ? niche : Niche.Bambi,
    caps: Object.assign({}, DEFAULT_CAPS, saved.caps || {}),
    endless: q.has('endless') ? q.get('endless') !== '0' : !!saved.endless,
    steerValve: numOr(q.get('steer'), numOr(saved.steerValve, 1)),
    priorRun: saved.priorRun || null,
    // Optional standalone AI: ?ai=https://server&token=... (else stub).
    ai: q.get('ai') ? { serverBase: q.get('ai'), authToken: q.get('token') || '' } : (saved.ai || null),
    hosted: false,
    m2Test: q.has('m2test'),
    // Normalized through the same seam as the hosted path so the render modules see one
    // shape. Standalone has no host to ask, so the remote pool is always off.
    media: normalizeMedia(saved.media, false),
    remoteMedia: false,
    subjectId: q.get('subject') || saved.subjectId || null,
    subliminals: Array.isArray(saved.subliminals) ? saved.subliminals : null,
  });
}

/* ----------------------------------------------------------------------------
 * REMOTE MEDIA — the `need-remote` / `assets-append` pair.
 *
 * The host's MediaManifest is built once, from disk, and rides `init`. Remote stills
 * cannot: fetching them is a network round trip the host will not make on the thread that
 * builds `init`. So the page asks, and batches arrive later — the same bridge pair the For
 * You feed uses, and for the same reason.
 *
 * THE ONE RULE THAT MAKES IT WORK: an append MUTATES THE EXISTING ARRAYS. `config.media`
 * was handed to effects/background/beats/steering at boot and none of them can be re-handed
 * one, so growth has to happen inside the arrays they already hold — which is why
 * normalizeMedia() guarantees `gifs`/`images` exist as arrays even when the host sent no
 * media at all. Appending is never a reset: nothing already on screen is disturbed.
 *
 * REMOTE STILLS GO IN `images`, NEVER `gifs`. They do not animate (the provider has no
 * usable gifs), and the surfaces that read `gifs` — the captcha's rotting tiles, the
 * jumpscare — are built around something that moves.
 * -------------------------------------------------------------------------- */
const REMOTE_CAP = 80;          // page-side ceiling on how big the remote pool may grow
let remoteAdded = 0;            // how many remote urls we have taken this run
let remoteInFlight = false;
let mediaRef = null;            // the live MediaManifest the render modules hold
const mediaAppendCbs = [];

/** Ensure a MediaManifest object with real arrays, so later appends have somewhere to land.
 *  Returns null only when there is genuinely nothing and never will be — the exact case the
 *  render modules already handle by falling back to their particle stand-ins. */
function normalizeMedia(raw, remoteEnabled) {
  const has = raw && typeof raw === 'object';
  if (!has && !remoteEnabled) return null;
  const src = has ? raw : {};
  const list = (v) => (Array.isArray(v) ? v.filter((u) => typeof u === 'string' && u.length > 0) : []);
  mediaRef = Object.assign({}, src, { gifs: list(src.gifs), images: list(src.images) });
  return mediaRef;
}

/** Ask the host for another batch of remote stills. No-op standalone, when the pool is
 *  full, or while one ask is already in the air. */
export function requestRemoteMedia() {
  if (!isHosted || remoteInFlight || !mediaRef || remoteAdded >= REMOTE_CAP) return;
  remoteInFlight = true;
  send({ type: 'need-remote' });
}

/** Register a callback fired after every append that actually added urls. Additive: the
 *  caller uses it to keep asking until the pool is stocked. */
export function onMediaAppend(cb) { if (typeof cb === 'function') mediaAppendCbs.push(cb); }

on('assets-append', (m) => {
  remoteInFlight = false;
  if (!mediaRef) return;
  const incoming = Array.isArray(m && m.images) ? m.images : [];
  const known = new Set(mediaRef.images);
  let added = 0;
  for (const u of incoming) {
    if (typeof u !== 'string' || !u.length || known.has(u)) continue;
    if (remoteAdded >= REMOTE_CAP) break;
    mediaRef.images.push(u);      // IN PLACE — see the block comment above
    known.add(u);
    remoteAdded++;
    added++;
  }
  if (added) {
    log(`remote media: +${added} still(s) (pool ${mediaRef.images.length})`);
    for (const cb of mediaAppendCbs) { try { cb(added); } catch (_e) { /* never break the bridge */ } }
  }
});

// Arrives after every attempt, success or not. It is what stops the page asking in a tight
// loop when every channel is dry or the machine is offline.
on('online-status', (m) => {
  remoteInFlight = false;
  if (m && m.ok === false) log('remote media: ' + ((m && m.error) || 'unavailable'));
});

/* ----------------------------------------------------------------------------
 * SUBJECT ID (standalone) — the fiction's stable per-install "Subject #NNNN".
 * Hosted, the host supplies BootConfig.subjectId on `init`; standalone we mint a
 * 4-digit id ONCE and persist it in the same prefs blob standaloneConfig reads,
 * so every future run greets the same Subject #. Additive only.
 * -------------------------------------------------------------------------- */
export function ensureStandaloneSubjectId() {
  if (isHosted) return null;
  try {
    const cur = readLocal('intake.bootConfig') || {};
    if (cur.subjectId) return String(cur.subjectId);
    const id = String(1 + Math.floor(Math.random() * 9998)).padStart(4, '0');
    saveStandalonePrefs({ subjectId: id });
    return id;
  } catch (_e) { return null; }
}

/* ----------------------------------------------------------------------------
 * RESULT EMIT — the whole point. Hand a QuizRunResult back to whoever can use it.
 *   Hosted    -> `quiz-result` message; C# QuizSessionGenerator drafts a session.
 *   Standalone-> persist to localStorage + return; stats.js also records it.
 * Returns a small ack so the caller can show "session drafted" vs "saved locally".
 * -------------------------------------------------------------------------- */
export function emitResult(result) {
  if (isHosted) {
    send({ type: 'quiz-result', result });
    return { delivered: 'host' };
  }
  try {
    const key = 'intake.lastResult';
    writeLocal(key, result);
    // keep a short rolling history for standalone debugging
    const hist = readLocal('intake.resultHistory') || [];
    hist.push({ at: result.endedAtMs || 0, niche: result.niche, peakDepth: result.peakDepth });
    writeLocal('intake.resultHistory', hist.slice(-20));
  } catch (_e) { /* storage may be unavailable */ }
  return { delivered: 'local' };
}

/** Persist the standalone BootConfig (so a harness reload keeps your picks). */
export function saveStandalonePrefs(partial) {
  if (isHosted) return;
  const cur = readLocal('intake.bootConfig') || {};
  writeLocal('intake.bootConfig', Object.assign(cur, partial || {}));
}

/* ----------------------------------------------------------------------------
 * PAYLOAD COVER — parity with DtRH: a native mandatory video may cover the page.
 * Standalone this never fires. Hosted, the engine pauses while covered.
 * -------------------------------------------------------------------------- */
export function onPayloadCover(cb) {
  on('payload-state', (m) => { if (m && m.kind === 'video') cb(!!m.on); });
}

/** Heartbeat for the host wedge-watchdog (parity with DtRH). No-op standalone. */
export function startHeartbeat() {
  if (!isHosted) return;
  on('ping', (m) => send({ type: 'pong', t: m && m.t }));
  let last = 0;
  (function beat(now) {
    if (now - last > 2000) { last = now; send({ type: 'heartbeat', t: now }); }
    requestAnimationFrame(beat);
  })(0);
}

/** Report a fatal boot failure (host may fall back / show a message). */
export function bootError(msg) { send({ type: 'boot-error', msg: String(msg) }); }

/* ----------------------------------------------------------------------------
 * ABORT CLOSE — the "are you sure? -> Yes" jumpscare (beats.js) asks the window
 * to close as an ABORT: NO QuizRunResult is emitted, so no session is drafted.
 *   Hosted    -> `intake-close`; C# IntakeHostService disposes the window.
 *   Standalone-> best-effort window.close() then blank the page (no C# to ask).
 * Additive surface — callers guard `typeof shim.closeHost === 'function'`.
 * -------------------------------------------------------------------------- */
export function closeHost() {
  if (isHosted) {
    send({ type: 'intake-close' });
    return { closed: 'host' };
  }
  try { if (typeof window !== 'undefined' && typeof window.close === 'function') window.close(); } catch (_e) {}
  try { if (typeof location !== 'undefined') location.href = 'about:blank'; } catch (_e) {}
  return { closed: 'local' };
}

/* ----------------------------------------------------------------------------
 * tiny local helpers
 * -------------------------------------------------------------------------- */
function numOr(v, d) { const n = parseFloat(v); return isFinite(n) ? n : d; }
function readLocal(k) {
  try { const s = localStorage.getItem(k); return s ? JSON.parse(s) : null; } catch (_e) { return null; }
}
function writeLocal(k, v) {
  try { localStorage.setItem(k, JSON.stringify(v)); } catch (_e) { /* ignore */ }
}
