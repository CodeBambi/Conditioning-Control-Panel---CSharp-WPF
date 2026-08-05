/* ============================================================================
 * bridge.js — the postMessage contract with the WPF host (GoonHostService) for
 * the Goon Game page. Protocol v1.
 *
 * ONE module, TWO worlds, ONE interface (the dtrh/bridge.js core plus the
 * intake/web-shim.js dual path):
 *   - HOSTED (WebView2, https://ccp.game/goon/index.html): C# posts frames with
 *     PostWebMessageAsJson, we post back with window.chrome.webview.postMessage.
 *     The host queues everything until we announce 'ready'; we pre-buffer any
 *     frame that lands before its handler is registered. Neither side races.
 *   - STANDALONE (a plain browser, for dev/play-testing): no C#. We SYNTHESIZE
 *     the host's `init` (+ an empty `manifest`) from the querystring and
 *     localStorage and deliver them through the SAME handler path, so boot.js
 *     and every screen after it have exactly ONE code path.
 *
 * Wire (v1):
 *   Host -> Page : init · manifest · fullscreen · ping · end-run ·
 *                  haptics-state · net-post-result
 *   Page -> Host : ready · log · boot-error · heartbeat · pong · exit ·
 *                  exit-done · fullscreen-set · net-post
 *
 * HARD RULES (both learned the expensive way in intake/dtrh):
 *   1. NOTHING may throw at module import — WebView2 has no devtools, so a
 *      throw is a silent infinite loader. Every window/document/location/
 *      localStorage touch below is guarded or lazy.
 *   2. ONE handler per message type. A second on('x') THROWS here (intake let
 *      the second silently displace the first, which cost days).
 * ==========================================================================*/

export const PROTOCOL = 1;

/**
 * THE BUILD STAMP — bump it in every PR that touches this folder.
 *
 * Two devices, one robocopy mirror, and iOS Safari's cache made "which build is
 * that phone actually running?" an unanswerable question for three play-tests
 * in a row (the 2026-08-05 transfer hunt spent its first sessions on exactly
 * that). The stamp closes the hole from both ends: boot.js logs it through
 * bridge.log (so the C# app log and the ?debug=1 overlay both carry it) and
 * the title screen prints it under the fineprint, so ONE GLANCE at either
 * device answers the question. Format: r<round>-<yyyymmdd>[letter].
 */
export const GOON_BUILD = 'r15-20260805';

const win = (typeof window !== 'undefined') ? window : null;
const webview = (win && win.chrome && win.chrome.webview) ? win.chrome.webview : null;

/** True inside the WebView2 host; false in a plain browser (and under node). */
export const isHosted = !!webview;

const handlers = new Map();   // type -> fn   (exactly one)
const preBuffer = [];         // frames that arrived before their handler

/** The single demux path — hosted frames AND synthesized standalone frames. */
function dispatch(m) {
  if (!m || typeof m.type !== 'string') return;
  const h = handlers.get(m.type);
  if (h) {
    try { h(m); } catch (e) { log('handler ' + m.type + ' threw: ' + (e && e.message || e)); }
  } else {
    preBuffer.push(m);
  }
}

if (webview) {
  try { webview.addEventListener('message', (e) => dispatch(e.data)); } catch (_e) { /* never throw at import */ }
}

/**
 * Register THE handler for a message type; replays any buffered frames of that
 * type in arrival order. Throws on a duplicate registration — loudly, at wiring
 * time, which is the only moment the mistake is cheap to see.
 */
export function on(type, fn) {
  if (typeof type !== 'string' || !type) throw new Error('bridge.on: bad type');
  if (typeof fn !== 'function') throw new Error('bridge.on: handler must be a function (' + type + ')');
  if (handlers.has(type)) {
    throw new Error('bridge.on: duplicate handler for "' + type + '" — one handler per type; fan out from the first');
  }
  handlers.set(type, fn);
  for (let i = 0; i < preBuffer.length; i++) {
    if (preBuffer[i].type === type) {
      const m = preBuffer.splice(i, 1)[0];
      i--;
      try { fn(m); } catch (e) { log('handler ' + type + ' threw (replay): ' + (e && e.message || e)); }
    }
  }
}

/** Drop a handler (the only sanctioned way to re-register a type). */
export function off(type) { return handlers.delete(type); }

/** Post a frame to the host (no-op standalone / under node). */
export function send(msg) {
  try { if (webview) webview.postMessage(msg); } catch (_e) { /* host gone */ }
}

/** Hosted: tunnel to the C# log (400-char clamp). Standalone: console. */
export function log(msg) {
  const s = String(msg).slice(0, 400);
  if (webview) send({ type: 'log', msg: s });
  else if (typeof console !== 'undefined') console.log('[goon]', s);
}

/** Announce boot completion — the host flushes its queued init/manifest. */
export function announceReady() { send({ type: 'ready', protocol: PROTOCOL }); }

/** Report a fatal boot failure (the host shows its own fallback UI). */
export function bootError(msg) { send({ type: 'boot-error', msg: String(msg).slice(0, 400) }); }

/* ----------------------------------------------------------------------------
 * NET — POST to the CCP server, either through the host (so the Patreon bearer
 * never lives in the page) or directly (standalone dev, or a host that hands us
 * the token and says "go ahead").
 *
 *   configureNet({serverBase, authToken, viaHost})  <- boot calls this from init
 *   postNet(path, body) -> Promise<{status:number, body:string}>
 *
 * NEVER rejects: a dead host or a dead network resolves {status:0, body:''} so
 * every call site can branch on status alone.
 * -------------------------------------------------------------------------- */
let netCfg = { serverBase: '', authToken: '', viaHost: true };
const pending = new Map();   // id -> {resolve, timer}
let netSeq = 0;

const NET_TIMEOUT_MS = 45000;

// The host's reply lands here, INSIDE the bridge — boot.js must not register
// 'net-post-result' (on() would throw). Registered at module scope: touches the
// handler Map only, so it is import-safe under node.
on('net-post-result', (m) => {
  const p = pending.get(m.id);
  if (!p) return;                       // late reply after our timeout — drop
  pending.delete(m.id);
  try { clearTimeout(p.timer); } catch (_e) { /* ignore */ }
  p.resolve({ status: m.status | 0, body: typeof m.body === 'string' ? m.body : '' });
});

/** Point the net helpers at a server. Called by boot.js from the `init` frame. */
export function configureNet(net) {
  const n = net || {};
  netCfg = {
    serverBase: String(n.serverBase || '').replace(/\/+$/, ''),
    authToken: String(n.authToken || ''),
    // Hosted default is "route through C#"; standalone can only ever be direct.
    viaHost: isHosted ? (n.viaHost !== false) : false,
  };
  return { serverBase: netCfg.serverBase, viaHost: netCfg.viaHost, hasToken: !!netCfg.authToken };
}

/** Read-only view of the net config (never exposes the token). */
export function netInfo() {
  return { serverBase: netCfg.serverBase, viaHost: netCfg.viaHost, hasToken: !!netCfg.authToken, pending: pending.size };
}

/**
 * POST JSON. Resolves {status, body} — status 0 means "never got an answer".
 *
 * `opts.noAuth` sends the call WITHOUT X-Auth-Token: the anonymous-guest path
 * (net/signaling.js), whose caller has no account token and whose only authority
 * is the room token in the body. Direct-fetch only, which is the only place it
 * can arise — hosted, the app always has an account and the C# side attaches the
 * header itself.
 * @param {{noAuth?: boolean}} [opts]
 */
export function postNet(path, body, opts) {
  const p = String(path || '');
  const noAuth = !!(opts && opts.noAuth);
  if (isHosted && netCfg.viaHost) {
    const id = 'n' + (++netSeq);
    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        pending.delete(id);
        log('net-post timeout: ' + p);
        resolve({ status: 0, body: '' });
      }, NET_TIMEOUT_MS);
      pending.set(id, { resolve, timer });
      send({ type: 'net-post', id, path: p, body: body || {} });
    });
  }
  // Direct fetch (standalone, or a host that opted out of proxying).
  const url = (netCfg.serverBase || '') + p;
  const headers = { 'Content-Type': 'application/json' };
  if (netCfg.authToken && !noAuth) headers['X-Auth-Token'] = netCfg.authToken;
  let f = null;
  try { f = (typeof fetch === 'function') ? fetch : null; } catch (_e) { f = null; }
  if (!f) return Promise.resolve({ status: 0, body: '' });
  return f(url, { method: 'POST', headers, body: JSON.stringify(body || {}) })
    .then((r) => r.text().then((t) => ({ status: r.status | 0, body: t })).catch(() => ({ status: r.status | 0, body: '' })))
    .catch((e) => { log('net-post failed: ' + p + ' — ' + (e && e.message || e)); return { status: 0, body: '' }; });
}

/* ----------------------------------------------------------------------------
 * STANDALONE BOOT SYNTHESIS — the dev path.
 *
 *   ?solo=1&mode=loopback&profile=p2p|relay|instant&skew=3517&name=A
 *   localStorage['goon.prefs'] = {...}   (merged under the querystring)
 *
 * Delivered as a real `init` frame (then an empty `manifest`) through dispatch()
 * on a microtask, so boot.js's handlers see the identical shape the host sends —
 * whether they were registered before or after this fires (pre-buffer).
 *
 * Gated on `document`: under node (import sweep) nothing is synthesized and
 * nothing runs, which keeps the modules provably side-effect free.
 * -------------------------------------------------------------------------- */
const PROFILES = ['p2p', 'relay', 'instant'];

function readPrefs() {
  try {
    const s = win && win.localStorage ? win.localStorage.getItem('goon.prefs') : null;
    const v = s ? JSON.parse(s) : null;
    return (v && typeof v === 'object') ? v : {};
  } catch (_e) { return {}; }
}

/**
 * The stored standalone blob, for anything that has to decide something BEFORE
 * the `init` frame is built (ui/debugOverlay.js asks whether the debug strip was
 * turned on in an earlier launch). Always an object; `{}` hosted or unreadable.
 */
export function storedPrefs() { return isHosted ? {} : readPrefs(); }

/** Persist standalone prefs (so a reload keeps your picks). No-op hosted. */
export function savePrefs(partial) {
  if (isHosted) return;
  try {
    const cur = readPrefs();
    win.localStorage.setItem('goon.prefs', JSON.stringify(Object.assign(cur, partial || {})));
  } catch (_e) { /* storage may be unavailable */ }
}

function numOr(v, d) { const n = parseFloat(v); return isFinite(n) ? n : d; }

/**
 * The API base a standalone launch falls back to when neither `?server=` nor a
 * stored pref names one. Non-empty ONLY on the public deployment (cclabs.app):
 * an invite link is a bare `?join=CODE`, and a page that cannot find its server
 * from that link alone is a dead end for the person it was built to welcome.
 * Everywhere else (localhost, file://, forks) stays '' — the serverless dev
 * playground, with its always-on transfer affordance, is keyed off "no server
 * anywhere" and a built-in default would quietly kill it. Exported for tests.
 */
export function defaultServerBase() {
  try {
    const host = (typeof location !== 'undefined' && location.hostname) || '';
    return /(^|\.)cclabs\.app$/i.test(host) ? 'https://codebambi-proxy.vercel.app' : '';
  } catch (_e) { return ''; }
}

/** Build the fake `init` a standalone browser boots from. Exported for tests. */
export function standaloneInit() {
  const q = new URLSearchParams((typeof location !== 'undefined' && location.search) || '');
  const prefs = readPrefs();
  const profile = q.get('profile') || prefs.profile || 'p2p';
  const name = q.get('name') || prefs.name || 'Solo';
  /* A REAL ACCOUNT ID, or ''. `?uid=` now, or one an earlier launch adopted into prefs. The
   * `local-…` fallback below is deliberately NOT one: it is a display convenience for the pure-dev
   * path, the server would refuse it, and treating it as an account is what would send an
   * invite-link visitor to a 401 instead of to a free guest seat. */
  const account = String(q.get('uid') || prefs.unifiedId || '');
  return {
    type: 'init',
    protocol: PROTOCOL,
    hosted: false,
    solo: q.get('solo') !== '0',
    // Field-for-field the host's shape (GoonHostService.OnPageReady) so the page
    // never branches on where the frame came from.
    identity: {
      // `?uid=` lets a phone/browser identify as a real account (paired with
      // `?token=` below) — without it the page plays as a throwaway local id.
      unifiedId: account || ('local-' + name),
      displayName: String(name).slice(0, 32),
      appVersion: 'dev',
      /**
       * NO ACCOUNT AT ALL — the invite-link click from somebody who has never
       * seen the app. `/join` then omits `unified_id` entirely and the server
       * mints a free `g_` guest seat (net/signaling.js `anonymous`). Joining is
       * free for everyone; only HOSTING is gated, and a guest never hosts.
       *
       * Hosted this field is simply absent: the C# host always has an account.
       */
      anonymous: !account,
    },
    caps: Object.assign({
      haptics: false,
      brainDrain: true,
      camera: false,
      video: true,
      platform: 'web',
      rounds: [0, 2, 3],       // QuickDrawLockCard · ReactionDuel · BubbleRace
      // There is no C# host out here, so there is no compression queue and no
      // transfer-cache to talk to: the assets screen renders "compression lives in
      // the app" instead of a spinner that never resolves.
      assetCache: false,
      /* SENDING IS FREE FOR EVERY SEAT (owner call, 2026-08-05). This WAS a
       * premium default — OFF against any real server, ON only on the pure-local
       * dev path — and boot.js flipped it on once the server's `media_send`
       * verdict landed. That defaulting is what a free guest experienced as
       * "my attacks throw blanks": the paid perk is HOSTING (caps.canHost, the
       * server's tier-2 bar at /v2/goon/invite) and once a room exists BOTH
       * players sending their own media is the whole shape of a duel.
       *
       * It stays TRUE here and the server's verdict stays the AUTHORITY: boot.js
       * still folds `media_send` in (adoptServerSendVerdict), so a server that
       * answers false turns sending off again with no client release, and a
       * server that predates the field (null) leaves this default alone. The
       * inversion matters — defaulting ON and letting the server veto is a very
       * different failure mode from defaulting OFF and hoping a verdict arrives
       * in time, and the second one is the bug this replaced.
       *
       * NOT an enforcement point either way: media is P2P, so this gate is
       * advisory UX (the lobby row's explanation, the queue's send arm). The
       * REAL gates are the two lobby consent toggles, which are untouched.
       */
      mediaTransfer: true,
    }, prefs.caps || {}),
    consent: Object.assign({
      liveDurationSec: 720,    // GoonConsts.LiveDurationSecDefault
      toyCap: 0,
      payloadMinGapMs: 30000,  // GoonConsts.PayloadMinGapMs
    }, prefs.consent || {}),
    match: {
      mode: q.get('mode') || prefs.mode || 'loopback',
      profile: PROFILES.indexOf(profile) >= 0 ? profile : 'p2p',
      skewMs: numOr(q.get('skew'), numOr(prefs.skewMs, 0)),
    },
    net: {
      serverBase: q.get('server') || prefs.serverBase || defaultServerBase(),
      authToken: q.get('token') || prefs.authToken || '',
      viaHost: false,
    },
    prefs,
    fullscreen: false,
  };
}

if (!isHosted && typeof document !== 'undefined') {
  // Defer a tick so a module that imports us can still register handlers first;
  // anything that lands early is pre-buffered anyway.
  Promise.resolve().then(() => {
    try {
      // Creds handed on the querystring are adopted into prefs so a later bare-URL
      // launch (or a home-screen pin that dropped the query) still boots with them.
      // Explicit params always win over stored ones inside standaloneInit itself.
      try {
        const q = new URLSearchParams(location.search || '');
        const keep = {};
        if (q.get('server')) keep.serverBase = q.get('server');
        if (q.get('token')) keep.authToken = q.get('token');
        if (q.get('uid')) keep.unifiedId = q.get('uid');
        if (q.get('name')) keep.name = q.get('name');
        // `?debug=1` sticks: a phone that reloads (or was pinned to the home
        // screen without the query) keeps the one surface that can explain what
        // just happened. `?debug=0` is how it comes back off.
        if (q.has('debug')) keep.debug = /^(1|true|on|yes)$/i.test(q.get('debug') || '1');
        if (Object.keys(keep).length) savePrefs(keep);
      } catch (_e) { /* prefs are a convenience, never load-bearing */ }
      dispatch(standaloneInit());
      dispatch({ type: 'manifest', images: [], videos: [], skipped: 0, truncated: false });
    } catch (e) {
      if (typeof console !== 'undefined') console.error('[goon] standalone init failed', e);
    }
  });
}
