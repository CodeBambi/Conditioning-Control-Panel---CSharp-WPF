/* ============================================================================
 * bridge.js - the postMessage contract with the WPF host (ChaosWebViewHost /
 * DtrhHostService). Protocol v1.
 *
 * Transport: window.chrome.webview.postMessage (JS->C#) and the host's
 * PostWebMessageAsJson (C#->JS). The host queues everything until we announce
 * 'ready', and we buffer any message that lands before its handler is
 * registered - so neither side ever races the other's boot.
 * ----------------------------------------------------------------------------
 * PRODUCT DERIVATIVE (SP-023): payload blob 13af3f4d + the dtrh-admission.md
 * §3.1 minimal transport-only diff, and NOTHING else:
 *   (a) import-time isHosted also accepts invokeCSharpAction (WebKitGTK);
 *   (b) send(): chrome.webview -> native postMessage(object); else
 *       invokeCSharpAction(JSON.stringify(msg)) - page owns stringify;
 *   (c) receive: chrome.webview listener unchanged (Windows synthetic
 *       MessageEvent dispatch); else long-poll GET /bridge/<token>/inbox
 *       on the page origin (token from location), same dispatch+preBuffer.
 * ==========================================================================*/

export const PROTOCOL = 1;

const webview = window.chrome && window.chrome.webview;
const handlers = new Map();   // type -> fn
const preBuffer = [];         // messages that arrived before their handler

const dispatch = (m) => {
  if (!m || typeof m.type !== 'string') return;
  const h = handlers.get(m.type);
  if (h) h(m);
  else preBuffer.push(m);
};

if (webview) {
  webview.addEventListener('message', (e) => dispatch(e.data));
}

// Host->page, Linux (WebKitGTK NativeWebDialog has no chrome.webview):
// long-poll the host's retained inbox on the page origin (§3.3). The token
// rides in the navigated URL (?bridge=<token>); messages are sequence-numbered
// and retained until the next poll's `after` acknowledges them, so a late
// handler re-reads from its last seq - preBuffer-replay equivalence.
const bridgeToken = new URLSearchParams(location.search).get('bridge');
if (!webview && typeof invokeCSharpAction === 'function' && bridgeToken) {
  let after = 0;
  (async () => {
    for (;;) {
      try {
        const r = await fetch(`${location.origin}/bridge/${bridgeToken}/inbox?after=${after}`);
        if (!r.ok) { await new Promise(res => setTimeout(res, 1000)); continue; }
        const data = await r.json();
        for (const m of data.messages) { after = m.seq; dispatch(m.body); }
      } catch (e) {
        await new Promise(res => setTimeout(res, 1000)); // host gone / restarting
      }
    }
  })();
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
  try {
    if (webview) webview.postMessage(msg);
    else if (typeof invokeCSharpAction === 'function') invokeCSharpAction(JSON.stringify(msg));
  } catch (e) { /* host gone */ }
}

export function log(msg) { send({ type: 'log', msg: String(msg).slice(0, 400) }); }

/** Announce boot completion - the host flushes its queued init/manifest on receipt. */
export function announceReady() { send({ type: 'ready', protocol: PROTOCOL }); }

// Import-time hosted detection (§3.1 rule 1 - load-bearing): WebView2 native
// object OR the WebKitGTK invokeCSharpAction global.
export const isHosted = !!webview || typeof invokeCSharpAction === 'function';
