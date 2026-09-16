/* ============================================================================
 * bridge.js - the postMessage contract with the WPF host
 * (ChaosWebViewHost / PieceByPieceHostService).
 *
 * Transport: window.chrome.webview.postMessage (JS->C#) and the host's
 * PostWebMessageAsJson (C#->JS). Every export here is a SAFE NO-OP when there
 * is no host - the page is meant to run from a plain dev server too, and
 * `isHosted` is the one thing to branch on if a caller needs to know.
 *
 * The four messages the board itself needs:
 *
 *   host -> page   { type: 'pbp:settings', videoHoldSec, reducedMotion }   once
 *   page -> host   { type: 'pbp:media-request', kinds: [...], count }
 *   host -> page   { type: 'pbp:media', images: [...], gifs: [...], videos: [...] }
 *   page -> host   { type: 'pbp:exit' }
 *
 * plus the shell conventions the host also speaks: `ready` (which is what
 * makes it flush its queued settings frame), a `heartbeat` every couple of
 * seconds, and a `pong` answer to its `ping`.
 *
 * ONLINE PLAY adds three more, and they are a set: one that hands the page an
 * identity, and a request/reply pair that carries HTTP for it.
 *
 *   host -> page   { type: 'pbp:identity', unifiedId, displayName, appVersion,
 *                    online, net: { serverBase, authToken, viaHost } }   once
 *   page -> host   { type: 'pbp:net', id, method, path, body }
 *   host -> page   { type: 'pbp:net-result', id, status, body }
 *
 * THE TOKEN IS NEVER PERSISTED. It arrives in `pbp:identity`, lives in the
 * module-scope `netCfg` below for as long as the document does, and is never
 * written to localStorage, sessionStorage, the URL or the DOM. `netInfo()` is
 * deliberately unable to hand it back - it reports `hasToken`, not the token -
 * so nothing downstream can lift it out and keep it.
 *
 * AND THE PAGE WOULD RATHER NOT HOLD IT AT ALL. `viaHost` (true from the
 * desktop host) routes every call back through C#, which attaches
 * X-Auth-Token itself against a path whitelist; the token in the frame is only
 * there so a future direct-fetch build has it once CORS for the ccp.game
 * origin is deployed. This mirrors goon/bridge.js exactly - same reasoning,
 * same shape, one letter of difference in the frame names.
 *
 * Neither side can race the other's boot: the host queues everything until
 * `ready`, and anything that lands here before a listener is registered is
 * buffered and replayed in arrival order.
 * ==========================================================================*/

// Read through a guarded global so this module can be IMPORTED under node - the
// smoke runs pull net/api.js in, and api.js pulls this in. Nothing here runs
// without a host anyway; the guard only stops the reference itself throwing.
const win = (typeof window !== 'undefined') ? window : null;
const webview = (win && win.chrome && win.chrome.webview) || null;

/** True when the page is running inside the desktop host. */
export const isHosted = !!webview;

const listeners = [];     // fn(msg)
const internals = [];     // fn(msg), bridge-owned, never buffered - see the pump
const preBuffer = [];     // messages that arrived before any listener existed
let heartbeat = 0;        // setInterval handle, 0 when not beating

if (webview) {
  webview.addEventListener('message', (e) => {
    const m = e && e.data;
    if (!m || typeof m.type !== 'string') return;
    // The host's liveness probe. Answered here rather than left to a caller:
    // a missed pong closes the window, and no feature should be able to
    // forget about it.
    if (m.type === 'ping') { postToHost({ type: 'pong' }); return; }
    // Bridge-owned frames first, and NEVER through the pre-buffer: the buffer
    // drains into whoever registers first, so an internal handler that took
    // part in it would quietly eat the settings frame boot.js is waiting for.
    for (const fn of internals) { try { fn(m); } catch (err) { console.warn('[pbp] bridge handler threw', err); } }
    if (listeners.length === 0) { preBuffer.push(m); return; }
    deliver(m);
  });
}

function deliver(m) {
  for (const fn of listeners) {
    try { fn(m); } catch (err) { console.warn('[pbp] host message handler threw', err); }
  }
}

/**
 * Post a message object (it must carry a string `type`) to the host.
 * No-op, never a throw, when there is no host or the host has gone away.
 */
export function postToHost(msg) {
  if (!webview) return;
  try { webview.postMessage(msg); } catch (err) { /* host gone; nothing to do */ }
}

/**
 * Listen for host -> page messages. Returns an unsubscribe function.
 * The first listener to register drains anything that arrived before it.
 */
export function onHostMessage(fn) {
  if (typeof fn !== 'function') return () => {};
  listeners.push(fn);
  if (preBuffer.length) {
    const queued = preBuffer.splice(0, preBuffer.length);
    for (const m of queued) deliver(m);
  }
  return () => {
    const i = listeners.indexOf(fn);
    if (i >= 0) listeners.splice(i, 1);
  };
}

/**
 * Announce boot completion, then keep beating. The host holds its `pbp:settings`
 * frame until this lands, and its heartbeat watchdog only starts counting once
 * it has - so this is what turns the bridge on in both directions.
 */
export function signalReady() {
  postToHost({ type: 'ready' });
  if (!webview || heartbeat) return;
  heartbeat = setInterval(() => postToHost({ type: 'heartbeat' }), 2000);
}

/**
 * Ask the host for media from the player's own library. Kinds are any of
 * 'image', 'gif', 'video'; the reply is one `pbp:media` message carrying an
 * array per kind, and ANY of those arrays may be empty - a fresh install has
 * no media at all.
 */
export function requestMedia(kinds, count) {
  postToHost({
    type: 'pbp:media-request',
    kinds: Array.isArray(kinds) && kinds.length ? kinds : ['image', 'gif', 'video'],
    count: Number(count) > 0 ? Math.floor(Number(count)) : 24,
  });
}

/** Ask the host to close the window. */
export function exitToHost() {
  postToHost({ type: 'pbp:exit' });
}

/* ----------------------------------------------------------------------------
 * IDENTITY, and the net lane it opens.
 *
 * Everything about who this player is, and how a call reaches the server,
 * lives in this one closure. Nothing else in the page ever sees the token.
 * -------------------------------------------------------------------------- */

let netCfg = { serverBase: '', authToken: '', viaHost: true };
let identityCfg = { unifiedId: '', displayName: '', appVersion: '', online: false };
const identityListeners = [];
let identitySeen = false;

/**
 * Who is playing, minus the secret. Always an object, so a caller never has to
 * null-check: before the host has spoken (or with no host at all) it reads
 * `online: false` and an empty id, which is exactly the state an offline page
 * should see.
 */
export function identity() {
  return {
    unifiedId: identityCfg.unifiedId,
    displayName: identityCfg.displayName,
    appVersion: identityCfg.appVersion,
    online: identityCfg.online,
  };
}

/**
 * Fires when the host hands the page its identity, and immediately if it
 * already has. Returns an unsubscribe.
 */
export function onIdentity(fn) {
  if (typeof fn !== 'function') return () => {};
  identityListeners.push(fn);
  if (identitySeen) { try { fn(identity()); } catch (err) { console.warn('[pbp] identity handler threw', err); } }
  return () => {
    const i = identityListeners.indexOf(fn);
    if (i >= 0) identityListeners.splice(i, 1);
  };
}

/**
 * Resolve once the host has handed over an identity, or after `timeoutMs` with
 * whatever we have (which, unhosted, is the empty one and always will be).
 *
 * There is a real race behind this. The host posts `pbp:identity` right after
 * `ready`, but the online lane is imported lazily from a `.then()` chain, so it
 * can arrive on either side of that frame. Awaiting is what makes the two
 * orders identical, instead of leaving one of them to fail its first call.
 */
export function whenIdentity(timeoutMs = 3000) {
  if (identitySeen || !webview) return Promise.resolve(identity());
  return new Promise((resolve) => {
    let done = false;
    let off = () => {};
    const finish = () => { if (done) return; done = true; off(); resolve(identity()); };
    off = onIdentity(finish);
    const t = setTimeout(finish, Math.max(0, timeoutMs));
    if (t && t.unref) t.unref();
  });
}

/**
 * Point the net lane at a server by hand. The host frame calls this for you;
 * a standalone dev page (or a test) is the only other caller. Returns the
 * readable half of the config, never the token.
 */
export function configureNet(net) {
  const n = net || {};
  netCfg = {
    serverBase: String(n.serverBase || '').replace(/\/+$/, ''),
    authToken: String(n.authToken || ''),
    // Hosted, "route through C#" is the default and the only tested path.
    // Standalone there is no host to route through, so it can only be direct.
    viaHost: isHosted ? (n.viaHost !== false) : false,
  };
  return netInfo();
}

/** A readable view of the net lane. Reports that a token exists, never what it is. */
export function netInfo() {
  return {
    serverBase: netCfg.serverBase,
    viaHost: netCfg.viaHost,
    hasToken: !!netCfg.authToken,
    pending: netPending.size,
  };
}

const netPending = new Map();   // id -> { resolve, timer }
let netSeq = 0;

/**
 * Long enough to sit through a 25s event long-poll and still be a timeout
 * rather than a hang. The host's own HttpClient deadline is shorter, so in
 * practice this only ever fires when the host stopped answering at all.
 */
const NET_TIMEOUT_MS = 45000;

if (webview) {
  internals.push((m) => {
    if (m.type === 'pbp:identity') {
      identityCfg = {
        unifiedId: String(m.unifiedId || ''),
        displayName: String(m.displayName || ''),
        appVersion: String(m.appVersion || ''),
        online: m.online !== false,
      };
      configureNet(m.net);
      identitySeen = true;
      const snap = identity();
      for (const fn of identityListeners.slice()) {
        try { fn(snap); } catch (err) { console.warn('[pbp] identity handler threw', err); }
      }
      return;
    }
    if (m.type !== 'pbp:net-result') return;
    const p = netPending.get(m.id);
    if (!p) return;                       // a late reply to a call we already gave up on
    netPending.delete(m.id);
    try { clearTimeout(p.timer); } catch (err) { /* already fired */ }
    p.resolve({ status: m.status | 0, body: typeof m.body === 'string' ? m.body : '' });
  });
}

/**
 * One HTTP call. Resolves `{ status, body }` and NEVER rejects: status 0 means
 * "no answer at all" (offline, host gone, timed out), which every call site can
 * branch on without a try/catch. `path` carries its own query string.
 *
 * Hosted, this hands the call to C#, which is what attaches X-Auth-Token and
 * refuses any path outside the game's own prefix. Standalone it is a plain
 * fetch with whatever configureNet was given.
 */
export function netRequest(method, path, body) {
  const verb = String(method || 'GET').toUpperCase();
  const p = String(path || '');
  if (isHosted && netCfg.viaHost) {
    const id = 'n' + (++netSeq);
    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        netPending.delete(id);
        resolve({ status: 0, body: '' });
      }, NET_TIMEOUT_MS);
      netPending.set(id, { resolve, timer });
      postToHost({ type: 'pbp:net', id, method: verb, path: p, body: body || null });
    });
  }
  const url = (netCfg.serverBase || '') + p;
  const headers = {};
  if (body !== null && body !== undefined) headers['Content-Type'] = 'application/json';
  if (netCfg.authToken) headers['X-Auth-Token'] = netCfg.authToken;
  if (identityCfg.unifiedId) headers['X-Unified-Id'] = identityCfg.unifiedId;
  let f = null;
  try { f = (typeof fetch === 'function') ? fetch : null; } catch (err) { f = null; }
  if (!f) return Promise.resolve({ status: 0, body: '' });
  const init = { method: verb, headers };
  if (body !== null && body !== undefined) init.body = JSON.stringify(body);
  return f(url, init)
    .then((r) => r.text().then(
      (t) => ({ status: r.status | 0, body: t }),
      () => ({ status: r.status | 0, body: '' }),
    ))
    .catch(() => ({ status: 0, body: '' }));
}
