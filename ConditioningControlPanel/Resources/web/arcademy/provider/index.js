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
          const url = list[Math.floor(rng() * list.length)];
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

  return {
    claim,
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
      };
    },
    dispose() {
      disposed = true;
      for (const p of [...claims]) p.release();
      channel.dispose();
      prewarmed = new Set();
    },
  };
}

export default createAssets;
