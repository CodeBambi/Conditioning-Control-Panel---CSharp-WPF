/* feel.js - THE HOUSE BOOK for the slot (lane F1). PURE: the numbers and the choices, no DOM, no three,
 * no audio, so node:test holds the Brake to its word. scene.js, bank.js, sound.js and station.js only
 * ever play what this file decides.
 *
 * The shared atoms (THE THUD, THE SHIVER, THE BANK's timings and its reversed token count) come from
 * arcademy/shell/counterfx.js so a thud is a thud in the Arcademy and in the Back Room. */

import { CFX, bankCount } from '../../../arcademy/shell/counterfx.js';
import { ANTICIPATION } from './pace.js';
import { HIGHLIGHT_MS, HIGHLIGHT_GAP_MS, FX_DELAY_MS, CALLOUT_MS } from '../../shared/hypno/callout.js';

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
  // C1 (10.16.D): an emi2 landing has not missed yet, so it takes neither THE SHIVER nor THE ALMOST. It is a
  // muted thud and nothing else - `hold`, a quiet party - and the re-spin that follows IS the event.
  if (isHold(o)) return { ...r, tier: 0, party: 'hold', sound: 'muted', tokens: false, heat: 0 };
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

/** THE CHIME LADDER across THE BANK's rollup (playbook A3): the ladder climbs while the readout counts, so a
 *  big win rises instead of ringing once. Step 0 is the landing cue sound.js has already played; the steps after
 *  it follow a semitone apart, spread over `ms`, never closer than the strobe floor (Brake 7, 6 Hz) and never
 *  more than LADDER_CAP. Tier 1 and a melted win get the landing note and nothing more (Law IX, Brake 5).
 *  Returns `[{ at, semis }]` in ms from the landing, both rising. */
const LADDER_STEPS = Object.freeze([0, 1, 3, 5, 7]);
export function ladderPlan(tier, ms, melted = false) {
  const t = Math.max(0, Math.min(4, Math.floor(Number(tier) || 0))), span = Math.max(0, Number(ms) || 0);
  const want = melted || t <= 1 ? 1 : Math.min(FEEL.LADDER_CAP, LADDER_STEPS[t]);
  const gap = Math.max(FEEL.STROBE_MIN_MS, span / want);
  const n = Math.max(1, Math.min(want, Math.floor(span / gap) || 1));
  return Array.from({ length: n }, (_, i) => ({ at: Math.round(i * gap), semis: i }));
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

/* THE BANK's proportional rollup (playbook A3). A casino scales the count-up to the win, and so does this:
 * the token count and stagger never move (3-7 tokens, 4 on Calm, 560 ms each, 70 ms apart, House Book), only
 * how long the READOUT keeps counting. The tokens tick it as they land, exactly as before; when the rollup is
 * longer than their flight the readout carries on from the last landing to the settled value over the rest,
 * and the mini-thud waits for the end. Law I: the value it lands on is the tape's, never this file's. */
export const ROLLUP_MS = Object.freeze([0, 500, 1200, 2000, 6000]);   // by tier: nothing, 500, 1.2 s, 2 s, the 6 s jackpot climb

/** How long the count-up runs. `x` is a tier (0-4), an outcome, or a raw pay read through tierOf's thresholds
 *  (only a tier or an outcome can name the jackpot: a bare 400 is a big line, not `emi3`). */
export function rollupMs(x) {
  if (x && typeof x === 'object') return ROLLUP_MS[tierOf(x)];
  const n = Math.max(0, Math.floor(Number(x) || 0));
  if (n <= 4) return ROLLUP_MS[n];
  return ROLLUP_MS[n >= 40 ? 3 : n >= 10 ? 2 : 1];
}

/** The tokens' whole flight, and when token `i` lands (both from the House Book's own two numbers). */
export const bankFlightMs = n => FEEL.BANK_FLY_MS + Math.max(0, (n | 0) - 1) * FEEL.BANK_STAGGER_MS;
export const bankLandMs = i => FEEL.BANK_FLY_MS + Math.max(0, i | 0) * FEEL.BANK_STAGGER_MS;

/** The count-up curve: a shallow ease-out, so a big roll sprints and then settles. `q >= 1` is exactly `to`. */
export function rollupAt(from, to, q) {
  const a = Math.round(Number(from) || 0), b = Math.round(Number(to) || 0), k = Math.min(1, Math.max(0, Number(q) || 0));
  return k >= 1 ? b : Math.round(a + (b - a) * (1 - (1 - k) ** 2));
}

/** The readout value at each token landing. Without a rollup longer than the flight this is the old even ladder,
 *  the last one exactly `to`; with one, each landing is that moment's count and the tail finishes the job. */
export function rollupTicks(from, to, n, ms) {
  const k = Math.max(1, n | 0), flight = bankFlightMs(k);
  if (!(Number(ms) > flight)) return tickValues(from, to, k);
  return Array.from({ length: k }, (_, i) => rollupAt(from, to, bankLandMs(i) / Number(ms)));
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

/* THE PAYLINE FRAME (playbook A6). After a win the winning row is framed for exactly the length of THE BANK's
 * rollup, then THE GLOW goes out over 480 ms. Law X: it shares the beat with the reveal, it does not add one. */
export const PAYLINE_PULSE_MIN_MS = 500;   // Brake 7: the pulse never runs faster than 2 Hz

/** How many pulses the frame takes over a rollup. 0 means a steady frame: reduced motion takes the STATE
 *  (Law VI) and a melted spin gets no ceremony (Brake 5). Tier 1 gets one soft pulse, never confetti (Law IX). */
export function paylinePulses(tier, { reduced = false, melted = false } = {}) {
  if (reduced || melted) return 0;
  const t = Math.max(0, Math.min(4, Math.floor(Number(tier) || 0)));
  if (t <= 1) return 1;
  return Math.max(1, Math.floor(rollupMs(t) / 600));
}

/** The frame's lit level 0..1 at `ms` into it: `pulses` eases in and out across `hold`, then THE GLOW fades.
 *  `pulses <= 0` holds it steady for the settled beat. It never goes dark before the fade (Brake 9). */
export function paylineGlow(ms, hold, pulses = 1) {
  const h = Math.max(0, Number(hold) || 0), at = Number(ms) || 0;
  if (at < 0 || at >= h + FEEL.GLOW_OUT_MS) return 0;
  const out = at > h ? 1 - (at - h) / FEEL.GLOW_OUT_MS : 1;
  if (!(pulses > 0)) return out;
  const period = Math.max(PAYLINE_PULSE_MIN_MS, h / pulses);
  return out * (0.55 + 0.45 * (1 - Math.cos((2 * Math.PI * Math.min(at, h)) / period)) / 2);
}

/** A cubic-bezier(x1,y1,x2,y2) easing, solved for x. */
export function bezier([a, b, c, d], x) {
  const q = Math.min(1, Math.max(0, x));
  let lo = 0, hi = 1, t = q;
  for (let i = 0; i < 16; i++) { t = (lo + hi) / 2; const v = 3 * (1 - t) ** 2 * t * a + 3 * (1 - t) * t * t * c + t ** 3; if (v < q) lo = t; else hi = t; }
  return 3 * (1 - t) ** 2 * t * b + 3 * (1 - t) * t * t * d + t ** 3;
}

/* ---------------------------------------------------------------------------------------------
 * The playbook, Tier A (CONTRACT 10.15). Neither of these decides anything: the tape carries the
 * outcome before a reel moves, so A1 is a delay over a known result and A2 only reads the strip the
 * server already drew. No stop is weighted, moved or re-drawn anywhere in this file.
 * ------------------------------------------------------------------------------------------ */

const symKind = id => {
  const m = /^(gif|sub|spiral)(\d+)$/.exec(id || '');
  return m ? m[1] : id === 'emi' || id === 'melt' ? id : 'unknown';
};
/** What reel `r` shows: the outcome's own symbols, else the cell of `strips[r]` it stopped on. */
function shows(o, strips, r) {
  if (o && Array.isArray(o.symbols) && o.symbols[r]) return o.symbols[r];
  const strip = strips && strips[r], k = o && Array.isArray(o.stops) ? o.stops[r] : -1;
  return Array.isArray(strip) && strip.length && k >= 0 ? strip[k % strip.length] || null : null;
}

/** The live pair on reels 1 and 2: 'gif' (the SAME gif id), 'spiral', 'sub', 'emi', else 'none'. */
export function livePair(o, strips) {
  const a = shows(o, strips, 0), b = shows(o, strips, 1);
  if (!a || !b) return 'none';
  const ka = symKind(a), kb = symKind(b);
  if (ka === 'gif' && kb === 'gif') return a === b ? 'gif' : 'none';
  if (ka !== kb) return 'none';
  return ka === 'spiral' || ka === 'sub' || ka === 'emi' ? ka : 'none';
}

/**
 * A1 THE ANTICIPATION REEL. A live pair on reels 1 and 2 keeps reel 3 spinning `holdMs` past its
 * normal stop (pace.ANTICIPATION), with a rising tone and the lights a notch down. `melted` is the
 * melt BEFORE the spin: Brake 5 halves the hold and never lets it go gold.
 * -> { kind: 'none'|'gif'|'spiral'|'sub'|'emi', holdMs, gold }
 */
export function anticipation(o, strips, { melted = false, held = null } = {}) {
  const none = { kind: 'none', holdMs: 0, gold: false };
  if (!o || held === 2) return none;                       // a frozen reel 3 never travels, so it never holds
  const kind = livePair(o, strips), hold = ANTICIPATION[kind] || 0;
  if (!hold) return none;
  return { kind, holdMs: melted ? Math.round(hold / 2) : hold, gold: kind === 'emi' && !melted };
}

/**
 * A2 THE ALMOST on the strip. A no-pay spin under a live pair, where the cell one above or one below
 * the payline on reel 3 would have completed the line. The cell comes from the server's own strip and
 * the stop it drew; the tell SHOWS that truth, it never manufactures it (no stop weighting, ever).
 * -> { reel: 2, dir: -1|1, cell, symbol, kind } | null (at most one per spin: the first direction that fits)
 */
export function almost(o, strips, { held = null } = {}) {
  if (!o || held === 2 || o.line !== 'none') return null;
  const kind = livePair(o, strips);
  if (kind === 'none') return null;
  const strip = strips && strips[2], k = o && Array.isArray(o.stops) ? o.stops[2] : -1;
  if (!Array.isArray(strip) || strip.length < 3 || !(k >= 0)) return null;
  const n = strip.length, wanted = kind === 'gif' ? shows(o, strips, 0) : null;
  const fits = id => (wanted ? id === wanted : symKind(id) === kind);
  for (const dir of [-1, 1]) {
    const cell = ((k + dir) % n + n) % n;                  // the strip is a ring: 0 and n-1 are neighbours
    if (fits(strip[cell])) return { reel: 2, dir, cell, symbol: strip[cell], kind };
  }
  return null;
}

/** A2's tell: gold in, then ONE snap back (House Book THE ALMOST, 620-1,400 ms, 120 ms snap). */
export const ALMOST = Object.freeze({ TELL_MS: 620, SNAP_MS: 120 });

/* ---- Playbook Tier A (backroom-casino-playbook.md section 2): A4 attract mode, A5 the EMI land-wiggle.
 * Both are presentation over an outcome the tape already holds: neither reads a stop, moves SP or changes
 * a result. Pure here so the Brake keeps its word in node:test. --------------------------------------- */

/** A4 ATTRACT, House Book deck II ("an autoplay ghost after ~25 s idle"). The reels drift, a chase sweeps,
 *  EMI winks, and nothing lands. Entering and leaving it is not a party (Brake 1). */
export const ATTRACT = Object.freeze({
  IDLE_MS: 25000,            // seated and untouched this long before the cabinet starts attracting
  CHASE_MS: 8000,            // one bulb chase this often...
  CHASE_PASS_MS: 1400,       // ...each sweep taking this long (one pulse a bulb, nowhere near the strobe floor)
  WINK_FIRST_MS: 4000,       // the first wink, so the drift is doing the talking first
  WINK_MS: 12000,            // and a wink this often after it
  DRIFT_CELLS_PER_S: 0.35,   // THE DRIFT: about a cell every 3 s, a continuous roll, never a spin
  SETTLE_MS: 420,            // leaving it: a quiet ease-out back onto the current stops (Law XI utility motion)
});

/** A5 THE EMI LAND-WIGGLE: two small oscillations that damp back to the stop, a fraction of a cell. */
export const WIGGLE = Object.freeze({ MS: 300, CELLS: 0.17, CYCLES: 2 });

/**
 * A4: may the cabinet attract right now? Seated, idle and quiet only; never while melted (Brake 5), never
 * on Calm, never under reduced motion (Law VI: the settled state, not a slower one), never while suspended.
 */
export function attractOk({ seated = false, phase = 'idle', busy = false, banking = false,
                            meltLeft = 0, calm = false, reduced = false, suspended = false } = {}) {
  if (!seated || calm || reduced || suspended) return false;
  if (busy || banking || phase !== 'idle') return false;
  return !((meltLeft || 0) > 0);
}

/** A4: the drift offset in cells at `ms` into the attract (a continuous roll, so it just keeps counting). */
export const attractCells = ms => (ms > 0 ? (ms / 1000) * ATTRACT.DRIFT_CELLS_PER_S : 0);

/**
 * A5: the reels showing EMI on this landing, left to right. Any reel counts, a losing spin included, so the
 * jackpot symbol stays present between jackpots. A 3-EMI line IS the REVEAL, so that spin wiggles nothing
 * (Brake 2: one hero per beat).
 */
export function emiLandings(o) {
  if (!o || o.line === 'emi3') return [];
  const syms = Array.isArray(o.symbols) ? o.symbols : [];
  const out = [];
  for (let i = 0; i < Math.min(3, syms.length); i++) if (syms[i] === 'emi') out.push(i);
  return out;
}

/** A5: the wiggle offset in cells at `ms` into it (0 outside), damping to nothing exactly on the stop. */
export function wiggleCells(ms) {
  if (!(ms >= 0) || ms >= WIGGLE.MS) return 0;
  const q = ms / WIGGLE.MS;
  return WIGGLE.CELLS * Math.sin(q * Math.PI * 2 * WIGGLE.CYCLES) * (1 - q);
}

/* ---------------------------------------------------------------------------------------------
 * The playbook, Tier B and C (CONTRACT 10.16): B1 the spiral jar, B3 the welcome-back comp, C1 the
 * EMI pair free re-spin. Same rule as Tier A: none of this decides anything. The jar count, the comp
 * and every re-spin symbol are settled on the server before the page sees them (Law I); these helpers
 * only say WHEN the tube ticks, WHICH beat plays and what the readouts read.
 * ------------------------------------------------------------------------------------------ */

/* ---- C1 the EMI pair free re-spin (10.16.D) ---- */

export const RESPIN_KIND = 'emi_respin';
/** The emi2 landing: reels 1 and 2 hold EMI and reel 3 is about to come back. It pays 0 by design. */
export const isHold = o => !!o && o.line === 'emi2';
/** The re-spin plays on its own, right after the emi2 that queued it: no second lever press (10.16.D). */
export const playsWithoutPress = kind => kind === RESPIN_KIND;
/** Which reels stay exactly where they are: the re-spin holds 1 and 2 and redraws reel 3 alone. */
export const respinKeep = kind => (kind === RESPIN_KIND ? [0, 1] : []);
/**
 * The re-spin's own hold: A1's EMI row at FULL length, gold, never halved, not even while melted. Brake 5's
 * halving in 10.15 applies to A1's anticipation, not to this beat, because this beat IS the event (10.16.D).
 * Calm and reduced motion drop the light change only, exactly as A1 does; the hold and the tone stay.
 */
export const respinHold = () => ({ kind: 'emi', holdMs: ANTICIPATION.emi, gold: true, respin: true });

/* ---- B1 the spiral jar (10.16.A) ---- */

/** The reels showing a spiral on this outcome, left to right (the order they thud in). */
export function spiralReels(o) {
  const syms = o && Array.isArray(o.symbols) ? o.symbols : [];
  const out = [];
  for (let i = 0; i < Math.min(3, syms.length); i++) if (symKind(syms[i]) === 'spiral') out.push(i);
  return out;
}

/**
 * THE SPIRAL JAR, per landed outcome. The tube ticks once per spiral SHOWN, on that reel's own THUD (Law X),
 * and only on an outcome that actually earned: a freeze and everything it expanded into are sealed from the
 * jar (10.16.A), which the tape says plainly by carrying the same `jarN` it came in with.
 *   -> { reels, values, full, from, to, size }
 * `values[i]` is the count the tube reads after reel `reels[i]` thuds; the last one is the tape's own `jarN`,
 * never this file's arithmetic (Law I).
 */
export function jarPlan(o, before = 0, size = 0) {
  const sz = Math.max(0, Math.floor(Number(size) || 0));
  const from = Math.max(0, Math.floor(Number(before) || 0));
  const raw = o && o.jarN;
  const to = Number.isFinite(Number(raw)) && raw !== null ? Math.max(0, Math.floor(Number(raw))) : null;
  const sealed = !!o && (o.kind === 'freeze' || (to !== null && to === from));
  const reels = sealed ? [] : spiralReels(o);
  const values = reels.map((_, i) => (sz > 0 ? (from + i + 1) % sz : from + i + 1));
  if (to !== null && values.length) values[values.length - 1] = to;
  return { reels, values, full: sz > 0 && reels.length > 0 && from + reels.length >= sz,
           from, to: values.length ? values[values.length - 1] : (to ?? from), size: sz };
}

/** The jar borrows spiral3's shape because it IS that event: 3 free spins, one tier 2 party (10.16.A). */
export const JAR_TIER = 2;
const jarOutcome = melted => ({ line: 'spiral3', pay: 10, meltLeft: melted ? 1 : 0, halved: !!melted });
/**
 * The party a full jar gets: tier 2 (two notes, a jolt, a chase and the screen) plus `fx.spiral_full`, and
 * THEN the free spins play. Brake 2, exactly as 10.16.A puts it: when the same outcome ALSO won a line, the
 * two merge into the higher party and THE JAR'S OWN NOTE IS DROPPED, so the landing keeps one note and the
 * jar keeps the bigger party when it is the bigger one. A win of tier 2 or better takes the whole beat and
 * the jar plays nothing at all. Calm: the fill only, no party (`fx.spiral_full` still fires at its Calm
 * recipe). Brake 5: melted takes the melt party like any other win.
 * -> a recipe (`sound: null` meaning no note of its own), or null for no party at all.
 */
export function jarParty({ melted = false, calm = false, winTier = 0, seen = 0 } = {}) {
  const won = Math.max(0, Math.floor(Number(winTier) || 0));
  if (calm || won >= JAR_TIER) return null;
  // No tokens: the jar pays free SPINS, not SP, and THE BANK only ever flies value that moved (Law XII).
  return { ...recipe(jarOutcome(melted), { seen, jackpots: 0 }), tokens: false, sound: won > 0 ? null : 'two' };
}

/* ---- B3 the welcome-back comp (10.16.C) ---- */

/** The id the server mints: `c_` + the grant day + the account hash, both base 36. */
export const COMP_ID = /^c_[A-Za-z0-9_-]{4,48}$/;
/** A stored comp, read defensively -> { id, spins } or null. */
export function compOffer(comp) {
  if (!comp || typeof comp !== 'object') return null;
  const id = String(comp.id || ''), spins = Math.floor(Number(comp.spins) || 0);
  return COMP_ID.test(id) && spins >= 1 ? { id, spins } : null;
}
/**
 * Is the comp still spendable? It is spent on the first tape BUY of the sit-down and never stacks, so a press
 * that only plays an outcome already on the tape leaves it standing (10.16.C says "the lever's FIRST press";
 * a press that buys nothing cannot buy a comp, and a refused buy must not eat it either).
 */
export const compAvailable = (comp, spent = false) => !spent && !!compOffer(comp);

/* ---------------------------------------------------------------------------------------------
 * THE FLOW on the frame the result SHOWS (shared/hypno/callout.js, owner direction 2026-09-15). The
 * timings are the shared contract's, never this file's: the thud at 0, the winning glyphs 0..HIGHLIGHT_MS
 * in reel order HIGHLIGHT_GAP_MS apart, the callout and the host fx TOGETHER at FX_DELAY_MS, the next
 * press no earlier than UNLOCK_MS on a paid line. Law I: every rule below reads a row the tape already
 * holds; nothing here decides, weights or re-draws an outcome.
 * ------------------------------------------------------------------------------------------ */

export const FLOW = Object.freeze({
  HIGHLIGHT_MS, HIGHLIGHT_GAP_MS, FX_DELAY_MS, CALLOUT_MS,
  UNLOCK_MS: 2000,                                    // a paid line: the next press unlocks no earlier than this
  UNLOCK_JACKPOT_MS: FX_DELAY_MS + CALLOUT_MS + 1400,  // the jackpot keeps its longer hold (the hero, then PARTY_MS[4])
  TEASE_TUNNEL: 0.4,                                  // A1's hold pulls the host tunnel to this, released on landing
  HAZE_IDLE_MS: 8000,                                 // the attract haze breathes after this much idle...
  HAZE_BREATH_MS: 6000,                               // ...one breath this long...
  HAZE_OPACITY: 0.12,                                 // ...never brighter than this
});

/** The callout names, keyed as the lexicon carries them (br_callout_*). One sub is NOT a callout: the other
 *  lane's word is that beat. emi2 is the chase (reels 1+2 lock on EMI), shown before reel 3's re-spin. */
export const CALLOUTS = Object.freeze({
  sub2:     { key: 'br_callout_echo',         fallback: 'Echo',         tier: 'small' },
  sub3:     { key: 'br_callout_chorus',       fallback: 'Chorus',       tier: 'big' },
  gif3:     { key: 'br_callout_picture_show', fallback: 'Picture Show', tier: 'small' },
  gif3same: { key: 'br_callout_storm',        fallback: 'Storm',        tier: 'big' },
  spiral2:  { key: 'br_callout_double_spin',  fallback: 'Double Spin',  tier: 'small' },
  spiral3:  { key: 'br_callout_sinking_down', fallback: 'Sinking Down', tier: 'big' },
  melt:     { key: 'br_callout_brain_melt',   fallback: 'Brain Melt',   tier: 'big' },
  emi3:     { key: 'br_callout_emi_jackpot',  fallback: 'Emi Jackpot',  tier: 'hero' },
  emi2:     { key: 'br_callout_emi_chase',    fallback: 'Emi Chase',    tier: 'big' },
  jar:      { key: 'br_callout_overflow',     fallback: 'Overflow',     tier: 'big' },
  respin:   { key: 'br_callout_respin',       fallback: 'Respin',       tier: 'small' },
});

/** The callout a landed row earns by its LINE, or null (`none`, a single sub, a sealed row). */
export const calloutFor = o => (o && CALLOUTS[o.line]) || null;

/** The reels whose glyph glows on this landing, left to right: every reel on a three-line, the two matching
 *  reels on a pair, reels 1+2 on the chase, the melt cell on a melt. A `none` row lights nothing. */
export function hitReels(o) {
  if (!o) return [];
  const syms = Array.isArray(o.symbols) ? o.symbols.slice(0, 3) : [];
  const idx = pred => syms.map((s, i) => (pred(s) ? i : -1)).filter(i => i >= 0);
  switch (o.line) {
    case 'emi3': case 'gif3same': case 'gif3': case 'sub3': case 'spiral3': return [0, 1, 2];
    case 'emi2': return [0, 1];
    case 'sub2': return idx(s => symKind(s) === 'sub');
    case 'spiral2': return idx(s => symKind(s) === 'spiral');
    case 'melt': return idx(s => s === 'melt');
    default: return [];
  }
}

/** The highlight plan: `[{ reel, at }]`, reel order, HIGHLIGHT_GAP_MS apart, all inside HIGHLIGHT_MS. */
export const highlightPlan = o => hitReels(o).map((reel, i) => ({ reel, at: i * HIGHLIGHT_GAP_MS }));

/** One glyph's glow 0..1 at `ms` after the landing, for a hit that starts at `at`: in fast, then out so every
 *  glyph is dark again by HIGHLIGHT_MS (the callout's frame). Reduced motion takes the lit state, flat. */
export function glyphGlow(ms, at = 0, reduced = false) {
  const t = Number(ms) || 0, a = Math.max(0, Number(at) || 0);
  if (t < a || t >= HIGHLIGHT_MS) return 0;
  if (reduced) return 1;
  const span = Math.max(1, HIGHLIGHT_MS - a), q = (t - a) / span, rise = Math.min(0.3, 80 / span);
  return q < rise ? q / rise : 1 - (q - rise) / (1 - rise);
}

/** The GIF tease (owner: a couple of points on the GIF visuals, the economy untouched): a row reading `none`
 *  that shows EXACTLY two GIF symbols fires one host GIF flash (`fx.gif_burst`, args.count 1) at FX_DELAY_MS.
 *  No callout, no SP, no pay: the tape's row is what it was. */
export function teaseGif(o) {
  if (!o || o.line !== 'none') return false;
  const syms = Array.isArray(o.symbols) ? o.symbols.slice(0, 3) : [];
  return syms.filter(s => symKind(s) === 'gif').length === 2;
}
export const GIF_TEASE_FX = 'fx.gif_burst';

/** When the next press unlocks, in ms from the landing: the jackpot's longer hold, UNLOCK_MS on a paid line
 *  and on any row with a callout (the melt's word needs its frame), 0 on a loss (as quick as today). The
 *  chase (emi2) keeps its own sequencing: the re-spin follows on the pace's beat. */
export function unlockMs(o) {
  if (!o) return 0;
  if (o.line === 'emi3') return FLOW.UNLOCK_JACKPOT_MS;
  if (o.line === 'emi2') return 0;
  return o.pay > 0 || calloutFor(o) ? FLOW.UNLOCK_MS : 0;
}

/**
 * The whole flow for one landed row -> { hits, callouts: [{ at, ...callout }], fx: [{ at, id, args? }],
 * unlockMs }. `respinRow` is a row the spiral2 respin granted (tape kind `respin`): its "Respin" word shows
 * on the landing frame and its own result callout, if any, replaces it at FX_DELAY_MS (the shared contract:
 * a show while one is up replaces it). `jarWord`: the jar's Overflow (big) already holds the frame, so a small
 * word yields to it (Brake 2, one beat) and a big one still takes over. The host fx ids are the row's own, moved
 * to FX_DELAY_MS so they fire with the callout; the tunnel and the jar keep their own timing (station.js).
 */
export function flowPlan(o, { respinRow = false, jarWord = false } = {}) {
  if (!o) return { hits: [], callouts: [], fx: [], unlockMs: 0 };
  const own = calloutFor(o), callouts = [], keep = c => !!c && !(jarWord && c.tier === 'small');
  if (respinRow && keep(CALLOUTS.respin)) callouts.push({ at: own ? 0 : FX_DELAY_MS, ...CALLOUTS.respin });
  if (keep(own)) callouts.push({ at: FX_DELAY_MS, ...own });
  const fx = (Array.isArray(o.fx) ? o.fx : []).map(id => ({ at: FX_DELAY_MS, id }));
  if (teaseGif(o)) fx.push({ at: FX_DELAY_MS, id: GIF_TEASE_FX, args: { count: 1 } });
  return { hits: highlightPlan(o), callouts, fx, unlockMs: unlockMs(o) };
}

/** The attract haze 0..1 at `ms` into it: one slow breath (HAZE_BREATH_MS), never above HAZE_OPACITY. */
export const hazeAt = ms => (ms > 0 ? FLOW.HAZE_OPACITY * (1 - Math.cos((2 * Math.PI * ms) / FLOW.HAZE_BREATH_MS)) / 2 : 0);
