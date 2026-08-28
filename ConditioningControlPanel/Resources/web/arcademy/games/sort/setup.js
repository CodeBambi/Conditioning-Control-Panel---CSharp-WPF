/* ============================================================================
 * games/sort/setup.js - THE DOOR. Room 201's setup + tutorial, and the only
 * place the player chooses what SORT means tonight.
 *
 * It runs OUTSIDE the 180s class clock (shell/shell.js calls instance.setup()
 * after create() and before beginPlay()), so it can afford to explain itself.
 * Five steps, and every one shows before it tells:
 *
 *   1 SOURCE   two drawn doors: a web tile, a folder. ONLINE only when the app
 *              actually has remote consent; LOCAL only when there is enough of
 *              a tree to make two different piles out of. Neither = QUICK SORT.
 *   2 TARGET   the pink stack. Catalog niches are FIXED chips; the player's own
 *              subs are ORANGE library pills with a clip count and an X that
 *              removes them everywhere; a search box probes a new sub.
 *   3 NOISE    the grey stack. Same picker plus the eight-sub starter row, and
 *              the two rules the door enforces: a sub on the target side is
 *              REMOVED from the noise side, and a noise NICHE that shared one
 *              flags the pairing HOT.
 *   4 GHOST    two of the player's own cards sort themselves, right with YES,
 *              left with NO. That is the whole rulebook, drawn.
 *   5 PLAY     onPlay(setup, resolved); G1 saves, claims, and calls ghost().
 *
 * WHAT THIS FILE OWNS AND WHAT IT DOES NOT
 *   It owns the door's DOM, its state machine, the persisted SORT_SETUP v1 blob
 *   and the pure resolution from that blob to provider `sources` rows. It owns
 *   NO media claim, NO deck, NO clock: the pool is G1's, and the door only ever
 *   RENDERS rows G1 hands back through ghost().
 *
 * THE LAWS IT KEEPS
 *   - Esc is NOT ours. The shell's leave confirm owns the key (trap 29 and its
 *     corollary); the door's own way out is a signed button that calls onLeave.
 *   - Every string is t('sort_*', fallback). The eight starter subs are DATA.
 *   - Truth is never a guess: the door writes explicit sub lists / folder paths
 *     into the setup, and the host tags each served row from them.
 *   - Keyboard first: every control is a real <button> or <input>, and arrows
 *     walk a chip group.
 *   - Nothing is written to meta until PLAY. A player who leaves at step 2 has
 *     changed nothing (including doorSeen).
 *   - Timers resolve setTimeout off the global at CALL time, so a fake clock in
 *     a suite drives the whole ghost round with no test-only code in here.
 * ==========================================================================*/

import { SETUP_LEX } from './setup-lex.js';

/* ---------------------------------------------------------------- data --- */

/** The shipped noise starter row. Real subs, all thick, all safe for work.
 *  DATA, not lexicon: a mod re-voicing "pokemon" would ask the host for a
 *  subreddit that does not exist. */
export const STARTER_NOISE = Object.freeze([
  'cats', 'aww', 'pokemon', 'food', 'EarthPorn', 'carporn', 'spaceporn', 'architecture',
]);

/** The two folders the host always projects. A tree that is ONLY these has no
 *  second pile in it, which is what QUICK SORT exists for. */
export const ROOT_FOLDERS = Object.freeze(['images', 'videos']);

export const SETUP_VERSION = 1;
export const PROBE_DEBOUNCE_MS = 400;
export const SUB_CAP_PER_SIDE = 12;

/** The ghost round's script. Roughly 2.4s end to end (pitch 4). */
export const GHOST = Object.freeze({
  STAMP_A_MS: 340,
  FLY_A_MS: 640,
  STAMP_B_MS: 1260,
  FLY_B_MS: 1560,
  DONE_MS: 2280,
  REDUCED_DONE_MS: 380,
});

/* ------------------------------------------------------------- pure bits -- */

/** "r/Name", a full url or stray punctuation to a bare subreddit name. Mirror
 *  of FypOnlineCoordinator.SanitizeSub and of fyp/main.js sanitizeSub. */
export function sanitizeSub(raw) {
  let s = String(raw == null ? '' : raw).trim();
  const idx = s.toLowerCase().lastIndexOf('/r/');
  if (idx >= 0) s = s.slice(idx + 3);
  else if (s.toLowerCase().startsWith('r/')) s = s.slice(2);
  s = (s.match(/^[A-Za-z0-9_]+/) || [''])[0];
  return s.length >= 2 && s.length <= 40 ? s : null;
}

const lc = (v) => String(v == null ? '' : v).toLowerCase();

/** Case-insensitive uniq that KEEPS the first spelling it saw (the host is
 *  case-insensitive; the player is not, and "EarthPorn" should stay pretty). */
export function uniqCI(list) {
  const seen = new Set();
  const out = [];
  for (const raw of (Array.isArray(list) ? list : [])) {
    const s = String(raw == null ? '' : raw).trim();
    if (!s) continue;
    const k = s.toLowerCase();
    if (seen.has(k)) continue;
    seen.add(k);
    out.push(s);
  }
  return out;
}

const hasCI = (list, name) => (Array.isArray(list) ? list : []).some((x) => lc(x) === lc(name));

/**
 * Normalise whatever `ctx.assets.catalog()` answers into the shape the door
 * relies on. A host that predates any of these fields is not a failure: it is
 * a campus with fewer doors open.
 */
export function readCatalog(assets) {
  let raw = {};
  try {
    if (assets && typeof assets.catalog === 'function') raw = assets.catalog() || {};
  } catch (e) { raw = {}; }
  const arr = (v) => (Array.isArray(v) ? v : []);
  return {
    remoteCatalog: arr(raw.remoteCatalog)
      .map((n) => ({
        id: String((n && n.id) || ''),
        label: String((n && (n.label || n.id)) || ''),
        subs: uniqCI(arr(n && n.subs)),
      }))
      .filter((n) => n.id && n.subs.length),
    subLibrary: normalizeLibrary(raw.subLibrary),
    localFolders: arr(raw.localFolders)
      .map((f) => ({
        path: String((f && f.path) || '').replace(/\\/g, '/').replace(/^\/+|\/+$/g, ''),
        gifs: Number((f && f.gifs) || 0) || 0,
        stills: Number((f && f.stills) || 0) || 0,
        videos: Number((f && f.videos) || 0) || 0,
      }))
      .filter((f) => f.path),
    assetPresets: arr(raw.assetPresets)
      .map((p) => ({ id: String((p && p.id) || ''), name: String((p && (p.name || p.id)) || '') }))
      .filter((p) => p.id),
    remoteConsent: raw.remoteConsent === true,
    remoteMediaEnabled: raw.remoteMediaEnabled !== false,
    mediaSource: typeof raw.mediaSource === 'string' ? raw.mediaSource : 'local',
  };
}

/** `library` arrives as an array of rows, or wrapped, or as bare names from an
 *  older host. Accept all three (the fyp popover learned this lesson first). */
export function normalizeLibrary(value) {
  const list = Array.isArray(value) ? value
    : (value && Array.isArray(value.subLibrary) ? value.subLibrary : []);
  const out = [];
  const seen = new Set();
  for (const raw of list) {
    const isObj = raw && typeof raw === 'object';
    const name = String((isObj ? raw.name : raw) || '').trim();
    if (!name || seen.has(name.toLowerCase())) continue;
    seen.add(name.toLowerCase());
    out.push({
      name,
      ok: isObj && (raw.ok === true || raw.ok === false) ? raw.ok : null,
      videoCount: isObj && Number.isFinite(Number(raw.videoCount)) ? Number(raw.videoCount) : null,
      stillOnly: isObj ? raw.stillOnly === true : false,
    });
  }
  return out;
}

const folderMedia = (f) => (f.gifs || 0) + (f.stills || 0) + (f.videos || 0);

/**
 * Which doors step 1 may open.
 *   online  remote consent AND the app is not pinned to local media
 *   local   an asset preset, or two media folders of which at least one is not
 *           a bare root (a tree that is only images/ + videos/ has no second
 *           pile in it, which is exactly the QUICK SORT case)
 */
export function sourceOptions(cat) {
  const mediaFolders = cat.localFolders.filter((f) => folderMedia(f) > 0);
  const subFolders = mediaFolders.filter((f) => ROOT_FOLDERS.indexOf(f.path) < 0);
  const online = cat.remoteConsent === true && cat.mediaSource !== 'local';
  const local = cat.assetPresets.length >= 1 || (subFolders.length >= 1 && mediaFolders.length >= 2);
  return { online, local, quickOnly: !online && !local, mediaFolders, subFolders };
}

/** Shape-only validity of a persisted blob. A stale one still has to RESOLVE. */
export function isValidSetup(setup) {
  if (!setup || typeof setup !== 'object') return false;
  if (Number(setup.v) !== SETUP_VERSION) return false;
  if (['remote', 'local', 'quick'].indexOf(setup.source) < 0) return false;
  const side = (s) => {
    if (!s || typeof s !== 'object') return false;
    const n = Array.isArray(s.niches) ? s.niches.length : 0;
    const u = Array.isArray(s.subs) ? s.subs.length : 0;
    const f = Array.isArray(s.folders) ? s.folders.length : 0;
    return n + u + f > 0 || !!s.presetId;
  };
  return side(setup.target) && side(setup.noise);
}

function nicheSubs(cat, ids) {
  const want = new Set((Array.isArray(ids) ? ids : []).map(String));
  const out = [];
  for (const n of cat.remoteCatalog) if (want.has(n.id)) out.push(...n.subs);
  return out;
}

/**
 * SORT_SETUP v1 -> the provider's `sources` rows (contract section 2).
 *
 *   remote  one row per tag, subs = uniq(flatten(niches) + subs). Any sub that
 *           is on the TARGET side is removed from the noise row, and `hot` is
 *           true when one of those came from a noise NICHE (the player asked
 *           for two niches that share ground: hot, not refused).
 *   local   one row per tag, folders OR presetId. The same folder or the same
 *           preset on both sides is REFUSED, not silently merged.
 *   quick   both tags served from the same roots; the truth is row.kind, and
 *           `quick` tells G1 to judge LIVE vs STILL instead of the tag.
 *
 * Pure, and the door's only bridge between the blob and the deck.
 * @returns {{ok, reason, sources, hot, quick, target, noise, dropped}}
 */
export function resolveSetup(setup, cat) {
  const bad = (reason) => ({ ok: false, reason, sources: [], hot: false, quick: false, dropped: [] });
  if (!isValidSetup(setup)) return bad('shape');

  if (setup.source === 'quick') {
    const roots = uniqCI((setup.target && setup.target.folders) || []);
    if (!roots.length) return bad('empty');
    return {
      ok: true, reason: '', quick: true, hot: false, dropped: [],
      target: { folders: roots.slice() }, noise: { folders: roots.slice() },
      sources: [
        { tag: 'target', kind: 'local', folders: roots.slice() },
        { tag: 'noise', kind: 'local', folders: roots.slice() },
      ],
    };
  }

  if (setup.source === 'local') {
    const rowFor = (tag, side) => {
      if (side && side.presetId) return { tag, kind: 'local', presetId: String(side.presetId) };
      const folders = uniqCI((side && side.folders) || []);
      return folders.length ? { tag, kind: 'local', folders } : null;
    };
    const a = rowFor('target', setup.target);
    const b = rowFor('noise', setup.noise);
    if (!a || !b) return bad('empty');
    if (a.presetId && b.presetId && a.presetId === b.presetId) return bad('same');
    if (a.folders && b.folders) {
      const overlap = a.folders.filter((p) => hasCI(b.folders, p));
      if (overlap.length) return bad('same');
    }
    return {
      ok: true, reason: '', quick: false, hot: false, dropped: [],
      target: a.presetId ? { presetId: a.presetId } : { folders: a.folders.slice() },
      noise: b.presetId ? { presetId: b.presetId } : { folders: b.folders.slice() },
      sources: [a, b],
    };
  }

  /* ---- remote ---------------------------------------------------------- */
  const tgtFromNiche = nicheSubs(cat, setup.target.niches);
  const targetSubs = uniqCI([...tgtFromNiche, ...((setup.target.subs) || [])]);
  const noiseFromNiche = uniqCI(nicheSubs(cat, setup.noise.niches));
  const noiseAll = uniqCI([...noiseFromNiche, ...((setup.noise.subs) || [])]);

  const dropped = noiseAll.filter((s) => hasCI(targetSubs, s));
  const noiseSubs = noiseAll.filter((s) => !hasCI(targetSubs, s));
  /* HOT is about NICHES: two hand-typed subs that collide are the player's own
   * business, but two CATALOG niches sharing a sub means the piles overlap by
   * design and the room is about to get hard. */
  const hot = dropped.some((s) => hasCI(noiseFromNiche, s));

  if (!targetSubs.length || !noiseSubs.length) return bad(noiseSubs.length ? 'empty' : 'same');
  return {
    ok: true, reason: '', quick: false, hot, dropped,
    target: { subs: targetSubs }, noise: { subs: noiseSubs },
    sources: [
      { tag: 'target', kind: 'remote', subs: targetSubs },
      { tag: 'noise', kind: 'remote', subs: noiseSubs },
    ],
  };
}

/** Human labels for the "same sort" line: niche labels, then sub names, then
 *  folder tails, capped at three a side with a "+N" tail. */
export function summarize(setup, cat, cap) {
  const max = Number.isFinite(cap) ? cap : 3;
  const side = (s) => {
    if (!s) return { names: [], more: 0 };
    const names = [];
    for (const id of (s.niches || [])) {
      const row = cat.remoteCatalog.find((n) => n.id === String(id));
      names.push(row ? row.label : String(id));
    }
    for (const sub of (s.subs || [])) names.push(String(sub));
    for (const f of (s.folders || [])) {
      const parts = String(f).split('/');
      names.push(parts[parts.length - 1] || String(f));
    }
    if (s.presetId) {
      const p = cat.assetPresets.find((x) => x.id === String(s.presetId));
      names.push(p ? p.name : String(s.presetId));
    }
    return { names: names.slice(0, max), more: Math.max(0, names.length - max) };
  };
  return { target: side(setup && setup.target), noise: side(setup && setup.noise) };
}

/* --------------------------------------------------------------- the DOM -- */

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}
function attr(node, name, value) {
  try { if (node && typeof node.setAttribute === 'function') node.setAttribute(name, String(value)); }
  catch (e) { /* the DOM double may not carry attributes - never fatal */ }
}
function focusSoon(node) {
  try { if (node && typeof node.focus === 'function') node.focus(); } catch (e) { /* noop */ }
}
function isVideoRow(row) {
  if (!row) return false;
  if (typeof row.mime === 'string' && row.mime.toLowerCase().indexOf('video/') === 0) return true;
  return /\.(mp4|webm|m4v)(\?|#|$)/i.test(String(row.url || ''));
}

/* ========================================================================== *
 * createSetupDoor
 * ========================================================================== */
/**
 * @param {Object}   o
 * @param {Object}   o.ctx        the shell class ctx (store, platform, motion,
 *                                hideTutorial, exits, log)
 * @param {Function=} o.t         t(key, fallback); falls back to ctx.lexicon
 * @param {Object=}  o.mount      node to append the door to (ctx.root)
 * @param {Object=}  o.existing   the persisted SORT_SETUP v1, or null
 * @param {Object=}  o.assets     ctx.assets (catalog / probeSub /
 *                                removeLibrarySub / onLibrary)
 * @param {Function} o.onPlay     (setup, resolved) - G1 saves + claims
 * @param {Function} o.onLeave    the player took the door back out
 * @param {Function=} o.cue       (name, level, extra) - THE GAME'S clamped cue
 *                                helper, threaded in (W3 P0-20). The door owns
 *                                no audio node and imports no mixer; without
 *                                the closure it is simply silent, as it was.
 * @returns {{el, destroy, setBusy, ghost, warnThin, step, diagnostics}}
 */
export function createSetupDoor(o) {
  const opts = o || {};
  const ctx = opts.ctx || {};
  const assets = opts.assets || (ctx && ctx.assets) || {};
  const onPlay = typeof opts.onPlay === 'function' ? opts.onPlay : () => {};
  const onLeave = typeof opts.onLeave === 'function' ? opts.onLeave : () => {};
  const say = (m) => { try { if (typeof ctx.log === 'function') ctx.log('door: ' + m); } catch (e) { /* noop */ } };
  /* W3 P0-20: the door was the one surface in the school with ZERO cue sites -
   * five steps of picking, probing and pressing, all of it silent. G1 hands
   * this in; a missing closure is a silent door, never a thrown one. */
  const cue = (name, level, extra) => {
    if (typeof opts.cue !== 'function' || destroyed) return;
    try { opts.cue(name, level, extra); } catch (e) { /* a cue never opens a door */ }
  };

  const t = (key, fallback) => {
    const dflt = fallback == null ? SETUP_LEX[key] : fallback;
    try {
      if (typeof opts.t === 'function') return opts.t(key, dflt);
      if (typeof ctx.lexicon === 'function') return ctx.lexicon(key, dflt);
    } catch (e) { /* fall through */ }
    return dflt == null ? key : dflt;
  };

  const isTouch = !!(ctx.platform && ctx.platform.isTouch);
  const reduced = !!(ctx.motion && ctx.motion.reducedMotion);

  /* ---- once-per-install hand holding --------------------------------- */
  const meta = (() => {
    try { return (ctx.store && typeof ctx.store.gameMeta === 'function' && ctx.store.gameMeta('sort')) || {}; }
    catch (e) { return {}; }
  })();
  const teach = ctx.hideTutorial !== true && meta.doorSeen !== true;

  /* ---- the world the door is picking out of --------------------------- */
  let cat = readCatalog(assets);
  let opt = sourceOptions(cat);

  /* ---- state ----------------------------------------------------------- */
  const blankSide = () => ({ niches: [], subs: [], folders: [], presetId: null });
  const sel = { source: null, target: blankSide(), noise: blankSide() };
  let step = 'source';
  let staleNote = false;
  let errKey = '';
  let thinTags = null;
  let busyKey = '';
  let destroyed = false;
  let played = false;

  const probeState = { name: '', state: 'idle', side: '' };   // idle|probing|ok|missing|bad|dupe
  const deadStarters = new Set();
  const probingStarters = new Set();
  let debounceId = null;
  let searchText = '';

  const existing = opts.existing;
  if (isValidSetup(existing) && resolveSetup(existing, cat).ok) step = 'same';
  else if (existing) staleNote = true;
  if (step !== 'same' && opt.quickOnly) sel.source = 'quick';

  /* ---- listeners + timers (destroy() has to leave nothing behind) ------- */
  const perm = [];
  let trans = [];
  function on(node, type, fn, permanent) {
    if (!node || typeof node.addEventListener !== 'function') return node;
    node.addEventListener(type, fn);
    (permanent ? perm : trans).push([node, type, fn]);
    return node;
  }
  function unbind(list) {
    for (const row of list) { try { row[0].removeEventListener(row[1], row[2]); } catch (e) { /* noop */ } }
    list.length = 0;
  }
  const timers = new Set();
  function later(ms, fn) {
    const st = globalThis.setTimeout;
    const id = st(() => { timers.delete(id); if (!destroyed) { try { fn(); } catch (e) { say('timer threw'); } } },
      Math.max(0, Math.round(ms) || 0));
    timers.add(id);
    return id;
  }
  function unlater(id) {
    if (id == null) return;
    timers.delete(id);
    try { globalThis.clearTimeout(id); } catch (e) { /* noop */ }
  }
  function killTimers() {
    for (const id of Array.from(timers)) { try { globalThis.clearTimeout(id); } catch (e) { /* noop */ } }
    timers.clear();
  }

  /* ---- the library push ------------------------------------------------ */
  let offLibrary = null;
  try {
    if (typeof assets.onLibrary === 'function') {
      const ret = assets.onLibrary((payload) => {
        if (destroyed) return;
        cat = Object.assign({}, cat, { subLibrary: normalizeLibrary(payload) });
        opt = sourceOptions(cat);
        pruneToLibrary();
        render();
      });
      if (typeof ret === 'function') offLibrary = ret;
    }
  } catch (e) { say('onLibrary unavailable'); }

  /** A sub the host dropped from the library leaves BOTH piles in the same
   *  gesture. "Added once, X everywhere" cuts this way too. */
  function pruneToLibrary() {
    const known = (name) => hasCI(cat.subLibrary.map((r) => r.name), name)
      || cat.remoteCatalog.some((n) => hasCI(n.subs, name));
    for (const side of ['target', 'noise']) {
      sel[side].subs = sel[side].subs.filter((s) => known(s));
    }
  }

  /* ---- the shell ------------------------------------------------------- */
  const root = el('div', 'g-sort-door');
  attr(root, 'data-step', step);
  if (isTouch) attr(root, 'data-touch', '1');
  if (reduced) attr(root, 'data-reduced', '1');

  const head = el('div', 'g-sort-head');
  head.appendChild(el('h2', 'g-sort-title', t('sort_door_title')));
  head.appendChild(el('p', 'g-sort-sub', t('sort_door_sub')));
  const tutLine = el('p', 'g-sort-tut');
  tutLine.hidden = !teach;
  head.appendChild(tutLine);
  const rail = el('div', 'g-sort-rail');
  const lamps = {};
  for (const key of ['source', 'target', 'noise']) {
    const lamp = el('span', 'g-sort-lamp', t('sort_step_' + key));
    lamps[key] = lamp;
    rail.appendChild(lamp);
  }
  head.appendChild(rail);
  root.appendChild(head);

  const body = el('div', 'g-sort-body');
  root.appendChild(body);

  const foot = el('div', 'g-sort-foot');
  root.appendChild(foot);

  let busyEl = null;

  if (opts.mount && typeof opts.mount.appendChild === 'function') opts.mount.appendChild(root);

  /* ------------------------------------------------------------ helpers -- */

  function clear(node) {
    while (node.children && node.children.length) node.removeChild(node.children[node.children.length - 1]);
    node.textContent = '';
  }

  /** Arrow keys walk a chip group. Tab still leaves it, Enter/Space still
   *  activate: this is an addition to native behaviour, never a replacement. */
  function wireArrows(group) {
    group._sdChips = [];
    on(group, 'keydown', (ev) => {
      const k = ev && ev.key;
      if (k !== 'ArrowLeft' && k !== 'ArrowRight' && k !== 'ArrowUp' && k !== 'ArrowDown') return;
      const items = (group._sdChips || []).filter((n) => n && !n.disabled);
      const i = items.indexOf(ev.target);
      if (i < 0) return;
      const d = (k === 'ArrowRight' || k === 'ArrowDown') ? 1 : -1;
      focusSoon(items[(i + d + items.length) % items.length]);
      if (typeof ev.preventDefault === 'function') ev.preventDefault();
    });
    return group;
  }
  /**
   * One chip. `extra.host` seats it inside a wrapper (the library pill's X is a
   * SIBLING button, never a child: a button inside a button is invalid content
   * in a real browser and a screen reader cannot address the inner one). Arrow
   * navigation always registers against the GROUP, wrapper or not.
   */
  function chip(group, label, on1, handler, extra) {
    const b = el('button', 'g-sort-chip');
    b.type = 'button';
    attr(b, 'data-on', on1 ? '1' : '0');
    attr(b, 'aria-pressed', on1 ? 'true' : 'false');
    b.appendChild(el('span', 'g-sort-chip-t', label));
    if (extra && extra.count != null) b.appendChild(el('span', 'g-sort-count', extra.count));
    if (extra && extra.lib) attr(b, 'data-lib', '1');
    if (extra && extra.state) attr(b, 'data-state', extra.state);
    if (extra && extra.disabled) b.disabled = true;
    if (extra && extra.title) attr(b, 'title', extra.title);
    /* W3 P0-20: every chip in the door answers. Chrome-quiet - a chip is a
     * choice being noted, not a verdict - and the wrapper is here rather than
     * at the six call sites so a chip added later cannot be born mute. */
    if (handler) on(b, 'click', () => { cue('blip', 0.15); handler(); });
    ((extra && extra.host) || group).appendChild(b);
    (group._sdChips || (group._sdChips = [])).push(b);
    return b;
  }

  const sideOf = (name) => sel[name === 'noise' ? 'noise' : 'target'];
  function toggleIn(list, value) {
    const i = list.findIndex((x) => lc(x) === lc(value));
    if (i >= 0) list.splice(i, 1); else list.push(value);
  }

  /* --------------------------------------------------------- the pickers -- */

  function buildRemotePicker(panel, sideName) {
    const side = sideOf(sideName);
    const other = sideOf(sideName === 'noise' ? 'target' : 'noise');

    /* catalog niches: FIXED chips, never library rows */
    if (cat.remoteCatalog.length) {
      panel.appendChild(el('p', 'g-sort-sub-h', t('sort_catalog_head')));
      const g = wireArrows(el('div', 'g-sort-chips'));
      for (const n of cat.remoteCatalog) {
        chip(g, n.label, hasCI(side.niches, n.id), () => {
          toggleIn(side.niches, n.id);
          errKey = '';
          render();
        }, { count: n.subs.length });
      }
      panel.appendChild(g);
    }

    /* THE STARTER ROW OFFERS WHAT THE LIBRARY DOES NOT. A starter that is
     * already a library row was drawn twice on this step - once as an EASY
     * NOISE chip and again as a MY LIBRARY pill - and the two chips toggled the
     * same name, so the step asked the same question in two places and answered
     * itself in both. A sub in the library renders ONLY as its library pill
     * (which is the richer control: it carries the clip count and the X). */
    if (sideName === 'noise') {
      const fresh = STARTER_NOISE.filter(
        (name) => !cat.subLibrary.some((r) => lc(r.name) === lc(name)));
      if (fresh.length) {
        panel.appendChild(el('p', 'g-sort-sub-h', t('sort_starter_head')));
        const g = wireArrows(el('div', 'g-sort-chips'));
        for (const name of fresh) {
          const dead = deadStarters.has(lc(name));
          const busy = probingStarters.has(lc(name));
          chip(g, 'r/' + name, hasCI(side.subs, name), () => pickStarter(name, sideName), {
            disabled: dead || busy,
            state: dead ? 'dead' : busy ? 'probing' : '',
            title: dead ? t('sort_probe_missing') : '',
          });
        }
        panel.appendChild(g);
        panel.appendChild(el('p', 'g-sort-hint', t('sort_starter_hint')));
      }
    }

    /* the library: ORANGE pills, a clip count, and an X that removes the sub
     * from the library everywhere (feed selection included, host side). */
    panel.appendChild(el('p', 'g-sort-sub-h', t('sort_lib_head')));
    if (!cat.subLibrary.length) {
      panel.appendChild(el('p', 'g-sort-hint', t('sort_lib_empty')));
    } else {
      const g = wireArrows(el('div', 'g-sort-chips'));
      for (const row of cat.subLibrary) {
        const taken = hasCI(other.subs, row.name) && sideName === 'noise';
        const count = row.stillOnly ? t('sort_stills_only')
          : (row.videoCount == null ? '' : row.videoCount + ' ' + t('sort_clips'));
        const title = row.ok === false ? t('sort_missing')
          : row.ok === null ? t('sort_unverified') : t('sort_verified');
        const holder = el('span', 'g-sort-pill');
        g.appendChild(holder);
        chip(g, 'r/' + row.name, hasCI(side.subs, row.name), () => {
          toggleIn(side.subs, row.name);
          errKey = '';
          render();
        }, { lib: row.ok !== false, count: count || null, title, disabled: row.ok === false || taken, host: holder });
        if (typeof assets.removeLibrarySub === 'function') {
          const x = el('button', 'g-sort-x', '✕');
          x.type = 'button';
          attr(x, 'title', t('sort_remove'));
          attr(x, 'aria-label', t('sort_remove') + ' r/' + row.name);
          on(x, 'click', (ev) => {
            if (ev && typeof ev.stopPropagation === 'function') ev.stopPropagation();
            removeFromLibrary(row.name);
          });
          holder.appendChild(x);
          g._sdChips.push(x);
        }
      }
      panel.appendChild(g);
    }

    /* the search box */
    if (typeof assets.probeSub === 'function') {
      panel.appendChild(el('p', 'g-sort-sub-h', t('sort_search_head')));
      const wrap = el('div', 'g-sort-search');
      const input = el('input', 'g-sort-input');
      attr(input, 'type', 'text');
      attr(input, 'placeholder', t('sort_search_ph'));
      attr(input, 'aria-label', t('sort_search_head'));
      input.value = searchText;
      const add = el('button', 'btn ghost', t('sort_search_btn'));
      add.type = 'button';
      on(input, 'input', () => {
        searchText = String(input.value || '');
        unlater(debounceId);
        debounceId = later(PROBE_DEBOUNCE_MS, () => { debounceId = null; submitSearch(sideName, true); });
      });
      on(input, 'keydown', (ev) => {
        if (!ev || ev.key !== 'Enter') return;
        if (typeof ev.preventDefault === 'function') ev.preventDefault();
        unlater(debounceId); debounceId = null;
        submitSearch(sideName, false);
      });
      on(add, 'click', () => { unlater(debounceId); debounceId = null; submitSearch(sideName, false); });
      wrap.appendChild(input);
      wrap.appendChild(add);
      panel.appendChild(wrap);
      lastInput = input;

      const note = el('p', 'g-sort-note');
      attr(note, 'data-state', probeState.state);
      note.textContent = probeNote();
      panel.appendChild(note);
    }
  }

  function probeNote() {
    switch (probeState.state) {
      case 'probing': return t('sort_probe_probing') + ' r/' + probeState.name;
      case 'ok': return 'r/' + probeState.name + ' ' + t('sort_probe_ok');
      case 'missing': return 'r/' + probeState.name + ' ' + t('sort_probe_missing');
      case 'bad': return t('sort_probe_bad');
      case 'dupe': return t('sort_probe_dupe');
      default: return '';
    }
  }

  function buildLocalPicker(panel, sideName) {
    const side = sideOf(sideName);
    const other = sideOf(sideName === 'noise' ? 'target' : 'noise');

    panel.appendChild(el('p', 'g-sort-sub-h', t('sort_folders_head')));
    const list = wireArrows(el('div', 'g-sort-folders'));
    for (const f of opt.mediaFolders) {
      const taken = hasCI(other.folders, f.path);
      const b = el('button', 'g-sort-folder');
      b.type = 'button';
      const on1 = hasCI(side.folders, f.path);
      attr(b, 'data-on', on1 ? '1' : '0');
      attr(b, 'aria-pressed', on1 ? 'true' : 'false');
      if (taken || side.presetId) { b.disabled = true; attr(b, 'title', t('sort_folder_taken')); }
      b.appendChild(el('span', 'g-sort-folder-path', f.path));
      b.appendChild(el('span', 'g-sort-count', folderMedia(f) + ' ' + t('sort_counts')));
      on(b, 'click', () => { toggleIn(side.folders, f.path); errKey = ''; render(); });
      list.appendChild(b);
      list._sdChips.push(b);
    }
    panel.appendChild(list);

    if (cat.assetPresets.length) {
      panel.appendChild(el('p', 'g-sort-sub-h', t('sort_presets_head')));
      const g = wireArrows(el('div', 'g-sort-chips'));
      chip(g, t('sort_preset_none'), !side.presetId, () => { side.presetId = null; errKey = ''; render(); });
      for (const p of cat.assetPresets) {
        const taken = other.presetId === p.id;
        chip(g, p.name, side.presetId === p.id, () => {
          side.presetId = side.presetId === p.id ? null : p.id;
          if (side.presetId) side.folders = [];
          errKey = '';
          render();
        }, { disabled: taken, title: taken ? t('sort_folder_taken') : '' });
      }
      panel.appendChild(g);
    }
  }

  /* ------------------------------------------------------- probe + remove -- */

  function submitSearch(sideName, quiet) {
    pendingFocus = 'search';
    const raw = String(searchText || '').trim();
    if (!raw) { if (!quiet) { probeState.state = 'idle'; render(); } return; }
    const clean = sanitizeSub(raw);
    if (!clean) {
      if (quiet && raw.length < 2) return;
      /* W3 P0-20: a refusal, and refusals in this school are quiet. */
      probeState.name = raw; probeState.state = 'bad'; cue('bump', 0.08); render(); return;
    }
    const side = sideOf(sideName);
    if (hasCI(side.subs, clean)) { probeState.name = clean; probeState.state = 'dupe'; cue('bump', 0.08); render(); return; }
    const known = cat.subLibrary.find((r) => lc(r.name) === lc(clean) && r.ok === true);
    if (known) {
      side.subs.push(known.name);
      searchText = '';
      probeState.name = known.name; probeState.state = 'ok';
      cue('chime', 0.3);          // W3 P0-20: the library already had it
      render();
      return;
    }
    probeState.name = clean; probeState.state = 'probing'; probeState.side = sideName;
    render();
    runProbe(clean, sideName, false);
  }

  function pickStarter(name, sideName) {
    const side = sideOf(sideName);
    if (hasCI(side.subs, name)) { toggleIn(side.subs, name); render(); return; }
    const known = cat.subLibrary.find((r) => lc(r.name) === lc(name) && r.ok === true);
    if (known) { side.subs.push(known.name); errKey = ''; render(); return; }
    probingStarters.add(lc(name));
    render();
    runProbe(name, sideName, true);
  }

  function runProbe(name, sideName, fromStarter) {
    let p = null;
    /* THE SCOPE (2026-08-28). This add is the SORTING ROOM's, and the side it
     * lands on decides what it may become. A `noise` pick is the decoy heap:
     * the provider keeps it on the library shelf so the door can offer it again
     * tomorrow, and fences it out of the app-wide feed that every OTHER class's
     * `claim()` draws from - "the cat and pokemon feeds I added for the sorting
     * room followed me through to all the other games" is the bug that closes.
     * A `target` pick is content the player chose, so it is added exactly the
     * way the Media counter's own box adds one. Both fields are optional and a
     * provider that predates them ignores them. */
    try {
      p = assets.probeSub(name, { scope: 'sort', pile: sideName === 'noise' ? 'noise' : 'target' });
    } catch (e) { p = null; }
    Promise.resolve(p).then((res) => {
      if (destroyed) return;
      probingStarters.delete(lc(name));
      const ok = !!(res && res.ok);
      const answered = String((res && res.name) || name);
      if (!ok) {
        if (fromStarter) deadStarters.add(lc(name));
        if (probeState.name === name || !fromStarter) { probeState.name = answered; probeState.state = 'missing'; }
        if (!fromStarter) pendingFocus = 'search';
        cue('bump', 0.08);        // W3 P0-20: there is no such sub
        render();
        return;
      }
      /* An ok probe is a library row: the host has already written it, and the
       * `library` push will confirm - but the pill has to exist NOW. */
      if (!cat.subLibrary.some((r) => lc(r.name) === lc(answered))) {
        cat = Object.assign({}, cat, {
          subLibrary: cat.subLibrary.concat([{
            name: answered,
            ok: true,
            videoCount: Number.isFinite(Number(res.videoCount)) ? Number(res.videoCount) : null,
            stillOnly: res.stillOnly === true,
          }]),
        });
      }
      const side = sideOf(sideName);
      if (!hasCI(side.subs, answered)) side.subs.push(answered);
      if (!fromStarter) { searchText = ''; probeState.name = answered; probeState.state = 'ok'; pendingFocus = 'search'; }
      errKey = '';
      /* W3 P0-20: the probe came back and the pill is real. The one payoff
       * beat in the door, so it is the only thing in here above chrome. */
      cue('chime', 0.3);
      render();
    }).catch(() => {
      if (destroyed) return;
      probingStarters.delete(lc(name));
      probeState.state = 'missing';
      cue('bump', 0.08);          // W3 P0-20: the probe never answered
      render();
    });
  }

  function removeFromLibrary(name) {
    /* Optimistic: the pill goes NOW, and the host's `library` push confirms.
     * A failed remove simply re-appears on the next push, which is the honest
     * outcome - the alternative is a pill that lies about being gone. */
    cat = Object.assign({}, cat, { subLibrary: cat.subLibrary.filter((r) => lc(r.name) !== lc(name)) });
    for (const s of ['target', 'noise']) sel[s].subs = sel[s].subs.filter((x) => lc(x) !== lc(name));
    render();
    try {
      const p = assets.removeLibrarySub(name);
      if (p && typeof p.catch === 'function') p.catch(() => say('removeLibrarySub failed for ' + name));
    } catch (e) { say('removeLibrarySub threw'); }
  }

  /* ------------------------------------------------------------ the steps -- */

  function spice() {
    if (sel.source !== 'remote') return null;
    const n = sel.noise;
    if (!n.niches.length && !n.subs.length) return null;
    if (n.niches.length) return 'hot';
    if (n.subs.every((s) => hasCI(STARTER_NOISE, s))) return 'mild';
    return 'mid';
  }

  function buildSetup() {
    const iso = new Date().toISOString();
    if (sel.source === 'quick') {
      const roots = opt.mediaFolders.map((f) => f.path);
      const use = roots.length ? roots : ROOT_FOLDERS.slice();
      return { v: SETUP_VERSION, source: 'quick', target: { folders: use.slice() }, noise: { folders: use.slice() }, updatedAt: iso };
    }
    const side = (s) => {
      if (sel.source === 'local') return s.presetId ? { presetId: s.presetId } : { folders: uniqCI(s.folders) };
      return { niches: s.niches.slice(), subs: uniqCI(s.subs).slice(0, SUB_CAP_PER_SIDE) };
    };
    return {
      v: SETUP_VERSION,
      source: sel.source === 'local' ? 'local' : 'remote',
      target: side(sel.target),
      noise: side(sel.noise),
      updatedAt: iso,
    };
  }

  function stepSource(host) {
    tutLine.textContent = t('sort_tut_rule');
    if (opt.quickOnly) {
      const panel = el('div', 'g-sort-panel');
      panel.appendChild(el('h3', 'g-sort-h', t('sort_quick_head')));
      panel.appendChild(el('p', 'g-sort-hint', t('sort_quick_rule')));
      const strip = el('div', 'g-sort-strip');
      strip.appendChild(el('span', 'g-sort-strip-txt', t('sort_quick_nag')));
      panel.appendChild(strip);
      host.appendChild(panel);
      return;
    }
    const doors = el('div', 'g-sort-doors');
    const mk = (kind, nameKey, hintKey, offKey, live) => {
      const b = el('button', 'g-sort-door-card');
      b.type = 'button';
      const glyph = el('i', 'g-sort-glyph');
      attr(glyph, 'data-kind', kind === 'remote' ? 'web' : 'folder');
      attr(glyph, 'aria-hidden', 'true');
      b.appendChild(glyph);
      b.appendChild(el('span', 'g-sort-door-name', t(nameKey)));
      b.appendChild(el('span', 'g-sort-door-why', live ? t(hintKey) : t(offKey)));
      if (!live) b.disabled = true;
      else on(b, 'click', () => { sel.source = kind; errKey = ''; step = 'target'; render(); });
      if (sel.source === kind) attr(b, 'data-on', '1');
      doors.appendChild(b);
      return b;
    };
    mk('remote', 'sort_source_online', 'sort_source_online_hint', 'sort_source_online_off', opt.online);
    mk('local', 'sort_source_local', 'sort_source_local_hint', 'sort_source_local_off', opt.local);
    host.appendChild(doors);
    if (staleNote) host.appendChild(strip('err', t('sort_stale')));
  }

  function strip(kind, text, action) {
    const s = el('div', 'g-sort-strip');
    attr(s, 'data-kind', kind);
    s.appendChild(el('span', 'g-sort-strip-txt', text));
    if (action) {
      const b = el('button', 'btn ghost', action.label);
      b.type = 'button';
      on(b, 'click', action.run);
      s.appendChild(b);
    }
    return s;
  }

  function stepPile(host, sideName) {
    tutLine.textContent = sideName === 'target' ? t('sort_tut_pick') : t('sort_tut_ghost');
    const panel = el('div', 'g-sort-panel');
    attr(panel, 'data-side', sideName);
    panel.appendChild(el('h3', 'g-sort-h', t('sort_' + sideName + '_head')));
    panel.appendChild(el('p', 'g-sort-hint', t('sort_' + sideName + '_hint')));
    if (sel.source === 'local') buildLocalPicker(panel, sideName);
    else buildRemotePicker(panel, sideName);
    host.appendChild(panel);

    if (sideName === 'noise') {
      const heat = spice();
      if (heat) {
        const s = strip('spice', t('sort_spice_' + heat));
        attr(s, 'data-heat', heat);
        host.appendChild(s);
      }
      const probe = resolveSetup(buildSetup(), cat);
      if (probe.ok && probe.dropped && probe.dropped.length) {
        host.appendChild(strip('spice', t('sort_overlap_note') + ': r/' + probe.dropped.join(', r/')));
      }
    }
  }

  function stepGhost(host, rows, done) {
    tutLine.textContent = t('sort_tut_ghost');
    const stage = el('div', 'g-sort-ghost');
    const mk = (tag, row) => {
      const card = el('div', 'g-sort-gcard');
      attr(card, 'data-tag', tag);
      attr(card, 'data-stamp', '0');
      if (row && row.url) {
        let media;
        if (isVideoRow(row)) {
          media = el('video', 'g-sort-gmedia');
          media.muted = true; media.loop = true; media.autoplay = true;
          attr(media, 'muted', 'true'); attr(media, 'loop', 'true');
          attr(media, 'playsinline', 'true'); attr(media, 'autoplay', 'true');
          media.src = row.url;
          try { const p = media.play(); if (p && p.catch) p.catch(() => {}); } catch (e) { /* noop */ }
        } else {
          media = el('img', 'g-sort-gmedia');
          attr(media, 'alt', '');
          media.src = row.url;
        }
        card.appendChild(media);
        card._sdMedia = media;
      }
      card.appendChild(el('span', 'g-sort-gstamp', t(tag === 'target' ? 'sort_stamp_yes' : 'sort_stamp_no')));
      card.appendChild(el('span', 'g-sort-gtag', t(tag === 'target' ? 'sort_ghost_target' : 'sort_ghost_noise')));
      stage.appendChild(card);
      return card;
    };
    const a = mk('target', rows && rows.target);
    const b = mk('noise', rows && rows.noise);
    host.appendChild(el('p', 'g-sort-sub-h', t('sort_ghost_head')));
    host.appendChild(stage);

    if (reduced) {
      attr(a, 'data-stamp', '1');
      attr(b, 'data-stamp', '1');
      attr(a, 'data-fly', 'right');
      attr(b, 'data-fly', 'left');
      later(GHOST.REDUCED_DONE_MS, done);
      return;
    }
    later(GHOST.STAMP_A_MS, () => attr(a, 'data-stamp', '1'));
    later(GHOST.FLY_A_MS, () => attr(a, 'data-fly', 'right'));
    later(GHOST.STAMP_B_MS, () => attr(b, 'data-stamp', '1'));
    later(GHOST.FLY_B_MS, () => attr(b, 'data-fly', 'left'));
    later(GHOST.DONE_MS, done);
  }

  function stepSame(host) {
    tutLine.hidden = true;
    const wrap = el('div', 'g-sort-same');
    const s = summarize(existing, cat);
    const line = el('p', 'g-sort-summary');
    const push = (cls, part) => {
      const n = el(cls, null, part.names.join(' + ') + (part.more ? ' +' + part.more : ''));
      line.appendChild(n);
    };
    push('b', s.target);
    line.appendChild(el('em', null, t('sort_vs')));
    push('i', s.noise);
    wrap.appendChild(line);
    host.appendChild(wrap);
  }

  /* --------------------------------------------------------------- footer -- */

  function buildFoot() {
    clear(foot);
    const leave = el('button', 'btn ghost', t('sort_leave'));
    leave.type = 'button';
    on(leave, 'click', () => { if (!destroyed) onLeave(); });
    try { if (ctx.exits && typeof ctx.exits.sign === 'function') ctx.exits.sign(leave, { dir: 'back', quiet: true }); }
    catch (e) { /* an unsigned button is still an exit */ }
    foot.appendChild(leave);
    foot.appendChild(el('span', 'g-sort-spacer'));

    if (step === 'ghost') return null;

    if (step === 'same') {
      const change = el('button', 'btn ghost', t('sort_change'));
      change.type = 'button';
      on(change, 'click', () => { step = 'source'; staleNote = false; render(); });
      foot.appendChild(change);
      const same = el('button', 'btn primary', t('sort_same'));
      same.type = 'button';
      on(same, 'click', () => play(existing));
      foot.appendChild(same);
      return same;
    }

    if (step !== 'source') {
      const back = el('button', 'btn ghost', t('sort_back'));
      back.type = 'button';
      on(back, 'click', () => {
        step = step === 'noise' ? 'target' : 'source';
        errKey = '';
        render();
      });
      foot.appendChild(back);
    }

    /* THE SOURCE STEP HAS NO NEXT. It shipped one, permanently disabled, and it
     * would have done nothing if it were not: picking a door IS the advance.
     * A dead button on a step is a promise the step never meant to make. */
    if (step === 'source' && !opt.quickOnly) return null;

    const lastStep = step === 'noise' || (step === 'source' && opt.quickOnly);
    const primary = el('button', 'btn primary',
      lastStep ? (opt.quickOnly && step === 'source' ? t('sort_quick_head') : t('sort_play')) : t('sort_next'));
    primary.type = 'button';
    on(primary, 'click', () => {
      if (step === 'source' && opt.quickOnly) { sel.source = 'quick'; play(buildSetup()); return; }
      if (step === 'target') {
        if (!sidePicked('target')) { errKey = 'sort_need_pick'; render(); return; }
        step = 'noise'; errKey = ''; render(); return;
      }
      if (step === 'noise') {
        if (!sidePicked('noise')) { errKey = 'sort_need_pick'; render(); return; }
        play(buildSetup());
      }
    });
    foot.appendChild(primary);
    return step === 'source' ? null : primary;
  }

  function sidePicked(name) {
    const s = sel[name];
    if (sel.source === 'local') return !!s.presetId || s.folders.length > 0;
    return s.niches.length > 0 || s.subs.length > 0;
  }

  function play(setup) {
    if (destroyed) return;
    const resolved = resolveSetup(setup, cat);
    if (!resolved.ok) {
      errKey = resolved.reason === 'same' ? 'sort_need_split' : 'sort_need_pick';
      if (step === 'same') { step = 'source'; staleNote = true; }
      cue('bump', 0.08);          // W3 P0-20: PLAY refused, and it says so
      render();
      return;
    }
    /* W3 P0-20: THE START PRESS. Trap 69's chrome vocabulary, the same `lift`
     * at the same level the other nine classes open on - this door opens a
     * class, so it opens like one. */
    cue('lift', 0.5);
    /* Persist BEFORE handing off: G1's claim may take seconds and a player who
     * kills the app mid-deal should still come back to a lit "same sort". */
    try {
      if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
        ctx.store.mergeGameMeta('sort', { setup, doorSeen: true });
      }
    } catch (e) { say('meta write failed'); }
    played = true;
    errKey = '';
    try {
      onPlay(setup, {
        sources: resolved.sources,
        hot: resolved.hot,
        quick: resolved.quick,
      });
    } catch (e) { say('onPlay threw: ' + ((e && e.message) || e)); }
  }

  /* --------------------------------------------------------------- render -- */

  /* The search box is re-minted on every render, so a probe verdict would
   * otherwise steal the caret. `pendingFocus` puts it back where the player
   * left it; nothing else in the door ever claims focus off a render. */
  let pendingFocus = '';
  let lastInput = null;

  function render() {
    if (destroyed) return;
    unbind(trans);
    lastInput = null;
    clear(body);
    attr(root, 'data-step', step);
    tutLine.hidden = !teach;
    /* THE RAIL DESCRIBES THE THREE-STEP WALK, so it only shows on that walk.
     * On the night-two "same sort" step none of the three is current and none is
     * done, which drew three dead lamps under the title: a progress rail
     * reporting no progress through a journey the player is not taking. */
    rail.hidden = step === 'same';
    for (const key of ['source', 'target', 'noise']) {
      const order = ['source', 'target', 'noise'];
      const here = order.indexOf(step);
      const mine = order.indexOf(key);
      attr(lamps[key], 'data-on', step === key ? '1' : '0');
      attr(lamps[key], 'data-done', (here > mine && here >= 0) ? '1' : '0');
    }

    if (thinTags) {
      const which = thinTags.indexOf('target') >= 0 ? 'target' : 'noise';
      body.appendChild(strip('warn', t('sort_thin'), {
        label: t('sort_thin_add'),
        run: () => { thinTags = null; setBusy(false); step = which; render(); },
      }));
    }
    if (errKey) body.appendChild(strip('err', t(errKey)));

    if (step === 'same') stepSame(body);
    else if (step === 'source') stepSource(body);
    else if (step === 'target' || step === 'noise') stepPile(body, step);
    else if (step === 'ghost' && ghostPaint) ghostPaint(body);

    const primary = buildFoot();
    let want = null;
    if (pendingFocus === 'search' && lastInput) want = lastInput;
    else if (primary && !primary.disabled) want = primary;
    else if (step === 'source') want = firstEnabled(body);
    pendingFocus = '';
    focusSoon(want);
  }

  /** The first thing on the step a keyboard player can actually press. */
  function firstEnabled(host) {
    const walk = (n) => {
      for (let i = 0; i < (n.children ? n.children.length : 0); i++) {
        const c = n.children[i];
        if (c && c.tagName === 'BUTTON' && !c.disabled) return c;
        const deep = walk(c);
        if (deep) return deep;
      }
      return null;
    };
    return walk(host);
  }

  let ghostPaint = null;

  /* ----------------------------------------------------------- the handle -- */

  /**
   * setBusy(on, msgKey[, detail]). `detail` (0827) is the vet's live count
   * ("14/48") - a number, never a sentence, so the lexicon stays the only
   * source of words: the row is `t(msgKey)` and the detail rides after it.
   */
  function setBusy(onFlag, msgKey, detail) {
    if (destroyed) return;
    busyKey = onFlag ? String(msgKey || 'sort_dealing') : '';
    const line = () => t(busyKey) + (detail == null || detail === '' ? '' : ' ' + String(detail));
    if (!onFlag) {
      if (busyEl && busyEl.parentNode) busyEl.parentNode.removeChild(busyEl);
      busyEl = null;
      return;
    }
    if (!busyEl) {
      busyEl = el('div', 'g-sort-busy');
      attr(busyEl, 'role', 'status');
      busyEl.appendChild(el('i', 'g-sort-spin'));
      busyEl._sdText = el('span', 'g-sort-busy-t', line());
      busyEl.appendChild(busyEl._sdText);
      root.appendChild(busyEl);
    } else {
      busyEl._sdText.textContent = line();
    }
  }

  /**
   * THE GHOST ROUND. G1 hands back two rows from the pool it just claimed and
   * the door shows the rule with the player's OWN media: the target swipes
   * itself right under a YES, the noise left under a NO. Resolves when the
   * script is done, which is what G1 waits on before resolving setup().
   * A missing row is not a failure: the card runs empty (drawn frame + stamp).
   */
  function ghost(rows) {
    if (destroyed) return Promise.resolve();
    setBusy(false);
    return new Promise((resolve) => {
      let settled = false;
      const done = () => { if (settled) return; settled = true; resolve(); };
      ghostPaint = (host) => stepGhost(host, rows || {}, done);
      step = 'ghost';
      render();
      /* The door never gets to hang the class: whatever the transitions do, the
       * promise resolves on the script's own deadline plus slop. */
      later((reduced ? GHOST.REDUCED_DONE_MS : GHOST.DONE_MS) + 600, done);
    });
  }

  /**
   * THIN WARNING (an ADDITION to contract section 4, not a change).
   * `pool.thin(tag)` is only knowable AFTER the claim, so it cannot be a step:
   * G1 calls this once the pool resolves. The door raises a strip over
   * whatever it is showing (busy, ghost) and offers ONE affordance, "add
   * another pick", which drops the busy state and walks back to that side's
   * picker. Pressing PLAY there calls onPlay a SECOND time, so G1 must dispose
   * the thin pool and re-claim rather than assume onPlay fires once.
   */
  function warnThin(tags) {
    if (destroyed) return;
    const list = (Array.isArray(tags) ? tags : [tags]).filter((x) => x === 'target' || x === 'noise');
    thinTags = list.length ? list : ['target'];
    render();
  }

  function destroy() {
    if (destroyed) return;
    destroyed = true;
    unlater(debounceId);
    debounceId = null;
    killTimers();
    unbind(trans);
    unbind(perm);
    try { if (typeof offLibrary === 'function') offLibrary(); } catch (e) { /* noop */ }
    offLibrary = null;
    ghostPaint = null;
    if (busyEl && busyEl.parentNode) busyEl.parentNode.removeChild(busyEl);
    busyEl = null;
    if (root.parentNode) root.parentNode.removeChild(root);
  }

  render();

  return {
    el: root,
    destroy,
    setBusy,
    ghost,
    warnThin,
    step: () => step,
    diagnostics: () => ({
      step,
      source: sel.source,
      teach,
      busy: busyKey,
      thin: thinTags ? thinTags.slice() : null,
      played,
      listeners: perm.length + trans.length,
      timers: timers.size,
      setup: sel.source ? buildSetup() : null,
      options: { online: opt.online, local: opt.local, quickOnly: opt.quickOnly },
    }),
  };
}

export default createSetupDoor;
