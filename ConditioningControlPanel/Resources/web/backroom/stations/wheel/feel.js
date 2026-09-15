/* feel.js - THE HOUSE BOOK for the Daily Daze wheel. PURE: the numbers and the choices, no DOM, no three,
 * no audio. scene.js, bank.js, sound.js and station.js only ever play what this file decides.
 *
 * The shared atoms (THE THUD, THE SHIVER, THE GLOW, THE BANK's timings) come from arcademy/shell/counterfx.js,
 * the same stable path the slot's feel pass reads, so a thud is a thud in every room. The slot's own feel.js is
 * not imported: its tiers are paylines, the wheel's are one landing a day. */

import { CFX } from '../../../arcademy/shell/counterfx.js';

export const FEEL = Object.freeze({
  ANSWER_MS: 100,                 // Law VIII: every input answers inside this, before any network reply
  THUD_MS: CFX.THUD_MS,           // THE THUD: 340 ms, cubic-bezier(.2,1.5,.4,1), pitched cue on the same frame
  THUD_EASE: [0.2, 1.5, 0.4, 1],
  REVEAL_MS: 620,                 // THE REVEAL: the jackpot hero only, cubic-bezier(.2,1.35,.35,1)
  REVEAL_EASE: [0.2, 1.35, 0.35, 1],
  SHIVER_MS: CFX.SHIVER_MS,       // THE SHIVER: +-4 px, three cycles, no colour change
  SHIVER_PX: 4,
  GLOW_OUT_MS: CFX.GLOW_MS,
  BANK_FLY_MS: CFX.BANK_FLY_MS,   // THE BANK: 560 ms a token, 70 ms stagger, 3-7 tokens, 4 on lite
  BANK_STAGGER_MS: CFX.BANK_STAGGER_MS,
  BANK_MIN: CFX.BANK_MIN,
  BANK_MAX: CFX.BANK_MAX,
  BANK_MAX_LITE: CFX.BANK_MAX_LITE,
  GLANCE_HOLD_MS: 600,            // THE MASCOT GLANCE holds 400-800 ms
  BREATH_MS: 3200,                // THE BREATH: one element (the jackpot star), 2.6-4 s ease-in-out
  LADDER_CAP: 7,                  // THE CHIME LADDER: +1 semitone a step, 7 at most
  STROBE_MIN_MS: 1000 / 6,        // no light or tick changes faster than 6 Hz
  PARTY_MS: [900, 700, 900, 1000, 2400],   // per tier: how long the wheel celebrates (THE BREATH waits it out)
});

/** Law IX tiers for a landed result: 0 Snooze, 1 small (1-3 SP, a chime), 2 bigger (5-20, two notes and a jolt),
 *  3 big (40 and up, THE THUD), 4 the pot (THE REVEAL). A gated jackpot hit pays Dazed and is a tier 3.
 *  Tiers pick sounds, tokens and the bulbs only: the fullscreen moment is the kit's wheelSize (CONTRACT 10.13.F). */
export function tierOf(r) {
  if (!r) return 0;
  if (r.jackpotWon) return 4;
  if (r.reward?.kind === 'double' || (r.reward?.kind === 'decoration' && !r.reward.fallback)) return 2;
  if (r.snoozed || !(r.pay > 0)) return 0;
  return r.pay >= 40 ? 3 : r.pay >= 5 ? 2 : 1;
}

/**
 * The party for one landing. `still` = reduced motion or Calm (Law VI: the settled state, no travel).
 *   sound: 'snooze' (a muted thud and a yawn, Brake 6) | 'chime' | 'two' | 'thud' | 'reveal'
 */
export function recipe(r, { still = false } = {}) {
  const tier = tierOf(r);
  const base = { tier, heat: tier, tokens: tier > 0, gold: tier === 4, jolt: tier >= 2, reveal: false, sparks: false,
                 shiver: false, sleepy: false, partyMs: FEEL.PARTY_MS[tier] };
  const sound = ['snooze', 'chime', 'two', 'thud', 'reveal'][tier];
  if (tier === 0) return { ...base, sound, heat: 0, tokens: false, shiver: !still, sleepy: true };
  if (tier === 4) return { ...base, sound, reveal: !still, sparks: !still };
  return { ...base, sound };
}

/** THE BANK token count for a pay tier. */
export function winTokens(tier, lite) {
  const hi = lite ? FEEL.BANK_MAX_LITE : FEEL.BANK_MAX;
  return Math.max(FEEL.BANK_MIN, Math.min(hi, tier >= 4 ? FEEL.BANK_MAX : tier + 2));
}

/** The readout value after each token lands: evenly stepped, the last one exactly `to` (Law X). */
export function tickValues(from, to, n) {
  const a = Math.round(Number(from) || 0), b = Math.round(Number(to) || 0), k = Math.max(1, n | 0);
  return Array.from({ length: k }, (_, i) => (i === k - 1 ? b : Math.round(a + (b - a) * ((i + 1) / k))));
}

/**
 * THE CHIME LADDER on the pointer ticks. A peg crossing plays (a tick cue, a pointer kick, the highlight moves)
 * only when the last played tick is at least STROBE_MIN_MS old, so nothing flickers over 6 Hz however fast the
 * wheel turns. Once crossings come slower than that (the wheel is settling), each played tick climbs a
 * semitone, capped at LADDER_CAP. Returns the next ladder state and whether this crossing plays.
 */
export function tick(ladder, now, crossingGapMs) {
  const s = ladder || { lastAt: -Infinity, step: 0 };
  if (now - s.lastAt < FEEL.STROBE_MIN_MS) return { play: false, semis: 0, ladder: s };
  const slow = crossingGapMs >= FEEL.STROBE_MIN_MS;
  const step = slow ? Math.min(FEEL.LADDER_CAP, s.step + 1) : 0;
  return { play: true, semis: step, ladder: { lastAt: now, step } };
}

/** THE MASCOT GLANCE. Poses from the face atlas. Never the same pose twice in a row: a repeat takes its alternate. */
export const POSES = Object.freeze(['idle0_0', 'hearts', 'spirals', 'melt', 'jackpot']);
const ALT = { idle0_0: 'spirals', spirals: 'idle0_0', hearts: 'spirals', melt: 'idle0_0', jackpot: 'hearts' };
export function glance(prev, want) {
  const w = POSES.includes(want) ? want : 'idle0_0';
  return w === prev ? ALT[w] : w;
}
export const pressPose = () => 'spirals';
/** The landed face: the pot, a win, or sleepy for Snooze (the melt cell is the droopy one). */
export function landPose(r) {
  if (!r) return 'idle0_0';
  if (r.jackpotWon) return 'jackpot';
  if (r.reward?.kind === 'double') return 'spirals';
  if (r.reward?.kind === 'decoration') return 'hearts';
  if (r.snoozed) return 'melt';
  return r.pay > 0 ? 'hearts' : 'idle0_0';
}

/** THE BREATH, 0..1..0 over BREATH_MS, ease-in-out. */
export const breath = t => (1 - Math.cos((2 * Math.PI * t) / FEEL.BREATH_MS)) / 2;

/** THE SHIVER offset in px at `ms` into it (0 outside). */
export function shiverPx(ms) {
  if (!(ms >= 0) || ms >= FEEL.SHIVER_MS) return 0;
  return FEEL.SHIVER_PX * Math.sin((ms / FEEL.SHIVER_MS) * Math.PI * 6) * (1 - ms / FEEL.SHIVER_MS);
}

/**
 * THE REVEAL's count-up on the wheel screen, `q` 0..1 into it. The ease overshoots (y 1.35) for the motion; a number
 * must never read above the pay (it showed JACKPOT +551 for +550), so the count is clamped to 0..pay.
 */
export function revealCount(pay, q) {
  const p = Math.max(0, Math.round(Number(pay) || 0));
  if (!(q > 0)) return 0;
  if (q >= 1) return p;
  return Math.max(0, Math.min(p, Math.round(p * bezier(FEEL.REVEAL_EASE, q))));
}

/** A cubic-bezier(x1,y1,x2,y2) easing, solved for x. */
export function bezier([a, b, c, d], x) {
  const q = Math.min(1, Math.max(0, x));
  let lo = 0, hi = 1, t = q;
  for (let i = 0; i < 16; i++) { t = (lo + hi) / 2; const v = 3 * (1 - t) ** 2 * t * a + 3 * (1 - t) * t * t * c + t ** 3; if (v < q) lo = t; else hi = t; }
  return 3 * (1 - t) ** 2 * t * b + 3 * (1 - t) * t * t * d + t ** 3;
}
