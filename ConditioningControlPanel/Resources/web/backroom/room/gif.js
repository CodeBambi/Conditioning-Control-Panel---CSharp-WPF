/* ============================================================================
 * backroom/room/gif.js - one wall picture that can play: an animated GIF (or
 * animated WebP) decoded in the page with WebCodecs ImageDecoder, drawn into a
 * small canvas that is the screen's texture.
 *
 * Why ImageDecoder and no vendored decoder: WebView2 is Chromium, which has
 * shipped ImageDecoder since 94 in secure contexts (https://ccp.game is one),
 * it returns fully composited frames (GIF disposal handled), and it decodes off
 * the main thread. Where it is missing or refuses the file, the caller falls
 * back to a still first frame through an <img>.
 *
 * Costs are capped by the caller's clock, not here: a source advances only when
 * `tick()` is called, at most once per its frame delay and never faster than
 * MAX_FPS, one decode in flight, into a texture no larger than MAX_EDGE px.
 * ==========================================================================*/

import * as T from 'three';

export const MAX_FPS = 12;
export const MAX_EDGE = 384;
const EXT = { gif: 'image/gif', webp: 'image/webp', png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg' };

export const canAnimate = () => typeof ImageDecoder === 'function';

/** Decode `url` into a playable source, or null when this page cannot (caller uses a still). */
export async function animatedSource(url) {
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
    const scale = Math.min(1, MAX_EDGE / Math.max(first.displayWidth, first.displayHeight));
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(first.displayWidth * scale));
    canvas.height = Math.max(1, Math.round(first.displayHeight * scale));
    const g = canvas.getContext('2d');
    g.drawImage(first, 0, 0, canvas.width, canvas.height);
    first.close();
    const texture = new T.CanvasTexture(canvas);
    texture.colorSpace = T.SRGBColorSpace; texture.flipY = false;
    texture.generateMipmaps = false; texture.minFilter = T.LinearFilter;

    if (count < 2) { try { decoder.close(); } catch (e) { /* noop */ } }   // a still needs no decoder kept open
    let index = 0, dueAt = 0, busy = false, closed = false, frames = 0;
    const show = (i) => {
      busy = true;
      return decoder.decode({ frameIndex: i }).then((r) => {
        if (closed) { r.image.close(); return; }
        g.drawImage(r.image, 0, 0, canvas.width, canvas.height);
        const delay = r.image.duration ? r.image.duration / 1000 : 100;   // microseconds to ms
        r.image.close();
        index = i; frames++;
        texture.needsUpdate = true;
        dueAt = performance.now() + Math.max(1000 / MAX_FPS, delay);
      }).catch(() => { count = 1; }).finally(() => { busy = false; });
    };

    return {
      texture, animated: count > 1,
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
      dispose() { closed = true; try { decoder.close(); } catch (e) { /* noop */ } texture.dispose(); },
    };
  } catch (e) {
    try { if (decoder) decoder.close(); } catch (err) { /* noop */ }
    return null;
  }
}
