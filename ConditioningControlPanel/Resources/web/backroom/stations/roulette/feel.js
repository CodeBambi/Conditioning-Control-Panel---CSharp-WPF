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

import { FX_DELAY_MS, CALLOUT_MS } from '../../shared/hypno/callout.js';
import { houseTier } from '../../shared/win/tier.js';
import { ladderSemis } from '../../shared/win/ladder.js';
import { glyphFx, glyphFor } from './glyphs.js';

const TAU = Math.PI * 2;
export const POCKETS = 37;
export const SEG = TAU / POCKETS;

export const FEEL = Object.freeze({
  DT: 1 / 120,                // planner step, real seconds
  SPIN_MS: 8000,              // one spin of a tape, launch to the next launch (the sim's page pace)
  MIN_HOLD_MS: 1500,          // the landing stays on screen at least this long before the next spin
  WIN_HOLD_MS: FX_DELAY_MS + CALLOUT_MS,   // a paying landing: the next spin waits out the highlight, the callout and the fx (2000)
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
/**
 * When the next spin of a tape may launch. A paying landing (`landMs`, `win`) holds WIN_HOLD_MS from the
 * landing frame. `partyMs` is THE REWARD's own hold (plan.partyMs, CONTRACT 10.22): a party owns the station
 * until it is finished, so the 6 s hero climb is never cut off by the next launch (Law X, one gesture one
 * beat). It is measured from the fx frame, FX_DELAY_MS after the landing, and 0 leaves the old hold exactly
 * as it was.
 */
export const nextLaunchAt = (launchMs, restMs, { landMs = null, win = false, partyMs = 0 } = {}) => Math.max(launchMs + FEEL.SPIN_MS, restMs + FEEL.MIN_HOLD_MS,
  win && Number.isFinite(landMs) ? landMs + Math.max(FEEL.WIN_HOLD_MS, FX_DELAY_MS + Math.max(0, Number(partyMs) || 0)) : -Infinity);

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
  // The ball's validated rattle uses the original rotor-speed range. A stronger throw
  // adds decaying whole-wheel momentum to the picture, carrying ball and wheel together,
  // so the relative landing and its timing remain exactly the server-directed plan.
  const launchVelocity=Number.isFinite(rotVel0)?rotVel0:FEEL.ROTOR_KICK;
  const simulatedVelocity=clamp(launchVelocity,-2,2), excess=launchVelocity-simulatedVelocity;
  const momentumDecay=.7;
  let best = null;
  for (let k = 0; k < 24; k++) {
    const sim = simulate(mulberry32((seed >>> 0) + k * 977), { calm, rotVel0:simulatedVelocity });
    if (sim.landIdx < 0) continue;
    const fits = sim.restT <= FEEL.RUN_BUDGET_S;
    const score = (fits ? 0 : 100) + (sim.hits >= 2 ? 0 : sim.hits === 1 ? 10 : 50) + sim.restT;
    if (!best || score < best.score) best = { sim, score };
    if (fits && sim.hits >= 2) break;
  }
  const s = best.sim, shift = (target - s.landIdx) * SEG;
  // One extra counter-rotation, eased away before the drop. Whole turns leave
  // the server pocket and the complete fret/rattle choreography unchanged.
  const dropAt = Math.max(FEEL.DT, s.ph.findIndex(p => p !== 0) * FEEL.DT);
  const launchArc = i => calm ? 0 : TAU * Math.pow(Math.max(0, 1 - i * FEEL.DT / dropAt), 3);
  const f32 = (a, add = 0) => Float32Array.from(a, (v) => v + add);
  return {
    index: target, calm: !!calm, hits: s.hits,
    landAt: s.landT, restAt: s.restT, duration: s.restT,
    sparks: s.sparks.map((x) => ({ at: x.at, a: x.a + shift })),
    rot: Float32Array.from(s.rot,(v,i)=>v+excess*(1-Math.exp(-momentumDecay*i*FEEL.DT))/momentumDecay), rel: Float32Array.from(s.rel,(v,i)=>v+shift+launchArc(i)), r: f32(s.rr), tscale: f32(s.ts), speed: f32(s.sp), phase: Uint8Array.from(s.ph),
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

/* ---------------------------------------------------------------------------------------------
 * THE HOST RECIPE (the slot's pattern, section 4 ids). Every beat of a spin maps to the host's
 * fullscreen effects, on top of the moments (shared/hypno/moments.js keeps the tunnel run, the
 * haze, the wake spiral hold, the wash and the pocket GIF). PURE: what fires and when, gated and
 * cooled here so node:test holds it to its word; station.js only plays what this table says.
 *
 * Law I: a beat before the landing frame (nomore, launch, wake, rattle) is the same for every
 * spin whatever the tape holds, so nothing on the screen tells the pocket before the ball does.
 * The wake is shown as text at its launch already, so its beat is allowed to know it woke.
 * Law VI: `skip` fires nothing; the station drops its cooldowns, the moments release their holds
 * and the host lets a one-shot settle (suspend and close cancel every primitive the room started).
 * Law 6 (Calm): motion steps drop (a flash burst, a GIF storm, the jackpot hero); words and spirals
 * stay and the host halves them. Nothing is halved here (Normal values only).
 * Gates: NONE (owner direction 2026-09-15): every gate reads as on whatever the host reports; the
 * host enforces its own toggles per primitive. FX_GATE stays as the record of which toggle stood
 * behind an id.
 * THE POCKET GLYPHS (glyphs.js, GLYPHS.md): every landing fires the landed pocket's own id on the thud
 * frame (Law X), win or lose, keyed by the pocket NUMBER page-side. The glyph IS the landing's fullscreen
 * step now: land.win and land.straight fire nothing of their own, the callout names the pay, the Wake
 * tiers keep their signature on top and the streak keeps its storm.
 * The flow on the landing frame (shared/hypno/callout.js): the thud at 0, the pocket and the paying
 * chips glow to HIGHLIGHT_MS, then at FX_DELAY_MS the callout (calloutFor) and the landing's host
 * beat fire together; the next launch waits WIN_HOLD_MS (nextLaunchAt). A miss stays quick.
 * ------------------------------------------------------------------------------------------ */

export const FX = Object.freeze({
  STREAK_FROM: 2,             // the second paying spin in a row brings the storm
  COOLDOWN_MS: Object.freeze({
    'fx.sub_single': 4000, 'fx.sub_pair': 8000, 'fx.sub_cascade': 12000,
    'fx.spiral_brief': 6000, 'fx.spiral_full': 12000,
    'fx.gif_burst': 6000, 'fx.gif_storm': 20000, 'fx.jackpot': 60000, 'fx.melt': 30000,
  }),
});

/** A section 4 id -> the app toggle that used to stand behind it (the record only; nothing drops a step now). */
export const FX_GATE = Object.freeze({
  'fx.gif_burst': 'flash', 'fx.gif_storm': 'flash',
  'fx.sub_single': 'subliminal', 'fx.sub_pair': 'subliminal', 'fx.sub_cascade': 'subliminal',
  'fx.spiral_brief': 'spiral', 'fx.spiral_full': 'spiral',
  'fx.jackpot': 'any', 'fx.melt': 'brainDrain',
});

/** Steps Calm strips (the mockup's reduced motion): a flash burst, a GIF rain, the jackpot hero. */
const MOTION = Object.freeze(['fx.gif_burst', 'fx.gif_storm', 'fx.jackpot']);

const deepFreeze = (o) => { if (o && typeof o === 'object' && !Object.isFrozen(o)) { Object.freeze(o); for (const v of Object.values(o)) deepFreeze(v); } return o; };

/**
 * The table. A step: { fx, gif?: the spin's picture key rides in symbols, words?: n word keys ride in symbols,
 * when?: 'full' | 'below_full' (the intensity it needs), once?: 'spin' (at most once a spin) }.
 */
export const FX_RECIPE = deepFreeze({
  'nomore': [{ fx: 'fx.sub_single', words: 1 }],                                       // the press frame (Law VIII), before any reply
  'launch': [{ fx: 'fx.gif_burst', gif: true }],                                        // every spin's launch, wake or not
  'wake': [{ fx: 'fx.sub_single', words: 1 }],                                          // a Spiral Wake's launch (the spiral hold is the moments')
  'rattle': [{ fx: 'fx.sub_single', words: 1, once: 'spin' }],                          // the first fret clip
  'run': [],                                                                             // the tunnel run is moments.tunnel(rouletteRunLevel)
  'near': [{ fx: 'fx.spiral_brief' }],                                                  // a miss one pocket off a covered number
  'glyph': [],                                                                           // the landed pocket's glyph (glyphFx), filled per pocket by fxPlan
  'land.miss': [],                                                                       // the page's chip vortex, nothing fullscreen
  'land.win': [],                                                                        // an outside bet pays: the glyph is its effect, the callout its name
  'land.straight': [],                                                                   // a straight-up hit: the glyph again (the wash and the pocket GIF are the moments')
  'land.wake': [{ fx: 'fx.spiral_full' }],                                              // a woken win: the turret's spiral goes full
  'land.full': [{ fx: 'fx.jackpot', gif: true, when: 'full' }, { fx: 'fx.sub_cascade', gif: true, when: 'below_full' }],   // a straight-up hit on a wake
  'streak': [{ fx: 'fx.gif_storm', gif: true }],                                        // STREAK_FROM paying spins in a row
  'skip': [],                                                                            // Back or suspend (Law VI)
});

export const FX_BEATS = Object.freeze(Object.keys(FX_RECIPE));

/**
 * The straight chips one pocket off the landing on the WHEEL (not the mat). Reads the tape's bets and
 * state.wheel, never a page copy of the order. -> the covered neighbour numbers (empty when none).
 */
export function nearMisses(read, bets, wheel) {
  const w = Array.isArray(wheel) ? wheel : [];
  const i = read ? Number(read.index) : -1;
  if (!(i >= 0 && i < w.length) || !Array.isArray(bets)) return [];
  const n = w.length, left = w[(i - 1 + n) % n], right = w[(i + 1) % n];
  const straights = new Set(bets.map((b) => (b && /^s\d+$/.test(String(b.spot)) ? Number(String(b.spot).slice(1)) : NaN)));
  return [left, right].filter((x) => Number.isFinite(x) && straights.has(x));
}

/** The landing beat for a read outcome (tape.readOutcome). `near` = the covered neighbours (nearMisses) on a miss. */
export function landBeat(read, near = []) {
  if (!read || !(read.pay > 0)) return Array.isArray(near) && near.length ? 'near' : 'land.miss';
  if (read.straight && read.wake) return 'land.full';
  if (read.straight) return 'land.straight';
  return read.wake ? 'land.wake' : 'land.win';
}

/** Every known id fires: gates no longer drop a step. */
const gateOn = (_gates, fx) => !!FX_GATE[fx];

/** The glyph beat's row for a landed pocket: its one id (glyphs.js), or nothing on the house pocket. */
const glyphRows = (pocket) => { const fx = glyphFx(pocket); return fx ? [{ fx }] : []; };

/**
 * What a beat fires, after Calm and the intensity (`gates` is read for nothing). `streak` = paying spins in a
 * row, this one included: the storm rides a paying landing at STREAK_FROM and above. `pocket` = the landed
 * pocket number, read by the glyph beat alone.
 * -> [{ fx, gif, words, once }] in firing order (the beat's own steps first, then the streak's)
 */
export function fxPlan(beat, { gates = null, calm = false, full = false, streak = 0, pocket = null } = {}) {
  const rows = beat === 'glyph' ? glyphRows(pocket) : FX_RECIPE[beat];
  if (!rows) return [];
  const list = rows.slice();
  // Brake 2, one hero per beat: the storm rides a paying landing, never the jackpot hero's own frame.
  if (beat.startsWith('land.') && beat !== 'land.miss' && streak >= FX.STREAK_FROM && !(full && !calm && list.some((s) => s.fx === 'fx.jackpot'))) list.push(...FX_RECIPE.streak);
  const isFull = !!full && !calm;
  return list.filter((s) => {
    if (s.when === 'full' && !isFull) return false;
    if (s.when === 'below_full' && isFull) return false;
    if (calm && MOTION.includes(s.fx)) return false;
    return gateOn(gates, s.fx);
  }).map((s) => ({ fx: s.fx, gif: !!s.gif, words: s.words || 0, once: s.once || null }));
}

/**
 * THE CALLOUT (shared/hypno/callout.js): the diegetic name of a landing, one a spin at most, shown at FX_DELAY_MS
 * with the host beat. By the landing beat: an outside win, a straight-up hit, a woken win, a straight-up on a wake.
 * "On A Roll" is the streak's own name and shows only when the spin's beat named nothing (never on a miss or a
 * near miss; with every paying beat named above it is the fallback, not a second callout: Brake 2).
 */
export const CALLOUTS = Object.freeze({
  'land.win': { key: 'br_callout_chips_in', fallback: 'Chips In', tier: 'small' },
  'land.straight': { key: 'br_callout_straight_up', fallback: 'Straight Up', tier: 'big' },
  'land.wake': { key: 'br_callout_spiral_wake', fallback: 'Spiral Wake', tier: 'big' },
  'land.full': { key: 'br_callout_full_wake', fallback: 'Full Wake', tier: 'hero' },
  streak: { key: 'br_callout_on_a_roll', fallback: 'On A Roll', tier: 'small' },
});
export function calloutFor(beat, { streak = 0 } = {}) {
  if (Object.prototype.hasOwnProperty.call(CALLOUTS, beat) && beat !== 'streak') return CALLOUTS[beat];
  if (typeof beat === 'string' && beat.startsWith('land.') && beat !== 'land.miss' && streak >= FX.STREAK_FROM) return CALLOUTS.streak;
  return null;
}

/** The dealt keys a step carries: the spin's picture (deck.pickKey) and `words` word keys turned by the spin index. */
export function fxSymbols(step, { gif = null, spin = 0, wordCount = 4 } = {}) {
  const out = [];
  if (step.gif && typeof gif === 'string' && /^g\d{1,2}$/.test(gif)) out.push(gif);
  const n = Math.max(1, Math.trunc(Number(wordCount)) || 4), i = Math.max(0, Math.trunc(Number(spin)) || 0);
  for (let k = 0; k < (step.words || 0); k++) out.push('s' + ((i + k) % n));
  return out;
}

/** The cooldown ledger: one clock per fx id (FX.COOLDOWN_MS) and the once-a-spin steps. Back and suspend reset it. */
export function createFxCooldowns() {
  let last = new Map(), spinKey = null, spent = new Set();
  return {
    /** May `step` fire at `nowMs` for spin `spin`? Taking it starts its cooldown. */
    take(step, nowMs, spin = null) {
      const fx = typeof step === 'string' ? step : step.fx, once = typeof step === 'string' ? null : step.once;
      const key = fx + '@' + once;
      if (once === 'spin') {
        if (spin !== spinKey) { spinKey = spin; spent = new Set(); }
        if (spent.has(key)) return false;
      }
      const gap = FX.COOLDOWN_MS[fx] || 0, prev = last.get(fx);
      if (prev != null && nowMs - prev < gap) return false;   // cooled: the once-a-spin step stays unspent for a later clip
      last.set(fx, nowMs);
      if (once === 'spin') spent.add(key);
      return true;
    },
    reset() { last = new Map(); spinKey = null; spent = new Set(); },
    debug() { return { last: Object.fromEntries(last), spin: spinKey, spent: [...spent] }; },
  };
}

/* ---------------------------------------------------------------------------------------------
 * THE REWARD (CONTRACT 10.22), PURE. What a landing is WORTH and what it may SPEND, so the wiring in
 * station.js only has to play it. Law IX, X, XII and XIII with Brakes 2, 3, 5, 8 and 9 live in
 * shared/win/ and NOWHERE else: nothing here re-derives a restraint, it only says what the roulette's
 * own vocabulary calls the rung, the voice, the pitch and the chips a win leaves from.
 *
 * WHY THIS SECTION EXISTS. station.js took only ctx.spReadout.owe(), so a pay made the SP number simply
 * redraw - Law XII broken outright, the one thing the reward pass was called for. THE BANK now flies from
 * the paying chips to the chip that keeps them, and the rung it flies at is decided here.
 *
 * Law I: every function below reads a SETTLED read (tape.readOutcome) and nothing else. No pocket, pay or
 * rung is known here one frame before the ball is in the server's pocket.
 * ------------------------------------------------------------------------------------------ */

export const REWARD = Object.freeze({
  REVEAL_MS: 620,                                    // THE REVEAL, the declared hero move; nothing else here is over 620 ms
  REVEAL_EASE: 'cubic-bezier(.2,1.35,.35,1)',        // house-book 2, the same curve the wheel's pot uses
  GAIN_MS: 1600,                                     // how long +N SP stands when no count is running behind it (Brake 9)
  /** plan.spent -> the kit's win voice (shared/sound/kit.js). Rung 0 never sounds a win at all. */
  WIN_SOUND: Object.freeze(['settle', 'small', 'mid', 'big', 'hero']),
});

/**
 * The rung a settled read is worth, 0..4, through the house's one entry point. The roulette has no numeric
 * recipe of its own: its rung IS its callout size (CALLOUTS above - small 1, big 3, hero 4), RAISED by the
 * pay (tier.PAY_STEPS), which is the only way this station reaches a bare 2. `near` is nearMisses(), so a
 * miss beside a covered number stays the 0 it is - a near miss is never a small win (10.22, "no losses
 * disguised as wins").
 */
export function rewardTier(read, near = []) {
  if (!read || !(Number(read.pay) > 0)) return 0;
  return houseTier({ station: 'roulette', moment: landBeat(read, near), pay: Number(read.pay) });
}

/** The kit's win voice for a SPENT rung (plan.spent, never plan.tier - the brakes have already shrunk it). */
export function winSound(spent) {
  const t = Math.max(0, Math.min(4, Math.floor(Number(spent) || 0)));
  return REWARD.WIN_SOUND[t];
}

/**
 * THE CHIME LADDER's root for a streak. `streak` is the paying spins in a row INCLUDING this one (station.js
 * counts it on the landing frame), so the first pay is the root note and every pay after it is a semitone up,
 * capped at 7 by ladderSemis. Brake 5 drops the whole ladder an octave while melted, which is exactly
 * plan.octave: the station never adds the octave twice.
 */
export const ladderRoot = (streak, melted = false) => ladderSemis(Math.max(0, (Math.trunc(Number(streak)) || 0) - 1), melted);

/**
 * Brake 5 at the roulette: is this landing a focus state? The pocket glyphs (GLYPHS.md) give the station its
 * own answer - a landing on a `drop` pocket fires fx.melt on that very frame, so the beat the melt arrives on
 * is a melted beat. The party drops to a chime an octave down and the tokens still fly: a melted win is quiet,
 * never invisible. Nothing else at this station is a trance, so nothing else reads as melted.
 */
export const meltedBy = (read) => !!read && glyphFor(read.pocket) === 'drop';

/**
 * Law XII: value leaves WHERE IT WAS WON. `n` tokens dealt round-robin over the paying spots, so every chip
 * that paid sends something and the handful leaves the mat spread out instead of stacked on one cell.
 * -> an array of `n` spot ids; a null entry means "no cell to leave from" and the caller falls back to the
 * landed pocket on the wheel.
 */
export function tokenSpots(n, hits) {
  const count = Math.max(0, Math.trunc(Number(n)) || 0);
  const list = Array.isArray(hits) ? hits.filter((s) => typeof s === 'string' && s) : [];
  return Array.from({ length: count }, (_, i) => (list.length ? list[i % list.length] : null));
}
