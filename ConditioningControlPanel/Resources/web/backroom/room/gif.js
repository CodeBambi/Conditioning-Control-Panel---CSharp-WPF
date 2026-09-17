/* ============================================================================
 * backroom/room/gif.js - one wall picture that can play. The decoding lives in
 * room/gif-decode.js (three-free, shared with the hypno kit); this wraps its
 * canvas in the CanvasTexture a wall screen samples.
 *
 * Costs are capped by the caller's clock, not here: a source advances only when
 * `tick()` is called, at most once per its frame delay and never faster than
 * MAX_FPS, one decode in flight, into a texture no larger than MAX_EDGE px.
 * ==========================================================================*/

import * as T from 'three';
import { decodedSource, canAnimate, MAX_FPS, MAX_EDGE } from './gif-decode.js';
import { clipSource, isClip } from './clip-source.js';

export { canAnimate, MAX_FPS, MAX_EDGE };

/** Decode `url` into a playable texture source, or null when this page cannot (caller uses a still). */
export async function animatedSource(url, { signal } = {}) {
  let texture = null;
  const onFrame = () => { if (texture) texture.needsUpdate = true; };
  // A webm or mp4 from the clip feed is played, not decoded: WebView2 is Chromium and does it in
  // hardware, so the desktop needs no transcode hop. clip-source.js hands back the same shape a
  // decoded GIF does, so everything below this line is unchanged.
  const src = isClip(url)
    ? await clipSource(url, { signal, onFrame })
    : await decodedSource(url, { signal, onFrame });
  if (!src) return null;
  texture = new T.CanvasTexture(src.canvas);
  texture.colorSpace = T.SRGBColorSpace; texture.flipY = false;
  texture.generateMipmaps = false; texture.minFilter = T.LinearFilter;
  return {
    texture, animated: src.animated,
    get frames() { return src.frames; },
    get index() { return src.index; },
    /** Advance if due. `still` holds (and returns to) the first frame. Returns true when a decode started. */
    tick: (now, still) => src.tick(now, still),
    dispose() { src.dispose(); texture.dispose(); },
  };
}
