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
 *  - claim() runs an ON-DECK WARM RAIL (0825, mobile web): the pool FORECASTS
 *    its next few draws with the very selection code next() runs, then warms
 *    those bytes the SORT way - a detached Image()+decode() for stills/gifs,
 *    fetch(url,{mode:'no-cors'}) for video urls (NEVER a detached <video> -
 *    it spins a demuxer, trap 36). Remote urls only; local is the host serving
 *    the player's own disk and warming it is waste (and what keeps the desktop
 *    WebView2 build inert). The draw itself is still decided AT DRAW TIME by
 *    the same seeded logic, so the served sequence never moves.
 *  - THE MANIFEST SEAM (0825): a game that knows its WHOLE ordered media list
 *    up front (SORT's deck) hands it to pool.warmManifest(entries) and walks
 *    pool.warmCursor(i); pool.ready(url) answers when a url's warm landed, and
 *    pool.markBroken(url)/isBroken(url) drive the shared url blacklist that
 *    every draw skips. All five verbs live on BOTH pool shapes.
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

/* THE ON-DECK RAIL's dials (0825). SORT's warm rail proved the shape and the
 * numbers' logic (games/sort/index.js WARM_AHEAD/WARM_INFLIGHT header): the
 * deck is a LOOK-AHEAD, not a supply figure, and the warm is a background
 * TRICKLE that must lose to whatever is already on screen. */
export const DECK_AHEAD = 6;      // draws forecast (and byte-warmed) ahead, per kind
export const WARM_INFLIGHT = 3;   // warms in flight at once - a third lane lets small
                                  // stills slip past one slow multi-megabyte download
const WARM_VIDEO_INFLIGHT = 2;    // of those, at most TWO may be video fetches - a
                                  // third stalled mp4 would eat into Safari's
                                  // 6-connections-per-host budget the game plays on
const WARM_VIDEO_TIMEOUT_MS = 12000;  // a video warm that outlives this is aborted
                                      // (an abort is a shrug, never a blacklist)
const WARM_HELD_MAX = 24;         // decoded detached Images held against GC
const VIDEO_URL_RE = /\.(mp4|webm|m4v)(\?|#|$)/i;

/* THE MANIFEST WARMER's windows (0825, SORT). A game that knows its complete
 * ordered media need list up front (SORT builds the whole deck before the first
 * card shows) hands the list to pool.warmManifest() and advances
 * pool.warmCursor() as its own play position moves; the warm window rides the
 * cursor. IDLE is the door/ghost-round/rules-sheet time - the cursor has never
 * advanced, the player is reading and the network is otherwise quiet, so the
 * window is deep. Once the cursor moves the window narrows to a look-ahead a
 * fast swiper cannot outrun but that still loses to whatever is on screen.
 * Manifest warms ride the SAME rail as the forecast deck above - one
 * WARM_INFLIGHT budget, one warm per url per provider, WARM_HELD_MAX held,
 * nothing at all under Data Saver. */
export const MANIFEST_AHEAD_IDLE = 24;
export const MANIFEST_AHEAD_PLAY = 10;

/* THE URL BLACKLIST (0825), module-level so a dead CDN url stays dead across
 * every pool and every class this page runs (it empties with the page). Fed by
 * PROOF only: an image warm that completed with zero pixels, and games
 * reporting a face whose error actually convicts the url via pool.markBroken().
 * A no-cors video fetch REJECTING is NOT proof and never lands here - CSP
 * refusal (production's connect-src excludes the media CDNs), a content
 * blocker, a dropped cellular link and a page-backgrounding abort all reject
 * exactly the way a dead host does, and treating them as verdicts once
 * blacklisted the entire loop pool in seconds. Entries FORGIVE: the first
 * strike heals after BROKEN_TTL_MS (a transient stumble is not a sentence),
 * a second strike is permanent for the page. Draws skip a broken url and
 * serve the next eligible row instead; the placeholder floor stays the final
 * answer. Bounded: the least recently struck entry is forgotten first. */
const BROKEN_URL_CAP = 400;
const BROKEN_TTL_MS = 45000;      // one strike heals after this; two never do
const brokenUrls = new Map();     // url -> { at, strikes }
export function markBrokenUrl(url) {
  const s = String(url || '');
  if (!s) return false;
  const prior = brokenUrls.get(s);
  if (prior) {
    /* a repeat offender: bump the strike and re-stamp. delete + set keeps the
     * Map's insertion order meaning "least recently struck evicts first". */
    brokenUrls.delete(s);
    brokenUrls.set(s, { at: Date.now(), strikes: prior.strikes + 1 });
    return false;
  }
  brokenUrls.set(s, { at: Date.now(), strikes: 1 });
  while (brokenUrls.size > BROKEN_URL_CAP) {
    const oldest = brokenUrls.keys().next().value;
    if (oldest == null) break;
    brokenUrls.delete(oldest);
  }
  return true;
}
export function isBrokenUrl(url) {
  const rec = brokenUrls.get(String(url || ''));
  if (!rec) return false;
  return rec.strikes >= 2 || (Date.now() - rec.at) < BROKEN_TTL_MS;
}

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

  /* ------------------------------------------------------------------------
   * THE RAND BUFFER - the seam that lets a pool decide its draws EARLY without
   * moving them. next() used to call rng() inline; now every draw takes its
   * values through takeRand() (a FIFO over the same stream) and a FORECAST
   * peeks the values a future draw WILL take via peekRand() without consuming
   * them. Emissions are handed to real draws strictly in emission order, so
   * for a given seed the served sequence is byte-identical to the pre-deck
   * provider - peeking pulls values into the buffer early, never past a draw.
   * The buffer is provider-level because the rng is (shell.js hands us
   * makeRng(utcDateSeed + '|assets') and nothing else consumes it).
   * --------------------------------------------------------------------- */
  const randBuf = [];
  const takeRand = () => (randBuf.length ? randBuf.shift() : rng());
  const peekRand = (i) => { while (randBuf.length <= i) randBuf.push(rng()); return randBuf[i]; };

  /** Data Saver is the player's word that we do not spend bytes on a guess. */
  const saveData = (() => {
    try { return !!(typeof navigator !== 'undefined' && navigator.connection && navigator.connection.saveData === true); }
    catch { return false; }
  })();

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

  /* ------------------------------------------------------------------------
   * THE WARM RAIL - bytes AND decode ahead of the deal, and nothing else.
   *
   * The old mechanism was a `link rel=preload` at fetchPriority low: a hint
   * that bought no decode, and it was pointed at urls the cursor+jump draw
   * almost never actually served. Deleted in favour of the two warms SORT's
   * rail proved on a phone (games/sort/index.js, owner report 2026-08-24):
   *
   *  - a still or gif warms in a DETACHED new Image() + decode(). Nothing
   *    composites; the strong ref in warmHeld only keeps GC off the request,
   *    and the decoded frames land in the browser's image cache keyed by url -
   *    exactly where the game's own <img> will look.
   *  - a video url warms with fetch(url, {mode:'no-cors'}) and the opaque
   *    reply is thrown away unread. Deliberately NEVER a detached <video>: a
   *    warm <video> starts a demuxer the instant it has bytes - an off-screen
   *    decoder no ceiling ever counted (trap 36's law).
   *
   * BOUNDED, and that is load-bearing (0821, Lost & Found's dense-board pass):
   * a stampede of warms races the media the player is looking at. This is a
   * TRICKLE - WARM_INFLIGHT lanes, every url at most once per provider, only
   * the deck HEAD may ask for 'high' priority - and under Data Saver no byte
   * moves at all. REMOTE urls only: local (ccp.* / relative / data: / blob: /
   * same-origin) is the host serving the player's own disk, already instant,
   * which is what keeps the desktop WebView2 build's behaviour inert.
   * --------------------------------------------------------------------- */
  const warmQueue = [];
  const warmHeld = new Map();       // url -> the detached Image holding it open
  let warmFlight = 0;
  let warmVideoFlight = 0;          // the video lanes within warmFlight (capped)

  /** Only bytes that actually travel: remote http(s), not our own origin. */
  function warmable(url) {
    const s = String(url || '');
    if (!s || prewarmed.has(s)) return false;
    if (isBrokenUrl(s)) return false;            // a dead url is never re-asked for
    if (isLocalUrl(s)) return false;             // ccp.* / relative / data: / blob:
    if (!/^https?:\/\//i.test(s)) return false;
    try {
      if (typeof location !== 'undefined' && location.origin && new URL(s).origin === location.origin) return false;
    } catch { return false; }
    return true;
  }

  /** A url whose warm cannot help: local disk, our own origin, data:/blob:.
   *  These are ready the instant an element asks - ready() answers true NOW. */
  function instantUrl(url) {
    const s = String(url || '');
    if (!s) return false;
    if (isLocalUrl(s)) return true;
    if (!/^https?:\/\//i.test(s)) return true;
    try {
      if (typeof location !== 'undefined' && location.origin && new URL(s).origin === location.origin) return true;
    } catch { /* an unparsable url is not instant */ }
    return false;
  }

  /* Warm OUTCOMES, for ready(): which urls finished (bytes + decode for a
   * still/gif, bytes for a video) and who is waiting to hear. */
  const warmDoneUrls = new Set();
  const readyWaiters = new Map();   // url -> Set of resolve callbacks

  function flushReady(url, ok) {
    const set = readyWaiters.get(url);
    if (!set) return;
    readyWaiters.delete(url);
    for (const fn of [...set]) { try { fn(!!ok); } catch { /* a bad waiter never kills the rail */ } }
  }
  /** The instance's blacklist verb: the module Set, plus this instance's
   *  waiters answered "no" so a gate consulting ready() moves on at once. */
  function markBroken(url) {
    const s = String(url || '');
    if (!s) return;
    markBrokenUrl(s);
    flushReady(s, false);
  }

  function warmDone(video) {
    warmFlight = Math.max(0, warmFlight - 1);
    if (video) warmVideoFlight = Math.max(0, warmVideoFlight - 1);
    if (!disposed) warmPump();
  }
  function warmOk(url, video) { warmDoneUrls.add(url); flushReady(url, true); warmDone(video); }
  function warmPump() {
    while (!disposed && warmFlight < WARM_INFLIGHT && warmQueue.length) {
      /* the video lanes are capped BELOW the rail's: with both spoken for, a
       * queued image may still overtake, but a third mp4 waits its turn */
      let at = 0;
      if (warmVideoFlight >= WARM_VIDEO_INFLIGHT) {
        at = warmQueue.findIndex((j) => !j.video);
        if (at < 0) break;                  // only videos queued and both lanes busy
      }
      const job = warmQueue.splice(at, 1)[0];
      warmFlight += 1;
      if (job.video) {
        /* the opaque reply's body is CANCELLED the moment the headers land:
         * warmOk was always a headers-level verdict, and an unread multi-MB
         * mp4 body parked on the socket starves Safari's 6-per-host budget
         * until later requests time out. A REJECTED warm proves NOTHING about
         * the url - CSP refusal, content blockers, a dropped link and our own
         * deadline abort all reject identically - so it NEVER feeds the
         * blacklist; the waiters just hear "no" and the draw moves on. */
        warmVideoFlight += 1;
        try {
          if (typeof fetch !== 'function') { warmDone(true); continue; }
          const init = { mode: 'no-cors', credentials: 'omit' };
          if (job.high) init.priority = 'high';       // Fetch Priority API; unknown keys are ignored
          let deadline = 0;
          try {
            if (typeof AbortController === 'function') {
              const ctl = new AbortController();
              init.signal = ctl.signal;
              deadline = setTimeout(() => { try { ctl.abort(); } catch { /* noop */ } }, WARM_VIDEO_TIMEOUT_MS);
            }
          } catch { /* engines without AbortController warm undeadlined */ }
          const settle = () => { if (deadline) { try { clearTimeout(deadline); } catch { /* noop */ } } };
          const pr = fetch(job.url, init);
          if (pr && pr.then) pr.then((res) => {
            settle();
            /* opaque responses may carry a null body - the cancel is guarded */
            try { if (res && res.body && typeof res.body.cancel === 'function') res.body.cancel(); } catch { /* noop */ }
            warmOk(job.url, true);
          }, () => { settle(); flushReady(job.url, false); warmDone(true); });
          else { settle(); warmDone(true); }
        } catch { warmDone(true); }
      } else {
        let img = null;
        try { img = new Image(); } catch { img = null; }
        if (!img) { warmDone(); continue; }
        warmHeld.set(job.url, img);
        while (warmHeld.size > WARM_HELD_MAX) {
          const oldest = warmHeld.keys().next().value;
          if (oldest == null) break;
          warmHeld.delete(oldest);
        }
        try {
          img.decoding = 'async';
          try { img.fetchPriority = job.high ? 'high' : 'low'; } catch { /* older engines */ }
          img.src = job.url;
          /* BYTES ARE HALF THE BILL: a cached gif still pays its decode on
           * first paint, so a warm is not done until decode() says the first
           * frame exists. Fallback for engines without decode(): load/error,
           * bytes-only. */
          if (typeof img.decode === 'function') {
            img.decode().then(() => warmOk(job.url), () => {
              warmHeld.delete(job.url);
              /* decode() also rejects when a HEALTHY url's decode is aborted
               * mid-flight; only a url with no pixels at all is actually dead. */
              if (img.complete && !(Number(img.naturalWidth) > 0)) markBroken(job.url);
              else flushReady(job.url, false);
              warmDone();
            });
          } else {
            img.onload = () => warmOk(job.url);
            img.onerror = () => { warmHeld.delete(job.url); markBroken(job.url); warmDone(); };
          }
        } catch { warmHeld.delete(job.url); warmDone(); }
      }
    }
  }

  /** Queue one url for warming. Returns true if it was actually queued.
   *  `videoHint` overrides the extension sniff when the caller knows the mime. */
  function warmUrl(url, high, videoHint) {
    if (disposed || saveData) return false;
    if (typeof document === 'undefined') return false;   // node harness: inert
    if (!warmable(url)) return false;
    const s = String(url);
    prewarmed.add(s);
    warmQueue.push({ url: s, video: videoHint == null ? VIDEO_URL_RE.test(s) : !!videoHint, high: !!high });
    warmPump();
    return true;
  }

  /**
   * prewarm(urls): the bounded warm verb (tagged.js injects it; its pool.prewarm
   * already peeks the rows its cursor WILL serve, so its urls are honest). The
   * first url that actually queues is the caller's own deck head - 'high'.
   */
  const PREWARM_MAX = 12;
  function prewarm(urls) {
    let n = 0;
    for (const url of (urls || [])) {
      if (n >= PREWARM_MAX) break;
      if (!url || prewarmed.has(url)) continue;
      if (warmUrl(url, n === 0)) n += 1;
    }
  }

  /* ------------------------------------------------------------------------
   * THE MANIFEST WARMER (0825). warmManifest(entries) declares the ORDERED
   * media need list a game already knows in full; warmCursor(i) is the game's
   * play position in it. The warm window is [cursor, cursor + ahead): deep
   * (MANIFEST_AHEAD_IDLE) while the cursor has never advanced - the door /
   * intro / rules-sheet time - and a tight look-ahead (MANIFEST_AHEAD_PLAY)
   * once play starts. One manifest per provider: a game claims one pool at a
   * time, and a new warmManifest() simply replaces the old list. Everything
   * funnels through warmUrl(), so remote-only, once per url, WARM_INFLIGHT
   * shared with the forecast rail, WARM_HELD_MAX held, saveData-inert - all
   * of it holds here by construction. The 6-deep forecast rail above stays
   * exactly as it was for games that never call this.
   * --------------------------------------------------------------------- */
  let manifest = null;    // { entries: [{url, video}], cursor, moved }

  function normalizeManifestEntries(entries) {
    const out = [];
    for (const e of (Array.isArray(entries) ? entries : [])) {
      const url = typeof e === 'string' ? e : (e && e.url);
      if (!url || typeof url !== 'string') continue;
      const mime = (e && typeof e === 'object') ? String(e.mime || '') : '';
      out.push({ url: String(url), video: /^video\//i.test(mime) || VIDEO_URL_RE.test(url) });
    }
    return out;
  }

  /** Warm the window the cursor currently commands. Returns how many queued. */
  function pumpManifest() {
    if (!manifest || disposed) return 0;
    const ahead = manifest.moved ? MANIFEST_AHEAD_PLAY : MANIFEST_AHEAD_IDLE;
    const at = Math.max(0, Math.min(manifest.cursor, manifest.entries.length));
    const end = Math.min(manifest.entries.length, at + ahead);
    let queued = 0;
    for (let k = at; k < end; k++) {
      const e = manifest.entries[k];
      /* the HEAD of the window is the card the player is (about to be) looking
       * at - the one warm allowed to ask for priority */
      if (e && warmUrl(e.url, k === at, e.video)) queued += 1;
    }
    return queued;
  }

  function warmManifest(entries) {
    manifest = { entries: normalizeManifestEntries(entries), cursor: 0, moved: false };
    return pumpManifest();
  }

  function warmCursor(i) {
    if (!manifest) return;
    const n = Math.max(0, Math.round(Number(i) || 0));
    if (n > 0) manifest.moved = true;
    if (n === manifest.cursor) return;
    manifest.cursor = n;
    pumpManifest();
  }

  /**
   * ready(url, {timeoutMs}) -> Promise<boolean>. True the moment the url's
   * warm has completed (immediately if it already has, and immediately for a
   * local / same-origin url - the host serving the player's own disk cannot be
   * probed and does not need to be); false on a KNOWN failure (the blacklist)
   * or when timeoutMs passes first. Never rejects: a media gate that could
   * throw would be a class that could not start. timeoutMs 0 answers from
   * what is known right now.
   */
  const READY_DEFAULT_MS = 1000;
  function readyFor(url, opts) {
    const s = String(url || '');
    if (!s) return Promise.resolve(false);
    if (isBrokenUrl(s)) return Promise.resolve(false);
    if (instantUrl(s)) return Promise.resolve(true);
    if (warmDoneUrls.has(s)) return Promise.resolve(true);
    const t = (opts && opts.timeoutMs != null) ? Math.max(0, Number(opts.timeoutMs) || 0) : READY_DEFAULT_MS;
    /* nothing pending can ever land: disposed, Data Saver (no warm runs), or
     * a zero timeout - answer with what is known */
    if (t === 0 || disposed || saveData) return Promise.resolve(false);
    return new Promise((resolve) => {
      let set = readyWaiters.get(s);
      if (!set) { set = new Set(); readyWaiters.set(s, set); }
      let timer = 0;
      const fn = (ok) => { try { clearTimeout(timer); } catch { /* noop */ } resolve(!!ok); };
      set.add(fn);
      timer = setTimeout(() => {
        const cur = readyWaiters.get(s);
        if (cur) { cur.delete(fn); if (!cur.size) readyWaiters.delete(s); }
        resolve(false);
      }, t);
    });
  }

  /** The media seam every pool shape exposes (claim() below, claimTagged's
   *  wrapper, and deck-side adapters forward these five verbs verbatim). */
  const mediaSeam = {
    warmManifest: (entries, opts) => { void opts; return warmManifest(entries); },
    warmCursor: (i) => warmCursor(i),
    ready: (url, opts) => readyFor(url, opts),
    markBroken: (url) => markBroken(url),
    isBroken: (url) => isBrokenUrl(url),
  };

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

    // (The claim-time warm moved BELOW the deck machinery: it now warms the
    //  actual first deck entries - the urls next() WILL serve - instead of a
    //  slice of the local lists, which on the web build were placeholder SVGs
    //  and on desktop are the host's own disk. See refreshDeck().)

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
            /* The pool just grew, so every forecast is stale: re-deal the deck
             * and warm ITS urls. (The old `slice(-4)` warmed the newest
             * arrivals, which the cursor+jump draw almost never served next.) */
            refreshDeck();
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

    /* =======================================================================
     * THE DRAW, parameterized (THE ON-DECK RAIL, 0825).
     *
     * computeDraw/computeLocal are next()'s old inline selection logic MOVED,
     * not changed: same branches, same cursor walk, same seeded jump, and the
     * rand supplier `take` is called at exactly the code positions rng() sat.
     * next() runs them against the LIVE cursors with takeRand() (consuming the
     * stream in emission order - byte-identical serving); refreshDeck() runs
     * them against CLONED cursors with peekRand() (consuming nothing) to learn
     * which urls the next DECK_AHEAD draws of a kind WILL serve, and hands the
     * remote ones to the warm rail - the head at 'high' priority. A forecast
     * is exact until the pool grows; every draw and every absorbed batch
     * re-deals it, so the deck head is always the truth.
     * ==================================================================== */
    function computeLocal(kind, st, take) {
      const list = kind === 'loop' ? localLoops : (kind === 'target' ? localTargets : localStills);
      if (!list.length) return placeholders[Math.floor(take() * placeholders.length)] || null;
      const i = st.cursors[kind] % list.length;
      st.cursors[kind] += 1;
      // a deterministic walk with a seeded jump keeps repeats far apart without
      // ever risking "the same tile twice in a row" on a tiny local pool
      const jump = list.length > 2 ? Math.floor(take() * (list.length - 1)) : 0;
      /* THE BLACKLIST SKIP consumes no rand: the walk just steps forward to the
       * next eligible row, so with a clean blacklist (the ordinary day) the
       * served sequence is byte-identical to the pre-blacklist provider. */
      for (let step = 0; step < list.length; step++) {
        const cand = list[(i + jump + step) % list.length];
        if (cand && !isBrokenUrl(cand)) return cand;
      }
      return placeholders[(i + jump) % placeholders.length] || null;   // every row dead: the floor
    }

    function computeDraw(k, st, take) {
      const remoteKind = k === 'target' ? 'still' : k;
      // The ratio is a MIX dial, not a veto: on the placeholder floor (no real
      // local media of this kind) there is nothing to mix WITH, so the remote
      // pool serves every draw it can cover.
      const bareLocal = !(remoteKind === 'loop' ? localPools.loop.length : localPools.still.length);
      if (wantRemote(bareLocal ? 1 : remoteRatio, take(), (remotePools[remoteKind] || []).length > 0, canvasSafe)) {
        const list = remotePools[remoteKind];
        const i = st.remoteCursors[remoteKind] % list.length;
        st.remoteCursors[remoteKind] += 1;
        const jump = list.length > 2 ? Math.floor(take() * (list.length - 1)) : 0;
        /* the blacklist skip: step to the next eligible row, no rand consumed;
         * a fully dead remote list falls through to the local side */
        let url = null;
        for (let step = 0; step < list.length; step++) {
          const cand = list[(i + jump + step) % list.length];
          if (cand && !isBrokenUrl(cand)) { url = cand; break; }
        }
        if (url && (!canvasSafe || isLocalUrl(url))) return { url, remote: true };
      }
      return { url: computeLocal(k, st, take), remote: false };
    }

    const liveState = { cursors, remoteCursors };

    /** The next `depth` draws of one kind, decided EARLY over cloned cursors
     *  and peeked rand values. Mutates nothing that serving reads. */
    function forecast(k, depth) {
      const st = { cursors: Object.assign({}, cursors), remoteCursors: Object.assign({}, remoteCursors) };
      let pi = 0;
      const take = () => peekRand(pi++);
      const out = [];
      for (let i = 0; i < depth; i++) out.push(computeDraw(k, st, take));
      return out;
    }

    /* Forecast only the kinds this claim actually asked for; a spec-less claim
     * still decks the default kind. Each kind's forecast starts at peek index
     * 0 - "the next draws are all this kind" - so whichever kind the game asks
     * for next, that kind's deck HEAD is the url it gets. */
    const deckKinds = [];
    if (want.loop) deckKinds.push('loop');
    if (want.still) deckKinds.push('still');
    if (want.target) deckKinds.push('target');
    if (!deckKinds.length) deckKinds.push('still');

    /** Re-deal the deck and warm its remote urls. Returns how many queued. */
    function refreshDeck(depth) {
      if (released || disposed || saveData) return 0;
      if (typeof document === 'undefined') return 0;
      const d = Math.max(1, Math.min(DECK_AHEAD, (depth | 0) || DECK_AHEAD));
      let queued = 0;
      for (const k of deckKinds) {
        const entries = forecast(k, d);
        for (let i = 0; i < entries.length; i++) {
          const e = entries[i];
          if (e && e.url && e.remote && warmUrl(e.url, i === 0)) queued += 1;
        }
      }
      return queued;
    }

    /* The claim-time warm: the actual first deck entries. On desktop (local
     * media, remote pool empty or unused) this queues nothing - inert. */
    refreshDeck();

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
        /* Decided NOW, with the live cursors and the next rand emissions - the
         * deck never serves, it only warmed what this call is about to pick. */
        const res = computeDraw(k, liveState, takeRand);
        refreshDeck();               // the window moved: warm the new tail
        return res;
      },
      /**
       * Warm the next n deck entries' bytes now (bounded by DECK_AHEAD).
       * SORT's quick path has always called this behind a typeof guard - it
       * was a claimTagged-only verb before 0825. Returns how many queued.
       */
      prewarm(n) {
        const d = Math.round(Number(n) || 0);
        return d > 0 ? refreshDeck(d) : 0;
      },
      /**
       * THE MANIFEST SEAM (0825), same five verbs on every pool shape:
       *   warmManifest(entries[, opts])  ordered [{url, kind?, mime?}] (or bare
       *                                  url strings) - the game's full need
       *   warmCursor(i)                  the game's play position in it
       *   ready(url, {timeoutMs})       -> Promise<boolean> (see readyFor)
       *   markBroken(url) / isBroken(url) the shared url blacklist
       */
      warmManifest: mediaSeam.warmManifest,
      warmCursor: mediaSeam.warmCursor,
      ready: mediaSeam.ready,
      markBroken: mediaSeam.markBroken,
      isBroken: mediaSeam.isBroken,
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
      /* the shared url blacklist: a tagged serve skips a dead row and the
       * seeded re-serve is its substitute source (see tagged.js nextRow) */
      broken: isBrokenUrl,
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
        /* the manifest seam rides BOTH pool shapes (see claim().warmManifest) */
        pool.warmManifest = mediaSeam.warmManifest;
        pool.warmCursor = mediaSeam.warmCursor;
        pool.ready = mediaSeam.ready;
        pool.markBroken = mediaSeam.markBroken;
        pool.isBroken = mediaSeam.isBroken;
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
      manifest = null;
      for (const url of [...readyWaiters.keys()]) flushReady(url, false);
      warmDoneUrls.clear();
      warmQueue.length = 0;
      for (const img of warmHeld.values()) {
        try { img.onload = null; img.onerror = null; } catch { /* noop */ }
      }
      warmHeld.clear();
      warmFlight = 0;
      warmVideoFlight = 0;
      prewarmed = new Set();
    },
  };
}

export default createAssets;
