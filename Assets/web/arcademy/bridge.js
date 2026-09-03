/* ============================================================================
 * bridge.js - the postMessage contract with the WPF host (ChaosWebViewHost /
 * ArcademyHostService). Protocol v1, BUILD-CONTRACT §4.
 *
 * Transport: window.chrome.webview.postMessage (JS->C#) and the host's
 * PostWebMessageAsJson (C#->JS). Same hygiene as dtrh/bridge.js and intake's
 * web-shim.js:
 *   - the host queues host->page frames until we post `ready`;
 *   - we PRE-BUFFER any frame that lands before its handler is registered;
 *   - we QUEUE-UNTIL-INIT every page->host frame except the boot allowlist,
 *     so nothing gameplay-shaped can race the settings projection.
 * Neither side ever races the other's boot.
 *
 * Two differences from dtrh/bridge.js, both deliberate:
 *   1. handlers are a Map of type -> Set(fn), not type -> fn. The Arcademy has
 *      several independent subscribers per type (core/store.js wants `meta`,
 *      shell/settings.js wants `setting`, provider/ wants `assets`), and a
 *      single-slot Map would let whichever module imported last silently steal
 *      the other's messages.
 *   2. send() queues until markInitialized(). Only `ready`, `log`, `heartbeat`,
 *      `boot-error` and `exit` bypass the queue - the boot/telemetry/escape
 *      lane must work even when init never arrives.
 *
 * PORTABILITY: a phone/web host swaps only the transport block at the top
 * (intake's web-shim.js shows the react-native-webview shape). Nothing above
 * this file knows which host it is in.
 * ==========================================================================*/

export const PROTOCOL = 1;

const win = (typeof window !== 'undefined') ? window : null;
const webview = win && win.chrome && win.chrome.webview;

export const isHosted = !!webview;

/** type -> Set(handler). Multi-subscriber on purpose (see header). */
const handlers = new Map();
/** Host frames that arrived before anyone subscribed to their type. */
const preBuffer = [];
/** Page frames held until `init` lands (everything but the boot allowlist). */
const outQueue = [];

let initialized = false;
let heartbeatTimer = 0;

/** Frames that must never wait for init. */
const BOOT_LANE = new Set(['ready', 'log', 'heartbeat', 'boot-error', 'exit']);

const MAX_PREBUFFER = 200;   // a host that spams an unsubscribed type can't grow us forever
const MAX_OUTQUEUE = 200;

/* ----------------------------------------------------------------------------
 * HOST -> PAGE
 * -------------------------------------------------------------------------- */
function dispatch(m) {
  if (!m || typeof m.type !== 'string') return;
  const set = handlers.get(m.type);
  if (!set || !set.size) {
    if (preBuffer.length < MAX_PREBUFFER) preBuffer.push(m);
    return;
  }
  // Copy before iterating: a handler may subscribe/unsubscribe during dispatch.
  for (const fn of Array.from(set)) {
    try { fn(m); } catch (e) { log('error', `handler ${m.type} threw: ${(e && e.message) || e}`); }
  }
}

if (webview) {
  webview.addEventListener('message', (e) => {
    try { dispatch(e.data); } catch (err) { /* never let a bad frame kill the listener */ }
  });
}

/**
 * Subscribe to a host frame type. Replays any pre-buffered frames of that type,
 * in arrival order, into THIS handler only.
 * @returns {() => void} unsubscribe
 */
export function on(type, fn) {
  if (typeof fn !== 'function') return () => {};
  let set = handlers.get(type);
  if (!set) { set = new Set(); handlers.set(type, set); }
  set.add(fn);
  for (let i = 0; i < preBuffer.length; i++) {
    if (preBuffer[i].type === type) {
      const m = preBuffer.splice(i, 1)[0];
      i--;
      try { fn(m); } catch (e) { log('error', `replay ${type} threw: ${(e && e.message) || e}`); }
    }
  }
  return () => off(type, fn);
}

/** Unsubscribe one handler. */
export function off(type, fn) {
  const set = handlers.get(type);
  if (set) set.delete(fn);
}

/** Subscribe once (auto-unsubscribes after the first frame). */
export function once(type, fn) {
  const stop = on(type, (m) => { stop(); fn(m); });
  return stop;
}

/* ----------------------------------------------------------------------------
 * PAGE -> HOST
 * -------------------------------------------------------------------------- */
function post(msg) {
  try { if (webview) webview.postMessage(msg); } catch (e) { /* host gone */ }
}

/** Post a frame (must carry a string `type`). Queued until init unless boot-lane. */
export function send(msg) {
  if (!msg || typeof msg.type !== 'string') return;
  if (initialized || BOOT_LANE.has(msg.type)) { post(msg); return; }
  if (outQueue.length < MAX_OUTQUEUE) outQueue.push(msg);
}

/**
 * Flush the queue - called by boot.js the moment `init` is handled. Idempotent.
 * Everything held is posted in arrival order so the host sees one coherent
 * sequence, never a reordered one.
 */
export function markInitialized() {
  if (initialized) return;
  initialized = true;
  while (outQueue.length) post(outQueue.shift());
}

export function isInitialized() { return initialized; }

/**
 * Serilog passthrough. `level` is optional and defaults to 'debug'.
 *
 * The default is deliberately the QUIET one. The host's logger floor is Information, so 'debug'
 * frames are dropped rather than filed, and a campus with this many call sites would otherwise
 * bury every real report in class chatter. Anything worth a triage read has to say so: 'warn' for
 * a degraded room, 'error' for a broken one.
 */
export function log(level, msg) {
  if (msg === undefined) { msg = level; level = 'debug'; }
  send({ type: 'log', level: String(level), msg: String(msg).slice(0, 400) });
}

/** Announce boot completion - the host flushes its queued `init` on receipt. */
export function announceReady() { send({ type: 'ready', protocol: PROTOCOL }); }

/** Watchdog feed: one frame every 5s (BUILD-CONTRACT §4). */
export function startHeartbeat(everyMs) {
  if (heartbeatTimer) return;
  const period = everyMs || 5000;
  const beat = () => send({ type: 'heartbeat' });
  heartbeatTimer = setInterval(beat, period);
  beat();
}

export function stopHeartbeat() {
  if (heartbeatTimer) { clearInterval(heartbeatTimer); heartbeatTimer = 0; }
}

/**
 * Correlated request: post `msg`, resolve with the first `replyType` frame that
 * `match(frame)` accepts. NEVER rejects - it resolves with null on timeout, so
 * no caller can wedge on a host that stopped answering (a mute host must
 * degrade, not hang: the meta store falls back to its local cache).
 */
export function request(msg, replyType, match, timeoutMs) {
  return new Promise((resolve) => {
    let done = false;
    const finish = (v) => { if (!done) { done = true; stop(); clearTimeout(timer); resolve(v); } };
    const stop = on(replyType, (m) => {
      try { if (!match || match(m)) finish(m); } catch (e) { finish(null); }
    });
    const timer = setTimeout(() => finish(null), timeoutMs || 8000);
    send(msg);
  });
}

/** Convenience surface for modules that would rather hold one object. */
export const ccp = { send, on, off, once, log, request, get initialized() { return initialized; } };

export default ccp;
