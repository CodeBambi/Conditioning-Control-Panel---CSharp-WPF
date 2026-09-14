/* ============================================================================
 * backroom/room/gif-decode.js - one picture that can play: an animated GIF (or
 * animated WebP) decoded in the page with WebCodecs ImageDecoder, drawn into a
 * small canvas. No three.js here (CONTRACT 10.13.D): the room's wall screens
 * wrap it in a CanvasTexture (room/gif.js), the hypno kit draws it straight
 * onto a 2D canvas (shared/hypno/media.js).
 *
 * Why ImageDecoder and no vendored decoder: WebView2 is Chromium, which has
 * shipped ImageDecoder since 94 in secure contexts (https://ccp.game is one),
 * it returns fully composited frames (GIF disposal handled), and it decodes off
 * the main thread. Where it is missing or refuses the file, the caller falls
 * back to a still first frame through an <img>.
 *
 * Costs are capped by the caller's clock, not here: a source advances only when
 * `tick()` is called, at most once per its frame delay and never faster than
 * `maxFps`, one decode in flight, into a canvas no larger than `maxEdge` px.
 * ==========================================================================*/

export const MAX_FPS = 12;
export const MAX_EDGE = 384;
const EXT = { gif: 'image/gif', webp: 'image/webp', png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg' };

export const canAnimate = () => typeof ImageDecoder === 'function';

/**
 * Decode `url` into a playable source, or null when this page cannot (caller uses a still).
 * `onFrame` (optional) runs after each frame lands in the canvas, the first one excluded.
 * @returns {Promise<{canvas, animated, frames, index, tick(now, still), dispose()} | null>}
 */
export async function decodedSource(url, { maxEdge = MAX_EDGE, maxFps = MAX_FPS, onFrame = null } = {}) {
  if (!canAnimate()) return null;
  let decoder = null;
  try {
    const res = await fetch(url, { mode: 'cors', credentials: 'omit' });
    if (!res.ok) return null;
    const ext = (new URL(url, location.href).pathname.split('.').pop() || '').toLowerCase();
    const type = (res.headers.get('content-type') || '').split(';')[0].trim() || EXT[ext] || '';
    if (!type.startsWith('image/') || !(await ImageDecoder.isTypeSupported(type))) return null;
    decoder = new ImageDecoder({ data: await res.arrayBuffer(), type });
    await decoder.tracks.ready;
    await decoder.completed;
    const track = decoder.tracks.selectedTrack;
    let count = track ? track.frameCount : 1;
    const first = (await decoder.decode({ frameIndex: 0 })).image;
    const scale = Math.min(1, maxEdge / Math.max(first.displayWidth, first.displayHeight));
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(first.displayWidth * scale));
    canvas.height = Math.max(1, Math.round(first.displayHeight * scale));
    const g = canvas.getContext('2d');
    g.drawImage(first, 0, 0, canvas.width, canvas.height);
    first.close();

    if (count < 2) { try { decoder.close(); } catch (e) { /* noop */ } }   // a still needs no decoder kept open
    const minGap = 1000 / Math.max(1, maxFps);
    let index = 0, dueAt = 0, busy = false, closed = false, frames = 0;
    const show = (i) => {
      busy = true;
      return decoder.decode({ frameIndex: i }).then((r) => {
        if (closed) { r.image.close(); return; }
        g.drawImage(r.image, 0, 0, canvas.width, canvas.height);
        const delay = r.image.duration ? r.image.duration / 1000 : 100;   // microseconds to ms
        r.image.close();
        index = i; frames++;
        dueAt = performance.now() + Math.max(minGap, delay);
        if (typeof onFrame === 'function') { try { onFrame(i); } catch (e) { /* the caller's problem */ } }
      }).catch(() => { count = 1; }).finally(() => { busy = false; });
    };

    return {
      canvas, animated: count > 1,
      get frames() { return frames; },
      get index() { return index; },
      /** Advance if due. `still` holds (and returns to) the first frame. Returns true when a decode started. */
      tick(now, still) {
        if (closed || busy || count < 2) return false;
        if (still) { if (index !== 0) { show(0); return true; } return false; }
        if (now < dueAt) return false;
        show((index + 1) % count);
        return true;
      },
      dispose() { closed = true; try { decoder.close(); } catch (e) { /* noop */ } },
    };
  } catch (e) {
    try { if (decoder) decoder.close(); } catch (err) { /* noop */ }
    return null;
  }
}
