/* ============================================================================
 * games/deja-vu/casino.js - DECK II of the House Rules for the memory lab:
 * the lighting rig. The trickster (trickster.js) lies about the lab's memory;
 * this file pays the player's.
 *
 *   LAB IDENTITY    seeded per CLASS seed: the monogram etched on every card
 *                   back, the monitor-glow hue pair, the oscilloscope sweep
 *                   period, the filed-tag tilt. No two labs look alike; a
 *                   retake walks into the identical lab (same seed, same
 *                   script - the day's script IS the day's script).
 *   MARQUEE CHASE   a bulb-chase frame around the bench. Crawls at low heat,
 *                   spins up with the class's own heat curve, goes gold and
 *                   frantic for the last pair, and sighs out - never cuts -
 *                   when the bell takes the board.
 *   THE ALMOST      near-miss staging: on a "you KNEW that one" mismatch the
 *                   partner's face ghosts through the wrongly picked card for
 *                   a beat. It shows WHAT you needed, never WHERE it hides -
 *                   the almost, not a hint.
 *   KEN-BURNS       face media drifts (scale + pan) while a card is up or
 *                   locked, so no lit plate is ever still (Law III). Media
 *                   layer ONLY - the card, its hitbox and its marks never move.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - nothing here reads or writes attempts/matches/grade;
 *       index.js calls in AFTER its own accounting.
 *   II  input honest  - every node is pointer-events:none; ken-burns animates
 *       the media INSIDE a card, the card never moves.
 *   V   seeded        - per-tag mulberry32 streams off seed+'|dv-casino' (the
 *       trickster.js discipline; core makeTaggedRoll clusters).
 *   VI  exits sacred  - bgIntensity 0 disarms the rig; reduced motion keeps a
 *       static dim frame, no almost, no ken-burns; the stage's .suspended
 *       rule freezes the chase with everything else; timers live in the
 *       game's registry.
 *   VII strings      - this file renders no text at all.
 *
 * ENGINE PLACEMENT: game-local BY CHOICE (third marquee in the house now -
 * see the lost-and-found header for the promotion plan: bar choreography to
 * engine/sustained.js as a `marquee` kind, geometry stays in the games).
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const DV_CASINO = Object.freeze({
  /** Marquee pace band: heat 0 -> lazy crawl, heat 1 -> hungry. Seconds. */
  MQ_T_SLOW: 2.7,
  MQ_T_FAST: 0.85,
  /** Presence band (opacity): the lab hums before it blazes. */
  MQ_A_LO: 0.24,
  MQ_A_HI: 0.82,
  /** The last pair: gold, and faster than heat could ever push it. */
  MQ_T_BELL: 0.45,
  MQ_A_BELL: 0.95,
  /** Payout pulse length (outlives the .6s CSS animation). */
  FLASH_MS: 700,
  /** The almost: how long the partner's face haunts the wrong card. */
  ALMOST_MS: 620,
  /** Ken-burns drift period band (seconds). */
  KB_DUR_MIN_S: 14,
  KB_DUR_SPAN_S: 8,
  /** Monogram pool for the card backs (font-safe, like the face glyphs). */
  MONOGRAMS: Object.freeze(['◈', '✦', '❖', '◇', '✹']),
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
 * @param {string}   o.seed      the class seed (retakes replay the lab)
 * @param {Object}   o.stage     the stage element (identity props land here)
 * @param {Object}   o.bench     the boardwrap element (marquee host)
 * @param {Object}   o.grid      the grid element (ken-burns class host)
 * @param {Object}   o.timers    {after(ms,fn)->id, cancel(id)}
 * @param {boolean}  o.reduced   reduced motion
 * @param {boolean}  o.capsOk    false when bgIntensity is capped to 0
 * @param {Function=} o.cue     the GAME's clamped audio helper, cue(name, level, extra).
 *                              THE CUE ROAD (W2): deliberately NOT part of armed() -
 *                              bgIntensity 0 is the player's VISUAL exit (Law VI), and a
 *                              visual dial must not mute the school. See sounds() below.
 * @param {Function=} o.log
 */
export function createDvCasino(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const timers = opts.timers;
  const reduced = !!opts.reduced;
  const armed = !!opts.capsOk && !!opts.stage && !!opts.bench && !!timers
    && typeof document !== 'undefined';
  /* THE DECOUPLE (W2): the deck's own audio gate. It shares everything with
   * armed() EXCEPT capsOk - the marquee can be dark and the frame still sighs.
   * Every cue site tests sounds(); no visual site does. */
  const cue = typeof opts.cue === 'function' ? opts.cue : () => {};
  const sounds = () => !destroyed;

  const seedBase = String(opts.seed || 'dv') + '|dv-casino|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  let destroyed = false;
  let mq = null;
  let bars = [];
  let bellOn = false;
  let lastHeat = 0;
  let flashTimer = 0;
  let kbOn = false;
  let identity = null;

  /* -------------------------------------------------- the lab's identity */
  function dressLab() {
    if (!armed || !opts.stage.style) return;
    const set = (k, v) => { try { opts.stage.style.setProperty(k, v); } catch (e) { /* ignore */ } };
    // hue pair on the violet->rose arc; the second glow sits 24-40deg away
    const hueA = 250 + roll('hue') * 90;
    const hueB = hueA + 24 + roll('hue2') * 16;
    const mono = DV_CASINO.MONOGRAMS[Math.floor(roll('mono') * DV_CASINO.MONOGRAMS.length)];
    const sweep = 7 + roll('sweep') * 5;             // 7..12s oscilloscope period
    const tilt = 4 + roll('tilt') * 9;               // filed-tag tilt 4..13deg
    set('--dv-n-glowa', 'hsla(' + hueA.toFixed(0) + ',62%,70%,.16)');
    set('--dv-n-glowb', 'hsla(' + hueB.toFixed(0) + ',70%,66%,.10)');
    set('--dv-n-mono', '"' + mono + '"');
    set('--dv-n-sweep', sweep.toFixed(1) + 's');
    set('--dv-n-tilt', tilt.toFixed(0) + 'deg');
    set('--dv-n-mq', 'hsl(' + hueA.toFixed(0) + ',82%,70%)');
    identity = { hueA: Math.round(hueA), mono, sweep: Math.round(sweep) };
    say('casino: lab dressed (hue ' + identity.hueA + ', monogram ' + mono + ')');
  }

  /* ------------------------------------------------------------ marquee */
  function mount() {
    if (!armed || mq || !opts.bench.appendChild) return;
    mq = el('div', 'g-dv-mq');
    if (!mq) return;
    bars = [];
    for (const cls of ['mq-t', 'mq-r', 'mq-b', 'mq-l']) {
      const bar = el('i', cls);
      if (!bar) continue;
      bars.push(bar);
      mq.appendChild(bar);
    }
    if (mq.style) mq.style.setProperty('--g-dv-mqp', (roll('mq-phase') * -2.7).toFixed(2) + 's');
    opts.bench.appendChild(mq);
  }

  function paint() {
    if (!mq || !mq.style) return;
    const t = bellOn ? DV_CASINO.MQ_T_BELL
      : DV_CASINO.MQ_T_SLOW - (DV_CASINO.MQ_T_SLOW - DV_CASINO.MQ_T_FAST) * lastHeat;
    const a = bellOn ? DV_CASINO.MQ_A_BELL
      : DV_CASINO.MQ_A_LO + (DV_CASINO.MQ_A_HI - DV_CASINO.MQ_A_LO) * lastHeat;
    mq.style.setProperty('--g-dv-mqt', t.toFixed(2) + 's');
    mq.style.setProperty('--g-dv-mqa', a.toFixed(2));
  }

  /* ---------------------------------------------------------- ken-burns */
  function armKenBurns() {
    // A DV board tops out at 20 cells and only up/locked faces animate, so
    // there is no density gate here (the 30-tile ceiling is L&F's problem).
    if (!armed || reduced || kbOn || !opts.grid || !opts.grid.classList) return;
    const dur = DV_CASINO.KB_DUR_MIN_S + DV_CASINO.KB_DUR_SPAN_S * roll('kb-dur');
    if (opts.grid.style) opts.grid.style.setProperty('--g-dv-kbdur', dur.toFixed(1) + 's');
    opts.grid.classList.add('g-dv-kb');
    kbOn = true;
  }

  /* ---------------------------------------------------------------- api */
  return {
    /**
     * Dress the lab + light the frame. Call ONCE A CLASS, at the first board's
     * armPlay - `dressLab()` re-rolls the seeded lab identity off its own
     * stream, so a second call would re-skin the room mid-class. index.js keeps
     * the `casinoLit` latch for exactly that reason (class-length wave).
     */
    start() {
      if (!armed || destroyed) { say('casino: disarmed'); return; }
      dressLab();
      mount();
      paint();
      armKenBurns();
      say('casino: marquee lit' + (kbOn ? ', ken-burns armed' : ''));
    },

    /** Ride the class's own heat curve. index.js calls from heat(). */
    setHeat(h) {
      lastHeat = clamp01(h);
      paint();
    },

    /** The last pair: gold frame, frantic chase, until the BOARD clears (a
     *  class is a run of boards now, and win() drops the frame again). */
    bell(on) {
      if (!mq || !mq.classList) return;
      bellOn = !!on;
      if (bellOn) mq.classList.add('g-dv-mq-bell'); else mq.classList.remove('g-dv-mq-bell');
      paint();
    },

    /** A match pays light: one pulse, brighter as the rack fills (n = matched). */
    payout(n) {
      if (!mq || !mq.classList) return;
      mq.classList.remove('g-dv-mq-flash');
      if (typeof mq.offsetWidth === 'number') void mq.offsetWidth;
      mq.style.setProperty('--g-dv-mqf', String(1 + 0.1 * Math.max(1, Math.min(10, Number(n) || 1))));
      mq.classList.add('g-dv-mq-flash');
      if (flashTimer) timers.cancel(flashTimer);
      flashTimer = timers.after(DV_CASINO.FLASH_MS, () => {
        if (mq && mq.classList) mq.classList.remove('g-dv-mq-flash');
      });
    },

    /**
     * The almost: the partner's face ghosts through the wrongly picked card.
     * Face content only (img url or glyph) - a video partner ghosts as its
     * glyph, because spinning up a second decoder for 600ms is how the wall
     * game learned about the 30Hz lock the hard way.
     */
    almost(wrongCell, partnerCell) {
      if (!armed || destroyed || reduced || !wrongCell || !partnerCell) return;
      const host = wrongCell.card;
      if (!host || !host.appendChild) return;
      const face = partnerCell.face;
      const node = el('div', 'g-dv-almost');
      if (!node) return;
      const url = face && face._url;
      if (url && face.tagName === 'IMG') {
        const img = el('img');
        if (img) { img.alt = ''; img.src = url; node.appendChild(img); }
      } else {
        node.textContent = (face && face._glyph) || '◈';
        node.classList.add('glyph');
      }
      host.appendChild(node);
      timers.after(DV_CASINO.ALMOST_MS, () => { try { node.remove(); } catch (e) { /* ignore */ } });
    },

    /** The bell took the board: the frame sighs out instead of cutting. */
    dimOut() {
      // ...and it is heard, not just seen: a slide pitched DOWN is the sigh.
      // Fires before the visual work so it lands on the same frame, and it is
      // outside the capsOk gate on purpose - a dark marquee still exhales.
      if (sounds()) cue('slide', 0.3, { pitch: 0.8 });
      bellOn = false;
      if (mq && mq.classList) {
        mq.classList.remove('g-dv-mq-bell', 'g-dv-mq-flash');
        mq.classList.add('g-dv-mq-out');
      }
    },

    /** The class is over; nothing may pulse again. (A board ending is NOT this
     *  call - the marquee is the room's and outlives every board.) */
    stop() {
      if (flashTimer) { timers.cancel(flashTimer); flashTimer = 0; }
      if (mq && mq.classList) mq.classList.remove('g-dv-mq-flash');
    },

    destroy() {
      destroyed = true;
      if (flashTimer && timers) { timers.cancel(flashTimer); flashTimer = 0; }
      if (kbOn && opts.grid && opts.grid.classList) {
        try { opts.grid.classList.remove('g-dv-kb'); } catch (e) { /* ignore */ }
      }
      if (mq) { try { mq.remove(); } catch (e) { /* ignore */ } }
      mq = null; bars = [];
    },

    /** Diagnostics for the harness; not part of the module contract. */
    diagnostics() {
      return { armed, marquee: !!mq, bell: bellOn, kenBurns: kbOn, heat: lastHeat, identity };
    },
  };
}

export default createDvCasino;
