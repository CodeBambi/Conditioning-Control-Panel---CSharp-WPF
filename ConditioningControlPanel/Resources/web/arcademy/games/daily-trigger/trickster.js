/* ============================================================================
 * games/daily-trigger/trickster.js - DECK III of the House Rules: the three
 * cards the floor map deals homeroom. "A letters game is where reading betrays
 * you. The ladder already climbs rungs; the new cards become upper rungs."
 *
 *   CROOKED CLOCK   (from rung 3) the clock chip's FACE bends: it races ahead
 *                   through the boring middle and crawls as it nears the
 *                   budget, then goes honest at the line. The real elapsed
 *                   time - the composite input, the warn state, the log - is
 *                   exact and untouched. Only the face lies.
 *   STAT FLICKER    (from rung 4) the rung or row chip briefly shows a number
 *                   slightly off, then "corrects" itself with a static pop.
 *                   The ledger never moves; confidence does.
 *   CHALK WHISPER   (rung 5 - the top of the storm, tier 3+ territory) a ghost
 *                   hand writes a LIE about the marks under the message line
 *                   ("forget the pink ones"). Unreliable Label, homeroom
 *                   voice: the text lies, the glyphs on the board and keycaps
 *                   are always the truth - the card TRAINS Law IV. It waits
 *                   politely while a real proctor message is up (two messages
 *                   on one line means the player reads neither).
 *
 * DEALING RULE: deal TIMES and card choices are seeded off the class seed
 * (budgeted per tier, 2 -> 8); whether a card's rung is armed when its moment
 * comes depends on the player's own misses - the same shape as Lost & Found's
 * ghost lure firing off the player's stall. A deal whose rung is not armed yet
 * re-queues politely, then folds. A retake replays the identical deal.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - the clock bend is display-only (bendClock is a pure
 *       function index.js calls to PAINT, never to score); stat flicker writes
 *       chip text and index repaints truth on a deadline; the whisper is a
 *       separate decoration line. Rows, marks, share grid: untouched.
 *   II  input honest  - nothing here is clickable, nothing overlays the desk.
 *   V   seeded        - per-tag mulberry32 streams off seed+'|dt-trickster'
 *       (core makeTaggedRoll's trailing-counter hash clusters; see the
 *       lost-and-found header for the measurement).
 *   VI  exits sacred  - bgIntensity 0 disarms the whole deck; reduced motion
 *       drops the flicker pop and the whisper materialise (the lie appears as
 *       a plain line, the clock still bends - a number is not motion); all
 *       timers live in the game's registry.
 *   VII strings      - whisper lines are lexicon rows (dt_whisper_1..4) with
 *       neutral fallbacks; this file never invents text.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const DT_TRICKSTER = Object.freeze({
  /** Dealt card events per class, by tier (the House budget: 2 -> 8). */
  DEALS: Object.freeze({ 1: 2, 2: 4, 3: 6, 4: 8 }),
  /** Never more than 4 a minute; never before the first row has breathed. */
  MIN_GAP_MS: 15000,
  FIRST_DEAL_MS: 14000,
  /** A deal whose moment is wrong (ceremony, unarmed rung) retries, then folds. */
  RETRY_MS: 2600,
  RETRY_MAX: 10,

  /** Rung gates - the cards ARE the upper rungs. */
  CLOCK_RUNG: 3,
  FLICKER_RUNG: 4,
  WHISPER_RUNG: 5,

  /** Crooked clock: how far ahead the face runs at mid-budget (fraction). */
  CLOCK_BEND: 0.14,

  /** Stat flicker: how long the wrong number stands before the static pop. */
  FLICKER_MS: 450,

  /** Chalk whisper: how long the ghost line stays on the wall. */
  WHISPER_MS: 3400,
  WHISPER_LINES: 4,
});

function clampTier(t) { return Math.max(1, Math.min(4, Math.round(Number(t) || 1))); }

/**
 * The crooked face, pure: display seconds for a real elapsed count.
 * f(0)=0, f(budget)=budget, monotonic (bend*pi < 1), honest at/after budget.
 * Ahead through the middle, crawling as the budget nears - the playbook's
 * "fast through the boring middle, crawling near zero", pointed up-hill
 * because homeroom counts UP.
 */
export function bendClock(elapsedSec, budgetSec, bend) {
  const x = Math.max(0, Number(elapsedSec) || 0);
  const B = Math.max(1, Number(budgetSec) || 90);
  if (x >= B) return Math.round(x);                  // honest at the line
  const A = Number.isFinite(bend) ? bend : DT_TRICKSTER.CLOCK_BEND;
  const u = x / B;
  return Math.round(B * (u + A * Math.sin(Math.PI * u)));
}

/**
 * @param {Object} o
 * @param {string}   o.seed        the class seed (date + tier - retakes replay)
 * @param {number}   o.tier        1..4
 * @param {number}   o.budgetSec   the class budget (clock bend input)
 * @param {Object}   o.timers      {after(ms,fn)->id, cancel(id)}
 * @param {boolean}  o.reduced     reduced motion
 * @param {boolean}  o.capsOk      false when bgIntensity is capped to 0
 * @param {Function} o.getRung     () => the ladder's current rung
 * @param {Function} o.isHalted    () => bool (pause/suspend/ceremony/reveal)
 * @param {Function} o.stats       () => {rung, cap, row, rows} - the TRUTH
 * @param {Function} o.paintTruth  () => void - repaint the chips honestly
 * @param {Function} o.chipEl      (which: 'rung'|'row') => element|null
 * @param {Function} o.canWhisper  () => bool (no proctor message up)
 * @param {Function} o.whisperHost () => element|null (the ghost line's wall)
 * @param {Function} o.t           ctx.lexicon
 * @param {Function=} o.log
 */
export function createDtTrickster(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const t = typeof opts.t === 'function' ? opts.t : (k, f) => f || k;
  const timers = opts.timers;
  const tier = clampTier(opts.tier);
  const reduced = !!opts.reduced;
  const armed = !!opts.capsOk && !!timers && typeof document !== 'undefined';

  const seedBase = String(opts.seed || 'dt') + '|dt-trickster|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };
  const getRung = typeof opts.getRung === 'function' ? opts.getRung : () => 0;
  const isHalted = typeof opts.isHalted === 'function' ? opts.isHalted : () => false;

  let destroyed = false;
  let stopped = false;
  let clockArmed = false;       // latches on: a clock that un-crooks is a tell
  let flickers = 0;
  let whispers = 0;
  let whisperEl = null;

  /* --------------------------------------------------------------- the deal */
  function buildDeals() {
    const n = DT_TRICKSTER.DEALS[tier] || 2;
    const spanMs = Math.max(60, Math.min(300, Number(opts.budgetSec) || 90)) * 1000;
    // homeroom regularly runs past its budget (it is untimed by design), so
    // the deal window stretches to 1.6x - late rows deserve cards too
    const usable = Math.max(30000, spanMs * 1.6 - DT_TRICKSTER.FIRST_DEAL_MS);
    const times = [];
    for (let i = 0; i < n; i++) times.push(DT_TRICKSTER.FIRST_DEAL_MS + roll('when') * usable);
    times.sort((a, b) => a - b);
    for (let i = 1; i < times.length; i++) {
      if (times[i] - times[i - 1] < DT_TRICKSTER.MIN_GAP_MS) {
        times[i] = times[i - 1] + DT_TRICKSTER.MIN_GAP_MS;
      }
    }
    // The card in each slot is the seed's choice, not the moment's.
    return times.map((at) => ({
      at: Math.round(at),
      card: roll('card') < 0.6 ? 'flicker' : 'whisper',
    }));
  }

  function attempt(deal, tries) {
    if (destroyed || stopped) return;
    const rung = getRung();
    const need = deal.card === 'whisper' ? DT_TRICKSTER.WHISPER_RUNG : DT_TRICKSTER.FLICKER_RUNG;
    if (isHalted() || rung < need) {
      // A whisper the storm never reaches downgrades to a flicker try once,
      // so a tier-2 class (cap 4) is not dealt a fistful of duds.
      if (tries >= DT_TRICKSTER.RETRY_MAX) {
        if (deal.card === 'whisper' && rung >= DT_TRICKSTER.FLICKER_RUNG) dealFlicker();
        return;
      }
      timers.after(DT_TRICKSTER.RETRY_MS, () => attempt(deal, tries + 1));
      return;
    }
    if (deal.card === 'whisper') dealWhisper();
    else dealFlicker();
  }

  /* ------------------------------------------------------------ stat flicker */
  function dealFlicker() {
    if (destroyed || stopped) return;
    const stats = typeof opts.stats === 'function' ? opts.stats() : null;
    const chipFor = typeof opts.chipEl === 'function' ? opts.chipEl : () => null;
    if (!stats) return;
    const which = roll('flick-which') < 0.5 ? 'rung' : 'row';
    const chip = chipFor(which);
    if (!chip || chip.textContent == null) return;
    // Slightly off, never absurd: one step up (the storm claims more of you).
    const lie = which === 'rung'
      ? 'rung ' + Math.min(stats.cap, stats.rung + 1) + '/' + stats.cap
      : Math.min(stats.rows, stats.row + 1) + ' / ' + stats.rows;
    if (chip.textContent === lie) return;            // the truth already says it
    chip.textContent = lie;
    if (!reduced && chip.classList) {
      chip.classList.add('g-dt-statlie');
    }
    flickers += 1;
    timers.after(DT_TRICKSTER.FLICKER_MS, () => {
      if (destroyed) return;
      try { if (chip.classList) chip.classList.remove('g-dt-statlie'); } catch (e) { /* ignore */ }
      try { if (typeof opts.paintTruth === 'function') opts.paintTruth(); } catch (e) { /* ignore */ }
    });
    say('trickster: stat flicker (' + which + ')');
  }

  /* ----------------------------------------------------------- chalk whisper */
  function dealWhisper() {
    if (destroyed || stopped || whisperEl) return;
    if (typeof opts.canWhisper === 'function' && !opts.canWhisper()) {
      timers.after(DT_TRICKSTER.RETRY_MS, () => attempt({ card: 'whisper' }, DT_TRICKSTER.RETRY_MAX - 1));
      return;
    }
    const host = typeof opts.whisperHost === 'function' ? opts.whisperHost() : null;
    if (!host || !host.appendChild) return;
    const which = 1 + Math.floor(roll('whisper-line') * DT_TRICKSTER.WHISPER_LINES);
    const line = t('dt_whisper_' + which, WHISPER_FALLBACKS[which - 1]);
    let node = null;
    try {
      node = document.createElement('p');
      node.className = 'g-dt-whisper' + (reduced ? ' plain' : '');
      node.textContent = String(line);
      node.setAttribute('aria-hidden', 'true');      // a lie is not for screen readers
    } catch (e) { return; }
    host.appendChild(node);
    whisperEl = node;
    whispers += 1;
    timers.after(DT_TRICKSTER.WHISPER_MS, () => {
      try { node.remove(); } catch (e) { /* ignore */ }
      if (whisperEl === node) whisperEl = null;
    });
    say('trickster: chalk whisper #' + which);
  }

  /* ------------------------------------------------------------------- api */
  return {
    /** Deal the class. Call once, when the board opens. */
    start() {
      if (!armed || destroyed) { say('trickster: disarmed'); return; }
      const deals = buildDeals();
      for (const deal of deals) timers.after(deal.at, () => attempt(deal, 0));
      say('trickster: dealt ' + deals.length + ' cards ('
        + deals.map((d) => d.card + '@' + Math.round(d.at / 1000) + 's').join(', ') + ')');
    },

    /**
     * The clock face, crooked or honest. index.js paints THIS; the real
     * elapsed count never routes through here. The bend arms with rung 3 and
     * latches (a clock that snaps back mid-class would out the trick).
     */
    clockFace(elapsedSec, budgetSec) {
      if (!armed || stopped) return Math.round(Math.max(0, Number(elapsedSec) || 0));
      if (!clockArmed && getRung() >= DT_TRICKSTER.CLOCK_RUNG) {
        clockArmed = true;
        say('trickster: the clock goes crooked (rung ' + getRung() + ')');
      }
      if (!clockArmed) return Math.round(Math.max(0, Number(elapsedSec) || 0));
      return bendClock(elapsedSec, budgetSec, DT_TRICKSTER.CLOCK_BEND);
    },

    /** The class is over: faces go honest, no card may fire again. */
    stop() {
      stopped = true;
      if (whisperEl) { try { whisperEl.remove(); } catch (e) { /* ignore */ } whisperEl = null; }
    },

    destroy() {
      destroyed = true;
      stopped = true;
      if (whisperEl) { try { whisperEl.remove(); } catch (e) { /* ignore */ } whisperEl = null; }
    },

    /** Diagnostics for the harness; not part of the module contract. */
    diagnostics() {
      return { armed, tier, clockArmed, flickers, whispers };
    },
  };
}

/** Neutral fallbacks for the whisper rows (each mirrored in NeutralLexicon). */
const WHISPER_FALLBACKS = Object.freeze([
  'Forget the pink ones.',
  'It was never in row two.',
  'You already typed it.',
  'The stars are lying, not me.',
]);

export default createDtTrickster;
