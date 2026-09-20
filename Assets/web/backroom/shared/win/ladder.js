/* ladder.js - THE CHIME LADDER, once, for all four stations (lane BR2-spine, CONTRACT 10.22).
 *
 * House Book move: one sound family, +1 semitone a step (or per two), cap 7 layers, drops an octave in a
 * focus state. Lifted verbatim out of stations/slot/feel.js (`ladderSemis`, `ladderPlan`, `LADDER_CAP`,
 * `STROBE_MIN_MS`) so the wheel, the roulette and the cards climb the same notes the slot climbs instead of
 * each growing a copy. ADDITIVE: the slot's own exports still stand and still work; the slot lane retrofits
 * its call sites, this file does not reach into it.
 *
 * PURE: no DOM, no audio, no timers. It returns WHEN and HOW HIGH; sound.js plays it.
 *
 *   Law IX    the rungs are the tiers: a small win gets the landing note and nothing after it.
 *   Law X     one gesture, one beat. Step 0 IS the landing cue the station already plays; the steps after
 *             it are the rollup's climb, not a second party.
 *   Brake 5   melted (a trance, a melt) drops the whole ladder an octave and flattens it to one note.
 *   Brake 7   nothing ever steps faster than the 6 Hz strobe floor, however short the rollup is.
 *   Brake 9   the notes are never the only channel: the readout is counting underneath them.
 */

export const LADDER = Object.freeze({
  CAP: 7,                     // THE CHIME LADDER: 7 layers at most, whatever the streak says
  OCTAVE: -12,                // Brake 5: a focus state drops it an octave, it never goes silent
  STROBE_MIN_MS: 1000 / 6,    // Brake 7: no step closer than 6 Hz
  /** How many notes a tier is worth, including the landing cue. Tier 1 is one note - Law IX, a small win
   *  never gets the fanfare. 4 spends the full cap over the jackpot's 6 s climb. */
  STEPS: Object.freeze([0, 1, 3, 5, 7]),
});

/** How many notes a rung is worth. Melted flattens it to the landing cue alone (Brake 5). */
export function ladderSteps(tier, melted = false) {
  const t = Math.max(0, Math.min(4, Math.floor(Number(tier) || 0)));
  if (t === 0) return 0;
  if (melted) return 1;
  return Math.min(LADDER.CAP, LADDER.STEPS[t]);
}

/** THE CHIME LADDER's pitch: `step` wins in a row before this one, capped, an octave down while melted.
 *  Exactly stations/slot/feel.js ladderSemis - the slot's tests pin these numbers. */
export function ladderSemis(step, melted) {
  const s = Math.max(0, Math.min(LADDER.CAP, Math.floor(Number(step) || 0)));
  return melted ? s + LADDER.OCTAVE : s;
}

/**
 * THE CHIME LADDER across THE BANK's rollup: the ladder climbs while the readout counts, so a big win rises
 * instead of ringing once. Step 0 is the landing cue the station has already played; the steps after it
 * follow a semitone apart, spread over `ms`, never closer than the strobe floor and never more than the cap.
 * Tier 1 and a melted win get the landing note and nothing more (Law IX, Brake 5).
 *
 * Returns `[{ at, semis }]` in ms from the landing frame, both rising. `semis` is the RUNG, not the pitch:
 * run it through `ladderSemis` for the octave drop, exactly as the slot does.
 */
export function ladderPlan(tier, ms, melted = false) {
  const span = Math.max(0, Number(ms) || 0);
  const want = Math.max(1, ladderSteps(tier, melted));
  const gap = Math.max(LADDER.STROBE_MIN_MS, span / want);
  const n = Math.max(1, Math.min(want, Math.floor(span / gap) || 1));
  return Array.from({ length: n }, (_, i) => ({ at: Math.round(i * gap), semis: i }));
}
