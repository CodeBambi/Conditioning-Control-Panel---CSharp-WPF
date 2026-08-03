/* ============================================================================
 * exec/media.js — the Goon Game media pool. Cloned from dtrh/hostMedia.js (the
 * shuffled deck + 8-draw echo guard are ported verbatim so draw behaviour is
 * identical), minus DtRH's favorites ranking and its window.__sfMedia global.
 *
 * The host enumerates the user's active preset and posts ONE `manifest` frame of
 * https://ccp.assets/ URLs; entries hold URLs ONLY — no handles, no blobs.
 * Chromium's HTTP cache + the virtual host do the lazy work, so release() is a
 * no-op because there is nothing to revoke.
 *
 * INTERFACE CONTRACT (do not widen): P3-web swaps this backend for one that
 * hands out blob: URLs from an IndexedDB/OPFS cache. Everything downstream must
 * keep going through acquire()/release() — never entry.url directly — so that
 * swap stays a one-file change.
 *
 *   setManifest({images,videos,skipped,truncated})
 *   draw() / drawKind('image'|'video') -> {kind, name, url, acquire} | null
 *   acquire(entry) -> {url, release()}
 *   counts() -> {images, videos, skipped, truncated}   hasMedia() -> bool
 * ==========================================================================*/

const NO_ECHO = 8; // a reshuffled deck avoids repeating the last N draws

export function createGoonMediaPool() {
  let entries = [];   // { kind: 'image'|'video', name, url }
  let skipped = 0;    // reported by the host (browser-undecodable formats etc.)
  let truncated = false;
  let deck = [];      // shuffled indices into entries, drawn from the end
  const recent = [];  // last NO_ECHO drawn indices (echo guard for tiny pools)

  const counts = () => {
    let images = 0, videos = 0;
    for (const e of entries) (e.kind === 'image' ? images++ : videos++);
    return { images, videos, skipped, truncated };
  };

  function reshuffle() {
    deck = entries.map((_, i) => i);
    for (let i = deck.length - 1; i > 0; i--) {
      const j = (Math.random() * (i + 1)) | 0;
      [deck[i], deck[j]] = [deck[j], deck[i]];
    }
    // echo guard: push recently-drawn indices to the bottom of the fresh deck
    if (entries.length > NO_ECHO) {
      for (const r of recent) {
        const at = deck.indexOf(r);
        if (at >= 0) { deck.splice(at, 1); deck.unshift(r); }
      }
    }
  }

  function drawIndex() {
    if (!entries.length) return -1;
    if (!deck.length) reshuffle();
    const i = deck.pop();
    if (i == null || i >= entries.length) {
      reshuffle();
      return deck.length ? deck.pop() : -1;
    }
    recent.push(i);
    if (recent.length > NO_ECHO) recent.shift();
    return i;
  }

  /** The ONE handle every consumer holds. Backend-swappable (see header). */
  function acquire(entry) {
    if (!entry || !entry.url) return null;
    // Nothing to read or revoke — the URL streams straight off the virtual host.
    return { url: entry.url, release() {} };
  }

  const view = (e) => ({ kind: e.kind, name: e.name, url: e.url, acquire: () => acquire(e) });

  return {
    /** Swap in a manifest: {images:[{name,url}], videos:[...], skipped, truncated}. */
    setManifest(m) {
      const src = m || {};
      entries = [];
      for (const e of (src.images || [])) if (e && e.url) entries.push({ kind: 'image', name: e.name || '', url: e.url });
      for (const e of (src.videos || [])) if (e && e.url) entries.push({ kind: 'video', name: e.name || '', url: e.url });
      skipped = src.skipped | 0;
      truncated = !!src.truncated;
      deck = [];          // re-deal with the new entries in the mix
      recent.length = 0;
      return counts();
    },

    hasMedia: () => entries.length > 0,
    counts,
    acquire,

    /** Resolve an asset name to its virtual-host URL (null if not in the pool). */
    urlByName(name) {
      const e = name ? entries.find((x) => x.name === name) : null;
      return e ? e.url : null;
    },

    /** Next entry from the shuffled deck (null when the pool is empty). */
    draw() {
      const i = drawIndex();
      return i < 0 ? null : view(entries[i]);
    },

    /** Draw specifically an image/video (null when that kind is absent). */
    drawKind(kind) {
      if (!entries.some((e) => e.kind === kind)) return null;
      for (let tries = 0; tries < 24; tries++) {
        const i = drawIndex();
        if (i < 0) return null;
        if (entries[i].kind === kind) return view(entries[i]);
      }
      return null;
    },
  };
}

/** The page's single pool (constructed at import — touches nothing global). */
export const media = createGoonMediaPool();
export default media;
