/* ============================================================================
 * feel.js - Velvet Vortex timings and choreography, PURE (CONTRACT 10.13.F).
 *
 *   landMoment(read)   a read outcome -> 'roulette.land.miss' | '.win' | '.big'
 *   beamAngle / beamLit / litNumbers
 *                      the Lighthouse (law 4): the beam turns on its OWN clock,
 *                      beamAngle = -0.7 x t rad, against the rotor, half-width
 *                      0.24 rad, and lights every number it passes. It is never
 *                      derived from the rotor angle (the mockup's first cut was,
 *                      and only ever lit 0 and 32).
 *   planRun(...)       the ball's run for one spin, PLANNED BACKWARDS from the
 *                      server's pocket: the mockup's run, drop and fret rattle
 *                      are simulated forward on a seeded rng, then the whole
 *                      ball path is turned by a whole number of pockets so the
 *                      pocket it settles in is `index`. Frets stay frets, every
 *                      clip keeps its spark, and the landing never changes.
 *   sampleRun(plan, s) the plan at s seconds of real time since launch.
 *
 * Handedness (law 3): the rotor turns clockwise on screen (angle grows), the
 * ball runs against it, and the turret arms bend back against the rotor.
 * ==========================================================================*/

const TAU = Math.PI * 2;
export const POCKETS = 37;
export const SEG = TAU / POCKETS;

export const FEEL = Object.freeze({
  DT: 1 / 120,                // planner step, real seconds
  SPIN_MS: 8000,              // one spin of a tape, launch to the next launch (the sim's page pace)
  MIN_HOLD_MS: 1500,          // the landing stays on screen at least this long before the next spin
  RUN_BUDGET_S: 6.4,          // launch -> ball at rest, so a spin fits SPIN_MS
  SLOW: 0.42, SLOW_CALM: 0.7, // the rattle's slow motion (law 6 raises the floor)
  SLOW_EASE: 8,
  ROTOR_IDLE: 0.4, ROTOR_KICK: 1.5, ROTOR_EASE: 0.22, ROTOR_CALM_EASE: 1.5,
  RUN_V: Object.freeze([8.2, 9.6]), DROP_V: 3.2, DROP_RATE: 0.24,
  R_RIM: 0.86, R_FRET: 0.62, R_REST: 0.6, R_BOUNCE: 0.66,
  RATTLE_V: Object.freeze([2.2, 3.0]), RATTLE_MAX_S: 1.8, RATTLE_DAMP: 0.9, CLIP_MIN_V: 0.8, REST_V: 0.35,
  MAX_CLIPS: 3, SPARK_S: 0.5, SPARK_GAP_S: 0.34,
  SETTLE_RATE: 2.2,
  BEAM_SPEED: -0.7, BEAM_HALF: 0.24,
  WHIRL_MUL: 2.2, WHIRL_ALPHA: 0.85, WHIRL_FADE: 1.2,
  WAKE_SETTLE_S: 1.3,
  CHIP_DELAY_MS: 500, CHIP_MS: 1800, CHIP_STAGGER_MS: 300,
  GIF_BOX: Object.freeze({ w: 40, h: 30 }), GIF_RADIUS: 0.7,
});

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
export const wrapAngle = (a) => ((a % TAU) + TAU) % TAU;
export const ease = (p) => 1 - Math.pow(1 - clamp(p, 0, 1), 3);
/** Shortest angular distance, 0..PI. */
export const angDist = (a, b) => Math.abs((((a - b) % TAU) + TAU + Math.PI) % TAU - Math.PI);

/** The page moment for a read outcome (tape.readOutcome). Big: a wake win or a straight hit. */
export function landMoment(read) {
  if (!read || !(read.pay > 0)) return 'roulette.land.miss';
  return read.wake || read.straight ? 'roulette.land.big' : 'roulette.land.win';
}

/** Law 4: the beam's angle at t seconds on the station's own clock. */
export const beamAngle = (tSec) => FEEL.BEAM_SPEED * (Number(tSec) || 0);
/** How lit a number at screen angle `a` is under a beam at `beamA`, 0..1. */
export const beamLit = (a, beamA) => clamp(1 - angDist(a, beamA) / FEEL.BEAM_HALF, 0, 1);
/** Pocket i's centre on screen, the rotor at `rot`. */
export const pocketAngle = (i, rot) => rot + (i + 0.5) * SEG;
/** The numbers the beam lights right now. */
export function litNumbers(wheel, rot, beamA) {
  const out = [];
  for (let i = 0; i < wheel.length; i++) if (beamLit(pocketAngle(i, rot), beamA) > 0) out.push(wheel[i]);
  return out;
}
/** The whirlpool's Loom angle: the rotor's clockwise angle x 2.2. */
export const whirlAngle = (rot) => rot * FEEL.WHIRL_MUL;
/** When the next spin of a tape may launch. */
export const nextLaunchAt = (launchMs, restMs) => Math.max(launchMs + FEEL.SPIN_MS, restMs + FEEL.MIN_HOLD_MS);

function mulberry32(a) {
  return () => {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
/** A stable seed for a spin: FNV-1a of the tape id and the spin index. */
export function seedFor(tapeId, i) {
  const s = String(tapeId) + ':' + i;
  let h = 0x811c9dc5;
  for (let k = 0; k < s.length; k++) { h ^= s.charCodeAt(k); h = Math.imul(h, 0x01000193); }
  return h >>> 0;
}

const PHASES = ['run', 'drop', 'rattle', 'settle', 'rest'];
const pocketOf = (rel) => Math.floor(wrapAngle(rel) / SEG) % POCKETS;

/** One forward run of the mockup's ball on `rnd`. Angles are continuous (never wrapped). */
function simulate(rnd, { calm, rotVel0 }) {
  const F = FEEL, DT = F.DT, cap = Math.ceil(20 / DT);
  const rot = [], rel = [], rr = [], ts = [], sp = [], ph = [], sparks = [];
  let t = 0, r = 0, rv = rotVel0, b = rnd() * TAU, bv = -(F.RUN_V[0] + rnd() * (F.RUN_V[1] - F.RUN_V[0]));
  let br = F.R_RIM, phase = 0, tscale = 1, hits = 0, rattleT = 0, lastHit = -9, last = 0, settle = 0, from = 0, offset = 0, landIdx = -1, landT = 0;
  const push = () => { rot.push(r); rel.push(b - r); rr.push(br); ts.push(tscale); sp.push(Math.abs(bv)); ph.push(phase); };
  push();
  for (let k = 0; k < cap; k++) {
    const slowT = phase === 2 ? (calm ? F.SLOW_CALM : F.SLOW) : 1;
    tscale += (slowT - tscale) * Math.min(1, DT * F.SLOW_EASE);
    const dt = DT * tscale;
    t += DT;
    rv += (F.ROTOR_IDLE - rv) * (1 - Math.exp(-dt * F.ROTOR_EASE));
    r += rv * dt;
    if (phase === 0 || phase === 1) {
      bv += (Math.abs(bv) * 0.3 + 0.2) * dt;
      if (phase === 1 && bv > -0.6) bv = -0.6;
      b += bv * dt;
      if (phase === 0 && Math.abs(bv) < F.DROP_V) phase = 1;
      if (phase === 1) {
        br -= F.DROP_RATE * dt;
        if (br <= F.R_FRET) {
          br = F.R_FRET; phase = 2; rattleT = 0;
          bv = rv - (F.RATTLE_V[0] + rnd() * (F.RATTLE_V[1] - F.RATTLE_V[0]));
          last = pocketOf(b - r);
        }
      }
    } else if (phase === 2) {
      rattleT += dt;
      let relV = (bv - rv) * Math.exp(-dt * F.RATTLE_DAMP);
      bv = rv + relV;
      b += bv * dt;
      br += (F.R_REST - br) * Math.min(1, dt * 6);
      const relNow = b - r, i = pocketOf(relNow);
      if (i !== last) {
        if (Math.abs(relV) > F.CLIP_MIN_V && hits < F.MAX_CLIPS && t - lastHit >= F.SPARK_GAP_S) {
          // the fret just crossed, as a continuous relative angle
          const fret = relV > 0 ? Math.floor(relNow / SEG) * SEG : (Math.floor(relNow / SEG) + 1) * SEG;
          sparks.push({ at: t, a: fret });
          hits++; lastHit = t;
          const dir = Math.sign(relV);
          relV *= hits === 2 ? 0.55 : -0.5;
          bv = rv + relV; br = F.R_BOUNCE;
          // a bounce goes back into the pocket it came from; a glancing clip carries on into the next
          b = r + fret + (Math.sign(relV) === dir ? 0.01 : -0.01) * dir;
          last = pocketOf(b - r);
        } else last = i;
      }
      if (Math.abs(relV) < F.REST_V || rattleT > F.RATTLE_MAX_S) {
        phase = 3; landT = t; landIdx = pocketOf(b - r);
        from = b - r; offset = Math.floor(from / SEG) * SEG + SEG / 2; settle = 0;
      }
    } else if (phase === 3) {
      settle = Math.min(1, settle + DT * F.SETTLE_RATE);
      b = r + from + (offset - from) * ease(settle);
      bv = rv;
      br += (F.R_REST - br) * Math.min(1, DT * 6);
      if (settle >= 1) { phase = 4; push(); break; }
    }
    push();
  }
  return { rot, rel, rr, ts, sp, ph, sparks, hits, landIdx, landT, restT: t };
}

/**
 * Plan one spin. `index` is the pocket's place in state.wheel (0..36). Tries up to 24 seeded runs and keeps the
 * first with two or three clips inside the time budget, else the best it saw (at least one clip, shortest).
 * @returns {{ index, calm, hits, landAt, restAt, duration, sparks:[{at, a}], rot, rel, r, tscale, speed, phase }}
 */
export function planRun({ index, seed = 1, calm = false, rotVel0 = FEEL.ROTOR_KICK } = {}) {
  const target = ((Math.trunc(Number(index)) % POCKETS) + POCKETS) % POCKETS;
  let best = null;
  for (let k = 0; k < 24; k++) {
    const sim = simulate(mulberry32((seed >>> 0) + k * 977), { calm, rotVel0 });
    if (sim.landIdx < 0) continue;
    const fits = sim.restT <= FEEL.RUN_BUDGET_S;
    const score = (fits ? 0 : 100) + (sim.hits >= 2 ? 0 : sim.hits === 1 ? 10 : 50) + sim.restT;
    if (!best || score < best.score) best = { sim, score };
    if (fits && sim.hits >= 2) break;
  }
  const s = best.sim, shift = (target - s.landIdx) * SEG;
  const f32 = (a, add = 0) => Float32Array.from(a, (v) => v + add);
  return {
    index: target, calm: !!calm, hits: s.hits,
    landAt: s.landT, restAt: s.restT, duration: s.restT,
    sparks: s.sparks.map((x) => ({ at: x.at, a: x.a + shift })),
    rot: f32(s.rot), rel: f32(s.rel, shift), r: f32(s.rr), tscale: f32(s.ts), speed: f32(s.sp), phase: Uint8Array.from(s.ph),
  };
}

/** The plan at `sec` real seconds since launch: rotor delta, ball angle relative to the rotor, radius, phase name. */
export function sampleRun(plan, sec) {
  const n = plan.rot.length, x = clamp(Number(sec) / FEEL.DT, 0, n - 1), i = Math.floor(x), j = Math.min(n - 1, i + 1), f = x - i;
  const lerp = (a) => a[i] + (a[j] - a[i]) * f;
  return { rot: lerp(plan.rot), rel: lerp(plan.rel), r: lerp(plan.r), tscale: lerp(plan.tscale), speed: lerp(plan.speed),
    phase: PHASES[plan.phase[i]], done: i >= n - 1 };
}

/** Where the ball rests in pocket `index`, relative to the rotor. */
export const restRel = (index) => (index + 0.5) * SEG;
