/* ============================================================================
 * ui/screens/assets.js — YOUR MEDIA, AND WHAT IT COSTS TO SEND IT.
 *
 * The ninth screen. It shows the host's transfer cache (ui/assetsStore.js) and
 * gives the player the four verbs that matter: compress everything, pause,
 * delete the copies, change the ceiling. Everything else on it is truth-telling.
 *
 * THINGS THAT ARE NOT DECORATION
 *   1. Every tile carries a WORD for its state. The grey/pink/gold difference is
 *      a second channel, never the only one.
 *   2. The progress ring is SVG stroke-dashoffset written from JS, not a CSS
 *      animation — so the effect armor (--gg-deco-play: paused at data-gg-fx
 *      "hot") cannot freeze the one surface that says work is still happening.
 *      The ring's rule re-declares `running` anyway, like .gg-mon-proj.
 *   3. The grid is VIRTUALIZED in two directions. Tiles are appended a chunk at
 *      a time as you reach the bottom, and an IntersectionObserver attaches the
 *      media only for what is on screen (plus a row of margin) and DETACHES it
 *      again on the way out. At most MAX_LIVE_PREVIEWS micro-previews play at
 *      once. A five-thousand-file library must not be a five-thousand-decoder
 *      page.
 *   4. Filenames are user data and are written with textContent, only ever.
 *   5. Standalone renders "compression lives in the app" on the FIRST frame.
 *      There is no host, so there is nothing to wait for, so there is no spinner.
 *
 * Every listener, timer and observer goes through the screen's ledger.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S } from '../strings.js';
import {
  CONFIRM_ETA_MS, CONFIRM_INPUT_BYTES, FILTERS, GB, LOCAL_ARTIFACT_MAX_BYTES, LOCAL_MAX_BYTES,
  LOCAL_ZIP_MAX_ENTRIES, NEEDS_STATES,
  capGb, etaMinutes, formatBytes, formatUsage, matchesFilter, normalizeState, pendingInputBytes,
} from '../assetsStore.js';

/** What the picker offers the OS file sheet — mirror of the wire's allowlist,
 * plus .zip: a phone cannot hand over a folder, so an archive is the only way
 * a player on a phone gives us a whole library (assetsStore expands it). */
const LOCAL_ACCEPT = '.png,.jpg,.jpeg,.gif,.webp,.mp4,.m4v,.webm,.zip,'
  + 'image/png,image/jpeg,image/gif,image/webp,video/mp4,video/webm,'
  + 'application/zip,application/x-zip-compressed';

/** Hard ceiling on simultaneously playing micro-previews. */
const MAX_LIVE_PREVIEWS = 24;
/** Tiles appended per growth step (and the initial fill). */
const CHUNK = 120;
/** Ring geometry — r and the circumference the dash maths needs. */
const RING_R = 15;
const RING_C = 2 * Math.PI * RING_R;
/** The cap slider writes to the host at most this often. */
const CAP_DEBOUNCE_MS = 400;
/** The search box repaints at most this often. */
const SEARCH_DEBOUNCE_MS = 120;

const FILTER_LABELS = {
  all: () => S.assets.filterAll,
  needs: () => S.assets.filterNeeds,
  ready: () => S.assets.filterReady,
  failed: () => S.assets.filterFailed,
  exempt: () => S.assets.filterExempt,
};

/** The word on the tile. Never a colour on its own. */
function badgeFor(item) {
  const state = normalizeState(item.state);
  if (state === 'ready') return S.assets.badgeReady;
  if (state === 'exempt') return S.assets.badgeExempt;
  if (state === 'failed') return S.assets.badgeFailed(item.fail || '');
  if (state === 'working') return S.assets.badgeWorking(Math.round(item.pct || 0));
  if (state === 'queued') return S.assets.badgeQueued;
  return S.assets.badgeNotReady;
}

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { actions, audio, sheets, prefs, logger } = ctx || {};
  const store = ctx?.assets || null;

  /* ------------------------------------------------------------ no store */
  if (!store) {
    container.appendChild(unavailableCard());
    return { unmount() { ledger.dispose(); } };
  }

  const reduceMotion = !!(prefs && prefs.get && prefs.get('reduceMotion'));

  let state = store.state;
  let items = store.items;
  let filter = FILTERS.indexOf(String(ctx?.filter || '')) >= 0 ? String(ctx.filter) : 'all';
  let query = '';

  /* ---------------------------------------------------------------- head */
  const chipReady = el('span', { class: 'gg-chip gg-assets-chip', text: '' });
  const chipNeeds = el('span', { class: 'gg-chip gg-assets-chip', text: '' });
  const chipFailed = el('span', { class: 'gg-chip gg-assets-chip is-bad', text: '', hidden: true });
  const chipUsage = el('span', { class: 'gg-chip gg-assets-chip is-usage', text: '' });
  const stats = el('div', { class: 'gg-assets-stats' }, [chipReady, chipNeeds, chipFailed, chipUsage]);

  const head = el('div', { class: 'gg-assets-head' }, [
    el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: S.assets.eyebrow })]),
    el('p', { class: 'gg-lead', text: S.assets.lead }),
    stats,
  ]);

  /* ------------------------------------------------------------- toolbar */
  const compressBtn = button(ledger, S.assets.compressAllIdle, () => onCompressAll(), { variant: 'primary', audio });
  const pauseBtn = button(ledger, S.assets.pause, () => onPauseToggle(), { audio });
  const deleteBtn = button(ledger, S.assets.deleteCompressed, () => onDeleteAll(), { variant: 'ghost', audio });

  const seg = el('div', { class: 'gg-assets-seg', role: 'group', 'aria-label': S.assets.filterAll });
  const segBtns = new Map();
  for (const f of FILTERS) {
    const b = el('button', {
      type: 'button',
      class: 'gg-assets-segbtn',
      'aria-pressed': String(f === filter),
      dataset: { filter: f },
      text: FILTER_LABELS[f](),
    });
    ledger.listen(b, 'click', () => {
      if (filter === f) return;
      filter = f;
      try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
      paintSeg();
      rebuildGrid();
    });
    segBtns.set(f, b);
    seg.appendChild(b);
  }

  const search = el('input', {
    type: 'search',
    class: 'gg-assets-search',
    'aria-label': S.assets.searchLabel,
    placeholder: S.assets.searchPlaceholder,
  });
  let searchTimer = 0;
  ledger.listen(search, 'input', () => {
    try { clearTimeout(searchTimer); } catch (_e) { /* ignore */ }
    searchTimer = ledger.timer(() => {
      const next = String(search.value || '');
      if (next === query) return;
      query = next;
      rebuildGrid();
    }, SEARCH_DEBOUNCE_MS);
  });

  const tools = el('div', { class: 'gg-assets-tools' }, [
    el('div', { class: 'gg-assets-tools-row' }, [compressBtn, pauseBtn, deleteBtn]),
    el('div', { class: 'gg-assets-tools-row' }, [seg, search]),
  ]);

  const etaLine = el('p', { class: 'gg-assets-eta', text: '', role: 'status' });
  const noticeLine = el('p', { class: 'gg-assets-notice', text: '', hidden: true, role: 'status' });

  /* ---------------------------------------------------------------- grid */
  const grid = el('div', { class: 'gg-assets-grid', role: 'list' });
  const emptyLine = el('p', { class: 'gg-assets-empty', text: S.assets.loading });
  const sentinel = el('div', { class: 'gg-assets-sentinel', 'aria-hidden': 'true' });
  const moreBtn = button(ledger, S.assets.more(0), () => grow(), { variant: 'ghost', audio });
  moreBtn.classList.add('gg-assets-more');
  moreBtn.hidden = true;

  /* -------------------------------------------------------------- footer */
  const capValue = el('span', { class: 'gg-row-value', text: '' });
  const capInput = el('input', {
    type: 'range', min: '1', max: '64', step: '1',
    value: String(capGb(state.capBytes)),
    'aria-label': S.assets.capLabel,
  });
  let capTimer = 0;
  let capDirty = false;      // the player is dragging: do not stomp their value
  ledger.listen(capInput, 'input', () => {
    const gb = Math.round(Number(capInput.value) || 1);
    capValue.textContent = S.assets.capValue(gb);
    capDirty = true;
    try { clearTimeout(capTimer); } catch (_e) { /* ignore */ }
    capTimer = ledger.timer(() => { capDirty = false; store.setCap(gb * GB); }, CAP_DEBOUNCE_MS);
  });

  const backBtn = button(ledger, S.assets.back, () => { actions?.goTitle?.(); }, { variant: 'ghost', audio, sfx: 'ui-back' });

  const footer = el('div', { class: 'gg-assets-foot' }, [
    el('div', { class: 'gg-row gg-assets-cap' }, [
      el('span', { class: 'gg-row-label', text: S.assets.capLabel }),
      capInput,
      capValue,
    ]),
    el('p', { class: 'gg-assets-note', text: S.assets.protectedNote }),
    el('div', { class: 'gg-assets-foot-actions' }, [backBtn]),
  ]);

  const card = el('div', { class: 'gg-card gg-assets' }, [
    head, tools, etaLine, noticeLine, emptyLine, grid, moreBtn, sentinel, footer,
  ]);
  container.appendChild(card);

  /* ------------------------------------------------------ virtualization */

  /** id -> {node, media, mediaHost, ring, badge, acts, item, live} */
  const tiles = new Map();
  let view = [];            // the filtered item list
  let viewKey = '';         // identity of the rendered id sequence
  let rendered = 0;         // how many of `view` are in the DOM
  let livePreviews = 0;

  const IO = (typeof globalThis !== 'undefined') ? globalThis.IntersectionObserver : null;

  const io = IO ? new IO((entries) => {
    for (const entry of entries) {
      const id = entry && entry.target && entry.target.dataset ? entry.target.dataset.id : null;
      if (!id) continue;
      const rec = tiles.get(id);
      if (!rec) continue;
      if (entry.isIntersecting) attachMedia(rec);
      else detachMedia(rec);
    }
  }, { root: container, rootMargin: '400px 0px' }) : null;
  if (io) ledger.add(() => { try { io.disconnect(); } catch (_e) { /* ignore */ } });

  const growIo = IO ? new IO((entries) => {
    if (entries.some((e) => e && e.isIntersecting)) grow();
  }, { root: container, rootMargin: '600px 0px' }) : null;
  if (growIo) {
    try { growIo.observe(sentinel); } catch (_e) { /* ignore */ }
    ledger.add(() => { try { growIo.disconnect(); } catch (_e) { /* ignore */ } });
  }
  // No IntersectionObserver (an old runtime, or the node DOM stub): the button
  // is the whole virtualization story and the media attaches eagerly.
  if (!growIo) moreBtn.hidden = false;

  /* ------------------------------------------------------------ the tile */

  function makeRing() {
    const doc = (typeof document !== 'undefined') ? document : null;
    if (!doc || typeof doc.createElementNS !== 'function') return null;
    const NS = 'http://www.w3.org/2000/svg';
    let svg = null;
    try {
      svg = doc.createElementNS(NS, 'svg');
      svg.setAttribute('class', 'gg-asset-ring');
      svg.setAttribute('viewBox', '0 0 36 36');
      svg.setAttribute('aria-hidden', 'true');
      const track = doc.createElementNS(NS, 'circle');
      track.setAttribute('class', 'gg-asset-ring-track');
      track.setAttribute('cx', '18'); track.setAttribute('cy', '18'); track.setAttribute('r', String(RING_R));
      const value = doc.createElementNS(NS, 'circle');
      value.setAttribute('class', 'gg-asset-ring-value');
      value.setAttribute('cx', '18'); value.setAttribute('cy', '18'); value.setAttribute('r', String(RING_R));
      value.setAttribute('stroke-dasharray', RING_C.toFixed(2));
      value.setAttribute('stroke-dashoffset', RING_C.toFixed(2));
      svg.appendChild(track);
      svg.appendChild(value);
      svg._value = value;
    } catch (_e) { return null; }
    return svg;
  }

  function setRing(ring, pct) {
    if (!ring || !ring._value) return;
    const p = Math.min(100, Math.max(0, Number(pct) || 0));
    try { ring._value.setAttribute('stroke-dashoffset', (RING_C * (1 - p / 100)).toFixed(2)); } catch (_e) { /* ignore */ }
  }

  function makeTile(item) {
    const mediaHost = el('div', { class: 'gg-asset-media' });
    const ring = makeRing();
    if (ring) mediaHost.appendChild(ring);

    const badge = el('span', { class: 'gg-asset-badge', text: badgeFor(item) });
    const name = el('span', { class: 'gg-asset-name' });
    name.textContent = String(item.name || '');       // user data — text, always
    name.title = String(item.name || '');

    const acts = el('div', { class: 'gg-asset-acts' });

    const node = el('article', {
      class: 'gg-asset',
      role: 'listitem',
      dataset: { id: item.id, state: normalizeState(item.state) },
    }, [mediaHost, badge, name, acts]);

    const rec = { id: item.id, node, mediaHost, media: null, ring, badge, name, acts, item, live: false, wantPlay: false };
    buildActs(rec);
    setRing(ring, item.pct);
    return rec;
  }

  function buildActs(rec) {
    rec.acts.replaceChildren();
    const st = normalizeState(rec.item.state);
    if (st === 'pending' || st === 'queued') {
      rec.acts.appendChild(tileBtn(S.assets.tileCompress, () => store.compress([rec.id])));
    } else if (st === 'working') {
      rec.acts.appendChild(tileBtn(S.assets.tileCancel, () => store.cancel([rec.id])));
    } else if (st === 'failed') {
      rec.acts.appendChild(tileBtn(S.assets.tileRetry, () => store.compress([rec.id])));
    } else if (st === 'ready') {
      rec.acts.appendChild(tileBtn(S.assets.tileDelete, () => store.deleteOne([rec.id])));
    }
  }

  function tileBtn(label, onClick) {
    const b = el('button', { type: 'button', class: 'gg-asset-act', text: label });
    ledger.listen(b, 'click', (e) => {
      e.preventDefault();
      try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
      try { onClick(); } catch (err) { logger?.warn?.('[GG assets] tile action threw: ' + ((err && err.message) || err)); }
    });
    return b;
  }

  /* ---------------------------------------------------------- the media */

  function attachMedia(rec) {
    if (!rec || rec.media) return;
    const item = rec.item;
    const st = normalizeState(item.state);

    if (st === 'ready' && item.prevUrl && livePreviews < MAX_LIVE_PREVIEWS) {
      const v = el('video', {
        class: 'gg-asset-vid',
        muted: true,
        loop: true,
        playsinline: true,
        preload: 'metadata',
        // #t=0.1 makes the runtime paint the first real frame as the poster
        // instead of a black box while the file is still being decoded.
        src: item.prevUrl + '#t=0.1',
      });
      v.muted = true;
      rec.media = v;
      rec.live = true;
      livePreviews++;
      rec.mediaHost.appendChild(v);
      if (reduceMotion && !rec.wantPlay) {
        rec.node.classList.add('is-clicktoplay');
        // Wired ONCE per tile: a tile can enter and leave the viewport all day,
        // and a listener per crossing is a leak with a scrollbar attached.
        if (!rec.clickWired) {
          rec.clickWired = true;
          ledger.listen(rec.mediaHost, 'click', () => {
            rec.wantPlay = true;
            rec.node.classList.remove('is-clicktoplay');
            try { rec.media?.play?.(); } catch (_e) { /* ignore */ }
          });
        }
      } else {
        try { const p = v.play?.(); if (p && p.catch) p.catch(() => {}); } catch (_e) { /* autoplay refused */ }
      }
      return;
    }

    const url = (st === 'exempt') ? (item.srcUrl || item.artUrl) : (st === 'ready' ? (item.artUrl || item.srcUrl) : '');
    if (!url) return;                        // queued/working/failed stay grey
    const img = el('img', { class: 'gg-asset-img', src: url, alt: '', decoding: 'async', loading: 'lazy' });
    rec.media = img;
    rec.mediaHost.appendChild(img);
  }

  function detachMedia(rec) {
    if (!rec || !rec.media) return;
    const m = rec.media;
    rec.media = null;
    if (rec.live) { rec.live = false; livePreviews = Math.max(0, livePreviews - 1); }
    try { if (m.pause) m.pause(); } catch (_e) { /* ignore */ }
    try { m.removeAttribute('src'); } catch (_e) { /* ignore */ }
    try { if (m.load) m.load(); } catch (_e) { /* ignore */ }
    try { m.remove(); } catch (_e) { /* ignore */ }
  }

  /* ----------------------------------------------------------- the grid */

  function computeView() {
    const out = [];
    for (const it of items) if (matchesFilter(it, filter, query)) out.push(it);
    return out;
  }

  function keyOf(list) {
    let k = String(list.length) + '|';
    for (let i = 0; i < list.length; i++) k += list[i].id + ',';
    return k;
  }

  function rebuildGrid() {
    for (const rec of tiles.values()) {
      detachMedia(rec);
      if (io) { try { io.unobserve(rec.node); } catch (_e) { /* ignore */ } }
    }
    tiles.clear();
    livePreviews = 0;
    rendered = 0;
    grid.replaceChildren();
    view = computeView();
    viewKey = keyOf(view);
    grow();
    paintEmpty();
  }

  function grow() {
    const target = Math.min(view.length, rendered + CHUNK);
    for (let i = rendered; i < target; i++) {
      const rec = makeTile(view[i]);
      tiles.set(rec.id, rec);
      grid.appendChild(rec.node);
      if (io) { try { io.observe(rec.node); } catch (_e) { attachMedia(rec); } }
      else attachMedia(rec);
    }
    rendered = target;
    const left = view.length - rendered;
    moreBtn.textContent = S.assets.more(left);
    moreBtn.hidden = left <= 0;
  }

  function paintEmpty() {
    if (!state.available) { emptyLine.hidden = true; return; }
    if (!store.isListLoaded) { emptyLine.hidden = false; emptyLine.textContent = S.assets.loading; return; }
    if (view.length) { emptyLine.hidden = true; return; }
    emptyLine.hidden = false;
    emptyLine.textContent = (items.length && (query || filter !== 'all')) ? S.assets.emptyFiltered : S.assets.empty;
  }

  /** Progress arrived: move the tiles we already built, in place. */
  function paintTiles() {
    for (const rec of tiles.values()) {
      const fresh = store.item(rec.id);
      if (fresh) rec.item = fresh;
      const st = normalizeState(rec.item.state);
      const wasState = rec.node.dataset.state;
      rec.badge.textContent = badgeFor(rec.item);
      setRing(rec.ring, st === 'working' ? rec.item.pct : (st === 'ready' || st === 'exempt' ? 100 : 0));
      if (wasState !== st) {
        rec.node.dataset.state = st;
        buildActs(rec);
        // A tile that just finished has different media to show than the grey
        // placeholder it was; re-attach if it is still on screen.
        if (rec.media) { detachMedia(rec); attachMedia(rec); }
      }
    }
  }

  /* --------------------------------------------------------- the chrome */

  function needsCount() {
    if (store.isListLoaded) {
      let n = 0;
      for (const it of items) if (NEEDS_STATES.indexOf(normalizeState(it.state)) >= 0) n++;
      return n;
    }
    return (state.pending | 0) + (state.queued | 0) + (state.working | 0);
  }

  function paintSeg() {
    for (const [f, b] of segBtns) b.setAttribute('aria-pressed', String(f === filter));
  }

  function paintChrome() {
    if (!state.available) return;
    chipReady.textContent = S.assets.statReady(state.ready);
    const needs = needsCount();
    chipNeeds.textContent = S.assets.statNeeds(needs);
    chipFailed.textContent = S.assets.statFailed(state.failed);
    chipFailed.hidden = state.failed <= 0;
    chipUsage.textContent = S.assets.statUsage(formatUsage(state.usedBytes, state.capBytes));

    compressBtn.textContent = needs > 0 ? S.assets.compressAll(needs) : S.assets.compressAllIdle;
    compressBtn.disabled = needs <= 0;

    const byMatch = state.paused && state.pausedBy === 'match';
    pauseBtn.textContent = state.paused ? S.assets.resume : S.assets.pause;
    pauseBtn.disabled = byMatch;                    // the match owns that flag
    deleteBtn.disabled = state.usedBytes <= 0;

    etaLine.textContent = etaText();
    etaLine.hidden = etaLine.textContent === '';

    const notice = state.overCap ? S.assets.overCap
      : (state.presetChanged && needs > 0 ? S.assets.presetChanged(needs) : '');
    noticeLine.textContent = notice;
    noticeLine.hidden = notice === '';

    const gb = capGb(state.capBytes);
    if (!capDirty && String(capInput.value) !== String(gb)) capInput.value = String(gb);
    capValue.textContent = S.assets.capValue(Number(capInput.value) || gb);
  }

  function etaText() {
    if (state.paused) return state.pausedBy === 'match' ? S.assets.etaPausedMatch : S.assets.etaPausedUser;
    if (needsCount() <= 0) return '';
    const mins = etaMinutes(state.etaMs);
    if (!mins) return S.assets.etaEstimating;
    return S.assets.eta(S.assets.minutes(mins), state.hw ? S.assets.encoderHw : S.assets.encoderSw);
  }

  /* -------------------------------------------------------- the actions */

  function onCompressAll() {
    const bytes = pendingInputBytes(items);
    const heavy = state.etaMs > CONFIRM_ETA_MS || bytes > CONFIRM_INPUT_BYTES;
    if (!heavy || !sheets || !sheets.open) { store.compressAll(); return; }
    const mins = etaMinutes(state.etaMs);
    const c = S.assets.confirmCompress;
    sheets.open({
      icon: c.icon,
      headline: c.headline,
      line: c.line(
        mins ? S.assets.minutes(mins) : S.assets.etaEstimating,
        formatBytes(bytes),
        state.hw ? S.assets.encoderHw : S.assets.encoderSw,
      ),
      actions: [
        { id: 'cancel', label: c.cancel, variant: 'ghost' },
        { id: 'go', label: c.go, variant: 'primary' },
      ],
    }).then((pick) => { if (pick === 'go') store.compressAll(); });
  }

  function onPauseToggle() {
    if (state.paused) store.resume('user');
    else store.pause('user');
  }

  function onDeleteAll() {
    const d = S.assets.confirmDelete;
    if (!sheets || !sheets.open) { store.deleteAll(); return; }
    sheets.open({
      icon: d.icon,
      headline: d.headline,
      line: d.line(formatBytes(state.usedBytes)),
      actions: [
        { id: 'cancel', label: d.cancel, variant: 'ghost' },
        { id: 'go', label: d.go, variant: 'primary' },
      ],
    }).then((pick) => { if (pick === 'go') store.deleteAll(); });
  }

  /* ---------------------------------------------------- the subscriptions */

  let unavailableShown = false;
  function showUnavailable() {
    if (unavailableShown) return;
    unavailableShown = true;
    // No host cache does NOT mean no library any more: standalone gets a picker
    // and its files ride the exempt path straight into the transfer queue.
    container.replaceChildren(localMediaCard({ store, ledger, audio, actions, logger }));
  }

  ledger.sub(store.onState((s) => {
    state = s;
    if (!s.available) { showUnavailable(); return; }
    paintChrome();
  }));

  ledger.sub(store.onItems((list) => {
    items = list;
    if (!state.available) return;
    const next = computeView();
    const key = keyOf(next);
    if (key !== viewKey) { view = next; viewKey = key; rebuildGrid(); }
    else paintTiles();
    paintChrome();
    paintEmpty();
  }));

  if (state.available) {
    paintSeg();
    paintChrome();
    rebuildGrid();
    store.requestList();
  } else {
    showUnavailable();
  }

  return {
    unmount() {
      for (const rec of tiles.values()) detachMedia(rec);
      tiles.clear();
      ledger.dispose();
    },
  };
}

/** The standalone / no-host card. Never a spinner: there is nothing coming. */
function unavailableCard(onBack, ledger, audio) {
  const kids = [
    el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: S.assets.eyebrow })]),
    el('h2', { class: 'gg-assets-standalone-head', text: S.assets.standaloneHeadline }),
    el('p', { class: 'gg-lead', text: S.assets.standaloneLine }),
  ];
  if (onBack && ledger) {
    kids.push(el('div', { class: 'gg-assets-foot-actions' }, [
      button(ledger, S.assets.back, onBack, { variant: 'ghost', audio: audio || null, sfx: 'ui-back' }),
    ]));
  }
  return el('div', { class: 'gg-card gg-assets gg-assets--off' }, kids);
}

/* ---------------------------------------------------------------------------
 * STANDALONE — the device-picker library. No queue, no cache, no host: files
 * are adopted in-memory (store.addLocalFiles), sent as exempt originals, and
 * die with the page. Everything renders from store.items so a match screen
 * asking listSendable() and this card can never disagree about what exists.
 * ------------------------------------------------------------------------ */
function localMediaCard({ store, ledger, audio, actions, logger }) {
  const L = S.assets.local;
  const maxText = formatBytes(LOCAL_MAX_BYTES);
  const artMaxText = formatBytes(LOCAL_ARTIFACT_MAX_BYTES);

  const input = el('input', { type: 'file', class: 'gg-sr-only', multiple: true, accept: LOCAL_ACCEPT, 'aria-hidden': 'true', tabindex: '-1' });
  const addBtn = button(ledger, L.add, () => { try { input.click(); } catch (_e) { /* ignore */ } }, { variant: 'primary', audio });
  const status = el('p', { class: 'gg-assets-eta', text: '', role: 'status', hidden: true });
  /* A SECOND status line, and not the same one as the summary: the summary is
   * the answer and this is the wait. Folding them together means the last run's
   * result is what the player stares at while the next one runs. */
  const progress = el('p', { class: 'gg-assets-eta gg-local-progress', text: '', role: 'status', hidden: true });
  const list = el('div', { class: 'gg-local-list', role: 'list' });

  /* Adding a big zip is minutes of decoding and encoding now, so the card says
   * so while it happens. Through the ledger, like every other subscription. */
  if (typeof store.onLocalProgress === 'function') ledger.sub(store.onLocalProgress((p) => {
    if (!p || !p.running) { progress.hidden = true; progress.textContent = ''; return; }
    progress.hidden = false;
    if (p.pct > 0 && p.name) progress.textContent = L.compressing(p.name, p.pct);
    else if (p.total > 1) progress.textContent = L.adding(Math.min(p.done + 1, p.total), p.total);
    else progress.textContent = p.name ? L.addingOne(p.name) : L.adding(p.done, Math.max(1, p.total));
  }));

  ledger.listen(input, 'change', () => {
    const files = input.files;
    if (!files || !files.length) return;
    addBtn.disabled = true;
    status.hidden = true;
    store.addLocalFiles(files).then((r) => {
      addBtn.disabled = false;
      try { input.value = ''; } catch (_e) { /* ignore */ }
      const bits = [];
      if (r.added) bits.push(L.added(r.added));
      // Worth its own clause: the whole point of the compression lanes is that
      // files which used to be refused are now sendable, and the player should
      // be told that is what happened to them.
      if (r.compressed) bits.push(L.compressed(r.compressed));
      if (r.dupes) bits.push(L.skipDupe(r.dupes));
      if (r.tooBig) bits.push(L.skipBig(r.tooBig, maxText));
      // Video is the one family the page cannot re-encode, so it gets the
      // ARTIFACT cap in its own sentence rather than the exempt one.
      if (r.tooBigVideo) bits.push(L.skipBigVideo(r.tooBigVideo, artMaxText));
      if (r.badType) bits.push(L.skipType(r.badType));
      if (r.failed) bits.push(L.skipFailed(r.failed));
      // "Took the first 500" is not a failure and must never read as one.
      if (r.trimmed) bits.push(L.trimmed(r.trimmed, LOCAL_ZIP_MAX_ENTRIES));
      // An archive we could not OPEN is its own sentence. It must never be
      // folded into skipBig: the per-file cap text on an archive failure is how
      // a 1 GB library got reported as "1 over 8 MB".
      if (r.zipBad) bits.push(L.zipBad(r.zipBad));
      // An archive that opened and held nothing sendable would otherwise say
      // nothing at all, which reads as "the button is broken".
      if (!bits.length && r.zips) bits.push(L.zipNone);
      status.textContent = bits.join(' · ');
      status.hidden = bits.length === 0;
      if (r.added) { try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ } }
    }).catch((e) => {
      addBtn.disabled = false;
      logger?.warn?.('[GG assets] local add threw: ' + ((e && e.message) || e));
    });
  });

  function row(it) {
    const thumb = el('div', { class: 'gg-local-thumb' });
    // A COMPRESSED pick has no srcUrl — its bytes are the artifact, behind
    // artUrl. Reading only srcUrl here is how a compressed photo renders as the
    // word "image" next to a perfectly good thumbnail.
    const previewUrl = it.srcUrl || it.artUrl || '';
    if (previewUrl && it.kind === 'image') {
      thumb.appendChild(el('img', { src: previewUrl, alt: '', decoding: 'async', loading: 'lazy' }));
    } else {
      thumb.appendChild(el('span', { class: 'gg-local-thumb-kind', text: it.kind === 'video' ? 'video' : 'image' }));
    }
    const name = el('span', { class: 'gg-local-name' });
    name.textContent = String(it.name || '');          // user data — text, always
    name.title = String(it.name || '');
    // What it weighs ON THE WIRE, and what it weighed before — a compressed pick
    // showing only its new size looks like the picker lied about the file.
    const shrunk = it.srcBytes > it.bytes;
    const size = el('span', {
      class: 'gg-local-size',
      text: shrunk ? L.sizeShrunk(formatBytes(it.srcBytes), formatBytes(it.bytes)) : formatBytes(it.bytes),
    });
    const rm = el('button', { type: 'button', class: 'gg-asset-act', text: L.remove });
    ledger.listen(rm, 'click', () => {
      try { audio?.sfx?.('ui-back'); } catch (_e) { /* stub bus */ }
      store.removeLocal(it.id);
    });
    return el('div', { class: 'gg-local-row', role: 'listitem' }, [thumb, name, size, rm]);
  }

  function paint() {
    const locals = (store.items || []).filter((it) => it && String(it.id).indexOf('local:') === 0);
    list.replaceChildren();
    if (!locals.length) {
      list.appendChild(el('p', { class: 'gg-assets-empty', text: L.empty }));
      return;
    }
    for (const it of locals) list.appendChild(row(it));
  }
  ledger.sub(store.onItems(() => paint()));
  paint();

  return el('div', { class: 'gg-card gg-assets gg-assets--local' }, [
    el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: S.assets.eyebrow })]),
    el('h2', { class: 'gg-assets-standalone-head', text: L.headline }),
    el('p', { class: 'gg-lead', text: L.line }),
    el('div', { class: 'gg-assets-tools-row gg-local-tools' }, [addBtn, input]),
    el('p', { class: 'gg-assets-note', text: L.limits(maxText, artMaxText) }),
    progress,
    status,
    list,
    el('p', { class: 'gg-assets-note', text: L.note }),
    el('div', { class: 'gg-assets-foot-actions' }, [
      button(ledger, S.assets.back, () => { actions?.goTitle?.(); }, { variant: 'ghost', audio, sfx: 'ui-back' }),
    ]),
  ]);
}

export default { mount };
