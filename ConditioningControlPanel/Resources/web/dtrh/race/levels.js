/* ============================================================================
 * race/levels.js - the LEVELS panel: the first thing a player sees.
 *
 *   levelsEnabled(settings) -> boolean       the verb's whole visibility rule
 *   mmss(seconds) -> '2:42'                  pure
 *   parseSets(json) -> set[]                 pure, no network, no DOM
 *   loadLevels(url, { fetch }) -> Promise<set[]>
 *   createLevels({ ... }) -> panel
 *
 * WHAT IT IS. race/levels.json is a static list of the tracks in a public set,
 * shipped with the game. The panel paints one big tappable row per track, and a
 * tap plays it. No pasting, no login, no search box: open the race, see the
 * levels, tap one, drive.
 *
 * THE FILE IS STATIC AND IT IS OURS. levels.json holds a title, a file id, a
 * length and a byte count per track and NOTHING ELSE. It is read from our own
 * origin, it is never refreshed off the site, and a set that is not in it simply
 * is not on the list. `bytes` is the whole point of carrying it: the CHART.md
 * hash is a length plus the first megabyte, so a length written down here is a
 * length the page does not have to ask a cdn for (race/CLOUD.md, "levels").
 *
 * TWO HOSTS, ONE PANEL.
 *   web      the row hands the track to race/cloud.js, which owns the <audio>
 *            element, and the run follows that element the way lane W1 built it.
 *   desktop  (`settings.trackPick`) the desktop owns playback, so the row posts
 *            `cloud-open { url }` and says to press play over there. This page
 *            never loads a byte of audio in that mode.
 *
 * THE MARK ON A ROW. `hand-tuned` when race/charts/index.json has a row for that
 * track, `road` when it does not, `again` on the last one played. Deciding it
 * costs NO NETWORK: the index is same-origin and already fetched, and the key is
 * the cloudId off the url.
 *
 * THE PICKED ROW IS THE STATUS. Tapping a level opens no second panel anywhere:
 * THAT ROW lights (`is-picked`, a pink plate a thumb cannot miss) and grows a
 * progress bar under its title, and the bar carries the same number the menu's
 * track plate used to - the web lane's chart steps (reading, decoding, charting,
 * naming) through `setStage`, a host's `track-progress` pct through `setTrack`.
 * `setTrack(state)` answers TRUE when it painted that state on a row, and
 * raceBoot hides the plate for exactly that answer: a pasted link and a picked
 * file still get the plate, a listed level never does. WHICH row is picked is
 * read live off the player on the web (`play the set` rolling on to level 4
 * lights level 4; a pasted link playing lights nothing) and off the last tap on
 * a desktop host, which owns playback and reports back through track-progress.
 *
 * `back` IS ALWAYS ON SCREEN. Eleven rows are taller than a phone, so the foot
 * is `position: sticky` against the bottom of the scrolling column (menu.css).
 * It rides the bottom edge at every scroll position and is still the LAST row
 * the keyboard and the pad walk to, so no index math moved.
 *
 * THE PASTE BOX IS STILL THERE. `or paste a link` opens lane W1's panel under
 * the list, unchanged, so a track that is not one of the levels is still one
 * paste away.
 * ==========================================================================*/

import { cloudIdFrom, findAuthored } from './chartSource.js';

/** The panel key. Kept as `cloud` so the menu, `?panel=cloud` and the smokes all still name it. */
export const VERB_ID = 'cloud';
/** The verb. Lower case like every other one, and it sits right under `race`. */
export const VERB_LABEL = 'levels';
/** Where the list lives, relative to race/. `?levels=` overrides it, same origin only. */
export const LEVELS_FILE = 'levels.json';
/** The last level played, so the panel can say `again` on it. One id, nothing else. */
export const LAST_KEY = 'race.level';
/** The page a desktop host is asked to open for a track. A PAGE, never a file. */
export const FILE_PAGE = 'https://bambicloud.com/file/';

export const SET_LABEL = 'play the set';
export const PASTE_LABEL = 'or paste a link';
export const PASTE_OPEN_LABEL = 'hide the link box';
export const BACK_LABEL = 'back';
export const HAND_MARK = 'hand-tuned';
export const ROAD_MARK = 'road';
export const AGAIN_MARK = 'again';
export const OVER_THERE_LINE = 'press play over there';
export const EMPTY_LINE = 'no levels on this build, paste a link instead';

/**
 * How full the picked row's bar is on each step, so one number can come from two
 * very different places. The four on the left are race/cloudChart.js's own words
 * (the web lane, through setStage); the rest are the host's `track-progress`
 * stages (CHART.md), which also carry a real pct and use it when they do.
 */
export const STAGE_PCT = {
  reading: 0.3, decoding: 0.55, charting: 0.75, naming: 0.9,
  opening: 0.08, fetching: 0.35, decode: 0.55, energy: 0.75, words: 0.9, loading: 0.15,
};
/** The mark a host's stage puts on the picked row. The web lane's own words are already marks. */
export const STAGE_MARK = {
  opening: 'over there', fetching: 'loading', decode: 'reading', energy: 'charting', words: 'naming',
  ready: 'loaded', error: 'would not load',
};

/** The verb's whole visibility rule: a build that carries the levels, host or no host. */
export function levelsEnabled(settings) {
  return !!settings && settings.cloud === true;
}

/** 162 -> '2:42'. A length nobody can read is an empty string, not a lie. */
export function mmss(seconds) {
  const s = Math.round(Number(seconds));
  if (!(s >= 0) || !isFinite(s)) return '';
  const m = Math.floor(s / 60);
  return m + ':' + String(s % 60).padStart(2, '0');
}

/**
 * levels.json -> sets. PURE. Anything malformed is dropped rather than repaired:
 * a level with no url cannot be played and a set with no levels is not a set.
 */
export function parseSets(json) {
  const sets = Array.isArray(json && json.sets) ? json.sets : [];
  const out = [];
  for (const s of sets) {
    if (!s || typeof s !== 'object') continue;
    const levels = [];
    for (const l of Array.isArray(s.levels) ? s.levels : []) {
      if (!l || !l.id || !l.url) continue;
      levels.push({
        n: Number(l.n) || levels.length + 1,
        id: String(l.id),
        title: String(l.title || 'track').slice(0, 60),
        url: String(l.url),
        durationSec: Number(l.durationSec) || 0,
        bytes: Number(l.bytes) || 0,
        hash: String(l.hash || ''),
      });
    }
    if (!levels.length) continue;
    out.push({ id: String(s.id || 'set'), title: String(s.title || 'a set').slice(0, 60), playlistUrl: String(s.playlistUrl || ''), levels });
  }
  return out;
}

/**
 * race/levels.json, off our own origin. A list that will not load is an EMPTY
 * list and one log line: the paste box is still there and the race still runs.
 */
export async function loadLevels(url, { fetch: f = null, log = null } = {}) {
  const get = f || (typeof fetch !== 'undefined' ? fetch : null);
  const say = (m) => { try { if (log) log('levels: ' + m); } catch (e) { /* no log */ } };
  if (!get || !url) return [];
  try {
    const res = await get(String(url), { credentials: 'omit' });
    if (!res || !res.ok) { say('none (' + (res ? res.status : 'no answer') + ')'); return []; }
    const sets = parseSets(await res.json());
    say(sets.length ? sets.map((s) => `${s.title} (${s.levels.length})`).join(', ') : 'the file is empty');
    return sets;
  } catch (e) { say('unreadable: ' + ((e && e.message) || e)); return []; }
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
 * @param {object[]} o.sets       parseSets output. An empty list is a panel that says so.
 * @param {object}   [o.cloud]    race/cloud.js, or null on a desktop host
 * @param {object}   [o.index]    race/charts/index.json, for the `hand-tuned` mark
 * @param {object}   o.hooks      play(entries), open(url), toast(line)
 * @param {function} [o.log]
 * @param {object}   [o.store]    localStorage, or null. The smoke's seam.
 */
export function createLevels({ settings = {}, sets = [], cloud = null, index = null, hooks = {}, log = null, store = undefined }) {
  const say = (m) => { try { if (log) log('levels: ' + m); } catch (e) { /* no log */ } };
  const call = (n, ...a) => { try { return typeof hooks[n] === 'function' ? hooks[n](...a) : undefined; } catch (e) { say(n + ': ' + e); return undefined; } };
  // A desktop host owns playback outright: this panel only ever points it at a page.
  const desktop = settings.trackPick === true || !cloud;
  const set = sets[0] || null;
  const levels = set ? set.levels : [];
  const mem = store !== undefined ? store : (typeof localStorage !== 'undefined' ? localStorage : null);

  let slotEl = null, listEl = null, pasteEl = null, cloudUi = null;
  let levelEls = [], tailEls = [], backEl = null;
  let levelRows = [], tailRows = [], backRow = null;
  let pasteOpen = false, disposed = false;
  let stageId = '', stageWord = '';
  let pickedId = '';                 // the last level TAPPED here; the web player below can outvote it
  let prog = null;                   // { pct, mark } from a host's track-progress, or null
  let last = '';
  let onPick = null, onClose = null, onRefresh = null;

  try { last = String((mem && mem.getItem(LAST_KEY)) || ''); } catch (e) { last = ''; }
  const remember = (id) => {
    last = String(id || '');
    try { if (mem) mem.setItem(LAST_KEY, last); } catch (e) { /* a private window, and nothing is lost */ }
  };

  /** The `hand-tuned` mark, decided with no network at all: the index is same origin. */
  const authored = (lv) => !!findAuthored(index, { cloudId: cloudIdFrom(lv.url), hash: lv.hash });
  /** What race/cloud.js is handed for a level. `bytes` saves it a HEAD the cdn will not answer. */
  const entry = (lv) => ({ id: lv.id, url: lv.url, title: lv.title, bytes: lv.bytes, locked: false });

  /** Where a level sits in the player right now, or ''. Read at paint time: the list is live. */
  function playerState(lv) {
    if (desktop || !cloud) return '';
    let st = null;
    try { st = cloud.state; } catch (e) { return ''; }
    if (!st || st.at < 0) return '';
    const cur = st.list[st.at];
    if (!cur || cur.id !== lv.id) return '';
    if (st.busy) return stageId === lv.id && stageWord ? stageWord : 'loading';
    if (!st.played) return 'loaded';
    return st.playing ? 'playing' : 'paused';
  }

  /**
   * WHICH ROW IS THE PICKED ONE. On the web the player is the truth: whatever race/cloud.js
   * holds right now is what the panel lights, so `play the set` rolling on lights the next
   * row by itself and a pasted link playing lights NOTHING (it is not on this list, and its
   * status belongs to the menu's plate). With no player here - a desktop host, which owns
   * playback outright - the last tap is all there is, and the host's progress lands on it.
   */
  function pickedLevel() {
    if (!desktop && cloud) {
      let st = null;
      try { st = cloud.state; } catch (e) { st = null; }
      if (st && st.at >= 0) {
        const cur = st.list[st.at];
        return (cur && levels.find((l) => l.id === cur.id)) || null;
      }
    }
    return (pickedId && levels.find((l) => l.id === pickedId)) || null;
  }

  /** How full the picked row's bar is, 0..1. -1 means this row carries no bar at all. */
  function pctOf(lv) {
    if (lv !== pickedLevel()) return -1;
    const live = playerState(lv);
    if (live === 'loaded' || live === 'playing' || live === 'paused') return 1;
    if (live && STAGE_PCT[live] != null) return STAGE_PCT[live];
    if (prog) return prog.pct;
    return live ? STAGE_PCT.loading : 0;
  }

  /** The small mark on the right of a row. State first, then the host's, then memory, then the road it will get. */
  function markOf(lv) {
    const live = playerState(lv);
    if (live) return live;
    if (prog && prog.mark && lv === pickedLevel()) return prog.mark;
    if (lv.id === last) return AGAIN_MARK;
    return authored(lv) ? HAND_MARK : ROAD_MARK;
  }

  /* ---- the presses ------------------------------------------------------- */
  /** A tap on a level. One track, one run. On a desktop it is a door, not a download.
   *  The tap is the whole visit: the panel closes behind it and the main list takes over,
   *  where the menu's plate shows the load and the first verb becomes `start · <name>`. */
  function tap(lv) {
    remember(lv.id);
    pickedId = lv.id; prog = null;   // the row itself is the answer from here on
    if (desktop) {
      call('open', FILE_PAGE + lv.id, lv.title);
      call('toast', OVER_THERE_LINE);
      say(lv.title + ': handed to the desktop');
      paint();
      if (onClose) onClose();
      return;
    }
    say('level ' + lv.n + ': ' + lv.title);
    call('play', [entry(lv)]);
    paint();
    if (onClose) onClose();
  }

  /** The whole set, in order. The rollover between them is lane W1's, unchanged. */
  function playSet() {
    if (!levels.length) return;
    remember(levels[0].id);
    pickedId = levels[0].id; prog = null;
    say(set.title + ': all ' + levels.length);
    call('play', levels.map(entry));
    paint();
    if (onClose) onClose();
  }

  function togglePaste() {
    pasteOpen = !pasteOpen;
    if (pasteEl) pasteEl.hidden = !pasteOpen;
    paint();
  }

  /* ---- the rows the menu walks ------------------------------------------- */
  /** How many rows sit above the nested paste panel, so a click in it maps to the right index. */
  const head = () => levelRows.length + tailRows.length;
  function rows() {
    const out = levelRows.concat(tailRows);
    if (pasteOpen && cloudUi) out.push(...cloud.rows());
    if (backRow) out.push(backRow);
    return out;
  }
  function els() {
    const out = levelEls.concat(tailEls);
    if (pasteOpen && cloudUi) out.push(...cloud.els());
    if (backEl) out.push(backEl);
    return out;
  }

  function paint() {
    if (!slotEl || disposed) return;
    const picked = pickedLevel();
    for (let i = 0; i < levels.length; i++) {
      const b = levelEls[i]; if (!b) continue;
      const lv = levels[i], mark = markOf(lv), m = b.querySelector('.rm-level-mark');
      if (m && m.textContent !== mark) m.textContent = mark;
      b.classList.toggle('is-on', !!playerState(lv));
      b.classList.toggle('is-hand', authored(lv));
      // the picked row IS the status: it lights, and the bar under its title is the load
      const pct = pctOf(lv), on = pct >= 0;
      b.classList.toggle('is-picked', on);
      b.classList.toggle('is-loaded', pct >= 1);
      if (on) b.setAttribute('aria-current', 'true'); else b.removeAttribute('aria-current');
      const fill = b.querySelector('.rm-level-bar > i');
      if (fill) fill.style.width = `${Math.round(Math.max(0, Math.min(1, pct)) * 100)}%`;
    }
    const pasteBtn = tailEls[tailEls.length - 1];
    if (pasteBtn && cloudUi) {
      const want = pasteOpen ? PASTE_OPEN_LABEL : PASTE_LABEL;
      if (pasteBtn.textContent !== want) pasteBtn.textContent = want;
    }
    if (onRefresh) onRefresh();
  }

  /**
   * Build the panel into the menu's slot. `pick(i)` is the menu's own press for row i,
   * `close` is the way back to the verbs, `refresh` repaints the menu's focus after the
   * rows changed shape (the paste drawer opening is exactly that).
   */
  function buildPanel({ slot, pick, close, refresh }) {
    slotEl = slot;
    onPick = typeof pick === 'function' ? pick : null;
    onClose = typeof close === 'function' ? close : null;
    onRefresh = typeof refresh === 'function' ? refresh : null;
    elm('h3', 'rm-h', slot, VERB_LABEL);
    elm('div', 'rm-hint rm-levels-set', slot, set ? set.title : EMPTY_LINE);
    listEl = elm('div', 'rm-levels', slot);

    levelRows = []; levelEls = [];
    levels.forEach((lv, i) => {
      const b = elm('button', 'rm-btn rm-level-btn', listEl);
      b.type = 'button'; b.dataset.id = 'lv-' + lv.n; b.setAttribute('role', 'menuitem');
      elm('span', 'rm-level-n', b, String(lv.n).padStart(2, '0'));
      elm('span', 'rm-level-title', b, lv.title);
      elm('span', 'rm-level-len', b, mmss(lv.durationSec));
      elm('span', 'rm-level-mark', b, '');
      // the bar lives IN the row, under the title, and only the picked row ever shows it
      const bar = elm('i', 'rm-level-bar', b); elm('i', '', bar);
      b.addEventListener('click', (ev) => { ev.stopPropagation(); if (onPick) onPick(i); });
      levelEls.push(b);
      levelRows.push({ id: 'lv-' + lv.n, label: lv.title, press: () => tap(lv) });
    });

    const tail = elm('div', 'rm-levels-tail', slot);
    tailRows = []; tailEls = [];
    const tailBtn = (id, label, press) => {
      const b = elm('button', 'rm-btn rm-media-btn rm-levels-btn', tail, label);
      b.type = 'button'; b.dataset.id = id; b.setAttribute('role', 'menuitem');
      const at = levelRows.length + tailRows.length;
      b.addEventListener('click', (ev) => { ev.stopPropagation(); if (onPick) onPick(at); });
      tailEls.push(b); tailRows.push({ id, label, press });
    };
    if (levels.length && !desktop) tailBtn('set', SET_LABEL, playSet);
    if (cloud) tailBtn('paste', PASTE_LABEL, togglePaste);

    // The paste drawer: lane W1's panel, whole and unchanged, nested under the list.
    pasteEl = elm('div', 'rm-cloud-paste', slot); pasteEl.hidden = true;
    if (cloud) {
      cloudUi = cloud.buildPanel({
        slot: pasteEl, nested: true,
        pick: (i) => { if (onPick) onPick(head() + i); },
        close: () => { if (onClose) onClose(); },
        refresh: () => paint(),
      });
    }
    const foot = elm('div', 'rm-levels-foot', slot);
    backEl = elm('button', 'rm-btn rm-media-btn rm-levels-btn', foot, BACK_LABEL);
    backEl.type = 'button'; backEl.dataset.id = 'back'; backEl.setAttribute('role', 'menuitem');
    backEl.addEventListener('click', (ev) => { ev.stopPropagation(); if (onPick) onPick(rows().length - 1); });
    backRow = { id: 'back', label: BACK_LABEL, press: () => { if (onClose) onClose(); } };

    paint();
    return { rows, els };
  }

  return {
    buildPanel,
    rows,
    els,
    paint,
    /** Lane W2's progress, landing on the row it belongs to. '' takes the stage word off again. */
    setStage(id, word) { stageId = String(id || ''); stageWord = String(word || ''); paint(); },
    /**
     * The state the menu's track plate would have shown. TRUE when this panel painted it on a
     * picked row too, which is the menu's rule for keeping the plate down WHILE THIS PANEL IS OPEN
     * (on the main list the plate shows regardless: a tap lands there).
     *   picking  a file dialog: not a level at all, so the pick is dropped and the plate takes it
     *   null     nothing loaded any more: the row goes back to being a row
     * A state with no picked row under it (a pasted link, a file) is never claimed.
     */
    setTrack(state) {
      const st = state && state.stage ? state : null;
      if (!st || st.stage === 'picking' || st.stage === 'cancelled') {
        if (!st || st.stage === 'picking') { pickedId = ''; prog = null; }
        paint();
        return false;
      }
      if (!pickedLevel()) { prog = null; paint(); return false; }
      const pct = Number(st.pct);
      prog = {
        pct: st.stage === 'ready' ? 1 : (isFinite(pct) && pct > 0 ? Math.max(0, Math.min(1, pct)) : (STAGE_PCT[st.stage] || 0)),
        mark: STAGE_MARK[st.stage] || '',
      };
      paint();
      return true;
    },
    /** Which level the panel is lit on right now, or ''. The smoke's window on the pick. */
    get picked() { const lv = pickedLevel(); return lv ? lv.id : ''; },
    get state() { return { desktop, sets: sets.length, levels: levels.length, pasteOpen, last, stage: stageWord, picked: (pickedLevel() || {}).id || '' }; },
    dispose() { disposed = true; try { if (cloud) cloud.dispose(); } catch (e) { /* already gone */ } },
  };
}

export default createLevels;
