/* ============================================================================
 * ui/opponent.js — the streamer-cam "monitor" that IS the opponent.
 *
 * Everything the other player lets us see lands here: their name, their charge
 * meter, their attention bar, the closeness they CLAIM, their emotes, and a
 * stylized miniature of their screen driven purely by the effect NAMES on their
 * state tick ("Flashes", "Bubbles", ...). The miniature is DOM/CSS only — it is
 * a caricature, never a stream: no frame of their machine ever crosses the wire.
 *
 * The monitor is also the payload DROP TARGET (ui/arsenal.js hit-tests against
 * the element this module exposes as `dropTarget`).
 *
 * TRUST: every remote-sourced string is written with textContent. The engine
 * already sanitized it; this module never builds markup from it either way.
 *
 * Node-import-safe: no DOM at import, only inside mountOpponent().
 * ==========================================================================*/

import { GoonConnectionHealth } from '../core/match.js';
import { GoonConsts, GoonElement, enumName } from '../core/contracts.js';

/** Closeness 0-3 -> the word that always rides with the colour. */
export const CLOSENESS_WORDS = Object.freeze(['steady', 'warm', 'close', 'edge']);

/** Emote bubble dwell. */
const EMOTE_MS = 4000;

/**
 * Miniature parts. `key` is the wire name on tick.active_effects; `anim` marks the
 * ones that carry motion — at most ANIM_BUDGET of those run at a time.
 */
const MINIS = Object.freeze([
  { key: 'Flashes', cls: 'gg-mini-flash', anim: true },
  { key: 'Bubbles', cls: 'gg-mini-bubbles', anim: true },
  { key: 'Videos', cls: 'gg-mini-video', anim: true },
  { key: 'BouncingText', cls: 'gg-mini-bounce', anim: true },
  { key: 'Subliminals', cls: 'gg-mini-sub', anim: true },
  { key: 'LockCards', cls: 'gg-mini-lock', anim: false },
  { key: 'ToyPatterns', cls: 'gg-mini-toy', anim: true },
  { key: 'BrainDrain', cls: 'gg-mini-drain', anim: false },
]);

const ANIM_BUDGET = 2;

// ------------------------------------------------------------------ helpers

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

/** Effect names arrive as strings from the wire; be forgiving about codes too. */
function effectName(v) {
  if (typeof v === 'string') return v;
  if (typeof v === 'number') return enumName(GoonElement, v);
  return '';
}

// ------------------------------------------------------------------ mount

/**
 * @param {object} o
 * @param {Element} o.host             where the monitor column is appended
 * @param {object}  o.match            GoonMatchService
 * @param {object}  [o.audio]          {sfx(id)}
 * @param {object}  [o.fx]             chrome-animation budget from ui/hud.js
 * @returns {{unmount:Function, root:Element|null, dropTarget:Element|null, showEmote:Function}}
 */
export function mountOpponent({ host, match, audio = null, fx = null } = {}) {
  const led = createLedger();
  const root = el('div', 'gg-mon');
  if (!root || !host) return { unmount() { led.run(); }, root: null, dropTarget: null, showEmote() {} };

  // ---- head: name · connection dot · their score · charge pips ------------
  const head = add(root, el('div', 'gg-mon-head'));
  const dot = add(head, el('i', 'gg-mon-dot'));
  const nameEl = add(head, el('span', 'gg-mon-name', 'opponent'));
  const scoreEl = add(head, el('span', 'gg-mon-score', '0'));
  const pipRow = add(head, el('span', 'gg-mon-pips'));
  const pips = [];
  for (let i = 0; i < GoonConsts.ChargeCap; i++) pips.push(add(pipRow, el('i', 'gg-pip gg-pip--sm')));

  // ---- the screen inside the bezel ---------------------------------------
  const frame = add(root, el('div', 'gg-mon-frame'));
  const screen = add(frame, el('div', 'gg-mon-screen'));
  const parts = new Map();
  for (const m of MINIS) {
    const node = add(screen, el('div', 'gg-mini ' + m.cls));
    if (m.key === 'Bubbles') for (let i = 0; i < 4; i++) add(node, el('i', 'gg-mini-bub'));
    if (m.key === 'Videos') for (let i = 0; i < 3; i++) add(node, el('i', 'gg-mini-bar'));
    if (m.key === 'Subliminals') add(node, el('span', 'gg-mini-line', 'deeper'));
    if (m.key === 'BouncingText') add(node, el('span', 'gg-mini-word', 'good girl'));
    if (m.key === 'LockCards') { add(node, el('i', 'gg-mini-lock-bar')); add(node, el('i', 'gg-mini-lock-bar')); }
    if (m.key === 'ToyPatterns') add(node, el('i', 'gg-mini-toy-dot'));
    parts.set(m.key, node);
  }
  const idle = add(screen, el('div', 'gg-mini-idle', 'quiet'));

  const bezel = el('img', 'gg-mon-bezel');
  if (bezel) {
    bezel.src = './assets/monitor_frame.png';
    bezel.alt = '';
    bezel.decoding = 'async';
    led.listen(bezel, 'error', () => { cls(frame, 'is-nobezel', true); try { bezel.remove(); } catch (_e) { /* gone */ } });
    add(frame, bezel);
  }

  // emote bubble — one at a time, textContent only
  const bubble = add(frame, el('div', 'gg-mon-bubble'));
  if (bubble) bubble.hidden = true;
  const bubbleIcon = add(bubble, el('span', 'gg-mon-bubble-icon'));
  const bubbleText = add(bubble, el('span', 'gg-mon-bubble-text'));

  // drop hint (arsenal flips this on while an item is armed / dragging)
  const hint = add(frame, el('div', 'gg-mon-hint', 'drop it here'));
  if (hint) hint.hidden = true;

  // ---- attention slim bar ------------------------------------------------
  const attWrap = add(root, el('div', 'gg-mon-att'));
  const attFill = add(attWrap, el('i', 'gg-mon-att-fill'));
  const attLabel = add(root, el('div', 'gg-mon-att-label', 'their focus 100%'));

  // ---- the closeness gauge: what they CLAIM -------------------------------
  const gauge = add(root, el('div', 'gg-mon-close'));
  const gaugeLabel = add(gauge, el('div', 'gg-mon-close-label', 'they claim'));
  const segRow = add(gauge, el('div', 'gg-mon-close-segs'));
  const segs = [];
  for (let i = 0; i < 4; i++) segs.push(add(segRow, el('i', 'gg-mon-close-seg')));
  const gaugeWord = add(gauge, el('div', 'gg-mon-close-word', 'unknown'));

  // ---- abandon countdown / connection word -------------------------------
  const foot = add(root, el('div', 'gg-mon-foot'));
  const connWord = add(foot, el('span', 'gg-mon-conn', 'live'));
  const abandonEl = add(foot, el('span', 'gg-mon-abandon'));
  if (abandonEl) abandonEl.hidden = true;

  add(host, root);

  // ------------------------------------------------------------- painting

  let lastCloseness = null;
  let lastHealth = GoonConnectionHealth.Fresh;
  let emoteTimer = 0;

  function health() {
    const op = match && match.opponent;
    return op ? (op.health | 0) : GoonConnectionHealth.Fresh;
  }

  function stalePrefix() { return health() === GoonConnectionHealth.Fresh ? '' : '~'; }

  function paintMinis(op) {
    const live = new Set();
    for (const raw of (op && op.activeEffects) || []) {
      const n = effectName(raw);
      if (n) live.add(n);
    }
    if (op && op.toyActive) live.add('ToyPatterns');

    let animating = 0;
    for (const m of MINIS) {
      const node = parts.get(m.key);
      if (!node) continue;
      const on = live.has(m.key);
      cls(node, 'is-on', on);
      // Motion budget: only the first ANIM_BUDGET animated minis actually move.
      const moving = on && m.anim && animating < ANIM_BUDGET;
      if (moving) animating++;
      cls(node, 'is-anim', moving);
    }
    if (idle) idle.hidden = live.size > 0;
  }

  function paintCloseness(op) {
    const v = op && op.closeness;
    const known = v !== null && v !== undefined;
    for (let i = 0; i < segs.length; i++) cls(segs[i], 'is-lit', known && i <= v);
    cls(gauge, 'is-edge', known && v === 3);
    gauge && gauge.setAttribute && gauge.setAttribute('data-gg-close', known ? String(v) : 'none');
    text(gaugeWord, known ? CLOSENESS_WORDS[v] : 'no word yet');

    if (known && v !== lastCloseness) {
      const runIt = () => {
        cls(gauge, 'is-sweep', true);
        setTimeout(() => cls(gauge, 'is-sweep', false), 320);
      };
      if (fx && typeof fx.play === 'function') fx.play(320, runIt); else runIt();
      if (lastCloseness !== null) sfx(audio, 'gg-taunt-up');
    }
    lastCloseness = known ? v : null;
  }

  function paint() {
    const op = (match && match.opponent) || null;
    if (!op) return;
    const p = stalePrefix();

    text(nameEl, op.displayName || 'opponent');
    text(scoreEl, p + String(op.score | 0));
    for (let i = 0; i < pips.length; i++) cls(pips[i], 'is-on', i < (op.charges | 0));

    const pct = Math.max(0, Math.min(100, Number(op.attentionPct) || 0));
    if (attFill && attFill.style) attFill.style.width = pct + '%';
    cls(attWrap, 'is-low', pct < 50);
    text(attLabel, 'their focus ' + p + Math.round(pct) + '%');

    paintMinis(op);
    paintCloseness(op);
    paintHealth(op);
  }

  function paintHealth(op) {
    const h = health();
    cls(root, 'is-wobbly', h === GoonConnectionHealth.Wobbly);
    cls(root, 'is-gone', h === GoonConnectionHealth.Dead);
    cls(dot, 'is-wobbly', h === GoonConnectionHealth.Wobbly);
    cls(dot, 'is-gone', h === GoonConnectionHealth.Dead);
    text(connWord, h === GoonConnectionHealth.Fresh ? 'live' : h === GoonConnectionHealth.Wobbly ? 'wobbly' : 'gone');

    // Once their ticks go stale the engine is already counting toward an abandon
    // at GoonConsts.TickDeadMs. Show the same clock rather than a silent freeze.
    let secs = 0;
    if (h !== GoonConnectionHealth.Fresh && op && op.lastTickLocalMs) {
      const age = nowMs() - op.lastTickLocalMs;
      secs = Math.max(0, Math.ceil((GoonConsts.TickDeadMs - age) / 1000));
    }
    if (abandonEl) {
      abandonEl.hidden = h === GoonConnectionHealth.Fresh;
      if (!abandonEl.hidden) text(abandonEl, secs > 0 ? 'abandon in ' + secs + 's' : 'abandoned');
    }
  }

  function nowMs() {
    try {
      if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') return performance.now();
    } catch (_e) { /* fall through */ }
    return Date.now();
  }

  // ------------------------------------------------------------ subscribe

  function sub(name, fn) {
    if (!match || typeof match[name] !== 'function') return;
    const off = match[name](fn);
    led.add(typeof off === 'function' ? off : null);
  }

  sub('onOpponentStateChanged', () => paint());
  sub('onConnectionHealthChanged', (h) => {
    if (h !== lastHealth) lastHealth = h;
    paint();
  });
  sub('onEmoteReceived', (e) => showEmote(e && e.text, e && e.icon));

  // the abandon clock has to tick even when no state arrives (that IS the point)
  led.interval(1000, () => { try { paint(); } catch (_e) { /* never break the HUD */ } });
  paint();

  /** Renders an incoming emote in the bubble. Remote text — textContent ONLY. */
  function showEmote(msg, icon) {
    if (!bubble) return;
    text(bubbleIcon, icon || '');
    text(bubbleText, msg || '');
    bubble.hidden = false;
    cls(bubble, 'is-in', true);
    sfx(audio, 'gg-emote');
    try { clearTimeout(emoteTimer); } catch (_e) { /* gone */ }
    emoteTimer = setTimeout(() => {
      cls(bubble, 'is-in', false);
      bubble.hidden = true;
    }, EMOTE_MS);
    led.add(() => { try { clearTimeout(emoteTimer); } catch (_e) { /* gone */ } });
  }

  /** Arsenal calls this while an item is armed or being dragged. */
  function setTargeted(on) {
    cls(root, 'is-targeted', !!on);
    if (hint) hint.hidden = !on;
  }

  return {
    root,
    dropTarget: frame,
    showEmote,
    setTargeted,
    unmount() {
      led.run();
      try { root.remove(); } catch (_e) { /* already gone */ }
    },
  };
}
