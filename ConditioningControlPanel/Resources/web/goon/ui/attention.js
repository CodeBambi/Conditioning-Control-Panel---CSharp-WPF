/* ============================================================================
 * ui/attention.js — the no-cam attention module: a countdown ring, the last five
 * checks as dots, and the CHECK TOKEN itself.
 *
 * v1 ships without a camera, so "attention" is an interaction check: every
 * ~90 s the engine raises interactionCheckDue and a tap circle appears somewhere
 * safe. Tap it and you keep your multiplier; let the grace ring empty and the
 * engine drops you to x0.6 for a minute.
 *
 * WHO OWNS THE TIMEOUT: the engine does NOT. GoonMatchService only raises the
 * due event and waits for reportInteractionCheck(bool) — so the grace clock, and
 * the failure report when it runs out, live HERE. (Cadence mirrors the engine's
 * private NO_CAM_CHECK_INTERVAL_MS; it is not exported, so the ring is an
 * estimate that re-bases on every real due event.)
 *
 * Node-import-safe: no DOM at import, only inside mountAttention().
 * ==========================================================================*/

import { GoonAttentionMode } from '../core/contracts.js';

/** Mirrors match.js's private NO_CAM_CHECK_INTERVAL_MS. */
const CHECK_INTERVAL_MS = 90000;
/** How long the token stays tappable before it counts as missed. */
const GRACE_MS = 12000;
/** The "you missed one" banner dwell. */
const BANNER_MS = 3000;
/** Token diameter and the exclusion radius around the panels it must never cover. */
const TOKEN_PX = 88;
const KEEP_CLEAR_PX = 120;
const HISTORY = 5;

const doc = () => (typeof document !== 'undefined' ? document : null);

function el(tag, cls, text) {
  const d = doc();
  if (!d || typeof d.createElement !== 'function') return null;
  const n = d.createElement(tag);
  if (cls && n) n.className = cls;
  if (text != null && n) n.textContent = String(text);
  return n;
}

function add(parent, child) {
  if (parent && child && typeof parent.appendChild === 'function') parent.appendChild(child);
  return child;
}

function cls(node, name, on) {
  if (!node || !node.classList) return;
  try { node.classList[on ? 'add' : 'remove'](name); } catch (_e) { /* stub DOM */ }
}

function text(node, value) {
  if (node) node.textContent = value == null ? '' : String(value);
}

function sfx(audio, id) {
  try { if (audio && typeof audio.sfx === 'function') audio.sfx(id); } catch (_e) { /* stub */ }
}

function rectOf(node) {
  try {
    if (node && typeof node.getBoundingClientRect === 'function') {
      const r = node.getBoundingClientRect();
      if (r && typeof r.left === 'number') return r;
    }
  } catch (_e) { /* stub DOM */ }
  return null;
}

function createLedger() {
  const list = [];
  return {
    add(fn) { if (typeof fn === 'function') list.push(fn); },
    listen(target, type, fn, opts) {
      if (!target || typeof target.addEventListener !== 'function') return;
      target.addEventListener(type, fn, opts);
      list.push(() => { try { target.removeEventListener(type, fn, opts); } catch (_e) { /* gone */ } });
    },
    interval(ms, fn) {
      if (typeof setInterval !== 'function') return 0;
      const id = setInterval(fn, ms);
      list.push(() => { try { clearInterval(id); } catch (_e) { /* gone */ } });
      return id;
    },
    run() { while (list.length) { const fn = list.pop(); try { fn(); } catch (_e) { /* keep unwinding */ } } },
  };
}

function viewport() {
  const w = (typeof window !== 'undefined' && window && window.innerWidth) || 1280;
  const h = (typeof window !== 'undefined' && window && window.innerHeight) || 720;
  return { w, h };
}

/**
 * @param {object}   o
 * @param {Element}  o.host        the top-bar slot (ring + dots live here)
 * @param {Element}  [o.tokenHost] full-bleed layer the token is placed in
 * @param {object}   o.match
 * @param {object}   [o.audio]
 * @param {Function} [o.getAvoid]  () => Element[] the token must not land on
 * @param {Function} [o.onLog]
 */
export function mountAttention({ host, tokenHost = null, match, audio = null, getAvoid = null, onLog = null } = {}) {
  const led = createLedger();
  const root = el('div', 'gg-att gg-plate');
  if (!root || !host) return { unmount() { led.run(); }, spawn() {} };

  const ring = add(root, el('div', 'gg-att-ring'));
  const ringNum = add(ring, el('span', 'gg-att-ring-num', '—'));
  const col = add(root, el('div', 'gg-att-col'));
  const label = add(col, el('div', 'gg-att-label', 'next check ~90s'));
  const dots = add(col, el('div', 'gg-att-dots'));
  const dotEls = [];
  for (let i = 0; i < HISTORY; i++) dotEls.push(add(dots, el('i', 'gg-att-dot')));
  add(host, root);

  const layer = tokenHost || host;
  const banner = el('div', 'gg-att-banner');
  if (banner) banner.hidden = true;
  add(layer, banner);

  let nextAtElapsed = CHECK_INTERVAL_MS;
  let token = null;
  let tokenTimer = 0;
  let bannerTimer = 0;
  const history = [];   // true = held, false = missed (newest last)

  function elapsed() {
    try { return (match && typeof match.liveElapsedMs === 'number') ? match.liveElapsedMs : 0; } catch (_e) { return 0; }
  }

  function isNoCam() {
    const m = match && match.localAttentionMode;
    return m === undefined || m === null || m === GoonAttentionMode.NoCam;
  }

  function paint() {
    root.hidden = !isNoCam();
    if (root.hidden) return;
    const left = Math.max(0, nextAtElapsed - elapsed());
    const secs = Math.ceil(left / 1000);
    text(label, token ? 'check due now' : 'next check ~' + secs + 's');
    text(ringNum, token ? 'tap' : String(secs));
    const pct = Math.max(0, Math.min(100, 100 - (left / CHECK_INTERVAL_MS) * 100));
    if (ring && ring.style && ring.style.setProperty) ring.style.setProperty('--gg-ring', pct.toFixed(1) + '%');
    cls(ring, 'is-due', !!token);

    for (let i = 0; i < dotEls.length; i++) {
      const v = history[history.length - dotEls.length + i];
      cls(dotEls[i], 'is-ok', v === true);
      cls(dotEls[i], 'is-bad', v === false);
    }
  }

  // ------------------------------------------------------------- the token

  /** A point that is inside the stage band and clear of mercy / rails / dial. */
  function placeToken() {
    const { w, h } = viewport();
    const avoid = [];
    try {
      const list = typeof getAvoid === 'function' ? getAvoid() : [];
      for (const node of list || []) { const r = rectOf(node); if (r && r.width) avoid.push(r); }
    } catch (_e) { /* place blind rather than not at all */ }

    const minX = w * 0.20;
    const maxX = w * 0.80 - TOKEN_PX;
    const minY = h * 0.22;
    const maxY = h * 0.62 - TOKEN_PX;

    for (let attempt = 0; attempt < 14; attempt++) {
      const x = minX + Math.random() * Math.max(1, maxX - minX);
      const y = minY + Math.random() * Math.max(1, maxY - minY);
      let clear = true;
      for (const r of avoid) {
        const cx = x + TOKEN_PX / 2;
        const cy = y + TOKEN_PX / 2;
        const dx = Math.max(r.left - cx, 0, cx - r.right);
        const dy = Math.max(r.top - cy, 0, cy - r.bottom);
        if (Math.sqrt(dx * dx + dy * dy) < KEEP_CLEAR_PX) { clear = false; break; }
      }
      if (clear) return { x, y };
    }
    return { x: (w - TOKEN_PX) / 2, y: h * 0.34 };
  }

  function spawn() {
    if (token || !isNoCam()) return;
    const node = el('button', 'gg-att-token');
    if (!node) return;
    node.type = 'button';
    add(node, el('i', 'gg-att-token-ring'));
    add(node, el('span', 'gg-att-token-word', 'tap'));
    const at = placeToken();
    if (node.style) {
      node.style.left = Math.round(at.x) + 'px';
      node.style.top = Math.round(at.y) + 'px';
      node.style.setProperty && node.style.setProperty('--gg-grace', GRACE_MS + 'ms');
    }
    add(layer, node);
    token = node;
    sfx(audio, 'gg-check');
    if (typeof requestAnimationFrame === 'function') requestAnimationFrame(() => cls(node, 'is-live', true));
    else cls(node, 'is-live', true);

    led.listen(node, 'pointerdown', (e) => { if (e && e.preventDefault) e.preventDefault(); resolve(true); });
    tokenTimer = setTimeout(() => resolve(false), GRACE_MS);
    led.add(() => { try { clearTimeout(tokenTimer); } catch (_e) { /* gone */ } });
    paint();
  }

  function resolve(passed) {
    if (!token) return;
    try { clearTimeout(tokenTimer); } catch (_e) { /* gone */ }
    const node = token;
    token = null;
    cls(node, 'is-live', false);
    cls(node, passed ? 'is-held' : 'is-missed', true);
    setTimeout(() => { try { node.remove(); } catch (_e) { /* gone */ } }, 260);

    try { if (match && typeof match.reportInteractionCheck === 'function') match.reportInteractionCheck(!!passed); }
    catch (_e) { /* the engine is allowed to be gone */ }

    history.push(!!passed);
    while (history.length > HISTORY) history.shift();
    if (typeof onLog === 'function') { try { onLog({ t: 'check', passed: !!passed }); } catch (_e) { /* ignore */ } }

    if (passed) {
      sfx(audio, 'gg-check-ok');
      chip('focus held');
    } else {
      showBanner('missed check — ×0.6 for 60 seconds.');
    }
    paint();
  }

  function chip(msg) {
    const node = el('div', 'gg-att-chip', msg);
    if (!node) return;
    add(layer, node);
    setTimeout(() => { try { node.remove(); } catch (_e) { /* gone */ } }, 1800);
  }

  function showBanner(msg) {
    if (!banner) return;
    text(banner, msg);
    banner.hidden = false;
    cls(banner, 'is-in', true);
    try { clearTimeout(bannerTimer); } catch (_e) { /* gone */ }
    bannerTimer = setTimeout(() => { cls(banner, 'is-in', false); banner.hidden = true; }, BANNER_MS);
    led.add(() => { try { clearTimeout(bannerTimer); } catch (_e) { /* gone */ } });
  }

  // ------------------------------------------------------------- wiring

  if (match && typeof match.onInteractionCheckDue === 'function') {
    const off = match.onInteractionCheckDue(() => {
      nextAtElapsed = elapsed() + CHECK_INTERVAL_MS;   // re-base off the real cadence
      spawn();
    });
    led.add(typeof off === 'function' ? off : null);
  }
  led.interval(500, () => { try { paint(); } catch (_e) { /* never break the HUD */ } });
  paint();

  return {
    spawn,
    unmount() {
      try { clearTimeout(tokenTimer); } catch (_e) { /* gone */ }
      led.run();
      if (token) { try { token.remove(); } catch (_e) { /* gone */ } token = null; }
      try { banner && banner.remove(); } catch (_e) { /* gone */ }
      try { root.remove(); } catch (_e) { /* gone */ }
    },
  };
}
