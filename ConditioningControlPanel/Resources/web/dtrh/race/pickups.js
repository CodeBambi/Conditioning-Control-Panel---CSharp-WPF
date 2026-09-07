/* ============================================================================
 * race/pickups.js - the passive pickups. Implements CONTRACT.md "race/pickups.js".
 *
 * A pickup is a picture standing on the road (rooms.js draws it in the sugar
 * cube's old slots). You drive through it and it happens: no roll, no card, no
 * slot, no key, no toast. Every one is a bonus that makes you take MORE of the
 * road, never less, so the no-lose contract holds for the whole table.
 *
 * This module is the brain and nothing else: which spot lights, when, with
 * what, and how long each effect has left. It never imports three or touches
 * the DOM, so race/smoke/pickups-check.mjs drives all of it under node. What an
 * effect DOES lives in run.js (onPickup) off the events below, the way the
 * cube's items used to be handed over:
 *   { type:'pickupSpawn', id, d, x }      a spot lit: rooms.js stands the picture up
 *   { type:'pickupTake', id, p, refresh } the kart crossed it (refresh: the same one was live)
 *   { type:'pickupEnd', id }              a timed effect ran out (or its family replaced it)
 *   { type:'pickupDrop', id }             nobody took it: it went behind the kart
 *
 * THE GENTLE START (race/pace.js) applies here too: nothing lights inside a
 * track's first act or the first FIRST_SEC seconds of a seeded run, one pickup
 * is on the road at a time, and the next one waits GAP_SEC of driving.
 *
 * FAMILIES. Two chips can be live at once, one per family: a `bonus` (group
 * one) and a `sweep` (group two) overlap by design. Taking one whose family is
 * already live refreshes that bar when it is the same pickup and replaces the
 * effect when it is not; a third chip never stacks.
 *
 * Every tunable number is in TUNE or on its PICKUPS row. Nothing below is
 * written twice.
 * ==========================================================================*/

const SPRITE_BASE = '/dtrh/assets/items/';

/** The one table of knobs. Seconds are seconds of driving (a pause stops all of them). */
export const TUNE = Object.freeze({
  FIRST_SEC: 45,            // a seeded run lights nothing before this (a track: never in its first act)
  GAP_SEC: [30, 45],        // seconds of driving between one pickup and the next
  AHEAD_M: [55, 140],       // a spot lights when it is this far ahead of the kart
  TAKE_X: 1.2,              // the cube's own crossing test: |x - ks.x| <= TAKE_X
  POINTS: 10,               // a take pays like a plain treat and keeps the combo warm
  DROP_M: 8,                // this far behind the kart an untaken pickup goes away
});

/**
 * The pickups. `sec` is the effect's length, `family` the chip it shares, `pool` the spawn
 * weight curve (weightFor). The other fields are the effect's own numbers, read by run.js.
 */
export const PICKUPS = [
  // group one: the passive bonuses (family bonus)
  { id: 'poppers',      name: 'poppers',      family: 'bonus', pool: 'mid',   sec: 8,  scale: 1.8, reach: 1.35, sprite: SPRITE_BASE + 'poppers.png' },
  // group two: the pop-everything family (family sweep)
  { id: 'the_pump',     name: 'the pump',     family: 'sweep', pool: 'mid',   sec: 5,  sprite: SPRITE_BASE + 'the_pump.png' },
];
export const PICKUP_BY_ID = Object.fromEntries(PICKUPS.map((p) => [p.id, p]));

/** Pool weights by multiplier: x1 is generous with catch-up, x8 leans into risk / reward. */
export function weightFor(pickup, mult) {
  const t = Math.min(1, Math.max(0, ((Number(mult) || 1) - 1) / 7));   // 0 at x1, 1 at x8
  if (pickup.pool === 'catch') return 3.0 - 2.5 * t;
  if (pickup.pool === 'risk') return 0.6 + 2.6 * t;
  return 1.4;
}

/** Roll one pickup off the pool weights at this multiplier. `exclude` is a set of ids left out. */
export function rollPickup(mult, rand = Math.random, exclude = null) {
  const pool = exclude ? PICKUPS.filter((p) => !exclude.has(p.id)) : PICKUPS;
  if (!pool.length) return null;
  const weights = pool.map((p) => weightFor(p, mult));
  let r = rand() * weights.reduce((a, b) => a + b, 0);
  for (let i = 0; i < pool.length; i++) { r -= weights[i]; if (r <= 0) return pool[i]; }
  return pool[pool.length - 1];
}

const range = (rand, [a, b]) => a + (b - a) * rand();

/**
 * createPickups({ rng, spots, totalDepth }) -> the run's pickup brain.
 *   spots      the road's pickup slots, [{ d, x }] in depth order (spine.js `pickup` features)
 *   totalDepth the lap length, so a spot just past the start line is still "ahead"
 */
export function createPickups({ rng, spots = [], totalDepth = 1e9 } = {}) {
  const rand = typeof rng === 'function' ? rng : Math.random;
  const T = Math.max(1, Number(totalDepth) || 1);
  const relD = (d, from) => { let r = (d - from) % T; if (r > T / 2) r -= T; else if (r <= -T / 2) r += T; return r; };
  const listeners = [];
  const emit = (ev) => { for (const cb of listeners) { try { cb(ev); } catch (e) { /* a listener never breaks the run */ } } };
  const active = new Map();     // id -> { p, left }
  let live = null;              // the one on the road: { p, d, x }
  let gapLeft = 0, prevD = null;
  const rollGap = () => range(rand, TUNE.GAP_SEC);

  /** Stand a pickup up on a spot. Returns it, or null while one is already on the road. */
  function light(p, spot) {
    if (live || !p || !spot) return null;
    live = { p, d: spot.d, x: spot.x };
    emit({ type: 'pickupSpawn', id: p.id, d: live.d, x: live.x });
    return live;
  }
  /** The nearest spot inside the AHEAD_M window, or null when none is in it this frame. */
  function spotAhead(d) {
    let best = null, bestRel = Infinity;
    for (const s of spots) {
      const rel = relD(s.d, d);
      if (rel >= TUNE.AHEAD_M[0] && rel <= TUNE.AHEAD_M[1] && rel < bestRel) { best = s; bestRel = rel; }
    }
    return best;
  }
  function start(p) {
    const was = [...active.values()].find((a) => a.p.family === p.family);
    const refresh = !!was && was.p.id === p.id;
    if (was && !refresh) { active.delete(was.p.id); emit({ type: 'pickupEnd', id: was.p.id }); }
    active.set(p.id, { p, left: p.sec });
    emit({ type: 'pickupTake', id: p.id, p, refresh });
  }

  const api = {
    /**
     * One frame. `f` is the kart and the run this second:
     *   { d, x, speed, elapsed, opening, mult }
     * elapsed is seconds of driving, opening true while the gentle start holds (race/pace.js).
     */
    update(dt, f) {
      if (!(dt > 0) || !f) return;
      for (const [id, a] of active) {
        a.left -= dt;
        if (a.left > 0) continue;
        active.delete(id);
        emit({ type: 'pickupEnd', id });
      }
      const d = Number(f.d) || 0;
      if (live) {
        const rel = relD(live.d, d), was = prevD == null ? rel : relD(live.d, prevD);
        if (was >= 0 && rel < 0) {
          if (Math.abs(live.x - (Number(f.x) || 0)) <= TUNE.TAKE_X) api.take();
        } else if (rel < -TUNE.DROP_M) {
          const id = live.p.id; live = null; gapLeft = rollGap();
          emit({ type: 'pickupDrop', id });
        }
      } else if (!f.opening && (Number(f.elapsed) || 0) >= TUNE.FIRST_SEC) {
        gapLeft -= dt;
        if (gapLeft <= 0) {
          const spot = spotAhead(d);
          if (spot) { light(rollPickup(f.mult, rand), spot); gapLeft = rollGap(); }
        }
      }
      prevD = d;
    },
    /** The kart crossed the live pickup: it is taken, and its effect starts (or refreshes). */
    take() {
      if (!live) return null;
      const p = live.p; live = null; gapLeft = rollGap();
      start(p);
      return p;
    },
    /** The `?pickup=<id>` aid and the smokes: light this pickup on this spot, gentle start or not. */
    light(id, spot) { return light(PICKUP_BY_ID[id] || null, spot); },
    /** What the chips show: one per live effect, its bar the fraction of the effect still left. */
    chips() { return [...active.values()].map((a) => ({ id: a.p.id, sprite: a.p.sprite, name: a.p.name, frac: Math.max(0, a.left / a.p.sec) })); },
    reset() { active.clear(); live = null; gapLeft = 0; prevD = null; },
    onEvent(cb) { if (typeof cb === 'function') listeners.push(cb); return () => { const i = listeners.indexOf(cb); if (i >= 0) listeners.splice(i, 1); }; },
    byId(id) { return PICKUP_BY_ID[id] || null; },
    get live() { return live; },
    get active() { return active; },
    get spots() { return spots; },
  };
  return api;
}

// self-check: node race/smoke/pickups-check.mjs drives the table, the roll and a whole run of this.
