/* ============================================================================
 * fieldFeel.js - the pure maths of the field's feel pass (no DOM, node tested).
 *
 * chaosField.js and chaosHud.js read these; feel-field.css carries the curves.
 * Every number the owner may want to tune by eye lives here or in the
 * :root block of feel-field.css.
 * ==========================================================================*/

/* Idle breath: each bubble draws its own period and starts somewhere inside
 * it, so two copies of one sprite never rise and fall together. */
export const BREATH = { minS: 3.2, maxS: 5.2 };

/* Hard ceilings on short-lived fx nodes. A monster chain degrades to fewer
 * shards per pop, never to an unbounded DOM. */
export const SHARD_CAP = 96;       // .cf-spark nodes alive at once
export const RAIN_CAP = 60;        // .rh-emorain nodes alive at once

/* Reward flights (emotes and gold into the HUD). */
export const FLIGHT = {
  ms: 520,          // one glyph, pop to wallet
  staggerMs: 70,    // between glyphs of one pop
  maxPerPop: 3,     // glyphs per pop at full motion (1 under reduced)
  cap: 12,          // glyphs in the air at once; past it the value just updates
  arcPx: 46,        // how far the path bows sideways at full motion
  goldWindowMs: 300, // a gold bump this soon after a pop flies from that pop
  arriveGapMs: 90,  // least time between two arrival ticks sent to the audio lane
};

/* Detonation: the field drains and eases back. Matches the audio lane's
 * low-pass recovery (about 2.6 s). */
export const DIM = { ms: 2600, sat: 0.35, bright: 0.72 };

const clamp01 = (v) => Math.max(0, Math.min(1, v));

/** Period and negative start delay for one bubble's breath. r1, r2 in [0,1). */
export function breathPhase(r1, r2) {
  const durS = BREATH.minS + (BREATH.maxS - BREATH.minS) * clamp01(r1);
  return { durS, delayS: -durS * clamp01(r2) };
}

/** How many shards a burst may add: all of them while there is room, what is
 * left near the cap, none once the cap is reached. */
export function shardCount(wanted, liveNow, cap) {
  const want = Math.max(0, wanted | 0);
  const room = Math.max(0, (cap | 0) - Math.max(0, liveNow | 0));
  return Math.min(want, room);
}

/** Glyphs to fly for one pop. 0 = no flight, the value just updates. */
export function flightCount(earned, scale, inAir, cap = FLIGHT.cap) {
  if (!(earned > 0) || !(scale > 0)) return 0;
  const want = scale >= 1 ? Math.min(FLIGHT.maxPerPop, earned | 0) : 1;
  const room = Math.max(0, cap - Math.max(0, inAir | 0));
  return Math.max(0, Math.min(want, room));
}

/** Split an amount over n arrivals so the parts sum to the whole. */
export function splitAmount(amount, n) {
  const total = Math.max(0, amount | 0);
  if (n <= 0) return [];
  const base = Math.floor(total / n);
  const out = new Array(n).fill(base);
  out[n - 1] += total - base * n;
  return out;
}

/** Point on a flight at k in [0,1]: ease in-out along the line, with a
 * sideways bow that is zero at both ends. arc is in pixels (signed). */
export function flightPoint(sx, sy, tx, ty, k, arc = 0) {
  const u = clamp01(k);
  const e = u < 0.5 ? 4 * u * u * u : 1 - Math.pow(-2 * u + 2, 3) / 2;
  const dx = tx - sx, dy = ty - sy;
  const len = Math.hypot(dx, dy) || 1;
  const bow = Math.sin(Math.PI * e) * arc;
  return { x: sx + dx * e + (-dy / len) * bow, y: sy + dy * e + (dx / len) * bow, e };
}

/** Dim depth for the motion scale: full depth at 1, half the drop at 0.5 and
 * below (a dim is a fade, so it stays under motion off, at the gentle depth). */
export function dimLevels(scale) {
  const k = scale >= 1 ? 1 : 0.5;
  return {
    sat: +(1 - (1 - DIM.sat) * k).toFixed(3),
    bright: +(1 - (1 - DIM.bright) * k).toFixed(3),
    ms: DIM.ms,
  };
}
