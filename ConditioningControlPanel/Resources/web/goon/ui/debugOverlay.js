/* ============================================================================
 * ui/debugOverlay.js — the console a phone does not have.
 *
 * WHY THIS EXISTS. The page's worst failures are SILENT: a guest joins, the
 * lobby paints for a second, and the player is back on the title with nothing
 * written anywhere they can read. On the desktop that is one devtools tab away;
 * on an iPhone in a bedroom it is unfalsifiable. Hosted, `logger.warn/error`
 * tunnel to the C# log and the answer is in `logs/crash.log` — STANDALONE there
 * is no C# and no log, so the same lines go nowhere at all.
 *
 * So: a fixed, high-z, tap-to-collapse strip that mirrors the last MAX_LINES
 * warn/error lines, plus every window.onerror and unhandledrejection. Tiny,
 * monospace, no dependencies, no styling from goon.css (a broken stylesheet must
 * not be able to hide the thing that explains the break).
 *
 * HARD RULES
 *   1. NOTHING at module import touches document/window/localStorage — this file
 *      is swept by the node import test like every other module.
 *   2. It never loads unless asked: `?debug=1`. Standalone the answer is
 *      remembered in `goon.prefs` (bridge.js persists it next to server/token),
 *      so a reload — or a home-screen pin that dropped the query — keeps it on.
 *      HOSTED IT IS ALWAYS EXPLICIT: a WebView2 session must never grow a debug
 *      strip because a browser tab once had one.
 *   3. It never touches gameplay. One <div> appended to <body>, pointer-events
 *      on itself only, no listeners on anything it does not own, and every entry
 *      point is wrapped — a logger tee that can throw would turn a warning into
 *      a crash, which is the exact inversion of the point.
 * ==========================================================================*/

/** The querystring flag, and the key bridge.js parks it under in goon.prefs. */
export const DEBUG_PARAM = 'debug';
export const DEBUG_PREF_KEY = 'debug';
/** Ring size. Small on purpose: the last 50 lines are the ones that explain it. */
export const MAX_LINES = 50;
/** Above #gg-modal (70), above the mercy button, above anything a wave adds. */
export const OVERLAY_Z = 2147483000;

const ON = /^(1|true|on|yes)$/i;

/** Is the flag on in a raw querystring? `?debug=1`, `?debug` and `?debug=true` all count. */
export function debugInSearch(search) {
  const s = String(search || '');
  if (!s) return false;
  try {
    const q = new URLSearchParams(s.charAt(0) === '?' ? s.slice(1) : s);
    if (!q.has(DEBUG_PARAM)) return false;
    const v = q.get(DEBUG_PARAM);
    return v === '' || ON.test(String(v));
  } catch (_e) { return false; }
}

/**
 * The whole decision, as one pure function so it is testable under node.
 * @param {object} o
 * @param {string} [o.search] location.search
 * @param {object} [o.prefs] the parsed goon.prefs blob (standalone only)
 * @param {boolean} [o.hosted] inside WebView2
 */
export function debugRequested({ search = '', prefs = null, hosted = false } = {}) {
  if (debugInSearch(search)) return true;
  if (hosted) return false;                       // rule 2: hosted is explicit, always
  const v = prefs && typeof prefs === 'object' ? prefs[DEBUG_PREF_KEY] : undefined;
  return v === true || (typeof v === 'string' && ON.test(v));
}

function stamp(now) {
  const d = new Date(now);
  const p = (n, w) => String(n).padStart(w, '0');
  return `${p(d.getHours(), 2)}:${p(d.getMinutes(), 2)}:${p(d.getSeconds(), 2)}.${p(d.getMilliseconds(), 3)}`;
}

/** Flatten anything a logger, an ErrorEvent or a rejection reason can carry. */
export function describe(v) {
  if (v === null || v === undefined) return String(v);
  if (typeof v === 'string') return v;
  if (v instanceof Error || (v && typeof v.message === 'string' && typeof v.stack === 'string')) {
    return String(v.message) + (v.stack ? ' | ' + String(v.stack).split('\n').slice(1, 3).join(' <- ') : '');
  }
  try { return JSON.stringify(v); } catch (_e) { return String(v); }
}

/**
 * Build the strip. Returns a handle even when there is no DOM (node, or a page
 * that never asked for it) — every method is a no-op then, so callers never
 * branch on whether the overlay exists.
 *
 * @param {object} [o]
 * @param {Document} [o.doc] injectable for the self-test's stub DOM
 * @param {number} [o.max] ring size
 * @param {() => number} [o.now] injectable clock
 * @returns {{push:(level:string,msg:any)=>void, lines:()=>string[], collapsed:()=>boolean,
 *            toggle:()=>void, node:object|null, dispose:()=>void}}
 */
export function createDebugOverlay({ doc = null, max = MAX_LINES, now = () => Date.now() } = {}) {
  const d = doc || (typeof document !== 'undefined' ? document : null);
  const ring = [];
  let root = null;
  let body = null;
  let badge = null;
  let collapsed = false;
  let disposed = false;
  const cap = Math.max(1, max | 0);

  const noop = {
    push() {}, lines() { return []; }, collapsed() { return false; },
    toggle() {}, node: null, dispose() {},
  };
  if (!d || !d.body || typeof d.createElement !== 'function') return noop;

  const style = (node, css) => { try { node.setAttribute('style', css); } catch (_e) { /* stub DOM */ } };

  try {
    root = d.createElement('div');
    root.id = 'gg-debug';
    style(root,
      'position:fixed;left:0;bottom:0;z-index:' + OVERLAY_Z + ';max-width:100vw;'
      + 'font:10px/1.35 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;'
      + 'color:#ffd7f2;background:rgba(10,6,20,.86);border-top:1px solid rgba(255,105,180,.55);'
      + 'pointer-events:auto;-webkit-user-select:text;user-select:text;');

    badge = d.createElement('div');
    badge.id = 'gg-debug-badge';
    style(badge,
      'padding:4px 8px;background:rgba(255,105,180,.22);letter-spacing:.06em;'
      + 'cursor:pointer;touch-action:manipulation;');
    badge.textContent = 'debug 0';

    body = d.createElement('div');
    body.id = 'gg-debug-body';
    // 30vh, and the badge is the TOP row so one tap folds it to a 16px bar: a
    // diagnostic strip that covers the button you have to press to reproduce the
    // bug is a strip that prevents its own use.
    style(body, 'max-height:30vh;overflow:auto;padding:4px 8px 6px;white-space:pre-wrap;word-break:break-word;');

    root.appendChild(badge);
    root.appendChild(body);
    // TAP ANYWHERE ON THE STRIP, not just a hit-box: a 10px close button on a
    // phone is a control that does not exist.
    if (typeof root.addEventListener === 'function') root.addEventListener('click', () => api.toggle());
    d.body.appendChild(root);
  } catch (_e) {
    return noop;                                  // a strip that cannot build is simply absent
  }

  function paintBadge() {
    try { badge.textContent = 'debug ' + ring.length + (collapsed ? ' ▴' : ' ▾'); }
    catch (_e) { /* stub DOM */ }
  }

  function paint(entry) {
    try {
      const row = d.createElement('div');
      style(row, 'color:' + (entry.level === 'error' ? '#ff8a8a' : entry.level === 'warn' ? '#ffd27f' : '#c9c9ff'));
      row.textContent = entry.at + ' ' + entry.level + ' ' + entry.msg;
      body.appendChild(row);
      while (body.childNodes && body.childNodes.length > cap) body.removeChild(body.childNodes[0]);
      if (typeof body.scrollHeight === 'number') body.scrollTop = body.scrollHeight;
    } catch (_e) { /* stub DOM */ }
    paintBadge();
  }

  const api = {
    /** One line. NEVER throws — this sits inside logger.warn/error. */
    push(level, msg) {
      if (disposed) return;
      try {
        const entry = { at: stamp(now()), level: String(level || 'log'), msg: describe(msg).slice(0, 400) };
        ring.push(entry);
        while (ring.length > cap) ring.shift();
        paint(entry);
      } catch (_e) { /* a log line can never be the thing that breaks the page */ }
    },
    lines() { return ring.map((e) => e.at + ' ' + e.level + ' ' + e.msg); },
    collapsed() { return collapsed; },
    toggle() {
      collapsed = !collapsed;
      try { body.hidden = collapsed; } catch (_e) { /* stub DOM */ }
      paintBadge();
    },
    get node() { return root; },
    dispose() {
      if (disposed) return;
      disposed = true;
      try { root.remove(); } catch (_e) { /* already gone */ }
    },
  };

  paintBadge();
  return api;
}

/**
 * Catch what never reaches a logger: a throw out of an event handler and a
 * promise nobody awaited. boot.js already forwards both to bridge.log, which is
 * a C# tunnel that goes nowhere standalone — this is the same two seams, ending
 * somewhere a phone can actually read.
 * @returns {() => void} unsubscribe
 */
export function captureGlobalErrors(overlay, win = (typeof window !== 'undefined' ? window : null)) {
  if (!overlay || !win || typeof win.addEventListener !== 'function') return () => {};
  const onError = (e) => {
    const where = e && e.filename ? ' @ ' + String(e.filename).split('/').pop() + ':' + e.lineno : '';
    overlay.push('error', ((e && e.message) || 'script error') + where);
  };
  const onReject = (e) => overlay.push('error', 'promise: ' + describe(e && e.reason));
  try {
    win.addEventListener('error', onError);
    win.addEventListener('unhandledrejection', onReject);
  } catch (_e) { return () => {}; }
  return () => {
    try { win.removeEventListener('error', onError); } catch (_e) { /* ignore */ }
    try { win.removeEventListener('unhandledrejection', onReject); } catch (_e) { /* ignore */ }
  };
}

export default { createDebugOverlay, captureGlobalErrors, debugRequested, debugInSearch };
