/* ============================================================================
 * provider/index.js — THE ASSET PROVIDER (BUILD-CONTRACT §6).
 *
 * One interface, two sources. Games declare needs ("24 loops + 1 target") and
 * NEVER fetch anything themselves.
 *
 *   const assets = createAssets({ bridge, remoteMediaEnabled, remoteMediaRatio,
 *                                 offlineMode, platform, localManifest, niches,
 *                                 rng, log });
 *   const pool = await assets.claim({ loops: 24, targets: 1, stills: 6,
 *                                     canvasSafe: false, niches: ['...'] });
 *   pool.next('loop')   -> { url, remote }   NEVER blocks, NEVER null
 *   pool.next('target') -> { url, remote }   the hunt target (its own slot)
 *   pool.release()
 *
 * SECOND SHAPE, ADDITIVE (SORT, 2026-08-23): `claimTagged({sources, want,
 * perSourceMin, seed, timeoutMs})` deals TWO PILES whose rows carry the `tag`
 * the host stamped on them - see ./tagged.js for the laws. Nothing about
 * `claim()` moves; every other class draws exactly the media it drew before.
 * Beside it ride the DOOR's four reads/writes: `catalog()` (the niches, the
 * player's sub library, their local folders and asset presets, projected on
 * init), `probeSub(name)`, `removeLibrarySub(name)` and `onLibrary(cb)`.
 *
 * LAWS
 *  - NEVER BLOCK A DRAW. A local candidate is always ready: the C#-supplied
 *    manifest, else the bundled mono-pink placeholder tiles in ./assets. Remote
 *    urls only ever ARRIVE LATE and top the pool up (FlashService posture:
 *    empty remote falls through to local, silently).
 *  - CORS TWO-POOL LAW. `canvasSafe:true` pools are LOCAL ONLY — a canvas/WebGL
 *    consumer never sees a tainted origin. Enforced on origin, not on a marker.
 *  - iOS is mp4-only; formats are filtered per platform.
 *  - Remote goes through the BRIDGE ('assets-request'/'assets'), never a fetch
 *    from the page, and never at all under OfflineMode or a closed consent gate.
 *  - claim() PREWARMS (link rel=preload, else new Image()) so the first draw is
 *    already decoded.
 *
 * LOCAL INVENTORY. A virtual host cannot be enumerated from JS, so SOMETHING has
 * to hand the page the list. Accepted (first non-empty wins, all merged):
 *   createAssets({ localManifest: ['images/a.gif', ...] })          explicit
 *   createAssets({ manifest: [...] }) / ({ media: {images, gifs} }) intake-shaped
 *   createAssets({ settings: init.settings })                       scanned for
 *     localAssets (the name C# uses) / arcademyAssetManifest / assetManifest /
 *     localManifest / manifest / assets / media
 * Entries may be ccp.assets-relative paths or absolute ccp.* urls. With NO
 * manifest at all the provider still works — on the bundled placeholder tiles.
 * ==========================================================================*/

import { buildLocalPools, isLocalUrl, formatOk, kindOf, wantRemote } from './inventory.js';
import { createRemoteChannel } from './remote.js';
import { createTaggedPool, TAGGED } from './tagged.js';

/** Bundled placeholder tiles (geometric mono-pink, no text). The floor. */
export const PLACEHOLDER_FILES = Object.freeze([
  'ae-ph-1.svg', 'ae-ph-2.svg', 'ae-ph-3.svg', 'ae-ph-4.svg', 'ae-ph-5.svg', 'ae-ph-6.svg',
]);

function placeholderUrls() {
  return PLACEHOLDER_FILES.map((f) => {
    try { return new URL('./assets/' + f, import.meta.url).href; } catch { return './assets/' + f; }
  });
}

const REMOTE_CAP = 80;        // mirrors intake's REMOTE_CAP: a page never hoards media

/** Keys the shell/host might carry the local inventory under. A page cannot
 *  enumerate a virtual host, so SOMETHING has to hand us the list; we accept
 *  every reasonable shape rather than making the shell adapt to one name. */
const MANIFEST_KEYS = Object.freeze([
  // 'localAssets' FIRST: that is the name ArcademyHostService actually ships the
  // host-built inventory under (BuildSettingsBag -> {gifs:[], stills:[]} of
  // absolute ccp.assets urls). The rest are tolerated aliases.
  'localAssets',
  'arcademyAssetManifest', 'assetManifest', 'localManifest', 'manifest', 'assets', 'media',
]);

/** Pull a manifest array out of whatever was passed (array | {images,gifs} | settings bag). */
export function resolveManifest(opts) {
  const seen = [];
  const push = (v) => {
    if (!v) return;
    if (Array.isArray(v)) { seen.push(...v); return; }
    if (typeof v === 'object') {
      for (const k of ['images', 'gifs', 'loops', 'stills', 'videos']) if (Array.isArray(v[k])) seen.push(...v[k]);
    }
  };
  push(opts.localManifest);
  push(opts.manifest);
  push(opts.media);
  const bag = opts.settings;
  if (bag && typeof bag === 'object') for (const k of MANIFEST_KEYS) push(bag[k]);
  return seen;
}

export function createAssets(options = {}) {
  const opts = options || {};
  const platform = opts.platform || {};
  const rng = typeof opts.rng === 'function' ? opts.rng : Math.random;
  const log = typeof opts.log === 'function' ? opts.log : (msg) => {
    if (typeof document !== 'undefined' && typeof CustomEvent === 'function') {
      try { document.dispatchEvent(new CustomEvent('arcademy-log', { detail: { msg: 'assets: ' + msg } })); } catch { /* ignore */ }
    }
  };

  const offlineMode = !!opts.offlineMode;
  const remoteEnabled = !!opts.remoteMediaEnabled && !offlineMode;
  const remoteRatio = Number.isFinite(opts.remoteMediaRatio) ? Math.max(0, Math.min(1, opts.remoteMediaRatio)) : 0;
  const defaultNiches = Array.isArray(opts.niches) ? opts.niches.slice() : null;

  const placeholders = placeholderUrls();
  const localPools = buildLocalPools(resolveManifest(opts), platform);
  // the placeholder floor: only used when a kind has no real local entries
  const localFor = (kind) => (localPools[kind] && localPools[kind].length ? localPools[kind] : placeholders);

  const remotePools = { loop: [], still: [] };     // shared across pools, origin-checked
  const channel = createRemoteChannel({ bridge: opts.bridge, offlineMode, enabled: remoteEnabled, log });
  // Subscribe to the host's 'assets' replies NOW rather than on the first claim.
  // ArcademyHostService answers 'assets-request' with {type:'assets', reqId, urls,
  // done} and may also push an unsolicited batch; bridge.on is multi-subscriber
  // (bridge.js header) so this costs the shell nothing and cannot steal frames.
  if (remoteEnabled && opts.bridge) { try { channel.listen(); } catch { /* ignore */ } }
  const claims = new Set();
  let prewarmed = new Set();
  let disposed = false;

  function absorbRemote(entries) {
    let added = 0;
    for (const e of (entries || [])) {
      const url = e && (e.url || e);
      if (!url || typeof url !== 'string') continue;
      if (isLocalUrl(url)) continue;                       // host already gives us locals
      if (!formatOk(url, platform)) continue;              // iOS mp4-only etc.
      const k = kindOf(e && e.kind ? e : url);
      const arr = remotePools[k] || remotePools.still;
      if (arr.includes(url)) continue;
      arr.push(url);
      if (arr.length > REMOTE_CAP) arr.shift();
      added += 1;
    }
    if (added) log('remote +' + added + ' (loop ' + remotePools.loop.length + ' / still ' + remotePools.still.length + ')');
    return added;
  }

  /**
   * Prewarm: decode-ahead without blocking anything. Silent on failure.
   *
   * BOUNDED, and that is load-bearing (0821, Lost & Found's dense-board pass).
   * A `link rel=preload as=image` is a HIGH-priority fetch: firing one per
   * manifest entry (a claim can ask for 130+ loops) puts the whole library in
   * front of the media the player is actually looking at, and for gifs it also
   * starts decoders the board has no budget for. A short warm queue is the
   * point of a prewarm; a long one is a stampede that races the visible loads.
   */
  const PREWARM_MAX = 12;
  function prewarm(urls) {
    if (typeof document === 'undefined') return;
    let n = 0;
    for (const url of urls) {
      if (n >= PREWARM_MAX) break;
      if (!url || prewarmed.has(url)) continue;
      prewarmed.add(url);
      n += 1;
      try {
        const head = document.head || document.documentElement;
        if (head) {
          const link = document.createElement('link');
          link.rel = 'preload';
          link.as = /\.(mp4|webm|m4v)(\?|#|$)/i.test(url) ? 'video' : 'image';
          // a warm-up must never outrank what is already on screen
          try { link.fetchPriority = 'low'; } catch { /* older engines */ }
          link.href = url;
          head.appendChild(link);
          continue;
        }
      } catch { /* fall through to Image() */ }
      try { const img = new Image(); img.src = url; } catch { /* ignore */ }
    }
  }

  /* ==========================================================================
   * THE DOOR'S SEAM (SORT). Four reads and two writes, all additive.
   *
   * The host projects the pickable world on `init.settings` and the shell hands
   * it to us; `catalog()` is the sanitized view of it, so the door never parses
   * a host bag itself and a field the host has not shipped yet is an empty list
   * rather than a crash. `probeSub` / `removeLibrarySub` are the two writes, and
   * `onLibrary` is how the page hears about a change made anywhere else (the
   * Assets tab, the FYP popover, another probe) - the host pushes the whole
   * fresh library and we re-emit it.
   * ======================================================================= */
  const bag = (opts.settings && typeof opts.settings === 'object') ? opts.settings : {};
  const pickFirst = (...vals) => { for (const v of vals) if (v != null) return v; return null; };

  function sanitizeCatalogRows(list) {
    const out = [];
    for (const e of (Array.isArray(list) ? list : [])) {
      if (!e || typeof e !== 'object') continue;
      const id = typeof e.id === 'string' ? e.id : '';
      if (!id) continue;
      out.push(Object.freeze({
        id,
        label: typeof e.label === 'string' ? e.label : id,
        subs: Array.isArray(e.subs) ? e.subs.filter((s) => typeof s === 'string' && s) : [],
      }));
    }
    return out;
  }

  function sanitizeLibrary(list) {
    const out = [];
    const seen = new Set();
    for (const e of (Array.isArray(list) ? list : [])) {
      const name = typeof e === 'string' ? e : (e && typeof e.name === 'string' ? e.name : '');
      if (!name) continue;
      const key = name.toLowerCase();
      if (seen.has(key)) continue;                 // the library is case-insensitively unique
      seen.add(key);
      out.push(Object.freeze({
        name,
        ok: e && typeof e === 'object' && e.ok != null ? !!e.ok : true,
        videoCount: Math.max(0, (e && e.videoCount) | 0),
        stillOnly: !!(e && e.stillOnly),
      }));
    }
    return out;
  }

  function sanitizeFolders(list) {
    const out = [];
    for (const e of (Array.isArray(list) ? list : [])) {
      if (!e || typeof e !== 'object') continue;
      const path = typeof e.path === 'string' ? e.path.replace(/\\/g, '/') : '';
      if (!path) continue;
      out.push(Object.freeze({
        path,
        gifs: Math.max(0, e.gifs | 0),
        stills: Math.max(0, e.stills | 0),
        videos: Math.max(0, e.videos | 0),
      }));
    }
    return out;
  }

  function sanitizePresets(list) {
    const out = [];
    for (const e of (Array.isArray(list) ? list : [])) {
      if (!e || typeof e !== 'object') continue;
      const id = e.id == null ? '' : String(e.id);
      if (!id) continue;
      out.push(Object.freeze({ id, name: typeof e.name === 'string' ? e.name : id }));
    }
    return out;
  }

  const remoteCatalog = sanitizeCatalogRows(pickFirst(opts.remoteCatalog, bag.remoteCatalog));
  const localFolders = sanitizeFolders(pickFirst(opts.localFolders, bag.localFolders));
  const assetPresets = sanitizePresets(pickFirst(opts.assetPresets, bag.assetPresets));
  const remoteConsent = !!pickFirst(opts.remoteConsent, bag.remoteConsent, false);
  const mediaSource = String(pickFirst(opts.mediaSource, bag.mediaSource, '') || '');
  let subLibrary = sanitizeLibrary(pickFirst(opts.subLibrary, bag.subLibrary));

  const libraryCbs = new Set();
  const probes = new Map();          // reqId -> {resolve, timer}
  let probeSeq = 0;
  const PROBE_TIMEOUT_MS = 15000;    // a probe is a network round trip on the HOST
  const LIBRARY_ECHO_MS = 4000;      // how long a remove waits for the host's push

  function emitLibrary() {
    const view = subLibrary.slice();
    for (const fn of [...libraryCbs]) { try { fn(view); } catch { /* a bad listener never kills the seam */ } }
  }

  function onLibraryFrame(msg) {
    const m = (msg && msg.detail) || msg;
    if (!m) return;
    if (!Array.isArray(m.subLibrary)) return;
    subLibrary = sanitizeLibrary(m.subLibrary);
    log('library push: ' + subLibrary.length + ' subs');
    emitLibrary();
  }

  function onSubProbeFrame(msg) {
    const m = (msg && msg.detail) || msg;
    if (!m || !m.reqId) return;
    const rec = probes.get(m.reqId);
    if (!rec) return;                        // a stale/foreign verdict is not ours
    probes.delete(m.reqId);
    try { clearTimeout(rec.timer); } catch { /* noop */ }
    const row = {
      name: typeof m.name === 'string' ? m.name : rec.name,
      ok: !!m.ok,
      videoCount: Math.max(0, m.videoCount | 0),
      stillOnly: !!m.stillOnly,
    };
    /* On an OK verdict the host has already added the sub to the library and is
     * pushing the fresh list; folding it in here as well means the door can act
     * on the verdict without waiting for a second frame. */
    if (row.ok && !subLibrary.some((s) => s.name.toLowerCase() === row.name.toLowerCase())) {
      subLibrary = subLibrary.concat([Object.freeze(row)]);
      emitLibrary();
    }
    rec.resolve(row);
  }

  if (opts.bridge) {
    try { channel.subscribe('library', onLibraryFrame); } catch { /* ignore */ }
    try { channel.subscribe('sub-probe', onSubProbeFrame); } catch { /* ignore */ }
  }

  /**
   * claim(spec) -> Promise<pool>
   * spec = { loops, targets, stills, canvasSafe, niches }
   * Resolves as soon as the LOCAL side is ready (immediately); the remote request
   * is in flight and tops the pool up as batches land.
   */
  async function claim(spec = {}) {
    const canvasSafe = !!spec.canvasSafe;
    const want = {
      loop: Math.max(0, spec.loops | 0),
      still: Math.max(0, spec.stills | 0),
      target: Math.max(0, spec.targets | 0),
    };
    const niches = Array.isArray(spec.niches) ? spec.niches.slice() : defaultNiches;
    const cursors = { loop: 0, still: 0, target: 0 };
    /* The remote pools walk a cursor exactly like the local ones. A uniform
     * random pick WITH replacement (the old shape) re-served the same url draw
     * after draw whenever the pool was young - and under an online-only source
     * the local side is the placeholder floor, so EVERY draw is a remote draw
     * and a whole board could dress itself in one clip. */
    const remoteCursors = { loop: 0, still: 0 };
    let released = false;
    let reqIds = [];

    // --- the local side, ready NOW -----------------------------------------
    const localLoops = localFor('loop').slice();
    const localStills = localFor('still').slice();
    // targets come out of the still pool but keep their own cursor so a hunt
    // target is never the tile the decoys are drawing from in the same frame
    const localTargets = localStills.slice().reverse();

    // A HANDFUL of each kind, not the whole manifest: see prewarm()'s header.
    // The first draws are what this is for; everything after them is dressed
    // progressively by the game anyway.
    prewarm(localLoops.slice(0, Math.min(6, Math.max(4, want.loop)))
      .concat(localStills.slice(0, Math.min(6, Math.max(4, want.still + want.target)))));

    // --- the remote side, if the gate is open ------------------------------
    // The host's contract is "whatever is buffered NOW, and ask again after
    // every reply" (ArcademyHostService assets-request header): a single ask on
    // a cold buffer lands empty, its async batch arrives AFTER the first dress,
    // and the sibling kind's ask is dropped by the host's single-flight latch.
    // So each kind keeps asking (bounded, backed off) until its pool covers the
    // spec or the asks run out - and onUpdate() below lets a game re-dress
    // placeholder tiles as media actually lands.
    const RETRY_MS = 1500;
    const MAX_ASKS = 8;
    const retryTimers = new Set();
    const updateCbs = new Set();
    function notifyUpdate() {
      for (const fn of [...updateCbs]) { try { fn(); } catch { /* a bad listener never kills the pool */ } }
    }
    function askRemote(kind, count, attempt) {
      if (released || disposed) return;
      const id = channel.request({
        kind, count, niches,
        onBatch: (entries) => {
          if (released || disposed) return;
          if (absorbRemote(entries)) {
            prewarm((remotePools[kind] || []).slice(-4));
            notifyUpdate();
          }
          if ((remotePools[kind] || []).length < count && attempt < MAX_ASKS) {
            const t = setTimeout(() => { retryTimers.delete(t); askRemote(kind, count, attempt + 1); }, RETRY_MS * Math.max(1, attempt));
            retryTimers.add(t);
          }
        },
      });
      if (id) reqIds.push(id);
    }
    if (!canvasSafe && remoteEnabled && remoteRatio > 0) {
      // The goal per kind may exceed the host's 24-per-reply batch cap - the
      // ask-again loop tops the pool up across replies, up to REMOTE_CAP.
      if (want.loop) askRemote('loop', Math.min(REMOTE_CAP, Math.max(4, want.loop)), 1);
      if (want.still + want.target) askRemote('still', Math.min(REMOTE_CAP, Math.max(4, want.still + want.target)), 1);
    } else if (canvasSafe && remoteEnabled) {
      log('canvasSafe claim: local-only pool (CORS two-pool law)');
    }

    function drawLocal(kind) {
      const list = kind === 'loop' ? localLoops : (kind === 'target' ? localTargets : localStills);
      if (!list.length) return placeholders[Math.floor(rng() * placeholders.length)] || null;
      const i = cursors[kind] % list.length;
      cursors[kind] += 1;
      // a deterministic walk with a seeded jump keeps repeats far apart without
      // ever risking "the same tile twice in a row" on a tiny local pool
      const jump = list.length > 2 ? Math.floor(rng() * (list.length - 1)) : 0;
      return list[(i + jump) % list.length];
    }

    const pool = {
      spec: { loops: want.loop, stills: want.still, targets: want.target, canvasSafe, niches: niches || null },
      /**
       * next(kind) -> { url, remote }
       * kind: 'loop' | 'still' | 'target' (default 'still'). NEVER blocks, never
       * returns null — the placeholder floor guarantees a url.
       */
      next(kind) {
        const k = (kind === 'loop' || kind === 'gif') ? 'loop' : (kind === 'target' ? 'target' : 'still');
        if (released) return { url: placeholders[0] || null, remote: false };
        const remoteKind = k === 'target' ? 'still' : k;
        // The ratio is a MIX dial, not a veto: on the placeholder floor (no real
        // local media of this kind) there is nothing to mix WITH, so the remote
        // pool serves every draw it can cover.
        const bareLocal = !(remoteKind === 'loop' ? localPools.loop.length : localPools.still.length);
        if (wantRemote(bareLocal ? 1 : remoteRatio, rng(), (remotePools[remoteKind] || []).length > 0, canvasSafe)) {
          const list = remotePools[remoteKind];
          const i = remoteCursors[remoteKind] % list.length;
          remoteCursors[remoteKind] += 1;
          const jump = list.length > 2 ? Math.floor(rng() * (list.length - 1)) : 0;
          const url = list[(i + jump) % list.length];
          if (url && (!canvasSafe || isLocalUrl(url))) return { url, remote: true };
        }
        return { url: drawLocal(k), remote: false };
      },
      /** How many candidates the pool can actually serve right now. */
      stats() {
        return {
          local: { loop: localLoops.length, still: localStills.length },
          remote: { loop: remotePools.loop.length, still: remotePools.still.length },
          canvasSafe, remoteEnabled, remoteRatio, offlineMode,
          placeholderFloor: !localPools.loop.length && !localPools.still.length,
        };
      },
      /** Subscribe to "the pool just grew" (a remote batch landed). Returns an
       *  unsubscribe; every subscription dies with release(). */
      onUpdate(fn) {
        if (typeof fn !== 'function' || released) return () => {};
        updateCbs.add(fn);
        return () => updateCbs.delete(fn);
      },
      release() {
        if (released) return;
        released = true;
        reqIds = [];
        updateCbs.clear();
        for (const t of retryTimers) clearTimeout(t);
        retryTimers.clear();
        claims.delete(pool);
      },
    };
    claims.add(pool);
    return pool;
  }

  /* ==========================================================================
   * claimTagged(spec) -> Promise<taggedPool>       (SORT; see ./tagged.js)
   * The pools live in their own module because their rules are nothing like
   * claim()'s: two cursors, a seeded dry re-serve, a resolve that is allowed to
   * give up, and rows whose TAG is the game's only source of truth.
   * ======================================================================= */
  const taggedPools = new Set();

  function claimTagged(spec = {}) {
    if (disposed) return Promise.resolve(null);
    return createTaggedPool({
      spec,
      channel,
      platform,
      prewarm,
      log,
      /* The remote gate is the app's, not the door's: with remote media off (or
       * OfflineMode on) a remote source row simply never asks, the tag lands
       * empty, and the door refuses to start on pool.empty(). A LOCAL row is
       * never gated - a folder on disk is not a network call. */
      remoteAllowed: remoteEnabled,
    }).then((pool) => {
      if (pool) {
        taggedPools.add(pool);
        const dispose = pool.dispose;
        pool.dispose = () => { taggedPools.delete(pool); dispose(); };
      }
      return pool;
    });
  }

  return {
    claim,
    claimTagged,
    /** The pickable world, as projected on init.settings. Never null fields. */
    catalog() {
      return {
        remoteCatalog: remoteCatalog.slice(),
        subLibrary: subLibrary.slice(),
        localFolders: localFolders.slice(),
        assetPresets: assetPresets.slice(),
        remoteConsent,
        remoteMediaEnabled: remoteEnabled,
        offlineMode,
        mediaSource,
      };
    },
    /**
     * Ask the host to verify a sub. NEVER rejects: a silent host resolves
     * `{ok:false, timeout:true}` after PROBE_TIMEOUT_MS, which the door shows as
     * "not found" - a spinner that never stops is worse than a wrong no.
     */
    probeSub(name) {
      const clean = String(name == null ? '' : name).trim();
      if (!clean) return Promise.resolve({ name: '', ok: false, videoCount: 0, stillOnly: false });
      if (!opts.bridge || disposed) {
        return Promise.resolve({ name: clean, ok: false, videoCount: 0, stillOnly: false, offline: true });
      }
      probeSeq += 1;
      const reqId = 'ae-probe-' + probeSeq + '-' + Math.floor(Date.now() % 1e7);
      return new Promise((resolve) => {
        const timer = setTimeout(() => {
          probes.delete(reqId);
          resolve({ name: clean, ok: false, videoCount: 0, stillOnly: false, timeout: true });
        }, PROBE_TIMEOUT_MS);
        probes.set(reqId, { name: clean, resolve, timer });
        if (!channel.sendRaw('probe-sub', { reqId, name: clean })) {
          probes.delete(reqId);
          try { clearTimeout(timer); } catch { /* noop */ }
          resolve({ name: clean, ok: false, videoCount: 0, stillOnly: false, offline: true });
        }
      });
    },
    /**
     * Delete a sub from the player's library - the host also drops its verdict
     * and its feed selection ("added once, X everywhere"). Resolves on the
     * host's fresh `library` push, or after LIBRARY_ECHO_MS either way: the
     * door's pill has already gone and a promise that never settled would leave
     * it spinning.
     */
    removeLibrarySub(name) {
      const clean = String(name == null ? '' : name).trim();
      if (!clean || !opts.bridge || disposed) return Promise.resolve();
      return new Promise((resolve) => {
        let done = false;
        const finish = () => { if (done) return; done = true; try { off(); } catch { /* noop */ } resolve(); };
        const off = channel.subscribe('library', () => setTimeout(finish, 0));
        setTimeout(finish, LIBRARY_ECHO_MS);
        /* Optimistic locally as well: the host is the truth, but the pill the
         * player just clicked must not sit there until a frame comes back. */
        subLibrary = subLibrary.filter((s) => s.name.toLowerCase() !== clean.toLowerCase());
        emitLibrary();
        channel.sendRaw('library-remove', { name: clean });
      });
    },
    /** Subscribe to library pushes. Returns an unsubscribe. */
    onLibrary(fn) {
      if (typeof fn !== 'function') return () => {};
      libraryCbs.add(fn);
      return () => libraryCbs.delete(fn);
    },
    /** The shell may hand host 'assets' replies straight in (bridge-agnostic). */
    receive: (msg) => channel.receive(msg),
    /** Absorb urls the shell already has (e.g. an init-time remote batch). */
    absorb: (entries) => absorbRemote(entries),
    stats() {
      return {
        local: { loop: localPools.loop.length, still: localPools.still.length },
        remote: { loop: remotePools.loop.length, still: remotePools.still.length },
        placeholders: placeholders.length,
        // true = the host shipped no local inventory and every draw is a bundled
        // tile. The shell surfaces this as its one asset-seam diagnostic.
        placeholderFloor: !localPools.loop.length && !localPools.still.length,
        claims: claims.size,
        remoteEnabled, remoteRatio, offlineMode,
        /* the SORT seam, read-only: live tagged pools and the pickable world */
        taggedPools: taggedPools.size,
        perSourceMinDefault: TAGGED.PER_SOURCE_MIN,
        catalogNiches: remoteCatalog.length,
        librarySubs: subLibrary.length,
        folders: localFolders.length,
        presets: assetPresets.length,
        remoteConsent, mediaSource,
      };
    },
    dispose() {
      disposed = true;
      for (const p of [...claims]) p.release();
      for (const p of [...taggedPools]) { try { p.dispose(); } catch { /* ignore */ } }
      taggedPools.clear();
      for (const rec of [...probes.values()]) {
        try { clearTimeout(rec.timer); } catch { /* noop */ }
        try { rec.resolve({ name: rec.name, ok: false, videoCount: 0, stillOnly: false, offline: true }); } catch { /* noop */ }
      }
      probes.clear();
      libraryCbs.clear();
      channel.dispose();
      prewarmed = new Set();
    },
  };
}

export default createAssets;
