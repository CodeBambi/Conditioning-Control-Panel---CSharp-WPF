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
 *   setLocalLibrary([{kind,name,url}])                 (see below)
 *   draw() / drawKind('image'|'video') -> {kind, name, url, acquire} | null
 *   acquire(entry) -> {url, release(), provenance}
 *   counts() -> {images, videos, skipped, truncated}   hasMedia() -> bool
 *
 * THE LOCAL LIBRARY (standalone). In a plain browser there is no host and
 * therefore no manifest frame worth the name — bridge.js synthesizes an EMPTY
 * one — so the only library a phone has is what the player picked on the assets
 * screen (ui/assetsStore.js `localItems`). Those picks used to reach the SEND
 * path only, which meant a practice session on a phone drew from an empty deck
 * and every effect fired with no media. `setLocalLibrary` is the other half:
 * the deck is `hostEntries + localEntries`, and the two are set INDEPENDENTLY —
 * a late manifest cannot wipe the player's picks and a new pick cannot wipe the
 * host's preset. Both re-deal; neither touches `received`.
 *
 * RECEIVED ARTIFACTS (P2P media transfer, spec §4.3) live in a SECOND map that
 * the deck never sees:
 *
 *   addReceived({sha,kind,mime,url,bytes,acquire?})   incremental; never the deck
 *   hasReceived(sha) / dropReceived(sha) / receivedCount()
 *   acquireByTag('xfer:<sha>') -> {url, release(), provenance:'peer'} | null
 *   drawFor(kind, payload)     -> THE resolution order (tag hit, else own library)
 *   attachBlocklist(bl)        -> setter, so this stays a LEAF (no net/ imports)
 *
 * Three consequences, all of them the point:
 *   - setManifest's hard deck reset cannot destroy them;
 *   - a random draw()/drawKind() can NEVER surface the opponent's file — their
 *     media appears exactly where their payload asked for it and nowhere else;
 *   - "incremental add" is a Map write: no reshuffle, no disturbance of the
 *     8-draw echo guard.
 * ==========================================================================*/

const NO_ECHO = 8; // a reshuffled deck avoids repeating the last N draws

/** The one namespace `tags` carries for this feature. `tags` stays open for others. */
export const XFER_TAG_PREFIX = 'xfer:';

const SHA_RE = /^[0-9a-f]{64}$/;

export function createGoonMediaPool() {
  let hostEntries = [];   // the host's manifest — the user's active preset
  let localEntries = [];  // standalone: files the player picked in this browser
  let entries = [];       // hostEntries + localEntries — what the deck indexes
  let skipped = 0;    // reported by the host (browser-undecodable formats etc.)
  let truncated = false;
  let deck = [];      // shuffled indices into entries, drawn from the end
  const recent = [];  // last NO_ECHO drawn indices (echo guard for tiny pools)

  /**
   * sha -> {sha, kind, mime, url, bytes, acquire?}. SEPARATE FROM `entries` BY
   * DESIGN — see the header. `acquire` is the received store's refcounted view
   * factory, handed in by boot; without it the plain `url` is used and release()
   * is the no-op the disk backend wants anyway.
   */
  const received = new Map();

  /** Last sha drawReceived handed out per kind, so a 2-3 file pool doesn't strobe. */
  const lastPeerPick = { image: '', video: '' };

  /** Injected (never imported): {knows(sha), isBlocked(sha)} from net/blocklist.js. */
  let blocklist = null;

  const counts = () => {
    let images = 0, videos = 0;
    for (const e of entries) (e.kind === 'image' ? images++ : videos++);
    return { images, videos, skipped, truncated };
  };

  /**
   * The deck's view of the two sources, re-dealt. Called by BOTH setters, which
   * is what makes them independent: whichever moved, the other survives.
   */
  function rebuildEntries() {
    entries = localEntries.length ? hostEntries.concat(localEntries) : hostEntries.slice();
    deck = [];          // re-deal with the new entries in the mix
    recent.length = 0;
  }

  /** One {kind,name,url} normalized, or null when it is not something to draw. */
  function toEntry(e) {
    if (!e || !e.url) return null;
    const kind = e.kind === 'video' ? 'video' : (e.kind === 'image' ? 'image' : '');
    if (!kind) return null;
    return { kind, name: String(e.name || ''), url: String(e.url) };
  }

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
    // A peer artifact never resolves off the entry: it goes back through the
    // received map so a refcounting backend (the standalone blob store) actually
    // sees the acquire, and so a hash dropped between draw and acquire (blocklist
    // sweep, user delete) reads as "gone" instead of a dangling URL.
    if (entry.provenance === 'peer') {
      const rec = received.get(entry.sha);
      return rec ? acquirePeer(rec) : null;
    }
    // Nothing to read or revoke — the URL streams straight off the virtual host.
    return { url: entry.url, release() {}, provenance: 'local' };
  }

  const view = (e) => ({
    kind: e.kind, name: e.name, url: e.url, provenance: 'local', acquire: () => acquire(e),
  });

  /* ------------------------------------------------------------ received map */

  /** The refcounted handle for one received artifact. */
  function acquirePeer(rec) {
    let h = null;
    if (typeof rec.acquire === 'function') {
      try { h = rec.acquire(); } catch (_e) { h = null; }
    }
    if (h && h.url) {
      // The memory backend revokes its object URL at refcount zero and mints a
      // fresh one on the next view; keep the record's copy current so the
      // no-store fallback below never hands out a revoked URL.
      rec.url = h.url;
      const rel = typeof h.release === 'function' ? h.release.bind(h) : (() => {});
      return { url: h.url, release: rel, provenance: 'peer', sha: rec.sha, mime: rec.mime };
    }
    if (!rec.url) return null;
    return { url: rec.url, release() {}, provenance: 'peer', sha: rec.sha, mime: rec.mime };
  }

  /**
   * One received artifact as an ENTRY (the shape drawKind returns), or null.
   * `kind` must AGREE — a video tag on a FlashBurst is skipped, not stretched —
   * and a blocklisted sha is null here, which is the gate that actually matters
   * because it is the one that puts pixels on screen (spec §7.4).
   */
  function viewReceived(sha, kind) {
    if (typeof sha !== 'string' || !SHA_RE.test(sha)) return null;
    const rec = received.get(sha);
    if (!rec) return null;
    if (kind && rec.kind !== kind) return null;
    if (blocklist && typeof blocklist.isBlocked === 'function') {
      try { if (blocklist.isBlocked(sha) === true) return null; } catch (_e) { /* never fatal */ }
    }
    return {
      kind: rec.kind,
      name: XFER_TAG_PREFIX + sha.slice(0, 12),
      url: rec.url,
      mime: rec.mime,
      sha,
      provenance: 'peer',
      acquire: () => acquirePeer(rec),
    };
  }

  /** The deck draw, hoisted so `drawFor` can reach it without a `this` binding. */
  function drawKindInner(kind) {
    if (!entries.some((e) => e.kind === kind)) return null;
    for (let tries = 0; tries < 24; tries++) {
      const i = drawIndex();
      if (i < 0) return null;
      if (entries[i].kind === kind) return view(entries[i]);
    }
    return null;
  }

  return {
    /** Swap in a manifest: {images:[{name,url}], videos:[...], skipped, truncated}. */
    setManifest(m) {
      const src = m || {};
      hostEntries = [];
      for (const e of (src.images || [])) { const v = toEntry({ kind: 'image', name: e && e.name, url: e && e.url }); if (v) hostEntries.push(v); }
      for (const e of (src.videos || [])) { const v = toEntry({ kind: 'video', name: e && e.name, url: e && e.url }); if (v) hostEntries.push(v); }
      skipped = src.skipped | 0;
      truncated = !!src.truncated;
      rebuildEntries();
      // NOTE: `received` is deliberately NOT touched. The deck reset is about the
      // user's own preset changing; what a duel partner sent is keyed by hash and
      // has nothing to do with it (spec §4.3, trap register #5/#7).
      // NOTE 2: `localEntries` is not touched either — a manifest frame that lands
      // after the player has picked files (a host that re-scans its preset, or the
      // synthesized standalone frame arriving late) must not empty their library.
      return counts();
    },

    /**
     * Swap in the LOCAL library — the whole list, every time, because that is
     * what ui/assetsStore.js can hand over cheaply and a diff would only buy a
     * reshuffle we do not mind paying for. Entries are `{kind,name,url}` with
     * `kind` ALREADY DECIDED by the store (a blob: URL has no extension to sniff,
     * so re-deriving it here would classify every pick as an image).
     *
     * URLs are the store's to own: acquire() hands them out with a no-op
     * release(), so nothing here ever revokes a blob the assets screen is still
     * rendering a thumbnail from.
     */
    setLocalLibrary(list) {
      localEntries = [];
      for (const e of (list || [])) { const v = toEntry(e); if (v) localEntries.push(v); }
      rebuildEntries();
      return counts();
    },

    /** How many of the deck's entries came from the player's own picks. */
    localCount: () => localEntries.length,

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
    drawKind: drawKindInner,

    /**
     * A NON-CONSUMING look at what the next drawKind(kind) would most likely
     * hand back: the topmost entry of that kind still in the shuffled deck (and,
     * for a deck that has not been dealt yet, the first one in the pool).
     *
     * IT MUTATES NOTHING — not `deck`, not `recent`, not the echo guard — and
     * that restraint is the entire reason it exists. ui/throwPreview.js shows the
     * player a thumbnail of what a payload is about to play; if it got that
     * thumbnail by DRAWING, it would spend the draw the effect was going to make
     * and the preview would be a picture of a clip that then never played. A
     * preview must never be able to change what it is previewing.
     *
     * It is a GUESS by construction (drawKind skips past the wrong kinds, and
     * anything may draw in between), so callers treat it as representative —
     * see `exact` in ui/throwPreview.js. A tag hit is the only exact answer.
     */
    peekKind(kind) {
      const want = kind === 'video' ? 'video' : (kind === 'image' ? 'image' : '');
      if (!want) return null;
      for (let i = deck.length - 1; i >= 0; i--) {
        const e = entries[deck[i]];
        if (e && e.kind === want) return view(e);
      }
      for (const e of entries) if (e.kind === want) return view(e);
      return null;
    },

    /* ------------------------------------------------ received (peer) artifacts */

    /**
     * Point the render-time gate at the blocklist. A SETTER, not an import: this
     * module is a leaf and must never reach into net/, or exec/ and net/ start
     * importing each other and the node import sweep stops being a straight line.
     */
    attachBlocklist(bl) {
      blocklist = (bl && typeof bl.isBlocked === 'function') ? bl : null;
      return !!blocklist;
    },

    /**
     * Register one artifact a duel partner sent (or one this machine already had,
     * primed from the manifest frame). Touches a Map, NEVER the deck.
     * @param {{sha:string, kind:string, mime?:string, url:string, bytes?:number,
     *          acquire?:() => ({url:string, release?:() => void}|null)}} a
     */
    addReceived(a) {
      const o = a || {};
      const sha = typeof o.sha === 'string' ? o.sha : '';
      if (!SHA_RE.test(sha)) return false;
      const kind = o.kind === 'video' ? 'video' : (o.kind === 'image' ? 'image' : '');
      if (!kind) return false;
      if (!o.url && typeof o.acquire !== 'function') return false;
      received.set(sha, {
        sha,
        kind,
        mime: String(o.mime || ''),
        url: String(o.url || ''),
        bytes: Math.max(0, Number(o.bytes) || 0),
        acquire: typeof o.acquire === 'function' ? o.acquire : null,
      });
      return true;
    },

    /** The offer gate's dedupe answer, synchronous. */
    hasReceived(sha) { return typeof sha === 'string' && received.has(sha); },

    /** Blocklist sweep / eviction / user delete. Does not touch the file. */
    dropReceived(sha) { return typeof sha === 'string' && received.delete(sha); },

    receivedCount() { return received.size; },

    /**
     * 'xfer:<sha>' -> a peer handle, or null. Null is the WHOLE fallback story:
     * a blocked hash, a kind that never landed and an unknown tag all read the
     * same, and the caller draws from its own library exactly as it does today.
     */
    acquireByTag(tag) {
      if (typeof tag !== 'string' || !tag.startsWith(XFER_TAG_PREFIX)) return null;
      const v = viewReceived(tag.slice(XFER_TAG_PREFIX.length), null);
      return v ? v.acquire() : null;
    },

    /**
     * THE resolution order (spec §4.3), and the only door a peer artifact has.
     * Tags are taken IN ORDER and anything unrecognised is skipped; the last line
     * is today's behaviour, unchanged, which is why a transfer that never landed
     * costs the receiver nothing.
     */
    drawFor(kind, payload) {
      const tags = (payload && Array.isArray(payload.tags)) ? payload.tags : null;
      if (tags) {
        for (const t of tags) {
          if (typeof t !== 'string' || !t.startsWith(XFER_TAG_PREFIX)) continue;
          const v = viewReceived(t.slice(XFER_TAG_PREFIX.length), kind);   // kind must match, else skip
          if (v) return v;
        }
      }
      return drawKindInner(kind);                                          // <- today's line
    },

    /**
     * A random received (peer) artifact of that kind, or null. This is how a
     * PAYLOAD keeps rendering the sender's files after its few `xfer:` tags are
     * spent (2026-08-05 play-test: one desktop gif and then local-only reads as
     * "the transfer doesn't work"): everything the opponent has transferred this
     * match is fair game, and the pool only grows as the queue keeps landing.
     *
     * The header's invariant survives because ONLY payload render paths call
     * this — draw()/drawKind() still never surface a peer file, so their media
     * still appears exactly where a payload of theirs asked for it. Blocklist
     * and kind agreement ride on viewReceived; with any choice at all the same
     * sha is never handed out twice in a row.
     */
    drawReceived(kind) {
      const want = kind === 'video' ? 'video' : (kind === 'image' ? 'image' : '');
      if (!want) return null;
      const pool = [];
      for (const sha of received.keys()) {
        const v = viewReceived(sha, want);
        if (v) pool.push(v);
      }
      if (!pool.length) return null;
      let at = (Math.random() * pool.length) | 0;
      if (pool.length > 1 && pool[at].sha === lastPeerPick[want]) at = (at + 1) % pool.length;
      lastPeerPick[want] = pool[at].sha;
      return pool[at];
    },
  };
}

/** The page's single pool (constructed at import — touches nothing global). */
export const media = createGoonMediaPool();
export default media;
