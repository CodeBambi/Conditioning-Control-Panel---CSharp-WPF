/* ============================================================================
 * hostMedia.js - manifest-driven media pool for the in-app DtRH game.
 *
 * Replaces the site's media.js (folder pickers / zip ingest): the WPF host
 * enumerates the user's active preset (DtrhAssetManifest.cs) and posts a
 * `manifest` message of https://ccp.assets/ URLs. Same consumer interface the
 * spawner expects from createMediaSource():
 *   draw() / drawKind(kind) -> { kind, name, acquire } | null
 *   acquire() (async)       -> { url, release() } | null
 *   stats() -> { images, videos, skipped }   hasUserMedia() -> bool
 *
 * The memory contract gets STRONGER than the site's: entries hold URLs only -
 * no handles, no blobs. Chromium's HTTP cache + the virtual host do the lazy
 * work; release() is a no-op because there is nothing to revoke.
 *
 * The shuffled deck + echo guard are ported verbatim from media.js:76-150 so
 * draw behavior (non-repeating, echo-resistant on tiny pools) is identical.
 * ==========================================================================*/

const NO_ECHO = 8; // a reshuffled deck avoids repeating the last N draws

export function createHostMediaSource() {
  let entries = [];   // { kind: 'image'|'video', name, url }
  let skipped = 0;    // reported by the host (browser-undecodable formats etc.)
  let deck = [];      // shuffled indices into entries, drawn from the end
  const recent = [];  // last NO_ECHO drawn indices (echo guard for tiny pools)

  const counts = () => {
    let images = 0, videos = 0;
    for (const e of entries) (e.kind === 'image' ? images++ : videos++);
    return { images, videos, skipped };
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

  const makeAcquire = (entry) => async function acquire() {
    // Nothing to read or revoke - the URL streams straight off the virtual host.
    return { url: entry.url, release() {} };
  };

  window.__sfMedia = counts; // panel diagnostics read the pool size live

  return {
    /** Swap in a (new) manifest from the host: {images:[{name,url}], videos:[...], skipped}. */
    setManifest(m) {
      entries = [];
      for (const e of (m.images || [])) entries.push({ kind: 'image', name: e.name, url: e.url });
      for (const e of (m.videos || [])) entries.push({ kind: 'video', name: e.name, url: e.url });
      skipped = m.skipped | 0;
      deck = [];          // re-deal with the new entries in the mix
      recent.length = 0;
    },

    hasUserMedia: () => entries.length > 0,
    stats: counts,

    // Picker surface from the site's media.js: the pool is host-fed here, so the
    // in-scene "add a folder" affordances are simply off (supportsFS false) and the
    // rest are inert no-ops kept only so the engine can call them blindly.
    supportsFS: () => false,
    pickFolder: async () => null,
    handleDrop: async () => counts(),
    addFileList: () => counts(),
    addZip: async () => counts(),

    // Draw the next entry from the shuffled deck (null when the pool is empty).
    draw() {
      const i = drawIndex();
      if (i < 0) return null;
      const e = entries[i];
      return { kind: e.kind, name: e.name, acquire: makeAcquire(e) };
    },

    // Draw specifically an image/video (used when the live-video cap is hit).
    drawKind(kind) {
      if (!entries.some((e) => e.kind === kind)) return null;
      for (let tries = 0; tries < 24; tries++) {
        const i = drawIndex();
        if (i < 0) return null;
        const e = entries[i];
        if (e.kind === kind) return { kind: e.kind, name: e.name, acquire: makeAcquire(e) };
      }
      return null;
    },
  };
}
