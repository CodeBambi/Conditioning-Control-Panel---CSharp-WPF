// Segment grid + weighting constants — 1:1 port of the mobile reel's lib/reelSegments.ts.
// Contract: the *played* window must equal the *scored* segId, or retention accrues
// against sections that were never actually on screen.

/** Grid stride: one addressable segment starts every 30s of a video's timeline. */
export const SEGMENT_STRIDE_SEC = 30;
/** Per-cut random window length range (continuous, not integral). */
export const CLIP_MIN_SEC = 20;
export const CLIP_MAX_SEC = 40;
/** Videos at most this long play whole (single segment, no grid). */
export const WHOLE_VIDEO_MAX_SEC = CLIP_MAX_SEC + 10;

/** Dwell is capped at this multiple of the clip length (loop-camping guard). */
export const DWELL_CAP_FACTOR = 2;
/** Dwell / (RETENTION_TARGET * clipLen) saturates the retention score at 1. */
export const RETENTION_TARGET = 2;
/** EWMA blend factor for the retention score. */
export const EWMA_ALPHA = 0.3;
/** Score assumed for a segment never seen (optimism prior). */
export const SCORE_PRIOR = 0.5;
/** Soft cap on distinct segment stats kept. */
export const MAX_STAT_ENTRIES = 5000;

/** Selection weight: W_BASE + W_BOOST_MAX*score + W_EXPLORE_BONUS/(1+plays).
 *  Balanced profile — hot segments reach ~4x the floor. */
export const W_BASE = 1;
export const W_BOOST_MAX = 3;
export const W_EXPLORE_BONUS = 1;

/** Wider-than-tall enough to stack; excludes near-square by design. */
export const LANDSCAPE_RATIO = 1.2;

/** Asset ids are relative paths (never contain ':'), so ':' separates cleanly. */
export function segId(assetId, k) {
  return `${assetId}:${k}`;
}

export function assetIdOf(key) {
  return key.slice(0, key.indexOf(':'));
}

/** How many segments a video of this duration exposes. Bucket count is sized off
 *  CLIP_MAX (not the per-cut random length) so the set is stable across sessions. */
export function segmentCount(durationSec) {
  if (!Number.isFinite(durationSec) || durationSec <= 0) return 1;
  const span = durationSec - CLIP_MAX_SEC;
  if (span <= 0) return 1;
  return Math.floor(span / SEGMENT_STRIDE_SEC) + 1;
}

/** Which segment a (possibly seed-derived) start offset falls into. */
export function segmentIndexForStart(startSec) {
  return startSec > 0 ? Math.floor(startSec / SEGMENT_STRIDE_SEC) : 0;
}

/** Grid-aligned start for segment k, clamped so the window fits the video. */
export function startForSegment(k, durationSec, clipLenSec) {
  return Math.min(k * SEGMENT_STRIDE_SEC, Math.max(0, durationSec - clipLenSec));
}

/** Random window length in [CLIP_MIN, CLIP_MAX) seconds. */
export function randomClipLenSec(rng = Math.random) {
  return CLIP_MIN_SEC + rng() * (CLIP_MAX_SEC - CLIP_MIN_SEC);
}

/** Unknown dims are treated as portrait until a probe backfills them. */
export function isLandscapeDims(w, h) {
  return w != null && h != null && h > 0 && w >= h * LANDSCAPE_RATIO;
}
