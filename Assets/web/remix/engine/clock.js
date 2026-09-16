/* ============================================================================
 * clock.js - one master clock, 15 fps, everything resampled onto it.
 *
 * A source gif runs at whatever delays it was authored with; a video runs at
 * its own rate. At import we build a lookup from master frame -> source frame
 * so playback never has to think about time again. Sources loop to fill the
 * canvas, a still image is one frame repeated.
 *
 * Pure. No DOM.
 * ==========================================================================*/

export const FPS = 15;
export const DURATIONS = [3, 5, 8];
export const PLAY_MODES = ['forward', 'hold', 'rewind', 'boomerang'];

/** Seconds -> master frames. */
export function framesForSeconds(sec, fps = FPS) {
  return Math.max(1, Math.round(sec * fps));
}

/** Master frames -> seconds. */
export function secondsForFrames(frames, fps = FPS) {
  return frames / fps;
}

/**
 * The canvas grew from `was` to `now` frames: does this strip need decoding
 * again? A strip is one loop of the source, capped at the canvas length, so
 * only a strip that hit the old cap and whose source has more to give.
 */
export function stripNeedsMore(stripLen, srcDurMs, was, now, fps = FPS) {
  if (!(now > was) || stripLen >= now || stripLen < was) return false;
  return Math.ceil(srcDurMs / (1000 / fps)) > stripLen;
}

/**
 * Resample a source onto the master clock.
 *
 * durations: per source frame, in ms (a still image passes [Infinity] or []).
 * Returns an array of length `count` of source frame indices, nearest source
 * frame by time, looping once the source runs out.
 */
export function resampleIndices(durations, count, fps = FPS) {
  const n = durations && durations.length ? durations.length : 1;
  const out = new Array(Math.max(0, count | 0));
  if (n === 1) { out.fill(0); return out; }

  // cumulative start time of each source frame, and the total
  const starts = new Array(n);
  let total = 0;
  for (let i = 0; i < n; i++) {
    starts[i] = total;
    total += Math.max(10, durations[i] || 0);
  }
  if (!(total > 0)) { out.fill(0); return out; }

  const step = 1000 / fps;
  let cursor = 0;
  for (let f = 0; f < out.length; f++) {
    const t = (f * step) % total;
    // starts is ascending; walk forward with wrap, cheaper than a search
    if (t < starts[cursor]) cursor = 0;
    while (cursor + 1 < n && starts[cursor + 1] <= t) cursor++;
    out[f] = cursor;
  }
  return out;
}

/** Total source duration in ms. */
export function totalDuration(durations) {
  let t = 0;
  for (let i = 0; i < (durations ? durations.length : 0); i++) t += Math.max(10, durations[i] || 0);
  return t;
}

/**
 * The source frame a tile shows on master frame `frame`.
 *
 * len      how many frames the tile's resampled source has
 * mode     forward | hold | rewind | boomerang
 * enter    the tile's enter frame (only `hold` cares)
 * offset   extra shift, used by the mirror layout's droste stagger
 *
 * forward  the source has been running since canvas frame 0, so a tile that
 *          arrives late arrives mid loop.
 * hold     the source starts at its first frame when the tile lands.
 */
export function sourceIndexFor(mode, frame, enter = 0, len = 1, offset = 0) {
  const n = Math.max(1, len | 0);
  if (n === 1) return 0;
  let k;
  switch (mode) {
    case 'hold': k = Math.max(0, frame - (enter | 0)); break;
    default: k = frame; break;
  }
  k += offset | 0;
  switch (mode) {
    case 'rewind': {
      const m = mod(k, n);
      return n - 1 - m;
    }
    case 'boomerang': {
      const period = n * 2 - 2;
      const m = mod(k, period);
      return m < n ? m : period - m;
    }
    default:
      return mod(k, n);
  }
}

function mod(a, n) {
  return ((a % n) + n) % n;
}

/** Loop marker treatment windows. Returns null when the frame is untouched. */
export function loopPhase(kind, frame, frames) {
  if (kind === 'snap') {
    const start = frames - 3;
    if (frame >= start) return { kind: 'snap', u: (frame - start + 1) / 3 };
    return null;
  }
  if (kind === 'seamless') {
    const start = frames - 6;
    if (frame >= start) return { kind: 'seamless', u: (frame - start + 1) / 6, mixFrame: frame - start };
    return null;
  }
  return null;
}
