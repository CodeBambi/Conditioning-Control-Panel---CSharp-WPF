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

/* ---------------------------------------------------------------------------------------------
 * THE DESKTOP RECIPE (owner ask, 2026-09-15). The kit's wheel.land.* moment stays the landing's
 * fullscreen (10.13.F); this table ADDS the section 4 ids the host already renders, one row a moment,
 * the way the slot's paylines map onto them. Pure: station.js fires exactly what fxPlan returns.
 *
 *   Law I:  grab and coast are the same for every press (a word, a plum wash); nothing before the
 *           server answers reads as an outcome. The landing rows fire on the frame the pointer settles.
 *   Gates:  a row's id fires only while at least one of its gates is on (the host skips the rest per
 *           primitive and never forces a toggle). A host that sends no gate reads as on.
 *   Calm:   reduced motion, Calm or Motion off strips the travel (the coast wash, the near-miss spiral,
 *           the storm's rain, the melt's drip); cues that are not motion (a word, a burst, a wash) stay.
 *           The page sends Normal args; the host halves (law 6, nobody halves twice).
 *   Brake:  a moment on cooldown fires nothing (a rim grabbed twice, a coast re-pressed), and the same
 *           landing extra shrinks to nothing after FX_REPEAT_CAP plays in one sit-down (Brake 3).
 *   Law VI: Back and suspend fire nothing more; what is already on the desktop settles or is cancelled
 *           by the host's station-close, never by the page.
 * ------------------------------------------------------------------------------------------ */

/** Which app toggle(s) stand behind an id (CONTRACT section 4 and 10.13.B). */
export const FX_GATE = Object.freeze({
  'fx.wash': ['flash'], 'fx.gif_from': ['flash'], 'fx.gif_burst': ['flash'], 'fx.gif_storm': ['flash'],
  'fx.sub_single': ['subliminal'], 'fx.sub_pair': ['subliminal', 'spiral'], 'fx.sub_cascade': ['subliminal', 'flash'],
  'fx.spiral_brief': ['spiral'], 'fx.spiral_full': ['spiral'], 'fx.loom_spiral': ['spiral'],
  'fx.melt': ['brainDrain'], 'fx.haze': ['brainDrain'],
  'fx.jackpot': ['spiral', 'flash', 'subliminal'],
});

/** Repeats of one landing extra per sit-down before it goes quiet (Brake 3: repetition shrinks the party). */
export const FX_REPEAT_CAP = 3;

/**
 * The rows. `fx` is the Normal list, `calm` the still list (absent: the same, `[]`: nothing).
 * A step carries `gifs` (dealt picture keys ride in symbols), `words` (dealt word keys) and fixed `args`.
 * `coolMs`: the least gap between two plays of the row. `capped`: counts toward FX_REPEAT_CAP.
 */
export const FX_MOMENTS = Object.freeze({
  grab:     { fx: [{ id: 'fx.sub_single', words: 1 }], coolMs: 6000 },
  coast:    { fx: [{ id: 'fx.wash', args: { color: '#9b6bff', strength: 0.35 } }], calm: [], coolMs: 2000 },
  nearMiss: { fx: [{ id: 'fx.spiral_brief' }], calm: [], capped: true },
  snooze:   { fx: [] },
  small:    { fx: [{ id: 'fx.sub_single', words: 1 }], capped: true },
  mid:      { fx: [{ id: 'fx.gif_burst', gifs: 2 }, { id: 'fx.sub_single', words: 1 }], capped: true },
  big:      { fx: [{ id: 'fx.gif_storm', gifs: 3 }, { id: 'fx.sub_pair', words: 2 }],
              calm: [{ id: 'fx.gif_burst', gifs: 2 }, { id: 'fx.sub_pair', words: 2 }], capped: true },
  jackpot:  { fx: [{ id: 'fx.jackpot', gifs: 3, words: 3 }] },
  double:   { fx: [{ id: 'fx.sub_pair', words: 2 }], capped: true },
  gift:     { fx: [{ id: 'fx.gif_burst', gifs: 2 }, { id: 'fx.wash', args: { color: '#ff5fa2', strength: 0.6 } }], capped: true },
  empty:    { fx: [{ id: 'fx.melt' }], calm: [], capped: true },
});

/** The landing row for a result: the reward kind first, then the pay tier (10.19 roster: 15 and 30 mid, 60 and 150 big). */
export function landMoment(r) {
  if (!r) return 'snooze';
  if (r.jackpotWon) return 'jackpot';
  const kind = r.reward?.kind;
  if (kind === 'nothing') return 'empty';
  if (kind === 'double') return 'double';
  if (kind === 'decoration' && !r.reward.fallback) return 'gift';
  if (r.snoozed || !(r.pay > 0)) return 'snooze';
  return ['snooze', 'small', 'mid', 'big', 'jackpot'][tierOf(r)];
}

/**
 * THE ALMOST on the wheel: the pointer rests one slice off the pot, on either side of the ring, and the pot
 * was not won. Reads the server's own landing, never moves it (10.16.F: no near-miss weighting). Fires after the
 * answer, on the landing frame, so it tells nothing early (Law I).
 */
export function nearMiss(layout, idx, r) {
  if (!Array.isArray(layout) || !(layout.length >= 3) || !(idx >= 0) || !r || r.jackpotWon) return false;
  const pot = layout.findIndex(s => s && s.id === 'jackpot');
  if (pot < 0 || pot === idx) return false;
  const n = layout.length;
  return idx === (pot + 1) % n || idx === (pot - 1 + n) % n;
}

/** FNV-1a 32 bit over a string: a stable pick per result, the same one on every frame. */
export function hash32(str) {
  let h = 0x811c9dc5;
  const s = String(str);
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193); }
  return h >>> 0;
}

/** `n` distinct keys out of `keys`, starting at a seeded offset (fewer when the deal is short). */
export function pickKeys(keys, n, seed) {
  const list = Array.isArray(keys) ? keys.filter(k => typeof k === 'string' && k) : [];
  if (!list.length || !(n > 0)) return [];
  const start = hash32(seed) % list.length;
  return Array.from({ length: Math.min(n | 0, list.length) }, (_, i) => list[(start + i) % list.length]);
}

/** May `id` fire under these gates? A host that reports no gates, or no such gate, reads as on. */
export function fxAllowed(id, gates) {
  const need = FX_GATE[id];
  if (!need) return false;
  const g = gates && typeof gates === 'object' ? gates : {};
  return need.some(k => g[k] !== false);
}

/** A fresh cooldown ledger for one sit-down. */
export const freshCool = () => ({ at: {}, seen: {} });

/**
 * What the desktop plays for `moment`, and the ledger after it.
 *   { fx: [{ id, symbols, args }], cool, why: null | 'unknown' | 'cool' | 'capped' | 'still' | 'gated' }
 * `why` says what emptied the list, for the feel log and the checks.
 */
export function fxPlan(moment, { still = false, gates = null, cool = null, now = 0, gifs = [], words = [], seed = '' } = {}) {
  const row = Object.prototype.hasOwnProperty.call(FX_MOMENTS, moment) ? FX_MOMENTS[moment] : null;
  const ledger = cool || freshCool();
  if (!row) return { fx: [], cool: ledger, why: 'unknown' };
  const seen = ledger.seen[moment] || 0, last = ledger.at[moment];
  if (row.coolMs > 0 && Number.isFinite(last) && now - last < row.coolMs) return { fx: [], cool: ledger, why: 'cool' };
  if (row.capped && seen >= FX_REPEAT_CAP) return { fx: [], cool: ledger, why: 'capped' };
  const steps = still && row.calm ? row.calm : row.fx;
  const fx = [];
  for (const step of steps) {
    if (!fxAllowed(step.id, gates)) continue;
    const symbols = [...pickKeys(gifs, step.gifs || 0, seed + '|' + step.id + '|g'), ...pickKeys(words, step.words || 0, seed + '|' + step.id + '|s')];
    fx.push({ id: step.id, symbols: symbols.length ? symbols : undefined, args: step.args ? { ...step.args } : undefined });
  }
  const next = { at: { ...ledger.at, [moment]: now }, seen: { ...ledger.seen, [moment]: seen + (row.capped ? 1 : 0) } };
  const why = fx.length ? null : steps.length ? 'gated' : still && row.calm ? 'still' : null;
  return { fx, cool: next, why };
}
