/* ============================================================================
 * reveals.js - the reveal framework, ported from ChaosRevealService.cs. The
 * Warren starts naked: every gateable surface stays HIDDEN until its predicate
 * flips, then it enters the persisted pending set (over the bridge), flashes
 * once on the next hub open, and settles to seen.
 *
 * Settings clamp rule (unchanged): gates only flip visibility/visual selection,
 * NEVER a saved setting - and C# FromSettings re-clamps at deal time anyway.
 *
 * Persistence rides the generic set ops the bridge already exposes:
 *   pending: add-to-set pendingReveals · seen: remove-from-set pendingReveals
 *   + add-to-set seenReveals (the bridge whitelists exactly that removal).
 * ==========================================================================*/

import { RANK } from './catalog.js';

/** id -> predicate over a metaView. Missing id = always unlocked (C# parity). */
export const REVEAL_PREDS = {
  dollhouse:            (v) => v.runs >= 1,
  tab_looking_glass:    (v) => v.rankIndex >= RANK.Slipping,
  // grab-in-the-tube rework: toys/accessories are discovered in the fall, not pocket-gated.
  // The collection shelves open from the first descent so you can see what's discoverable.
  toybox_toys:          (v) => v.runs >= 1,
  toybox_accessories:   (v) => v.runs >= 1,
  // gold cutover: pockets (toybox_her_corner, bench_*_pocket_2) retired -
  // ids pruned here AND from ChaosRevealService.cs + the v3 save migration.
  pill_teasing:         (v) => v.rankIndex >= RANK.Tempted,
  pill_relentless:      (v) => v.rankIndex >= RANK.Entranced,
  pill_inescapable:     (v) => v.extremeUnlocked,
  draft_skip:           (v) => v.runs >= 2,
  start_picker:         (v) => v.bench.has('start_mantra'),
  diary:                (v) => v.bench.has('diary'),
  stats_panel:          (v) => v.bench.has('stats_panel'),
  // 2026-07: the giants are open from the first run (the ids stay so persisted
  // pending/seen sets keep parsing) - depth-gating moved in-run: the spawner
  // only deals video/htlink in chambers III-IV (chaosRun.js).
  variant_video:        () => true,
  variant_htlink:       () => true,
  capstones:            (v) => v.rankIndex >= RANK.Devoted,
  extreme_tier_buyable: (v) => v.rankIndex >= RANK.Devoted,
  // crafting (THE BOUDOIR): the fourth station sews itself in on the fifth descent.
  // Web-only id - the native RevealService treats unknown ids as unlocked, and
  // pending/seen persistence rides the generic set ops like everything above.
  boudoir:              (v) => v.runs >= 5,
};

export function isUnlocked(id, view) {
  const pred = REVEAL_PREDS[id];
  if (!pred) return true;
  try { return !!pred(view); } catch (e) { return false; }
}

/**
 * Detect fresh unlocks (predicate true, never flashed, not already pending)
 * and queue them for the next hub-open flash. Mutates the view's local sets
 * for an immediate read and persists each over the bridge. Returns the ids
 * newly queued (callers usually just re-run the flash pass instead).
 */
export function sync(view, bridge, reason) {
  const fresh = [];
  for (const id of Object.keys(REVEAL_PREDS)) {
    if (!isUnlocked(id, view)) continue;
    if (view.seenReveals.has(id) || view.pending.has(id)) continue;
    view.pending.add(id);
    fresh.push(id);
    bridge.send({ type: 'meta-command', op: 'add-to-set', set: 'pendingReveals', id });
    bridge.log(`reveal pending: ${id} (${reason})`);
  }
  return fresh;
}

/** After an element's flash played: pending -> seen, persisted. */
export function markSeen(id, view, bridge) {
  const had = view.pending.delete(id);
  const added = !view.seenReveals.has(id);
  view.seenReveals.add(id);
  if (had) bridge.send({ type: 'meta-command', op: 'remove-from-set', set: 'pendingReveals', id });
  if (added) bridge.send({ type: 'meta-command', op: 'add-to-set', set: 'seenReveals', id });
}
