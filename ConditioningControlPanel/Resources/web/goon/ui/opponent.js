/* ============================================================================
 * ui/opponent.js — the streamer-cam "monitor" that IS the opponent.
 *
 * Everything the other player lets us see lands here: their name, their charge
 * meter, their attention bar, the closeness they CLAIM, their emotes, and — in a
 * centred PROJECTION RECT inside the bezel — a stylized miniature of their
 * screen. The miniature is DOM/CSS only: it is a caricature, never a stream, and
 * no frame of their machine ever crosses the wire.
 *
 * Three things drive the minis, and they are different on purpose:
 *   1. the effect NAMES on their state tick ("Flashes", "Spiral", ...) — their
 *      own draft ramp, the ambience;
 *   2. the payloads WE fired (markPayloadFired / markReceipt, threaded from
 *      ui/arsenal.js through ui/hud.js) — a payload they are enduring never
 *      appears in active_effects, so the sender animates its own window;
 *   3. the emotes WE sent (markEmoteFired) — see the tap below.
 * A payload that comes back `survived` gets the green wash + checkmark: the one
 * piece of motion here that is feedback rather than ambience, and the only one
 * exempt from the two-mini motion budget.
 *
 * EMOTES ARE NOT PAYLOADS. `t:'emote'` is its own message family: no cost, no
 * rate limiter beyond the sheet's own 5 s, no `accepted` ACK and NO RECEIPT AT
 * ALL. So there is nothing for ui/hud.js to thread the way it threads a fire,
 * and the mini runs on a fixed dwell instead of a receipt lifecycle. Both
 * directions are drawn, in different places, because they mean different things:
 *   OUTBOUND (markEmoteFired) — our line landing on THEIR screen: a speech
 *     bubble inside the projection rect, exactly like the payload minis;
 *   INBOUND  (showEmote)      — their line, spoken AT us: the .gg-mon-bubble on
 *     the bezel, which is not part of their screen and never was.
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
import { GoonConsts, GoonElement, GoonMatchPhase, GoonPayloadKind, enumName } from '../core/contracts.js';
import { GoonReceiptStatus } from '../core/scoring.js';
import { S } from './strings.js';

/** Closeness 0-3 -> the word that always rides with the colour. */
export const CLOSENESS_WORDS = Object.freeze(['steady', 'warm', 'close', 'edge']);

/** Emote bubble dwell. */
const EMOTE_MS = 4000;

/**
 * Miniature parts. `key` is the wire name on tick.active_effects; `anim` marks the
 * ones that carry motion — at most ANIM_BUDGET of those run at a time. The two
 * `anim: false` ones (the lock card mockup, the drain veil) are always drawn:
 * they are a STATE, not a motion, and they cost nothing to leave on screen.
 */
const MINIS = Object.freeze([
  { key: 'Flashes', cls: 'gg-mini-flash', anim: true },
  { key: 'Bubbles', cls: 'gg-mini-bubbles', anim: true },
  { key: 'Videos', cls: 'gg-mini-video', anim: true },
  { key: 'Spiral', cls: 'gg-mini-spiral', anim: true },
  { key: 'BouncingText', cls: 'gg-mini-bounce', anim: true },
  { key: 'Subliminals', cls: 'gg-mini-sub', anim: true },
  { key: 'LockCards', cls: 'gg-mini-lock', anim: false },
  { key: 'ToyPatterns', cls: 'gg-mini-toy', anim: true },
  { key: 'BrainDrain', cls: 'gg-mini-drain', anim: false },
  // Not a wire effect name — nothing ever puts 'Emote' in active_effects. It is
  // in this table so the emote rides the SAME machinery as every other mini:
  // one window in `windows`, one budget slot, one is-on/is-anim/is-yours pass.
  { key: 'Emote', cls: 'gg-mini-emote', anim: true },
]);

/** The emote mini's whole life. No receipt ever ends it — see the header. */
export const EMOTE_MINI_MS = 2600;
/** The wiggle+fade that plays out the last of that dwell. */
const EMOTE_MINI_OUTRO_MS = 420;
/** The bubble is ~90 px wide on a 220 px monitor; anything longer is ellipsis. */
const EMOTE_MINI_TEXT_MAX = 22;
/** Two marks of the same line inside this are one send seen twice. */
const EMOTE_MINI_DEDUPE_MS = 400;
/** The synthetic window id: there is only ever one emote on their screen. */
const EMOTE_WINDOW_ID = '#emote';

/**
 * What YOU fired -> the mini that should play while it is landing on them.
 * active_effects only lists their sustained ramp elements; a payload they are
 * enduring never appears there, so the sender animates it from its own window.
 */
export const MINI_FOR_PAYLOAD = Object.freeze({
  [GoonPayloadKind.FlashBurst]: 'Flashes',
  [GoonPayloadKind.SubliminalStorm]: 'Subliminals',
  [GoonPayloadKind.BubbleSwarm]: 'Bubbles',
  [GoonPayloadKind.Video]: 'Videos',
  [GoonPayloadKind.LockCard]: 'LockCards',
  [GoonPayloadKind.ToyPattern]: 'ToyPatterns',
  [GoonPayloadKind.BrainDrain]: 'BrainDrain',
  [GoonPayloadKind.Spiral]: 'Spiral',
});

const ANIM_BUDGET = 2;

/**
 * Receipt statuses that END a payload window.
 *
 * A payload gets TWO receipts, not one: `accepted` the instant the peer admits
 * it, and a terminal one when it is over. Closing on `accepted` closed every
 * window ~60 ms after it opened — i.e. before the lead time had even elapsed,
 * so no payload we fired ever showed on their screen. Anything NOT in this set
 * (a future status, a garbled one) is treated as non-terminal and left to the
 * window's own timer, which is clamped at MAX_WINDOW_MS and cannot strand it.
 */
const TERMINAL_RECEIPTS = new Set([
  GoonReceiptStatus.Survived,
  GoonReceiptStatus.Completed,
  GoonReceiptStatus.RejectedRate,
  GoonReceiptStatus.RejectedFiltered,
]);

/** Terminal-status test that also covers `rejected_*` reasons added later. */
export function isTerminalReceipt(status) {
  const s = String(status || '');
  return TERMINAL_RECEIPTS.has(s) || s.indexOf('rejected') === 0;
}

/** How long the green pass wash + checkmark hold. Feedback, never ambience. */
const PASS_MS = 1200;
/** Longest payload window we will ever hold a mini open for (engine clamp). */
const MAX_WINDOW_MS = 180000;

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
  if (!root || !host) {
    return {
      unmount() { led.run(); },
      root: null, dropTarget: null,
      showEmote() {}, markEmoteFired() { return false; },
    };
  }

  // ---- head: name · connection dot · their score · charge pips ------------
  const head = add(root, el('div', 'gg-mon-head'));
  const dot = add(head, el('i', 'gg-mon-dot'));
  const nameEl = add(head, el('span', 'gg-mon-name', 'opponent'));
  const scoreEl = add(head, el('span', 'gg-mon-score', '0'));
  const pipRow = add(head, el('span', 'gg-mon-pips'));
  const pips = [];
  for (let i = 0; i < GoonConsts.ChargeCap; i++) pips.push(add(pipRow, el('i', 'gg-pip gg-pip--sm')));

  // ---- the screen inside the bezel ---------------------------------------
  // THE STACKING ORDER HERE IS LOAD-BEARING. assets/monitor_frame.png is an
  // OPAQUE cel-shaded CRT whose face is painted PURE BLACK — it is not a cutout.
  // So the three layers go, bottom to top:
  //
  //   .gg-mon-screen  dark glass, only ever seen through .is-nobezel (z1);
  //   .gg-mon-bezel   the art itself                                  (z2);
  //   .gg-mon-proj    the PROJECTION RECT, where every miniature plays (z3).
  //
  // The projection rect used to be a CHILD of .gg-mon-screen, and z-index:1 on
  // that element opens a stacking context — no z-index on a descendant can climb
  // out of it, so every mini rendered underneath the painted black screen and
  // nothing was ever visible. DOM order below and `z-index: 3` in hud.css are
  // two halves of one fix; do not separate them.
  const frame = add(root, el('div', 'gg-mon-frame'));
  add(frame, el('div', 'gg-mon-screen'));

  const bezel = el('img', 'gg-mon-bezel');
  if (bezel) {
    bezel.src = './assets/monitor_frame.png';
    bezel.alt = '';
    bezel.decoding = 'async';
    led.listen(bezel, 'error', () => { cls(frame, 'is-nobezel', true); try { bezel.remove(); } catch (_e) { /* gone */ } });
    add(frame, bezel);
  }

  // …and the projection rect ON TOP of the art, sized (in hud.css) to the CRT
  // face the art paints.
  const proj = add(frame, el('div', 'gg-mon-proj'));
  const parts = new Map();
  let emoteIconEl = null;
  let emoteTextEl = null;
  for (const m of MINIS) {
    const node = add(proj, el('div', 'gg-mini ' + m.cls));
    if (m.key === 'Flashes') for (let i = 0; i < 4; i++) add(node, el('i', 'gg-mini-shard'));
    if (m.key === 'Bubbles') for (let i = 0; i < 5; i++) add(node, el('i', 'gg-mini-bub'));
    if (m.key === 'Videos') {
      for (let i = 0; i < 3; i++) add(node, el('i', 'gg-mini-bar'));
      add(node, el('i', 'gg-mini-scan'));
    }
    if (m.key === 'Spiral') add(node, el('i', 'gg-mini-spiral-disc'));
    if (m.key === 'Subliminals') add(node, el('span', 'gg-mini-line', 'deeper'));
    if (m.key === 'BouncingText') add(node, el('span', 'gg-mini-word', 'good girl'));
    if (m.key === 'LockCards') {
      const card = add(node, el('div', 'gg-mini-card'));
      for (let i = 0; i < 3; i++) add(card, el('i', 'gg-mini-scribble'));
      add(card, el('i', 'gg-mini-caret'));
    }
    if (m.key === 'ToyPatterns') add(node, el('i', 'gg-mini-toy-dot'));
    if (m.key === 'BrainDrain') add(node, el('i', 'gg-mini-drain-vig'));
    if (m.key === 'Emote') {
      const bub = add(node, el('div', 'gg-mini-emote-bub'));
      emoteIconEl = add(bub, el('span', 'gg-mini-emote-icon'));
      emoteTextEl = add(bub, el('span', 'gg-mini-emote-text'));
    }
    parts.set(m.key, node);
  }
  const idle = add(proj, el('div', 'gg-mini-idle', S.monitor.idle));

  // pass feedback: a subtle green wash over the projection + a checkmark. This
  // pair is EXEMPT from the motion budget and from the "hot" parking rule — it
  // answers something the player did, it is not ambience.
  const passWash = add(proj, el('i', 'gg-mon-pass'));
  const check = add(proj, el('div', 'gg-mon-check', '✓'));
  if (check && check.setAttribute) {
    check.setAttribute('role', 'status');
    check.setAttribute('aria-label', S.monitor.passed);
  }

  // emote bubble — one at a time, textContent only
  const bubble = add(frame, el('div', 'gg-mon-bubble'));
  if (bubble) bubble.hidden = true;
  const bubbleIcon = add(bubble, el('span', 'gg-mon-bubble-icon'));
  const bubbleText = add(bubble, el('span', 'gg-mon-bubble-text'));

  // drop hint (arsenal flips this on while an item is armed / dragging)
  const hint = add(frame, el('div', 'gg-mon-hint', S.monitor.dropHint));
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
  let passTimer = 0;
  let lastEmoteMarkAt = -Infinity;
  let lastEmoteMarkKey = null;

  /** payload id -> {key, open, timers[]} for the payloads WE fired. */
  const windows = new Map();

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

    // …plus everything WE have in flight at them right now.
    const forced = new Set();
    for (const w of windows.values()) {
      if (!w.open || !w.key) continue;
      forced.add(w.key);
      live.add(w.key);
    }

    // Motion budget: at most ANIM_BUDGET minis actually move, and a payload we
    // fired outranks their ambient ramp for that budget — it is the thing the
    // player just paid for and is waiting to see land.
    const moving = new Set();
    const wants = MINIS.filter((m) => m.anim && live.has(m.key));
    wants.sort((a, b) => (forced.has(b.key) ? 1 : 0) - (forced.has(a.key) ? 1 : 0));
    for (const m of wants) { if (moving.size < ANIM_BUDGET) moving.add(m.key); }

    // An effect name we have no mini for (a newer peer, a garbled tick) must not
    // silently blank the idle word: count what we can actually DRAW.
    let drawn = 0;
    for (const m of MINIS) {
      const node = parts.get(m.key);
      if (!node) continue;
      const on = live.has(m.key);
      if (on) drawn++;
      cls(node, 'is-on', on);
      cls(node, 'is-anim', moving.has(m.key));
      cls(node, 'is-yours', forced.has(m.key));
    }
    if (idle) idle.hidden = drawn > 0;
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
    // The minis paint even with no opponent state yet: a payload window of our
    // own is reason enough for the little screen to be doing something.
    paintMinis(op);
    if (!op) return;
    const p = stalePrefix();

    text(nameEl, op.displayName || 'opponent');
    text(scoreEl, p + String(op.score | 0));
    for (let i = 0; i < pips.length; i++) cls(pips[i], 'is-on', i < (op.charges | 0));

    const pct = Math.max(0, Math.min(100, Number(op.attentionPct) || 0));
    if (attFill && attFill.style) attFill.style.width = pct + '%';
    cls(attWrap, 'is-low', pct < 50);
    text(attLabel, 'their focus ' + p + Math.round(pct) + '%');

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

  /* ------------------------------------------------ the outgoing-emote tap
   *
   * There is no onEmoteSent/onFired for an emote to arrive on: the engine has
   * `t:'emote'` as a fire-and-forget family (core/match.js sendEmote -> _send,
   * no id, no receipt, no event), and ui/emotes.js calls it directly off the
   * sheet. The only signal that an emote actually went out is the call itself,
   * so we borrow it for the life of the mount and hand it straight back.
   *
   * markEmoteFired() below is the REAL seam. If ui/hud.js ever grows a hook
   * (mountEmotes({ onSent }) -> opponent.markEmoteFired), point it there and
   * delete this block — the dedupe inside markEmoteFired makes an overlap
   * where BOTH fire harmless rather than a double bubble.
   */
  if (match && typeof match.sendEmote === 'function') {
    const original = match.sendEmote;
    const hadOwn = Object.prototype.hasOwnProperty.call(match, 'sendEmote');
    const tapped = function sendEmoteTapped(t, i) {
      const out = original.apply(this, arguments);
      // Mirror the engine's own guard: a send it drops must not draw anything.
      try {
        if (match.phase !== GoonMatchPhase.Idle) markEmoteFired(t, i);
      } catch (_e) { /* a mini must never break a send */ }
      return out;
    };
    try {
      match.sendEmote = tapped;
      led.add(() => {
        try {
          if (match.sendEmote !== tapped) return;      // someone else re-tapped: leave it
          if (hadOwn) match.sendEmote = original;
          else delete match.sendEmote;
        } catch (_e) { /* gone */ }
      });
    } catch (_e) { /* a frozen engine just means no outgoing mini */ }
  }

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

  // ------------------------------------------------- payload windows + pass

  function laterOnce(ms, fn) {
    if (typeof setTimeout !== 'function') { fn(); return 0; }
    const id = setTimeout(() => { try { fn(); } catch (_e) { /* never break the HUD */ } }, Math.max(0, ms | 0));
    led.add(() => { try { clearTimeout(id); } catch (_e) { /* gone */ } });
    return id;
  }

  function closeWindow(id) {
    const w = windows.get(id);
    if (!w) return;
    for (const t of w.timers) { try { clearTimeout(t); } catch (_e) { /* gone */ } }
    windows.delete(id);
    paint();
  }

  /**
   * hud.js calls this the moment the engine accepts one of OUR fires. We know
   * the kind, roughly when it lands (the engine schedules a fixed buffer ahead)
   * and how long it runs — that is the whole animation window, and it does not
   * depend on their tick ever mentioning it.
   *
   * @param {object} o {id, kind, durationMs, leadMs}
   */
  function markPayloadFired({ id, kind, durationMs = 0, leadMs = 0 } = {}) {
    const key = MINI_FOR_PAYLOAD[kind];
    if (!key || !id) return;
    closeWindow(id);
    const run = Math.max(1000, Math.min(MAX_WINDOW_MS, durationMs | 0));
    const w = { key, kind, open: false, timers: [] };
    windows.set(id, w);
    w.timers.push(laterOnce(Math.max(0, leadMs | 0), () => {
      const cur = windows.get(id);
      if (!cur) return;
      cur.open = true;
      paint();
    }));
    w.timers.push(laterOnce(Math.max(0, leadMs | 0) + run, () => closeWindow(id)));
  }

  /** Drops the emote window AND the classes only it uses. */
  function closeEmoteWindow() {
    const node = parts.get('Emote');
    cls(node, 'is-out', false);
    cls(node, 'is-lost', false);
    closeWindow(EMOTE_WINDOW_ID);
  }

  /**
   * An emote WE sent, landing on their little screen.
   *
   * Unlike a payload there is no lead (nothing is scheduled: the engine puts it
   * on the wire immediately) and no receipt (the family has none), so the whole
   * lifecycle is local: pop in, sit for EMOTE_MINI_MS, wiggle out. The one
   * failure state that genuinely exists is a peer whose link is already DEAD —
   * that line is not going to be read by anyone, so it greys and drops instead.
   *
   * @param   {string} [msg]  one of ui/emotes.js EMOTE_PRESETS
   * @param   {string} [icon] one of ui/emotes.js EMOTE_ICONS
   * @returns {boolean} whether a bubble was actually drawn
   */
  function markEmoteFired(msg, icon) {
    const node = parts.get('Emote');
    if (!node) return false;

    const line = String(msg == null ? '' : msg);
    const glyph = String(icon == null ? '' : icon);
    const key = line + String.fromCharCode(31) + glyph;
    const at = nowMs();
    if (key === lastEmoteMarkKey && at - lastEmoteMarkAt < EMOTE_MINI_DEDUPE_MS) return false;
    lastEmoteMarkKey = key;
    lastEmoteMarkAt = at;

    closeEmoteWindow();

    // Both of these are OUR OWN canned strings, but they go through textContent
    // for the same reason everything else on this monitor does.
    text(emoteIconEl, glyph || (line ? '' : '💬'));
    text(emoteTextEl, line.length > EMOTE_MINI_TEXT_MAX ? line.slice(0, EMOTE_MINI_TEXT_MAX - 1) + '…' : line);

    const lost = health() === GoonConnectionHealth.Dead;
    cls(node, 'is-lost', lost);

    const w = { key: 'Emote', kind: null, open: true, timers: [] };
    windows.set(EMOTE_WINDOW_ID, w);
    // The bounce-in is CSS: the node goes display:none -> flex, which restarts it.
    // (No cue here — ui/emotes.js already plays `gg-emote` on the send itself,
    // and the arsenal tile plays it again when the sheet opens. Three is a lot.)
    paint();

    if (!lost) {
      w.timers.push(laterOnce(EMOTE_MINI_MS - EMOTE_MINI_OUTRO_MS, () => {
        if (windows.get(EMOTE_WINDOW_ID) === w) cls(node, 'is-out', true);
      }));
    }
    w.timers.push(laterOnce(EMOTE_MINI_MS, () => {
      if (windows.get(EMOTE_WINDOW_ID) === w) closeEmoteWindow();
    }));
    return true;
  }

  /**
   * …and this when a receipt for one of ours comes back. `survived` is the
   * flagship: they took the whole thing and held. That earns the green.
   *
   * NOT every receipt is the end. The engine acks with `accepted` the moment
   * the peer admits the payload — roughly 60 ms after the fire, and 1.4 s
   * BEFORE the window is even due to open. Closing on that ack meant no fired
   * payload ever reached their little screen; only their own ramp did, which is
   * why the monitor looked like it only ever showed one thing.
   */
  function markReceipt(id, status) {
    if (!id) return;
    const s = String(status || '');
    if (!isTerminalReceipt(s)) return;        // `accepted` — it is still landing
    const w = windows.get(id);
    if (s === GoonReceiptStatus.Survived) playPass(w && w.key);
    closeWindow(id);
  }

  /** Green wash + checkmark, and the lock-card mockup lights up with them. */
  function playPass(key) {
    const lock = key === 'LockCards' ? parts.get('LockCards') : null;
    cls(lock, 'is-pass', true);
    cls(passWash, 'is-in', true);
    cls(check, 'is-in', true);
    sfx(audio, 'gg-endured');
    try { clearTimeout(passTimer); } catch (_e) { /* gone */ }
    passTimer = setTimeout(() => {
      cls(lock, 'is-pass', false);
      cls(passWash, 'is-in', false);
      cls(check, 'is-in', false);
    }, PASS_MS);
    led.add(() => { try { clearTimeout(passTimer); } catch (_e) { /* gone */ } });
  }

  return {
    root,
    dropTarget: frame,
    /** The projection rect — exposed so a play-test driver can find the minis. */
    projection: proj,
    showEmote,
    /** OUR emote, on THEIR screen. The seam a ui/hud.js hook would call. */
    markEmoteFired,
    setTargeted,
    markPayloadFired,
    markReceipt,
    unmount() {
      for (const id of Array.from(windows.keys())) closeWindow(id);
      led.run();
      try { root.remove(); } catch (_e) { /* already gone */ }
    },
  };
}
