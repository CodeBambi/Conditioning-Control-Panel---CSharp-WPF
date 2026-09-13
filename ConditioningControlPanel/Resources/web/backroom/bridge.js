/* ============================================================================
 * backroom/bridge.js - the postMessage contract with BackRoomHostService.
 * Protocol 1, CONTRACT section 2. The arcademy/bridge.js pattern:
 *   - the host queues its frames until we post `ready`;
 *   - host frames that land before anyone subscribed are pre-buffered;
 *   - page frames wait for `init`, except `ready`, `log` and `exit`.
 *
 * One difference, from the contract: request() never rejects AND never
 * resolves null. A missing reply resolves as {ok:false, reason:'timeout'} so a
 * station can read `res.ok` / `res.reason` on every path.
 * ==========================================================================*/

export const PROTOCOL = 1;

const win = (typeof window !== 'undefined') ? window : null;
const webview = win && win.chrome && win.chrome.webview;

export const isHosted = !!webview;

const handlers = new Map();   // type -> Set(fn)
const preBuffer = [];
const outQueue = [];
const BOOT_LANE = new Set(['ready', 'log', 'exit']);
const MAX_BUFFER = 200;
let initialized = false;

function dispatch(m) {
  if (!m || typeof m.type !== 'string') return;
  const set = handlers.get(m.type);
  if (!set || !set.size) {
    if (preBuffer.length < MAX_BUFFER) preBuffer.push(m);
    return;
  }
  for (const fn of Array.from(set)) {
    try { fn(m); } catch (e) { log('error', 'handler ' + m.type + ' threw: ' + ((e && e.message) || e)); }
  }
}

if (webview) {
  webview.addEventListener('message', (e) => { try { dispatch(e.data); } catch (err) { /* keep listening */ } });
}

/** Subscribe to a host frame type; replays pre-buffered frames of that type. */
export function on(type, fn) {
  if (typeof fn !== 'function') return () => {};
  let set = handlers.get(type);
  if (!set) { set = new Set(); handlers.set(type, set); }
  set.add(fn);
  for (let i = 0; i < preBuffer.length; i++) {
    if (preBuffer[i].type !== type) continue;
    const m = preBuffer.splice(i, 1)[0];
    i--;
    try { fn(m); } catch (e) { log('error', 'replay ' + type + ' threw: ' + ((e && e.message) || e)); }
  }
  return () => off(type, fn);
}

export function off(type, fn) {
  const set = handlers.get(type);
  if (set) set.delete(fn);
}

export function once(type, fn) {
  const stop = on(type, (m) => { stop(); fn(m); });
  return stop;
}

function post(msg) {
  try { if (webview) webview.postMessage(msg); } catch (e) { /* host gone */ }
}

/** Post a frame. Held until init unless it is on the boot lane. */
export function send(msg) {
  if (!msg || typeof msg.type !== 'string') return;
  if (initialized || BOOT_LANE.has(msg.type)) { post(msg); return; }
  if (outQueue.length < MAX_BUFFER) outQueue.push(msg);
}

/** Called once `init` is handled: flush held frames in arrival order. */
export function markInitialized() {
  if (initialized) return;
  initialized = true;
  while (outQueue.length) post(outQueue.shift());
}

export function isInitialized() { return initialized; }

/** Serilog passthrough; 'debug' unless the caller says otherwise. */
export function log(level, msg) {
  if (msg === undefined) { msg = level; level = 'debug'; }
  send({ type: 'log', level: String(level), msg: String(msg).slice(0, 400) });
}

export function announceReady() { send({ type: 'ready', protocol: PROTOCOL }); }

/** 32 hex chars from the platform rng (reqIds, idem keys, fx tokens). */
export function mintId() {
  const b = new Uint8Array(16);
  try { crypto.getRandomValues(b); } catch (e) { for (let i = 0; i < 16; i++) b[i] = Math.floor(Math.random() * 256); }
  return Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('');
}

/**
 * Correlated request. Resolves with the first `replyType` frame `match` accepts,
 * or with `fallback` (default {ok:false, reason:'timeout'}) after `timeoutMs`.
 * NEVER rejects.
 */
export function request(msg, replyType, match, timeoutMs, fallback) {
  return new Promise((resolve) => {
    let done = false;
    const finish = (v) => { if (!done) { done = true; stop(); clearTimeout(timer); resolve(v); } };
    const stop = on(replyType, (m) => {
      try { if (!match || match(m)) finish(m); } catch (e) { finish(fallback || { ok: false, reason: 'timeout' }); }
    });
    const timer = setTimeout(() => finish(fallback || { ok: false, reason: 'timeout' }), timeoutMs || 6000);
    send(msg);
  });
}

export const bridge = { send, on, off, once, log, request, mintId, get initialized() { return initialized; } };
export default bridge;
