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

const webview = (typeof window !== 'undefined') && window.chrome && window.chrome.webview;
export const isHosted = !!webview;

const handlers = new Map();  // type -> fn
const preBuffer = [];        // host messages that arrived before their handler

if (webview) {
  webview.addEventListener('message', (e) => {
    const m = e.data;
    if (!m || typeof m.type !== 'string') return;
    const h = handlers.get(m.type);
    if (h) h(m); else preBuffer.push(m);
  });
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
  try { webview && webview.postMessage(msg); } catch (_e) { /* host gone */ }
}

export function log(msg) {
  const s = String(msg).slice(0, 400);
  if (webview) send({ type: 'log', msg: s });
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
    media: (c.media && typeof c.media === 'object') ? c.media : null,
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
    media: saved.media || null,
    subjectId: q.get('subject') || saved.subjectId || null,
    subliminals: Array.isArray(saved.subliminals) ? saved.subliminals : null,
  });
}

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
