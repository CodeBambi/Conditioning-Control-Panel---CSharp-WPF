/* ============================================================================
 * backroom/room/win-echo.js - THE ROOM HEARD THAT (lane BR2-room, CONTRACT 10.22.A/B).
 *
 * The close-up party belongs to the station: the bank, the ladder, the payline frame, the reveal.
 * This is the OTHER half of a win - what the floor gets. A fixture that just paid keeps its aura hot
 * and its own screen on the line for a hold, so a win you took is still settling when you stand up
 * and turn around, and a hero puts its line on the Prize Parlour marquee, which is the one board the
 * whole floor can read.
 *
 * WHAT DECIDES: shared/win/plan.js and nothing here. `ctx.revealedWin(amount, tier, text)` already
 * arrives carrying the station's `plan.shower`, and the room asks plan.js AGAIN with its own motion
 * state and its own Brake 3 ledger. That is not a second opinion, it is a second SCOPE: a station's
 * ledger wears its party down over one sit-down, and the room's wears the floor's echo down over the
 * whole visit, which is the longer clock and the one a player walking a loop of the room feels.
 *
 *   Law IX    a small win never crosses the room. `loud.shower === 0` and the floor hears nothing:
 *             tier 1 is a close-up chime and that is the whole of it.
 *   Law VI    reduced motion takes the state - no aura travel, no coins, and the line still reads.
 *   Brake 2   two pays landing on one fixture inside one beat MERGE into the higher (mergePlans).
 *             They never stack and they never queue.
 *   Brake 3   the tenth big win of a visit is a smaller echo than the first, and from the fortieth
 *             the floor stops turning its head.
 *   Brake 5   melted is the station's own state, so the station simply never announces: a melted win
 *             arrives here as `shower: 0` and gets no echo, which is the same answer from both ends.
 *   Brake 9   THE LINE IS TEXT, and it survives Calm, reduced motion and motion level 0. The aura and
 *             the coins are the decoration on top of it, and they are the only part quiet takes.
 *   Law XIII  every rung names an EMI gesture, so the dealer looks up when the table pays.
 *
 * TRAP, and it is the good kind: the room's clock STOPS while a station holds the screen (scene.js
 * cancels its rAF in hold()). An echo raised from a seated win therefore does not start ageing until
 * the player is back on their feet - the walk-back is free, and it is why this module is driven by an
 * accumulated room clock rather than performance.now().
 *
 * PURE: no three, no DOM, no timers. room/tests/win-echo.test.mjs holds it.
 * ==========================================================================*/

import { normTier } from '../shared/win/tier.js';
import { winPlan, sitPlan, afterParty, freshSit, mergePlans } from '../shared/win/plan.js';

export const ECHO = Object.freeze({
  /** How long the floor carries a win after the station's own party is over, on top of plan.partyMs. */
  TAIL_MS: 1800,
  /** In fast, out slow - THE GLOW's own shape, at room scale. Neither is a strobe (Brake 9's floor). */
  RISE_MS: 160,
  FADE_MS: 900,
  /** What the aura gains over its chase, by the rung actually spent. Tiers 0 and 1 never cross the room. */
  GAIN: Object.freeze([0, 0, 0.5, 0.8, 1.1]),
  /** The winning fixture's own mascot looks up (Law XIII). Room gestures, not close-up face poses. */
  EMI: Object.freeze([null, null, 'look', 'present', 'wave']),
  /** A line is a label, not an essay: the fixture screens paint at 1024x128. */
  LINE_MAX: 48,
  /** The Parlour marquee joins a fixture's name to its line with the room's own mark. */
  JOIN: '  ✦  ',
});

const clean = (s) => String(s == null ? '' : s).replace(/\s+/g, ' ').trim().slice(0, ECHO.LINE_MAX);

/** The hold envelope: in over RISE, out over FADE, 1 in between. 0 before it starts and after it ends. */
export function envelope(age, ms) {
  if (!(ms > 0) || !Number.isFinite(age) || age < 0 || age >= ms) return 0;
  const rise = Math.min(ECHO.RISE_MS, ms / 2), fade = Math.min(ECHO.FADE_MS, ms / 2);
  return Math.max(0, Math.min(1, Math.min(age / rise, (ms - age) / fade)));
}

/**
 * One echo desk for one room visit. `now` is the room's own accumulated ms (see the trap above), and
 * every read takes it rather than keeping a clock of its own.
 */
export function createWinEcho() {
  let sit = freshSit();
  const live = new Map();       // rowKey -> { at, ms, plan, line, name, aura }
  const out = new Map();        // gains(), reused frame to frame: see the note on it
  let hero = null;              // the one line on the Parlour marquee, { at, ms, text }
  let raised = 0, hushed = 0;

  /**
   * A station announced a paid result. Returns what the floor should spend, or null when the floor
   * hears nothing (nothing was won, or it was a close-up-sized win).
   *   { plan, shower, line, aura, marquee, emi, ms } - the caller spends it and never re-decides it.
   */
  function celebrate(win, ctx, now) {
    const w = win || {};
    const amount = Number(w.amount);
    if (!Number.isFinite(amount) || amount <= 0) return null;   // Law I: no pay, no picture
    const asked = normTier(w.tier);
    const c = ctx || {};
    // What this rung would be worth to the floor with motion out of the question. It is the ONLY test
    // for whether the room hears it at all, so Calm loses the decoration and keeps the news (Brake 9).
    const loud = winPlan(asked, { lite: !!c.lite });
    if (!(loud.shower > 0)) { hushed++; return null; }

    let plan = sitPlan(asked, sit, { still: !!c.still, reduced: !!c.reduced, lite: !!c.lite });
    const running = live.get(w.key);
    // Brake 2: a pay landing inside a running echo folds into the higher one. The beat keeps its own
    // start, so the floor does not get a second rise out of one moment.
    if (running && now - running.at < running.ms) plan = mergePlans(running.plan, plan);
    sit = afterParty(sit, plan);
    raised++;

    const ms = plan.partyMs + ECHO.TAIL_MS;
    const line = clean(w.text);
    const aura = plan.shower > 0 ? (ECHO.GAIN[plan.spent] || 0) : 0;
    const row = { at: now, ms, plan, line, name: clean(w.name), aura };
    live.set(w.key, row);
    // THE REVEAL is the only thing that leaves the fixture. plan.reveal is already once a visit.
    const marquee = plan.reveal && line ? (row.name ? row.name + ECHO.JOIN + line : line) : null;
    if (marquee) hero = { at: now, ms, text: marquee };
    return Object.freeze({ key: w.key, plan, shower: plan.shower, line, aura, marquee, ms,
      emi: ECHO.EMI[plan.spent] || null });
  }

  const alive = (row, now) => !!row && now - row.at < row.ms;

  return {
    celebrate,
    /** The aura's boost for one fixture, 0..1+. Quiet takes it whole: decoration is what Calm strips. */
    gain(key, now, still) {
      const row = live.get(key);
      if (still || !alive(row, now) || !(row.aura > 0)) return 0;
      return envelope(now - row.at, row.ms) * row.aura;
    },
    /** Every fixture with a live aura this frame, so the bulb loop asks once and not once per bulb.
     *  ONE map, reused: this is called on every frame of the render loop and must not allocate. */
    gains(now, still) {
      out.clear();
      if (still) return out;
      for (const [key, row] of live) {
        if (!alive(row, now) || !(row.aura > 0)) continue;
        const g = envelope(now - row.at, row.ms) * row.aura;
        if (g > 0) out.set(key, g);
      }
      return out;
    },
    /** The fixture's own screen while the echo holds. Text, so it outlives every motion setting. */
    line(key, now) { const row = live.get(key); return alive(row, now) && row.line ? row.line : null; },
    /** The Prize Parlour marquee's line, or null for its own name. */
    marquee(now) { return alive(hero, now) ? hero.text : null; },
    /** Law VI: a station closing, a room leaving. What was falling is cleared, never fast-forwarded. */
    clear(key) { out.clear(); if (key == null) { live.clear(); hero = null; } else live.delete(key); },
    debug: () => ({ raised, hushed, heroes: sit.heroes, seen: [...sit.seen],
      live: Object.fromEntries([...live].map(([k, r]) => [k, { spent: r.plan.spent, ms: r.ms, line: r.line }])) }),
  };
}
