/* ============================================================================
 * games/daily-trigger/casino.js - DECK II of the House Rules for homeroom, plus
 * the Rake's "tonight only": the lighting rig, and the fact that the WHOLE
 * WORLD shares it.
 *
 *   TONIGHT ONLY    every cosmetic here is seeded from the UTC DATE ALONE -
 *                   never the tier, never the class seed - so the room every
 *                   player on earth walks into tonight is the SAME room, and
 *                   it quietly expires at midnight. The daily word is a global
 *                   ritual; now the hall it hangs in is too ("the hall is gold
 *                   tonight" is a thing two players can say to each other).
 *                   A retake replays the identical night by construction.
 *   MARQUEE CHASE   a bulb-chase frame around the chalkboard slab. Crawls
 *                   lazily at rung 0, spins up as the ladder climbs (pace and
 *                   presence ride the ladder's own heat scalar), turns gold
 *                   for the absorb ceremony, and sighs out - never cuts - on
 *                   detention.
 *   THE ALMOST      near-miss staging: on a "one letter away" row the solved
 *                   row's chalk underline STARTS to draw, dies at ~62%, and
 *                   evaporates. The reel stopped one short. Row-level theatre
 *                   only - a cell mark is never repainted by an effect (the
 *                   index.js contract), and this never touches one.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - nothing here reads or writes rows/marks/grade/share;
 *       index.js calls in AFTER its own accounting. The share grid is sacred.
 *   II  input honest  - every node is pointer-events:none, and nothing here
 *       is ever laid over the keyboard (input-trust law: the desk stays bare).
 *   V   seeded        - per-tag mulberry32 streams off 'dt-night|' + dateUtc
 *       (the trickster.js discipline; core makeTaggedRoll clusters). The DATE
 *       is the whole seed ON PURPOSE - see TONIGHT ONLY above.
 *   VI  exits sacred  - bgIntensity 0 disarms the rig; reduced motion keeps a
 *       static dim frame (the shell freezes the CSS anyway, we drop alpha and
 *       skip the almost); freeze() rides pause/suspend; timers live in the
 *       game's registry.
 *   VII strings      - this file renders no text at all.
 *
 * ENGINE PLACEMENT: game-local BY CHOICE (same call as lost-and-found: the
 * marquee knows this room's slab geometry and z-order). When a third game
 * wants a marquee, lift the bar choreography into engine/sustained.js as a
 * `marquee` kind and leave the geometry in the games.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const DT_CASINO = Object.freeze({
  /** Marquee pace band: heat 0 -> lazy crawl, heat 1 -> hungry. Seconds. */
  MQ_T_SLOW: 2.8,
  MQ_T_FAST: 0.9,
  /** Presence band (opacity): homeroom whispers before it shouts. */
  MQ_A_LO: 0.22,
  MQ_A_HI: 0.8,
  /** The absorb ceremony: gold, and faster than heat could ever push it. */
  MQ_T_GOLD: 0.5,
  MQ_A_GOLD: 0.95,
  /** Payout pulse length (outlives the .6s CSS animation). */
  FLASH_MS: 700,
  /** The almost: how long the dying chalk line lingers before evaporating. */
  ALMOST_MS: 1150,
  /** Off-arc "bonus round" nights: ~1 night in 16 the hall leaves the brand
   *  arc entirely (teal). Rare enough to be an event, seeded so it is the
   *  same event for everyone. */
  OFF_ARC: 0.06,
});

function el(tag, cls) {
  try {
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    return n;
  } catch (e) { return null; }
}
function clamp01(v) { const n = Number(v) || 0; return n < 0 ? 0 : n > 1 ? 1 : n; }

/**
 * @param {Object} o
 * @param {string}   o.dateUtc   the day's UTC date string - THE seed (global)
 * @param {Object}   o.slab      the chalkboard slab element (marquee host)
 * @param {Object}   o.wrap      the game root (identity custom props land here)
 * @param {Object}   o.timers    {after(ms,fn)->id, cancel(id)}
 * @param {boolean}  o.reduced   reduced motion
 * @param {boolean}  o.capsOk    false when bgIntensity is capped to 0
 * @param {Function=} o.log
 */
export function createDtCasino(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const timers = opts.timers;
  const reduced = !!opts.reduced;
  const armed = !!opts.capsOk && !!opts.slab && !!opts.wrap && !!timers
    && typeof document !== 'undefined';

  /* Per-tag seeded streams. DATE ONLY: the night belongs to everyone. */
  const seedBase = 'dt-night|' + String(opts.dateUtc || '') + '|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  let destroyed = false;
  let mq = null;
  let bars = [];
  let goldOn = false;
  let lastHeat = 0;
  let flashTimer = 0;
  let night = null;             // the resolved identity, for diagnostics

  /* ------------------------------------------------- tonight's identity */
  /**
   * The nightly redecoration, applied as custom props the stylesheet already
   * falls back from. Hue pair rides the violet->rose arc (the house palette
   * draw from Deck I); rare off-arc nights land teal, like hitting a bonus
   * round just by showing up.
   */
  function dressNight() {
    if (!armed || !opts.wrap.style) return;
    const offArc = roll('arc') < DT_CASINO.OFF_ARC;
    // violet->rose: hue 268..338; off-arc: teal 172..188
    const hue = offArc ? 172 + roll('hue') * 16 : 268 + roll('hue') * 70;
    const warm = 36 + roll('warm') * 14;             // lamp warmth 36..50 (amber band)
    const lampA = 0.16 + roll('lampa') * 0.12;
    const neonA = 0.2 + roll('neona') * 0.14;
    const set = (k, v) => { try { opts.wrap.style.setProperty(k, v); } catch (e) { /* ignore */ } };
    set('--dt-n-neon', 'hsla(' + hue.toFixed(0) + ',86%,66%,' + neonA.toFixed(2) + ')');
    set('--dt-n-lamp', 'hsla(' + warm.toFixed(0) + ',72%,62%,' + lampA.toFixed(2) + ')');
    set('--dt-n-mq', 'hsl(' + hue.toFixed(0) + ',86%,68%)');
    set('--dt-n-tilt', ((roll('tilt') - 0.5) * 1.6).toFixed(2) + 'deg');
    night = { hue: Math.round(hue), offArc, warm: Math.round(warm) };
    say('casino: tonight hue ' + night.hue + (offArc ? ' (OFF-ARC night)' : ''));
  }

  /* ------------------------------------------------------------ marquee */
  function mount() {
    if (!armed || mq || !opts.slab.appendChild) return;
    mq = el('div', 'g-dt-mq');
    if (!mq) return;
    bars = [];
    for (const cls of ['mq-t', 'mq-r', 'mq-b', 'mq-l']) {
      const bar = el('i', cls);
      if (!bar) continue;
      bars.push(bar);
      mq.appendChild(bar);
    }
    // Seeded phase: tonight's chase opens on tonight's bulb, everywhere.
    if (mq.style) mq.style.setProperty('--g-dt-mqp', (roll('mq-phase') * -2.8).toFixed(2) + 's');
    opts.slab.appendChild(mq);
  }

  function paint() {
    if (!mq || !mq.style) return;
    const t = goldOn ? DT_CASINO.MQ_T_GOLD
      : DT_CASINO.MQ_T_SLOW - (DT_CASINO.MQ_T_SLOW - DT_CASINO.MQ_T_FAST) * lastHeat;
    const a = goldOn ? DT_CASINO.MQ_A_GOLD
      : DT_CASINO.MQ_A_LO + (DT_CASINO.MQ_A_HI - DT_CASINO.MQ_A_LO) * lastHeat;
    mq.style.setProperty('--g-dt-mqt', t.toFixed(2) + 's');
    mq.style.setProperty('--g-dt-mqa', a.toFixed(2));
  }

  /* ---------------------------------------------------------------- api */
  return {
    /** Dress the night + light the frame. Call once from start(). */
    start() {
      if (!armed || destroyed) { say('casino: disarmed'); return; }
      dressNight();
      mount();
      paint();
      say('casino: marquee lit');
    },

    /** Ride the ladder's own heat scalar. index.js calls on open()/miss(). */
    setHeat(h) {
      lastHeat = clamp01(h);
      paint();
    },

    /** The absorb ceremony: gold frame, frantic chase. */
    gold(on) {
      if (!mq || !mq.classList) return;
      goldOn = !!on;
      if (goldOn) mq.classList.add('g-dt-mq-gold'); else mq.classList.remove('g-dt-mq-gold');
      paint();
    },

    /** A strong row pays light: one pulse, brighter for longer hit chains. */
    payout(n) {
      if (!mq || !mq.classList) return;
      mq.classList.remove('g-dt-mq-flash');
      if (typeof mq.offsetWidth === 'number') void mq.offsetWidth;
      mq.style.setProperty('--g-dt-mqf', String(1 + 0.14 * Math.max(1, Math.min(5, Number(n) || 1))));
      mq.classList.add('g-dt-mq-flash');
      if (flashTimer) timers.cancel(flashTimer);
      flashTimer = timers.after(DT_CASINO.FLASH_MS, () => {
        if (mq && mq.classList) mq.classList.remove('g-dt-mq-flash');
      });
    },

    /**
     * Near-miss staging: the solved underline starts to draw under the row
     * and dies partway. The reel stopped one short. Never a cell repaint.
     */
    almost(rowEl) {
      if (!armed || destroyed || reduced || !rowEl || !rowEl.appendChild) return;
      const line = el('span', 'g-dt-almostline');
      if (!line) return;
      rowEl.appendChild(line);
      timers.after(DT_CASINO.ALMOST_MS, () => { try { line.remove(); } catch (e) { /* ignore */ } });
    },

    /** pause / suspend: the chase freezes with the room. */
    freeze(on) {
      for (const bar of bars) {
        if (!bar || !bar.style) continue;
        try { bar.style.animationPlayState = on ? 'paused' : 'running'; } catch (e) { /* ignore */ }
      }
    },

    /** Detention is never silence: the frame sighs out instead of cutting. */
    dimOut() {
      goldOn = false;
      if (mq && mq.classList) {
        mq.classList.remove('g-dt-mq-gold', 'g-dt-mq-flash');
        mq.classList.add('g-dt-mq-out');
      }
    },

    destroy() {
      destroyed = true;
      if (flashTimer && timers) { timers.cancel(flashTimer); flashTimer = 0; }
      if (mq) { try { mq.remove(); } catch (e) { /* ignore */ } }
      mq = null; bars = [];
    },

    /** Diagnostics for the harness; not part of the module contract. */
    diagnostics() {
      return { armed, marquee: !!mq, gold: goldOn, heat: lastHeat, night };
    },
  };
}

export default createDtCasino;
