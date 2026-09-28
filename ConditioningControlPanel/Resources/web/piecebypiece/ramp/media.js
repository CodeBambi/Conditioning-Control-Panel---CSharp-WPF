/* ============================================================================
 * ramp/media.js - where the ramp's pictures come from.
 *
 * TWO sources, ONE interface, so a layer never knows which one it is riding:
 *
 *   createFixtureMedia(list)  dev/test. `list` is the parsed dev/media.json:
 *                             [{ kind:'image'|'gif'|'video', url }]. An EMPTY
 *                             list is a supported state, not an error - draw()
 *                             answers null, the video card never appears and
 *                             the gif layers fall back to a pink noise tile.
 *
 *   createHostMedia()         the shipping source. STUB for now: the C# host
 *                             will post the same manifest DtRH gets
 *                             (DtrhAssetManifest -> https://ccp.assets/ urls,
 *                             see dtrh/hostMedia.js) and the page will feed it
 *                             in through `adopt()`. Until that lands it behaves
 *                             exactly like an empty fixture source, so the ramp
 *                             degrades instead of throwing.
 *
 * Everything is URL-only. No fetch, no blobs, no canvas, no WebGL upload: the
 * manifest may carry remote CDN entries that send no CORS headers, and a plain
 * <img>/<video>/background-image is the only road those can take (this is the
 * same rule that keeps dtrh/hostMedia.js's two pools apart).
 * ==========================================================================*/

const KINDS = ['image', 'gif', 'video'];
const NO_ECHO = 6;   // a reshuffled deck avoids repeating the last N draws

/** A pink static tile, generated in-page, used wherever a gif is missing. */
export function noiseTileUrl(seed = 2) {
  const svg = '<svg xmlns="http://www.w3.org/2000/svg" width="160" height="160">'
    + '<filter id="n"><feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="'
    + Math.max(1, seed | 0)
    + '"/><feColorMatrix values="0.9 0 0 0 0.55  0 0.2 0 0 0.08  0 0 0.6 0 0.5  0 0 0 0.7 0"/></filter>'
    + '<rect width="160" height="160" filter="url(#n)"/></svg>';
  return 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
}

/** Shuffle in place with an optional injected rng (tests want determinism). */
function shuffle(arr, rnd) {
  for (let i = arr.length - 1; i > 0; i--) {
    const j = Math.floor((rnd ? rnd() : Math.random()) * (i + 1));
    const tmp = arr[i]; arr[i] = arr[j]; arr[j] = tmp;
  }
  return arr;
}

/** The shared pool body both sources use. `entries` may be replaced by adopt(). */
function createPool(entries, { rnd, label, lowWater = 0, onLow } = {}) {
  let all = [];
  const decks = {};      // kind -> remaining shuffled indices
  const recent = [];     // last NO_ECHO urls handed out

  function ingest(list) {
    all = [];
    ingestInto(list, all);
    for (const k of KINDS) delete decks[k];
    recent.length = 0;
  }

  function ingestInto(list, out) {
    for (const raw of Array.isArray(list) ? list : []) {
      if (!raw) continue;
      const url = typeof raw === 'string' ? raw : raw.url;
      if (!url || typeof url !== 'string') continue;
      let kind = typeof raw === 'string' ? '' : String(raw.kind || '');
      if (!KINDS.includes(kind)) {
        // infer from the extension when the entry did not say
        if (/\.(mp4|webm|m4v|mov)(\?|$)/i.test(url)) kind = 'video';
        else if (/\.gif(\?|$)/i.test(url)) kind = 'gif';
        else kind = 'image';
      }
      // a CLIP is a gif that arrives as video (an online GifClip): it rides the gif
      // surfaces and the video card, never an image slot (a background-image cannot play it)
      const clip = kind === 'gif' && /\.(mp4|webm|m4v)(\?|#|$)/i.test(url);
      out.push({ kind, url, clip, name: (typeof raw === 'object' && raw.name) || url.split('/').pop() });
    }
  }
  ingest(entries);

  /** Indices of every entry usable as `kind` (a gif is also a fine image, a clip a fine video). */
  function usable(e, kind) {
    if (kind === 'gif') return e.kind === 'gif';
    if (kind === 'video') return e.kind === 'video' || !!e.clip;
    return (e.kind === 'image' || e.kind === 'gif') && !e.clip;
  }
  function poolFor(kind) {
    const out = [];
    for (let i = 0; i < all.length; i++) if (usable(all[i], kind)) out.push(i);
    return out;
  }

  function has(kind) { return poolFor(kind).length > 0; }

  /** Add entries without throwing away the deck we are part-way through. */
  function extend(list) {
    const seen = new Set(all.map((e) => e.url));
    const before = all.length;
    const add = [];
    ingestInto(list, add);
    for (const e of add) if (!seen.has(e.url)) { seen.add(e.url); all.push(e); }
    return all.length - before;
  }

  /** One url, non-repeating across a deck, echo-guarded on tiny pools. */
  function draw(kind) {
    const k = KINDS.includes(kind) ? kind : 'image';
    const pool = poolFor(k);
    if (!pool.length) return null;
    let deck = decks[k];
    if (!deck || !deck.length) deck = decks[k] = shuffle(pool.slice(), rnd);
    let idx = deck.pop();
    // echo guard: on a pool bigger than the guard, skip a url we just used
    if (pool.length > NO_ECHO && recent.includes(all[idx].url) && deck.length) idx = deck.pop();
    const url = all[idx].url;
    recent.push(url);
    while (recent.length > NO_ECHO) recent.shift();
    // tell the owner when this kind is nearly spent, so a host-backed pool can
    // ask for more BEFORE the deck runs dry and the layer has nothing to show
    if (onLow && deck.length < lowWater) { try { onLow(k, deck.length); } catch { /* the asker's problem */ } }
    return url;
  }

  /** A gif url, or a generated pink noise tile when the pool has none. */
  function drawTile() { return draw('gif') || draw('image') || noiseTileUrl(2 + ((Math.random() * 3) | 0)); }

  function stats() {
    const out = { label: label || 'pool', total: all.length };
    for (const k of KINDS) out[k] = all.filter((e) => e.kind === k).length;
    return out;
  }

  return {
    has, draw, drawTile, stats, adopt: ingest, extend,
    get size() { return all.length; },
    /** Every url held, in no order that matters. */
    urls() { return all.map((e) => e.url); },
    /** How many entries can answer `kind` at all. */
    count(kind) { return poolFor(KINDS.includes(kind) ? kind : 'image').length; },
    /** How many entries of a kind are still unused in the current deck. */
    unused(kind) {
      const k = KINDS.includes(kind) ? kind : 'image';
      const deck = decks[k];
      return deck ? deck.length : poolFor(k).length;
    },
  };
}

/**
 * Dev/test source. Pass the parsed dev/media.json (or []). An empty list is a
 * first-class state: no video card, gifs become pink noise tiles.
 */
export function createFixtureMedia(list, opts = {}) {
  const pool = createPool(list, { rnd: opts.rnd, label: 'fixture' });
  // assign, never spread: `size` is a getter and a spread would freeze it at
  // whatever it read once, so a later adopt() would go unnoticed
  pool.kind = 'fixture';
  return pool;
}

/* ---- the shipping source: the WebView2 host ------------------------------ */

export const HOST_MSG = Object.freeze({
  request: 'pbp:media-request',   // page -> host
  media: 'pbp:media',             // host -> page
  settings: 'pbp:settings',       // host -> page
  online: 'pbp:online-media',     // host -> page, the whole online set (2026-09-28)
  more: 'pbp:media-more',         // page -> host, most of the online set was shown
});

/** An online set this small takes a smaller share, so one fresh picture does not repeat. */
export const ONLINE_WARM = 6;
/** Share of the online set shown before the page asks the host for the next wave. */
export const ONLINE_LOW_SHARE = 0.7;

/**
 * The chance a draw comes from the online set: all of it when the local deck
 * cannot answer, none when the online set has nothing, else the host's share,
 * scaled down while the online set is still smaller than ONLINE_WARM (a cold
 * first wave of one or two pictures must not wear every tile).
 */
export function onlineChance(share, onlineCount, localHas) {
  if (!(onlineCount > 0)) return 0;
  if (!localHas) return 1;
  const s = Math.max(0, Math.min(1, Number.isFinite(share) ? share : 0));
  return s * Math.min(1, onlineCount / ONLINE_WARM);
}

const REQUEST_COUNT = 24;         // how many entries we ask for at a time
const LOW_WATER = 4;              // ask again once a deck is down to this
const REQUEST_GAP_MS = 4000;      // and never more often than this

/** The host talks in three lists of urls; the pool wants entries. */
function entriesFromFrame(frame) {
  const out = [];
  const push = (list, kind) => {
    for (const url of Array.isArray(list) ? list : []) {
      if (typeof url === 'string' && url) out.push({ kind, url });
    }
  };
  push(frame && frame.images, 'image');
  push(frame && frame.gifs, 'gif');
  push(frame && frame.videos, 'video');
  return out;
}

/**
 * The shipping source. Same interface as createFixtureMedia, plus the bridge.
 *
 * The page talks to C# exactly the way DtRH does (dtrh/bridge.js): objects out
 * through window.chrome.webview.postMessage, objects in through its `message`
 * event. Three frames:
 *
 *   page -> host  { type:'pbp:media-request', kinds:['image','gif','video'], count:24 }
 *                 sent once at attach, and again whenever a deck runs below
 *                 four unused entries (throttled, so a hot layer cannot spam
 *                 the host while it is still answering the last ask).
 *   host -> page  { type:'pbp:media', images:[...], gifs:[...], videos:[...] }
 *                 https://ccp.assets/ urls from the user's own library, already
 *                 filtered by their blocklists on the C# side. Any list may be
 *                 empty, and an empty answer is a normal answer.
 *   host -> page  { type:'pbp:settings', videoHoldSec, reducedMotion }
 *                 once after boot. videoHoldSec becomes the base hold for the
 *                 video card; reducedMotion turns the moving layers off.
 *   host -> page  { type:'pbp:online-media', state, subs, share, images:[...], clips:[...], have, want }
 *                 the WHOLE online set (Scrolller stills and gif clips under
 *                 https://ccp.assets/.temp/), every time it changes. Kept
 *                 beside the local deck and mixed in at share percent.
 *   page -> host  { type:'pbp:media-more' } once ONLINE_LOW_SHARE of the online
 *                 set has been on screen; re-armed when the set grows.
 *
 * Outside WebView2 (the dev harness in a plain browser) there is no bridge, so
 * this degrades to exactly what createFixtureMedia([]) does: an empty pool, a
 * console.warn, and no throw anywhere.
 *
 * URL-ONLY, like every other pool here: no fetch, no canvas, no WebGL upload.
 * The host may hand us remote CDN urls that send no CORS headers, and a plain
 * <img>/<video>/background-image is the only road those can travel.
 */
export function createHostMedia(entries, opts = {}) {
  const bridge = opts.bridge
    || (typeof window !== 'undefined' && window.chrome && window.chrome.webview) || null;

  let pool = null;
  const requestNow = () => { if (pool) pool.request(); };
  pool = createPool(entries || [], { label: 'host', lowWater: LOW_WATER, onLow: requestNow });
  pool.kind = 'host';

  // what the host told us about itself; the ramp reads these through onSettings
  const settings = { videoHoldSec: null, reducedMotion: false };
  const subs = new Set();
  const gapMs = Number.isFinite(opts.requestGapMs) ? Math.max(0, opts.requestGapMs) : REQUEST_GAP_MS;
  let lastAsk = -Infinity;
  let listener = null;

  const stamp = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());

  function request() {
    if (!bridge || typeof bridge.postMessage !== 'function') return false;
    const t = stamp();
    if (t - lastAsk < gapMs) return false;   // the host is still answering the last ask
    lastAsk = t;
    try {
      bridge.postMessage({ type: HOST_MSG.request, kinds: KINDS.slice(), count: REQUEST_COUNT });
      return true;
    } catch { return false; }
  }

  function handle(data) {
    if (!data || typeof data !== 'object') return;
    if (data.type === HOST_MSG.media) {
      const added = pool.extend(entriesFromFrame(data));
      // an answer that brought nothing must not leave us asking in a tight
      // loop, so the throttle stamp stands either way
      if (!added && !pool.size) warnEmpty();
      return;
    }
    if (data.type === HOST_MSG.online) { setOnline(data); return; }
    if (data.type === HOST_MSG.settings) {
      if (Number.isFinite(data.videoHoldSec) && data.videoHoldSec > 0) settings.videoHoldSec = data.videoHoldSec;
      settings.reducedMotion = !!data.reducedMotion;
      for (const fn of [...subs]) { try { fn({ ...settings }); } catch { /* a listener is not our problem */ } }
    }
  }

  function warnEmpty() {
    try { console.warn('[pbp/ramp] host media pool is empty; the ramp runs without pictures.'); } catch { /* no console */ }
  }

  if (bridge && typeof bridge.addEventListener === 'function') {
    listener = (e) => { try { handle(e && e.data); } catch { /* a bad frame is not a crash */ } };
    try { bridge.addEventListener('message', listener); } catch { listener = null; }
    request();
  } else {
    warnEmpty();
  }

  /* ---- the online set (2026-09-28) ----------------------------------------
   * Scrolller pictures the host fetched for the player's flavour (or the app's
   * own online source), kept BESIDE the local deck, replaced whole by every
   * `pbp:online-media` frame, and mixed in at the host's `share`. A draw falls
   * back to whichever side can answer, so an empty or failed online set is the
   * local deck exactly as before, and an empty library is the online set. */
  const rnd = typeof opts.rnd === 'function' ? opts.rnd : Math.random;
  const online = createPool([], { rnd, label: 'online' });
  const shown = new Set();
  const last = {};
  let share = 0.7;
  let asked = false;
  const local = { draw: pool.draw, has: pool.has, unused: pool.unused, stats: pool.stats, count: pool.count };
  const localSize = () => local.stats().total;

  function setOnline(frame) {
    const list = [];
    for (const url of Array.isArray(frame.images) ? frame.images : []) if (typeof url === 'string' && url) list.push({ kind: 'image', url });
    // clips are gifs that play as video: kind gif, the .webm/.mp4 url marks them
    for (const url of Array.isArray(frame.clips) ? frame.clips : []) if (typeof url === 'string' && url) list.push({ kind: 'gif', url });
    const next = new Set(list.map((e) => e.url));
    const before = online.size;
    // a set that only grew keeps its deck; anything retired means a fresh deal
    if (online.urls().every((u) => next.has(u))) online.extend(list); else online.adopt(list);
    if (Number.isFinite(frame.share)) share = Math.max(0, Math.min(1, frame.share / 100));
    for (const u of [...shown]) if (!next.has(u)) shown.delete(u);
    if (online.size > before || !online.size) asked = false;
  }

  function noteShown(url) {
    shown.add(url);
    if (asked || !online.size || shown.size < Math.ceil(online.size * ONLINE_LOW_SHARE)) return;
    asked = true;
    try { if (bridge && typeof bridge.postMessage === 'function') bridge.postMessage({ type: HOST_MSG.more }); } catch { /* host gone */ }
  }

  pool.draw = (kind) => {
    const k = KINDS.includes(kind) ? kind : 'image';
    const fromOnline = rnd() < onlineChance(share, online.count(k), local.has(k));
    let url = fromOnline ? online.draw(k) : local.draw(k);
    // the same picture twice in a row takes the other side when it can
    if (url && url === last[k]) {
      const alt = fromOnline ? (local.has(k) ? local.draw(k) : null) : (online.count(k) ? online.draw(k) : null);
      if (alt && alt !== url) { url = alt; if (!fromOnline) noteShown(alt); }
    } else if (url && fromOnline) noteShown(url);
    last[k] = url;
    return url;
  };
  pool.has = (kind) => local.has(kind) || online.has(kind);
  pool.unused = (kind) => local.unused(kind) + online.unused(kind);
  pool.count = (kind) => local.count(kind) + online.count(kind);
  pool.drawTile = () => pool.draw('gif') || pool.draw('image') || noiseTileUrl(2 + ((Math.random() * 3) | 0));
  pool.stats = () => ({ ...local.stats(), label: 'host', online: online.stats().total, share });
  Object.defineProperty(pool, 'size', { get: () => localSize() + online.size, configurable: true });
  pool.online = online;

  pool.settings = settings;
  /** Subscribe to host settings. Fires immediately if they already arrived. */
  pool.onSettings = (fn) => {
    if (typeof fn !== 'function') return () => {};
    subs.add(fn);
    if (settings.videoHoldSec != null || settings.reducedMotion) { try { fn({ ...settings }); } catch { /* ignore */ } }
    return () => subs.delete(fn);
  };
  pool.request = request;
  /** For tests and for a host that pushes without being asked. */
  pool.handleMessage = handle;
  pool.dispose = () => {
    subs.clear();
    if (bridge && listener && typeof bridge.removeEventListener === 'function') {
      try { bridge.removeEventListener('message', listener); } catch { /* gone */ }
    }
    listener = null;
  };
  return pool;
}

/** Load dev/media.json next to the harness. Never throws: [] on any failure. */
export async function loadFixtureList(url) {
  try {
    const res = await fetch(url, { cache: 'no-cache' });
    if (!res.ok) return [];
    const json = await res.json();
    return Array.isArray(json) ? json : (Array.isArray(json && json.entries) ? json.entries : []);
  } catch { return []; }
}

export default createFixtureMedia;
