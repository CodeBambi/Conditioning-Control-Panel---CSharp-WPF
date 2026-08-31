/* ============================================================================
 * games/impulse-control/cameo.js - DECK IV of the House Rules: THE CAMEO.
 *
 * The one deck in the school that does not paint a lie, a light or a ladder.
 * It asks the SHELL for the mascot, and the mascot walks into the basin.
 *
 *   THE STOWAWAY   the tube loads and the bubble slides down exactly as it
 *                  always does - the ordinary pink glow, the ordinary travel,
 *                  the ordinary riser. At the landing there is no reveal:
 *                  a plain bubble RING sits in the dish and EMI is standing
 *                  inside it. Click her (or press the POP key) and the ring
 *                  pops, she plays her pet reaction and teleports home.
 *                  Ignore her and the ring deflates and she squints off.
 *   THE FILE       the load glow STUTTERS (glitch_swap rgbsplit) and she
 *                  arrives at the midpoint wearing the GLITCH chain and a
 *                  phosphor frame, standing in a cracked-glass halo. There is
 *                  no slide at all. Pat her and a manila FOLDER slaps down
 *                  over the basin with a polaroid clipped to it: a live loop,
 *                  a live spiral, a burst, or a stapled note. It holds, then
 *                  it fades away.
 *
 * WHAT A CAMEO IS NOT: A PLAN SLOT.  It is dealt BETWEEN plan bubbles, from
 * `nextBubble()`, and the cursor does not move while it runs. It never writes
 * `S.bubble`, `S.revealAt`, the tally, the streak, the score or the grade; it
 * never enters `press()`'s reveal branch; a class that runs three of them
 * deals the identical plan in the identical order and grades identically. All
 * it costs is a few seconds of wall time, and this class has no wall clock
 * (the plan IS the length). That is the whole reason the feature is cheap.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - nothing here reads or writes score, streak, the tally,
 *       the plan, the reveal window or the grade. The stats a visit banks
 *       (`visits`, `visitPats`, `visitsIgnored`, `filesOpened`) are the
 *       WIDGET's and are written by the widget alone (CLAUDE.md trap 96: one
 *       writer of the `emi` key). This deck reports numbers; it writes none.
 *   II  input honest  - every node this file mints lives in its own layer
 *       inside nodes.stage at `pointer-events:none`, ring and folder included.
 *       The click that ends a cameo lands on `.emi` itself, which is one of
 *       the two nodes on `#arc-emi` that were already live (trap 59/92: FOUR
 *       pointer-events:auto rules on that layer, and this feature adds none).
 *       The class's own bubble is never moved, resized, hidden or delayed.
 *   III never still   - the ring breathes, the halo cracks, the photo develops,
 *       the card fades out. All of it CSS, so `.suspended` freezes the lot.
 *   IV  images over text - one folder tab, one stamp, and the note card's two
 *       lines. Everything else is drawn.
 *   V   seeded        - three append-only streams off `seed|ic-cameo|`:
 *       `dossier`, `stowaway`, `content`. Both slot rolls are drawn at EVERY
 *       eligible slot in a fixed order (dossier first), so a retake replays
 *       the same cameos at the same slots (CLAUDE.md §5 retake law, trap 40).
 *   VI  exits sacred  - every timer rides `opts.timers` (the game's
 *       pause-aware registry) plus a local set; the spiral's rAF is cancelled
 *       by hand; `pause()` / `cancel()` / `destroy()` send her HOME and leave
 *       no node, no timer and no frame loop behind. `capsOk` false (the
 *       player's bgIntensity exit) rolls nothing at all.
 *   VII strings       - `ic_file_tab`, `ic_file_stamp`, `ic_file_stamp_after`,
 *       `ic_file_note_head`, `ic_file_note_1..3`, all in lex.js, all through
 *       `t`, all <= 96 characters.
 *
 * THE SEAM. `ctx.emi.visit(spec)` is the SHELL's, and it is the only road a
 * game has to the mascot (a game holds `ctx.mood` and may tell her how the
 * room feels; it may never make her move or talk). It answers a handle, or
 * NULL - synchronously - when she refuses: docked, hidden, saying, dragging,
 * busy, a visit already in flight, or inside the shell's own visit floor. On
 * null this deck removes what it painted and the class deals its ordinary
 * bubble as if the roll had never happened. EVERY call is guarded: an older
 * shell has no `ctx.emi` at all, and on that shell this file is inert and the
 * class is byte-identical to the one that shipped without it.
 *
 * THE STYLESHEET is style.js's (the class sheet, one document-level
 * singleton). No new image asset ships with this deck: the folder, the
 * polaroid, the halo and the ring are all gradients and masks.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';
import { IC_LEX } from './lex.js';

/* ============================================================================
 * DIALS - every number this deck has, in one block (design plan §5).
 * Set STOWAWAY_P and DOSSIER_P to 0 and the feature is off with no other
 * change anywhere in the class.
 * ==========================================================================*/
export const IC_CAMEO = Object.freeze({
  /** Per ELIGIBLE slot. Owner ruling 2026-08-29: literal, and the fancier
   *  card is the frequent one - the polaroid is the app's core content and
   *  the pat is the rare warm beat. Swap the two to invert that. */
  STOWAWAY_P: 1 / 20,
  DOSSIER_P: 1 / 8,
  /** Never on the TEACH_GOOD bubbles (matches trickster FIRST_DEAL_IDX). */
  FIRST_SLOT_IDX: 2,
  /** Never in the last two bubbles - the ticket is next. */
  LAST_SLOT_GUARD: 2,
  /** Bubbles since ANY house event (a trickster card OR a cameo). Shared
   *  ledger, so a lie and a cameo can never sit closer than this. */
  MIN_GAP_IDX: 4,
  /** Both cards: no pat by then and she squints and goes home. */
  STOWAWAY_WAIT_MS: 4500,
  DOSSIER_WAIT_MS: 4500,
  /** The photo holds this long before it leaves (owner: about 3 s). */
  DOSSIER_HOLD_MS: 3000,
  /** How long she stays after the shock chain, so she is still there while
   *  the folder is open. handle.end() cuts it short at the exit. */
  DOSSIER_STAY_MS: 6000,
  /** The folder's exit: a plain fade, nothing to sit through. */
  EXIT_MS: 240,
  /** The basin settle after she is home - the plan's own between-bubble beat
   *  (schedule.js GAP_MS), so a cameo hands the tube back on the same rhythm. */
  GAP_MS: 350,
  /** Both cards from Year 1: this is a reward, not a trick. */
  TIER_FROM: 1,
  /** The ring is max(--ic-basin-d, her width + this). */
  RING_PAD_PX: 24,
  /** The polaroid well fades up from white over this. */
  DEVELOP_MS: 400,
  /** An engine that refused glitch_swap still owes us the arrival. */
  GLITCH_FALLBACK_MS: 180,
  /** The spiral canvas is allocated flat, never DPR-scaled (phone law). */
  SPIRAL_PX: 300,
  /** The burst rider on the `burst` content roll. */
  BURST_COUNT: 5,
  BURST_SIZE_PX: 190,
  /** The content roulette. Order is FIXED: the cumulative walk below reads it
   *  in this order, so re-ordering the list would re-deal every seed. */
  CONTENT_ORDER: Object.freeze(['loop', 'spiral', 'burst', 'note']),
  CONTENT_W: Object.freeze({ loop: 0.55, spiral: 0.30, burst: 0.10, note: 0.05 }),
  /** How many note lines lex.js carries (ic_file_note_1..N). */
  NOTE_LINES: 3,
});

/* ---------------------------------------------------------------- helpers */
const clamp01 = (v) => {
  const x = Number(v);
  if (!isFinite(x)) return 0;
  return x < 0 ? 0 : x > 1 ? 1 : x;
};
function nowMs() {
  try {
    if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') {
      return performance.now();
    }
  } catch (e) { /* fall through */ }
  return Date.now();
}
const VIDEO_RE = /\.(mp4|webm|mov|m4v)(\?|#|$)/i;
const isVideoUrl = (u) => typeof u === 'string' && VIDEO_RE.test(u);

/**
 * THE CONTENT ROULETTE, pure and exported for the suite. A cumulative walk in
 * CONTENT_ORDER over CONTENT_W; anything the weights do not cover falls to the
 * last kind, so the table can never answer undefined.
 * @param {number} r 0..1
 * @returns {'loop'|'spiral'|'burst'|'note'}
 */
export function pickContent(r) {
  const order = IC_CAMEO.CONTENT_ORDER;
  const w = IC_CAMEO.CONTENT_W;
  let x = clamp01(r);
  for (let i = 0; i < order.length; i++) {
    x -= Math.max(0, Number(w[order[i]]) || 0);
    if (x < 0) return order[i];
  }
  return order[order.length - 1];
}

/**
 * THE GATE, pure and exported for the suite. A slot is eligible when it is
 * past the teaching bubbles, clear of the ticket, and far enough from the last
 * HOUSE event (this deck's own cameos and the trickster's cards share one
 * ledger - design §6 law 3).
 * @param {number} idx bubble index about to be dealt
 * @param {number} total plan length
 * @param {number} houseLastIdx last index any house event landed on
 * @returns {boolean}
 */
export function slotEligible(idx, total, houseLastIdx) {
  const i = Number(idx);
  const n = Number(total);
  if (!isFinite(i) || !isFinite(n)) return false;
  if (i < IC_CAMEO.FIRST_SLOT_IDX) return false;
  if (i > n - 1 - IC_CAMEO.LAST_SLOT_GUARD) return false;
  const last = isFinite(Number(houseLastIdx)) ? Number(houseLastIdx) : -1e6;
  if (i - last < IC_CAMEO.MIN_GAP_IDX) return false;
  return true;
}

/* ============================================================================
 * THE DECK
 * ----------------------------------------------------------------------------
 * @param {Object}   o
 * @param {string}   o.seed        the class seed (streams hang off it)
 * @param {number}   o.gradeTier   1..4
 * @param {boolean}  o.reduced     prefers-reduced-motion (or motionLevel 0)
 * @param {number=}  o.motionLevel
 * @param {boolean=} o.touch       coarse pointer (the HOST's answer outranks a probe)
 * @param {Object}   o.nodes       render.nodes (stage is the only one used)
 * @param {Object}   o.engine      the game's null-safe deckEngine
 * @param {Object}   o.timers      deckTimers {after, every, clear}
 * @param {Function} o.capsOk      () => bool (bgIntensity 0 disarms the deck)
 * @param {Function} o.isHalted    () => bool
 * @param {Function=} o.emiOf      () => ctx.emi | null  (the SHELL's seam)
 * @param {Function=} o.pool       () => the class's asset pool (or null)
 * @param {Function=} o.t          lexicon reader
 * @param {Function=} o.log
 * @returns {Object|null}
 * ==========================================================================*/
export function createIcCameo(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const nodes = opts.nodes || {};
  const tier = Math.max(1, Math.min(4, Math.round(Number(opts.gradeTier) || 1)));
  const motionOff = Number(opts.motionLevel) === 0;
  const reduced = !!opts.reduced || motionOff;
  const touch = !!opts.touch;
  const engine = opts.engine || null;
  const timers = opts.timers || null;
  const capsFn = typeof opts.capsOk === 'function' ? opts.capsOk : () => opts.capsOk !== false;
  const capsOkNow = () => { try { return !!capsFn(); } catch (e) { return false; } };
  const isHalted = typeof opts.isHalted === 'function' ? opts.isHalted : () => false;
  const poolOf = typeof opts.pool === 'function' ? opts.pool : () => null;
  /* No private fallbacks here: lex.js is the one place the folder's words live, so a
     host with no translation for a key still prints the canon row (index.js hands
     ctx.lexicon IC_LEX[k] when the deck passes no fallback). */
  const t = typeof opts.t === 'function' ? opts.t : (k) => (IC_LEX[k] == null ? k : IC_LEX[k]);

  const hasDom = () => {
    try { return typeof document !== 'undefined' && !!document && typeof document.createElement === 'function'; }
    catch (e) { return false; }
  };
  const armed = !!nodes.stage && !!timers && typeof timers.after === 'function' && hasDom();
  if (!armed) { say('cameo: refused (no stage / no timers / no document)'); return null; }

  /** The SHELL's seam, resolved LIVE and guarded at every call. A shell with
   *  no `ctx.emi` (or one that lost it) makes this whole deck inert. */
  const emiOf = typeof opts.emiOf === 'function' ? opts.emiOf : () => (opts.emi || null);
  function emiApi() {
    try {
      const e = emiOf();
      return (e && typeof e.visit === 'function') ? e : null;
    } catch (e) { return null; }
  }
  function fileTag() {
    try {
      const e = emiOf();
      if (!e || typeof e.fileTag !== 'function') return null;
      const v = e.fileTag();
      return (typeof v === 'string' && v) ? v : null;
    } catch (e) { return null; }
  }

  /* ---------------------------------------------------------------- state */
  let destroyed = false;
  let paused = false;
  let started = false;
  let heat = 0.2;
  let layer = null;
  let ring = null;
  let halo = null;
  let folder = null;
  let leaving = null;           // the folder mid-fade: still ours to remove
  let handle = null;            // the live visit handle, or null
  let visiting = null;          // 'stowaway' | 'dossier' | null
  let rafId = 0;
  let rafKind = '';             // 'raf' | 'timer'
  const live = new Set();
  const counts = { rolled: 0, stowaway: 0, dossier: 0, refused: 0, pats: 0, snubs: 0, files: 0 };
  let lastContent = null;
  let lastJackpot = false;

  /* --------------------------------------------------------------- timers */
  const cancelFn = timers && (timers.clear || timers.cancel);
  function after(ms, fn) {
    if (destroyed) return 0;
    let id = 0;
    id = timers.after(ms, () => {
      live.delete(id);
      if (!destroyed) { try { fn(); } catch (e) { /* a cosmetic throw is not a class failure */ } }
    });
    if (id) live.add(id);
    return id;
  }
  function clear(id) {
    if (!id) return;
    live.delete(id);
    try { if (typeof cancelFn === 'function') cancelFn.call(timers, id); } catch (e) { /* noop */ }
  }
  function clearAllTimers() {
    for (const id of Array.from(live)) clear(id);
    live.clear();
  }

  /* ------------------------------------------------------------ frame loop */
  /** The spiral's own loop. rAF where there is one, a 50ms chain where there
   *  is not (a harness, an old webview). Cancelled by hand everywhere. */
  function startFrames(fn) {
    stopFrames();
    let raf = null;
    try {
      if (typeof requestAnimationFrame === 'function') raf = requestAnimationFrame;
      else if (typeof window !== 'undefined' && window && typeof window.requestAnimationFrame === 'function') {
        raf = window.requestAnimationFrame.bind(window);
      }
    } catch (e) { raf = null; }
    if (raf) {
      rafKind = 'raf';
      const step = () => {
        if (destroyed || paused || rafKind !== 'raf' || !rafId) return;
        try { fn(); } catch (e) { /* noop */ }
        try { rafId = raf(step); } catch (e) { rafId = 0; }
      };
      try { rafId = raf(step); } catch (e) { rafId = 0; }
      return;
    }
    rafKind = 'timer';
    const tick = () => {
      if (destroyed || paused || rafKind !== 'timer') return;
      try { fn(); } catch (e) { /* noop */ }
      rafId = after(50, tick);
    };
    rafId = after(50, tick);
  }
  function stopFrames() {
    if (!rafId) { rafKind = ''; return; }
    if (rafKind === 'raf') {
      try {
        if (typeof cancelAnimationFrame === 'function') cancelAnimationFrame(rafId);
        else if (typeof window !== 'undefined' && window && window.cancelAnimationFrame) window.cancelAnimationFrame(rafId);
      } catch (e) { /* noop */ }
    } else clear(rafId);
    rafId = 0;
    rafKind = '';
  }

  /* ----------------------------------------------------------- the streams */
  const seedBase = String(opts.seed == null ? 'ic' : opts.seed) + '|ic-cameo|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  /* -------------------------------------------------------------- the DOM */
  const el = (tag, cls, parent) => {
    try {
      const n = document.createElement(tag);
      if (!n) return null;
      if (cls) n.className = cls;
      if (parent && parent.appendChild) parent.appendChild(n);
      return n;
    } catch (e) { return null; }
  };
  const setCls = (n, cls, on) => {
    try { if (n && n.classList) n.classList[on ? 'add' : 'remove'](cls); } catch (e) { /* noop */ }
  };
  const setVar = (n, k, v) => {
    try { if (n && n.style && n.style.setProperty) n.style.setProperty(k, v); } catch (e) { /* noop */ }
  };
  const drop = (n) => { try { if (n && n.remove) n.remove(); } catch (e) { /* noop */ } };

  /* NODES ON THEIR WAY OUT. A popped ring and a faded halo still own a few
     hundred ms of screen after this deck has let go of them, and the timer
     that finally removes one is one of OURS - so a teardown, which kills
     every timer this deck owns, would strand them in the stage forever. That
     is a real leak and it is invisible: the class carries on, the mascot goes
     home, and a dead ring sits in the dish until the game is destroyed.
     Anything mid-exit is parked here and swept unconditionally. */
  const parting = new Set();
  function part(node, ms) {
    if (!node) return;
    parting.add(node);
    after(ms, () => { parting.delete(node); drop(node); });
  }
  function sweepParting() {
    for (const p of Array.from(parting)) drop(p);
    parting.clear();
  }

  function ensureLayer() {
    if (layer) return layer;
    layer = el('div', 'g-ic-cameo', nodes.stage);
    if (layer) setCls(layer, 'is-reduced', reduced);
    return layer;
  }

  /** Her live width, read off the shell's own node. A READ, never a reach:
   *  the deck never touches `#arc-emi`, it only measures what is already
   *  standing there so the ring can never be smaller than she is. */
  function emiWidth() {
    try {
      const n = document.querySelector && document.querySelector('#arc-emi .emi');
      if (!n || typeof n.getBoundingClientRect !== 'function') return 0;
      const r = n.getBoundingClientRect();
      const w = Math.max(Number(r.width) || 0, Number(r.height) || 0);
      return isFinite(w) ? w : 0;
    } catch (e) { return 0; }
  }

  /** The basin ring's diameter, as CSS so `--ic-basin-d` stays the one dial. */
  function ringSizeCss() {
    const w = Math.round(emiWidth() + IC_CAMEO.RING_PAD_PX);
    if (w > IC_CAMEO.RING_PAD_PX) return 'max(var(--ic-basin-d), ' + w + 'px)';
    return 'var(--ic-basin-d)';
  }

  /** A rect the SHELL resolves at FIRE TIME (the apparate law). */
  function rectOf(node) {
    return () => {
      try {
        if (!node || typeof node.getBoundingClientRect !== 'function') return null;
        const r = node.getBoundingClientRect();
        const box = {
          left: Number(r.left) || 0,
          top: Number(r.top) || 0,
          width: Number(r.width) || 0,
          height: Number(r.height) || 0,
        };
        if (box.width <= 0 || box.height <= 0) return null;
        return box;
      } catch (e) { return null; }
    };
  }

  /* ------------------------------------------------------------- the sound */
  const cue = (name, level, extra) => {
    try { if (engine && typeof engine.audio === 'function') engine.audio(name, level, extra); }
    catch (e) { /* noop */ }
  };
  const fire = (kind, o2) => {
    try { return (engine && typeof engine.fire === 'function') ? engine.fire(kind, o2 || {}) : null; }
    catch (e) { return null; }
  };
  const ceremony = (kind, o2) => {
    try { if (engine && typeof engine.ceremony === 'function') engine.ceremony(kind, o2 || {}); }
    catch (e) { /* noop */ }
  };

  /* ============================================================== THE ROLL */
  /**
   * Both rolls, every eligible slot, in a FIXED order (dossier then stowaway)
   * on two independent streams - that is what makes a retake replay the same
   * cameos at the same slots. An ineligible slot rolls NOTHING, so the streams
   * stay aligned with the eligible-slot sequence, which is itself a pure
   * function of the plan and the shared house ledger.
   *
   * @param {number} idx the bubble index about to be dealt
   * @param {number} total plan length
   * @param {number} houseLastIdx last index any house event landed on
   * @returns {'dossier'|'stowaway'|null}
   */
  function rollFor(idx, total, houseLastIdx) {
    if (destroyed || paused || !started) return null;
    if (tier < IC_CAMEO.TIER_FROM) return null;
    if (visiting || handle) return null;
    if (isHalted()) return null;
    /* bgIntensity 0 is the player's VISUAL exit and a cameo is a set piece:
       with the lights off, nothing is rolled and nothing is spent. */
    if (!capsOkNow()) return null;
    /* no shell seam = no feature. Byte-identical to the class without it. */
    if (!emiApi()) return null;
    if (!slotEligible(idx, total, houseLastIdx)) return null;
    counts.rolled += 1;
    const d = roll('dossier');
    const s = roll('stowaway');
    if (d < IC_CAMEO.DOSSIER_P) return 'dossier';
    if (s < IC_CAMEO.STOWAWAY_P) return 'stowaway';
    return null;
  }

  /* =========================================================== THE VISIT */
  /**
   * Ask the shell. Answers the handle, or null when she refuses - and on null
   * this deck has already put back whatever it painted.
   */
  function askVisit(spec, onHandle, onNull) {
    const api = emiApi();
    if (!api) { counts.refused += 1; if (onNull) onNull('no-seam'); return null; }
    let h = null;
    try { h = api.visit(spec) || null; }
    catch (e) { say('cameo: visit threw (' + ((e && e.message) || e) + ')'); h = null; }
    if (!h) {
      counts.refused += 1;
      say('cameo: ' + spec.kind + ' refused by the shell - dealing the ordinary bubble');
      if (onNull) onNull('refused');
      return null;
    }
    handle = h;
    if (typeof onHandle === 'function') { try { onHandle(h); } catch (e) { /* noop */ } }
    return h;
  }

  /** One place that ends a cameo, whichever door it came out of. */
  function finish(reason, onDone) {
    handle = null;
    visiting = null;
    stopFrames();
    if (typeof onDone === 'function') { try { onDone(reason); } catch (e) { /* noop */ } }
  }

  /* ======================================================== THE STOWAWAY */
  /**
   * Called by index.js AT THE LANDING, after the ordinary load and the
   * ordinary slide have run. Answers TRUE when she took the trip (the class
   * waits for onDone) and FALSE when she refused (the class reveals the
   * bubble the slide was carrying, immediately, as if nothing happened).
   */
  function landStowaway(o2 = {}) {
    if (destroyed || paused || isHalted()) return false;
    const host = ensureLayer();
    if (!host) return false;
    ring = el('i', 'g-ic-cameo-ring', host);
    if (!ring) return false;
    setVar(ring, '--ic-cam-d', ringSizeCss());
    setCls(ring, 'on', true);

    visiting = 'stowaway';
    const spec = {
      kind: 'stowaway',
      rect: rectOf(ring),
      face: '^_~',
      phosphor: false,
      waitMs: IC_CAMEO.STOWAWAY_WAIT_MS,
      patChain: 'love',
      stayMs: 0,
      onArrive: () => { cue('emi_blip', 0.35); },
      onPat: () => {
        counts.pats += 1;
        setCls(ring, 'on', false);
        setCls(ring, 'pop', true);
        cue('bubble_pop', 0.4);
      },
      onDone: (why) => {
        if (why === 'timeout') {
          counts.snubs += 1;
          setCls(ring, 'on', false);
          setCls(ring, 'deflate', true);      // no pop: she was ignored
        }
        const r = ring;
        ring = null;
        part(r, 360);
        finish(why, o2.onDone);
      },
    };
    const h = askVisit(spec, o2.onHandle, () => {
      drop(ring);
      ring = null;
      visiting = null;
    });
    if (!h) return false;
    counts.stowaway += 1;
    say('cameo: stowaway in the basin');
    return true;
  }

  /* =========================================================== THE FILE */
  /**
   * Called by index.js INSTEAD of the ordinary deal. There is no slide: the
   * load glow stutters and she arrives at the glitch's midpoint. `onNull` is
   * the class's road back to an ordinary bubble at the same index.
   */
  function dealDossier(o2 = {}) {
    if (destroyed || paused || isHalted()) { if (o2.onNull) o2.onNull('halted'); return false; }
    visiting = 'dossier';
    let arrived = false;
    const arrive = () => {
      if (arrived) return;
      arrived = true;
      if (destroyed || paused || isHalted() || visiting !== 'dossier') { if (o2.onNull) o2.onNull('halted'); visiting = null; return; }
      const host = ensureLayer();
      if (host) {
        halo = el('i', 'g-ic-cameo-halo', host);
        if (halo) {
          setVar(halo, '--ic-cam-d', ringSizeCss());
          setCls(halo, 'on', true);
        }
      }
      const spec = {
        kind: 'dossier',
        rect: rectOf(halo || nodes.stage),
        face: 'glitch',
        phosphor: true,
        waitMs: IC_CAMEO.DOSSIER_WAIT_MS,
        patChain: 'shock',
        stayMs: IC_CAMEO.DOSSIER_STAY_MS,
        onArrive: () => { cue('emi_blip', 0.35); },
        onPat: () => { counts.pats += 1; openFolder(); },
        onDone: (why) => {
          if (why === 'timeout') counts.snubs += 1;
          const hl = halo;
          halo = null;
          setCls(hl, 'on', false);
          part(hl, 300);
          finish(why, o2.onDone);
        },
      };
      const h = askVisit(spec, o2.onHandle, () => {
        drop(halo);
        halo = null;
        visiting = null;
        if (o2.onNull) o2.onNull('refused');
      });
      if (h) { counts.dossier += 1; say('cameo: the file arrives'); }
    };

    cue('glitch', 0.4);
    const went = fire('glitch_swap', { variant: 'rgbsplit', onSwap: arrive });
    /* trap 10: an undeclared or budget-refused effect no-ops SILENTLY, so a
       null answer must never swallow the arrival - she comes anyway. */
    if (!went) after(IC_CAMEO.GLITCH_FALLBACK_MS, arrive);
    return true;
  }

  /* ------------------------------------------------------------ THE FOLDER */
  function openFolder() {
    if (destroyed || paused || isHalted()) return;
    const host = ensureLayer();
    if (!host || folder) return;

    /* the jackpot rider is the ENGINE's own variable-ratio roll, so it replays
       per seed like everything else here. Truthy `.jackpot` fans a second
       polaroid and doubles the hold. */
    let jack = null;
    try { jack = (engine && typeof engine.rewardRoll === 'function') ? engine.rewardRoll({ heat }) : null; }
    catch (e) { jack = null; }
    const jackpot = !!(jack && jack.jackpot);
    lastJackpot = jackpot;

    folder = el('div', 'g-ic-file', host);
    if (!folder) return;
    if (reduced) setCls(folder, 'is-reduced', true);
    if (touch) setCls(folder, 'is-touch', true);
    counts.files += 1;
    cue('paper', 0.3);

    /* THE TAB. Innocent until the lab: `field notes` / `for you`. After the
       seep's reveal the SAME folder wears the subject code and the stamp is
       a black redaction bar. One seed, curdled on replay. */
    const tag = fileTag();
    const tab = el('i', 'g-ic-file-tab', folder);
    if (tab) tab.textContent = tag || t('ic_file_tab');
    const stamp = el('i', 'g-ic-file-stamp', folder);
    if (stamp) {
      if (tag) {
        setCls(stamp, 'redacted', true);
        stamp.textContent = t('ic_file_stamp_after');
      } else {
        stamp.textContent = t('ic_file_stamp');
      }
    }

    const kind = pickContent(roll('content'));
    lastContent = kind;
    const shot = mountPhoto(folder, kind, 0);
    if (jackpot && kind !== 'note') {
      mountPhoto(folder, kind === 'burst' ? 'loop' : kind, 1);
      ceremony('jackpot', { intensity: 0.6 });
    }
    if (shot !== 'note') cue('shutter', 0.4);

    if (kind === 'burst') burstAround();

    const holdMs = IC_CAMEO.DOSSIER_HOLD_MS * (jackpot ? 2 : 1);
    after(holdMs, dismiss);
  }

  /** One polaroid (or the stapled note), clipped to the folder. */
  function mountPhoto(host, kind, slot) {
    if (kind === 'note') {
      const note = el('div', 'g-ic-file-note', host);
      if (!note) return 'note';
      const head = el('b', 'g-ic-file-note-head', note);
      if (head) head.textContent = t('ic_file_note_head');
      const line = el('span', 'g-ic-file-note-line', note);
      if (line) {
        const n = 1 + Math.floor(roll('note') * IC_CAMEO.NOTE_LINES);
        const key = 'ic_file_note_' + Math.min(IC_CAMEO.NOTE_LINES, Math.max(1, n));
        line.textContent = t(key);
      }
      return 'note';
    }
    const card = el('div', 'g-ic-polaroid' + (slot ? ' fan' : ''), host);
    if (!card) return kind;
    const well = el('div', 'g-ic-polaroid-well', card);
    el('i', 'g-ic-polaroid-cap', card);
    if (!well) return kind;
    if (kind === 'spiral') mountSpiral(well);
    else mountMedia(well, kind === 'burst' ? 'still' : 'loop');
    const dev = el('i', 'g-ic-polaroid-dev', well);
    if (dev) setVar(dev, '--ic-cam-dev', IC_CAMEO.DEVELOP_MS + 'ms');
    return kind;
  }

  /**
   * THE LIVE LOOP. `mediaEl`'s SHAPE, written out here on purpose: a game may
   * not import engine internals, and the one thing that shape buys us is a
   * muted looping <video> for the mp4/webm a remote pool hands out on iOS.
   * The pool already honours consent, the offline fallback and the decoder
   * budget, so a `loop` draw may legitimately answer a still - design for it.
   */
  function mountMedia(well, want) {
    let got = null;
    try {
      const pool = poolOf();
      if (pool && typeof pool.next === 'function') got = pool.next(want) || null;
    } catch (e) { got = null; }
    const url = got && got.url ? String(got.url) : '';
    if (!url) { setCls(well, 'empty', true); return; }
    const video = isVideoUrl(url);
    const n = el(video ? 'video' : 'img', 'g-ic-polaroid-media', null);
    if (!n) { setCls(well, 'empty', true); return; }
    try {
      if (video) {
        n.muted = true; n.loop = true; n.autoplay = true; n.playsInline = true;
        n.setAttribute('muted', ''); n.setAttribute('loop', '');
        n.setAttribute('autoplay', ''); n.setAttribute('playsinline', '');
        n.setAttribute('preload', 'auto');
        n.disablePictureInPicture = true;
      } else {
        n.decoding = 'async';
        n.alt = '';
        n.setAttribute('draggable', 'false');
      }
    } catch (e) { /* an attribute a webview refuses is not a failure */ }
    try { n.src = url; } catch (e) { /* noop */ }
    try { well.appendChild(n); } catch (e) { /* noop */ }
    if (video) {
      try { const p = n.play(); if (p && typeof p.catch === 'function') p.catch(() => {}); }
      catch (e) { /* muted autoplay is allowed everywhere; the nudge is belt and braces */ }
    }
  }

  /**
   * THE LIVE SPIRAL. The Loom's own renderer, pulled in on demand - it is
   * three files away and nothing else in this class wants it, so a static
   * import would put it in the class's critical path for a 30% roll. The
   * canvas is allocated FLAT at SPIRAL_PX and never DPR-scaled (phone law).
   * If the import rejects (an old webview, a stripped bundle) the well keeps
   * the CSS conic stand-in it was born with and nothing else changes.
   */
  function mountSpiral(well) {
    const px = IC_CAMEO.SPIRAL_PX;
    const cv = el('canvas', 'g-ic-polaroid-canvas', null);
    if (!cv) { setCls(well, 'conic', true); return; }
    try { cv.width = px; cv.height = px; } catch (e) { /* noop */ }
    setCls(well, 'conic', true);           // the stand-in, until the import lands
    try { well.appendChild(cv); } catch (e) { /* noop */ }
    let ctx2d = null;
    try { ctx2d = typeof cv.getContext === 'function' ? cv.getContext('2d') : null; }
    catch (e) { ctx2d = null; }
    if (!ctx2d) return;

    const params = {
      schema: 1,
      arms: 2 + Math.floor(roll('spiral-arms') * 5),
      turns: 1 + roll('spiral-turns') * 2,
      style: ['log', 'arch', 'ribbon'][Math.floor(roll('spiral-style') * 3)] || 'log',
      duty: 0.35 + roll('spiral-duty') * 0.3,
      speed: 3,
      direction: roll('spiral-dir') < 0.5 ? 1 : -1,
      colors: ['#ff69b4', '#b8a6e8'],
      bg: '#14062b',
    };
    let mod = null;
    try { mod = import('../../../dtrh/shared/loomSpiral.js'); } catch (e) { mod = null; }
    if (!mod || typeof mod.then !== 'function') return;
    mod.then((m) => {
      if (destroyed || !folder || !m || typeof m.drawSpiral !== 'function') return;
      setCls(well, 'conic', false);
      const t0 = nowMs();
      const period = 2400;
      startFrames(() => {
        if (!folder) { stopFrames(); return; }
        const phase = ((nowMs() - t0) % period) / period;
        try { m.drawSpiral(ctx2d, px, params, phase); } catch (e) { stopFrames(); }
      });
    }).catch((e) => { say('cameo: spiral import refused (' + ((e && e.message) || e) + ') - conic stands in'); });
  }

  /** The 10% rider: loops scattered around the folder, decoration only. */
  function burstAround() {
    let x = 50;
    let y = 50;
    try {
      const r = folder && typeof folder.getBoundingClientRect === 'function' ? folder.getBoundingClientRect() : null;
      const vw = (typeof window !== 'undefined' && window && Number(window.innerWidth)) || 0;
      const vh = (typeof window !== 'undefined' && window && Number(window.innerHeight)) || 0;
      if (r && vw > 0 && vh > 0) {
        x = Math.round(((Number(r.left) || 0) + (Number(r.width) || 0) / 2) / vw * 100);
        y = Math.round(((Number(r.top) || 0) + (Number(r.height) || 0) / 2) / vh * 100);
      }
    } catch (e) { /* the middle of the room is a fine answer */ }
    fire('gif_burst', {
      clickSafe: true,
      count: IC_CAMEO.BURST_COUNT,
      x: Math.max(8, Math.min(92, x)),
      y: Math.max(10, Math.min(90, y)),
      sizePx: IC_CAMEO.BURST_SIZE_PX,
    });
  }

  /* --------------------------------------------------------------- THE EXIT */
  /** The card goes. It used to MELT - a wax sheet ran down it for 1.4s and
   *  dripped - and the owner's verdict on 2026-08-31 was "it shows, it's bad,
   *  remove it". It fades now, and that is the whole ceremony. */
  function dismiss() {
    if (destroyed || !folder) return;
    /* the photo stops being alive the moment the card starts leaving: one
       cancelled frame loop, and she is sent home in the same breath. */
    stopFrames();
    const host = folder;
    setCls(host, 'leaving', true);
    leaving = host;
    cue('whoosh', 0.4);
    try { if (handle && typeof handle.end === 'function') handle.end(); }
    catch (e) { /* noop */ }
    folder = null;
    after(IC_CAMEO.EXIT_MS + 120, () => { if (leaving === host) leaving = null; drop(host); });
  }

  /* ------------------------------------------------------------- TEARDOWN */
  /** Everything this deck is holding, gone. She goes HOME, not still. */
  function teardown(sendHome) {
    if (sendHome && handle) {
      try { if (typeof handle.cancel === 'function') handle.cancel(); } catch (e) { /* noop */ }
    }
    handle = null;
    visiting = null;
    stopFrames();
    clearAllTimers();
    drop(ring); ring = null;
    drop(halo); halo = null;
    drop(folder); folder = null;
    drop(leaving); leaving = null;
    sweepParting();
  }

  /* ============================================================= LIFECYCLE */
  return {
    start() {
      if (started || destroyed) return;
      started = true;
      ensureLayer();
      say('cameo: armed (stowaway 1/' + Math.round(1 / IC_CAMEO.STOWAWAY_P)
        + ', dossier 1/' + Math.round(1 / IC_CAMEO.DOSSIER_P) + ')');
    },

    setHeat(h) { heat = clamp01(h); },

    rollFor,
    landStowaway,
    dealDossier,

    /** True while a cameo owns the basin. */
    active() { return !!visiting || !!handle; },

    /** The go key's road, for a class that would rather not hold the handle. */
    patKey() {
      if (!handle || typeof handle.pat !== 'function') return false;
      try { handle.pat('key'); return true; } catch (e) { return false; }
    },

    /** A freeze, a suspend, a class end: straight home, layer down. */
    cancel() { teardown(true); },

    pause() {
      if (paused) return;
      paused = true;
      teardown(true);
    },

    resume() {
      paused = false;
      /* nothing is re-armed: a cameo is not a plan slot and is never owed. */
    },

    destroy() {
      if (destroyed) return;
      destroyed = true;
      teardown(true);
      drop(layer);
      layer = null;
      streams.clear();
    },

    diagnostics() {
      return {
        armed: true,
        tier,
        reduced,
        seam: !!emiApi(),
        active: !!visiting || !!handle,
        content: lastContent,
        jackpot: lastJackpot,
        counts: Object.assign({}, counts),
      };
    },
  };
}

export default createIcCameo;
