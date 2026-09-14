/* feel.js - THE HOUSE BOOK for the slot (lane F1). PURE: the numbers and the choices, no DOM, no three,
 * no audio, so node:test holds the Brake to its word. scene.js, bank.js, sound.js and station.js only
 * ever play what this file decides.
 *
 * The shared atoms (THE THUD, THE SHIVER, THE BANK's timings and its reversed token count) come from
 * arcademy/shell/counterfx.js so a thud is a thud in the Arcademy and in the Back Room. */

import { CFX, bankCount } from '../../../arcademy/shell/counterfx.js';

export const FEEL = Object.freeze({
  ANSWER_MS: 100,                 // Law VIII: every input answers inside this, before any network reply
  LEAN_MS: 80,                    // the lever's lean into a press (inside ANSWER_MS)
  THUD_MS: CFX.THUD_MS,           // THE THUD: 340 ms, cubic-bezier(.2,1.5,.4,1), pitched cue on the same frame
  THUD_EASE: [0.2, 1.5, 0.4, 1],
  REVEAL_MS: 620,                 // THE REVEAL: the declared jackpot hero, cubic-bezier(.2,1.35,.35,1)
  REVEAL_EASE: [0.2, 1.35, 0.35, 1],
  SHIVER_MS: CFX.SHIVER_MS,       // THE SHIVER: +-4 px, three cycles, no colour change
  SHIVER_PX: 4,
  GLOW_OUT_MS: CFX.GLOW_MS,       // THE GLOW: in fast, out slow (480 ms)
  GLOW_IN_MS: 80,
  BANK_FLY_MS: CFX.BANK_FLY_MS,   // THE BANK: 560 ms a token, 70 ms stagger, 3-7 tokens, 4 on lite
  BANK_STAGGER_MS: CFX.BANK_STAGGER_MS,
  BANK_MIN: CFX.BANK_MIN,
  BANK_MAX: CFX.BANK_MAX,
  BANK_MAX_LITE: CFX.BANK_MAX_LITE,
  GLANCE_HOLD_MS: 600,            // THE MASCOT GLANCE holds 400-800 ms...
  GLANCE_HOLD_MELT_MS: 800,       // ...and slows while melted (Brake 5)
  BREATH_MS: 3200,                // THE BREATH: one element (the lever at rest), 2.6-4 s ease-in-out
  LADDER_CAP: 7,                  // THE CHIME LADDER: +1 semitone a step, 7 at most
  FANFARE_TIMES: 3,               // Brake 3: the first three get the fanfare...
  THUD_ONLY_FROM: 40,             // ...the 40th is a thud and the tokens
  MOVE_CAP_MS: 620,               // no move longer, except the declared hero (the jackpot REVEAL)
  STROBE_MIN_MS: 1000 / 6,        // no light changes faster than 6 Hz
});

/** Law IX tiers: 0 nothing, 1 small (a chime), 2 bigger (two notes and a jolt), 3 big (THE THUD), 4 jackpot (THE REVEAL). */
const LINE_TIER = { spiral2: 1, sub2: 1, gif3: 1, spiral3: 2, sub3: 2, gif3same: 3, emi3: 4 };
export function tierOf(o) {
  if (!o) return 0;
  if (o.line === 'emi3') return 4;              // halved by melt it is still the jackpot
  if (!(o.pay > 0)) return 0;
  return LINE_TIER[o.line] || (o.pay >= 40 ? 3 : o.pay >= 10 ? 2 : 1);
}

/** Melted means the Brake's focus state: this spin was halved, or melt is still running after it. */
export const meltedBy = o => !!o && (!!o.halved || (o.meltLeft || 0) > 0);

/**
 * THE BRAKE, per landed outcome. `seen` = how many earlier outcomes of this tier celebrated this sit-down,
 * `jackpots` = earlier jackpots this sit-down (Law IX: the top tier's REVEAL plays once a run).
 *   party: 'shiver' | 'melt' | 'fanfare' | 'bead' | 'thud'
 *   sound: 'muted' (a no-pay thud) | 'chime' | 'two' | 'thud' | 'reveal'
 */
export function recipe(o, { seen = 0, jackpots = 0 } = {}) {
  const tier = tierOf(o), melted = meltedBy(o);
  const r = { tier, melted, party: 'fanfare', sound: 'chime', tokens: tier > 0, heat: tier, gold: false,
              chase: false, screen: false, jolt: false, reveal: false, sparks: false, shiver: false };
  if (tier === 0) return { ...r, party: 'shiver', sound: 'muted', heat: 0, shiver: true };   // Brake 6: never silence
  if (melted) return { ...r, party: 'melt', heat: Math.min(1, tier) };                         // Brake 5: no ceremonies
  if (tier === 4 && jackpots === 0) {
    return { ...r, sound: 'reveal', gold: true, chase: true, screen: true, jolt: true, reveal: true, sparks: true };
  }
  if (seen >= FEEL.THUD_ONLY_FROM - 1) return { ...r, party: 'thud', sound: 'thud', heat: 0 };
  if (seen >= FEEL.FANFARE_TIMES) return { ...r, party: 'bead', sound: 'chime' };            // chime + the marquee bead
  return { ...r, sound: tier === 1 ? 'chime' : tier === 2 ? 'two' : 'thud',
           gold: tier === 4, chase: true, screen: tier >= 2, jolt: tier >= 2 };
}

/** THE CHIME LADDER: `step` wins in a row before this one, capped, an octave down while melted. */
export function ladderSemis(step, melted) {
  const s = Math.max(0, Math.min(FEEL.LADDER_CAP, Math.floor(Number(step) || 0)));
  return melted ? s - 12 : s;
}

/** THE BANK token count: a win by its tier, a spend by its cost (counterfx's reversed count). */
export function winTokens(tier, lite) {
  const hi = lite ? FEEL.BANK_MAX_LITE : FEEL.BANK_MAX;
  return Math.max(FEEL.BANK_MIN, Math.min(hi, tier >= 4 ? FEEL.BANK_MAX : tier + 2));
}
export const spendTokens = (cost, lite) => bankCount(cost, lite);

/** The readout value after each landing: evenly stepped, the last one exactly `to` (Law X: ticks on landing). */
export function tickValues(from, to, n) {
  const a = Math.round(Number(from) || 0), b = Math.round(Number(to) || 0), k = Math.max(1, n | 0);
  return Array.from({ length: k }, (_, i) => (i === k - 1 ? b : Math.round(a + (b - a) * ((i + 1) / k))));
}

/** THE MASCOT GLANCE. Poses from the face atlas. Never the same pose twice in a row: a repeat takes its alternate. */
export const POSES = Object.freeze(['idle0_0', 'hearts', 'spirals', 'melt', 'jackpot']);
const ALT = { idle0_0: 'spirals', spirals: 'idle0_0', hearts: 'spirals', melt: 'idle0_0', jackpot: 'hearts' };
export function glance(prev, want) {
  const w = POSES.includes(want) ? want : 'idle0_0';
  return w === prev ? ALT[w] : w;
}
export const restPose = meltLeft => ((meltLeft || 0) > 0 ? 'melt' : 'idle0_0');
export const pressPose = () => 'spirals';
export function landPose(o) {
  if (!o) return 'idle0_0';
  if (o.line === 'emi3') return 'jackpot';
  if (o.pay > 0) return 'hearts';
  if (o.line === 'melt' || (o.meltLeft || 0) > 0) return 'melt';
  return 'idle0_0';
}
export const glanceHoldMs = melted => (melted ? FEEL.GLANCE_HOLD_MELT_MS : FEEL.GLANCE_HOLD_MS);

/** THE BREATH, 0..1..0 over BREATH_MS, ease-in-out. */
export const breath = t => (1 - Math.cos((2 * Math.PI * t) / FEEL.BREATH_MS)) / 2;

/** THE SHIVER offset in px at `ms` into it (0 outside). */
export function shiverPx(ms) {
  if (!(ms >= 0) || ms >= FEEL.SHIVER_MS) return 0;
  return FEEL.SHIVER_PX * Math.sin((ms / FEEL.SHIVER_MS) * Math.PI * 6) * (1 - ms / FEEL.SHIVER_MS);
}

/** THE MARQUEE: chase step period for a heat 0..4, never faster than the strobe floor. */
export const chaseMs = heat => Math.max(FEEL.STROBE_MIN_MS * 2, 1900 - 340 * Math.max(0, Math.min(4, heat)));

/** A cubic-bezier(x1,y1,x2,y2) easing, solved for x. */
export function bezier([a, b, c, d], x) {
  const q = Math.min(1, Math.max(0, x));
  let lo = 0, hi = 1, t = q;
  for (let i = 0; i < 16; i++) { t = (lo + hi) / 2; const v = 3 * (1 - t) ** 2 * t * a + 3 * (1 - t) * t * t * c + t ** 3; if (v < q) lo = t; else hi = t; }
  return 3 * (1 - t) ** 2 * t * b + 3 * (1 - t) * t * t * d + t ** 3;
}
