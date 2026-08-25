/* ============================================================================
 * shell/orientation.js - ORIENTATION DAY, the school's hello (ORIENTATION.md §3).
 *
 * Once, ever, on the first night: you walk in the Main Gate, the school walks
 * you to the Front Office, EMI notices, and the student ID card that has always
 * just BEEN there is handed to you mid-air and flies to its corner. After that
 * the card is furniture again and the beat never plays a second time.
 *
 * SIX LAWS, and every one of them is a thing that went wrong somewhere else:
 *
 *   1. THE BEAT GATES NOTHING. No modal, no scrim, no held input, no blocked
 *      door. It plays OVER a live campus and every click the player makes goes
 *      where they aimed it. Orientation is theatre; the school is the product.
 *
 *   2. ONE CARD OBJECT. The handover animates `campus.idCardEl()` - the same
 *      node the campus built - and never a copy. `idCardMode:'withheld'` built
 *      it with `hidden = true` (the `[hidden]{display:none!important}` reset,
 *      trap 27); the beat sets `hidden = false` and nothing here ever writes a
 *      bare `display:` on that node.
 *
 *   3. THE CARD ALWAYS LANDS. Esc, a door click, a host suspend, a teardown
 *      mid-walk, a campus that never built, a walker that throws, a `mapPoint`
 *      that answers null - every single one of those ends with the card in its
 *      home slot with no animation debt and `seenAt` written. A player must
 *      never be able to lose their ID by pressing a key at the wrong moment.
 *
 *   4. SEEN IS SEEN, EXACTLY ONCE. A half-watched beat is a watched beat
 *      (§3.1). The write is idempotent and guarded, and the guard is set BEFORE
 *      the store call so a throwing store cannot buy a second attempt.
 *
 *   5. NO NEW ESC RUNG. This module binds NO key listener. shell.js's
 *      `escapeStep()` calls `skip()` while `active()` is true and returns true
 *      for that press only - a transient rung that disappears with the beat
 *      (traps 29/48). The one listener this module does bind is a capture-phase
 *      pointerdown, armed on a timer so the click that opened the campus cannot
 *      abort the beat it just caused (walk.js's law 4, same reasoning).
 *
 *   6. NO NEW RENDER SURFACES. The FLIP is a transform on an existing node; the
 *      neon pulse toggles the sheet's OWN `.off` class on the sign; the sheen is
 *      one transient gradient div inside a node that already clips. No filters
 *      over live decodes, no blend modes, nothing that survives the beat.
 *
 * NODE SAFETY: the pure halves (`needsOrientation`, `orientationOptions`) import
 * and run with no DOM at all, and every DOM touch below is guarded. With no
 * `document` the beat still fires its moments, still writes `seenAt` and still
 * reveals the card - which is exactly what the headless suites assert.
 * ==========================================================================*/

import { doorPoint } from './campus.js';
import { ORIENTATION_LINES } from '../emi/moments.js';

/** The page-owned meta key. `{seenAt: 'yyyy-MM-dd'}`, local date (trap 8). */
export const ORIENT_META_KEY = 'orientation';

/** The room the school walks you to. NEVER named in a user-facing string. */
export const ORIENT_TARGET = 'registrar';

/** The miniature fades in at the gate before it moves (§3.3 step 1). */
export const ARRIVE_MS = 600;
/** How long the POINTER abort stays deaf after start(). On a first night the
 *  beat begins the instant FIRST BELL's layer starts its 600ms unmount fade,
 *  and the player is still clicking through what looks like a lingering scene
 *  - caught live in the browser: the click-through's tail landed one frame
 *  after the abort armed and spent the once-ever beat before anything visible
 *  had happened. So the first beat-and-a-half is deaf to pointers: nothing is
 *  missable in it (the fade-in and the first steps of the walk), a door click
 *  still does its ordinary campus thing over the running beat, and Esc is NOT
 *  graced - escapeStep calls skip() directly, and a deliberate key is a
 *  deliberate key. */
export const ABORT_GRACE_MS = 900;
/** THE watchable walk - the one walk in the school that ignores WALK_MS_CAP. */
export const ORIENT_WALK_MS = 2200;
/** Arrival -> the card leaves the counter. EMI gets her line to herself. */
export const HANDOVER_DELAY_MS = 900;
/** The FLIP itself. */
export const FLIP_MS = 700;
/** The card has landed -> the send-off line. */
export const SEND_OFF_MS = 1100;
/** How small the card is at the Front Office door before it flies. */
export const FLIP_SCALE = 0.4;
/** A settle overshoot, per the Feel playbook: it arrives, then it sits down. */
export const FLIP_EASE = 'cubic-bezier(.2,1.5,.35,1)';
/** The neon blink, twice (§3.3 step 3). */
export const NEON_PULSE_MS = 130;
/** One sheen sweep across the card as it lands. */
export const SHEEN_MS = 620;
/** Reduced motion: the card simply appears, and the whole beat is ~2s. */
export const REDUCED_FADE_MS = 280;
export const REDUCED_HI_MS = 320;
export const REDUCED_CARD_MS = 760;
export const REDUCED_GO_MS = 1400;

/** The beat's steps, in order. The moments fire with these and nothing else. */
export const ORIENT_STEPS = Object.freeze(['hi', 'card', 'go']);

function isObj(v) { return !!v && typeof v === 'object' && !Array.isArray(v); }

/**
 * SHOULD THE SCHOOL INTRODUCE ITSELF? Pure, and the whole gate (§3.1).
 *
 *   no `orientation.seenAt`  AND  no punch card has ever been enrolled
 *                            AND  the attendance streak is zero
 *
 * The last two terms are THE RESTORE STORY. A veteran whose blob arrived from
 * the server mirror, or anyone who has ever finished a graded class, is never
 * orientated - they are grandfathered, and they still get the card the ordinary
 * way (the shell reads a false here and builds the campus `idCardMode:'shown'`).
 * That is why this is derived from what already exists instead of shipping a
 * second flag: there is nothing here to keep in sync and nothing to forge.
 *
 * Accepts EITHER a plain `init.meta` blob or the live store (duck-typed on
 * `all`), so the shell hands it the store and the suites hand it a blob. When a
 * store is given its own accessors do the reading - `punchCard()` normalizes a
 * half-written card and `streak()` normalizes the two attendance shapes, and
 * re-parsing those blobs here would be a second copy of both formulas.
 *
 * @param {Object} source  init.meta, or a core/store.js store
 * @returns {boolean}
 */
export function needsOrientation(source) {
  try {
    if (!source) return false;
    const store = (typeof source.all === 'function') ? source : null;
    const meta = store ? store.all() : source;
    if (!isObj(meta)) return false;

    /* 1. seen is seen. */
    const seen = meta[ORIENT_META_KEY];
    if (isObj(seen) && typeof seen.seenAt === 'string' && seen.seenAt) return false;

    /* 2. anyone with an enrolled card has been to this school before. */
    const cards = isObj(meta.punchCards) ? meta.punchCards : {};
    for (const key of Object.keys(cards)) {
      if (store && typeof store.punchCard === 'function') {
        let c = null;
        try { c = store.punchCard(key); } catch (e) { c = null; }
        if (c && c.enrolled) return false;
      } else {
        const c = cards[key];
        if (isObj(c) && typeof c.enrolledAt === 'string' && c.enrolledAt) return false;
      }
    }

    /* 3. ...and so has anyone with an attendance streak. */
    let count = 0;
    if (store && typeof store.streak === 'function') {
      try { count = (store.streak() || {}).count | 0; } catch (e) { count = 0; }
    } else {
      const s = meta.streak;
      count = isObj(s) ? (s.count | 0) : (Number(s) > 0 ? Math.round(Number(s)) : 0);
    }
    if (count > 0) return false;

    return true;
  } catch (e) { return false; }
}

/**
 * `?orientation=force` is the PLAY-TEST path, the same shape presence's fixture
 * query has (ghosts.js `presenceOptions`). It forces the beat over any meta at
 * all - and a forced run writes NOTHING, so the next reload plays it again.
 * A production launch has no query string and sees none of this.
 * @param {string=} search  test seam
 * @returns {{force:boolean}}
 */
export function orientationOptions(search) {
  let q = search;
  if (q == null) {
    try { q = (typeof location !== 'undefined' && location && location.search) || ''; }
    catch (e) { q = ''; }
  }
  return { force: /(^|[?&])orientation=force(&|$)/.test(String(q || '')) };
}

/** Today, locally, when the caller did not hand one over. Trap 8's date. */
function localToday() {
  try {
    const d = new Date();
    const p = (n) => (n < 10 ? '0' + n : String(n));
    return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate());
  } catch (e) { return ''; }
}

/** Walk `node.children` BY INDEX - trap 49: it is an Array in the double and an
 *  HTMLCollection in a browser, and `Array.isArray` is false in the one that
 *  matters. */
function childWithClass(parent, cls) {
  try {
    const kids = parent && parent.children;
    if (!kids || typeof kids.length !== 'number') return null;
    for (let i = 0; i < kids.length; i++) {
      const n = kids[i];
      const a = (n && n.getAttribute && n.getAttribute('class')) || (n && n.className) || '';
      if (String(a).split(/\s+/).indexOf(cls) >= 0) return n;
    }
  } catch (e) { /* noop */ }
  return null;
}

/**
 * ORIENTATION DAY.
 *
 * @param {Object} o
 * @param {Object=} o.campus        createCampus's handle (idCardEl / mapPoint /
 *   walkMount / facilityNode). Null is legal: the beat degrades to its moments.
 * @param {Object=} o.walker        createWalker's handle. Null is legal.
 * @param {Object=} o.store         core/store.js - the ONE writer of `seenAt`.
 * @param {Function=} o.fireMoment  emi/moments.js's verb. Null is legal.
 * @param {Function=} o.t           core/lexicon.js's `t`. Null is legal.
 * @param {string=} o.localDate     today, locally (trap 8). Defaults to now.
 * @param {boolean=} o.reducedMotion  no walk, no FLIP; the card fades in place.
 * @param {boolean=} o.forced       `?orientation=force` - play the beat, write
 *   nothing, so the next reload plays it again.
 * @param {Function=} o.log
 * @param {Object=} o.clock         test seam {setTimeout, clearTimeout}
 * @returns {{start, skip, active, destroy, diagnostics}}
 */
export function createOrientation(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const campus = opts.campus || null;
  const walker = opts.walker || null;
  const store = opts.store || null;
  const forced = !!opts.forced;
  const still = !!(opts.reducedMotion || opts.lowPerf);
  const day = String(opts.localDate || localToday() || '');
  const tr = typeof opts.t === 'function' ? opts.t : (k, fb) => (fb == null ? String(k) : fb);
  const fire = typeof opts.fireMoment === 'function' ? opts.fireMoment : null;

  const clock = opts.clock || {
    setTimeout: (fn, ms) => {
      try { if (typeof setTimeout === 'function') return setTimeout(fn, ms); } catch (e) { /* noop */ }
      return 0;
    },
    clearTimeout: (id) => {
      try { if (id && typeof clearTimeout === 'function') clearTimeout(id); } catch (e) { /* noop */ }
    },
  };

  /** 'idle' -> 'running' -> 'done'. There is no fourth state and no way back. */
  let state = 'idle';
  let destroyed = false;
  let arrived = false;
  let seenWritten = false;
  let bound = false;
  let sheenNode = null;
  const timers = [];

  function at(ms, fn) {
    const id = clock.setTimeout(() => {
      const i = timers.indexOf(id);
      if (i >= 0) timers.splice(i, 1);
      try { fn(); } catch (e) { say('orientation step threw: ' + ((e && e.message) || e)); }
    }, Math.max(0, ms | 0));
    if (id) timers.push(id);
    return id;
  }

  function clearTimers() {
    while (timers.length) { try { clock.clearTimeout(timers.pop()); } catch (e) { /* noop */ } }
  }

  /* ------------------------------------------------------------ the nodes */

  function cardEl() {
    try {
      if (campus && typeof campus.idCardEl === 'function') return campus.idCardEl() || null;
    } catch (e) { /* noop */ }
    return null;
  }

  function youEl() {
    try { return childWithClass(campus && campus.walkMount, 'gh-you'); }
    catch (e) { return null; }
  }

  function neonEl() {
    try {
      if (!campus || typeof campus.facilityNode !== 'function') return null;
      return childWithClass(campus.facilityNode(ORIENT_TARGET), 'campus-neon');
    } catch (e) { return null; }
  }

  function setStyle(node, k, v) {
    try { if (node && node.style) node.style[k] = v; } catch (e) { /* noop */ }
  }

  /** One forced layout, so the browser has a FROM to transition out of. */
  function reflow(node) {
    try { if (node) return node.offsetWidth; } catch (e) { /* noop */ }
    return 0;
  }

  function rectOf(node) {
    try {
      if (!node || typeof node.getBoundingClientRect !== 'function') return null;
      const r = node.getBoundingClientRect();
      if (!r || !Number.isFinite(r.width) || !Number.isFinite(r.top)) return null;
      return r;
    } catch (e) { return null; }
  }

  /* ------------------------------------------------------------ the beats */

  /** One EMI line. The LINE comes through the payload because moments.js has no
   *  lexicon of its own (it never has - REPORT_LINES are literals there), so the
   *  mod-skinnable row is resolved HERE, where `t` lives, and the frozen table
   *  in moments.js is the English fallback and the owner of the faces. */
  function moment(step) {
    if (!fire) return false;
    const row = ORIENTATION_LINES[step] || null;
    const line = tr('emi_orientation_' + step, row ? row.line : '');
    try { return !!fire('orientation', { step, line }); }
    catch (e) { say('orientation moment threw: ' + ((e && e.message) || e)); return false; }
  }

  /** ONE stamp-family cue, through the document event shell/audio.js owns
   *  (trap 18 - this module may not hold an audio node). ceremonies.js's
   *  grammar, verbatim. */
  function stampCue() {
    try {
      if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
      const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
      if (!Ctor) return;
      document.dispatchEvent(new Ctor('arcademy-sfx', {
        detail: { name: 'stamp', level: 0.6, bus: 'fx' },
      }));
    } catch (e) { /* a cue must never be the thing that throws */ }
  }

  /** The Front Office sign notices you: two blinks, using the sheet's own
   *  `.off` modifier on `.campus-neon`. No new CSS surface (§4). */
  function pulseNeon() {
    if (still) return;
    const neon = neonEl();
    if (!neon || !neon.classList) return;
    const off = (on) => {
      try { if (on) neon.classList.add('off'); else neon.classList.remove('off'); }
      catch (e) { /* noop */ }
    };
    off(true);
    at(NEON_PULSE_MS, () => off(false));
    at(NEON_PULSE_MS * 2, () => off(true));
    at(NEON_PULSE_MS * 3, () => off(false));
  }

  /** Always safe to call: a sign left dark by an abort is lit again. */
  function neonHome() {
    const neon = neonEl();
    try { if (neon && neon.classList) neon.classList.remove('off'); } catch (e) { /* noop */ }
  }

  /** The miniature arrives at the gate. `mountAt(null)` IS the gate (walk.js). */
  function fadeInYou() {
    try { if (walker && typeof walker.mountAt === 'function') walker.mountAt(null); }
    catch (e) { say('orientation: the miniature refused to mount'); }
    if (still) return;
    const you = youEl();
    if (!you) return;
    setStyle(you, 'transition', 'none');
    setStyle(you, 'opacity', '0');
    reflow(you);
    setStyle(you, 'transition', 'opacity ' + ARRIVE_MS + 'ms ease-out');
    setStyle(you, 'opacity', '1');
    at(ARRIVE_MS + 60, () => {
      setStyle(you, 'transition', '');
      setStyle(you, 'opacity', '');
    });
  }

  function youHome() {
    const you = youEl();
    if (!you) return;
    setStyle(you, 'transition', '');
    setStyle(you, 'opacity', '');
  }

  /* ------------------------------------------------------------- the card */

  /** ONE sheen sweep across the card as it lands. A transient child inside a
   *  node that already clips (`overflow:hidden`), removed the moment it is
   *  done - never a rule in the sheet, never a filter, never a blend. */
  function sheen(card) {
    if (still || !card) return;
    try {
      if (typeof document === 'undefined' || typeof document.createElement !== 'function') return;
      const n = document.createElement('div');
      if (!n || typeof n.animate !== 'function') return;
      n.setAttribute('aria-hidden', 'true');
      n.style.position = 'absolute';
      n.style.top = '0';
      n.style.left = '0';
      n.style.width = '45%';
      n.style.height = '100%';
      n.style.pointerEvents = 'none';
      n.style.background =
        'linear-gradient(90deg, transparent, rgba(255,255,255,.20), transparent)';
      card.appendChild(n);
      sheenNode = n;
      const anim = n.animate([
        { transform: 'translateX(-160%) skewX(-18deg)' },
        { transform: 'translateX(300%) skewX(-18deg)' },
      ], { duration: SHEEN_MS, easing: 'ease-in-out' });
      const drop = () => {
        try { n.remove(); } catch (e) { /* noop */ }
        if (sheenNode === n) sheenNode = null;
      };
      try { anim.onfinish = drop; } catch (e) { /* noop */ }
      at(SHEEN_MS + 120, drop);
    } catch (e) { /* decoration, and only decoration */ }
  }

  function dropSheen() {
    if (!sheenNode) return;
    try { sheenNode.remove(); } catch (e) { /* noop */ }
    sheenNode = null;
  }

  /**
   * THE HANDOVER. The card un-hides at the Front Office door and flies home.
   * Returns true only when a real FLIP was started - a null `mapPoint` (no
   * `getScreenCTM`, a detached SVG, the headless double), a card that measures
   * zero (the small-viewport media rule hides it), or reduced motion all answer
   * false, and the caller lands it in place instead.
   */
  function flip(card) {
    if (!card || still) return false;
    let from = null;
    try {
      if (campus && typeof campus.mapPoint === 'function') from = campus.mapPoint(doorPoint(ORIENT_TARGET));
    } catch (e) { from = null; }
    if (!from) return false;
    const r = rectOf(card);
    if (!r || !(r.width > 0) || !(r.height > 0)) return false;
    const dx = from.x - (r.left + r.width / 2);
    const dy = from.y - (r.top + r.height / 2);
    if (!Number.isFinite(dx) || !Number.isFinite(dy)) return false;
    setStyle(card, 'transformOrigin', '50% 50%');
    setStyle(card, 'transition', 'none');
    /* The home rotation is the SHEET's (rotate(-2.6deg)); carrying it through
     * the whole flight is what makes clearing the inline transform below read as
     * a landing rather than as a snap. */
    setStyle(card, 'transform',
      'translate(' + Math.round(dx) + 'px,' + Math.round(dy) + 'px) scale('
      + FLIP_SCALE + ') rotate(-2.6deg)');
    reflow(card);
    setStyle(card, 'transition', 'transform ' + FLIP_MS + 'ms ' + FLIP_EASE);
    setStyle(card, 'transform', '');
    return true;
  }

  /** THE CARD IS IN THE AIR. campus.js refuses the ID's own click and its chip
   *  while this attribute is set: a card that is mid-handover is a beat, not a
   *  thing you can pick up. ONE attribute, set where the flight starts and
   *  cleared in landCard() - which every path out of the beat already goes
   *  through, so there is exactly one place it comes off. */
  function markInflight(on) {
    const card = cardEl();
    if (!card || !card.dataset) return;
    try {
      if (on) card.dataset.inflight = '1';
      else delete card.dataset.inflight;
    } catch (e) { /* noop */ }
  }

  /** LAW 3. The card is in its home slot, right now, with nothing pending. */
  function landCard() {
    const card = cardEl();
    if (!card) return false;
    try { card.hidden = false; } catch (e) { /* noop */ }
    markInflight(false);
    dropSheen();
    setStyle(card, 'transition', '');
    setStyle(card, 'transform', '');
    setStyle(card, 'transformOrigin', '');
    setStyle(card, 'opacity', '');
    return true;
  }

  /** Reduced motion: it is simply there, gently (§3.3's last paragraph). */
  function fadeInCard() {
    const card = cardEl();
    if (!card) return false;
    try { card.hidden = false; } catch (e) { /* noop */ }
    setStyle(card, 'transition', 'none');
    setStyle(card, 'opacity', '0');
    reflow(card);
    setStyle(card, 'transition', 'opacity ' + REDUCED_FADE_MS + 'ms ease-out');
    setStyle(card, 'opacity', '1');
    at(REDUCED_FADE_MS + 60, () => {
      setStyle(card, 'transition', '');
      setStyle(card, 'opacity', '');
      markInflight(false);
    });
    return true;
  }

  /* ----------------------------------------------------------- the ledger */

  /**
   * SEEN, EXACTLY ONCE (law 4). The latch is set before the store call, so a
   * store that throws cannot buy a second attempt - and a FORCED run latches
   * too, it just never writes: play-testing the beat must not spend the real
   * one, and it must not leave a half-written key behind either.
   */
  function writeSeen() {
    if (seenWritten) return false;
    seenWritten = true;
    if (forced) { say('orientation: forced run - seenAt deliberately not written'); return false; }
    if (!store || typeof store.set !== 'function') return false;
    try {
      store.set(ORIENT_META_KEY, { seenAt: day });
      say('orientation: seenAt ' + day);
      return true;
    } catch (e) {
      say('orientation seenAt write failed: ' + ((e && e.message) || e));
      return false;
    }
  }

  /* ------------------------------------------------------------ the input */

  function onAbortInput() { skip(); }

  /** Armed on a TIMER, never inline: the click that opened this campus is still
   *  bubbling, and a beat that aborted on its own cause would never play. The
   *  timer is ABORT_GRACE_MS, not 0 - the VN's dismissal tail (see the
   *  constant) taught us that "one turn later" is still inside the player's
   *  click-through. */
  function armAbort() {
    at(ABORT_GRACE_MS, () => {
      if (state !== 'running' || bound) return;
      try {
        if (typeof document === 'undefined' || typeof document.addEventListener !== 'function') return;
        document.addEventListener('pointerdown', onAbortInput, true);
        document.addEventListener('click', onAbortInput, true);
        bound = true;
      } catch (e) { /* noop */ }
    });
  }

  function disarmAbort() {
    if (!bound) return;
    bound = false;
    try {
      document.removeEventListener('pointerdown', onAbortInput, true);
      document.removeEventListener('click', onAbortInput, true);
    } catch (e) { /* noop */ }
  }

  /* -------------------------------------------------------------- the run */

  function onArrive() {
    if (arrived || state !== 'running') return;
    arrived = true;
    moment('hi');
    pulseNeon();
    at(HANDOVER_DELAY_MS, handover);
  }

  function handover() {
    if (state !== 'running') return;
    const card = cardEl();
    try { if (card) card.hidden = false; } catch (e) { /* noop */ }
    markInflight(true);
    const flew = flip(card);
    at(flew ? FLIP_MS : 0, () => {
      if (state !== 'running') return;
      landCard();
      sheen(card);
      stampCue();
      moment('card');
      at(SEND_OFF_MS, complete);
    });
  }

  function stepWalk() {
    if (state !== 'running') return;
    let asked = false;
    try {
      if (walker && typeof walker.walkTo === 'function') {
        walker.walkTo(ORIENT_TARGET, { durationMs: ORIENT_WALK_MS, onDone: onArrive });
        /* AFTER the call, never before: a walkTo that threw promised no onDone,
         * and a beat that waited for one would stall at the gate forever. */
        asked = true;
      }
    } catch (e) {
      say('orientation walk refused (' + ((e && e.message) || e) + ') - straight to the counter');
    }
    /* DECORATION, NEVER A GATE. No walker, or a walker that threw before it
     * could promise an onDone, and the beat carries on from the counter. */
    if (!asked) onArrive();
  }

  /** The full run reached its end: the send-off, then the ledger. */
  function complete() {
    if (state !== 'running') return;
    moment('go');
    finish('complete');
  }

  /**
   * THE ONE EXIT. Every path out of the beat - arrival, Esc, a door click, a
   * suspend, a teardown - is this function, so there is exactly one place the
   * card lands, the sign relights, the listener comes off and `seenAt` is
   * written.
   */
  function finish(reason) {
    if (state === 'done') return false;
    state = 'done';
    clearTimers();
    disarmAbort();
    neonHome();
    youHome();
    landCard();
    writeSeen();
    say('orientation: ' + tr('orientation_kicker', 'Orientation Day') + ' (' + reason + ')');
    return true;
  }

  /* ------------------------------------------------------------------- API */

  function start() {
    if (destroyed || state !== 'idle') return false;
    state = 'running';
    armAbort();
    fadeInYou();
    if (still) {
      /* REDUCED MOTION (~2s): no walk, no FLIP, the same three lines. */
      at(REDUCED_HI_MS, () => { if (state === 'running') { moment('hi'); } });
      at(REDUCED_CARD_MS, () => {
        if (state !== 'running') return;
        markInflight(true);
        fadeInCard();
        stampCue();
        moment('card');
      });
      at(REDUCED_GO_MS, complete);
      return true;
    }
    at(ARRIVE_MS, stepWalk);
    return true;
  }

  function skip() {
    if (state !== 'running') return false;
    return finish('skipped');
  }

  function active() { return state === 'running'; }

  function destroy() {
    if (destroyed) return;
    /* THE CARD OUTLIVES THE BEAT. finish() runs BEFORE the destroyed flag so a
     * screen torn down mid-walk still lands the ID and still banks `seenAt` -
     * the campus is going away, but the player's card is not the campus's. */
    if (state === 'running') finish('destroyed');
    destroyed = true;
    clearTimers();
    disarmAbort();
    dropSheen();
  }

  return {
    start,
    skip,
    active,
    destroy,
    /** Test seam. */
    diagnostics() {
      return {
        state, forced, still, arrived, seenWritten, bound,
        timers: timers.length, day,
      };
    },
  };
}

export default createOrientation;
