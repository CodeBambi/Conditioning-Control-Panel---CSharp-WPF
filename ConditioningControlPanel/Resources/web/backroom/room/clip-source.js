/* ============================================================================
 * backroom/room/clip-source.js - a Scrolller clip as a wall picture.
 *
 * WHY THIS EXISTS. Scrolller's "GIF" feed is webm and mp4; its "PICTURE" feed is
 * static. The web playtest solves that with a Vercel function that spawns ffmpeg
 * and transcodes each clip to animated WebP (api/clip.js, 384 px / 12 fps / 4 s),
 * because a browser page cannot cheaply hand a webm to a WebGL texture.
 *
 * The desktop host has no such problem: it is WebView2, which is Chromium, and
 * decodes VP9 and H.264 natively. So a clip the host has already materialized
 * into the assets temp dir plays straight off ccp.assets. Measured against the
 * alternatives on one representative clip at the 384 px rung: playing it costs
 * 339 KB (the source itself), an ffmpeg WebP hop costs 225-400 KB plus 10-20 MB
 * on the installer, and the bundled LibVLC-plus-WIC animated GIF route costs
 * 2.96 MB - which the card table would multiply by thirteen per sit-down.
 *
 * SAME SHAPE AS room/gif-decode.js ON PURPOSE. It returns the identical source
 * object, so room/gif.js wraps it in the same CanvasTexture and room/screens.js
 * does not learn a new kind of picture. In particular it paints into a canvas on
 * tick() rather than being a three.js VideoTexture: the room's clock owns when a
 * picture costs anything (CONTRACT 10.13.D), and a VideoTexture would upload
 * every frame the video decoded whether the room wanted it or not.
 *
 * Three-free, like gif-decode.js. No three.js import belongs in this file.
 * ==========================================================================*/

import { MAX_FPS, MAX_EDGE } from './gif-decode.js';
import { MEDIA_LIMITS } from './media-limits.js';

/** What Scrolller's clip feed actually returns, and all WebView2 needs to decode it. */
const CLIP_EXT = /\.(webm|mp4|m4v)$/i;

/**
 * True when this url is a clip rather than a picture. Used by room/gif.js to route.
 *
 * The extension is read off the PATH, with the query and the fragment cut away first. That is not
 * tidiness, it is the whole bug: the web playtest asks for its pictures through the transcoding hop
 * above, `/api/clip?u=<the clip url, encoded>`, which answers ANIMATED WEBP - and the encoded clip
 * url in that query still ends in `.mp4`, because encodeURIComponent leaves a dot alone. Matching
 * the whole string therefore sent every transcoded picture to a <video> element that cannot decode
 * a WebP, the element errored, clipSource returned null, and room/screens.js fell back to a plain
 * still texture. That is why the room's pulled GIFs showed one frame and never moved.
 */
export const isClip = (url) => CLIP_EXT.test(String(url || '').split(/[?#]/)[0]);

/** A page with no video element (a test stub, a stripped host) simply gets stills. */
export const canPlayClips = () => {
  try { return typeof document?.createElement === 'function' && !!document.createElement('video').canPlayType; }
  catch { return false; }
};

/**
 * Open `url` as a playable source, or null when this page cannot play it (the caller then uses a
 * still, exactly as it does for a GIF it could not decode).
 * `onFrame` (optional) runs after each frame lands in the canvas, the first one excluded.
 * @returns {Promise<{canvas, byteLength, animated, frames, index, tick(now, still), dispose()} | null>}
 */
export async function clipSource(url, { signal, onFrame } = {}) {
  if (!isClip(url) || !canPlayClips()) return null;

  const video = document.createElement('video');
  // muted is not a preference: an unmuted video will not autoplay, and the room's sound is the
  // kit's and the soundtrack's business. A Scrolller clip is silent anyway (CONTRACT 5).
  video.muted = true; video.defaultMuted = true; video.loop = true;
  video.autoplay = false; video.playsInline = true; video.preload = 'auto';
  // ccp.assets is mapped Allow precisely so dealt media can reach WebGL; ask for CORS so the
  // canvas this draws into stays readable and the texture upload is not refused as tainted.
  video.crossOrigin = 'anonymous';
  let closed = false;
  const stop = () => {
    closed = true;
    try { video.pause(); } catch { /* a detached element is already stopped */ }
    // Dropping the src is what actually frees the decoder; removeAttribute then load() is the
    // documented way, and without it a dismissed wall keeps a video decoding off screen.
    try { video.removeAttribute('src'); video.load(); } catch { /* noop */ }
  };

  try {
    await new Promise((resolve, reject) => {
      const fail = (why) => { cleanup(); reject(new Error(why)); };
      const done = () => { cleanup(); resolve(); };
      const abort = () => fail('clip load cancelled');
      // MEDIA_LIMITS.loadMs is the same budget a GIF gets. A clip that has not produced its
      // dimensions by then is not going to be worth waiting for on a wall.
      const timer = setTimeout(() => fail('clip load timed out'), MEDIA_LIMITS.loadMs);
      function cleanup() {
        clearTimeout(timer);
        video.removeEventListener('loadedmetadata', done);
        video.removeEventListener('error', onError);
        signal?.removeEventListener('abort', abort);
      }
      function onError() { fail('clip could not be decoded'); }
      video.addEventListener('loadedmetadata', done, { once: true });
      video.addEventListener('error', onError, { once: true });
      signal?.addEventListener('abort', abort, { once: true });
      if (signal?.aborted) { abort(); return; }
      video.src = url;
    });
  } catch { stop(); return null; }

  const vw = video.videoWidth, vh = video.videoHeight;
  if (!vw || !vh) { stop(); return null; }
  // The same two ceilings a decoded GIF gets: MAX_EDGE on the long side, and the pixel budget.
  const scale = Math.min(1, MAX_EDGE / Math.max(vw, vh));
  let w = Math.max(1, Math.round(vw * scale)), h = Math.max(1, Math.round(vh * scale));
  if (w * h > MEDIA_LIMITS.pixels) {
    const shrink = Math.sqrt(MEDIA_LIMITS.pixels / (w * h));
    w = Math.max(1, Math.floor(w * shrink)); h = Math.max(1, Math.floor(h * shrink));
  }

  const canvas = document.createElement('canvas');
  canvas.width = w; canvas.height = h;
  const ctx = canvas.getContext('2d', { alpha: false });
  const gap = 1000 / MAX_FPS;
  let dueAt = 0, painted = 0, first = true;

  const paint = () => {
    if (closed) return false;
    try { ctx.drawImage(video, 0, 0, w, h); } catch { return false; }
    painted++;
    if (first) first = false;
    else if (typeof onFrame === 'function') { try { onFrame(); } catch { /* the caller's problem */ } }
    return true;
  };

  try { await video.play(); } catch { /* a refused play still paints its first frame below */ }
  paint();

  return {
    canvas, byteLength: 0,   // the host streams it off disk; there is no decoded buffer to count
    animated: true,
    // A clip has no frame list to walk. `frames` is how many times the room has taken a picture of
    // it, which is what the room's budget counts, and `index` keeps the source's shape honest.
    get frames() { return painted; },
    get index() { return 0; },
    /**
     * Advance if due. `still` holds the picture where it is, which for a clip means pausing the
     * decoder rather than merely not drawing: an off-screen video that keeps decoding is exactly
     * the offscreen work the render budget exists to stop. Returns true when a frame was taken.
     */
    tick(now, still) {
      if (closed) return false;
      if (still) {
        if (!video.paused) { try { video.pause(); } catch { /* noop */ } }
        return false;
      }
      if (video.paused) { try { video.play().catch(() => {}); } catch { /* noop */ } }
      if (now < dueAt) return false;
      dueAt = now + gap;
      return paint();
    },
    dispose() { stop(); },
  };
}
