/* ============================================================================
 * games/lost-and-found/casino.js - DECK II of the House Rules: the lighting
 * rig. The Trickster (trickster.js) lies to the player; this file pays them.
 *
 *   MARQUEE CHASE   a bulb-chase frame around the wall. Crawls lazily at low
 *                   heat, spins up as the class heats, turns gold and frantic
 *                   for the final bell. It is a FRAME: the board must stay
 *                   scannable, so alpha and pace ride the same heat scalar the
 *                   Distraction Engine does and never exceed it.
 *   THE ALMOST      a warm click stages what it almost was: the target's look
 *                   ghosts through the clicked twin with a slot-reel settle.
 *                   Presentation on the EXISTING warm path - the ledger
 *                   (countWrong) is untouched, and the sanctioned warm shimmer
 *                   on the real target still runs.
 *   KEN-BURNS       tile media drifts (scale + pan) so no frame of the wall is
 *                   ever still (Law III). Media layer ONLY - seats, hitboxes
 *                   and the skin (hue filter / melt transform) never move.
 *   THE DIM-OUT     a loss is acknowledged, never silent: the marquee sighs
 *                   out instead of cutting to black.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - nothing here reads or writes finds/misclicks/grade;
 *       index.js calls in AFTER its own accounting.
 *   II  input honest  - every node is pointer-events:none; ken-burns animates
 *       the media INSIDE a seat, the seat itself never moves.
 *   V   seeded        - ken-burns period and marquee phase draw from per-tag
 *       mulberry32 streams off seed+'|lf-casino' (trickster.js discipline -
 *       core makeTaggedRoll's trailing-counter hash clusters, so not that).
 *   VI  exits sacred  - bgIntensity 0 disarms the whole rig; reduced motion
 *       gets a static dim frame (the shell freezes the CSS animations, we drop
 *       the alpha), no almost overlay, no ken-burns; freeze() rides the same
 *       halt path as the board; all timers live in the game's registry.
 *   VII strings      - the one string this deck owns (lf_royal) is fired from
 *       index.js through ctx.lexicon; this file renders no text at all.
 *
 * ENGINE PLACEMENT: game-local BY CHOICE, candidate for engine promotion.
 * The marquee frames THIS game's wall (it knows --g-lf-top, the vignette
 * z-order, the bell). Jackpot rarity deliberately does NOT add engine tiers:
 * engine/curves.js jackpotSpec already scales bursts/particles/shimmer off
 * `intensity`, so "royal" is intensity 1.0 - a named-tier API would duplicate
 * that dial. When a second game wants a marquee, lift the bar choreography
 * into engine/sustained.js as a `marquee` kind and leave the geometry here.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';
import { el, clamp, clamp01 } from './util.js';

export const CASINO = Object.freeze({
  /** Ken-burns: media-layer drift. Off above this density - a 40-tile wall
   *  (x2-3 wrap clones) is ~100 composited animations; 30 keeps it cheap. */
  KB_MAX_DENSITY: 30,
  KB_DUR_MIN_S: 15,
  KB_DUR_SPAN_S: 8,

  /** The almost: how long the target's look ghosts through the clicked twin. */
  ALMOST_MS: 680,

  /** Marquee pace band: heat 0 -> lazy crawl, heat 1 -> hungry. Seconds. */
  MQ_T_SLOW: 2.6,
  MQ_T_FAST: 0.8,
  /** Marquee presence band (opacity). The frame whispers before it shouts. */
  MQ_A_LO: 0.28,
  MQ_A_HI: 0.85,
  /** The final bell: gold, and faster than heat could ever push it. */
  MQ_T_BELL: 0.45,
  MQ_A_BELL: 0.95,
  /** Found pulse length (must outlive the CSS animation, .6s). */
  FLASH_MS: 700,
});

/**
 * @param {Object} o
 * @param {string}   o.seed       the class seed
 * @param {number}   o.tier       1..4
 * @param {number}   o.density    dealt tile count (ken-burns budget input)
 * @param {Object}   o.board      createBoard() api
 * @param {Object}   o.hud        createHud() api (root = the stage, view)
 * @param {Object}   o.timers     the GAME's timer registry
 * @param {boolean}  o.reduced    reduced motion
 * @param {boolean}  o.lite       coarse pointer / low-quality tier
 * @param {boolean}  o.capsOk     false when bgIntensity is capped to 0
 * @param {Function=} o.cue       the GAME's clamped cue helper (name, level,
 *        extra). A closure, never the engine: this deck cannot reach past the
 *        tier's audio ceiling because it has nothing to reach with.
 * @param {Function=} o.log
 */
export function createCasino(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const board = opts.board;
  const hud = opts.hud;
  const timers = opts.timers;
  const tier = clamp(opts.tier, 1, 4);
  const reduced = !!opts.reduced;
  const armed = !!opts.capsOk && !!board && !!hud && !!timers
    && typeof document !== 'undefined';
  const cue = typeof opts.cue === 'function' ? opts.cue : () => {};

  /* Per-tag seeded streams - the trickster.js discipline (see its header for
     why core makeTaggedRoll is not used). Append-only: new tags never shift
     old streams. */
  const seedBase = String(opts.seed || 'lf') + '|lf-casino|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  /* THE bgIntensity DECOUPLE (owner ruling: a visual dial must not mute the
     school). `armed` keeps its exact old meaning and still gates every visual,
     capsOk included - bgIntensity 0 is the player's VISUAL exit (Law VI), and
     nothing drawn here survives it. The CUE road is separate and gates only on
     the deck being alive: a player who capped the lights to zero still hears
     the frame ring and sigh, because those are the class's beats, not its
     decoration. */
  const sounds = () => !destroyed;

  let destroyed = false;
  let mq = null;                // the marquee frame element
  let bars = [];
  let bellOn = false;
  let kbOn = false;
  let lastHeat = 0;
  let flashTimer = 0;

  /* ------------------------------------------------------------ the marquee */
  function mount() {
    if (!armed || mq || !hud.root || !hud.root.appendChild) return;
    mq = el('div', 'g-lf-mq');
    if (!mq) return;
    bars = [];
    for (const cls of ['mq-t', 'mq-r', 'mq-b', 'mq-l']) {
      const bar = el('i', cls);
      if (!bar) continue;
      bars.push(bar);
      mq.appendChild(bar);
    }
    // Seeded phase: the chase never starts on the same bulb twice.
    if (mq.style) mq.style.setProperty('--g-lf-mqp', (roll('mq-phase') * -2.6).toFixed(2) + 's');
    hud.root.appendChild(mq);
  }

  /** Repaint pace + presence from heat. Bell mode outbids heat. */
  function paint() {
    if (!mq || !mq.style) return;
    const t = bellOn ? CASINO.MQ_T_BELL
      : CASINO.MQ_T_SLOW - (CASINO.MQ_T_SLOW - CASINO.MQ_T_FAST) * lastHeat;
    const a = bellOn ? CASINO.MQ_A_BELL
      : CASINO.MQ_A_LO + (CASINO.MQ_A_HI - CASINO.MQ_A_LO) * lastHeat;
    mq.style.setProperty('--g-lf-mqt', t.toFixed(2) + 's');
    mq.style.setProperty('--g-lf-mqa', a.toFixed(2));
  }

  /* ------------------------------------------------------------- ken-burns */
  function armKenBurns() {
    // Budget: media-layer transforms are composited per element, and a dense
    // wall carries 2-3 wrap clones per tile. 30 tiles (x reps) is the ceiling;
    // the 40-tile tier-4 wall is already the most alive board in the game.
    if (!armed || reduced || opts.lite) return;
    if ((Number(opts.density) || 0) > CASINO.KB_MAX_DENSITY) { say('casino: ken-burns off (density)'); return; }
    if (!board.root || !board.root.classList) return;
    const dur = CASINO.KB_DUR_MIN_S + CASINO.KB_DUR_SPAN_S * roll('kb-dur');
    if (board.root.style) board.root.style.setProperty('--g-lf-kbdur', dur.toFixed(1) + 's');
    board.root.classList.add('g-lf-kb');
    kbOn = true;
  }

  /* ------------------------------------------------------------- the almost */
  /** The visible element copy of a tile (strips wrap; clones sit off-frame). */
  function visibleEl(tile) {
    if (!tile) return null;
    let view = null;
    try { view = hud.view.getBoundingClientRect(); } catch (e) { return board.primaryEl(tile); }
    let fallback = null;
    for (const node of tile.els) {
      if (!node || typeof node.getBoundingClientRect !== 'function') continue;
      let r = null;
      try { r = node.getBoundingClientRect(); } catch (e) { continue; }
      if (!r || !r.width) continue;
      if (!fallback) fallback = node;
      const cx = r.left + r.width / 2;
      if (cx >= view.left && cx <= view.right) return node;
    }
    return fallback || board.primaryEl(tile);
  }

  /* ---------------------------------------------------------------- the api */
  return {
    /** Mount + first paint. Call when the hunt effects start. */
    start() {
      if (!armed || destroyed) { say('casino: disarmed'); return; }
      mount();
      paint();
      armKenBurns();
      say('casino: marquee lit' + (kbOn ? ', ken-burns armed' : ''));
    },

    /** Ride the same scalar the engine does. index.js calls this from setHeat. */
    setHeat(h) {
      lastHeat = clamp01(h);
      paint();
    },

    /** The final bell: gold frame, frantic chase, until stop()/dimOut(). */
    bell(on) {
      // THE FLOURISH: the final bell is a beat the GAME calls in, so it rings
      // on the decoupled road - even in a room whose frame was never lit.
      if (on && !bellOn && sounds()) cue('chime', 0.3, { pitch: 1.12 });
      if (!mq || !mq.classList) return;
      bellOn = !!on;
      if (bellOn) mq.classList.add('g-lf-mq-bell'); else mq.classList.remove('g-lf-mq-bell');
      paint();
    },

    /**
     * A find pays light: one bright pulse over the frame, brighter up the
     * ladder (n = the find just claimed, 1..5). The class flag self-clears.
     */
    payout(n) {
      if (!mq || !mq.classList) return;
      mq.classList.remove('g-lf-mq-flash');
      // restart the CSS animation even on back-to-back finds
      if (typeof mq.offsetWidth === 'number') void mq.offsetWidth;
      mq.style.setProperty('--g-lf-mqf', String(1 + 0.16 * clamp(n, 1, 5)));
      mq.classList.add('g-lf-mq-flash');
      if (flashTimer) timers.cancel(flashTimer);
      flashTimer = timers.after(CASINO.FLASH_MS, () => {
        if (mq && mq.classList) mq.classList.remove('g-lf-mq-flash');
      });
    },

    /**
     * A warm click stages the almost: the target's look ghosts through the
     * clicked twin with a slot-reel settle, then leaves without a trace.
     * paintCb paints a look into a host (hud/board's shared paintLook, passed
     * in so this file never imports board internals it does not use).
     */
    almost(tile, look, paintCb) {
      if (!armed || destroyed || reduced || !tile || typeof paintCb !== 'function') return;
      const host = visibleEl(tile);
      if (!host || !host.appendChild) return;
      const node = el('div', 'g-lf-almost');
      if (!node) return;
      try { paintCb(node, look || {}); } catch (e) { /* the gradient still shows */ }
      host.appendChild(node);
      timers.after(CASINO.ALMOST_MS, () => { try { node.remove(); } catch (e) { /* ignore */ } });
    },

    /** pause / suspend: the chase freezes with the board. */
    freeze(on) {
      for (const bar of bars) {
        if (!bar || !bar.style) continue;
        try { bar.style.animationPlayState = on ? 'paused' : 'running'; } catch (e) { /* ignore */ }
      }
    },

    /** A loss is never silence: the frame sighs out instead of cutting. */
    dimOut() {
      // THE SIGH, and the comment above it is true now: "a loss is never
      // silence". A `slide` pitched DOWN is the frame breathing out - the same
      // whoosh the marquee would make if you could hear a bulb ring dying.
      // Decoupled, so the end of a class is audible at bgIntensity 0 too.
      if (sounds()) cue('slide', 0.3, { pitch: 0.8 });
      bellOn = false;
      if (mq && mq.classList) {
        mq.classList.remove('g-lf-mq-bell', 'g-lf-mq-flash');
        mq.classList.add('g-lf-mq-out');
      }
    },

    /** The class is over; nothing may pulse again. */
    stop() {
      if (flashTimer) { timers.cancel(flashTimer); flashTimer = 0; }
      if (mq && mq.classList) mq.classList.remove('g-lf-mq-flash');
    },

    destroy() {
      destroyed = true;
      if (flashTimer && timers) { timers.cancel(flashTimer); flashTimer = 0; }
      if (kbOn && board && board.root && board.root.classList) {
        try { board.root.classList.remove('g-lf-kb'); } catch (e) { /* ignore */ }
      }
      if (mq) { try { mq.remove(); } catch (e) { /* ignore */ } }
      mq = null; bars = [];
    },

    /** Diagnostics for the harness; not part of the module contract. */
    diagnostics() {
      return { armed, marquee: !!mq, bell: bellOn, kenBurns: kbOn, heat: lastHeat };
    },
  };
}

export default createCasino;
