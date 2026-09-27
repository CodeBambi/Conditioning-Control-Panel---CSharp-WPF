/* ============================================================================
 * backroom/room/gif-decode.js - one picture that can play: an animated GIF (or
 * animated WebP) decoded in the page with WebCodecs ImageDecoder, drawn into a
 * small canvas. No three.js here (CONTRACT 10.13.D): the room's wall screens
 * wrap it in a CanvasTexture (room/gif.js), the hypno kit draws it straight
 * onto a 2D canvas (shared/hypno/media.js).
 *
 * Prefer ImageDecoder: WebView2 is Chromium, which has
 * shipped ImageDecoder since 94 in secure contexts (https://ccp.game is one),
 * it returns composited frames off the main thread. HTTP previews and browsers
 * without WebCodecs use image-frames.js; unsupported files keep a still frame.
 *
 * Costs are capped by the caller's clock, not here: a source advances only when
 * `tick()` is called, at most once per its frame delay and never faster than
 * `maxFps`, one decode in flight, into a canvas no larger than `maxEdge` px.
 * ==========================================================================*/

import { boundedStill } from './bounded-still.js';
import { compatibilityDecoder } from './image-frames.js';
import { MEDIA_LIMITS, MediaLimitError, refusedMedia, boundedImageBytes, imageDimensions, checkDimensions } from './media-limits.js';

/* 30, not 12 (2026-09-18, desktop testers: "framerate on the images on the monitors is awful, especially the
 * spiral gifs"). A spiral is authored at 25-33 fps, so a 12 fps cap made it lurch, and the next frame was
 * scheduled from decode COMPLETION rather than from the frame's own due time, so decode latency plus the
 * 30 Hz render tick pushed a "12 fps" source to 8-9 uneven fps. The cap is the room's: the slot and the
 * hypno deck pass their own maxFps. */
export const MAX_FPS = 30;
export const MAX_EDGE = 384;
const EXT = { gif: 'image/gif', webp: 'image/webp', png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg' };

export const canAnimate = () => typeof ImageDecoder === 'function';

function whileLoading(promise, signal) {
  if (signal.aborted) return Promise.reject(new DOMException('Media load cancelled', 'AbortError'));
  return new Promise((resolve,reject) => {
    const abort = () => reject(new DOMException('Media load cancelled', 'AbortError'));
    signal.addEventListener('abort',abort,{once:true});
    Promise.resolve(promise).then(resolve,reject).finally(()=>signal.removeEventListener('abort',abort));
  });
}

/**
 * Decode `url` into a playable source, or null when this page cannot (caller uses a still).
 * `onFrame` (optional) runs after each frame lands in the canvas, the first one excluded.
 * @returns {Promise<{canvas, animated, frames, index, tick(now, still), dispose()} | null>}
 */
export async function decodedSource(url, { maxEdge = MAX_EDGE, maxFps = MAX_FPS, onFrame = null, signal = null, maxBytes = MEDIA_LIMITS.bytes, preferCanvas = false } = {}) {
  let decoder = null, data = null, type = '', validated = false, native = false;
  const controller = new AbortController();
  const abort = () => { controller.abort(); try { decoder?.close(); } catch {} };
  signal?.addEventListener('abort', abort, {once:true});
  if (signal?.aborted) abort();
  const timer = setTimeout(abort, MEDIA_LIMITS.loadMs);
  try {
    const res = await whileLoading(fetch(url, { mode: 'cors', credentials: 'same-origin', signal: controller.signal }), controller.signal);
    if (!res.ok) throw new MediaLimitError('Media transfer failed','transfer');
    const ext = (new URL(url, location.href).pathname.split('.').pop() || '').toLowerCase();
    type = (res.headers.get('content-type') || '').split(';')[0].trim() || EXT[ext] || '';
    if (!type.startsWith('image/')) throw new MediaLimitError('Not an image','transfer');
    data = await boundedImageBytes(res, controller.signal, maxBytes);
    const dimensions = imageDimensions(data, type);
    if (!dimensions) throw new MediaLimitError('Unsupported image header','transfer');   // our sniffer's gap, not the file's fault: the browser may still decode it
    checkDimensions(...dimensions); validated = true;
    controller.signal.throwIfAborted();
    native = !preferCanvas && canAnimate() && await whileLoading(ImageDecoder.isTypeSupported(type), controller.signal);
    decoder = native ? new ImageDecoder({ data, type }) : await compatibilityDecoder(data, type);
    controller.signal.throwIfAborted();
    if (!decoder) return await boundedStill(data, type, maxEdge, controller.signal);
    await whileLoading(decoder.tracks.ready, controller.signal);
    await whileLoading(decoder.completed, controller.signal);
    controller.signal.throwIfAborted();
    const track = decoder.tracks.selectedTrack;
    let count = track ? track.frameCount : 1;
    if (count > MEDIA_LIMITS.frames) throw new MediaLimitError('Animation frame count exceeds media budget');
    const first = (await whileLoading(decoder.decode({ frameIndex: 0 }).then(result => {
      if (controller.signal.aborted) { result.image.close(); throw new DOMException('Media load cancelled','AbortError'); }
      return result;
    }), controller.signal)).image;
    try { controller.signal.throwIfAborted(); checkDimensions(first.displayWidth, first.displayHeight); }
    catch(error) { first.close(); throw error; }
    const scale = Math.min(1, maxEdge / Math.max(first.displayWidth, first.displayHeight));
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(first.displayWidth * scale));
    canvas.height = Math.max(1, Math.round(first.displayHeight * scale));
    const g = canvas.getContext('2d');
    try { g.drawImage(first, 0, 0, canvas.width, canvas.height); } finally { first.close(); }

    if (count < 2) { try { decoder.close(); } catch (e) { /* noop */ } }   // a still needs no decoder kept open
    const minGap = 1000 / Math.max(1, maxFps);
    let index = 0, dueAt = 0, busy = false, closed = false, frames = 0;
    const show = (i, started) => {
      busy = true;
      // Firefox can leave a backwards decode pending forever after the last frame.
      // Reopen from the retained, bounded bytes when rewinding, including Motion still.
      if (native && i < index) { decoder.close(); decoder = new ImageDecoder({ data, type }); }
      return decoder.decode({ frameIndex: i }).then((r) => {
        if (closed) { r.image.close(); return; }
        const delay = r.image.duration ? r.image.duration / 1000 : 100;   // microseconds to ms
        try {
          g.clearRect(0, 0, canvas.width, canvas.height);
          g.drawImage(r.image, 0, 0, canvas.width, canvas.height);
        } finally { r.image.close(); }
        index = i; frames++;
        // Keep the file's own cadence: the next frame is due one delay after this one was DUE, not after
        // its decode landed, so latency and tick quantisation stop compounding. More than one delay late
        // (off-frustum, a held room) re-anchors to the tick that woke it rather than bursting to catch up.
        const step = Math.max(minGap, delay);
        dueAt = (started - dueAt > step ? started : dueAt) + step;
        if (typeof onFrame === 'function') { try { onFrame(i); } catch (e) { /* the caller's problem */ } }
      }).catch(() => { count = 1; }).finally(() => { busy = false; });
    };

    return {
      canvas, byteLength: data.byteLength, animated: count > 1,
      get frames() { return frames; },
      get index() { return index; },
      /** Advance if due. `still` holds (and returns to) the first frame. Returns true when a decode started. */
      tick(now, still) {
        if (closed || busy || count < 2) return false;
        if (still) { if (index !== 0) { show(0, now); return true; } return false; }
        if (now < dueAt) return false;
        show((index + 1) % count, now);
        return true;
      },
      dispose() { closed = true; try { decoder.close(); } catch (e) { /* noop */ } },
    };
  } catch (e) {
    try { if (decoder) decoder.close(); } catch (err) { /* noop */ }
    if (controller.signal.aborted) throw new DOMException('Media load cancelled', 'AbortError');
    if (refusedMedia(e)) throw e;
    if (data && validated) return await boundedStill(data, type, maxEdge, controller.signal);
    throw new MediaLimitError('Media transfer failed','transfer');
  } finally {
    clearTimeout(timer); signal?.removeEventListener('abort', abort);
  }
}
