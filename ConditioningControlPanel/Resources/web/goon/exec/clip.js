/* ============================================================================
 * exec/clip.js - a gif that MOVES on a gif surface.
 *
 * Owner, 2026-09-23, desktop with no local assets, pictures from Scrolller only:
 * "gifs and glitch bubbles fullscreen gifs do not animate". The online set has
 * two lanes (Services/GoonGame/GoonOnlineMedia.cs): goon-stills are GifStill,
 * STATIC POSTERS BY DESIGN, and goon-clips are GifClip, the webm/mp4 video
 * rendition of the same gifs. Every gif surface in this page (the glitch/drain
 * wash, the pop flashes, the flash payload, the drain veil) drew from the image
 * lane only, so on a Scrolller-only deck it showed a poster and never moved.
 *
 * This module is the shared piece those surfaces use to play a clip instead:
 *
 *   drawClipHandle(media)  -> a handle for one gif clip (media.drawClip, then
 *                             media.acquire - never entry.url directly), or null
 *                             when there is no clip or the live cap is full.
 *   prepareClip(handle, {className, timeoutMs}, done)
 *                          -> builds a muted, looping, playsinline, autoplay
 *                             <video> and calls done(video) once it has a first
 *                             frame, or done(null) on error / no frame within
 *                             timeoutMs (the caller falls back to its still).
 *   stopClip(video)        -> pause, drop the source, remove, free the slot.
 *
 * THE CAP. At most MAX_LIVE_CLIPS clips decode at once, counted from the moment
 * prepareClip starts until stopClip: a pop storm does not decode ten videos at
 * once, the surplus draws take their still like before.
 *
 * Lite tier never asks for a clip (the surfaces keep their lite still
 * preference); calm does not change anything here (a moving picture is the
 * content, not decoration, same as an animated gif was).
 *
 * Import-safe under node: no DOM at import, every DOM touch is guarded.
 * ==========================================================================*/

/** Simultaneous clips allowed to decode. */
export const MAX_LIVE_CLIPS = 4;

/** A clip that shows no frame in this long is a dud: the caller takes its still. */
export const CLIP_START_MS = 1500;

const live = new Set();

/** How many clips hold a slot right now (tests and the cap). */
export function liveClipCount() {
  for (const v of Array.from(live)) {
    // A node someone removed without stopClip (a torn-down layer) frees its slot.
    // Only once it was handed over: a clip still loading is not in the DOM yet.
    if (v && v.__ggClipMounted && v.__ggClipShown && v.isConnected === false) stopClip(v);
  }
  return live.size;
}

/** Is there room for one more clip? */
export function clipRoom() {
  return liveClipCount() < MAX_LIVE_CLIPS;
}

/**
 * One gif clip's handle, or null (no clip in the deck, no room, a pool without
 * drawClip). The handle is the pool's own acquire() result.
 */
export function drawClipHandle(media) {
  if (!media || typeof media.drawClip !== 'function' || typeof media.acquire !== 'function') return null;
  if (!clipRoom()) return null;
  let entry = null;
  try { entry = media.drawClip(); } catch (_e) { entry = null; }
  if (!entry) return null;
  let handle = null;
  try { handle = media.acquire(entry); } catch (_e) { handle = null; }
  if (!handle || !handle.url) {
    try { if (handle && handle.release) handle.release(); } catch (_e) { /* ignore */ }
    return null;
  }
  return handle;
}

/**
 * Build the <video> for `handle` and report back ONCE: done(video) when it has
 * a frame to show, done(null) when it failed (the video is already stopped and
 * its slot freed; the HANDLE stays the caller's to release either way).
 * Returns the video (so a caller can cancel with stopClip) or null when no
 * video could be made at all (done(null) has then already been called).
 */
export function prepareClip(handle, opts, done) {
  const cb = typeof done === 'function' ? done : () => {};
  const o = opts || {};
  let v = null;
  try {
    if (typeof document !== 'undefined' && document && typeof document.createElement === 'function') {
      v = document.createElement('video');
    }
  } catch (_e) { v = null; }
  if (!v || typeof v.addEventListener !== 'function' || !handle || !handle.url) {
    cb(null);
    return null;
  }
  v.className = o.className || '';
  // All four as attributes AND properties: autoplay of a muted inline video is
  // what every engine allows without a gesture, and each one reads a different half.
  v.muted = true;
  v.defaultMuted = true;
  v.loop = true;
  v.playsInline = true;
  v.autoplay = true;
  try {
    v.setAttribute('muted', '');
    v.setAttribute('loop', '');
    v.setAttribute('playsinline', '');
    v.setAttribute('autoplay', '');
    v.setAttribute('disablepictureinpicture', '');
    v.setAttribute('aria-hidden', 'true');
  } catch (_e) { /* ignore */ }
  v.preload = 'auto';
  v.draggable = false;
  v.__ggClipMounted = true;
  live.add(v);

  let settled = false;
  let timer = 0;
  const finish = (ok) => {
    if (settled) return;
    settled = true;
    try { clearTimeout(timer); } catch (_e) { /* ignore */ }
    // The caller already stopped it (their surface went away): nobody to tell.
    if (!v.__ggClipMounted) return;
    if (ok) {
      try {
        const p = v.play();
        if (p && typeof p.catch === 'function') p.catch(() => { /* first frame still shows */ });
      } catch (_e) { /* ignore */ }
      cb(v);
      v.__ggClipShown = true;
    } else {
      stopClip(v);
      cb(null);
    }
  };
  v.addEventListener('loadeddata', () => finish(true), { once: true });
  v.addEventListener('error', () => finish(false), { once: true });
  const ms = Number(o.timeoutMs) > 0 ? Number(o.timeoutMs) : CLIP_START_MS;
  try { timer = setTimeout(() => finish(false), ms); } catch (_e) { /* ignore */ }
  try { v.src = handle.url; } catch (_e) { finish(false); return null; }
  try { if (typeof v.load === 'function') v.load(); } catch (_e) { /* ignore */ }
  return v;
}

/** Stop and remove a clip, and free its slot. Safe to call twice, and on null. */
export function stopClip(v) {
  if (!v) return;
  live.delete(v);
  if (!v.__ggClipMounted) return;
  v.__ggClipMounted = false;
  try { v.pause(); } catch (_e) { /* ignore */ }
  try { v.removeAttribute('src'); } catch (_e) { /* ignore */ }
  // load() after dropping src is what makes the engine release the decoder now
  // instead of at garbage collection.
  try { if (typeof v.load === 'function') v.load(); } catch (_e) { /* ignore */ }
  try { v.remove(); } catch (_e) { /* ignore */ }
}

/** Test seam: forget every slot (never called by the page). */
export function _resetClipsForTests() {
  live.clear();
}
