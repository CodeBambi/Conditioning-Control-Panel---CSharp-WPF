/* ============================================================================
 * race/pace.js - how fast the road is allowed to feel, second by second.
 *
 * THE OPENING IS GENTLE. The owner played two tracks on a phone and wrote: "the
 * start feels too fast (122mph)". He was reading the speed plate off a combo
 * boost in the first minute of an induction. Nothing about the kart's handling
 * was wrong; what was wrong is that a hypno file opens by putting you under, and
 * the road was already at full pelt while the voice was still saying hello.
 *
 * So the run asks this module for two numbers every frame and hands them to the
 * kart (kart.js pace()): the CRUISE it holds with the accelerator down, and the
 * CEILING a boost may reach. Both are honest metres per second - the speed plate
 * reads the kart's own speed, so whatever this says, the number on the glass is
 * the number the kart has (Law I).
 *
 *   THE ENVELOPE   a track's first act (or the first OPEN_SEC on a road with no
 *                  acts) cruises at OPEN_PACE of the base and a boost may only
 *                  reach OPEN_BOOST_PACE of that base: 15.4 m/s (55 km/h) with a
 *                  lift to 25.3 (91), instead of 22 (79) with a lift to 34 (122).
 *                  Over the OPEN_RAMP_SEC that follow, both walk in a straight
 *                  line up to the full curve, and from there the kart is the kart
 *                  it always was.
 *   THE ACT        an act's kind colours the pace once it is running: an
 *                  induction or a deepening is 0.85 of it, triggers and a mantra
 *                  are the whole of it, a build 1.1, a wake 1.05, a silence 0.75
 *                  (consts.js ACT_PACE). The change is a lean, not a lurch: it
 *                  arrives over ACT_BLEND_SEC from the act's own t0, so the road
 *                  is already leaning while the voice changes its mind.
 *
 * The envelope is a pure function of the second, which is what a seek needs: park
 * the clock anywhere and the road paces itself the same way it would have if it
 * had driven there. Only the act blend carries a memory (which pace it is coming
 * FROM), because that is a thing the file cannot say for you.
 *
 * A NOTE FOR THE ROW PLACEMENT: run.js places a trigger's row at
 * `kart.d + speed * dueIn`, which assumes the speed it reads at handover holds
 * over the lookahead. It does not any more, so the placement is out by the area
 * under the ramp: about 0.33 m/s^2 through the opening ramp, which over a 3 s
 * lookahead is 1.5 m, under a tenth of a second of road. An act change is
 * sharper (up to 1.8 m/s^2 across the blend) and that is the one to watch;
 * race/smoke/pace-check.mjs measures both against the clock.
 * ==========================================================================*/

import {
  KART_BASE_SPEED, KART_MAX_SPEED,
  OPEN_PACE, OPEN_BOOST_PACE, OPEN_SEC, OPEN_RAMP_SEC, ACT_PACE, ACT_BLEND_SEC,
} from './consts.js';

const num = (v, d = 0) => (typeof v === 'number' && isFinite(v) ? v : d);
const clamp01 = (v) => (v < 0 ? 0 : v > 1 ? 1 : v);
/** The boost ceiling the opening allows, in m/s. */
const OPEN_CAP = OPEN_BOOST_PACE * KART_BASE_SPEED;

/**
 * The opening envelope at a second, before the act has coloured it.
 * `openEnd` is the end of the file's first act; 0 or nothing means OPEN_SEC.
 * Returns the cruise as a fraction of KART_BASE_SPEED, the ceiling in m/s, and
 * `k`, how far along the ramp to the full curve this second is (1 = arrived).
 */
export function envelopeAt(t, openEnd) {
  const sec = Math.max(0, num(t));
  const end = num(openEnd) > 0 ? num(openEnd) : OPEN_SEC;
  const k = clamp01((sec - end) / OPEN_RAMP_SEC);
  return {
    pace: OPEN_PACE + (1 - OPEN_PACE) * k,
    cap: OPEN_CAP + (KART_MAX_SPEED - OPEN_CAP) * k,
    k,
    openEnd: end,
  };
}

/** What a kind is worth. An unknown kind (or none) is the road as written. */
export function paceForKind(kind) {
  const v = kind == null ? null : ACT_PACE[kind];
  return typeof v === 'number' && isFinite(v) ? v : 1;
}

/** Where the first act ends, off a chart. Null chart, no acts, or a broken t1: OPEN_SEC. */
export function openEndOf(chart) {
  const acts = chart && Array.isArray(chart.acts) ? chart.acts : null;
  const t1 = acts && acts.length ? num(acts[0].t1, 0) : 0;
  return t1 > 0 ? t1 : OPEN_SEC;
}

/**
 * The run's pace, frame by frame. `at(t, act, chart)` takes the track second (or
 * the run's own elapsed seconds off a chart), track.js's current act and the
 * chart it came from, and gives back the two numbers kart.js wants.
 */
export function createPace() {
  let actId = null;      // the act the blend is running for
  let from = 1;          // the pace it is leaning away from
  let last = 1;          // and the one it reached on the last frame

  return {
    at(t, act, chart) {
      const sec = Math.max(0, num(t));
      const env = envelopeAt(sec, openEndOf(chart));
      const want = paceForKind(act ? act.kind : null);
      const id = act ? (act.id != null ? act.id : act.t0) : null;
      if (id !== actId) { from = last; actId = id; }
      const k = act && isFinite(act.t0) ? clamp01((sec - num(act.t0)) / ACT_BLEND_SEC) : 1;
      const mult = from + (want - from) * k;
      last = mult;
      // while the opening is still running the envelope is a CEILING, not a suggestion: an act
      // kind may only make those seconds gentler (a first act of build does not undo the point)
      const open = env.k < 1;
      const cruise = KART_BASE_SPEED * env.pace;
      const base = Math.min(KART_MAX_SPEED, open ? Math.min(cruise, cruise * mult) : cruise * mult);
      const ceil = open ? Math.min(env.cap, env.cap * mult) : env.cap * mult;
      const cap = Math.min(KART_MAX_SPEED, Math.max(base, ceil));
      return { base, cap, mult, kind: act ? act.kind || null : null, opening: env.k < 1, k: env.k, openEnd: env.openEnd };
    },
    /** A fresh run (or a seek that changed the file): forget which act we came from. */
    reset() { actId = null; from = 1; last = 1; },
  };
}
