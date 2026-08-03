// Retention stats — port of the mobile reel's state/reelStatsStore.ts.
// One record per segment: { plays, dwellMs, score, lastShownAt }.
//   plays       — confidence (drives the exploration bonus decay)
//   dwellMs     — cumulative CAPPED dwell (debug/average only)
//   score       — EWMA retention 0..1 (the value selection consumes)
//   lastShownAt — epoch ms; recency + LRU eviction key
// Persistence goes through the C# bridge (fyp_stats.json): trailing-throttled so a
// swipe storm costs one write, flushed on pagehide.

import {
  DWELL_CAP_FACTOR, RETENTION_TARGET, EWMA_ALPHA, SCORE_PRIOR, MAX_STAT_ENTRIES, assetIdOf,
} from './segments.js';

const WRITE_WINDOW_MS = 2000;

let stats = Object.create(null);
let saveTimer = null;
let postSave = null; // set by init(); posts {type:'stats-save', stats} to C#

export function init(persisted, postSaveFn) {
  postSave = postSaveFn;
  stats = Object.create(null);
  if (persisted && typeof persisted === 'object') {
    for (const [k, v] of Object.entries(persisted)) {
      if (v && typeof v.score === 'number') stats[k] = v;
    }
  }
}

export function segmentScore(id) {
  return stats[id]?.score ?? SCORE_PRIOR;
}

export function segmentPlays(id) {
  return stats[id]?.plays ?? 0;
}

/** Record one viewing. Cap and target share the same factor, so retention
 *  saturates at 1.0 exactly where the loop-camping cap bites. */
export function recordView(id, dwellMs, clipLenMs) {
  if (!(clipLenMs > 0) || !(dwellMs > 0)) return;
  const capped = Math.min(dwellMs, DWELL_CAP_FACTOR * clipLenMs);
  const r = Math.min(1, capped / (RETENTION_TARGET * clipLenMs));
  const prev = stats[id];
  const isNew = !prev;
  stats[id] = {
    plays: (prev?.plays ?? 0) + 1,
    dwellMs: (prev?.dwellMs ?? 0) + capped,
    score: prev ? (1 - EWMA_ALPHA) * prev.score + EWMA_ALPHA * r : r,
    lastShownAt: Date.now(),
  };
  // Soft cap: prune only when a NEW key pushes the map over the limit.
  if (isNew) {
    const keys = Object.keys(stats);
    if (keys.length > MAX_STAT_ENTRIES) {
      keys.sort((a, b) => (stats[a].lastShownAt ?? 0) - (stats[b].lastShownAt ?? 0));
      for (let i = 0; i < keys.length - MAX_STAT_ENTRIES; i++) delete stats[keys[i]];
    }
  }
  scheduleSave();
}

/** Drop stats for assets that left the manifest (mirror of pruneAssets). */
export function pruneToAssets(aliveAssetIds) {
  let changed = false;
  for (const k of Object.keys(stats)) {
    if (!aliveAssetIds.has(assetIdOf(k))) { delete stats[k]; changed = true; }
  }
  if (changed) scheduleSave();
}

function scheduleSave() {
  if (saveTimer != null) return;
  saveTimer = setTimeout(() => { saveTimer = null; flush(); }, WRITE_WINDOW_MS);
}

/** Immediate write-through (pagehide / manual). */
export function flush() {
  if (saveTimer != null) { clearTimeout(saveTimer); saveTimer = null; }
  try { postSave?.(stats); } catch { /* bridge gone — nothing to do */ }
}

export function statCount() {
  return Object.keys(stats).length;
}
