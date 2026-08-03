// For You feed — orchestrator. Owns the bridge, settings, the vertical pager and
// the chrome; feed.js owns selection, surfaces.js owns tiles/media.

import * as stats from './stats.js';
import { createFeed } from './feed.js';
import { createPage } from './surfaces.js';

const PAGE_TRANSITION_MS = 340;
const CLIP_VIEWED_MIN_DWELL_MS = 5000; // below this a skim earns no XP
const ATTENTION_MIN_GAP_MS = 120000;   // attention target every 2-4 min
const ATTENTION_RAND_MS = 120000;
const ATTENTION_LIFETIME_MS = 6000;

const $ = (id) => document.getElementById(id);
const viewport = $('viewport');
const track = $('track');

// ---------- bridge ----------

const bridge = window.chrome?.webview;
function post(msg) {
  try { bridge?.postMessage(msg); } catch { /* host gone */ }
}

// ---------- state ----------

let assets = [];
let settings = {
  layout: 'duo',
  includeGifs: true,
  mosaicAutoChange: true,
  mosaicChangeSec: 10,
  autoAdvance: false,
  muted: false,
};
let feed = null;
const pages = new Map();      // compKey -> page object
const audioFocus = new Map(); // compKey -> tileIndex
let activeIdx = 0;
let booted = false;
let attTimer = null;

const pageH = () => viewport.clientHeight || 1;
const aspect = () => (viewport.clientWidth || 1) / pageH();

function setting(key, value) {
  settings[key] = value;
  post({ type: 'settings-changed', key, value });
}

// ---------- page context ----------

const pageCtx = {
  onAdvance(compKey) {
    if (feed.comps[activeIdx]?.key === compKey) next();
  },
  onSwap(compKey, tileIndex, dir) {
    const cut = feed.swapTile(compKey, tileIndex, dir);
    if (cut && tileIndex === 0 && feed.comps[activeIdx]?.key === compKey) updateCaption();
    return cut;
  },
  onTapAudio(compKey, tileIndex) {
    const comp = feed.comps.find((c) => c.key === compKey);
    if (comp?.tiles[tileIndex]?.cut.asset.type !== 'video') return;
    audioFocus.set(compKey, tileIndex);
    if (settings.muted) setMuted(false); // a tap means "let me hear this one"
  },
  onMorph(compKey) {
    const tiles = feed.morph(compKey);
    if (tiles) {
      audioFocus.delete(compKey); // a stale index would point at the wrong clip
      if (feed.comps[activeIdx]?.key === compKey) updateCaption();
    }
    return tiles;
  },
  onAssetMeta(asset, meta) {
    post({ type: 'asset-meta', id: asset.id, ...meta });
    // Backfill in place — improves future picks WITHOUT resetting the feed.
    if (meta.durationMs != null) asset.durationMs = meta.durationMs;
    if (meta.width != null) { asset.width = meta.width; asset.height = meta.height; }
    feed.noteAssetMeta();
  },
  report(segIdKey, dwellMs, clipLenMs) {
    stats.recordView(segIdKey, dwellMs, clipLenMs);
    if (dwellMs >= CLIP_VIEWED_MIN_DWELL_MS) {
      post({ type: 'clip-viewed', segId: segIdKey, dwellMs: Math.round(dwellMs) });
    }
  },
  isAutoAdvance: () => settings.autoAdvance,
  isMuted: () => settings.muted,
  audioFocus: (compKey) => audioFocus.get(compKey),
  mosaicAutoChange: () => settings.mosaicAutoChange,
  mosaicChangeMs: () => settings.mosaicChangeSec * 1000,
};

// ---------- pager ----------

function pageAt(i) {
  const comp = feed.comps[i];
  return comp ? pages.get(comp.key) : undefined;
}

function applyTrack(animated) {
  track.style.transition = animated
    ? `transform ${PAGE_TRANSITION_MS}ms cubic-bezier(0.22, 0.9, 0.36, 1)`
    : 'none';
  track.style.transform = `translateY(${-activeIdx * pageH()}px)`;
}

/** Reconcile page DOM with feed.comps (after configure/extend/trim). */
function syncPages() {
  const comps = feed.comps;
  const keep = new Set(comps.map((c) => c.key));
  for (const [key, page] of pages) {
    if (!keep.has(key)) {
      page.destroy();
      pages.delete(key);
      audioFocus.delete(key);
    }
  }
  comps.forEach((c, i) => {
    let page = pages.get(c.key);
    if (!page) {
      page = createPage(c, pageCtx);
      pages.set(c.key, page);
      track.appendChild(page.el);
    }
    page.el.style.top = (i * 100) + '%';
  });
}

function goTo(idx, animated = true) {
  const comps = feed.comps;
  if (idx < 0 || idx >= comps.length || idx === activeIdx) return;
  const prevPage = pageAt(activeIdx);
  activeIdx = idx;
  const page = pageAt(idx);
  page?.setActive(true); // both pages live during the slide — no black gap
  applyTrack(animated);
  if (prevPage && prevPage !== page) {
    if (animated) {
      setTimeout(() => { if (pageAt(activeIdx) !== prevPage) prevPage.setActive(false); }, PAGE_TRANSITION_MS + 40);
    } else {
      prevPage.setActive(false);
    }
  }
  updateCaption();
  // Append (and possibly head-trim) OUTSIDE the slide so the compensating
  // instant re-offset can never cancel the animation mid-flight.
  setTimeout(maybeExtend, animated ? PAGE_TRANSITION_MS + 60 : 0);
}

const next = () => goTo(activeIdx + 1);
const prev = () => goTo(activeIdx - 1);

function maybeExtend() {
  if (!feed || feed.comps.length === 0) return;
  if (activeIdx < feed.comps.length - 3) return;
  const { appended, trimmed } = feed.extend();
  if (!appended && !trimmed) return;
  if (trimmed) activeIdx = Math.max(0, activeIdx - trimmed);
  syncPages();
  applyTrack(false); // same visual position: pages shifted up + offset reduced together
}

// ---------- config / rebuild ----------

function applyConfig() {
  const reset = feed.configure(assets, settings.includeGifs, settings.layout);
  if (reset) {
    for (const p of pages.values()) p.destroy();
    pages.clear();
    audioFocus.clear();
    activeIdx = 0;
    syncPages();
    applyTrack(false);
    pageAt(0)?.setActive(true);
    updateCaption();
  }
  updateEmptyState();
}

/** Crash-retry: rebuild the whole engine from the current manifest. */
function hardReset() {
  for (const p of pages.values()) { try { p.destroy(); } catch { /* dying anyway */ } }
  pages.clear();
  audioFocus.clear();
  track.innerHTML = '';
  activeIdx = 0;
  feed = createFeed(aspect);
  applyConfig();
}

// ---------- chrome ----------

function updateCaption() {
  const cap = pageAt(activeIdx)?.caption();
  $('caption-name').textContent = cap?.filename ?? '';
  $('caption-folder').textContent = cap?.folder ?? '';
}

function setMuted(m) {
  settings.muted = m;
  post({ type: 'settings-changed', key: 'muted', value: m });
  $('btn-mute').textContent = m ? '🔇' : '🔊';
  $('btn-mute').classList.toggle('off', m);
  pageAt(activeIdx)?.applyAudio();
}

function setAutoAdvance(on) {
  settings.autoAdvance = on;
  post({ type: 'settings-changed', key: 'autoAdvance', value: on });
  $('btn-advance').classList.toggle('off', !on);
  $('btn-advance').title = on
    ? 'Auto-advance ON: scrolls on when a clip ends'
    : 'Auto-advance OFF: clips loop until you scroll';
  pageAt(activeIdx)?.applyAutoAdvance();
}

function updateChromeForLayout() {
  // Random mode advances itself via the morph timer — hide the toggle there.
  $('btn-advance').classList.toggle('hidden', settings.layout === 'random');
}

function updateOptionsUi() {
  document.querySelectorAll('.layout-chip').forEach((chip) => {
    chip.classList.toggle('selected', chip.dataset.layout === settings.layout);
  });
  $('toggle-gifs').classList.toggle('on', settings.includeGifs);
  $('toggle-mosaic').classList.toggle('on', settings.mosaicAutoChange);
  $('mosaic-sub').textContent = settings.mosaicAutoChange
    ? 'Mosaic reshuffles on a timer'
    : 'Mosaic holds until you swipe';
  $('mosaic-slider-row').classList.toggle('hidden', !settings.mosaicAutoChange);
  $('mosaic-sec').value = String(settings.mosaicChangeSec);
  $('mosaic-sec-label').textContent = `${settings.mosaicChangeSec}s`;
}

function updateEmptyState() {
  const empty = feed.comps.length === 0;
  $('empty').classList.toggle('hidden', !empty);
  if (empty) {
    const hiddenGifs = settings.includeGifs ? 0 : assets.filter((a) => a.type === 'gif').length;
    const btn = $('btn-include-gifs');
    btn.classList.toggle('hidden', hiddenGifs === 0);
    btn.textContent = `INCLUDE ${hiddenGifs} GIF${hiddenGifs === 1 ? '' : 'S'}`;
  }
}

function wireChrome() {
  $('btn-mute').addEventListener('click', () => setMuted(!settings.muted));
  $('btn-advance').addEventListener('click', () => setAutoAdvance(!settings.autoAdvance));
  $('btn-fullscreen').addEventListener('click', () => {
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
    else document.documentElement.requestFullscreen().catch(() => {});
  });
  $('btn-options').addEventListener('click', () => $('options-scrim').classList.remove('hidden'));
  $('btn-options-close').addEventListener('click', () => $('options-scrim').classList.add('hidden'));
  $('options-scrim').addEventListener('click', (e) => {
    if (e.target === $('options-scrim')) $('options-scrim').classList.add('hidden');
  });

  document.querySelectorAll('.layout-chip').forEach((chip) => {
    chip.addEventListener('click', () => {
      const layout = chip.dataset.layout;
      if (layout === settings.layout) return;
      setting('layout', layout);
      updateOptionsUi();
      updateChromeForLayout();
      applyConfig(); // layout change resets the feed to the top (mobile behavior)
    });
  });
  $('toggle-gifs').addEventListener('click', () => {
    setting('includeGifs', !settings.includeGifs);
    updateOptionsUi();
    applyConfig(); // pool identity changed
  });
  $('toggle-mosaic').addEventListener('click', () => {
    setting('mosaicAutoChange', !settings.mosaicAutoChange);
    updateOptionsUi();
    // live page picks the flag up on its next schedule pass
    const page = pageAt(activeIdx);
    if (page) { page.setActive(false); page.setActive(true); }
  });
  $('mosaic-sec').addEventListener('input', () => {
    $('mosaic-sec-label').textContent = `${$('mosaic-sec').value}s`;
  });
  $('mosaic-sec').addEventListener('change', () => {
    setting('mosaicChangeSec', Math.max(3, Math.min(60, Number($('mosaic-sec').value) || 10)));
    updateOptionsUi();
  });
  $('btn-include-gifs').addEventListener('click', () => {
    setting('includeGifs', true);
    updateOptionsUi();
    applyConfig();
  });
  $('btn-retry').addEventListener('click', () => {
    $('crash').classList.add('hidden');
    hardReset();
  });
}

// ---------- input ----------

function wireInput() {
  let wheelLockUntil = 0;
  viewport.addEventListener('wheel', (e) => {
    const now = performance.now();
    if (now < wheelLockUntil || Math.abs(e.deltaY) < 30) return;
    wheelLockUntil = now + PAGE_TRANSITION_MS + 60;
    if (e.deltaY > 0) next(); else prev();
  }, { passive: true });

  document.addEventListener('keydown', (e) => {
    switch (e.key) {
      case 'ArrowDown': case 'PageDown': next(); e.preventDefault(); break;
      case 'ArrowUp': case 'PageUp': prev(); e.preventDefault(); break;
      case 'm': case 'M': setMuted(!settings.muted); break;
      default: break;
    }
  });

  window.addEventListener('resize', () => applyTrack(false));

  // Minimized/hidden: tear the live page down (reports dwell, frees decoders);
  // restore on return.
  document.addEventListener('visibilitychange', () => {
    const page = pageAt(activeIdx);
    if (!page) return;
    if (document.hidden) page.setActive(false);
    else page.setActive(true);
  });
  window.addEventListener('pagehide', () => stats.flush());
}

// ---------- attention check ----------

function scheduleAttention() {
  clearTimeout(attTimer);
  attTimer = setTimeout(showAttention, ATTENTION_MIN_GAP_MS + Math.random() * ATTENTION_RAND_MS);
}

function showAttention() {
  const el = $('attention');
  if (document.hidden || !feed || feed.comps.length === 0) { scheduleAttention(); return; }
  el.style.left = (10 + Math.random() * 80) + '%';
  el.style.top = (12 + Math.random() * 72) + '%';
  el.classList.remove('hidden', 'hit');
  const gone = setTimeout(() => { el.classList.add('hidden'); scheduleAttention(); }, ATTENTION_LIFETIME_MS);
  el.onclick = () => {
    clearTimeout(gone);
    el.onclick = null;
    post({ type: 'attention-hit' });
    el.classList.add('hit');
    setTimeout(() => el.classList.add('hidden'), 400);
    scheduleAttention();
  };
}

// ---------- host messages ----------

function onHostMessage(data) {
  if (!data || typeof data !== 'object') return;
  switch (data.type) {
    case 'init': {
      assets = Array.isArray(data.assets) ? data.assets : [];
      settings = { ...settings, ...(data.settings || {}) };
      stats.init(data.stats, (s) => post({ type: 'stats-save', stats: s }));
      feed = createFeed(aspect);
      if (!booted) {
        booted = true;
        wireChrome();
        wireInput();
      }
      updateOptionsUi();
      updateChromeForLayout();
      $('btn-mute').textContent = settings.muted ? '🔇' : '🔊';
      $('btn-mute').classList.toggle('off', settings.muted);
      $('btn-advance').classList.toggle('off', !settings.autoAdvance);
      applyConfig();
      $('boot').classList.add('hidden');
      scheduleAttention();
      break;
    }
    default:
      break;
  }
}

// ---------- crash containment ----------

window.addEventListener('error', (e) => {
  // The feed is the crashiest surface (media-heavy) — contain it to a retry
  // panel instead of a dead page.
  try {
    $('crash-detail').textContent = String(e.message || 'unknown error');
    $('crash').classList.remove('hidden');
  } catch { /* truly broken */ }
});

// ---------- debug hooks (soak checks) ----------

Object.defineProperty(window, '__ccpDebug', {
  value: {
    get compsLen() { return feed?.comps.length ?? 0; },
    get historySize() { return feed?.historySize ?? 0; },
    get audioFocusCount() { return audioFocus.size; },
    get statCount() { return stats.statCount(); },
    get activeIdx() { return activeIdx; },
  },
});

// ---------- boot ----------

if (bridge) {
  bridge.addEventListener('message', (e) => onHostMessage(e.data));
  post({ type: 'ready' });
} else {
  // Opened in a plain browser (dev): show the empty state instead of hanging.
  $('boot').classList.add('hidden');
  $('empty').classList.remove('hidden');
}
