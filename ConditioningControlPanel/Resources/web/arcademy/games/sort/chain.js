/* ============================================================================
 * games/sort/chain.js - THE CHAIN. PURE.
 *
 * Swiping media by a fixed rule with no failure state is a soak. THE CHAIN is
 * what makes it a game: every card wears a ripe ring that closes in N seconds,
 * and N is a function of YOUR clean streak. Rung 0 gives you 2.4s to read a
 * card; rung 8 gives you 0.75s. Nothing else in the room speeds up - the deck
 * does not shrink, the cards do not get harder, the player drives the tempo and
 * the player can always slow it back down by being wrong.
 *
 * THE THREE VERDICTS, all off one clock:
 *   PERFECT   swiped in the LAST 40% of the ring (the gold arc)
 *   JUST      a PERFECT inside the last 12% - a near-miss you WON, and the one
 *             beat the casino stages loudest
 *   ALMOST    swiped in the 10% before the window opened - the near-miss you
 *             lost, and the reason the ring is drawn at all
 *
 * A WRONG SWIPE IS ONE RUNG DOWN, NEVER OUT. The chain resets to the FLOOR of
 * the rung below, on a 1.5s fade, so a mistake at rung 6 costs the two seconds
 * it takes to climb back and never the whole class. Rung 0 is the floor; there
 * is no rung below it and there is no fail state under it.
 *
 * A PASS IS NOT AN ERROR. Letting a ring close sinks the card under the stack
 * to be dealt again later. It costs the tempo it costs and nothing else: no
 * rung, no chain, no accuracy. That is the pressure valve that lets a player
 * actually LOOK at a card they cannot place instead of guessing.
 *
 * Everything here is a function of numbers. No DOM, no clock, no engine: the
 * room reads these and paints, the suite reads these and asserts.
 * ==========================================================================*/

export const CHAIN = Object.freeze({
  /** Clean swipes that earn rung 1..8. Index i is the step onto rung i+1. */
  RUNG_STEPS: Object.freeze([3, 5, 8, 12, 16, 21, 27, 34]),
  /** Ring length in ms at rung 0..8. */
  RING_MS: Object.freeze([2400, 2100, 1800, 1600, 1400, 1200, 1050, 900, 750]),
  /** The highest rung a tier will hand out (and therefore its floor ring). */
  CAP_BY_TIER: Object.freeze({ 1: 5, 2: 6, 3: 7, 4: 8 }),
  /** The gold arc: the last 40% of the ring is PERFECT. */
  PERFECT_FRAC: 0.40,
  /** JUST is a PERFECT inside the last 12%. */
  JUST_FRAC: 0.12,
  /** ALMOST is the 10% of ring immediately BEFORE the gold arc opens. */
  ALMOST_FRAC: 0.10,
  /** A wrong swipe walks the rung down over this long. */
  WRONG_FADE_MS: 1500,
  /** Reaching one of these rungs pays a major jackpot, once each per class. */
  MAJOR_RUNGS: Object.freeze([3, 5, 7]),
  /** A PERFECT at or above this rung may roll a minor jackpot. */
  MINOR_MIN_RUNG: 2,
  /** The royal: first time at this rung with no wrong swipe since ROYAL_CLEAN_FROM. */
  ROYAL_RUNG: 8,
  ROYAL_CLEAN_FROM: 5,
  /** Chime climbs one semitone per link and stops climbing here. */
  CHIME_CAP: 7,
  /** Highest rung that exists at all. */
  MAX_RUNG: 8,
});

function clamp(v, lo, hi) { const n = Number(v); return !Number.isFinite(n) ? lo : n < lo ? lo : n > hi ? hi : n; }
function tierOf(tier) { return Math.max(1, Math.min(4, Math.round(Number(tier) || 1))); }

/** The highest rung this grade tier hands out. */
export function capForTier(tier) { return CHAIN.CAP_BY_TIER[tierOf(tier)]; }

/** The rung a clean streak of `streak` has earned, never above the tier cap. */
export function rungForStreak(streak, cap) {
  const s = Math.max(0, Math.round(Number(streak) || 0));
  const ceiling = clamp(cap == null ? CHAIN.MAX_RUNG : cap, 0, CHAIN.MAX_RUNG);
  let rung = 0;
  for (let i = 0; i < CHAIN.RUNG_STEPS.length; i++) if (s >= CHAIN.RUNG_STEPS[i]) rung = i + 1;
  return Math.min(rung, ceiling);
}

/** The smallest clean streak that stands on `rung`. Rung 0 is a chain of 0. */
export function streakForRung(rung) {
  const r = clamp(rung, 0, CHAIN.MAX_RUNG);
  return r <= 0 ? 0 : CHAIN.RUNG_STEPS[r - 1];
}

/** The ring length at a rung, in ms. */
export function ringMsFor(rung) { return CHAIN.RING_MS[clamp(Math.round(rung), 0, CHAIN.MAX_RUNG)]; }

/** The floor ring of a tier: the ring you play at once you are capped. */
export function floorRingFor(tier) { return ringMsFor(capForTier(tier)); }

/** The ms mark where the gold arc opens on a ring of this length. */
export function ripeAt(ringMs) {
  const ms = Math.max(1, Number(ringMs) || 1);
  return ms * (1 - CHAIN.PERFECT_FRAC);
}

/**
 * Read a swipe against its ring.
 * @param {number} elapsedMs  ms since the card became grabbable
 * @param {number} ringMs     the ring this card was dealt with
 * @returns {{frac:number, perfect:boolean, just:boolean, almost:boolean,
 *            closed:boolean, verdict:'perfect'|'just'|'almost'|'early'|'closed'}}
 */
export function verdictFor(elapsedMs, ringMs) {
  const ms = Math.max(1, Number(ringMs) || 1);
  const at = Math.max(0, Number(elapsedMs) || 0);
  const frac = at / ms;
  const closed = frac >= 1;
  const perfect = !closed && frac >= (1 - CHAIN.PERFECT_FRAC);
  const just = !closed && frac >= (1 - CHAIN.JUST_FRAC);
  const almost = !perfect && !closed
    && frac >= (1 - CHAIN.PERFECT_FRAC - CHAIN.ALMOST_FRAC);
  const verdict = closed ? 'closed' : just ? 'just' : perfect ? 'perfect' : almost ? 'almost' : 'early';
  return { frac, perfect, just, almost, closed, verdict };
}

/**
 * A clean swipe: the chain grows by one and the rung follows it.
 * @returns {{chain:number, rung:number, rungUp:boolean, from:number}}
 */
export function afterClean(chain, rung, cap) {
  const from = clamp(rung, 0, CHAIN.MAX_RUNG);
  const next = Math.max(0, Math.round(Number(chain) || 0)) + 1;
  const to = rungForStreak(next, cap);
  return { chain: next, rung: to, rungUp: to > from, from };
}

/**
 * A wrong swipe: ONE rung down, floored at 0, and the chain drops to the floor
 * of the rung it lands on (so the climb back starts where the rung starts, not
 * from zero - "never to zero, never out").
 * @returns {{chain:number, rung:number, rungDown:boolean, from:number, fadeMs:number}}
 */
export function afterWrong(chain, rung) {
  const from = clamp(rung, 0, CHAIN.MAX_RUNG);
  const to = Math.max(0, from - 1);
  return {
    chain: streakForRung(to),
    rung: to,
    rungDown: to < from,
    from,
    fadeMs: CHAIN.WRONG_FADE_MS,
  };
}

/** A pass keeps everything. It is here so the room has one call per moment. */
export function afterPass(chain, rung) {
  return { chain: Math.max(0, Math.round(Number(chain) || 0)), rung: clamp(rung, 0, CHAIN.MAX_RUNG) };
}

/** The chime ratchet: +1 semitone per link, capped, as a playback rate. */
export function chimePitch(link) {
  const n = clamp(Math.round(link), 0, CHAIN.CHIME_CAP);
  return Number(Math.pow(2, n / 12).toFixed(4));
}

/** Is this rung one of the three that pays a major jackpot? */
export function isMajorRung(rung) { return CHAIN.MAJOR_RUNGS.indexOf(clamp(Math.round(rung), 0, CHAIN.MAX_RUNG)) >= 0; }

/**
 * The ROYAL verdict: the first time a class reaches rung 8 having made no wrong
 * swipe since it first stood on rung 5. Pure, so the suite can hold it still.
 */
export function isRoyal({ rung, wrongsSinceRoyalFloor, royalPaid } = {}) {
  if (royalPaid) return false;
  if (clamp(Math.round(rung), 0, CHAIN.MAX_RUNG) < CHAIN.ROYAL_RUNG) return false;
  return Math.max(0, Math.round(Number(wrongsSinceRoyalFloor) || 0)) === 0;
}

/** Progress toward the NEXT rung, 0..1, for the ladder HUD. */
export function ladderFrac(chain, rung, cap) {
  const ceiling = clamp(cap == null ? CHAIN.MAX_RUNG : cap, 0, CHAIN.MAX_RUNG);
  const r = clamp(rung, 0, ceiling);
  if (r >= ceiling) return 1;
  const from = streakForRung(r);
  const to = streakForRung(r + 1);
  const span = Math.max(1, to - from);
  const c = Math.max(0, Math.round(Number(chain) || 0));
  return clamp((c - from) / span, 0, 1);
}

export default { CHAIN, rungForStreak, ringMsFor, verdictFor, afterClean, afterWrong };
