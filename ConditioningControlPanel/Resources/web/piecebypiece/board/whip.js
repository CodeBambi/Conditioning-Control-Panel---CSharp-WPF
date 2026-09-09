/* ============================================================================
 * board/whip.js - the bishop's tentacle whip, as a timeline.
 *
 * A capture by the bishop does not land on the victim first: the bishop
 * arrives on the diagonal one stand-off short of him, drops with a thud,
 * draws the tentacle back, cracks it across the victim, and strides onto the
 * square while the tentacle rings out. The victim shivers at the crack, is
 * knocked over away from the bishop and rolls off to sink, the way every
 * taken man does.
 *
 * This file is the whole shape of that, and nothing else: numbers and pure
 * functions of time. anim.js drives the men off it in the game; the phone
 * preview (smoke/whip-preview.mjs) inlines this exact file and drives a copy
 * of the same geometry off it, so what the owner sees on the phone and what
 * the board plays are one curve, not two tunings.
 *
 * Time here is seconds since the bishop LANDED at the stand-off. Bend is in
 * the flex shader's units (jiggle.js: the tip's sideways offset, in the man's
 * own units, +1 toward the victim). Every stage is named so anim.js can emit
 * the right bus event at the right frame.
 *
 * THE 440 LINE (PBP-BEATS, beat 3): the opponent's clock is already running
 * from the move, so the taker must be ON his square by 440 ms and everything
 * after that is scenery. So the stride starts AT the crack, not after the
 * ring, and the ring plays over the stride and past it. Beats: approach 200
 * | wind 100 | snap 80, crack 36 in | stride 100 from the crack. Crack at
 * 336 ms from the move, the bishop stands on the square at 436 ms; the ring
 * dies by 800 ms, the victim is sunk by ~1420 and on the rim by ~1970.
 * ==========================================================================*/

/** Every number that decides how the whip plays. One place, on purpose. */
export const WHIP_TUNING = Object.freeze({
  standOff: 0.62,       // world units short of the victim the bishop lands at
  approachSec: 0.20,    // the slide to the stand-off (a plain move is 0.30 for a longer trip)
  approachHop: 0.18,    // its arc height
  windSec: 0.10,        // the tentacle draws back
  windAmp: 0.26,        // how far back (tip offset, away from the victim)
  snapSec: 0.08,        // the crack: back to forward, most of it in the last half
  snapAmp: 0.46,        // how far past upright the tip flies
  crackAt: 0.45,        // fraction of snapSec at which the tip crosses the victim
  ringSec: 0.42,        // the tentacle rings out after the snap (scenery, over the stride)
  ringHz: 4.2,          // about the flex spring's own rate, so it reads as the same body
  ringDecay: 8.5,       // per second
  lungeSquash: 2.4,     // a squat on the crack, handed to the flex spring
  stepSec: 0.10,        // the stride onto the square, from the crack
  stepHop: 0.10,
  // The victim.
  hitBend: 2.2,         // flex impulse into him at the crack (a flinch away from the bishop)
  hitSquash: 2.0,
  shiverSec: 0.25,      // SHIVER (house book): a forced tremor across the whip line
  shiverAmp: 0.05,      // local units, ~ +/-4 px on a pawn; no colour, no emissive
  shiverCycles: 3,
  tipSec: 0.10,         // knocked over: he hits the board as the bishop lands
  rollSec: 0.28,
  rollPush: 0.42,       // and is shoved further than a plain capture's 0.32
});

const easeIn = (t) => t * t;
const easeOut = (t) => 1 - Math.pow(1 - t, 3);

/** Stage boundaries, seconds since the stand-off landing. */
export function whipTimes(T = WHIP_TUNING) {
  const wind = T.windSec;
  const crack = wind + T.snapSec * T.crackAt;
  const snap = wind + T.snapSec;
  const step = crack;                // the stride starts at the crack
  const stand = step + T.stepSec;    // the bishop is on the square (land, capture:true)
  const ring = snap + T.ringSec;     // the tentacle is still
  const done = Math.max(stand, ring);
  return { wind, crack, snap, step, stand, ring, done };
}

/**
 * The tentacle's tip offset toward the victim at time t (seconds since the
 * stand-off landing), and the stage it is in: 'wind' | 'snap' | 'ring' |
 * 'done'. Negative is drawn back, positive is across the victim. The stride
 * is not a stage of the bend: it runs alongside from whipTimes().step.
 */
export function whipBend(t, T = WHIP_TUNING) {
  const k = whipTimes(T);
  if (t < 0) return { bend: 0, stage: 'wind' };
  if (t < k.wind) {
    return { bend: -T.windAmp * easeIn(t / T.windSec), stage: 'wind' };
  }
  if (t < k.snap) {
    // Most of the travel happens late: it holds back, then goes.
    const p = (t - k.wind) / T.snapSec;
    const q = p < T.crackAt ? 0.5 * Math.pow(p / T.crackAt, 3) : 0.5 + 0.5 * easeOut((p - T.crackAt) / (1 - T.crackAt));
    return { bend: -T.windAmp + (T.windAmp + T.snapAmp) * q, stage: 'snap' };
  }
  if (t < k.ring) {
    const u = t - k.snap;
    return { bend: T.snapAmp * Math.exp(-T.ringDecay * u) * Math.cos(2 * Math.PI * T.ringHz * u), stage: 'ring' };
  }
  return { bend: 0, stage: 'done' };
}

/** The victim's shiver at time u since the crack: a tremor across the whip line, 0 when over. */
export function shiverAt(u, T = WHIP_TUNING) {
  if (u < 0 || u >= T.shiverSec) return 0;
  const p = u / T.shiverSec;
  return T.shiverAmp * (1 - p) * Math.sin(2 * Math.PI * T.shiverCycles * p);
}

/** Seconds from the move being played to the bishop standing on the square. */
export function whipStandSec(T = WHIP_TUNING) {
  return T.approachSec + whipTimes(T).stand;
}

/** Seconds from the move being played to the tentacle being still. */
export function whipTotalSec(T = WHIP_TUNING) {
  return T.approachSec + whipTimes(T).done;
}
