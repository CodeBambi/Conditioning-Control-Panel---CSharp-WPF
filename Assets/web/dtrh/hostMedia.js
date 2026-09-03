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
 *
 * ---------------------------------------------------------------------------
 * TWO POOLS, AND THE REASON THEY CANNOT BE ONE
 *
 * Since the app-wide remote-media work the host may ride REMOTE entries on the
 * same manifest frame: absolute CDN urls (scrolller) instead of ccp.assets ones.
 * Their CDN sends no Access-Control-Allow-Origin, so those urls are CORS-TAINTED,
 * and that is not a detail we can paper over with a try/catch:
 *
 *   - `fetch(url)` REJECTS outright. That kills spawner.js's image path
 *     (fetch -> blob -> createImageBitmap), its gif path, and wallPosters.js.
 *   - `video.crossOrigin='anonymous'` makes the element REFUSE TO LOAD when the
 *     response has no ACAO header - spawner.js sets it on every user video.
 *   - Dropping crossOrigin does not help: uploading a tainted image/video to a
 *     WebGL texture throws SecurityError, and so does drawing it through a canvas.
 *
 * Every three.js consumer in this game is on one of those roads. So remote
 * entries live in a SECOND pool that draw()/drawKind()/favorite()/urlByName()
 * cannot see - those four feed the WebGL layer and stay local-only forever - and
 * are handed out only through drawDom(), whose one consumer is game/payloadFx.js
 * (plain <img>/<video>/CSS background-image, no canvas, no texture). The DTRH
 * tube therefore stays local-media-only by design; the payload FX are where a
 * user with no library of their own actually sees something.
 *
 * Remote-ness is decided by the URL's ORIGIN, never by the host's name marker: a
 * marker is a hint, an origin is a fact, and only a fact belongs on the road that
 * keeps tainted pixels out of the GPU.
 * ==========================================================================*/

const NO_ECHO = 8; // a reshuffled deck avoids repeating the last N draws

/** Origins WebView2 maps to local folders (see DtrhHostService's mapping table).
 *  Anything else is third-party and therefore CORS-tainted. */
const LOCAL_URL_RE = /^(?:https?:\/\/ccp\.[a-z0-9-]+\/|data:|blob:|\.{0,2}\/)/i;
const isLocalUrl = (u) => typeof u === 'string' && LOCAL_URL_RE.test(u);

/** "online30:sub-123.webm" -> 30. The host stamps the app-wide remote share onto
 *  every remote entry's name because the manifest frame's shape ({name,url}) is
 *  owned by two C# host services and is not ours to widen. Absent/garbled -> the
 *  AppSettings default. */
const SHARE_RE = /^online(\d{1,3}):/i;
const DEFAULT_REMOTE_SHARE = 0.3;

export function createHostMediaSource() {
  let entries = [];   // LOCAL { kind: 'image'|'video', name, url } - safe for WebGL
  let remote = [];    // REMOTE (CDN, tainted) - DOM layer only, never a texture
  let remoteShare = DEFAULT_REMOTE_SHARE;
  const remoteRecent = [];   // last few remote urls drawn (echo guard; the pool is small)
  let skipped = 0;    // reported by the host (browser-undecodable formats etc.)
  let deck = [];      // shuffled indices into entries, drawn from the end
  const recent = [];  // last NO_ECHO drawn indices (echo guard for tiny pools)
  let favorites = []; // host-ranked asset names (dtrh_asset_stats.json, most-engaged first)

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

  /** The LOCAL deck draw, hoisted so drawDom() can reach it without a `this` binding
   *  (callers routinely hold `media.drawDom` on its own). */
  function drawLocal(kind) {
    if (kind && !entries.some((e) => e.kind === kind)) return null;
    for (let tries = 0; tries < 24; tries++) {
      const i = drawIndex();
      if (i < 0) return null;
      const e = entries[i];
      if (!kind || e.kind === kind) return { kind: e.kind, name: e.name, acquire: makeAcquire(e) };
    }
    return null;
  }

  window.__sfMedia = counts; // panel diagnostics read the pool size live

  return {
    /** Swap in a (new) manifest from the host: {images:[{name,url}], videos:[...], skipped}.
     *  Splits local from remote on the way in (see the header): the local half is the only
     *  half the WebGL layer will ever be offered. */
    setManifest(m) {
      entries = [];
      remote = [];
      remoteShare = DEFAULT_REMOTE_SHARE;
      const take = (list, kind) => {
        for (const e of (list || [])) {
          if (!e || !e.url) continue;
          const name = String(e.name || '');
          const share = SHARE_RE.exec(name);
          if (share) remoteShare = Math.min(1, Math.max(0, Number(share[1]) / 100));
          const item = { kind, name: share ? name.slice(share[0].length) : name, url: e.url };
          (isLocalUrl(e.url) ? entries : remote).push(item);
        }
      };
      take(m.images, 'image');
      take(m.videos, 'video');
      skipped = m.skipped | 0;
      deck = [];          // re-deal with the new entries in the mix
      recent.length = 0;
      remoteRecent.length = 0;
    },

    hasUserMedia: () => entries.length > 0,
    /** Anything the DOM layer could show, local or remote. payloadFx gates on THIS, not on
     *  hasUserMedia — the whole point of remote media is a user whose library is empty. */
    hasDomMedia: () => entries.length > 0 || remote.length > 0,
    stats: counts,

    // ---- THE BIOMES (S3 read-back): the host ranks assets by cumulative
    // engagement (DtrhAssetStatsStore) and posts the top names with the
    // manifest. Mirror biomes ask for "the one you like most". ----
    /** Host-ranked names, most-engaged first ({type:'favorites'} message). */
    setFavorites(names) { favorites = Array.isArray(names) ? names.filter(Boolean) : []; },
    /** The most-engaged asset still present in the pool (kind-filtered). */
    favorite(kind) {
      for (const name of favorites) {
        const e = entries.find((x) => x.name === name && (!kind || x.kind === kind));
        if (e) return { kind: e.kind, name: e.name, url: e.url };
      }
      return null;
    },
    /** Resolve an asset name to its virtual-host URL (null if not in the pool). */
    urlByName(name) {
      const e = name ? entries.find((x) => x.name === name) : null;
      return e ? e.url : null;
    },

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

    /**
     * THE DOM-ONLY DRAW. Same {kind,name,acquire} shape as draw()/drawKind(), but it may
     * return a REMOTE entry, so the caller must render it with a plain element and nothing
     * else — no canvas, no createImageBitmap, no fetch, no THREE texture. game/payloadFx.js
     * is the only caller today; if you add another, read this file's header first.
     *
     * The mix: `remoteShare` of picks come from the remote pool ("online" mode stamps 100).
     * Either side being empty for this kind hands the draw to the other, so a user with no
     * library still gets payload FX and a user who is offline still gets their own media.
     */
    drawDom(kind) {
      const wantRemote = remote.some((e) => !kind || e.kind === kind);
      const wantLocal = entries.some((e) => !kind || e.kind === kind);
      if (!wantRemote && !wantLocal) return null;
      const useRemote = wantRemote && (!wantLocal || Math.random() < remoteShare);
      if (!useRemote) return drawLocal(kind);
      // Small pool, no deck: pick at random and refuse the last few urls outright.
      const pool = remote.filter((e) => (!kind || e.kind === kind) && !remoteRecent.includes(e.url));
      const from = pool.length ? pool : remote.filter((e) => !kind || e.kind === kind);
      const e = from[(Math.random() * from.length) | 0];
      if (!e) return null;
      remoteRecent.push(e.url);
      if (remoteRecent.length > 4) remoteRecent.shift();
      return { kind: e.kind, name: e.name, remote: true, acquire: makeAcquire(e) };
    },
  };
}
