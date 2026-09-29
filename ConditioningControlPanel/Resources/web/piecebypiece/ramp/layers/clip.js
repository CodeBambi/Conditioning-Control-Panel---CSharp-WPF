/* ============================================================================
 * layers/clip.js - a gif that MOVES on a gif surface (2026-09-28).
 *
 * Online gifs arrive from the host as GifClip renditions (webm/mp4, the moving
 * version of a Scrolller gif), never GifStill posters, which are static by
 * design. A clip cannot be a background-image or an <img>, so the gif surfaces
 * (rain, overlay, grab sticker) ask here: a clip url becomes a muted, looping,
 * inline <video>; anything else stays the still it always was. Same idea as the
 * Goon Game's exec/clip.js, smaller.
 *
 * THE CAP. At most MAX_LIVE_CLIPS decode at once. A surface that finds the cap
 * full takes a still instead (stillOr), so a capture burst never decodes ten
 * videos in one frame.
 *
 * Import-safe under node: no DOM at import, every DOM touch is guarded.
 * ==========================================================================*/

export const MAX_LIVE_CLIPS = 4;

const live = new Set();

/** A url that has to play as video. */
export function isClip(url) {
  return typeof url === 'string' && /\.(mp4|webm|m4v)(\?|#|$)/i.test(url);
}

/** Clips holding a slot now. A node someone removed without stopClip frees its slot. */
export function liveClips() {
  for (const v of [...live]) if (v && v.__pbpMounted && v.isConnected === false) live.delete(v);
  return live.size;
}

export const clipRoom = () => liveClips() < MAX_LIVE_CLIPS;

/** `url` when it can be shown now, else a still from `fallback()` (null is fine). */
export function stillOr(url, fallback) {
  if (!isClip(url) || clipRoom()) return url;
  try { return typeof fallback === 'function' ? fallback() : null; } catch { return null; }
}

/** A muted looping inline <video> for a clip, holding a slot until stopClip. Null when full. */
export function makeClip(ctx, url, cls) {
  if (!isClip(url) || !clipRoom()) return null;
  const v = ctx.el('video', cls);
  if (!v) return null;
  try {
    v.muted = true; v.defaultMuted = true; v.loop = true; v.autoplay = true; v.playsInline = true;
    v.setAttribute('muted', ''); v.setAttribute('playsinline', '');
    v.preload = 'auto';
    v.src = url;
    v.__pbpMounted = true;
    live.add(v);
    const p = v.play && v.play();
    if (p && typeof p.catch === 'function') p.catch(() => { /* autoplay is allowed by the host; a refusal just shows the frame */ });
  } catch { live.delete(v); return null; }
  return v;
}

/** Pause, drop the source, remove, free the slot. Safe on null and twice. */
export function stopClip(v) {
  if (!v) return;
  live.delete(v);
  try { v.pause(); } catch { /* gone */ }
  try { v.removeAttribute('src'); v.load(); } catch { /* gone */ }
  try { v.remove(); } catch { /* gone */ }
}

/**
 * Dress a box that used to take a background-image: a still stays a
 * background-image, a clip becomes a <video class="pbp-clip-fill"> child that
 * covers the box. The previous clip child, if any, is stopped first.
 * Answers the url the box shows now, or null: with nothing to show the box is
 * left empty, never wearing its old picture (the pool may hand that one out again).
 */
export function dressBox(ctx, el, url, fallback) {
  if (!el) return null;
  const shown = stillOr(url, fallback);
  if (el.__pbpClip) { stopClip(el.__pbpClip); el.__pbpClip = null; }
  if (isClip(shown)) {
    const v = makeClip(ctx, shown, 'pbp-clip-fill');
    if (v) {
      try { el.style.backgroundImage = ''; el.appendChild(v); el.__pbpClip = v; return shown; } catch { stopClip(v); }
    }
    try { el.style.backgroundImage = ''; } catch { /* gone */ }
    return null;
  }
  try { el.style.backgroundImage = shown ? `url("${shown}")` : ''; } catch { /* gone */ }
  return shown || null;
}

/** Drop a box's clip child (the box is being cleared or disposed). */
export function undressBox(el) {
  if (el && el.__pbpClip) { stopClip(el.__pbpClip); el.__pbpClip = null; }
}

/** Tests only. */
export function _resetClips() { live.clear(); }
