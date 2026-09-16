/* ============================================================================
 * flick.js - the throw. A finger (or a mouse) grabs the rotor and swings it:
 * this module turns that angular drag into ONE number, the rotor's starting
 * angular velocity, and nothing else. It is pure: no DOM, no three.js, no
 * clock of its own. bowl.js and bowl-3d.js pick the pointer's angle around the
 * wheel in their own view's convention (an angle that GROWS the way FEEL's
 * rotor angle grows), station.js feeds it here, and the answer goes to
 * feel.planRun as `rotVel0`.
 *
 * Law I, kept: the throw changes the picture only. The pocket is still the
 * server's - planRun simulates forward from `rotVel0` and then turns the whole
 * ball path by a whole number of pockets onto the answer, so a hard flick and
 * a soft one land in exactly the same place. The strength is clamped into
 * VEL_MIN..VEL_MAX (either sign) so a wild swipe cannot run the spin past the
 * page's budget, and a stale grab (a finger that stopped before it lifted) is
 * a nudge, not a throw.
 * ==========================================================================*/

export const FLICK = Object.freeze({
  MIN_TRAVEL: 0.16,      // rad swept before a drag is a throw at all (about 9 degrees)
  MIN_OMEGA: 0.0012,     // rad/ms at release, under this the wheel was only pushed around
  STALE_MS: 140,         // the grab went still this long before the lift: no throw
  EASE: 0.4,             // how hard each sample pulls the running speed (the wheel station's feel)
  INTENT_CAP: 0.12, INTENT_MIN: 0.025,   // the direction only turns over once the swing commits
  SOFT: 0.0015, HARD: 0.012,             // rad/ms mapped across VEL_MIN..VEL_MAX
  VEL_MIN: 1.2, VEL_MAX: 8.0,            // rad/s: a hard throw is visibly faster than the button's 1.5 kick
});

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

/** The short way round from `b` to `a`, in radians. */
export const wrapDelta = (a, b) => Math.atan2(Math.sin(a - b), Math.cos(a - b));

/** A grab starts at pointer angle `angle` (rad) at clock `now` (ms). */
export function flickStart(angle, now) {
  return { angle: Number(angle) || 0, at: Number(now) || 0, travel: 0, omega: 0, intent: 0, sign: 1, d: 0 };
}

/** One pointer sample. Returns a NEW grab; `d` is this step's turn, for the wheel to follow the finger. */
export function flickMove(grab, angle, now) {
  const a = Number(angle) || 0, t = Number(now) || 0;
  const d = wrapDelta(a, grab.angle), dt = Math.max(1, t - grab.at);
  const omega = grab.omega + (d / dt - grab.omega) * FLICK.EASE;
  const intent = clamp(grab.intent + d, -FLICK.INTENT_CAP, FLICK.INTENT_CAP);
  const sign = Math.abs(intent) > FLICK.INTENT_MIN ? Math.sign(intent) : grab.sign;
  return { angle: a, at: t, travel: grab.travel + Math.abs(d), omega, intent, sign: sign || 1, d };
}

/** The strength of |omega| (rad/ms) as a rotor speed in rad/s, clamped into the band. */
export function flickSpeed(omega) {
  const q = clamp((Math.abs(Number(omega) || 0) - FLICK.SOFT) / (FLICK.HARD - FLICK.SOFT), 0, 1);
  return FLICK.VEL_MIN + (FLICK.VEL_MAX - FLICK.VEL_MIN) * q;
}

/**
 * The lift. `ok` is true only for a real throw; `rotVel` is then the signed rotor speed for planRun.
 * @returns {{ ok: boolean, why: string|null, sign: number, rotVel: number|null, travel: number, omega: number }}
 */
export function flickRelease(grab, now) {
  const stale = (Number(now) || 0) - grab.at > FLICK.STALE_MS;
  const omega = stale ? 0 : Math.abs(grab.omega);
  const out = { ok: false, why: null, sign: grab.sign || 1, rotVel: null, travel: grab.travel, omega };
  if (grab.travel < FLICK.MIN_TRAVEL) { out.why = 'short'; return out; }
  if (omega < FLICK.MIN_OMEGA) { out.why = stale ? 'stale' : 'slow'; return out; }
  out.ok = true; out.rotVel = out.sign * flickSpeed(omega);
  return out;
}
