// Tile surfaces + pages — the DOM/media layer under feed.js's data model.
//
// Rules carried over from mobile (they are what keeps this smooth):
//  - Pages live in three states now (ported back from the browser FYP, which
//    solved the black-flash-on-advance first): LIVE (playing, audible),
//    PREVIEW (media mounted but paused on its first window frame — what a
//    half-swipe peeks at, and what a committed page resumes from with no
//    black gap), OFF (torn down, decoders freed). Only the active page is
//    live; its ±1 neighbours hold preview; everything further is off.
//  - Dwell = wall-clock while the surface is live AND playing, accumulated
//    across stalls; reported exactly once on teardown. A preview never plays,
//    so it banks nothing — promotion to live is when the clock starts.
//  - The played window must match the scored segId (grid cuts seek to their
//    grid start; cold cuts score the section actually played).
//  - Unmount clears src + load() so the decoder frees now, not at GC time.

import { WHOLE_VIDEO_MAX_SEC, segId, segmentIndexForStart, startForSegment } from './segments.js';

const SWAP_MS = 260;          // cross-slide duration on a committed swap
const DRAG_RESISTANCE = 0.7;  // finger-follow while dragging
const COMMIT_DIST = 64;       // px to commit a swap
const COMMIT_VELOCITY = 0.8;  // px/ms
// Movement below this never locks a drag axis — the release is a tap. The
// first travel past it picks the axis: 'x' trades this tile (the drag that
// was always here), 'y' hands the gesture to the pager so the whole track
// follows the pointer (browser-FYP parity).
const DRAG_LOCK_PX = 8;
const VELOCITY_WINDOW_MS = 120; // release velocity reads the last ~120ms only
// Resistance on a trade drag toward a side with nothing to offer (pool too
// small) — the same give as the pager's feed-edge rubber-band, same reason.
const DRAG_RUBBER = 0.35;
const ERROR_ADVANCE_MS = 1200; // never machine-gun a broken pack
const MAX_ERROR_SWAPS = 3;     // per mounted slot; then the visible fail state stays
const MORPH_FADE_OUT_MS = 220;
const MORPH_FADE_IN_MS = 260;

/** Visible failure state (#562): a tile that can't decode used to sit silent black —
 *  indistinguishable from "still loading" — forever. pointer-events stays off so the
 *  drag/chevron gestures keep working over it. */
function markFailed(el, cut) {
  if (el.querySelector('.media-fail')) return;
  const note = document.createElement('div');
  note.className = 'media-fail';
  note.textContent = '⚠ ' + (cut.asset.filename || 'clip') + " couldn't load (unsupported format?)";
  el.appendChild(note);
}

/** Release velocity over the last ~VELOCITY_WINDOW_MS of samples, px/ms. */
function dragVelocity(samples, k) {
  const last = samples[samples.length - 1];
  if (!last) return 0;
  let first = samples[0];
  for (const p of samples) {
    if (last.t - p.t <= VELOCITY_WINDOW_MS) { first = p; break; }
  }
  const dt = last.t - first.t;
  if (dt <= 0) return 0;
  return (last[k] - first[k]) / dt;
}

/** Media elements throw on an out-of-range volume, so every path funnels through this. */
function clampVolume(v) {
  const n = Number(v);
  if (!Number.isFinite(n)) return 1;
  return Math.min(1, Math.max(0, n));
}

/**
 * One media surface for a cut. Returns { el, stop(), setMuted(), setVolume(), setAutoAdvance(),
 * setPreview(), failed }. ctx: { onEnded, onError, onAssetMeta,
 * report(segId, dwellMs, clipLenMs), advances, preview }. Born with ctx.preview
 * it loads + seeks but never plays: a paused element showing its window's first
 * frame, ready to resume the instant the page goes live.
 */
function createVideoSurface(cut, ctx) {
  const el = document.createElement('div');
  el.className = 'surface';
  const video = document.createElement('video');
  video.src = cut.asset.url;
  video.preload = 'auto';
  video.playsInline = true;
  video.disablePictureInPicture = true;
  video.muted = true; // page audio routing unmutes the one audible tile
  // Loudness is a page-wide setting, not a per-clip one. Seed it here as well as in
  // applyAudio so a surface is never briefly full-volume between mount and routing.
  video.volume = clampVolume(ctx.volume?.());
  // Loop only matters at a natural end (whole-video cuts; windowed cuts are
  // steered by timeupdate before they ever get there).
  video.loop = !(ctx.autoAdvance && ctx.advances);
  video.style.objectFit = cut.fit;
  // Remote entries carry a still; local files don't need one (the paused
  // first frame IS the poster once metadata lands). Never black either way.
  if (cut.asset.posterUrl) video.poster = cut.asset.posterUrl;
  el.appendChild(video);

  let cutStart = null;   // seconds; null until metadata
  let windowLen = null;  // null = whole-video cut
  let finished = false;
  let previewing = !!ctx.preview;
  let autoAdvance = ctx.autoAdvance;
  // Retention attribution (set at metadata; cold cuts score what actually played).
  let scoreSegId = null;
  let clipLenSec = cut.lenSec;
  // Dwell bookkeeping.
  let dwellAccum = 0;
  let watchStart = null;
  let reported = false;
  let metaSent = false;
  let errTimer = null;

  const openDwell = () => { if (watchStart == null) watchStart = performance.now(); };
  const closeDwell = () => {
    if (watchStart != null) { dwellAccum += performance.now() - watchStart; watchStart = null; }
  };

  video.addEventListener('playing', openDwell);
  video.addEventListener('waiting', closeDwell);
  video.addEventListener('pause', () => {
    closeDwell();
    // There is no user-facing pause on the reel, so an unrequested pause while
    // the surface is live is an interruption — resume it (mobile parity). The
    // `reported` guard keeps teardown's own pause from re-kicking playback,
    // and `previewing` keeps a demoted page frozen on its frame.
    if (!reported && !finished && !previewing && !document.hidden && video.error == null) {
      video.play().catch(() => { /* teardown race */ });
    }
  });

  video.addEventListener('loadedmetadata', () => {
    const d = video.duration;
    if (!Number.isFinite(d) || d <= 0) return;
    if (cutStart == null) {
      const len = cut.lenSec;
      const gridClip = cut.durKnown && d > WHOLE_VIDEO_MAX_SEC;
      // durKnown short videos play whole; cold cuts fall back to the seed window
      // now that the duration is known.
      const windowed = cut.durKnown ? gridClip : d > len + 10;
      const start = gridClip
        ? startForSegment(cut.k, d, len)
        : windowed ? cut.seed * (d - len) : 0;
      cutStart = start;
      windowLen = windowed ? len : null;
      if (start > 0) { try { video.currentTime = start; } catch { /* not seekable yet */ } }
      scoreSegId = cut.durKnown ? cut.segId : segId(cut.asset.id, segmentIndexForStart(start));
      clipLenSec = windowed ? len : d; // whole-video normalizes on real duration
    }
    // Backfill duration + dims so future sessions build the grid up front.
    if (!metaSent && (cut.asset.durationMs == null || cut.asset.width == null)
        && video.videoWidth > 0 && video.videoHeight > 0) {
      metaSent = true;
      ctx.onAssetMeta(cut.asset, {
        durationMs: Math.round(d * 1000),
        width: video.videoWidth,
        height: video.videoHeight,
      });
    }
  });

  video.addEventListener('timeupdate', () => {
    if (cutStart == null || windowLen == null) return;
    if (video.currentTime >= cutStart + windowLen) {
      if (autoAdvance && ctx.advances) advance();
      else { try { video.currentTime = cutStart; } catch { /* ignore */ } }
    }
  });

  video.addEventListener('ended', () => {
    if (autoAdvance && ctx.advances) advance();
  });

  video.addEventListener('error', () => {
    // A corrupt file never reaches timeupdate; without this an auto-advancing
    // feed parks on a black tile forever. Delay so a broken pack pages <=1/s.
    closeDwell();
    markFailed(el, cut);
    if (autoAdvance && ctx.advances && errTimer == null) {
      errTimer = setTimeout(() => { errTimer = null; advance(); }, ERROR_ADVANCE_MS);
    }
    // MediaError code travels to the host log: 3 = decode failed, 4 = format not
    // supported (the HEVC signature Chromium can't play while LibVLC can).
    ctx.onError?.(cut, { code: video.error?.code ?? 0, message: video.error?.message || '' });
  });

  function advance() {
    if (finished) return;
    finished = true;
    ctx.onEnded();
  }

  // A preview only buffers + seeks; playback (and dwell) starts on promotion.
  if (!previewing) video.play().catch(() => { /* autoplay policy is disabled via browser args */ });

  return {
    el,
    cut,
    /** A slot promoting a preview checks this to self-heal a dead file (#562). */
    get failed() { return video.error != null; },
    setMuted(m) { video.muted = m; },
    setVolume(v) { video.volume = clampVolume(v); },
    setAutoAdvance(on) {
      autoAdvance = on;
      video.loop = !(on && ctx.advances);
    },
    /** live ⇄ preview without remounting: pause holds the frame (and the warm
     *  buffer), play resumes exactly where the window left off. */
    setPreview(on) {
      if (on === previewing) return;
      previewing = on;
      if (on) {
        closeDwell();
        video.muted = true; // a peeked page must never talk over the live one
        try { video.pause(); } catch { /* ignore */ }
      } else {
        video.play().catch(() => { /* teardown race */ });
      }
    },
    /** Teardown: report dwell exactly once, then free the decoder NOW. */
    stop() {
      if (errTimer != null) { clearTimeout(errTimer); errTimer = null; }
      closeDwell();
      if (!reported) {
        reported = true;
        if (scoreSegId && dwellAccum > 0) ctx.report(scoreSegId, dwellAccum, clipLenSec * 1000);
      }
      try { video.pause(); } catch { /* ignore */ }
      video.removeAttribute('src');
      try { video.load(); } catch { /* ignore */ }
      el.remove();
    },
  };
}

/** GIF sibling: a GIF cannot pause, so preview only changes the accounting —
 *  dwell is wall-clock while LIVE (not since mount), auto-advance is a timer
 *  armed only while live. */
function createGifSurface(cut, ctx) {
  const el = document.createElement('div');
  el.className = 'surface';
  const img = document.createElement('img');
  img.src = cut.asset.url;
  img.draggable = false;
  img.style.objectFit = cut.fit;
  el.appendChild(img);

  let previewing = !!ctx.preview;
  let dwellAccum = 0;
  let watchStart = previewing ? null : performance.now();
  let reported = false;
  let finished = false;
  let failed = false;
  let advTimer = null;
  let metaSent = false;
  let autoAdvance = ctx.autoAdvance;

  const closeDwell = () => {
    if (watchStart != null) { dwellAccum += performance.now() - watchStart; watchStart = null; }
  };

  img.addEventListener('load', () => {
    if (!metaSent && cut.asset.width == null && img.naturalWidth > 0 && img.naturalHeight > 0) {
      metaSent = true;
      ctx.onAssetMeta(cut.asset, { width: img.naturalWidth, height: img.naturalHeight });
    }
  });

  // GIFs had NO error listener at all — a broken image sat as the bare browser
  // glyph with nothing reported anywhere (#562).
  img.addEventListener('error', () => {
    failed = true;
    markFailed(el, cut);
    ctx.onError?.(cut, { code: 0, message: 'image failed to load' });
  });

  function arm(on) {
    if (advTimer != null) { clearTimeout(advTimer); advTimer = null; }
    if (on && !previewing && ctx.advances) {
      advTimer = setTimeout(() => {
        if (finished) return;
        finished = true;
        ctx.onEnded();
      }, cut.lenSec * 1000);
    }
  }
  arm(autoAdvance);

  return {
    el,
    cut,
    get failed() { return failed; },
    setMuted() { /* GIFs are silent */ },
    setVolume() { /* GIFs are silent */ },
    setAutoAdvance(on) { autoAdvance = on; arm(on); },
    setPreview(on) {
      if (on === previewing) return;
      previewing = on;
      if (on) { closeDwell(); arm(false); }
      else { watchStart = performance.now(); arm(autoAdvance); }
    },
    stop() {
      arm(false);
      if (!reported) {
        reported = true;
        closeDwell();
        if (dwellAccum > 0) ctx.report(segId(cut.asset.id, 0), dwellAccum, cut.lenSec * 1000);
      }
      img.removeAttribute('src');
      el.remove();
    },
  };
}

function createSurface(cut, ctx) {
  return cut.asset.type === 'gif' ? createGifSurface(cut, ctx) : createVideoSurface(cut, ctx);
}

/**
 * One tile slot: rect positioning, the live surface, the horizontal swap gesture
 * (drag left = fresh pick, right = history-back) with a TikTok cross-slide, hover
 * chevrons, click-to-focus-audio, and the stack blur cue.
 */
function createTileSlot(page, tileIndex, ctx) {
  const slot = document.createElement('div');
  slot.className = 'tile';
  const clip = document.createElement('div');
  clip.className = 'swap-clip';
  slot.appendChild(clip);

  // Audible-tile cue. A shadow set on .tile itself would be painted over by the
  // <video>/<img>, so the ring lives on its own overlay above the surface.
  const ring = document.createElement('div');
  ring.className = 'audio-ring';
  slot.appendChild(ring);

  const chevL = document.createElement('button');
  chevL.className = 'chev chev-l';
  chevL.textContent = '‹';
  chevL.title = 'Back (replay the previous clip)';
  const chevR = document.createElement('button');
  chevR.className = 'chev chev-r';
  chevR.textContent = '›';
  chevR.title = 'Swap this tile for a fresh clip';
  slot.append(chevL, chevR);

  let surface = null;   // settled live surface (null while page inactive)
  let sliding = false;
  let tile = null;      // current tile model (rect/audio/blur/fit)
  let mountGen = 0;     // bumps on mount/unmount so a mid-slide teardown can't
                        // resurrect a surface on a dead slot (morph vs. slide race)
  let errorSwaps = 0;   // capped self-heal swaps for this mount (see scheduleErrorSwap)
  let errSwapTimer = null;

  /** A failed tile swaps itself for a fresh pick instead of sitting dead (#562).
   *  Under auto-advance the advancing tile already pages away on its own error
   *  timer; every other case (manual browsing — the DEFAULT, autoAdvance is off —
   *  and non-advancing tiles) self-heals here. Capped so a mostly-broken library
   *  settles into visible fail states rather than swap-looping. Live pages only:
   *  a preview that failed waits for promotion (setPreview re-checks then) —
   *  swapping an off-screen page's tiles would churn the pool for nobody. */
  function scheduleErrorSwap() {
    if (!ctx.isLive()) return;
    if (ctx.isAutoAdvance() && tile?.advances) return;
    if (errorSwaps >= MAX_ERROR_SWAPS || errSwapTimer != null) return;
    errSwapTimer = setTimeout(() => {
      errSwapTimer = null;
      if (!surface || sliding || !tile) return;
      errorSwaps++;
      commit('next');
    }, ERROR_ADVANCE_MS);
  }

  function clearErrorSwap() {
    if (errSwapTimer != null) { clearTimeout(errSwapTimer); errSwapTimer = null; }
  }

  function applyRect(t) {
    slot.style.left = (t.rect.x * 100) + '%';
    slot.style.top = (t.rect.y * 100) + '%';
    slot.style.width = (t.rect.w * 100) + '%';
    slot.style.height = (t.rect.h * 100) + '%';
    slot.classList.toggle('blurred', !!t.blurred);
  }

  function surfaceCtx(t, preview, peek) {
    return {
      advances: t.advances,
      autoAdvance: ctx.isAutoAdvance(),
      preview: !!preview,
      onEnded: () => ctx.onAdvance(),
      onError: (cut, detail) => {
        // The host learns about every broken file (#562 strikes), but only the
        // LIVE occupant self-heals — a broken PEEK must not swap the healthy
        // tile it is merely riding beside (slideFromPeek re-checks on promote).
        ctx.onMediaError?.(cut.asset, detail);
        if (!peek) scheduleErrorSwap();
      },
      onAssetMeta: ctx.onAssetMeta,
      report: ctx.report,
      volume: ctx.volume,
    };
  }

  // ---- trade peek (the drag strip) ----
  // What a horizontal drag shows before it commits: the candidate each
  // direction would deal, mounted as preview surfaces (paused first frame,
  // muted, zero dwell) parked exactly one tile-width to each side. The feed
  // caches the candidates per tile (pinned to the cut's surfaceKey), so the
  // elements here survive a spring-back and the next drag shows the same
  // pictures; a commit promotes the peeked element in place — the user lands
  // on the very frame they were looking at.
  let peekEls = null; // { forKey, next: { cut, surf }|null, back: { cut, surf }|null }

  function clearPeekEls() {
    if (!peekEls) return;
    peekEls.next?.surf.stop();  // zero dwell — reports nothing
    peekEls.back?.surf.stop();
    peekEls = null;
  }

  /** Resolve + mount both candidates (idempotent; cheap after the first call). */
  function ensurePeekEls() {
    if (!tile || !surface) return;
    if (peekEls && peekEls.forKey !== tile.cut.surfaceKey) clearPeekEls();
    if (!peekEls) peekEls = { forKey: tile.cut.surfaceKey, next: null, back: null };
    for (const dir of ['next', 'back']) {
      const cut = ctx.onPeek(tileIndex, dir);
      const held = peekEls[dir];
      if (held && (!cut || held.cut.surfaceKey !== cut.surfaceKey)) {
        held.surf.stop();
        peekEls[dir] = null;
      }
      if (cut && !peekEls[dir]) {
        const surf = createSurface({ ...cut, fit: tile.fit }, surfaceCtx(tile, true, true));
        surf.el.style.transform = `translateX(${dir === 'next' ? '' : '-'}100%)`;
        clip.insertBefore(surf.el, clip.firstChild); // under the live surface
        peekEls[dir] = { cut, surf };
      }
    }
  }

  function mount(t, preview) {
    mountGen++;
    tile = t;
    errorSwaps = 0;
    clearErrorSwap();
    clearPeekEls(); // pinned to the old cut; a remount means someone new
    applyRect(t);
    if (surface) { surface.stop(); surface = null; }
    const s = createSurface({ ...t.cut, fit: t.fit }, surfaceCtx(t, preview));
    s.el.style.transform = 'translateX(0)';
    clip.appendChild(s.el);
    surface = s;
    ctx.applyAudio();
  }

  function unmount() {
    mountGen++;
    clearErrorSwap();
    clearPeekEls();
    if (surface) { surface.stop(); surface = null; }
    tile = null;
  }

  /** Commit a swap: cross-slide old out, new in; outgoing keeps playing. */
  function slideTo(cut, dir) {
    const t = tile;
    if (!t || !surface) return;
    const gen = mountGen;
    const w = slot.clientWidth || 1;
    const outgoing = surface;
    outgoing.setMuted(true); // the incoming tile carries audio from now on
    tile = { ...t, cut };
    const incoming = createSurface({ ...cut, fit: t.fit }, surfaceCtx(tile));
    incoming.el.style.transition = 'none';
    incoming.el.style.transform = `translateX(${dir === 'next' ? w : -w}px)`;
    clip.appendChild(incoming.el);
    sliding = true;
    // Two frames: let the incoming's start transform land before animating.
    requestAnimationFrame(() => requestAnimationFrame(() => {
      outgoing.el.style.transition = `transform ${SWAP_MS}ms ease-out`;
      incoming.el.style.transition = `transform ${SWAP_MS}ms ease-out`;
      outgoing.el.style.transform = `translateX(${dir === 'next' ? -w : w}px)`;
      incoming.el.style.transform = 'translateX(0)';
    }));
    // transitionend is lossy under load — settle on a timer instead.
    setTimeout(() => {
      outgoing.stop(); // reports the outgoing cut's dwell
      sliding = false;
      if (gen !== mountGen) { incoming.stop(); return; } // slot re-mounted mid-slide
      incoming.el.style.transition = '';
      surface = incoming;
      ctx.applyAudio();
    }, SWAP_MS + 40);
  }

  /**
   * Commit a swap whose incoming is ALREADY on the strip: the peeked preview
   * slides the rest of the way in and is promoted in place (play() on its
   * warm, already-seeked element) — the user lands on the exact frame the
   * drag was showing. The sibling of slideTo, for the peeked path.
   */
  function slideFromPeek(cut, dir, held) {
    const t = tile;
    if (!t || !surface) return;
    const gen = mountGen;
    const w = slot.clientWidth || 1;
    const outgoing = surface;
    outgoing.setMuted(true); // the incoming tile carries audio from now on
    tile = { ...t, cut };
    const incoming = held.surf;
    peekEls[dir] = null; // detach before clearing — the promotee must survive
    clearPeekEls();      // the other side was pinned to the old cut
    incoming.setPreview(false); // plays while it rides in (slideTo parity)
    sliding = true;
    // Two frames: the strip position is inline already; let style flushes land.
    requestAnimationFrame(() => requestAnimationFrame(() => {
      outgoing.el.style.transition = `transform ${SWAP_MS}ms ease-out`;
      incoming.el.style.transition = `transform ${SWAP_MS}ms ease-out`;
      outgoing.el.style.transform = `translateX(${dir === 'next' ? -w : w}px)`;
      incoming.el.style.transform = 'translateX(0)';
    }));
    // transitionend is lossy under load — settle on a timer instead.
    setTimeout(() => {
      outgoing.stop(); // reports the outgoing cut's dwell
      sliding = false;
      if (gen !== mountGen) { incoming.stop(); return; } // slot re-mounted mid-slide
      incoming.el.style.transition = '';
      surface = incoming;
      // The peek was born with the flags of its resolve moment; settle on now.
      surface.setAutoAdvance(ctx.isAutoAdvance() && !!tile.advances);
      if (surface.failed) scheduleErrorSwap(); // promoted a dead file (#562)
      ctx.applyAudio();
    }, SWAP_MS + 40);
  }

  function springBack() {
    if (!surface) return;
    // The whole strip goes home together: live surface to 0, peeks back to
    // their parked edges (they stay mounted — the next drag shows the same
    // pictures without a resolve).
    const parts = [[surface.el, 'translateX(0)']];
    if (peekEls?.next) parts.push([peekEls.next.surf.el, 'translateX(100%)']);
    if (peekEls?.back) parts.push([peekEls.back.surf.el, 'translateX(-100%)']);
    for (const [pEl, tf] of parts) {
      pEl.style.transition = 'transform 200ms ease-out';
      pEl.style.transform = tf;
    }
    setTimeout(() => { for (const [pEl] of parts) pEl.style.transition = ''; }, 240);
  }

  function commit(dir) {
    const cut = ctx.onSwap(tileIndex, dir);
    if (!cut) { springBack(); return; }
    // The feed installs exactly what it peeked, so a held strip element with
    // the same identity IS the incoming surface; anything else (chevron or
    // eye-blink with no prior drag, stale pin) takes the classic cross-slide.
    const held = peekEls?.[dir];
    if (held && held.cut.surfaceKey === cut.surfaceKey) {
      slideFromPeek(cut, dir, held);
    } else {
      clearPeekEls();
      slideTo(cut, dir);
    }
  }

  // Pointer drag. The first DRAG_LOCK_PX of travel lock an axis: 'x' is the
  // horizontal tile trade that was always here; 'y' hands the gesture to the
  // pager (via ctx.onPageDragMove/End) so the whole track follows the pointer
  // — half-swipe peeks the neighbour's preview, release commits or springs
  // back (browser-FYP parity). A gesture that never locks is a tap.
  let dragId = null;
  let dragAxis = null;   // null until locked: 'x' | 'y'
  let dragStartX = 0;
  let dragStartY = 0;
  let lastX = 0;
  let lastT = 0;
  let velocity = 0;      // instantaneous, for the 'x' trade (unchanged feel)
  let samples = [];      // windowed, for the 'y' release (flick detection)
  slot.addEventListener('pointerdown', (e) => {
    // A press on a chevron must never start a drag: setPointerCapture below
    // retargets the whole gesture (including the compatibility `click`) to the
    // slot, which is exactly what used to swallow the chevron's own click.
    if (e.target.closest?.('.chev')) return;
    if (e.button !== 0 || sliding || !surface) return;
    dragId = e.pointerId;
    dragAxis = null;
    dragStartX = lastX = e.clientX;
    dragStartY = e.clientY;
    lastT = performance.now();
    velocity = 0;
    samples = [{ t: lastT, x: e.clientX, y: e.clientY }];
    slot.setPointerCapture(e.pointerId);
  });
  slot.addEventListener('pointermove', (e) => {
    if (dragId !== e.pointerId || !surface) return;
    // A mouse release can slip past pointer capture (a native drag or a focus
    // steal swallows it) and plain hover moves would then steer the drag
    // forever. No button held means the drag is over: settle with what we have.
    if (dragAxis != null && e.pointerType !== 'touch' && e.buttons === 0) { endDrag(e); return; }
    const now = performance.now();
    const dt = now - lastT;
    if (dt > 0) velocity = (e.clientX - lastX) / dt;
    lastX = e.clientX;
    lastT = now;
    samples.push({ t: now, x: e.clientX, y: e.clientY });
    if (samples.length > 8) samples.shift();
    const dx = e.clientX - dragStartX;
    const dy = e.clientY - dragStartY;
    if (dragAxis == null) {
      if (Math.max(Math.abs(dx), Math.abs(dy)) < DRAG_LOCK_PX) return;
      // a page slide is still animating: let it land, keep sampling
      if (ctx.isPagerBusy?.()) return;
      dragAxis = Math.abs(dx) > Math.abs(dy) ? 'x' : 'y';
    }
    if (dragAxis === 'x') {
      // The strip: live surface + both peeked candidates ride the drag as one
      // piece, so the trade is visible BEFORE it commits and reversible until
      // release. A side with no candidate rubber-bands instead of revealing
      // the tile's backing.
      ensurePeekEls();
      const offer = dx < 0 ? peekEls?.next : peekEls?.back;
      const eff = offer ? dx * DRAG_RESISTANCE : dx * DRAG_RUBBER;
      surface.el.style.transition = 'none';
      surface.el.style.transform = `translateX(${eff}px)`;
      if (peekEls?.next) {
        peekEls.next.surf.el.style.transition = 'none';
        peekEls.next.surf.el.style.transform = `translateX(calc(100% + ${eff}px))`;
      }
      if (peekEls?.back) {
        peekEls.back.surf.el.style.transition = 'none';
        peekEls.back.surf.el.style.transform = `translateX(calc(-100% + ${eff}px))`;
      }
    } else {
      ctx.onPageDragMove(dy);
    }
  });
  const endDrag = (e) => {
    if (dragId !== e.pointerId) return;
    dragId = null;
    const axis = dragAxis;
    dragAxis = null;
    if (axis === 'y') {
      ctx.onPageDragEnd(e.clientY - dragStartY, dragVelocity(samples, 'y'));
      return;
    }
    if (!surface) return;
    surface.el.style.transition = '';
    const dx = e.clientX - dragStartX;
    const dy = e.clientY - dragStartY;
    if (axis === 'x' && (Math.abs(dx) > COMMIT_DIST || Math.abs(velocity) > COMMIT_VELOCITY)) {
      commit(dx < 0 || (Math.abs(dx) <= COMMIT_DIST && velocity < 0) ? 'next' : 'back');
    } else if (Math.abs(dx) < 10 && Math.abs(dy) < 10) {
      springBack();
      ctx.onTapAudio(tileIndex); // a tap means "let me hear this one"
    } else {
      springBack();
    }
  };
  slot.addEventListener('pointerup', endDrag);
  slot.addEventListener('pointercancel', (e) => {
    if (dragId !== e.pointerId) return;
    dragId = null;
    const axis = dragAxis;
    dragAxis = null;
    if (axis === 'y') ctx.onPageDragEnd(0, 0); // 0 travel: pager springs back
    else springBack();
  });

  // Chevrons drive the same commit path as the drag: ‹ = history-back (what a
  // rightward drag does), › = fresh pick (what a leftward drag does).
  function wireChev(btn, dir) {
    // Stop the press from reaching the slot at all (belt and braces alongside
    // the .chev bail above) so no pointer capture is ever taken for it.
    btn.addEventListener('pointerdown', (e) => { e.stopPropagation(); });
    btn.addEventListener('click', (e) => {
      e.stopPropagation();
      e.preventDefault();
      if (sliding || !surface) return;
      commit(dir);
    });
  }
  wireChev(chevL, 'back');
  wireChev(chevR, 'next');

  return {
    el: slot,
    get tile() { return tile; },
    get surface() { return surface; },
    get sliding() { return sliding; },
    /** A pointer is mid-gesture on this slot. The morph checks it: removing a
     *  slot that holds pointer capture silently kills the rest of the drag. */
    get dragging() { return dragId != null; },
    mount,
    unmount,
    applyRect,
    /** live ⇄ preview in place. Promotion re-checks a preview that failed
     *  while off-screen so the self-heal swap (#562) still happens. */
    setPreview(on) {
      surface?.setPreview(on);
      if (!on && surface?.failed) scheduleErrorSwap();
    },
    /** Eye control: the exact chevron path (same guard, same cross-slide).
     *  Returns false when the slot could not act (mid-slide / not mounted). */
    commitSwap(dir) {
      if (sliding || !surface) return false;
      commit(dir);
      return true;
    },
    /** Audible-tile ring (page decides; honours the audioGlow setting + mute). */
    setAudioGlow(on) { slot.classList.toggle('audio-glow', !!on); },
  };
}

/**
 * One feed page. Created for every composition in the window; media mounts in
 * 'preview' (paused first frame — the pager's neighbours) or 'live' (playing).
 * ctx:
 *   { onAdvance(compKey), onSwap(compKey, i, dir), onPeek(compKey, i, dir),
 *     onTapAudio(compKey, i), onAssetMeta, report, isAutoAdvance(), isMuted(),
 *     volume() -> 0..1,
 *     audioGlow() -> bool, audioFocus() -> index|null, isPagerBusy() -> bool,
 *     onPageDragMove(compKey, dy), onPageDragEnd(compKey, dy, vy) }
 */
export function createPage(comp, ctx) {
  const el = document.createElement('div');
  el.className = 'page';
  const inner = document.createElement('div');
  inner.className = 'page-inner';
  el.appendChild(inner);

  let mode = 'off';      // 'off' | 'preview' | 'live'
  let slots = [];
  let morphTimer = null;
  let morphing = false;  // a morph's fade-out is in flight

  function audioIndex() {
    const focus = ctx.audioFocus(comp.key);
    if (focus != null && comp.tiles[focus]?.cut.asset.type === 'video') return focus;
    return comp.tiles.findIndex((t) => t.audio);
  }

  function applyAudio() {
    // Only the LIVE page ever speaks: previews are all-muted, no glow, so a
    // half-swipe peek can never talk over the clip actually playing.
    const ai = mode === 'live' ? audioIndex() : -1;
    const muted = ctx.isMuted();
    // Nothing is audible while muted (or when no tile carries audio at all) —
    // in that case no tile glows either.
    const glow = !muted && ai >= 0 && ctx.audioGlow?.() !== false;
    const vol = clampVolume(ctx.volume?.() ?? 1);
    slots.forEach((s, i) => {
      s.surface?.setMuted(muted || i !== ai);
      s.surface?.setVolume?.(vol);
      s.setAudioGlow(glow && i === ai);
    });
  }

  function slotCtx(i) {
    return {
      isAutoAdvance: ctx.isAutoAdvance,
      isLive: () => mode === 'live',
      isPagerBusy: ctx.isPagerBusy,
      onAdvance: () => ctx.onAdvance(comp.key),
      onSwap: (idx, dir) => ctx.onSwap(comp.key, idx, dir),
      onPeek: (idx, dir) => ctx.onPeek(comp.key, idx, dir),
      onTapAudio: (idx) => { ctx.onTapAudio(comp.key, idx); applyAudio(); },
      onPageDragMove: (dy) => ctx.onPageDragMove(comp.key, dy),
      onPageDragEnd: (dy, vy) => ctx.onPageDragEnd(comp.key, dy, vy),
      onAssetMeta: ctx.onAssetMeta,
      onMediaError: ctx.onMediaError,
      report: ctx.report,
      volume: ctx.volume,
      applyAudio,
    };
  }

  function mountTiles(preview) {
    slots = comp.tiles.map((t, i) => {
      const slot = createTileSlot(el, i, slotCtx(i));
      inner.appendChild(slot.el);
      slot.mount(t, preview);
      return slot;
    });
    applyAudio();
  }

  function unmountTiles() {
    for (const s of slots) { s.unmount(); s.el.remove(); }
    slots = [];
  }

  /** The morph itself (fade out -> re-compose -> fade in), shared by the timer and
   *  by the eyes-closed gesture. Returns false when it could not run. */
  function runMorph() {
    if (mode !== 'live' || morphing || comp.layout !== 'random') return false;
    // A finger (or held mouse) on a slot: remounting would strip its pointer
    // capture and strand the drag mid-air. Try again on the next tick instead.
    if (slots.some((s) => s.dragging)) { scheduleMorph(); return false; }
    morphing = true;
    if (morphTimer != null) { clearTimeout(morphTimer); morphTimer = null; }
    inner.style.transition = `opacity ${MORPH_FADE_OUT_MS}ms ease-in`;
    inner.style.opacity = '0';
    setTimeout(() => {
      morphing = false;
      if (mode !== 'live') { inner.style.opacity = '1'; return; }
      const tiles = ctx.onMorph(comp.key); // feed re-composes in place
      if (tiles) { unmountTiles(); mountTiles(false); }
      inner.style.transition = `opacity ${MORPH_FADE_IN_MS}ms ease-out`;
      inner.style.opacity = '1';
      scheduleMorph();
    }, MORPH_FADE_OUT_MS);
    return true;
  }

  // Only clears/re-arms the timer — it must NOT touch opacity, or re-arming at the
  // tail of a morph would stomp the fade-in transition it was just given.
  function scheduleMorph() {
    if (morphTimer != null) { clearTimeout(morphTimer); morphTimer = null; }
    if (mode !== 'live' || comp.layout !== 'random' || !ctx.mosaicAutoChange()) return;
    morphTimer = setTimeout(runMorph, Math.max(3000, ctx.mosaicChangeMs()));
  }

  function cancelMorph() {
    if (morphTimer != null) { clearTimeout(morphTimer); morphTimer = null; }
    inner.style.opacity = '1';
    inner.style.transition = '';
  }

  return {
    el,
    key: comp.key,
    comp,
    get mode() { return mode; },
    get active() { return mode === 'live'; },

    // ---- eye control ----
    /** Tiles currently mounted (falls back to the model when inactive). */
    get tileCount() { return slots.length || comp.tiles.length; },
    /** True while a tile slide or a morph is in flight — do not act on a gesture. */
    get busy() { return morphing || slots.some((s) => s.sliding); },
    /**
     * Index of the tile under a client-space point, or -1. Uses the slots' own
     * laid-out rects, so it is right for every layout without duplicating the
     * band/mosaic geometry: duo/trio bands and mosaic cells are both just rects.
     */
    tileAtPoint(cx, cy) {
      for (let i = 0; i < slots.length; i++) {
        const r = slots[i].el.getBoundingClientRect();
        if (cx >= r.left && cx < r.right && cy >= r.top && cy < r.bottom) return i;
      }
      return -1;
    },
    /** Swap one tile through the exact chevron path (fresh pick + cross-slide). */
    swapTileAt(i) {
      return slots[i] ? slots[i].commitSwap('next') : false;
    },
    /** Re-compose the whole mosaic now, with the timer's fade choreography. */
    morphNow() { return runMorph(); },

    /**
     * The pager window drives this. Off→(preview|live) mounts media (paused or
     * playing); live⇄preview flips the SAME surfaces in place — pause holds the
     * frame and the buffer, play resumes it, and that in-place resume is what
     * killed the black flash. Only →off tears down (reporting dwell once).
     */
    setMode(next) {
      if (next === mode) return;
      const prev = mode;
      mode = next;
      if (next === 'off') { cancelMorph(); unmountTiles(); return; }
      if (prev === 'off') mountTiles(next === 'preview');
      else for (const s of slots) s.setPreview(next === 'preview');
      if (next === 'live') {
        // the toggle may have flipped while this page waited in preview
        for (const s of slots) s.surface?.setAutoAdvance(ctx.isAutoAdvance() && !!s.tile?.advances);
        scheduleMorph();
      } else {
        cancelMorph();
      }
      applyAudio();
    },
    applyAudio,
    /** Auto-advance flipped while live: re-arm the surfaces. */
    applyAutoAdvance() {
      const on = ctx.isAutoAdvance();
      slots.forEach((s) => s.surface?.setAutoAdvance(on && !!s.tile?.advances));
    },
    /** First tile's caption feeds the page chrome. */
    caption() {
      const t = comp.tiles[0];
      return t ? { filename: t.cut.asset.filename, folder: t.cut.asset.folder } : null;
    },
    destroy() {
      cancelMorph();
      unmountTiles();
      el.remove();
    },
  };
}
