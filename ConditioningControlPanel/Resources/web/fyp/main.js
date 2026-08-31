// For You feed — orchestrator. Owns the bridge, settings, the vertical pager and
// the chrome; feed.js owns selection, surfaces.js owns tiles/media.

import * as stats from './stats.js';
import { createFeed } from './feed.js';
import { createPage } from './surfaces.js';

const PAGE_TRANSITION_MS = 340;
// Neighbour pages this many steps from the active one stay mounted in PREVIEW
// (media loaded + seeked, paused on the window's first frame). This is at once
// the "never a black box" fix (a committed page resumes in place, no cold
// mount), the peek a half-swipe reveals, and the preload of the next page
// (preload='auto' buffers while the active clip plays). ±1 on purpose: a page
// can hold up to 4 decoders, and local files load fast enough that ±2 buys
// nothing but memory.
const NEIGHBOR_PAGES = 1;
// Live drag (ported back from the browser FYP, see FypFeed.tsx there):
const PAGE_COMMIT_FRAC = 0.22;         // drag this fraction of the viewport commits
const FLICK_V = 0.55;                  // px/ms release velocity that commits...
const FLICK_MIN_PX = 20;               // ...but a twitch is not a flick
const RUBBER = 0.35;                   // resistance where there is nothing to reveal
// Remote entries carry a poster still; warming the image cache this many pages
// ahead means even a swipe burst lands on a picture while the stream catches
// up. One fetch per url per session; local files have no poster (their paused
// preview frame plays that role) so this is online/mixed-mode work only.
const POSTER_WARM_AHEAD = 6;
const INTRO_EXIT_MS = 240;             // intro card is fully gone before the feed is built
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

let assets = [];            // local library manifest (init payload)
let settings = {
  layout: 'duo',
  includeGifs: true,
  mosaicAutoChange: true,
  mosaicChangeSec: 10,
  autoAdvance: false,
  muted: false,
  volume: 100,             // 0-100; independent of `muted` (mute is the panic switch)
  audioGlow: true,
  windowOpacity: 1,
  eyeControl: false,
  eyeGaze: false,
  source: 'library',        // 'library' | 'mixed' | 'online'
  onlineRatio: 30,          // mixed mode: % of picks that come from the online pool
  onlineConsented: false,   // one-time online-content consent accepted
};
// ---- online source state (Scrolller via the host) ----
let remoteAssets = [];      // appended online entries, session-scoped
let onlineCatalog = [];     // [{id,label,subs,selected}] from init
// The LIBRARY: every sub kept anywhere in the app, [{name, ok, videoCount, selected}]
// (ok null = never probed). `selected` is this feed's own membership - the app-wide
// selection the host stores as onlineCustomSubs. Toggling a pill changes the selection;
// the X forgets the sub everywhere (library-remove).
let customSubs = [];
let pendingSubs = [];       // names posted to the host, awaiting a 'sub-probe' verdict
let onlineOk = true;        // last online-status verdict
let remoteReqInFlight = false;
let lastRemoteReqAt = 0;
let remoteDryUntil = 0;     // backoff after an ok-but-empty batch (channels drained)
let remoteDryStep = 0;      // rung of the escalating dry ladder (reset by any fresh clip)
let remoteExhausted = false; // every channel drained: the feed is recycling on purpose
let exhaustionNoticed = false; // the on-feed note is a one-per-session courtesy
// Which niche rows have their sub list open. Page memory only — a disclosure
// triangle is not a preference worth a settings round-trip.
const openNicheSubs = new Set();
const seenRemote = new Set();  // remote asset ids that already played (buffer freshness)
let pendingSource = null;   // source waiting on the consent card
let feed = null;
const pages = new Map();      // compKey -> page object
const audioFocus = new Map(); // compKey -> tileIndex
const reportedMediaErrors = new Set(); // asset ids already reported this session (#562)
let activeIdx = 0;
let booted = false;
// The intro gate: false until the user presses DIVE IN. While it is false the feed has
// been configured with NOTHING - applyConfig() has never run, so no page/tile/<video>
// exists and the window cannot make a sound behind the card.
let started = false;
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

// ---------- online source plumbing ----------
//
// The host fetches (straight from the user's machine to Scrolller — never through CC
// Labs servers); this side only manages the buffer: ask when low, append what arrives,
// prune when the niche selection changes. Remote ids look like
// "scrolller/<subreddit>/<postId>" — the middle segment is the channel.

const REMOTE_LOWWATER = 60;         // ask for more when fewer unseen than this
const REMOTE_CAP = 1500;            // stop growing the session pool past this
const REMOTE_REQ_TIMEOUT_MS = 20000; // a request with no reply may be retried after this
const REMOTE_OFFLINE_BACKOFF_MS = 30000; // transport failure: breathe, then retry
// All channels came back with nothing NEW. Escalating, because "drained" is usually
// permanent for the session: a single-sub niche like r/censoredporn is ~64 clips in
// total, so a flat 30 s retry is just a slow version of the poll loop this replaces.
// Any batch with fresh ids resets the ladder (a sub that gained posts revives).
const REMOTE_DRY_LADDER_MS = [30000, 120000, 600000];
const CUSTOM_SUB_CAP = 20;          // mirror of the host's Take(20) - the FEED cap
const LIBRARY_CAP = 40;             // mirror of AppSettings.RemoteSubLibraryCap - the KEPT cap

const sourceUsesRemote = () => settings.source !== 'library';
const isRemoteId = (id) => typeof id === 'string' && id.startsWith('scrolller/');

function remoteShare() {
  if (settings.source === 'online') return 1;
  if (settings.source === 'mixed') return Math.min(0.95, Math.max(0.05, (Number(settings.onlineRatio) || 30) / 100));
  return 0;
}

function effectiveAssets() {
  if (settings.source === 'online') return remoteAssets.slice();
  if (settings.source === 'mixed') return assets.concat(remoteAssets);
  return assets;
}

function requestRemote() {
  const now = performance.now();
  if (remoteReqInFlight && now - lastRemoteReqAt < REMOTE_REQ_TIMEOUT_MS) return;
  remoteReqInFlight = true;
  lastRemoteReqAt = now;
  post({ type: 'need-remote' });
}

/** Keep the online buffer stocked: below the low-water mark in either total size or
 *  unseen clips → ask the host for a batch. Self-clocking — every append/status reply
 *  and every remote clip teardown calls back in here. */
function ensureRemoteBuffer() {
  if (!started || !sourceUsesRemote() || !settings.onlineConsented) return;
  if (remoteAssets.length >= REMOTE_CAP) return;
  if (performance.now() < remoteDryUntil) return;
  const unseen = remoteAssets.length - seenRemote.size;
  if (remoteAssets.length < REMOTE_LOWWATER || unseen < REMOTE_LOWWATER) requestRemote();
}

/** Channels currently in play, lowercased (mirror of the host's ActiveChannels). */
function activeChannelSet() {
  const set = new Set();
  for (const n of onlineCatalog) {
    if (n.selected) for (const s of n.subs || []) set.add(String(s).toLowerCase());
  }
  // Kept but not selected is not in play: the library is a shelf, the selection is the feed.
  for (const c of customSubs) if (c.selected !== false) set.add(String(c.name).toLowerCase());
  if (set.size === 0) {
    for (const s of onlineCatalog[0]?.subs || []) set.add(String(s).toLowerCase());
  }
  return set;
}

/** Niche selection changed: clips from deselected channels leave the pool now (the
 *  user just said "not that"), which resets the feed via the pool-identity check. */
function pruneRemoteToChannels() {
  const chans = activeChannelSet();
  const before = remoteAssets.length;
  remoteAssets = remoteAssets.filter((a) => chans.has((a.id.split('/')[1] || '').toLowerCase()));
  if (started && sourceUsesRemote() && remoteAssets.length !== before) applyConfig();
  ensureRemoteBuffer();
}

// ---------- page context ----------

const pageCtx = {
  onAdvance(compKey) {
    if (feed.comps[activeIdx]?.key === compKey) next();
  },
  isPagerBusy: () => performance.now() < pageBusyUntil,
  // Vertical drag, forwarded by whichever tile slot locked the 'y' axis. Only
  // the active page steers the track; a stale gesture (page changed under it)
  // is dropped rather than fought over.
  onPageDragMove(compKey, dy) {
    if (feed.comps[activeIdx]?.key !== compKey) return;
    const atEdge = (activeIdx <= 0 && dy > 0) || (activeIdx >= feed.comps.length - 1 && dy < 0);
    const eff = atEdge ? dy * RUBBER : dy;
    track.style.transition = 'none';
    track.style.transform = `translateY(${-activeIdx * pageH() + eff}px)`;
  },
  onPageDragEnd(compKey, dy, vy) {
    if (feed.comps[activeIdx]?.key !== compKey) { applyTrack(true); return; }
    let dir = 0;
    if (Math.abs(vy) > FLICK_V && Math.sign(vy) === Math.sign(dy) && Math.abs(dy) > FLICK_MIN_PX) {
      dir = dy < 0 ? 1 : -1;
    } else if (Math.abs(dy) > pageH() * PAGE_COMMIT_FRAC) {
      dir = dy < 0 ? 1 : -1;
    }
    // Commit continues the slide from wherever the pointer left the track
    // (goTo's applyTrack animates from the current transform); a refusal
    // (feed edge) or a short drag springs back the same way.
    if (dir === 0 || !goTo(activeIdx + dir)) applyTrack(true);
  },
  onSwap(compKey, tileIndex, dir) {
    const cut = feed.swapTile(compKey, tileIndex, dir);
    if (cut && tileIndex === 0 && feed.comps[activeIdx]?.key === compKey) updateCaption();
    return cut;
  },
  // The trade drag's face-up card: what a swap WOULD deal, resolved without
  // moving the ring or the history. The slot renders it riding the strip;
  // a commit installs this very cut (feed.swapTile honours the peek).
  onPeek(compKey, tileIndex, dir) {
    return feed.peekTile(compKey, tileIndex, dir);
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
  onMediaError(asset, detail) {
    // Once per asset per session: the host logs it (Warning) and strikes the id in
    // fyp_meta.json — two strikes and the manifest stops serving the file (#562).
    if (reportedMediaErrors.has(asset.id)) return;
    reportedMediaErrors.add(asset.id);
    post({ type: 'media-error', id: asset.id, code: detail?.code ?? 0, message: detail?.message || '' });
  },
  report(segIdKey, dwellMs, clipLenMs) {
    if (isRemoteId(segIdKey)) {
      // One-shot clips never enter the per-segment stats (useless there and the ids
      // churn forever); taste is learned per channel HOST-side from clip-viewed.
      seenRemote.add(segIdKey.slice(0, segIdKey.lastIndexOf(':')));
      ensureRemoteBuffer();
    } else {
      stats.recordView(segIdKey, dwellMs, clipLenMs);
    }
    if (dwellMs >= CLIP_VIEWED_MIN_DWELL_MS) {
      post({ type: 'clip-viewed', segId: segIdKey, dwellMs: Math.round(dwellMs) });
    }
  },
  isAutoAdvance: () => settings.autoAdvance,
  isMuted: () => settings.muted,
  // 0..1 for the media elements. Every surface reads this through applyAudio, so a
  // drag on the slider reaches the clip that is currently speaking AND every clip
  // mounted after it (mount() ends in applyAudio).
  volume: () => clampVolume(settings.volume) / 100,
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

/**
 * Enforce the page window around activeIdx: the active page LIVE, its ±NEIGHBOR
 * neighbours PREVIEW (mounted, paused on their first frame — the peek a
 * half-swipe reveals, and the warm media a commit resumes with no black gap),
 * everything else OFF. Hidden window: everything off — decoders freed, dwell
 * reported — exactly what the old binary active flag did on minimize.
 */
function applyWindow() {
  if (document.hidden) {
    for (const p of pages.values()) p.setMode('off');
    return;
  }
  feed.comps.forEach((c, i) => {
    const page = pages.get(c.key);
    if (!page) return;
    const d = Math.abs(i - activeIdx);
    page.setMode(d === 0 ? 'live' : d <= NEIGHBOR_PAGES ? 'preview' : 'off');
  });
  warmPosters();
}

// Poster pre-warm (online/mixed): one new Image() per url per session, well
// past the mounted window, so a swipe burst lands on a cached still while the
// stream spins up. Local entries have no posterUrl and skip out for free.
const warmedPosters = new Set();
function warmPosters() {
  const until = Math.min(activeIdx + POSTER_WARM_AHEAD, feed.comps.length - 1);
  for (let i = activeIdx + 1; i <= until; i++) {
    for (const t of feed.comps[i].tiles) {
      const u = t.cut.asset.posterUrl;
      if (!u || warmedPosters.has(u)) continue;
      warmedPosters.add(u);
      new Image().src = u;
    }
  }
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
  applyWindow();
}

/** Returns true when the pager actually moved (the drag release needs the
 *  refusal to spring back instead of committing into a wall). */
function goTo(idx, animated = true) {
  const comps = feed.comps;
  if (idx < 0 || idx >= comps.length || idx === activeIdx) return false;
  activeIdx = idx;
  // The window shift does all the media work in place: incoming preview
  // resumes (it was paused on the exact frame it shows — no black, no jump),
  // outgoing live page freezes as a preview and keeps its frame through the
  // slide, the far neighbour tears down (reporting its dwell).
  applyWindow();
  applyTrack(animated);
  if (animated) pageBusyUntil = performance.now() + PAGE_TRANSITION_MS + 60;
  updateCaption();
  // Append (and possibly head-trim) OUTSIDE the slide so the compensating
  // instant re-offset can never cancel the animation mid-flight.
  setTimeout(maybeExtend, animated ? PAGE_TRANSITION_MS + 60 : 0);
  return true;
}

const next = () => goTo(activeIdx + 1);
const prev = () => goTo(activeIdx - 1);

function maybeExtend() {
  if (!feed || feed.comps.length === 0) return;
  ensureRemoteBuffer(); // scroll progress is the natural "will need more soon" signal
  if (activeIdx < feed.comps.length - 3) return;
  const { appended, trimmed } = feed.extend();
  if (!appended && !trimmed) return;
  if (trimmed) activeIdx = Math.max(0, activeIdx - trimmed);
  syncPages();
  applyTrack(false); // same visual position: pages shifted up + offset reduced together
}

// ---------- config / rebuild ----------

function applyConfig() {
  const reset = feed.configure(effectiveAssets(), settings.includeGifs, settings.layout, remoteShare());
  if (reset) {
    for (const p of pages.values()) p.destroy();
    pages.clear();
    audioFocus.clear();
    activeIdx = 0;
    syncPages(); // applies the live/preview window around page 0
    applyTrack(false);
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

function clampVolume(v) {
  const n = Number(v);
  if (!Number.isFinite(n)) return 100;
  return Math.min(100, Math.max(0, Math.round(n)));
}

/**
 * Volume is deliberately NOT coupled to mute. Mute is the one-key panic switch (M, or the
 * speaker button) and has to give you back exactly the loudness you had, so unmuting never
 * touches this value and setting a volume never clears the mute. `post` is left to the
 * caller: a slider drag applies locally on every frame but only persists on release.
 */
function setVolume(pct, persist) {
  settings.volume = clampVolume(pct);
  if (persist) post({ type: 'settings-changed', key: 'volume', value: settings.volume });
  $('feed-volume-label').textContent = `${settings.volume}%`;
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
  $('feed-volume').value = String(clampVolume(settings.volume));
  $('feed-volume-label').textContent = `${clampVolume(settings.volume)}%`;
  $('toggle-audio-glow').classList.toggle('on', settings.audioGlow !== false);
  const pct = Math.round(clampOpacity(settings.windowOpacity) * 100);
  $('window-opacity').value = String(pct);
  $('window-opacity-label').textContent = `${pct}%`;
  $('toggle-clickthrough').classList.toggle('on', clickThrough);
  updateOnlineUi();
  updateEyeUi();
}

// ---------- online source UI ----------

function updateOnlineUi() {
  document.querySelectorAll('.source-chip').forEach((chip) => {
    chip.classList.toggle('selected', chip.dataset.source === settings.source);
  });
  $('online-ratio-row').classList.toggle('hidden', settings.source !== 'mixed');
  $('online-ratio').value = String(settings.onlineRatio);
  $('online-ratio-label').textContent = `${settings.onlineRatio}%`;
  $('online-config').classList.toggle('hidden', !sourceUsesRemote());

  // Niche chips are rebuilt in place — the catalog is tiny.
  const nicheBox = $('niche-chips');
  nicheBox.innerHTML = '';
  for (const n of onlineCatalog) {
    const chip = document.createElement('button');
    chip.className = 'niche-chip' + (n.selected ? ' selected' : '');
    chip.textContent = n.label;
    chip.addEventListener('click', () => {
      n.selected = !n.selected;
      setting('onlineNiches', onlineCatalog.filter((x) => x.selected).map((x) => x.id));
      updateOnlineUi();
      pruneRemoteToChannels();
    });
    nicheBox.appendChild(chip);
  }

  renderNicheSubs();
  renderCustomSubChips();

  const status = $('online-status');
  if (!sourceUsesRemote()) {
    status.classList.add('hidden');
  } else if (!onlineOk) {
    status.textContent = 'Online feed unreachable - retrying as you scroll.';
    status.classList.remove('hidden');
  } else if (remoteExhausted && remoteAssets.length > 0) {
    // Say it out loud instead of quietly re-serving: the pool is the whole niche.
    status.textContent = exhaustionLine(remoteAssets.length) + ' Add another niche for more.';
    status.classList.remove('hidden');
  } else if (remoteAssets.length > 0) {
    status.textContent = `${remoteAssets.length} online clips this session`;
    status.classList.remove('hidden');
  } else {
    status.classList.add('hidden');
  }
}

function exhaustionLine(n) {
  return `You've seen all ${n} clips in this niche - reshuffling.`;
}

/**
 * B2: one grey line per selected niche naming the subs it actually pulls from.
 * `n.subs` already rides in on the init payload, so this is pure disclosure - no
 * host round-trip, no setting. Collapsed it is a count (one line per niche, the
 * card is a 320px column); open it lists every r/ the niche resolves to, so a
 * "tiny" preset like Bambi Sleep cannot hide what it really is.
 * Rendered in its own container because #niche-chips is a flex-wrap chip row -
 * a full-width row inside it would fight the wrap.
 */
function renderNicheSubs() {
  const box = $('niche-subs');
  box.innerHTML = '';
  const selected = onlineCatalog.filter((n) => n.selected);
  // Nothing ticked = the host falls back to the first niche (activeChannelSet does
  // the same). Show THAT row rather than an empty gap, so the picker never implies
  // the feed is off.
  const rows = selected.length ? selected : onlineCatalog.slice(0, 1);
  for (const n of rows) {
    const subs = dedupeSubs(n.subs);
    if (!subs.length) continue;
    const open = openNicheSubs.has(n.id);
    const label = n.label + (selected.length ? '' : ' (default)');
    const row = document.createElement('button');
    row.type = 'button';
    row.className = 'niche-subs-row' + (open ? ' open' : '');
    row.textContent = open
      ? `▾ ${label} · ${subs.map((s) => 'r/' + s).join(' · ')}`
      : `▸ ${label} · ${subs.length} sub${subs.length === 1 ? '' : 's'}`;
    row.title = open ? 'Hide the subreddits' : 'Show the subreddits';
    row.addEventListener('click', () => {
      if (open) openNicheSubs.delete(n.id); else openNicheSubs.add(n.id);
      renderNicheSubs(); // only this list moves - no need to rebuild the whole card
    });
    box.appendChild(row);
  }
}

/** A niche's own list, case-insensitively deduped. Deliberately NOT deduped across
 *  niches: sissyhypno really is in both Hypno and Sissy, and hiding it from one of
 *  them would misrepresent that niche's contents. */
function dedupeSubs(subs) {
  const seen = new Set();
  const out = [];
  for (const raw of subs || []) {
    const s = String(raw || '').trim();
    if (!s) continue;
    const k = s.toLowerCase();
    if (seen.has(k)) continue;
    seen.add(k);
    out.push(s);
  }
  return out;
}

/** Names this feed currently pulls from, in library order. */
function selectedSubNames() {
  return customSubs.filter((s) => s.selected !== false).map((s) => s.name);
}

/** Commit the selection (and only the selection) to the host. The library itself only
 *  ever changes through a probe (add) or library-remove (forget). */
function commitSubSelection() {
  setting('onlineCustomSubs', selectedSubNames());
  updateOnlineUi();
  pruneRemoteToChannels();
}

/**
 * Library pills, three verdict states plus pending, times two membership states:
 *   orange  - the host probed Scrolller and it answered (ok === true)
 *   pink dashed - stored before verdicts existed (ok === null): unproven, still used
 *   muted "?"   - the probe said "not there" (ok === false); kept so the user can see
 *                 why it is dead instead of it silently vanishing
 *   .off        - kept but NOT in this feed right now; one click puts it back
 * The pill body toggles membership, the ✕ forgets the sub everywhere (library-remove) -
 * so a sub added for another surface (the Arcademy's sorting room) can sit here unlit
 * instead of quietly joining the feed. A pending pill is inert - it is a receipt for the
 * request in flight.
 */
function renderCustomSubChips() {
  const customBox = $('custom-sub-chips');
  customBox.innerHTML = '';
  for (const name of pendingSubs) {
    const chip = document.createElement('span');
    chip.className = 'niche-chip custom pending';
    chip.textContent = `r/${name}…`;
    chip.title = 'Checking that subreddit…';
    customBox.appendChild(chip);
  }
  for (const entry of customSubs) {
    const verified = entry.ok === true;
    const missing = entry.ok === false;
    const on = entry.selected !== false;
    const chip = document.createElement('button');
    chip.className = 'niche-chip custom'
      + (on ? ' selected' : ' off')
      + (verified ? ' verified' : missing ? ' unverified' : '');
    if (verified) {
      // videoCount 0 is a real answer (the sub exists, it just has no video) — say
      // "stills only" rather than a bare 0, and say nothing at all when the host
      // could not read a count.
      chip.title = (entry.videoCount === 0 ? 'verified · stills only'
        : entry.videoCount == null ? 'verified on Reddit'
        : `verified · ${entry.videoCount} clips on Reddit`)
        + (on ? ' · click to drop it from this feed' : ' · click to add it to this feed');
    } else if (missing) {
      chip.title = "That sub didn't have media - ✕ to forget it";
    } else {
      chip.title = on ? 'In this feed - click to drop it' : 'Kept - click to add it to this feed';
    }
    // Built from text nodes, not innerHTML: the name can come from an older
    // settings file and is never trusted as markup.
    chip.append(`r/${entry.name}`);
    if (missing) {
      const warn = document.createElement('span');
      warn.className = 'chip-warn';
      warn.textContent = '?';
      chip.appendChild(warn);
    }
    const x = document.createElement('span');
    x.className = 'chip-x';
    x.textContent = '✕';
    x.title = `Forget r/${entry.name} everywhere`;
    x.addEventListener('click', (ev) => {
      // The X is the LIBRARY gesture and it must not read as "just untick this".
      ev.stopPropagation();
      customSubs = customSubs.filter((s) => s !== entry);
      // The host owns the library: it drops the entry, its verdict and its feed
      // membership together, then pushes the fresh list back as {type:'library'}.
      post({ type: 'library-remove', name: entry.name });
      updateOnlineUi();
      pruneRemoteToChannels();
    });
    chip.appendChild(x);
    chip.addEventListener('click', () => {
      if (entry.selected === false && selectedSubNames().length >= CUSTOM_SUB_CAP) {
        showSubError(`That's the limit of ${CUSTOM_SUB_CAP} subs in the feed - drop one first`);
        return;
      }
      entry.selected = entry.selected === false;
      clearSubError();
      commitSubSelection();
    });
    customBox.appendChild(chip);
  }
}

/** "r/Name", full urls, stray punctuation → bare subreddit name (mirror of the host). */
function sanitizeSub(raw) {
  let s = String(raw || '').trim();
  const idx = s.toLowerCase().lastIndexOf('/r/');
  if (idx >= 0) s = s.slice(idx + 3);
  else if (s.toLowerCase().startsWith('r/')) s = s.slice(2);
  s = (s.match(/^[A-Za-z0-9_]+/) || [''])[0];
  return s.length >= 2 && s.length <= 40 ? s : null;
}

/** Init (or an older host) may hand us plain names; the current host sends verdicts.
 *  Accept both so a mismatched host/page pair still renders every pill.
 *  `selected` defaults to TRUE: a payload without it is the old shape, where the list
 *  WAS the feed - defaulting the other way would silently empty someone's feed. */
function normalizeCustomSubs(list) {
  if (!Array.isArray(list)) return [];
  const out = [];
  for (const raw of list) {
    const isObj = raw && typeof raw === 'object';
    const name = String((isObj ? raw.name : raw) || '').trim();
    if (!name) continue;
    const ok = isObj && (raw.ok === true || raw.ok === false) ? raw.ok : null; // null = never probed
    const vc = isObj && Number.isFinite(raw.videoCount) ? Number(raw.videoCount) : null;
    const selected = isObj && raw.selected === false ? false : true;
    out.push({ name, ok, videoCount: vc, selected });
  }
  return out;
}

/** The library if the host sent one, otherwise the old selection list read as a library
 *  where everything is selected (a host that predates the split). */
function normalizeLibrary(library, legacySelection) {
  const rows = normalizeCustomSubs(library);
  return rows.length ? rows : normalizeCustomSubs(legacySelection);
}

function showSubError(text) {
  const el = $('custom-sub-error');
  el.textContent = text;
  el.classList.remove('hidden');
}

function clearSubError() {
  $('custom-sub-error').classList.add('hidden');
}

const sameSub = (a, b) => String(a).toLowerCase() === String(b).toLowerCase();

/**
 * Submit a custom sub. Nothing is committed here any more: the page asks the host to
 * probe Scrolller and the HOST writes the sub into settings only if it answers, so a
 * typo can never become a permanently dead channel. Until the verdict lands the pill
 * shows pending and the typed text stays put - the old code silently emptied the box
 * on bad input, which read as "the app ate it".
 */
function addCustomSub() {
  const input = $('custom-sub-input');
  const raw = String(input.value || '').trim();
  const clean = sanitizeSub(raw);
  if (!clean) {
    showSubError(raw ? `"${raw}" isn't a subreddit name` : 'Type a subreddit name first');
    return;
  }
  if (pendingSubs.some((s) => sameSub(s, clean))) {
    showSubError(`r/${clean} is already being checked`);
    return;
  }
  // Typing a name the library already keeps is a SELECT, not an error: that is what
  // "added once" buys, and re-probing a known sub would spend a round trip to say so.
  const kept = customSubs.find((s) => sameSub(s.name, clean));
  if (kept) {
    if (kept.selected !== false) {
      showSubError(`r/${clean} is already in this feed`);
      return;
    }
    if (selectedSubNames().length >= CUSTOM_SUB_CAP) {
      showSubError(`That's the limit of ${CUSTOM_SUB_CAP} subs in the feed - drop one first`);
      return;
    }
    kept.selected = true;
    input.value = '';
    clearSubError();
    commitSubSelection();
    return;
  }
  if (selectedSubNames().length + pendingSubs.length >= CUSTOM_SUB_CAP) {
    showSubError(`That's the limit of ${CUSTOM_SUB_CAP} subs in the feed - drop one first`);
    return;
  }
  if (customSubs.length + pendingSubs.length >= LIBRARY_CAP) {
    showSubError(`Your kept list is full (${LIBRARY_CAP}) - remove one with its ✕ first`);
    return;
  }
  clearSubError();
  pendingSubs.push(clean);
  post({ type: 'probe-sub', sub: clean });
  updateOnlineUi();
}

/** Verdict for one probe. ok:false with no error = Scrolller has no such sub. */
function onSubProbe(data) {
  const name = String(data.sub || '').trim();
  if (!name) return;
  pendingSubs = pendingSubs.filter((s) => !sameSub(s, name));
  if (data.ok) {
    const vc = Number.isFinite(data.videoCount) ? Number(data.videoCount) : null;
    // A probe from THIS box is an add: the host kept it in the library and put it in the
    // app-wide selection, so the pill comes up lit.
    const existing = customSubs.find((s) => sameSub(s.name, name));
    if (existing) { existing.ok = true; existing.videoCount = vc; existing.selected = true; }
    else customSubs.push({ name, ok: true, videoCount: vc, selected: true });
    clearSubError();
    const input = $('custom-sub-input');
    // Only clear the box if it still holds THIS sub - the user may already be typing
    // the next one while the probe is in flight.
    if (sameSub(sanitizeSub(input.value) || '', name)) input.value = '';
    updateOnlineUi();
    pruneRemoteToChannels(); // the new channel joins the batch loop immediately
    return;
  }
  if (data.error === 'offline') showSubError("Couldn't reach the feed - try again");
  else if (data.error === 'invalid') showSubError(`r/${name} isn't a usable subreddit name`);
  else if (data.error) showSubError(`Couldn't check r/${name} - try again`);
  else showSubError(`r/${name} doesn't exist or has no media`);
  updateOnlineUi(); // drops the pending pill
}

let feedNoteTimer = null;

/** A small transient line over the feed itself. Same mechanism as the ghost-mode
 *  note (#ct-note): fades in, fades out, never takes a click. */
function showFeedNote(text, ms = 3000) {
  const el = $('feed-note');
  el.textContent = text;
  el.classList.add('show');
  clearTimeout(feedNoteTimer);
  feedNoteTimer = setTimeout(() => el.classList.remove('show'), ms);
}

/** First time a session runs the niche dry, tell the user on the FEED — most people
 *  never open the options card, and silent repetition reads as a broken shuffle.
 *  Once per session: after that the options status line carries it. */
function noteExhaustion() {
  if (exhaustionNoticed || !started || !remoteAssets.length) return;
  exhaustionNoticed = true;
  showFeedNote(exhaustionLine(remoteAssets.length));
}

/** Source chip pressed: consent-gate the first step away from the library. */
function requestSourceChange(src) {
  if (src === settings.source) return;
  if (src !== 'library' && !settings.onlineConsented) {
    pendingSource = src;
    $('consent-scrim').classList.remove('hidden');
    return;
  }
  applySource(src);
}

function applySource(src) {
  setting('source', src);
  updateOnlineUi();
  if (started) applyConfig();
  updateEmptyState();
  ensureRemoteBuffer();
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
  if (!empty) return;
  const gifBtn = $('btn-include-gifs');
  if (sourceUsesRemote()) {
    // Online/mixed with nothing yet: this is a loading (or offline) state, not
    // a "your library is empty" state.
    gifBtn.classList.add('hidden');
    $('empty-hint').classList.add('hidden');
    if (!onlineOk) {
      $('empty-emoji').textContent = '📡';
      $('empty-copy').innerHTML = "Couldn't reach the online feed.<br>Check your connection - it retries as you scroll.";
    } else {
      $('empty-emoji').textContent = '🌐';
      $('empty-copy').textContent = 'Tuning in to the online feed…';
    }
    return;
  }
  $('empty-emoji').textContent = '🎬';
  $('empty-copy').innerHTML = 'An endless feed of 20-40 second clips from your own videos and GIFs.<br>Add some to your assets folder to switch it on.';
  $('empty-hint').classList.remove('hidden');
  const hiddenGifs = settings.includeGifs ? 0 : assets.filter((a) => a.type === 'gif').length;
  gifBtn.classList.toggle('hidden', hiddenGifs === 0);
  gifBtn.textContent = `INCLUDE ${hiddenGifs} GIF${hiddenGifs === 1 ? '' : 'S'}`;
}

// ---------- intro gate ----------
//
// The window used to open straight into a wall of playing clips WITH audio. Everything
// media-shaped in this page hangs off applyConfig() -> syncPages() -> createPage(), so the
// gate is simply: don't call applyConfig() until the user asks for it. Held here rather
// than host-side because the host has no way to keep the page's own <video> elements from
// being created once the page has its manifest.

/** Show the card over the (still empty) feed. Called once, when init lands. */
function showIntro() {
  $('boot').classList.add('hidden');
  document.body.classList.add('intro-gate'); // the chrome has nothing to act on yet
  $('intro').classList.remove('hidden');
  // Enter/Space then start the feed without the user having to aim at the button.
  try { $('btn-intro-start').focus({ preventScroll: true }); } catch { /* focus is a nicety */ }
}

/** DIVE IN: card animates out first, and ONLY when it is gone does the feed build. */
function startFeed() {
  if (started) return;
  started = true;
  const intro = $('intro');
  intro.classList.add('gone');
  setTimeout(() => {
    intro.classList.add('hidden');
    document.body.classList.remove('intro-gate');
    if (!feed) return; // init never landed (host died) — nothing to build
    applyConfig();
    scheduleAttention();
    ensureRemoteBuffer();   // online/mixed: start filling before the user hits the end
  }, INTRO_EXIT_MS);
}

function wireIntro() {
  $('btn-intro-start').addEventListener('click', startFeed);
  // Art is optional: a missing or unreadable file collapses to a card with no image.
  $('intro-art').addEventListener('error', () => $('intro-art-wrap').classList.add('hidden'));
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
  document.querySelectorAll('.source-chip').forEach((chip) => {
    chip.addEventListener('click', () => requestSourceChange(chip.dataset.source));
  });
  $('online-ratio').addEventListener('input', () => {
    $('online-ratio-label').textContent = `${$('online-ratio').value}%`;
  });
  $('online-ratio').addEventListener('change', () => {
    setting('onlineRatio', Math.max(5, Math.min(95, Number($('online-ratio').value) || 30)));
    updateOnlineUi();
    if (started) applyConfig(); // same pool → no reset; just re-biases future picks
  });
  $('btn-add-sub').addEventListener('click', addCustomSub);
  $('custom-sub-input').addEventListener('input', clearSubError);
  $('custom-sub-input').addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); addCustomSub(); }
    e.stopPropagation(); // typing must never page the feed (arrow keys etc.)
  });
  $('btn-consent-accept').addEventListener('click', () => {
    settings.onlineConsented = true;
    post({ type: 'settings-changed', key: 'onlineConsented', value: true });
    $('consent-scrim').classList.add('hidden');
    const src = pendingSource || 'mixed';
    pendingSource = null;
    applySource(src);
  });
  $('btn-consent-cancel').addEventListener('click', () => {
    pendingSource = null;
    $('consent-scrim').classList.add('hidden');
    updateOnlineUi(); // chips snap back to the real (library) source
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
    if (page) { page.setMode('off'); page.setMode('live'); }
  });
  $('mosaic-sec').addEventListener('input', () => {
    $('mosaic-sec-label').textContent = `${$('mosaic-sec').value}s`;
  });
  $('mosaic-sec').addEventListener('change', () => {
    setting('mosaicChangeSec', Math.max(3, Math.min(60, Number($('mosaic-sec').value) || 10)));
    updateOptionsUi();
  });
  // Drag = hear it now (local only); release = persist. Mirrors the opacity slider's
  // split, minus the throttle: setting .volume on a handful of elements is free, so the
  // live half needs no host round trip at all.
  $('feed-volume').addEventListener('input', () => {
    setVolume(Number($('feed-volume').value), false);
  });
  $('feed-volume').addEventListener('change', () => {
    setVolume(Number($('feed-volume').value), true);
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
        // While the intro gate is up there is no feed to back out of, so Esc means
        // "I did not want this window" - the host closes it (same path as the ✕).
        if (!started) { post({ type: 'close' }); e.preventDefault(); break; }
        // Only meaningful while the window still holds keyboard focus (right
        // after flipping the toggle — exactly when someone wants out). Native
        // fullscreen-exit on Esc still happens; that's fine, both say "back off".
        if (clickThrough) setClickThrough(false, false);
        break;
      default: break;
    }
  });

  window.addEventListener('resize', () => applyTrack(false));

  // Minimized/hidden: tear every mounted page down (reports dwell, frees
  // decoders — previews included); the window is restored on return. The
  // re-snap covers a drag whose release was lost to the hide.
  document.addEventListener('visibilitychange', () => {
    if (!feed) return;
    applyWindow();
    if (!document.hidden) applyTrack(false);
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
      settings.volume = clampVolume(settings.volume);
      settings.windowOpacity = clampOpacity(settings.windowOpacity);
      onlineCatalog = Array.isArray(data.online?.niches) ? data.online.niches : [];
      customSubs = normalizeLibrary(data.online?.library, data.online?.customSubs);
      pendingSubs = [];
      // Consent is the source of truth for whether a non-library source can stand.
      if (!settings.onlineConsented && settings.source !== 'library') settings.source = 'library';
      clickThrough = false; // never sticky across a reload
      document.body.classList.remove('clickthrough');
      stats.init(data.stats, (s) => post({ type: 'stats-save', stats: s }));
      feed = createFeed(aspect);
      if (!booted) {
        booted = true;
        wireIntro();
        wireChrome();
        wireInput();
      }
      updateOptionsUi();
      updateChromeForLayout();
      $('btn-mute').textContent = settings.muted ? '🔇' : '🔊';
      $('btn-mute').classList.toggle('off', settings.muted);
      $('btn-advance').classList.toggle('off', !settings.autoAdvance);
      // Everything above is chrome state - cheap, silent, no media. The feed itself waits
      // behind the intro card; only a re-init after the gate is already open rebuilds now.
      if (started) {
        applyConfig();
        $('boot').classList.add('hidden');
        scheduleAttention();
      } else {
        showIntro();
      }
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
    case 'assets-append': {
      // A batch of online entries from the host. Dedupe, grow the pool in place
      // (feed.append never resets), and if the feed was empty-waiting (online mode
      // booting), mount the first pages that append just built.
      remoteReqInFlight = false;
      const list = Array.isArray(data.assets) ? data.assets : [];
      const known = new Set(remoteAssets.map((a) => a.id));
      const fresh = list.filter((a) => a && a.id && !known.has(a.id));
      remoteAssets.push(...fresh);
      if (started && feed && sourceUsesRemote() && fresh.length) {
        const wasEmpty = feed.comps.length === 0;
        feed.append(fresh);
        if (wasEmpty && feed.comps.length > 0) {
          activeIdx = 0;
          syncPages(); // applies the live/preview window around page 0
          applyTrack(false);
          updateCaption();
        }
        updateEmptyState();
      }
      updateOnlineUi();
      ensureRemoteBuffer();
      break;
    }
    case 'online-status': {
      // Arrives after every batch attempt, success or not — this is what clears the
      // in-flight latch, flips the offline notice, and paces the dry-channel backoff.
      remoteReqInFlight = false;
      onlineOk = !!data.ok;
      // `fresh` is the honest count of ids we had never seen. `added` used to be the
      // raw batch size, which a drained channel still fills with re-served ids — that
      // is exactly why the backoff below never armed and the page polled every ~1.1 s.
      const fresh = Number.isFinite(data.fresh) ? Number(data.fresh) : Number(data.added) || 0;
      const now = performance.now();
      if (!data.ok) {
        remoteDryUntil = now + REMOTE_OFFLINE_BACKOFF_MS; // transport, not exhaustion
      } else if (fresh > 0) {
        remoteDryStep = 0; // the well refilled: forget the ladder entirely
        remoteDryUntil = 0;
      } else {
        const rung = Math.min(remoteDryStep, REMOTE_DRY_LADDER_MS.length - 1);
        remoteDryUntil = now + REMOTE_DRY_LADDER_MS[rung];
        remoteDryStep++;
      }
      // `dry` is the host's own verdict (every channel exhausted or cooling); the
      // second clause covers a host that predates it.
      remoteExhausted = data.dry === true || (!!data.ok && fresh === 0 && remoteAssets.length > 0);
      if (remoteExhausted) noteExhaustion();
      updateEmptyState();
      updateOnlineUi();
      if (data.ok && fresh > 0) ensureRemoteBuffer();
      break;
    }
    case 'sub-probe':
      onSubProbe(data);
      break;
    case 'library':
      // The host pushes the whole library after any change to it (a probe elsewhere in the
      // app, an X here or in the Assets tab, a restored settings file). Replace, never
      // merge: the host is the owner and a half-applied diff is how two lists drift.
      customSubs = normalizeCustomSubs(data.library);
      pendingSubs = pendingSubs.filter((p) => !customSubs.some((s) => sameSub(s.name, p)));
      updateOnlineUi();
      pruneRemoteToChannels();
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
    get remoteCount() { return remoteAssets.length; },
    get remoteSeen() { return seenRemote.size; },
    get remoteExhausted() { return remoteExhausted; },
    get remoteDryStep() { return remoteDryStep; },
    get source() { return settings.source; },
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
