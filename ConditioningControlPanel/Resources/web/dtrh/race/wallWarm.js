/* ============================================================================
 * race/wallWarm.js - the feed pictures a run OPENS on.
 *
 *   warmWallPosters(media, n)  draw n feed pictures and start loading them NOW
 *   warmShots()                the ones that have finished, [{ url, aspect }]
 *
 * WHY IT EXISTS. race/wallDom.js only ever hangs a picture that has already
 * finished loading, which is the right rule and the reason a run used to open on
 * a bare wall: the loading started when the world was built, and a phone on a
 * CDN needs a second or two per picture. So the loading has to start EARLIER
 * than the world. raceBoot calls this the moment a `manifest` carrying feed rows
 * lands - before the menu, let alone the countdown - and whatever has decoded by
 * the time a run is built is on the wall from the first metre.
 *
 * THE ELEMENTS ARE KEPT. Holding each loader <img> keeps its decode resident, so
 * pointing a wall slot at the same url paints in the same frame instead of
 * re-decoding. Sixteen feed-sized pictures is the cap and the whole cost.
 *
 * NO THREE, NO DOM LAYOUT, NOTHING TO TEAR DOWN. This module is imported by
 * raceBoot, which must not pull the 3D graph in on the boot path, so it imports
 * nothing at all. race/wallDom.js reads the set; nobody else should.
 *
 * REMOTE ONLY, DOM ONLY. Every url here comes from hostMedia's drawRemoteDom(),
 * so it is a third-party CDN url and it may only ever be an <img> src. It is
 * never fetched, never drawn to a canvas and never uploaded to a texture
 * (dtrh/hostMedia.js's header has the reason).
 * ==========================================================================*/

/** Pictures held at once. Twelve is what a wall wants (race/wallDom.js PRE_DRAW); the
 *  few spare let a recycled slot re-point at something it was not already showing. */
export const WARM_MAX = 16;

const warm = [];      // { url, aspect, img } - only ever pushed once the picture is in
let pending = 0;      // draws in flight, so a second call does not re-ask for the same slots

/** The decoded set, for race/wallDom.js. Live array: it grows as pictures land. */
export function warmShots() { return warm; }

/** Let the set go (a feed switched off, a manifest with nothing remote in it). */
export function clearWarmPosters() {
  for (const s of warm) { if (s.img) { s.img.onload = null; s.img.onerror = null; s.img.src = ''; } }
  warm.length = 0;
}

function pull(pick) {
  pending++;
  Promise.resolve().then(() => pick.acquire()).then((h) => {
    if (!h || !h.url) { pending--; return; }
    // no crossOrigin: the feed's CDN sends no ACAO and an anonymous request would not load at all
    const img = new Image();
    img.decoding = 'async';
    img.onload = () => {
      pending--;
      if (img.naturalWidth && warm.length < WARM_MAX) warm.push({ url: h.url, aspect: img.naturalHeight / img.naturalWidth, img });
    };
    img.onerror = () => { pending--; };
    img.src = h.url;
  }).catch(() => { pending--; });
}

/**
 * Top the set up to `n` pictures from the feed pool. Cheap and idempotent: a full set asks for
 * nothing, and a pool with no feed in it (the desktop, or consent off) drops the set and returns 0.
 *
 * @param {object} media  dtrh/hostMedia.js's source; anything without drawRemoteDom is ignored
 * @param {number} [n]    how many to hold, capped at WARM_MAX
 * @returns {number} how many pictures are in hand right now
 */
export function warmWallPosters(media, n = 12) {
  if (!media || typeof media.drawRemoteDom !== 'function') return warm.length;
  if (typeof Image === 'undefined') return warm.length;
  const want = Math.max(1, Math.min(WARM_MAX, n | 0));
  for (let i = warm.length + pending; i < want; i++) {
    const pick = media.drawRemoteDom('image');
    // Nothing in the pool: if nothing has landed either, the feed is gone and so is the set.
    if (!pick) { if (!warm.length && !pending) clearWarmPosters(); break; }
    pull(pick);
  }
  return warm.length;
}

export default warmWallPosters;
