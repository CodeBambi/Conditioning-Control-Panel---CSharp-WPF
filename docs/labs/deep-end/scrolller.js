/* ============================================================================
 * labs/deep-end/scrolller.js - the web teaser's ASSET PROVIDER.
 *
 * Same contract the Arcademy's provider/index.js gives a class
 * (`assets.claim(spec) -> pool; pool.next(kind) -> {url, remote}` - never
 * blocks, never null), fed by the BROWSER fetching Scrolller directly.
 *
 * BRIGHT LINE (owner-locked, same law as the desktop For You feed and the web
 * Intake): api.scrolller.com answers with `access-control-allow-origin: *`,
 * so the page fetches it itself and CC Labs servers never proxy, cache or
 * re-serve a byte of it. Any edit that routes these requests through our
 * infrastructure is a regression, not a refactor.
 *
 * What Scrolller serves: animated content ONLY as silent webm/mp4 (its "GIF"
 * filter carries no .gif at all, just clips + static webp/jpg posters), stills
 * as webp/jpg. Loops therefore come back as video urls; the engine and the
 * Deep End render those through a muted looping <video> (engine/util.js
 * mediaEl), stills through <img>. Nothing here is ever drawn to a canvas, so
 * the CDN's missing CORS headers do not matter.
 *
 * CODEC - mp4 EVERYWHERE, on purpose: every animated post ships both
 * `-mobile.webm` (VP9) and `-mobile.mp4` (H.264) at the same 854x480
 * rendition, and the webm is 10-30% smaller. We take the bigger file anyway.
 * This page keeps up to 16 clips alive at once and is GPU-bound, not
 * bandwidth-bound: plenty of cards ship with no VP9 decode block at all (AMD
 * Polaris RX 4xx/5xx, pre-Skylake Intel, older NVIDIA laptops), so on those
 * machines every VP9 stream is software-decoded on the CPU and the board
 * stutters. H.264 hardware decode is universal. webm is now the fallback for
 * the rare post with no mp4 rendition, nothing more.
 * ==========================================================================*/

export const ENDPOINT = 'https://api.scrolller.com/admin';

/** The desktop FypOnlineCoordinator.Catalog, VERBATIM (same ids, same order,
 *  same subs). Every sub below was existence-checked live on 2026-08-23; ids
 *  are never renamed or removed (saved prefs reference them) and new presets
 *  are appended LAST. Desktop is the authority - hand-sync, never reorder. */
export const NICHES = Object.freeze([
  { key: 'hypno',      label: 'Hypno',       subs: ['EroticHypnosis', 'sissyhypno', 'HypnoGoneWild', 'HypnoHentai'] },
  { key: 'bimbo',      label: 'Bimbo',       subs: ['bimbo', 'bimbofication', 'bimbofetish'] },
  { key: 'sissy',      label: 'Sissy',       subs: ['Sissies', 'sissyhypno', 'sissycaptions'] },
  { key: 'hentai',     label: 'Hentai',      subs: ['hentai', 'rule34', 'nsfwanimegifs', 'ecchi'] },
  { key: 'censored',   label: 'Censored',    subs: ['censoredporn', 'Censored_Porn'] },
  { key: 'bbc',        label: 'BBC',         subs: ['BBCSluts', 'interracial_porn', 'QOS'] },
  { key: 'goon',       label: 'Goon',        subs: ['GOONED', 'GoonCaves', 'edging'] },
  { key: 'amateur',    label: 'Amateur',     subs: ['RealGirls', 'TittyDrop', 'gonewild'] },
  { key: 'relapse',    label: 'Relapse',     subs: ['pornrelapsed', 'stillstraightcaptions'] },
  { key: 'bambisleep', label: 'Bambi Sleep', subs: ['BambiSleep', 'HypnoGoneWild', 'EroticHypnosis'] },
  { key: 'futa',       label: 'Futa',        subs: ['futanari'] },
  { key: 'cosplay',    label: 'Cosplay',     subs: ['nsfwcosplay', 'cosplaygirls', 'CosplayLewd', 'cosplaybutts'] },
  { key: 'beta',       label: 'Beta / SPH',  subs: ['sph', 'SmallPenisHumiliation'] },
]);

const QUERY = `query SubredditQuery($url: String!, $iterator: String, $sortBy: GallerySortBy, $filter: GalleryFilter, $limit: Int!) {
  getSubreddit(data: {url: $url, iterator: $iterator, filter: $filter, limit: $limit, sortBy: $sortBy}) {
    id
    videoCount
    children { iterator items { id mediaSources { url width height isOptimized } } }
  }
}`;

const PAGE_LIMIT = 30;
const MIN_GAP_MS = 1100;          // gallery-dl politeness: ~one request a second
const MAX_PAGES_PER_STREAM = 3;   // (sub x filter) -> at most 90 posts each
const TARGET = { loop: 60, still: 40 };
const POOL_CAP = 160;
/** A loop is a tile face or a burst, never a feature: the SMALL rendition.
 *  A 4K clip per tile would melt a laptop; ~480px is plenty at face size. */
const MAX_LOOP_WIDTH = 640;
const MAX_STILL_WIDTH = 1280;
const MAX_POSTER_WIDTH = 960;

const PLACEHOLDERS = ['ae-ph-1.svg', 'ae-ph-2.svg', 'ae-ph-3.svg', 'ae-ph-4.svg', 'ae-ph-5.svg', 'ae-ph-6.svg']
  .map((f) => {
    try { return new URL('./arc/provider/assets/' + f, import.meta.url).href; } catch (e) { return './arc/provider/assets/' + f; }
  });

const VIDEO_RE = /\.(webm|mp4|m4v)(\?|#|$)/i;
const MP4_RE = /\.mp4(\?|#|$)/i;
const IMAGE_RE = /\.(webp|jpe?g|png|gif)(\?|#|$)/i;
const THUMB_RE = /_thumb/i;

/** Largest rendition at or under maxW (else the smallest), accept = url test. */
function pick(sources, accept, maxW) {
  if (!Array.isArray(sources)) return null;
  const ok = sources.filter((s) => s && typeof s.url === 'string' && accept(s.url) && !THUMB_RE.test(s.url) && Number(s.width) > 0);
  if (!ok.length) return null;
  const fit = ok.filter((s) => Number(s.width) <= maxW);
  const list = fit.length ? fit : ok;
  list.sort((a, b) => (fit.length ? Number(b.width) - Number(a.width) : Number(a.width) - Number(b.width)));
  return list[0];
}

/* ONE politeness gate for the whole module: page fetches and sub probes share
 * the ~1.1s spacing, so a probe can never jump the fetch queue. */
let lastAt = 0;
async function politeWait() {
  const gap = MIN_GAP_MS - (Date.now() - lastAt);
  if (gap > 0) await new Promise((r) => setTimeout(r, gap));
  lastAt = Date.now();
}

/** Existence check for a user-typed sub, on the same gate as the fetches.
 *  -> {ok:false} not found · {ok:true, videoCount} · {ok:false, error} transport. */
export async function probeSub(name) {
  const sub = String(name || '').trim().replace(/^\/?r\//i, '');
  if (!sub) return { ok: false };
  await politeWait();
  try {
    const res = await fetch(ENDPOINT, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        query: QUERY,
        variables: { url: '/r/' + sub, iterator: null, sortBy: 'RANDOM', filter: 'VIDEO', limit: 1 },
        authorization: null,
      }),
      credentials: 'omit',
      mode: 'cors',
    });
    if (!res.ok) return { ok: false, error: 'http-' + res.status };
    const body = await res.json();
    const data = body && body.data && body.data.getSubreddit;
    if (!data) return { ok: false };            // null getSubreddit = no such sub
    return { ok: true, videoCount: Number(data.videoCount) || 0 };
  } catch (e) {
    return { ok: false, error: 'offline' };
  }
}

export function createScrolllerAssets(options = {}) {
  const opts = options || {};
  const subs = Array.isArray(opts.subs) ? [...new Set(opts.subs.filter((s) => typeof s === 'string' && s.trim()))] : [];
  const rng = typeof opts.rng === 'function' ? opts.rng : Math.random;
  const log = typeof opts.log === 'function' ? opts.log : () => {};
  const iosOnlyMp4 = (() => { try { return /iPhone|iPad|iPod/.test(navigator.userAgent || ''); } catch (e) { return false; } })();

  const pools = { loop: [], still: [] };
  const seen = new Set();
  const streams = [];       // {sub, filter, iterator, pages, done}
  const listeners = new Set();
  const controller = (typeof AbortController === 'function') ? new AbortController() : null;
  const recent = { loop: [], still: [] };   // no-immediate-repeat rings
  let inflight = 0;
  let requests = 0;
  let warmed = false;
  let disposed = false;
  let dead = 0;

  function notify() { for (const fn of [...listeners]) { try { fn(); } catch (e) { /* a bad listener never kills the pool */ } } }

  function add(kind, url) {
    if (!url || seen.has(url)) return false;
    seen.add(url);
    const arr = pools[kind];
    arr.push(url);
    if (arr.length > POOL_CAP) arr.shift();
    return true;
  }

  function acceptVideo(url) {
    if (!VIDEO_RE.test(url)) return false;
    if (iosOnlyMp4 && !MP4_RE.test(url)) return false;
    return true;
  }

  /** mp4 first on every platform (see CODEC in the header): H.264 decodes in
   *  hardware everywhere, VP9 does not. webm only when a post has no mp4. */
  function pickLoop(sources) {
    return pick(sources, (u) => acceptVideo(u) && MP4_RE.test(u), MAX_LOOP_WIDTH)
      || pick(sources, acceptVideo, MAX_LOOP_WIDTH);
  }

  function absorb(items, filter) {
    let n = 0;
    for (const item of (items || [])) {
      const sources = item && item.mediaSources;
      if (!Array.isArray(sources)) continue;
      if (filter === 'GIF') {
        const v = pickLoop(sources);
        if (v && add('loop', v.url)) n += 1;
        // every clip carries a poster; a poster is a perfectly good still
        const p = pick(sources, (u) => IMAGE_RE.test(u), MAX_POSTER_WIDTH);
        if (p && add('still', p.url)) n += 1;
      } else {
        const s = pick(sources, (u) => IMAGE_RE.test(u), MAX_STILL_WIDTH);
        if (s && add('still', s.url)) n += 1;
      }
    }
    return n;
  }

  async function fetchPage(stream) {
    if (disposed || stream.done) return 0;
    await politeWait();
    if (disposed) return 0;
    inflight += 1;
    requests += 1;
    try {
      const res = await fetch(ENDPOINT, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          query: QUERY,
          variables: { url: '/r/' + stream.sub, iterator: stream.iterator || null, sortBy: 'RANDOM', filter: stream.filter, limit: PAGE_LIMIT },
          authorization: null,
        }),
        signal: controller ? controller.signal : undefined,
        credentials: 'omit',          // a third party never gets a cookie from us
        mode: 'cors',
      });
      if (!res.ok) { stream.done = true; log('r/' + stream.sub + ' ' + stream.filter + ' HTTP ' + res.status); return 0; }
      const body = await res.json();
      const sub = body && body.data && body.data.getSubreddit;
      if (!sub) { stream.done = true; dead += 1; log('r/' + stream.sub + ' does not resolve on scrolller'); return 0; }
      const items = sub.children && sub.children.items;
      const n = absorb(items, stream.filter);
      stream.pages += 1;
      stream.iterator = (sub.children && sub.children.iterator) || null;
      if (!Array.isArray(items) || items.length === 0 || stream.pages >= MAX_PAGES_PER_STREAM) stream.done = true;
      else if (!stream.iterator) {
        // Drained, not empty. Under RANDOM sort a fresh start walks a DIFFERENT
        // shuffle, so one restart can still surface posts this pass missed;
        // a restarted page that adds nothing new means the sub really is spent.
        if ((stream.restarts || 0) >= 1 && n === 0) stream.done = true;
        else { stream.iterator = null; stream.restarts = (stream.restarts || 0) + 1; }
      }
      log('r/' + stream.sub + ' ' + stream.filter + ' page ' + stream.pages + ': +' + n + ' (loop ' + pools.loop.length + ' / still ' + pools.still.length + ')');
      if (n) notify();
      return n;
    } catch (e) {
      stream.done = true;
      if (!(e && e.name === 'AbortError')) log('r/' + stream.sub + ' ' + stream.filter + ' failed: ' + ((e && e.message) || e));
      return 0;
    } finally { inflight -= 1; }
  }

  function need() {
    return pools.loop.length < TARGET.loop || pools.still.length < TARGET.still;
  }

  /** Kick the fetch. Idempotent; runs the streams round-robin (GIF first so
   *  the faces have loops early) until every stream is done or the pool is
   *  full. Resolves when the sweep ends; nothing awaits it. */
  async function warm() {
    if (warmed || disposed || !subs.length) return;
    warmed = true;
    for (const f of ['GIF', 'PICTURE']) for (const sub of subs) streams.push({ sub, filter: f, iterator: null, pages: 0, done: false });
    // two lanes, each politely paced, so a 2-sub niche lands its first loops
    // inside ~2.5s instead of ~5s
    const lanes = [0, 1].map(async (lane) => {
      for (;;) {
        if (disposed || !need()) return;
        const s = streams.find((x, i) => !x.done && !x.busy && (i % 2) === lane) || streams.find((x) => !x.done && !x.busy);
        if (!s) return;
        s.busy = true;
        try { await fetchPage(s); } finally { s.busy = false; }
      }
    });
    await Promise.all(lanes);
    log('warm done: loop ' + pools.loop.length + ' / still ' + pools.still.length + ' over ' + requests + ' requests' + (dead ? ' (' + dead + ' dead sub)' : ''));
    notify();
  }

  function sweepDone() { return !subs.length || (warmed && streams.length > 0 && streams.every((s) => s.done)); }
  function placeholder() { return PLACEHOLDERS[Math.floor(rng() * PLACEHOLDERS.length)] || PLACEHOLDERS[0] || null; }

  /** A draw that keeps repeats far apart on a small pool: the last
   *  clamp(floor(pool/3), 2, 24) urls of that kind are skipped, so 60 clips
   *  cycle evenly instead of re-serving a face that is still on screen. */
  function drawFrom(kind) {
    const list = pools[kind];
    if (!list.length) return null;
    const ring = Math.max(2, Math.min(24, Math.floor(list.length / 3)));
    const ban = recent[kind];
    if (list.length <= ring) return list[Math.floor(rng() * list.length)] || null;
    const free = list.filter((u) => !ban.includes(u));
    const from = free.length ? free : list;
    const url = from[Math.floor(rng() * from.length)] || null;
    if (url) { ban.push(url); while (ban.length > ring) ban.shift(); }
    return url;
  }

  function claim(spec = {}) {
    warm();
    let released = false;
    const cbs = new Set();
    const pool = {
      spec: { loops: spec.loops | 0, stills: spec.stills | 0, targets: spec.targets | 0, canvasSafe: !!spec.canvasSafe },
      next(kind) {
        const k = (kind === 'loop' || kind === 'gif') ? 'loop' : 'still';
        if (released || disposed) return { url: placeholder(), remote: false };
        // a loop draw with no loops yet is better served by a real still than
        // by a placeholder tile; a still draw never gets a clip
        const url = drawFrom(k) || (k === 'loop' ? drawFrom('still') : null);
        if (url) return { url, remote: true };
        // NOTHING YET. The Deep End freezes a tier's face on first deal and
        // never re-dresses it, so answering a placeholder while the fetch is
        // still in flight would pin that tier to a grey tile for the class;
        // a null url makes it ask again on the next repaint (the engine falls
        // back to its own placeholder floor on null). Once the sweep is DONE
        // with nothing, the bundled tiles are the honest answer.
        if (!sweepDone()) return { url: null, remote: false };
        return { url: placeholder(), remote: false };
      },
      stats() { return { remote: { loop: pools.loop.length, still: pools.still.length }, local: { loop: 0, still: 0 }, placeholderFloor: !pools.loop.length && !pools.still.length }; },
      onUpdate(fn) { if (typeof fn !== 'function' || released) return () => {}; cbs.add(fn); listeners.add(fn); return () => { cbs.delete(fn); listeners.delete(fn); }; },
      release() { released = true; for (const fn of cbs) listeners.delete(fn); cbs.clear(); },
    };
    return Promise.resolve(pool);
  }

  return {
    claim,
    warm,
    subs: subs.slice(),
    stats() { return { loop: pools.loop.length, still: pools.still.length, requests, inflight, warmed, dead, done: sweepDone() }; },
    onUpdate(fn) { if (typeof fn !== 'function') return () => {}; listeners.add(fn); return () => listeners.delete(fn); },
    dispose() {
      disposed = true;
      listeners.clear();
      try { if (controller) controller.abort(); } catch (e) { /* ignore */ }
    },
  };
}

/** No remote media at all: every draw is a bundled placeholder tile. */
export function createPlaceholderAssets(options = {}) {
  const rng = typeof options.rng === 'function' ? options.rng : Math.random;
  const placeholder = () => PLACEHOLDERS[Math.floor(rng() * PLACEHOLDERS.length)] || PLACEHOLDERS[0] || null;
  return {
    claim() {
      return Promise.resolve({
        spec: {}, next() { return { url: placeholder(), remote: false }; },
        stats() { return { remote: { loop: 0, still: 0 }, local: { loop: 0, still: 0 }, placeholderFloor: true }; },
        onUpdate() { return () => {}; }, release() {},
      });
    },
    warm() {}, subs: [],
    stats() { return { loop: 0, still: 0, requests: 0, inflight: 0, warmed: true, dead: 0, done: true }; },
    onUpdate() { return () => {}; },
    dispose() {},
  };
}

export default createScrolllerAssets;
