/* ============================================================================
 * net/webMedia.js - the Goon Game's online pictures IN A PLAIN BROWSER (2026-09-24).
 *
 * Hosted, GoonHostService + GoonOnlineMedia.cs own the flavour pick and fetch Scrolller.
 * In a browser there is no host, so this module plays that part for the three page ->
 * host frames and answers with the same host -> page frames:
 *
 *   media-flavour {flavour, custom, subs, online}  ->  online-media {state, subs, images, videos, progress}
 *   media-more                                      ->  the next wave of the same pool
 *   peer-niches {subs}                              ->  peer-media  (same shape; 'declined' when off)
 *   noise-want {set}                                ->  noise-media {set, state, images}  (a Sort duel's
 *                                                       NOISE board: a known set id only, stills only)
 *
 * BRIGHT LINE (same law as the desktop feed and the site's Intake): the BROWSER fetches
 * api.scrolller.com itself (it answers `access-control-allow-origin: *`); nothing passes
 * through CC Labs. Only niche NAMES are ever read off a frame, re-checked with the host's
 * grammar (GoonOnlineMediaRules: 2..40 of [A-Za-z0-9_], "r/" dropped, at most 8, deduped
 * without case). URLs are only ever taken from Scrolller's own answer.
 *
 * Numbers mirror GoonOnlineMediaRules: a wave aims at 24 stills + 12 clips, the deck keeps
 * 3 waves' worth per kind (the oldest retires past that), 3 dry batches in a row end a kind
 * for the wave. A picture's url is never dealt twice in a session (no repeats on a refill).
 *
 * CONSENT: the flavour pick is the opt-in, for THIS SESSION ONLY - init always carries an
 * empty `flavour`, so the card asks again next visit. The peer pool runs unless the player
 * switched online pictures off (GoonHostService.PeerFetchAllowed).
 *
 * Pure of the DOM; `fetch`, the clock and the emitter are injected, so node tests drive it.
 * Nothing here throws at import.
 * ==========================================================================*/

import { NOISE_STILLS, noiseSet } from '../core/noiseSets.js';

export const ENDPOINT = 'https://api.scrolller.com/admin';
export const MAX_SUBS = 8;
export const STILL_TARGET = 24;
export const CLIP_TARGET = 12;
export const MAX_WAVES = 3;
export const MAX_DRY_BATCHES = 3;
export const PAGE_LIMIT = 30;
export const MIN_GAP_MS = 1100;          // gallery-dl's politeness, as ScrolllerSource
export const MAX_STILL_WIDTH = 1280;
export const MAX_CLIP_WIDTH = 640;
export const FLAVOURS = ['trance', 'pink', 'frills', 'shiny', 'censored', 'mine'];

const NICHE_RE = /^[A-Za-z0-9_]{2,40}$/;

const QUERY = `query SubredditQuery($url: String!, $iterator: String, $sortBy: GallerySortBy, $filter: GalleryFilter, $limit: Int!) {
  getSubreddit(data: {url: $url, iterator: $iterator, filter: $filter, limit: $limit, sortBy: $sortBy}) {
    id
    children { iterator items { id mediaSources { url width height isOptimized } } }
  }
}`;

/** GoonOnlineMediaRules.CleanSubs: valid names only, first spelling wins, capped. */
export function cleanSubs(raw) {
  const out = [];
  if (!Array.isArray(raw)) return out;
  const seen = new Set();
  for (const r of raw) {
    if (typeof r !== 'string') continue;
    let s = r.trim();
    if (s.toLowerCase().startsWith('r/')) s = s.slice(2);
    if (!NICHE_RE.test(s)) continue;
    const k = s.toLowerCase();
    if (seen.has(k)) continue;
    seen.add(k);
    out.push(s);
    if (out.length >= MAX_SUBS) break;
  }
  return out;
}

export const cleanFlavour = (raw) => {
  const s = String(raw == null ? '' : raw).trim().toLowerCase();
  return FLAVOURS.includes(s) ? s : '';
};

/** GoonOnlineMediaRules.StateFor. */
export function stateFor(online, subs, have, running, failed) {
  if (!online) return 'off';
  if (subs === 0) return 'empty';
  if (have > 0) return running ? 'loading' : 'ready';
  if (running) return 'loading';
  return failed ? 'error' : 'empty';
}

const httpsUrl = (u) => typeof u === 'string' && /^https:\/\//i.test(u);

/** Largest still under the width cap (else the smallest), never a video container. */
export function pickStill(sources) {
  if (!Array.isArray(sources)) return null;
  const stills = sources.filter((s) => s && httpsUrl(s.url) && /\.(?:webp|jpe?g|png)(?:\?|$)/i.test(s.url)
    && typeof s.width === 'number' && s.width > 0);
  return pickByWidth(stills, MAX_STILL_WIDTH);
}

/** Largest non-thumb webm/mp4 under the clip cap (else the smallest). */
export function pickClip(sources) {
  if (!Array.isArray(sources)) return null;
  const clips = sources.filter((s) => s && httpsUrl(s.url) && /\.(?:webm|mp4)(?:\?|$)/i.test(s.url)
    && !/_thumb/i.test(s.url) && typeof s.width === 'number' && s.width > 0);
  return pickByWidth(clips, MAX_CLIP_WIDTH);
}

function pickByWidth(list, cap) {
  if (!list.length) return null;
  const fit = list.filter((s) => s.width <= cap);
  if (fit.length) return fit.reduce((a, b) => (b.width > a.width ? b : a)).url;
  return list.reduce((a, b) => (b.width < a.width ? b : a)).url;
}

/**
 * One pool: the player's own flavour (`online-media`) or the opponent's niches (`peer-media`).
 *
 * @param {object} o
 * @param {string} o.frameType          'online-media' | 'peer-media'
 * @param {(frame:object)=>void} o.emit the host -> page frame sink
 * @param {Function} [o.fetch]          window.fetch
 * @param {()=>number} [o.now]
 * @param {(ms:number)=>Promise<void>} [o.sleep]
 * @param {(msg:string)=>void} [o.log]
 * @param {number} [o.stillTarget]      stills per wave (default STILL_TARGET)
 * @param {number} [o.clipTarget]       clips per wave (default CLIP_TARGET; 0 = stills only)
 * @param {object} [o.extra]            fields stamped on every frame (a noise board's `set`)
 */
export function createWebMediaPool(o) {
  const frameType = o.frameType;
  const STILLS = Number.isInteger(o.stillTarget) && o.stillTarget >= 0 ? o.stillTarget : STILL_TARGET;
  const CLIPS = Number.isInteger(o.clipTarget) && o.clipTarget >= 0 ? o.clipTarget : CLIP_TARGET;
  const extra = o.extra && typeof o.extra === 'object' ? o.extra : null;
  const emit = typeof o.emit === 'function' ? o.emit : () => {};
  const doFetch = typeof o.fetch === 'function' ? o.fetch : null;
  const now = typeof o.now === 'function' ? o.now : () => Date.now();
  const sleep = typeof o.sleep === 'function' ? o.sleep : (ms) => new Promise((r) => setTimeout(r, ms));
  const log = typeof o.log === 'function' ? o.log : () => {};

  let gen = 0;
  let subs = [];
  let images = [];
  let videos = [];
  let running = false;
  let failed = false;
  let stillAdded = 0;
  let clipAdded = 0;
  let lastRequest = -Infinity;
  const seen = new Set();                  // every url dealt this session: no repeats
  const iterators = new Map();             // `${kind}|${sub}` -> iterator (null = start, false = walked out)
  let seq = 0;

  function snapshot() {
    const have = images.length + videos.length;
    const want = subs.length === 0 ? 0
      : running ? have + Math.max(0, STILLS - stillAdded) + Math.max(0, CLIPS - clipAdded)
        : Math.max(have, STILLS + CLIPS);
    return {
      ...(extra || {}),
      type: frameType,
      state: stateFor(true, subs.length, have, running, failed),
      subs: subs.slice(),
      images: images.map((e) => ({ name: e.name, url: e.url })),
      videos: videos.map((e) => ({ name: e.name, url: e.url })),
      progress: { have, want },
    };
  }
  const post = (g) => { if (g === gen) { try { emit(snapshot()); } catch (_e) { /* a sink never breaks a wave */ } } };

  async function page(sub, kind) {
    const key = kind + '|' + sub.toLowerCase();
    const it = iterators.has(key) ? iterators.get(key) : null;
    if (it === false) return { urls: [] };
    const wait = lastRequest + MIN_GAP_MS - now();
    if (wait > 0) await sleep(wait);
    lastRequest = now();
    const res = await doFetch(ENDPOINT, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        query: QUERY,
        variables: { url: '/r/' + sub, iterator: it, sortBy: 'RANDOM', filter: kind === 'image' ? 'PICTURE' : 'GIF', limit: PAGE_LIMIT },
        authorization: null,
      }),
      credentials: 'omit',          // a third party: never volunteer a cookie
      referrerPolicy: 'no-referrer',
    });
    if (!res || !res.ok) throw new Error('scrolller ' + (res ? res.status : 'no answer'));
    const body = await res.json();
    const g = body && body.data && body.data.getSubreddit;
    if (!g) return { urls: [] };
    const next = g.children && typeof g.children.iterator === 'string' ? g.children.iterator : false;
    iterators.set(key, next);
    const items = g.children && Array.isArray(g.children.items) ? g.children.items : [];
    const urls = [];
    for (const item of items) {
      const u = kind === 'image' ? pickStill(item && item.mediaSources) : pickClip(item && item.mediaSources);
      if (u) urls.push(u);
    }
    return { urls };
  }

  async function fillKind(g, kind) {
    const target = kind === 'image' ? STILLS : CLIPS;
    const cap = target * MAX_WAVES;
    const count = () => (kind === 'image' ? stillAdded : clipAdded);
    let dry = 0;
    let errors = 0;
    let asked = 0;
    // One batch = one page from every niche in turn. MAX_DRY_BATCHES batches in a row that
    // add nothing end this kind for the wave.
    while (g === gen && count() < target && dry < MAX_DRY_BATCHES) {
      let added = 0;
      for (const sub of subs.slice()) {
        if (g !== gen || count() >= target) break;
        let got = { urls: [] };
        asked++;
        try { got = await page(sub, kind); } catch (e) { errors++; log('web media: ' + (e && e.message || e)); }
        if (g !== gen) return { asked, errors };
        let fresh = 0;
        for (const u of got.urls) {
          if (count() >= target) break;
          if (seen.has(u)) continue;
          seen.add(u);
          const list = kind === 'image' ? images : videos;
          list.push({ name: 'web-' + kind + '-' + (++seq), url: u });
          while (list.length > cap) list.shift();          // the oldest wave retires
          if (kind === 'image') stillAdded++; else clipAdded++;
          fresh++;
        }
        added += fresh;
        if (fresh) post(g);
      }
      dry = added ? 0 : dry + 1;
    }
    return { asked, errors };
  }

  async function wave(g) {
    let total = { asked: 0, errors: 0 };
    for (const kind of ['image', 'video']) {
      const r = await fillKind(g, kind);
      total = { asked: total.asked + r.asked, errors: total.errors + r.errors };
      if (g !== gen) return;
    }
    running = false;
    failed = images.length + videos.length === 0 && total.asked > 0 && total.errors === total.asked;
    post(g);
  }

  const api = {
    /** Start (or restart) for these niches. The same list while it holds pictures = the snapshot again. */
    start(raw) {
      const clean = cleanSubs(raw);
      const same = clean.length === subs.length && clean.every((s, i) => s.toLowerCase() === subs[i].toLowerCase());
      if (same && (running || images.length + videos.length > 0)) { post(gen); return false; }
      const g = ++gen;
      subs = clean;
      images = [];
      videos = [];
      failed = false;
      stillAdded = 0;
      clipAdded = 0;
      running = clean.length > 0 && !!doFetch;
      post(g);
      if (running) wave(g).catch(() => { if (g === gen) { running = false; failed = true; post(g); } });
      return running;
    },
    /** The next wave for the same niches (the page's deck is mostly shown). */
    more() {
      if (running || subs.length === 0 || !doFetch) return false;
      const g = gen;
      running = true;
      failed = false;
      stillAdded = 0;
      clipAdded = 0;
      post(g);
      wave(g).catch(() => { if (g === gen) { running = false; failed = true; post(g); } });
      return true;
    },
    /** Stop, drop everything, say 'off'. */
    off() {
      gen++;
      subs = [];
      images = [];
      videos = [];
      running = false;
      failed = false;
      try { emit({ ...(extra || {}), type: frameType, state: 'off', subs: [], images: [], videos: [], progress: { have: 0, want: 0 } }); } catch (_e) { /* ignore */ }
    },
    snapshot,
    get running() { return running; },
  };
  return api;
}

/**
 * The init `media` block a browser boots with (GoonHostService.BuildMediaBlock's shape).
 * `flavour` is ALWAYS '': the pick is the opt-in and it lasts this session only, so the card
 * asks again on every visit. `last` and `custom` are only the remembered edits.
 */
export function mediaInitBlock(prefs) {
  const p = prefs || {};
  const c = p.goonMediaCustom;
  return {
    flavour: '',
    last: cleanFlavour(p.goonMediaLast),
    custom: c && typeof c === 'object' && !Array.isArray(c) ? c : {},
    online: p.goonMediaOnline !== false,
  };
}

/**
 * The browser stand-in for GoonHostService's three media handlers.
 *
 * @param {object} o
 * @param {(frame:object)=>void} o.emit          delivers host -> page frames
 * @param {Function} [o.fetch]
 * @param {(partial:object)=>void} [o.savePrefs] remembers the pick's edits (never the opt-in)
 * @param {boolean} [o.online]                    the stored online switch
 * @param {()=>number} [o.now]
 * @param {(ms:number)=>Promise<void>} [o.sleep]
 * @param {(msg:string)=>void} [o.log]
 */
export function createWebMediaHost(o) {
  const common = { fetch: o.fetch, now: o.now, sleep: o.sleep, log: o.log };
  const own = createWebMediaPool(Object.assign({ frameType: 'online-media', emit: o.emit }, common));
  const peer = createWebMediaPool(Object.assign({ frameType: 'peer-media', emit: o.emit }, common));
  const save = typeof o.savePrefs === 'function' ? o.savePrefs : () => {};
  const noise = new Map();   // set id -> pool (stills only, one board each)
  let online = o.online !== false;
  let flavour = '';          // this session's pick; '' until the card is answered
  let ownSubs = [];

  function onFlavour(m) {
    flavour = cleanFlavour(m && m.flavour);
    ownSubs = cleanSubs(m && m.subs);
    if (m && typeof m.online === 'boolean') online = m.online;
    const keep = { goonMediaLast: flavour, goonMediaOnline: online };
    if (m && m.custom && typeof m.custom === 'object' && !Array.isArray(m.custom)) {
      try { const s = JSON.stringify(m.custom); if (s.length <= 16 * 1024) keep.goonMediaCustom = JSON.parse(s); } catch (_e) { /* skip */ }
    }
    try { save(keep); } catch (_e) { /* prefs are a convenience */ }
    if (!online) { own.off(); return; }
    if (!flavour) return;                        // no pick: the card is up and owns the moment
    own.start(ownSubs);                          // [] = an honest 'empty'
  }

  return {
    /** Handle one page -> host frame. true when it was a media frame. */
    handle(m) {
      const t = m && m.type;
      if (t === 'media-flavour') { onFlavour(m); return true; }
      if (t === 'media-more') {
        if (online && flavour && ownSubs.length) own.more();
        return true;
      }
      if (t === 'noise-want') {
        const id = typeof (m && m.set) === 'string' ? m.set : '';
        if (!id) { for (const p of noise.values()) p.off(); noise.clear(); return true; }
        const board = noiseSet(id);
        if (!board) return true;                       // an id off the list is nothing
        if (!online) {
          try { o.emit({ type: 'noise-media', set: board.id, state: 'declined', subs: [], images: [], videos: [] }); } catch (_e) { /* ignore */ }
          return true;
        }
        let pool = noise.get(board.id);
        if (!pool) {
          pool = createWebMediaPool(Object.assign({
            frameType: 'noise-media', emit: o.emit, stillTarget: NOISE_STILLS, clipTarget: 0, extra: { set: board.id },
          }, common));
          noise.set(board.id, pool);
        }
        pool.start([board.sub]);
        return true;
      }
      if (t === 'peer-niches') {
        const subs = cleanSubs(m && m.subs);
        if (!subs.length) { peer.off(); return true; }
        if (!online) {
          peer.off();
          try { o.emit({ type: 'peer-media', state: 'declined', subs: [], images: [], videos: [] }); } catch (_e) { /* ignore */ }
          return true;
        }
        peer.start(subs);
        return true;
      }
      return false;
    },
    own, peer, noise,
  };
}
