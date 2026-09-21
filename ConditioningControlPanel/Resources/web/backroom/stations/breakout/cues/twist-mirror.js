/* ============================================================================
 * stations/breakout/cues/twist-mirror.js - the twist's sounds.
 *
 * MIRROR. One sound, and it says what happened: the twin's pop is the brick's
 * OWN note an octave up, panned to the twin's side of the board. The same note,
 * higher and over there. Nothing else is added, because the twin's break plays
 * the ordinary brick hit a breath later and two cues on one beat is a mess.
 *
 * House rules (AGENTS.md): pitched cues come from pentatonic() + ROOT_HZ and land
 * on quantise(); a physical impact plays at synth.now and is never quantised.
 * The note is asked for a sixteenth at least TWIN_DELAY away, so it lands with
 * the twin and still sits on the bed's grid.
 * ==========================================================================*/

import { TWIN_DELAY } from '../twists/mirror.js';

const clamp = v => (v < 0 ? 0 : v > 1 ? 1 : v);

/**
 * The twin's side, in 0..1. The event carries both centres and station.js normalises
 * only the first, so the second rides the same scale. A reflection is the other side,
 * which is the fallback when the numbers are not there.
 */
export function twinPan(d) {
  const x = Number(d && d.x), tx = Number(d && d.tx), xN = Number(d && d.xN);
  if (Number.isFinite(x) && x > 0 && Number.isFinite(tx) && Number.isFinite(xN)) return clamp((tx * xN) / x);
  return Number.isFinite(xN) ? clamp(1 - xN) : 0.5;
}

export const CUES = {
  /** A pair went. The brick's note, an octave up, over on the twin's side. */
  mirrorPop(synth, d) {
    if (d && d.state === 'grey') return false;            // grey is payload-free: the wall keeps its own dull knock
    const { tone, SEMI, ROOT_HZ, hitSemis, hitCutoff, saturation } = synth;
    const pan = twinPan(d);
    const hz = ROOT_HZ * SEMI(hitSemis(d && d.combo)) * 2;
    const cut = Math.min(hitCutoff(saturation), 6200);
    synth.play([
      tone(hz, 0.16, 0.055, { lp: cut, pan, wet: true }),                          // the note, over there
      tone(hz * 2, 0.05, 0.013, { lp: cut, pan, attack: 0.004 }),                  // a sliver of glass on top
    ], synth.quantise(TWIN_DELAY), synth.bus.sfx);
    return true;
  },
};

export default CUES;
