/* ============================================================================
 * bridge.js - the postMessage contract with the WPF host
 * (ChaosWebViewHost / PieceByPieceHostService).
 *
 * Transport: window.chrome.webview.postMessage (JS->C#) and the host's
 * PostWebMessageAsJson (C#->JS). Every export here is a SAFE NO-OP when there
 * is no host - the page is meant to run from a plain dev server too, and
 * `isHosted` is the one thing to branch on if a caller needs to know.
 *
 * The four messages, and nothing else:
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
 * Neither side can race the other's boot: the host queues everything until
 * `ready`, and anything that lands here before a listener is registered is
 * buffered and replayed in arrival order.
 * ==========================================================================*/

const webview = (window.chrome && window.chrome.webview) || null;

/** True when the page is running inside the desktop host. */
export const isHosted = !!webview;

const listeners = [];     // fn(msg)
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
