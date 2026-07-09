/* ============================================================================
 * bridge.js - the postMessage contract with the WPF host (ChaosWebViewHost /
 * DtrhHostService). Protocol v1.
 *
 * Transport: window.chrome.webview.postMessage (JS->C#) and the host's
 * PostWebMessageAsJson (C#->JS). The host queues everything until we announce
 * 'ready', and we buffer any message that lands before its handler is
 * registered - so neither side ever races the other's boot.
 * ==========================================================================*/

export const PROTOCOL = 1;

const webview = window.chrome && window.chrome.webview;
const handlers = new Map();   // type -> fn
const preBuffer = [];         // messages that arrived before their handler

if (webview) {
  webview.addEventListener('message', (e) => {
    const m = e.data;
    if (!m || typeof m.type !== 'string') return;
    const h = handlers.get(m.type);
    if (h) h(m);
    else preBuffer.push(m);
  });
}

/** Register a handler; replays any buffered messages of that type in order. */
export function on(type, fn) {
  handlers.set(type, fn);
  for (let i = 0; i < preBuffer.length; i++) {
    if (preBuffer[i].type === type) {
      const m = preBuffer.splice(i, 1)[0];
      i--;
      fn(m);
    }
  }
}

/** Post a message object (must carry a string `type`) to the host. */
export function send(msg) {
  try { webview && webview.postMessage(msg); } catch (e) { /* host gone */ }
}

export function log(msg) { send({ type: 'log', msg: String(msg).slice(0, 400) }); }

/** Announce boot completion - the host flushes its queued init/manifest on receipt. */
export function announceReady() { send({ type: 'ready', protocol: PROTOCOL }); }

export const isHosted = !!webview;
