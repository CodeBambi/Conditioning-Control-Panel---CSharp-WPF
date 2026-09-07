/* ============================================================================
 * race/cloud.js - the BambiCloud mini-player. WEB ONLY.
 *
 *   cloudEnabled(settings) -> boolean            the verb's whole visibility rule
 *   parseLinks(text, origin) -> entry[]          pure, no network, no DOM
 *   createCloud({ settings, hooks, ... }) -> null | player
 *
 * WHAT IT IS. A menu panel behind the verb `play from bambicloud` that turns a
 * link the player already has into the race's track clock. The page owns an
 * <audio> element, that element's currentTime IS the clock, and the run follows
 * it: the Brake pauses the file, resume resumes it, and the end of the file is
 * the end of the lap. Paste several links and they are a playlist: the next
 * track is the next lap.
 *
 * WHY THERE IS NO PLAYLIST BROWSER: the response shape of the site's public
 * playlist collection was never confirmed (race/CLOUD.md has the endpoints and
 * what the two probes answered), so this lane ships the paste box alone rather
 * than a screen of guesses. `entry.locked` and its copy are still here and
 * exercised: a listing that lands later hands entries to addTracks() and a
 * locked one is shown, skipped and NEVER fetched.
 *
 * THE BRIGHT LINE. This page talks to the site directly or not at all. Nothing
 * is proxied through CC Labs, nothing is uploaded, no credential is ever sent
 * (`crossOrigin = 'anonymous'` is a no-cookie request), and only two kinds of
 * url are ever loaded: one on the audio CDN, and a same-origin one. Anything
 * else is refused WITHOUT a request, a link to a page on the site included:
 * from here that track is locked, and locked is said out loud.
 *
 * WHERE THE FRAMES COME FROM. run.js already speaks the CHART.md host protocol:
 * `track-play` on a start, `track-pause {on}` on the Brake and on a host pause,
 * `track-stop` at the end. On the web nothing answers those, so this file does:
 * raceBoot taps its own outbound send and hands every `track-*` frame to
 * hostFrame(). That is the whole pause wiring, and it is the same protocol the
 * desktop host implements in C#.
 *
 * THE CHART SEAM (lane W2 fills it). `hooks.chart({ id, url, title, durationSec,
 * el })` returns the chart for a track. raceBoot's default answers demoChart cut
 * to the element's real duration, so the road and the clock work end to end
 * today and a real decode drops in without touching this file.
 *
 * FAIL LOUD, ONCE. A load that does not answer is retried exactly once. The
 * second failure marks that entry, toasts FAIL_LINE and puts the panel back on
 * the paste box. Nothing polls, nothing loops, nothing retries a locked entry.
 * ==========================================================================*/

/** The only third-party host a url may name. Its files answer `Access-Control-Allow-Origin: *`. */
export const CDN_HOST = 'cdn.bambicloud.com';
/** The verb, and the panel it opens. Lower case like every other verb in the menu. */
export const VERB_ID = 'cloud';
export const VERB_LABEL = 'play from bambicloud';
/** The honest line under the heading: what this device is about to do, in full. */
export const CONSENT_LINE = 'this device streams straight from bambicloud. none of it passes through cc labs and nothing is uploaded. paste a link to a track you can already play over there, one per line.';
export const PASTE_LABEL = 'paste a playlist or track link';
/** Said once, when a load has failed twice. */
export const FAIL_LINE = 'bambicloud is not answering, load your own file instead';
/** Said about an entry with no audio url. Never fetched, never retried. */
export const LOCKED_WORD = 'locked on bambicloud';
export const REFUSED_LINE = 'that is a page link, not a track link. open it over there and copy the track link.';

/** The host's own cadence (CHART.md), and the two waits a load is allowed. */
export const TICK_MS = 250;
/**
 * How close to the end of the file counts AS the end of it. run.js ends a tracked run
 * at `durationSec - 0.25` (CHART.md), which is BEFORE the element fires `ended`, and
 * ending the run posts `track-stop`, which pauses the element so `ended` never comes at
 * all. So a stop this close to the end is the file running out, and the lap rolls on to
 * the next track; a stop anywhere else is a player leaving, and it does not.
 */
export const END_SLOP = 1.2;
export const LOAD_TIMEOUT_MS = 15000;
export const RETRY_MS = 900;

/**
 * The verb's whole visibility rule, exported so race/menu.js and the smoke read
 * the SAME predicate instead of two copies that drift. `cloud` is the browser
 * host's capability flag; `trackPick` true means a desktop host owns track
 * loading outright and this panel would only be a second, worse door.
 */
export function cloudEnabled(settings) {
  return !!settings && settings.cloud === true && settings.trackPick !== true;
}

/** A url this page may hand to an <audio> element: the audio CDN, or same origin. */
function playable(u, origin) {
  if (u.host === CDN_HOST) return true;
  return !!origin && u.origin === origin;
}

/** The last path segment, made readable. A track with no name at all reads as its host. */
function titleOf(u) {
  const seg = u.pathname.split('/').filter(Boolean).pop() || '';
  let s = seg;
  try { s = decodeURIComponent(seg); } catch (e) { /* keep the raw segment */ }
  s = s.replace(/\.[a-z0-9]{2,4}$/i, '').replace(/[_+-]+/g, ' ').trim().toLowerCase();
  return (s || u.host).slice(0, 60);
}

/**
 * Split pasted text into entries. PURE: no network, no DOM, no side effect. A url
 * this page may not load becomes a LOCKED entry rather than being dropped, so the
 * player is told why their link did nothing instead of watching it vanish.
 *
 * @param {string} text
 * @param {string|null} origin  location.origin, so a same-origin url is playable
 * @returns {{id:string,url:string|null,title:string,locked:boolean,failed:boolean}[]}
 */
export function parseLinks(text, origin = null) {
  const out = [], seen = new Set();
  for (const raw of String(text || '').split(/[\s,]+/)) {
    const s = raw.trim();
    if (!s) continue;
    let u = null;
    try { u = new URL(s); } catch (e) { u = null; }
    if (!u || (u.protocol !== 'https:' && u.protocol !== 'http:')) continue;
    if (seen.has(u.href)) continue;
    seen.add(u.href);
    const ok = playable(u, origin);
    out.push({ id: u.href, url: ok ? u.href : null, title: titleOf(u), locked: !ok, failed: false });
  }
  return out;
}

const elm = (tag, cls, parent, text) => {
  const d = document.createElement(tag);
  d.className = cls;
  if (text != null) d.textContent = text;
  parent.appendChild(d);
  return d;
};

/**
 * @param {object}   o
 * @param {object}   o.settings   the host's `init.settings`
 * @param {object}   o.hooks      clock(t, playing), ended(), chart(info)->Promise<chart>,
 *                                track(chart|null, info|null), prefetch(info|null), toast(line),
 *                                pause(on), refresh()
 * @param {function} [o.log]
 * @param {function} [o.ui]       the menu blips
 * @param {function} [o.makeAudio] url -> element. The smoke's seam; the page uses `new Audio`.
 * @param {string}   [o.origin]   location.origin
 */
export function createCloud({ settings = {}, hooks = {}, log = null, ui = null, makeAudio = null, origin = null }) {
  if (!cloudEnabled(settings)) return null;

  const say = (m) => { try { if (log) log('cloud: ' + m); } catch (e) { /* no log */ } };
  const blip = (n) => { try { if (ui) ui(n); } catch (e) { /* audio gone */ } };
  const call = (n, ...a) => { try { return typeof hooks[n] === 'function' ? hooks[n](...a) : undefined; } catch (e) { say(n + ': ' + e); return undefined; } };

  let list = [], at = -1, el = null, timer = 0, tries = 0, retry = 0, gen = 0;
  let played = false;                 // a run has actually played this element, so `pause` means something
  let rolled = false;                 // this element has already handed the lap on
  let view = 'paste', busy = false, disposed = false;
  let input = null, countEl = null, nextEl = null, rows = [], els = [], slotEl = null;
  let onPickRow = null, onClose = null, onRefresh = null;

  /* ---- the element and the clock ---------------------------------------- */
  function stopTick() { if (timer) { clearInterval(timer); timer = 0; } }
  function dropEl() {
    stopTick();
    if (!el) return;
    try { el.pause(); } catch (e) { /* already gone */ }
    try { el.removeAttribute('src'); } catch (e) { /* stub element */ }
    el = null;
  }
  /** The 250 ms tick, the host's own cadence: the file is the authority on the second. */
  function startTick() {
    if (timer) return;
    timer = setInterval(() => {
      if (!el) { stopTick(); return; }
      call('clock', Number(el.currentTime) || 0, !el.paused);
    }, TICK_MS);
  }

  /** Resolve the element's real duration, or throw. The one wait in the whole file. */
  function metadata(a) {
    return new Promise((res, rej) => {
      let done = false;
      const end = (fn, v) => { if (done) return; done = true; clearTimeout(t); fn(v); };
      const t = setTimeout(() => end(rej, new Error('timed out')), LOAD_TIMEOUT_MS);
      const ready = () => { const d = Number(a.duration); end(d > 0 && isFinite(d) ? res : rej, d > 0 && isFinite(d) ? d : new Error('no duration')); };
      if (Number(a.duration) > 0 && isFinite(a.duration)) return ready();
      a.addEventListener('loadedmetadata', ready, { once: true });
      a.addEventListener('error', () => end(rej, new Error('would not load')), { once: true });
    });
  }

  /* ---- loading one entry ------------------------------------------------ */
  /** The next entry at or after `from` that may be played at all. Locked and failed are skipped. */
  function nextPlayable(from) {
    for (let i = Math.max(0, from); i < list.length; i++) {
      const e = list[i];
      if (e.url && !e.locked && !e.failed) return i;
    }
    return -1;
  }
  function info() {
    const e = list[at] || null;
    const total = list.length, nx = nextPlayable(at + 1);
    return e ? { title: e.title, pos: at + 1, total, nextTitle: nx >= 0 ? list[nx].title : null } : null;
  }

  /**
   * Load entry `i`, chart it and hand the chart out. One retry, then the entry is
   * marked, the toast is said once and the panel goes back to the paste box.
   */
  async function load(i) {
    const e = list[i];
    if (!e || !e.url || e.locked || e.failed || disposed) return false;
    const mine = ++gen;
    dropEl();
    at = i; busy = true; played = false; rolled = false; paint();
    let a = null;
    try {
      a = makeAudio ? makeAudio(e.url) : new Audio();
      a.crossOrigin = 'anonymous';       // a no-cookie request: no credential ever reaches them
      a.preload = 'auto';
      a.src = e.url;
      if (typeof a.load === 'function') a.load();
    } catch (err) { return bail(i, err, mine); }
    let dur = 0;
    try { dur = await metadata(a); } catch (err) { return bail(i, err, mine); }
    if (disposed || mine !== gen) return false;
    let chart = null;
    try { chart = await call('chart', { id: e.id, url: e.url, title: e.title, durationSec: dur, el: a }); }
    catch (err) { return bail(i, err, mine); }
    if (disposed || mine !== gen) return false;
    if (!chart) return bail(i, new Error('no chart'), mine);
    el = a;
    el.addEventListener('ended', onEnded);
    tries = 0; busy = false;
    blip('tick');
    say(`${e.title}: ${Math.round(dur)}s, ${at + 1} of ${list.length}`);
    call('track', chart, info());
    armPrefetch();
    paint();
    return true;
  }

  /**
   * Name the next playable track while this one is being driven, so whatever charts
   * it can start now rather than at the moment the lap rolls over. `null` means
   * there is nothing after this one and anything in flight can be let go.
   */
  function armPrefetch() {
    const nx = nextPlayable(at + 1);
    const e = nx >= 0 ? list[nx] : null;
    call('prefetch', e ? { id: e.id, url: e.url, title: e.title } : null);
  }

  /** One retry, then stop. Nothing here ever schedules a second one. */
  function bail(i, err, mine) {
    if (disposed || mine !== gen) return false;
    dropEl();
    say(`${(list[i] && list[i].title) || 'track'}: ${(err && err.message) || err}`);
    if (tries < 1) {
      tries++;
      retry = setTimeout(() => { retry = 0; if (!disposed) load(i); }, RETRY_MS);
      return false;
    }
    tries = 0; busy = false; at = -1;
    if (list[i]) list[i].failed = true;
    view = 'paste';
    call('track', null, null);
    call('toast', FAIL_LINE);
    paint();
    return false;
  }

  /** The file ran out: end this lap, then walk to the next playable track. Once per element. */
  function onEnded() {
    if (rolled) return;                 // the run ended AND the element ended: one rollover, not two
    rolled = true;
    stopTick();
    call('clock', el ? Number(el.duration) || 0 : 0, false);
    call('ended');
    const nx = nextPlayable(at + 1);
    if (nx < 0) { say('playlist done'); paint(); return; }
    call('toast', 'next: ' + list[nx].title);
    load(nx);
  }

  /* ---- the frames run.js posts (CHART.md) -------------------------------- */
  /**
   * raceBoot hands every outbound `track-*` frame here. `track-play` means a run
   * just started, so the file starts at the top - that is what makes "again" a
   * replay and a rolled-over track the next lap.
   */
  function hostFrame(m) {
    const t = m && m.type;
    if (!el || !t) return;
    if (t === 'track-play') {
      played = true;
      try { el.currentTime = 0; } catch (e) { /* not seekable yet */ }
      const p = el.play(); if (p && p.catch) p.catch((e) => say('play: ' + e));
      startTick();
    } else if (t === 'track-pause') {
      if (m.on === false) { const p = el.play(); if (p && p.catch) p.catch((e) => say('resume: ' + e)); }
      else { try { el.pause(); } catch (e) { /* already gone */ } }
      call('clock', Number(el.currentTime) || 0, m.on === false);
    } else if (t === 'track-stop') {
      stopTick();
      const left = Number(el.duration) - (Number(el.currentTime) || 0);
      try { el.pause(); } catch (e) { /* already gone */ }
      if (played && isFinite(left) && left <= END_SLOP) { onEnded(); return; }
    }
    paint();
  }

  /* ---- the panel -------------------------------------------------------- */
  const paused = () => !!(el && el.paused);
  function stateWord(e, i) {
    if (e.locked) return LOCKED_WORD;
    if (e.failed) return 'would not load';
    if (i !== at) return 'ready';
    if (busy) return 'loading';
    if (!played) return 'loaded';     // in hand, waiting for `race` to start the lap
    return paused() ? 'paused' : 'playing';
  }
  function countLine() {
    if (!list.length) return 'nothing pasted yet';
    if (at < 0) return `${list.length} link${list.length === 1 ? '' : 's'} · pick one`;
    return `${list[at].title} · ${at + 1} of ${list.length}`;
  }
  function nextLine() {
    const nx = nextPlayable(at + 1);
    return nx >= 0 ? 'next up: ' + list[nx].title : '';
  }

  /** Read the box, refuse nothing silently, and start the first playable track. */
  function addPasted() {
    const text = input ? input.value : '';
    const found = parseLinks(text, origin);
    if (!found.length) { call('toast', REFUSED_LINE); return; }
    addTracks(found);
    if (input) input.value = '';
  }

  /**
   * Take entries in a listing's own shape. The paste box uses it, and it is the
   * door a playlist listing would come through: a locked entry is kept, shown and
   * skipped, and its url is never touched because it does not have one.
   */
  function addTracks(entries) {
    const have = new Set(list.map((e) => e.id));
    for (const e of Array.isArray(entries) ? entries : []) {
      if (!e || !e.id || have.has(e.id)) continue;
      have.add(e.id);
      list.push({ id: String(e.id), url: e.url ? String(e.url) : null, title: String(e.title || 'track').slice(0, 60), locked: !e.url || e.locked === true, failed: false });
    }
    if (list.some((e) => e.locked)) say(`${list.filter((e) => e.locked).length} locked, skipped`);
    view = list.length ? 'list' : 'paste';
    paint();
    if (at < 0) { const nx = nextPlayable(0); if (nx >= 0) load(nx); }
    else armPrefetch();                 // a link pasted mid run may be the next lap
  }

  function forget() {
    dropEl();
    gen++; list = []; at = -1; tries = 0; busy = false; view = 'paste';
    call('track', null, null);
    call('prefetch', null);
    paint();
  }

  /** The rows the menu walks. Rebuilt on every paint: the list is what changes. */
  function buildRows() {
    const out = [];
    out.push({ id: 'add', label: list.length ? 'add these links' : 'play these links', press: addPasted });
    list.forEach((e, i) => out.push({ id: 'trk-' + i, label: e.title, press: () => { if (e.locked || e.failed) { call('toast', e.locked ? LOCKED_WORD : FAIL_LINE); return; } load(i); } }));
    if (el && played) out.push({ id: 'pause', label: paused() ? 'resume' : 'pause', press: () => call('pause', !paused()) });
    if (list.length) out.push({ id: 'forget', label: 'forget them', press: forget });
    out.push({ id: 'back', label: 'back', press: () => { if (onClose) onClose(); } });
    return out;
  }

  function paint() {
    if (!slotEl || disposed) return;
    if (input) input.hidden = false;                     // the box never goes away: a second link is always one paste off
    if (countEl && countEl.textContent !== countLine()) countEl.textContent = countLine();
    if (nextEl) { const l = nextLine(); if (nextEl.textContent !== l) nextEl.textContent = l; nextEl.hidden = !l; }
    const want = buildRows();
    // Only rebuild the buttons when the shape changed; a repaint on every frame must not
    // churn the DOM under a finger that is already on a row.
    const sig = want.map((r) => r.id).join('|');
    if (sig !== paint.sig) {
      paint.sig = sig;
      for (const b of els) { try { b.remove(); } catch (e) { /* gone */ } }
      els = want.map((r, i) => {
        const b = elm('button', 'rm-btn rm-media-btn rm-cloud-btn', slotEl);
        b.type = 'button'; b.dataset.id = r.id; b.setAttribute('role', 'menuitem');
        elm('span', 'rm-cloud-label', b, r.label);
        elm('span', 'rm-cloud-val', b, '');
        b.addEventListener('click', (ev) => { ev.stopPropagation(); if (onPickRow) onPickRow(i); });
        return b;
      });
    }
    rows = want;
    // the state word rides the row it belongs to, so nothing has to be looked up twice
    let k = 1;
    for (let i = 0; i < list.length; i++, k++) {
      const b = els[k]; if (!b) continue;
      const w = stateWord(list[i], i), v = b.lastChild;
      if (v && v.textContent !== w) v.textContent = w;
      b.classList.toggle('is-off', list[i].locked || list[i].failed);
      b.classList.toggle('is-on', i === at);
    }
    if (onRefresh) onRefresh();
  }
  paint.sig = '';

  /**
   * Build the panel into the menu's slot. `pick(i)` is the menu's own act('press') for row i, so a
   * finger, an arrow and the pad all land on the same line; `close` is the menu's way back to the
   * verbs; `refresh` repaints the menu's focus after the list has changed shape under it.
   */
  function buildPanel({ slot, pick, close, refresh }) {
    slotEl = slot;
    onPickRow = typeof pick === 'function' ? pick : null;
    onClose = typeof close === 'function' ? close : null;
    onRefresh = typeof refresh === 'function' ? refresh : null;
    elm('h3', 'rm-h', slot, VERB_LABEL);
    elm('div', 'rm-hint rm-cloud-line', slot, CONSENT_LINE);
    input = elm('input', 'rm-seed rm-cloud-in', slot);
    input.type = 'text'; input.placeholder = PASTE_LABEL; input.setAttribute('aria-label', PASTE_LABEL);
    input.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); addPasted(); } if (e.key === 'Escape') { e.preventDefault(); input.blur(); } e.stopPropagation(); });
    countEl = elm('div', 'rm-media-count rh-num', slot, countLine()); countEl.setAttribute('aria-live', 'polite');
    nextEl = elm('div', 'rm-hint rm-cloud-next', slot, ''); nextEl.hidden = true;
    paint();
    return { rows: () => rows, els: () => els };
  }

  return {
    buildPanel,
    /** The menu asks for these two every time it walks the panel: the list is live. */
    rows: () => rows,
    els: () => els,
    hostFrame,
    addTracks,
    paint,
    /** For the smoke and for anything that wants to know what is loaded. */
    get state() { return { view, at, busy, played, t: el ? Number(el.currentTime) || 0 : 0, playing: !!(el && !el.paused), list: list.map((e) => ({ ...e })) }; },
    dispose() {
      disposed = true;
      if (retry) { clearTimeout(retry); retry = 0; }
      call('prefetch', null);
      dropEl();
      list = []; els = []; rows = [];
    },
  };
}

export default createCloud;
