/* ============================================================================
 * backroom/room/prop-landing.js - A DECORATION ARRIVING (lane BR2-room, CONTRACT 10.22.D).
 *
 * The six Room Service props are the room's only NON-NUMERIC reward: you win a monstera at the wheel
 * and then, at the vending cabinet, you place it. Today `customization-props.js` places it with
 * `group.visible = on` - the one reward in the Back Room that arrives with no move at all, which is
 * Law XII broken in the only currency the room cannot count.
 *
 * So it lands. A short drop onto its spot, the furniture cut of THE THUD under it (a squash, not the
 * badge-sized stamp counterfx fires on a Yours mark), and THE GLOW's warm cut over the whole prop for
 * its 480 ms. Nothing else: a plant is not a jackpot.
 *
 *   Law VI    reduced motion takes the STATE. `travelMs` and `glowMs` both go to 0 and the prop is
 *             simply there, which is what the player asked for.
 *   Law IX    sized by shared/win/plan.js like everything else in this pass. A placement asks for
 *             TIER.GOOD and spends whatever the plan gives back.
 *   Brake 3   the ledger is the caller's, one per room visit, so toggling the same plant on and off
 *             all afternoon wears the landing down to a chime and then to a thud.
 *   Law X     one gesture, one beat: the drop, the squash and the glow are one move on one frame,
 *             340 ms of travel under the 620 ms ceiling.
 *
 * PURE: no three, no DOM, no timers - the caller samples it with its own accumulated seconds.
 * room/tests/prop-landing.test.mjs holds it.
 * ==========================================================================*/

import { TIER } from '../shared/win/tier.js';
import { sitPlan } from '../shared/win/plan.js';

export const LAND = Object.freeze({
  /** THE THUD's own budget (counterfx CFX.THUD_MS), which is also the whole travel. */
  MS: 340,
  /** How far above its spot a prop starts, in metres. A hand setting something down, not a drop pod. */
  DROP: 0.07,
  /** The furniture cut of the stamp: a squash on landing, never a prop at twice its size. */
  SQUASH: 0.06,
  /** Where in the travel the prop is down and the squash begins. */
  TOUCH: 0.55,
  /** THE GLOW, warm cut, in fast and out slow - counterfx's cfx-warm keyframes, 0 -> .22 -> 1. */
  GLOW_IN: 0.22,
});

/** The rung a placement asks for. A won decoration finally going up is a good win, never a hero. */
export const PLACE_TIER = TIER.GOOD;

/** The plan for one placement, off the room's own Brake 3 ledger. ctx is { still, reduced, lite }. */
export function placePlan(sit, ctx) {
  return sitPlan(PLACE_TIER, sit, ctx || {});
}

/**
 * What that plan buys, in metres and milliseconds.
 *   reduced   nothing at all: the state, no travel and no light (plan.glow and plan.partyMs are both 0).
 *   still     the glow alone. Calm strips the move and keeps the warm cut - a light is not travel.
 *   otherwise the drop, the squash and the glow, the drop sized by the rung the plan actually spent.
 */
export function placeFeel(plan, ctx) {
  const p = plan || {};
  const c = ctx || {};
  const glowMs = p.glow > 0 ? p.glow : 0;
  const quiet = !!c.still || !!c.reduced || !(p.partyMs > 0);
  const travelMs = quiet ? 0 : LAND.MS;
  const drop = travelMs ? LAND.DROP * (p.spent >= TIER.GOOD ? 1 : 0.6) : 0;
  return Object.freeze({ travelMs, glowMs, drop, ms: Math.max(travelMs, glowMs),
    why: p.why || (quiet ? 'still' : null) });
}

/**
 * The landing at `age` ms. `lift` is metres ABOVE the prop's own spot (never below it: a plant that
 * sinks through the carpet is a bug, not a thud), `scale` multiplies y with xz taking the opposite,
 * and `glow` is 0..1 of the warm cut for the caller to put on an emissive.
 */
export function sampleLanding(age, feel) {
  const f = feel || { travelMs: 0, glowMs: 0, drop: 0, ms: 0 };
  const t = Number.isFinite(age) && age > 0 ? age : 0;
  let lift = 0, squash = 0;
  if (f.travelMs > 0 && t < f.travelMs) {
    const k = t / f.travelMs;
    if (k < LAND.TOUCH) { const u = 1 - k / LAND.TOUCH; lift = f.drop * u * u; }
    else squash = Math.sin(Math.PI * ((k - LAND.TOUCH) / (1 - LAND.TOUCH)));
  }
  const glow = f.glowMs > 0 && t < f.glowMs
    ? (t < f.glowMs * LAND.GLOW_IN ? t / (f.glowMs * LAND.GLOW_IN)
      : (f.glowMs - t) / (f.glowMs * (1 - LAND.GLOW_IN)))
    : 0;
  return {
    lift,
    scaleY: 1 - LAND.SQUASH * squash,
    scaleXZ: 1 + LAND.SQUASH * 0.7 * squash,
    glow: Math.max(0, Math.min(1, glow)),
    done: !(f.ms > 0) || t >= f.ms,
  };
}
