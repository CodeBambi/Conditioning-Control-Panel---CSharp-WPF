/* ============================================================================
 * loomSpirals.js - the player's saved Loom spirals PLUS the bundled spiral
 * pool, and the one picker every 'spiral' effect shares.
 *
 * boot.js fills the Loom list from the host's loom-list message (urls point at
 * the ccp.spirals virtual host). pickSpiralUrl() mixes those ~50/50 with the
 * bundled spirals, and BOTH spiral effect paths call it - the classic overlay
 * (bubbles.fireEffect) and the in-run payload fx (payloadFx.showSpiral) - so a
 * woven spiral shows up everywhere from one source of truth. Deliberately dumb:
 * all file authority/validation is C#-side (DtrhLoomStore).
 *
 * THE BOOK (2026-09-09). Racing Thoughts does not want a stock gif at all - the
 * owner's law is "on ALL the games the spirals should be generated with the
 * Loom". `setLoomBook(fn)` hands this module a weaver (race/loomBook.js), and
 * `pickSpiral()` - a SECOND picker beside the url one, which is untouched -
 * answers with a PARAMS WRAPPER `{loom:true, params, id, href}` for a caller
 * that can draw one live, or a plain url for a caller that cannot. `href` is
 * always a real gif, so the wrapper carries its own floor.
 *
 * A SAVED SPIRAL IS PREFERRED AS PARAMS TOO: loom-list entries carry the `.json`
 * sidecar, so the player's own weave is redrawn from its params rather than
 * fetched as a multi-megabyte gif. Only the race sets a book and only the race
 * passes payloadFx a `spiralFx`, so the Descent still draws exactly the gifs it
 * always has.
 * ==========================================================================*/

let spirals = [];   // [{ slug, url, params }]

// the shipped spiral overlays (assets/bubbles/effects/spirals/); the Loom's
// saved spirals join this pool at runtime via pickSpiralUrl(). All animate
// natively in an <img>/background (gif/webp), so no webm decoder cold-start.
const BUNDLED_BASE = '/dtrh/assets/bubbles/effects/spirals/';
export const BUNDLED_SPIRALS = ['sp1.gif', 'sp2.webp', 'sp3.gif', 'sp4.webp', 'sp5.gif', 'sp6.gif', 'sp7.gif']
  .map((f) => BUNDLED_BASE + f);
// the two lightest of those (sp6.gif 123 KB, sp7.gif 721 KB; the other five weigh 2.2-5.3 MB each,
// sizes on disk 2026-09): the pool a phone draws from once the race asks (setBundledSpiralPool)
export const LEAN_SPIRALS = ['sp6.gif', 'sp7.gif'].map((f) => BUNDLED_BASE + f);
let bundledPool = BUNDLED_SPIRALS;

/** Opt-in: narrow the bundled pool every pickSpiralUrl() draws from (null / empty restores the whole
 *  set). The race sets LEAN_SPIRALS on its mobile tier and prefetches them before the run, so a lap
 *  never fetches a multi-megabyte gif mid-run; nothing else calls this and the Descent keeps the full
 *  pool. The Loom's saved spirals are untouched by it. */
export function setBundledSpiralPool(list) {
  bundledPool = Array.isArray(list) && list.length ? list.slice() : BUNDLED_SPIRALS;
}
export function getBundledSpiralPool() { return bundledPool; }

/** Warm the browser cache for a pool (one <img> per url, decoded off-thread); returns the urls asked
 *  for. A page without a DOM (node) asks for nothing. */
export function prefetchSpirals(list = bundledPool) {
  if (typeof Image === 'undefined') return [];
  for (const u of list) { try { const im = new Image(); im.decoding = 'async'; im.src = u; } catch (e) { /* a warm-up only */ } }
  return list.slice();
}

export function setLoomSpirals(list) {
  spirals = Array.isArray(list) ? list.filter((s) => s && s.url) : [];
}

export function getLoomSpirals() { return spirals; }

/** One spiral overlay url. The player's Loom spirals join the bundled pool
 *  ~50/50 (they only appear once at least one has been woven). */
export function pickSpiralUrl() {
  if (spirals.length && Math.random() < 0.5) {
    return spirals[Math.floor(Math.random() * spirals.length)].url;
  }
  return bundledPool[Math.floor(Math.random() * bundledPool.length)];
}

/* ----------------------------------------------------------------------------
 * THE BOOK: live-woven spirals (race/loomBook.js)
 * -------------------------------------------------------------------------- */

/** () => { loom:true, params, id } | null - set by the race, null everywhere else. */
let book = null;

/** Hand this module a weaver, or null to take it away again (run teardown).
 *  Only race/run.js calls this; with no book, pickSpiral() is pickSpiralUrl(). */
export function setLoomBook(fn) { book = typeof fn === 'function' ? fn : null; }
export function hasLoomBook() { return !!book; }

/** A bundled gif, always: the floor under every wrapper. */
const anyBundled = () => bundledPool[Math.floor(Math.random() * bundledPool.length)];

/**
 * ONE SPIRAL, as either a live-Loom wrapper or a url.
 *
 * The mix is the same ~50/50 pickSpiralUrl has always drawn: the player's own
 * woven spirals first when they have any, the race's book otherwise. A saved
 * spiral that kept its params sidecar comes back as a wrapper too (drawn live,
 * no gif fetched) with its own gif as the `href` floor; one without params is
 * still just its url.
 *
 * @param {Object} [ctx] passed straight to the book ({ room?, word? }); the
 *   race's book reads the live room and the road's last phrase on its own, so
 *   this is only for smokes and shot harnesses.
 * @returns {{loom:true, params:Object, id:string, href:string}|string}
 */
export function pickSpiral(ctx = undefined) {
  if (spirals.length && Math.random() < 0.5) {
    const s = spirals[Math.floor(Math.random() * spirals.length)];
    if (s.params && typeof s.params === 'object') {
      return { loom: true, params: s.params, id: 'saved:' + (s.slug || '?'), href: s.url, saved: true };
    }
    return s.url;
  }
  if (book) {
    try {
      const drawn = book(ctx);
      if (drawn && drawn.params) return { loom: true, params: drawn.params, id: String(drawn.id || 'loom:0'), href: anyBundled(), saved: false };
    } catch (e) { /* a book that throws is a book that is not there */ }
  }
  return anyBundled();
}
