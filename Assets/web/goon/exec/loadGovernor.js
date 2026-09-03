/* ============================================================================
 * exec/loadGovernor.js — THE CROSS-EFFECT LOAD GOVERNOR (lite tier only).
 *
 * WHY THIS EXISTS (2026-08-05, second mobile pass). PR #129's tier cut every
 * renderer's own budget, and the iPhone still buried itself at one specific
 * moment: a FLASH BURST landing on top of a running spiral bed and drain veil.
 * Each renderer was inside its own budget; the SUM was not — ten animated
 * images decoding, a WebGL pane filling the screen at 30fps and a fullscreen
 * blend stack all repainting in the same frames. One thing at full quality
 * beats three things at 5fps, so while a burst is on screen the AMBIENT
 * renderers volunteer a further step down, and take it back the moment the
 * squall passes.
 *
 * THE SHAPE: a hold. The renderer that is about to spend the frames (the
 * flash burst today; anything payload-shaped tomorrow) declares "I am the loud
 * one for the next N ms" via governorHold(ms) and gets back a release. The
 * ambient renderers ask one boolean, governorBusy(), from inside loops they
 * already run — nobody subscribes to anything, nobody imports anybody's
 * renderer, and a governor that nobody consults costs nothing.
 *
 * SELF-RESTORING BY CONSTRUCTION, twice over, because a degradation that can
 * stick is worse than the lag it prevents:
 *   · every hold carries a DEADLINE (capped at GOVERNOR_HOLD_MAX_MS) and a
 *     hold past its deadline reads as idle even if its release was lost to a
 *     thrown callback — the worst possible leak costs ten seconds of coarser
 *     spiral, not a match of it;
 *   · the release is idempotent, so the settle/cancel double-call every
 *     payload path can produce never underflows the count.
 *
 * FULL TIER IS EXEMPT AT THE READ, NOT THE WRITE. governorBusy() answers
 * false unless the lite tier is in force RIGHT NOW (read lazily, same as every
 * other tier dial), so a desktop's burst writes a timestamp nobody looks at
 * and the full-tier picture stays byte-identical. Holding unconditionally
 * keeps the caller free of tier logic — and means a mid-burst toggle to lite
 * degrades the ambience immediately, which is exactly when it is wanted.
 *
 * Import-safe under node: no DOM at import, no timers at all — the deadline
 * is compared, never scheduled.
 * ==========================================================================*/

import { perfLite } from './perfTier.js';

/** The longest any single hold may claim. A burst is 3-8s by contract
 *  (exec/flashes.js renderPayload clamps it); anything asking for more is
 *  getting the cap, because "temporarily" is the entire deal here. */
export const GOVERNOR_HOLD_MAX_MS = 10000;

let holders = 0;     // live (un-released) holds
let deadline = 0;    // latest expiry among them, in nowMs() time

const nowMs = () => {
  try { if (typeof performance === 'object' && performance && typeof performance.now === 'function') return performance.now(); }
  catch (_e) { /* fall through */ }
  return Date.now();
};

/**
 * Declare a burst of load for the next `ms` (clamped to the cap).
 * @returns {() => void} release — idempotent; call it when the burst settles.
 */
export function governorHold(ms) {
  const dur = Math.max(0, Math.min(GOVERNOR_HOLD_MAX_MS, ms | 0));
  holders++;
  deadline = Math.max(deadline, nowMs() + dur);
  let released = false;
  return () => {
    if (released) return;
    released = true;
    holders = Math.max(0, holders - 1);
    if (holders === 0) deadline = 0;
  };
}

/**
 * Should an ambient renderer step down RIGHT NOW? True only when (a) at least
 * one hold is live, (b) its deadline has not passed, and (c) the lite tier is
 * in force — a full-tier machine renders everything at once on purpose.
 */
export function governorBusy() {
  if (holders === 0) return false;
  if (nowMs() >= deadline) {
    // A hold that outlived its own deadline is a leak (a release lost to a
    // throw); heal it here rather than letting it gate the spiral forever.
    holders = 0;
    deadline = 0;
    return false;
  }
  return perfLite();
}

export default governorBusy;
