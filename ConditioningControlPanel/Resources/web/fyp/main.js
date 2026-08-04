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
const OPACITY_POST_MS = 100;           // throttle window-opacity posts while dragging
const OPACITY_MIN = 0.01;
// Eye control: one action per 600ms (a double-blink is a very common reflex), and a
// gaze point older than this is treated as stale — the host freezes gaze while the
// eyes are closed, so the point at blink time is always ~a moment old by design.
const EYE_ACTION_COOLDOWN_MS = 600;
const GAZE_STALE_MS = 1500;

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
  audioGlow: true,
  windowOpacity: 1,
  eyeControl: false,
  eyeGaze: false,
};
let feed = null;
const pages = new Map();      // compKey -> page object
const audioFocus = new Map(); // compKey -> tileIndex
let activeIdx = 0;
let booted = false;
let attTimer = null;
// Click-through is host state, never persisted here: it always starts OFF and
// the host turns it back off when the user hits the panic key.
let clickThrough = false;
// Eye control: the host owns the camera and tells us what is actually true.
let eyeStatus = { enabled: false, gaze: false, running: false, calibrated: false, reason: null };
let lastGaze = null;        // { x, y, t } normalized to the client area, or null
let lastEyeActionAt = 0;
let pageBusyUntil = 0;      // a page slide is in flight until this timestamp

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
  audioGlow: () => settings.audioGlow !== false,
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
  if (animated) pageBusyUntil = performance.now() + PAGE_TRANSITION_MS + 60;
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
  $('toggle-audio-glow').classList.toggle('on', settings.audioGlow !== false);
  const pct = Math.round(clampOpacity(settings.windowOpacity) * 100);
  $('window-opacity').value = String(pct);
  $('window-opacity-label').textContent = `${pct}%`;
  $('toggle-clickthrough').classList.toggle('on', clickThrough);
  updateEyeUi();
}

// ---------- window opacity / ghost mode ----------
//
// Both are HOST-side now. The slider sets the real translucency of the ghost mirror (a plain
// window whose constant alpha the DWM thumbnail respects), so there is nothing to draw here —
// the page only reports the value. Ghost mode itself parks the real window off-screen; the
// page keeps calling it `clickThrough` on the wire, which is all the host listens for.

function clampOpacity(v) {
  const n = Number(v);
  if (!Number.isFinite(n)) return 1;
  return Math.min(1, Math.max(OPACITY_MIN, Math.round(n * 100) / 100));
}

let opacityTimer = null;
let opacityPendingAt = 0;
let opacityPending = null;

function flushOpacity() {
  if (opacityTimer != null) { clearTimeout(opacityTimer); opacityTimer = null; }
  if (opacityPending == null) return;
  const v = opacityPending;
  opacityPending = null;
  opacityPendingAt = performance.now();
  setting('windowOpacity', v);
}

/** Live drag feedback without spamming the host: <=1 post per OPACITY_POST_MS. */
function queueOpacity(v) {
  opacityPending = v;
  if (opacityTimer != null) return;
  const wait = OPACITY_POST_MS - (performance.now() - opacityPendingAt);
  if (wait <= 0) flushOpacity();
  else opacityTimer = setTimeout(flushOpacity, wait);
}

let ctNoteTimer = null;

/** Ghost mode on/off. (`clickThrough` is the wire name; the host parks the real window
 *  off-screen behind a see-through DWM mirror.) */
function setClickThrough(on, fromHost) {
  clickThrough = !!on;
  document.body.classList.toggle('clickthrough', clickThrough);
  $('toggle-clickthrough').classList.toggle('on', clickThrough);
  clearTimeout(ctNoteTimer);
  if (clickThrough) {
    $('options-scrim').classList.add('hidden'); // nothing is clickable from here on
    stopAttention();                            // an unclickable target is just a nag
    // The chrome just vanished on purpose — say so, or it reads as a breakage.
    $('ct-note').classList.add('show');
    ctNoteTimer = setTimeout(() => $('ct-note').classList.remove('show'), 7000);
  } else {
    $('ct-note').classList.remove('show');
    scheduleAttention();
  }
  if (!fromHost) setting('clickThrough', clickThrough);
}

// ---------- eye control ----------
//
// The host does the sensing (blink / 2s eyes-closed / calibrated gaze) and posts
// three message types; everything below is just "which page thing does that mean".
// Deliberately independent of the chrome: in click-through mode the buttons are
// gone and the window takes no mouse, and eye control is exactly what is left.

function updateEyeUi() {
  $('toggle-eye').classList.toggle('on', !!settings.eyeControl);
  $('toggle-eye-gaze').classList.toggle('on', !!settings.eyeGaze);
  // Gaze needs the master toggle AND a trained calibration to mean anything.
  $('eye-gaze-row').classList.toggle('disabled', !(settings.eyeControl && eyeStatus.calibrated));
  // Calibration only needs a running camera, i.e. the master toggle.
  $('eye-calibrate-row').classList.toggle('disabled', !(settings.eyeControl && eyeStatus.running));
  $('btn-calibrate').textContent = eyeStatus.calibrated ? 'Recalibrate' : 'Calibrate';
  const line = eyeStatusText();
  $('eye-status').textContent = line ?? '';
  $('eye-status').classList.toggle('hidden', !line);
}

function eyeStatusText() {
  switch (eyeStatus.reason) {
    case 'starting': return 'Starting webcam...';
    case 'consent': return 'Webcam permission declined - eye control is off.';
    case 'no-camera': return 'Webcam unavailable - eye control is off.';
    case 'error': return 'Webcam error - eye control is off.';
    default: break;
  }
  if (!settings.eyeControl) return null;
  if (!eyeStatus.running) return 'Starting webcam...';
  if (!eyeStatus.calibrated) return 'Gaze needs calibration - blink still works.';
  return null; // running normally: no status line at all
}

/** True when a gesture must be ignored (a dialog is up, nothing to act on, or
 *  something is already animating). */
function eyeBusy() {
  if (!feed || feed.comps.length === 0 || document.hidden) return true;
  if (!$('options-scrim').classList.contains('hidden')) return true;  // options open
  if (!$('crash').classList.contains('hidden')) return true;
  if (!$('empty').classList.contains('hidden')) return true;
  const now = performance.now();
  return now - lastEyeActionAt < EYE_ACTION_COOLDOWN_MS || now < pageBusyUntil;
}

/** Tile the user is looking at, or -1 (gaze off / uncalibrated / off-window / stale). */
function gazeTile(page) {
  if (!settings.eyeGaze || !eyeStatus.calibrated || !lastGaze) return -1;
  if (performance.now() - lastGaze.t > GAZE_STALE_MS) return -1;
  return page.tileAtPoint(lastGaze.x * window.innerWidth, lastGaze.y * window.innerHeight);
}

/** Blink: swap ONE tile (gazed-at, else random). A single-tile page has nothing
 *  to swap within, so a blink pages on instead. */
function onEyeBlink() {
  if (eyeBusy()) return;
  const page = pageAt(activeIdx);
  if (!page) return;
  if (page.tileCount <= 1) {
    lastEyeActionAt = performance.now();
    next();
    return;
  }
  if (page.busy) return;
  let idx = gazeTile(page);
  if (idx < 0) idx = Math.floor(Math.random() * page.tileCount);
  // Same path as the › chevron: fresh weighted pick + cross-slide. A refusal
  // (slot mid-slide) costs nothing — leave the cooldown open for the next blink.
  if (page.swapTileAt(idx)) lastEyeActionAt = performance.now();
}

/** Eyes held shut ~2s: change the whole page — morph the mosaic, or scroll on. */
function onEyesClosed() {
  if (eyeBusy()) return;
  const page = pageAt(activeIdx);
  if (!page) return;
  lastEyeActionAt = performance.now();
  if (settings.layout === 'random' && page.morphNow()) return;
  next();
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
  $('toggle-audio-glow').addEventListener('click', () => {
    setting('audioGlow', settings.audioGlow === false);
    updateOptionsUi();
    pageAt(activeIdx)?.applyAudio(); // ring appears/disappears immediately
  });
  $('window-opacity').addEventListener('input', () => {
    const pct = Number($('window-opacity').value) || 100;
    $('window-opacity-label').textContent = `${pct}%`;
    queueOpacity(clampOpacity(pct / 100)); // the host owns the visual (ghost mirror alpha)
  });
  $('window-opacity').addEventListener('change', () => {
    const pct = Number($('window-opacity').value) || 100;
    opacityPending = clampOpacity(pct / 100); // the released value always lands
    flushOpacity();
    updateOptionsUi();
  });
  $('toggle-eye').addEventListener('click', () => {
    setting('eyeControl', !settings.eyeControl);
    // Optimistic UI; the host's eyeStatus is authoritative and will correct this
    // (including flipping the toggle back off if the camera refuses to start).
    if (!settings.eyeControl) { eyeStatus = { ...eyeStatus, running: false, reason: null }; lastGaze = null; }
    updateEyeUi();
  });
  $('toggle-eye-gaze').addEventListener('click', () => {
    if (!settings.eyeControl || !eyeStatus.calibrated) return; // row is inert anyway
    setting('eyeGaze', !settings.eyeGaze);
    updateEyeUi();
  });
  $('toggle-clickthrough').addEventListener('click', () => setClickThrough(!clickThrough, false));
  // The host runs the native calibration dialog (it owns the webcam); the click just asks.
  $('btn-calibrate').addEventListener('click', () => post({ type: 'calibrate' }));
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
      case 'Escape':
        // Only meaningful while the window still holds keyboard focus (right
        // after flipping the toggle — exactly when someone wants out). Native
        // fullscreen-exit on Esc still happens; that's fine, both say "back off".
        if (clickThrough) setClickThrough(false, false);
        break;
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
  attTimer = null;
  if (clickThrough) return; // the target could not be clicked anyway
  attTimer = setTimeout(showAttention, ATTENTION_MIN_GAP_MS + Math.random() * ATTENTION_RAND_MS);
}

/** Click-through just went on: drop the pending timer AND any live target. */
function stopAttention() {
  clearTimeout(attTimer);
  attTimer = null;
  const el = $('attention');
  el.onclick = null;
  el.classList.add('hidden');
  el.classList.remove('hit');
}

function showAttention() {
  const el = $('attention');
  if (clickThrough) return; // scheduleAttention() resumes it when control returns
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
      // Host may omit either (older builds) — both default rather than go undefined.
      settings.audioGlow = settings.audioGlow !== false;
      settings.windowOpacity = clampOpacity(settings.windowOpacity);
      clickThrough = false; // never sticky across a reload
      document.body.classList.remove('clickthrough');
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
    case 'clickThrough':
      // The host flips this off when the panic key gives the mouse back.
      setClickThrough(!!data.on, true);
      break;
    case 'openOptions':
      // The ghost gear button: the host has just un-ghosted us and wants the popover up.
      updateOptionsUi();
      $('options-scrim').classList.remove('hidden');
      break;
    case 'setMuted':
      // The ghost speaker button. setMuted echoes a settings-changed back; the host sets
      // the same value again, which is harmless.
      setMuted(!!data.on);
      break;
    case 'eyeStatus':
      eyeStatus = {
        enabled: !!data.enabled,
        gaze: !!data.gaze,
        running: !!data.running,
        calibrated: !!data.calibrated,
        reason: data.reason ?? null,
      };
      // The host is the source of truth for both toggles — a camera that refused
      // to start arrives here as enabled:false and resets the master pill.
      settings.eyeControl = eyeStatus.enabled;
      settings.eyeGaze = eyeStatus.gaze;
      if (!eyeStatus.enabled) lastGaze = null;
      updateEyeUi();
      break;
    case 'gaze':
      // Heavily smoothed and frozen while the eyes are shut: stamp it so a blink
      // can tell a live fixation from a stale one.
      lastGaze = data.inside === false
        ? null // looking off the feed window entirely -> no tile target
        : { x: Number(data.x) || 0, y: Number(data.y) || 0, t: performance.now() };
      break;
    case 'blink':
      onEyeBlink();
      break;
    case 'eyesClosed':
      onEyesClosed();
      break;
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
