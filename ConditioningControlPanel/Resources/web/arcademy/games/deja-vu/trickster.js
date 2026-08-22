/* ============================================================================
 * games/deja-vu/trickster.js - DECK III of the House Rules: the memory game
 * earns the memory lies. Three cards off the floor map:
 *
 *   FAKE SHUFFLE    right after the preview flips down, cards pantomime a
 *                   shuffle - lift, slide more than halfway toward each other,
 *                   hesitate, slide home. NOTHING MOVES. The lie is that it
 *                   looked like it did; the truth-signal is that no swap tell
 *                   fired. Sharp players learn the tell system IS the truth -
 *                   which is exactly what the playbook wants the card to
 *                   teach. (Law 1 of index.js is untouched: no mutation, so
 *                   no settled-board window is even consumed.)
 *   DEJA RE-DEAL    the native signature. Once a class, at a seeded settled
 *                   window, the machine re-shows the whole board - a second
 *                   preview, free - except from tier 3 exactly ONE unmatched
 *                   card wears a LIE face borrowed from another pair. Flip the
 *                   liar next and the class pays a called-it flavour bonus.
 *                   Tier 2 gets the truthful version (a gift with a shiver).
 *                   The real pairIds never move; only the shown frame lies.
 *   STAT FLICKER    the swap or clock chip briefly reads slightly off, then
 *                   corrects itself with a static pop. The ledger never
 *                   moves; confidence does.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - the shuffle moves nothing; the re-deal shows a false
 *       FRAME over an unchanged board and pays only flavorXp (game-owned,
 *       never composite); the flicker writes chip text and index repaints
 *       truth on a deadline.
 *   II  input honest  - shuffle/lie/flicker are all decoration; hitboxes and
 *       pairIds never move; the lie overlay is pointer-events:none.
 *   V   seeded        - per-tag mulberry32 streams off seed+'|dv-trickster'.
 *       The re-deal window, the lie's pick walk and every flicker moment are
 *       the seed's choice; a retake replays the identical show.
 *   VI  exits sacred  - reduced motion: shuffle off (it IS motion), re-deal
 *       becomes the truthful still version, flicker plain; bgIntensity 0
 *       disarms the deck; all timers live in the game's registry, so the
 *       run()/deferred discipline freezes everything with the class.
 *   VII strings      - dv_redeal_hint / dv_redeal_gift / dv_called_it via
 *       ctx.lexicon from index.js; this file renders no text.
 *
 * DIVISION OF LABOUR: this module owns the SCHEDULE and the pantomime
 * (transform theatre on holders); index.js owns everything that touches card
 * state (the re-deal's flips ride applyMedia/playFace/busy, which are its
 * language). pickLie() walks candidates with a seeded start the same way
 * lost-and-found's fallbackSwapPair does - deterministic given board state.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const DV_TRICKSTER = Object.freeze({
  /** Fake shuffle: from tier 2. Pairs of cards feinting, by tier. */
  SHUFFLE_FROM_TIER: 2,
  SHUFFLE_PAIRS: Object.freeze({ 1: 0, 2: 2, 3: 3, 4: 3 }),
  SHUFFLE_MS: 950,
  /** How far toward each other the feint slides (fraction of the gap). */
  SHUFFLE_REACH: 0.58,

  /** Re-deal: from tier 2; the lie face from tier 3. Board must be mid-game. */
  REDEAL_FROM_TIER: 2,
  LIE_FROM_TIER: 3,
  /** Seeded settled-window slot for the re-deal (min + spread). */
  REDEAL_WIN_MIN: 3,
  REDEAL_WIN_SPREAD: 4,
  /** Re-show length by tier (ms) - higher years get a shorter gift. */
  REDEAL_SHOW_MS: Object.freeze({ 2: 1500, 3: 1200, 4: 900 }),
  /** Minimum unmatched cells for a re-deal to be worth dealing. */
  REDEAL_MIN_CELLS: 6,
  /** Called-it flavour bonus (flavorXp, game-owned; never composite). */
  CALLED_IT_XP: 2,

  /** Stat flicker: from tier 3; deals per class by tier. */
  FLICKER_FROM_TIER: 3,
  FLICKER_DEALS: Object.freeze({ 3: 2, 4: 3 }),
  FLICKER_FIRST_MS: 18000,
  FLICKER_GAP_MS: 16000,
  FLICKER_MS: 450,
  FLICKER_RETRY_MS: 2400,
  FLICKER_RETRY_MAX: 8,
});

function clampTier(t) { return Math.max(1, Math.min(4, Math.round(Number(t) || 1))); }

/**
 * @param {Object} o
 * @param {string}   o.seed        the class seed (retakes replay)
 * @param {number}   o.tier        1..4
 * @param {Object}   o.timers      {after(ms,fn)->id, cancel(id)} (run()-gated)
 * @param {boolean}  o.reduced     reduced motion
 * @param {boolean}  o.capsOk      false when bgIntensity is capped to 0
 * @param {Function} o.isHalted    () => bool (dead/paused/ended)
 * @param {Function} o.stats       () => {swaps, budget, secLeft} - the TRUTH
 * @param {Function} o.chipEl      (which: 'swaps'|'clock') => element|null
 * @param {Function} o.chipText    (which) => the honest text (repaint source)
 * @param {Function=} o.log
 */
export function createDvTrickster(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const timers = opts.timers;
  const tier = clampTier(opts.tier);
  const reduced = !!opts.reduced;
  const armed = !!opts.capsOk && !!timers && typeof document !== 'undefined';
  const isHalted = typeof opts.isHalted === 'function' ? opts.isHalted : () => false;

  const seedBase = String(opts.seed || 'dv') + '|dv-trickster|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  let destroyed = false;
  let stopped = false;
  let shuffles = 0;
  let flickers = 0;

  /* The re-deal's one slot, drawn up-front so diagnostics can read it. */
  const redeal = {
    eligible: armed && tier >= DV_TRICKSTER.REDEAL_FROM_TIER,
    window: DV_TRICKSTER.REDEAL_WIN_MIN
      + Math.floor(roll('redeal-win') * DV_TRICKSTER.REDEAL_WIN_SPREAD),
    lies: tier >= DV_TRICKSTER.LIE_FROM_TIER && !reduced,
    showMs: DV_TRICKSTER.REDEAL_SHOW_MS[tier] || 1500,
    done: false,
    liedCell: -1,
  };

  /* ---------------------------------------------------------- fake shuffle */
  /**
   * The pantomime. Picks N seeded pairs of face-down cells and slides each
   * pair's HOLDERS toward each other and back - transforms only, hitboxes
   * and pairIds untouched, and deliberately NO tell (that IS the card).
   * Calls onDone when the theatre has left the stage.
   */
  function shuffle(cells, onDone) {
    const done = typeof onDone === 'function' ? onDone : () => {};
    if (!armed || destroyed || stopped || reduced || tier < DV_TRICKSTER.SHUFFLE_FROM_TIER) {
      done();
      return false;
    }
    const down = (cells || []).filter((c) => c && c.state === 'down' && c.pairId >= 0 && c.holder);
    const wantPairs = DV_TRICKSTER.SHUFFLE_PAIRS[tier] || 0;
    if (down.length < 4 || !wantPairs) { done(); return false; }

    // seeded pick walk: distinct cells, pairs of two
    const picked = [];
    const start = Math.floor(roll('shuffle-start') * down.length);
    for (let n = 0; n < down.length && picked.length < wantPairs * 2; n++) {
      picked.push(down[(start + n * 2 + (n % 3)) % down.length]);
    }
    const seen = new Set();
    const cast = picked.filter((c) => (seen.has(c.index) ? false : (seen.add(c.index), true)));
    if (cast.length < 2) { done(); return false; }

    let staged = 0;
    for (let i = 0; i + 1 < cast.length; i += 2) {
      const a = cast[i];
      const b = cast[i + 1];
      let ra = null;
      let rb = null;
      try { ra = a.holder.getBoundingClientRect(); rb = b.holder.getBoundingClientRect(); }
      catch (e) { continue; }
      if (!ra || !rb || !ra.width) continue;
      const dx = (rb.left - ra.left) * DV_TRICKSTER.SHUFFLE_REACH;
      const dy = (rb.top - ra.top) * DV_TRICKSTER.SHUFFLE_REACH;
      feint(a.holder, dx, dy);
      feint(b.holder, -dx, -dy);
      staged += 1;
    }
    if (!staged) { done(); return false; }
    shuffles += 1;
    say('trickster: fake shuffle (' + staged + ' feints, nothing moved)');
    timers.after(DV_TRICKSTER.SHUFFLE_MS + 80, done);
    return true;
  }

  function feint(holder, dx, dy) {
    if (!holder || !holder.style) return;
    holder.style.setProperty('--g-dv-fx', dx.toFixed(1) + 'px');
    holder.style.setProperty('--g-dv-fy', dy.toFixed(1) + 'px');
    if (holder.classList) {
      holder.classList.remove('g-dv-feint');
      if (typeof holder.offsetWidth === 'number') void holder.offsetWidth;
      holder.classList.add('g-dv-feint');
    }
    timers.after(DV_TRICKSTER.SHUFFLE_MS, () => {
      try { holder.classList.remove('g-dv-feint'); } catch (e) { /* ignore */ }
    });
  }

  /* -------------------------------------------------------------- re-deal */
  /** Is the re-deal due at this settled window? (index asks from mutate().) */
  function redealDue(settledWindow, unmatchedCount) {
    return redeal.eligible && !redeal.done && !stopped && !destroyed
      && settledWindow >= redeal.window
      && unmatchedCount >= DV_TRICKSTER.REDEAL_MIN_CELLS;
  }

  /**
   * The lie's seat: a seeded walk over the unmatched, face-down cells
   * (deterministic given board state - the fallbackSwapPair discipline).
   * Returns {cell, wearPairId} or null for the truthful version.
   */
  function pickLie(cells) {
    if (!redeal.lies) return null;
    const open = (cells || []).filter((c) => c && c.state === 'down' && c.pairId >= 0);
    if (open.length < 4) return null;
    const cell = open[Math.floor(roll('lie-pick') * open.length)];
    // wear another pair's face: the seeded walk finds a DIFFERENT pairId
    const others = open.filter((c) => c.pairId !== cell.pairId);
    if (!others.length) return null;
    const donor = others[Math.floor(roll('lie-wear') * others.length)];
    redeal.liedCell = cell.index;
    return { cell, wearPairId: donor.pairId };
  }

  function redealFired() { redeal.done = true; }

  /* --------------------------------------------------------- stat flicker */
  function armFlickers() {
    if (!armed || tier < DV_TRICKSTER.FLICKER_FROM_TIER) return;
    const n = DV_TRICKSTER.FLICKER_DEALS[tier] || 0;
    for (let i = 0; i < n; i++) {
      const at = DV_TRICKSTER.FLICKER_FIRST_MS
        + i * DV_TRICKSTER.FLICKER_GAP_MS
        + Math.round(roll('flick-when') * 9000);
      timers.after(at, () => tryFlicker(0));
    }
    if (n) say('trickster: ' + n + ' stat flickers armed');
  }

  function tryFlicker(tries) {
    if (destroyed || stopped) return;
    if (isHalted()) {
      if (tries < DV_TRICKSTER.FLICKER_RETRY_MAX) {
        timers.after(DV_TRICKSTER.FLICKER_RETRY_MS, () => tryFlicker(tries + 1));
      }
      return;
    }
    const stats = typeof opts.stats === 'function' ? opts.stats() : null;
    const chipFor = typeof opts.chipEl === 'function' ? opts.chipEl : () => null;
    const honest = typeof opts.chipText === 'function' ? opts.chipText : () => null;
    if (!stats) return;
    // the swaps chip only exists when the class has a swap budget
    const which = (chipFor('swaps') && roll('flick-which') < 0.5) ? 'swaps' : 'clock';
    const chip = chipFor(which);
    if (!chip || chip.textContent == null) return;
    let lie;
    if (which === 'swaps') {
      // one more swap than really fired: "did the board move without me?"
      lie = String(honest('swaps') || '').replace(
        stats.swaps + '/', Math.min(stats.budget, stats.swaps + 1) + '/');
    } else {
      // 7..15 seconds poorer, momentarily
      const drop = 7 + Math.floor(roll('flick-drop') * 9);
      const left = Math.max(0, stats.secLeft - drop);
      const m = Math.floor(left / 60);
      const s = left % 60;
      lie = m > 0 ? m + ':' + String(s).padStart(2, '0') : left + 's';
    }
    if (!lie || chip.textContent === lie) return;
    chip.textContent = lie;
    if (!reduced && chip.classList) chip.classList.add('g-dv-statlie');
    flickers += 1;
    timers.after(DV_TRICKSTER.FLICKER_MS, () => {
      if (destroyed) return;
      try { if (chip.classList) chip.classList.remove('g-dv-statlie'); } catch (e) { /* ignore */ }
      const truth = honest(which);
      if (truth != null) { try { chip.textContent = truth; } catch (e) { /* ignore */ } }
    });
    say('trickster: stat flicker (' + which + ')');
  }

  /* ------------------------------------------------------------------ api */
  return {
    /** Arm the timed cards. Call once when play arms (post-preview). */
    start() {
      if (!armed || destroyed) { say('trickster: disarmed'); return; }
      armFlickers();
      say('trickster: re-deal ' + (redeal.eligible
        ? 'window ' + redeal.window + (redeal.lies ? ' (with the lie)' : ' (truthful)')
        : 'not dealt at this tier'));
    },

    shuffle,
    redealDue,
    pickLie,
    redealFired,
    get redealShowMs() { return redeal.showMs; },
    get redealLies() { return redeal.lies; },
    get liedCell() { return redeal.liedCell; },

    /** The class is over: no card may fire again. */
    stop() { stopped = true; },
    destroy() { destroyed = true; stopped = true; },

    /** Diagnostics for the harness; not part of the module contract. */
    diagnostics() {
      return {
        armed, tier, shuffles, flickers,
        redeal: { window: redeal.window, done: redeal.done, lies: redeal.lies, liedCell: redeal.liedCell },
      };
    },
  };
}

export default createDvTrickster;
