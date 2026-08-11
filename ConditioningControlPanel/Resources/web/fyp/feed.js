// Feed engine — selection, compositions and the sliding page window.
// Port of the mobile reel's foryou.tsx selection/composition layer (no DOM here;
// surfaces.js renders what this produces).
//
// Vocabulary (same as mobile):
//   Candidate   — one addressable section of a video (or a whole GIF): the unit
//                 selection draws from.
//   Cut         — one media pick with its playback window: the content of a tile.
//   Tile        — a placed cut: fractional rect + audio/advance/blur flags.
//   Composition — one feed page: a keyed list of tiles.

import {
  segId, segmentCount, isLandscapeDims, randomClipLenSec,
  W_BASE, W_BOOST_MAX, W_EXPLORE_BONUS, WHOLE_VIDEO_MAX_SEC,
} from './segments.js';
import * as stats from './stats.js';

export const PAGE_SIZE = 8;
// Feed window: appending trims the head once the list exceeds this, so an endless
// session holds a bounded working set instead of growing forever.
export const MAX_COMPS = 6 * PAGE_SIZE;
const HISTORY_KEYS_MAX = 512; // hard ceiling on distinct per-tile history stacks
const HISTORY_MAX = 20;       // per-tile swipe-back depth
const ASSET_COOLDOWN = 2;     // don't show 3+ clips of the same video in a row
const SAME_ASSET_PENALTY = 0.25;
const MOSAIC_MIN_TILES = 2;
const MOSAIC_MAX_TILES = 4;
const RING_CAP = 128;

let surfaceSeq = 0;
const nextSurfaceKey = () => `s${surfaceSeq++}`;

// Mixed-source share: the probability that any given pick draws from the ONLINE
// candidate bucket instead of the local one (0 = library only, 1 = online only).
// Applied at pick time — not by rationing the pool — because local videos expand
// into many segments while online clips are one each, so pool-entry ratios would
// not match what the user actually sees. Module-level: one feed per page.
let mixRemoteShare = 0;

function weightFor(id) {
  return W_BASE + W_BOOST_MAX * stats.segmentScore(id) + W_EXPLORE_BONUS / (1 + stats.segmentPlays(id));
}

function segCooldownFor(n) {
  return Math.max(1, Math.min(12, Math.floor(n * 0.5)));
}

function remember(ring, id) {
  ring.push(id);
  if (ring.length > RING_CAP) ring.splice(0, ring.length - RING_CAP);
}

/** Expand active videos + GIFs into the flat candidate segment set. */
function enumerateSegments(assets) {
  const out = [];
  for (const a of assets) {
    const landscape = isLandscapeDims(a.width, a.height);
    const remote = a.origin === 'online';
    if (a.type === 'gif') {
      // A GIF is one whole-item segment: no timeline, no grid.
      out.push({ asset: a, assetId: a.id, k: 0, segId: segId(a.id, 0), durKnown: true, landscape, remote });
      continue;
    }
    const durSec = a.durationMs != null ? a.durationMs / 1000 : undefined;
    if (durSec == null) {
      // Cold: duration not probed yet — one placeholder segment, seed-window start.
      out.push({ asset: a, assetId: a.id, k: 0, segId: segId(a.id, 0), durKnown: false, landscape, remote });
    } else if (durSec <= WHOLE_VIDEO_MAX_SEC) {
      out.push({ asset: a, assetId: a.id, k: 0, segId: segId(a.id, 0), durKnown: true, landscape, remote });
    } else {
      const n = segmentCount(durSec);
      for (let k = 0; k < n; k++) {
        out.push({ asset: a, assetId: a.id, k, segId: segId(a.id, k), durKnown: true, landscape, remote });
      }
    }
  }
  return out;
}

/** Fallback when everything is on cooldown (tiny pack): least-recently-shown. */
function leastRecentlyUsed(candidates, ring) {
  let best = candidates[0];
  let bestIdx = ring.lastIndexOf(best.segId); // -1 (never shown) wins
  for (let i = 1; i < candidates.length; i++) {
    const idx = ring.lastIndexOf(candidates[i].segId);
    if (idx < bestIdx) { best = candidates[i]; bestIdx = idx; }
  }
  return best;
}

/**
 * One weighted-proportional pick. Weight blends an exploration floor, an
 * optimism-under-uncertainty term and a capped retention boost; recency and the
 * exclusion set zero candidates out. O(N); LRU fallback when all on cooldown.
 */
function weightedPick(pool, ring, segCooldown, exclude) {
  if (pool.length === 0) return null;
  const lastSeg = ring.length ? ring[ring.length - 1] : null;
  const lastAsset = lastSeg ? lastSeg.slice(0, lastSeg.indexOf(':')) : null;
  const recentSegs = new Set(ring.slice(-segCooldown));
  const recentAssets = new Set(ring.slice(-ASSET_COOLDOWN).map((s) => s.slice(0, s.indexOf(':'))));
  let total = 0;
  const eff = pool.map((c) => {
    let w = weightFor(c.segId);
    if (exclude?.has(c.segId)) w = 0;
    else if (recentSegs.has(c.segId)) w = 0;
    else if (recentAssets.has(c.assetId)) w = 0;
    else if (c.assetId === lastAsset) w *= SAME_ASSET_PENALTY;
    total += w;
    return w;
  });
  if (total > 0) {
    let r = Math.random() * total;
    let idx = 0;
    for (; idx < pool.length - 1; idx++) {
      r -= eff[idx];
      if (r <= 0) break;
    }
    return pool[idx];
  }
  const usable = exclude ? pool.filter((c) => !exclude.has(c.segId)) : pool;
  return usable.length ? leastRecentlyUsed(usable, ring) : null;
}

/**
 * A weighted pick that honours the source mix: roll which bucket (online/local)
 * this pick should come from, try there first, and fall back to the whole pool
 * so a starved bucket (online buffer still loading, tiny library) never blanks
 * a tile. Every fresh-pick site routes through here.
 */
function pickWithMix(pool, ring, segCooldown, exclude) {
  if (mixRemoteShare > 0 && mixRemoteShare < 1) {
    const wantRemote = Math.random() < mixRemoteShare;
    const bucket = pool.filter((c) => c.remote === wantRemote);
    if (bucket.length) {
      const pick = weightedPick(bucket, ring, segCooldown, exclude);
      if (pick) return pick;
    }
  }
  return weightedPick(pool, ring, segCooldown, exclude);
}

function toCut(pick) {
  return {
    surfaceKey: nextSurfaceKey(),
    asset: pick.asset,
    k: pick.k,
    segId: pick.segId,
    durKnown: pick.durKnown,
    lenSec: randomClipLenSec(),
    seed: Math.random(),
  };
}

/** Equal horizontal band i of n (top-to-bottom stack rects). */
function bandRect(i, n) {
  return { x: 0, y: i / n, w: 1, h: 1 / n };
}

/**
 * Partition the unit rect into n cells by recursively splitting the largest-area
 * cell across its longer *pixel* side (soft ratio in [0.36, 0.64]). Irregular,
 * non-symmetric quilt. `aspect` = page width/height, so cells split by real
 * proportions, not fractions.
 */
function splitRects(n, aspect) {
  const rects = [{ x: 0, y: 0, w: 1, h: 1 }];
  while (rects.length < n) {
    let idx = 0;
    let bestArea = -1;
    for (let i = 0; i < rects.length; i++) {
      const a = rects[i].w * rects[i].h;
      if (a > bestArea) { bestArea = a; idx = i; }
    }
    const r = rects[idx];
    const ratio = 0.36 + Math.random() * 0.28;
    const splitVertical = r.w * aspect >= r.h; // wider than tall -> left/right
    const a = splitVertical
      ? { x: r.x, y: r.y, w: r.w * ratio, h: r.h }
      : { x: r.x, y: r.y, w: r.w, h: r.h * ratio };
    const b = splitVertical
      ? { x: r.x + r.w * ratio, y: r.y, w: r.w * (1 - ratio), h: r.h }
      : { x: r.x, y: r.y + r.h * ratio, w: r.w, h: r.h * (1 - ratio) };
    rects.splice(idx, 1, a, b);
  }
  return rects;
}

/** Landscape stack (duo/trio) or a single portrait / lone-landscape tile. */
function buildLeadTiles(lead, rows, landscapePool, ring, segCooldown) {
  if (!lead.landscape) {
    return [{
      cut: toCut(lead),
      rect: { x: 0, y: 0, w: 1, h: 1 },
      audio: lead.asset.type === 'video',
      advances: true,
      blurred: false,
      fit: 'cover',
    }];
  }
  const picks = [lead];
  const exclude = new Set([lead.segId]);
  for (let r = 1; r < rows; r++) {
    const mate = pickWithMix(landscapePool, ring, segCooldown, exclude);
    if (!mate) break;
    picks.push(mate);
    exclude.add(mate.segId);
    remember(ring, mate.segId);
  }
  const firstVideo = picks.findIndex((p) => p.asset.type === 'video');
  const lone = picks.length === 1; // couldn't fill the stack -> letterbox
  return picks.map((p, i) => ({
    cut: toCut(p),
    rect: bandRect(i, picks.length),
    audio: i === firstVideo,
    advances: i === 0,
    blurred: !lone && i !== 0,
    fit: lone ? 'contain' : 'cover',
  }));
}

/** One irregular mosaic: orientation-matched picks into split cells. */
function buildMosaicTiles(candidates, ring, segCooldown, aspect) {
  const n = MOSAIC_MIN_TILES + Math.floor(Math.random() * (MOSAIC_MAX_TILES - MOSAIC_MIN_TILES + 1));
  const rects = splitRects(n, aspect);
  const exclude = new Set();
  const tiles = [];
  for (const rect of rects) {
    const cellAspect = (rect.w / rect.h) * aspect;
    const wantLandscape = cellAspect >= 1;
    const oriented = candidates.filter((c) => c.landscape === wantLandscape);
    const pool = oriented.length ? oriented : candidates;
    // Prefer fresh + orientation-matched; widen to the full pool; only for a pack
    // too small to fill every cell, allow a repeat rather than a black gap.
    const pick =
      pickWithMix(pool, ring, segCooldown, exclude) ??
      weightedPick(candidates, ring, segCooldown, exclude) ??
      weightedPick(candidates, ring, segCooldown, null);
    if (!pick) continue;
    exclude.add(pick.segId);
    remember(ring, pick.segId);
    tiles.push({ cut: toCut(pick), rect, audio: false, advances: false, blurred: false, fit: 'cover' });
  }
  // Audio follows the largest video cell (mosaics have no single "top").
  let audioIdx = -1;
  let audioArea = -1;
  tiles.forEach((t, i) => {
    if (t.cut.asset.type !== 'video') return;
    const area = t.rect.w * t.rect.h;
    if (area > audioArea) { audioArea = area; audioIdx = i; }
  });
  if (audioIdx >= 0) tiles[audioIdx] = { ...tiles[audioIdx], audio: true };
  return tiles;
}

/**
 * The feed: owns the asset pool, candidates, recency ring, composition window,
 * per-tile swipe history and the monotonic comp-key sequence.
 */
export function createFeed(getAspect) {
  let assets = [];
  let includeGifs = true;
  let layout = 'duo';
  let candidates = [];
  let poolKey = '';
  const recent = { ring: [] };
  let comps = [];
  let compSeq = 0;
  const history = new Map(); // `${compKey}:${tileIndex}` -> Cut[]

  function activePool() {
    return assets.filter((a) => a.type === 'video' || (includeGifs && a.type === 'gif'));
  }

  function rebuildCandidates() {
    candidates = enumerateSegments(activePool());
  }

  function computePoolKey() {
    return activePool().map((a) => a.id).sort().join('|');
  }

  function makeCompositions(count) {
    const out = [];
    if (candidates.length === 0) return out;
    const segCooldown = segCooldownFor(candidates.length);
    const landscapePool = candidates.filter((c) => c.landscape);
    const aspect = getAspect();
    for (let i = 0; i < count; i++) {
      const key = `cut-${compSeq++}`;
      let tiles;
      if (layout === 'random') {
        tiles = buildMosaicTiles(candidates, recent.ring, segCooldown, aspect);
      } else {
        const lead = pickWithMix(candidates, recent.ring, segCooldown, null);
        if (!lead) break;
        remember(recent.ring, lead.segId);
        tiles = buildLeadTiles(lead, layout === 'trio' ? 3 : 2, landscapePool, recent.ring, segCooldown);
      }
      if (!tiles.length) break;
      out.push({ key, layout, gen: 0, tiles });
    }
    return out;
  }

  /**
   * Fresh weighted pick for one tile: orientation want, page-wide exclusion, and
   * the outgoing cut pushed onto the tile's history stack. Shared by the 'next'
   * direction and by 'back' when there is no history to replay.
   */
  function freshSwap(comp, tileIndex, hKey) {
    const tile = comp.tiles[tileIndex];
    const stacked = comp.layout !== 'random' && comp.tiles.length > 1;
    let want;
    if (stacked) want = true; // a stacked slot never falls back to portrait
    else if (comp.layout === 'random') want = (tile.rect.w / tile.rect.h) * getAspect() >= 1;
    else want = isLandscapeDims(tile.cut.asset.width, tile.cut.asset.height);
    let pickPool = candidates.filter((c) => c.landscape === want);
    if (!stacked && pickPool.length < 2) pickPool = candidates;
    const exclude = new Set(comp.tiles.map((t) => t.cut.segId)); // no dupes on the page
    exclude.add(tile.cut.segId);
    const pick = pickWithMix(pickPool, recent.ring, segCooldownFor(candidates.length), exclude);
    if (!pick) return null;

    let stack = history.get(hKey);
    if (!stack) {
      if (history.size >= HISTORY_KEYS_MAX) {
        // evict the oldest stack (Map preserves insertion order)
        const oldest = history.keys().next().value;
        if (oldest != null) history.delete(oldest);
      }
      stack = [];
      history.set(hKey, stack);
    }
    stack.push(tile.cut);
    if (stack.length > HISTORY_MAX) stack.splice(0, stack.length - HISTORY_MAX);

    remember(recent.ring, pick.segId);
    const cut = toCut(pick);
    comp.tiles[tileIndex] = { ...tile, cut };
    return cut;
  }

  return {
    get comps() { return comps; },
    get layout() { return layout; },
    get candidateCount() { return candidates.length; },
    get historySize() { return history.size; },

    /** Returns true when the feed must reset to the top (pool identity or layout
     *  changed). Duration/dimension backfills keep the feed exactly where it is —
     *  and so does a remoteShare change (it only biases future picks). */
    configure(nextAssets, nextIncludeGifs, nextLayout, nextRemoteShare = 0) {
      assets = nextAssets;
      includeGifs = nextIncludeGifs;
      mixRemoteShare = Math.max(0, Math.min(1, Number(nextRemoteShare) || 0));
      rebuildCandidates();
      const nextKey = computePoolKey();
      const reset = nextKey !== poolKey || nextLayout !== layout;
      poolKey = nextKey;
      layout = nextLayout;
      if (reset) {
        recent.ring = [];
        history.clear();
        comps = makeCompositions(PAGE_SIZE);
        // Online ids churn every session and never enter stats (see main.js report),
        // so pruning against LOCAL ids only keeps library stats intact in online mode.
        stats.pruneToAssets(new Set(assets.filter((a) => a.origin !== 'online').map((a) => a.id)));
      }
      return reset;
    },

    /**
     * Grow the pool WITHOUT resetting — the online path's whole point. configure()
     * yanks the feed to the top whenever the pool identity changes, which would be
     * catastrophic mid-scroll every time a remote batch lands; append extends the
     * pool in place and updates the identity key so the next configure() (layout
     * toggle etc.) doesn't read the growth as a brand-new pool. If the feed was
     * empty (online mode waiting on its first batch), the first pages are built
     * here; the caller mounts them.
     */
    append(newAssets) {
      if (!Array.isArray(newAssets) || newAssets.length === 0) return 0;
      const known = new Set(assets.map((a) => a.id));
      const fresh = newAssets.filter((a) => a && a.id && !known.has(a.id));
      if (fresh.length === 0) return 0;
      assets = assets.concat(fresh);
      rebuildCandidates();
      poolKey = computePoolKey();
      if (comps.length === 0) comps = makeCompositions(PAGE_SIZE);
      return fresh.length;
    },

    /** A playback probe filled in duration/dims: refresh candidates in place. */
    noteAssetMeta() {
      rebuildCandidates();
    },

    /** Append a page batch; trim the head past MAX_COMPS. Returns the number of
     *  pages trimmed so the pager can compensate its offset in the same tick. */
    extend() {
      const appended = makeCompositions(PAGE_SIZE);
      comps = comps.concat(appended);
      let excess = comps.length - MAX_COMPS;
      if (excess < 0) excess = 0;
      if (excess > 0) {
        const dead = comps.slice(0, excess);
        comps = comps.slice(excess);
        for (const d of dead) {
          for (const k of history.keys()) {
            if (k.startsWith(d.key + ':')) history.delete(k);
          }
        }
      }
      return { appended: appended.length, trimmed: excess };
    },

    /**
     * Swap one tile's cut. dir 'next' = fresh weighted pick, 'back' = pop the
     * tile's history (replaying the exact same window). A 'back' with nothing
     * left to replay (first item, or a stack of dead assets) is NOT refused: it
     * falls through to a fresh pick, so the gesture always yields content and
     * the caller animates it in whichever direction the user swiped. Returns
     * null only when the pool itself can offer nothing — the surface springs back.
     */
    swapTile(compKey, tileIndex, dir) {
      const comp = comps.find((c) => c.key === compKey);
      const tile = comp?.tiles[tileIndex];
      if (!comp || !tile) return null;
      const hKey = `${compKey}:${tileIndex}`;

      if (dir === 'back') {
        const stack = history.get(hKey);
        const alive = new Set(candidates.map((c) => c.assetId));
        let popped = null;
        while (stack && stack.length) {
          const c = stack.pop();
          if (alive.has(c.asset.id)) { popped = c; break; } // skip cuts whose asset left the pool
        }
        if (!popped) return freshSwap(comp, tileIndex, hKey); // nothing to go back to
        // Same segId/lenSec/seed => the exact same window replays; fresh surfaceKey
        // so the old surface unmounts (reporting its dwell).
        const restored = { ...popped, surfaceKey: nextSurfaceKey() };
        remember(recent.ring, restored.segId);
        comp.tiles[tileIndex] = { ...tile, cut: restored };
        return restored;
      }

      return freshSwap(comp, tileIndex, hKey);
    },

    /** Re-compose a random-layout page in place (fresh layout + clips). Returns
     *  the new tiles, or null when the pool is empty. Drops the page's history. */
    morph(compKey) {
      const comp = comps.find((c) => c.key === compKey);
      if (!comp || comp.layout !== 'random' || candidates.length === 0) return null;
      const tiles = buildMosaicTiles(candidates, recent.ring, segCooldownFor(candidates.length), getAspect());
      if (!tiles.length) return null;
      for (const k of history.keys()) {
        if (k.startsWith(compKey + ':')) history.delete(k);
      }
      comp.gen++;
      comp.tiles = tiles;
      return tiles;
    },
  };
}
