// Tile surfaces + pages — the DOM/media layer under feed.js's data model.
//
// Rules carried over from mobile (they are what keeps this smooth):
//  - Only the ACTIVE page mounts live <video>/<img> elements; a deactivated page
//    tears its media down immediately (reporting dwell exactly once) and shows a
//    dark placeholder. Chromium then releases the decoders.
//  - Dwell = wall-clock while the surface is active AND playing, accumulated
//    across stalls; reported once on teardown.
//  - The played window must match the scored segId (grid cuts seek to their
//    grid start; cold cuts score the section actually played).
//  - Unmount clears src + load() so the decoder frees now, not at GC time.

import { WHOLE_VIDEO_MAX_SEC, segId, segmentIndexForStart, startForSegment } from './segments.js';

const SWAP_MS = 260;          // cross-slide duration on a committed swap
const DRAG_RESISTANCE = 0.7;  // finger-follow while dragging
const COMMIT_DIST = 64;       // px to commit a swap
const COMMIT_VELOCITY = 0.8;  // px/ms
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

/**
 * One live media surface for a cut. Returns { el, stop(), setMuted(), setAutoAdvance() }.
 * ctx: { onEnded, onError, onAssetMeta, report(segId, dwellMs, clipLenMs), advances }
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
  // Loop only matters at a natural end (whole-video cuts; windowed cuts are
  // steered by timeupdate before they ever get there).
  video.loop = !(ctx.autoAdvance && ctx.advances);
  video.style.objectFit = cut.fit;
  el.appendChild(video);

  let cutStart = null;   // seconds; null until metadata
  let windowLen = null;  // null = whole-video cut
  let finished = false;
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
    // `reported` guard keeps teardown's own pause from re-kicking playback.
    if (!reported && !finished && !document.hidden && video.error == null) {
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

  video.play().catch(() => { /* autoplay policy is disabled via browser args */ });

  return {
    el,
    cut,
    setMuted(m) { video.muted = m; },
    setAutoAdvance(on) {
      autoAdvance = on;
      video.loop = !(on && ctx.advances);
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

/** GIF sibling: always "playing", dwell is wall-clock, auto-advance is a timer. */
function createGifSurface(cut, ctx) {
  const el = document.createElement('div');
  el.className = 'surface';
  const img = document.createElement('img');
  img.src = cut.asset.url;
  img.draggable = false;
  img.style.objectFit = cut.fit;
  el.appendChild(img);

  const start = performance.now();
  let reported = false;
  let finished = false;
  let advTimer = null;
  let metaSent = false;

  img.addEventListener('load', () => {
    if (!metaSent && cut.asset.width == null && img.naturalWidth > 0 && img.naturalHeight > 0) {
      metaSent = true;
      ctx.onAssetMeta(cut.asset, { width: img.naturalWidth, height: img.naturalHeight });
    }
  });

  // GIFs had NO error listener at all — a broken image sat as the bare browser
  // glyph with nothing reported anywhere (#562).
  img.addEventListener('error', () => {
    markFailed(el, cut);
    ctx.onError?.(cut, { code: 0, message: 'image failed to load' });
  });

  function arm(on) {
    if (advTimer != null) { clearTimeout(advTimer); advTimer = null; }
    if (on && ctx.advances) {
      advTimer = setTimeout(() => {
        if (finished) return;
        finished = true;
        ctx.onEnded();
      }, cut.lenSec * 1000);
    }
  }
  arm(ctx.autoAdvance);

  return {
    el,
    cut,
    setMuted() { /* GIFs are silent */ },
    setAutoAdvance(on) { arm(on); },
    stop() {
      arm(false);
      if (!reported) {
        reported = true;
        const dwell = performance.now() - start;
        if (dwell > 0) ctx.report(segId(cut.asset.id, 0), dwell, cut.lenSec * 1000);
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
   *  settles into visible fail states rather than swap-looping. */
  function scheduleErrorSwap() {
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

  function surfaceCtx(t) {
    return {
      advances: t.advances,
      autoAdvance: ctx.isAutoAdvance(),
      onEnded: () => ctx.onAdvance(),
      onError: (cut, detail) => {
        ctx.onMediaError?.(cut.asset, detail);
        scheduleErrorSwap();
      },
      onAssetMeta: ctx.onAssetMeta,
      report: ctx.report,
    };
  }

  function mount(t) {
    mountGen++;
    tile = t;
    errorSwaps = 0;
    clearErrorSwap();
    applyRect(t);
    if (surface) { surface.stop(); surface = null; }
    const s = createSurface({ ...t.cut, fit: t.fit }, surfaceCtx(t));
    s.el.style.transform = 'translateX(0)';
    clip.appendChild(s.el);
    surface = s;
    ctx.applyAudio();
  }

  function unmount() {
    mountGen++;
    clearErrorSwap();
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

  function springBack() {
    if (!surface) return;
    surface.el.style.transition = 'transform 200ms ease-out';
    surface.el.style.transform = 'translateX(0)';
    setTimeout(() => { if (surface) surface.el.style.transition = ''; }, 240);
  }

  function commit(dir) {
    const cut = ctx.onSwap(tileIndex, dir);
    if (!cut) { springBack(); return; }
    slideTo(cut, dir);
  }

  // Pointer drag: horizontal swap; a near-still release is a tap (audio focus).
  let dragId = null;
  let dragStartX = 0;
  let dragStartY = 0;
  let lastX = 0;
  let lastT = 0;
  let velocity = 0;
  slot.addEventListener('pointerdown', (e) => {
    // A press on a chevron must never start a drag: setPointerCapture below
    // retargets the whole gesture (including the compatibility `click`) to the
    // slot, which is exactly what used to swallow the chevron's own click.
    if (e.target.closest?.('.chev')) return;
    if (e.button !== 0 || sliding || !surface) return;
    dragId = e.pointerId;
    dragStartX = lastX = e.clientX;
    dragStartY = e.clientY;
    lastT = performance.now();
    velocity = 0;
    slot.setPointerCapture(e.pointerId);
  });
  slot.addEventListener('pointermove', (e) => {
    if (dragId !== e.pointerId || !surface) return;
    const now = performance.now();
    const dt = now - lastT;
    if (dt > 0) velocity = (e.clientX - lastX) / dt;
    lastX = e.clientX;
    lastT = now;
    const dx = e.clientX - dragStartX;
    if (Math.abs(dx) > 6) {
      surface.el.style.transition = 'none';
      surface.el.style.transform = `translateX(${dx * DRAG_RESISTANCE}px)`;
    }
  });
  const endDrag = (e) => {
    if (dragId !== e.pointerId) return;
    dragId = null;
    if (!surface) return;
    surface.el.style.transition = '';
    const dx = e.clientX - dragStartX;
    const dy = e.clientY - dragStartY;
    if (Math.abs(dx) > COMMIT_DIST || Math.abs(velocity) > COMMIT_VELOCITY) {
      commit(dx < 0 || (Math.abs(dx) <= COMMIT_DIST && velocity < 0) ? 'next' : 'back');
    } else if (Math.abs(dx) < 10 && Math.abs(dy) < 10) {
      springBack();
      ctx.onTapAudio(tileIndex); // a tap means "let me hear this one"
    } else {
      springBack();
    }
  };
  slot.addEventListener('pointerup', endDrag);
  slot.addEventListener('pointercancel', (e) => { if (dragId === e.pointerId) { dragId = null; springBack(); } });

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
    mount,
    unmount,
    applyRect,
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
 * One feed page. Created for every composition in the window; only mounts live
 * media while active. ctx:
 *   { onAdvance(compKey), onSwap(compKey, i, dir), onTapAudio(compKey, i),
 *     onAssetMeta, report, isAutoAdvance(), isMuted(), audioGlow() -> bool,
 *     audioFocus() -> index|null }
 */
export function createPage(comp, ctx) {
  const el = document.createElement('div');
  el.className = 'page';
  const inner = document.createElement('div');
  inner.className = 'page-inner';
  el.appendChild(inner);

  let active = false;
  let slots = [];
  let morphTimer = null;
  let morphing = false;  // a morph's fade-out is in flight

  function audioIndex() {
    const focus = ctx.audioFocus(comp.key);
    if (focus != null && comp.tiles[focus]?.cut.asset.type === 'video') return focus;
    return comp.tiles.findIndex((t) => t.audio);
  }

  function applyAudio() {
    const ai = audioIndex();
    const muted = ctx.isMuted();
    // Nothing is audible while muted (or when no tile carries audio at all) —
    // in that case no tile glows either.
    const glow = !muted && ai >= 0 && ctx.audioGlow?.() !== false;
    slots.forEach((s, i) => {
      s.surface?.setMuted(muted || i !== ai);
      s.setAudioGlow(glow && i === ai);
    });
  }

  function slotCtx(i) {
    return {
      isAutoAdvance: ctx.isAutoAdvance,
      onAdvance: () => ctx.onAdvance(comp.key),
      onSwap: (idx, dir) => ctx.onSwap(comp.key, idx, dir),
      onTapAudio: (idx) => { ctx.onTapAudio(comp.key, idx); applyAudio(); },
      onAssetMeta: ctx.onAssetMeta,
      onMediaError: ctx.onMediaError,
      report: ctx.report,
      applyAudio,
    };
  }

  function mountTiles() {
    slots = comp.tiles.map((t, i) => {
      const slot = createTileSlot(el, i, slotCtx(i));
      inner.appendChild(slot.el);
      slot.mount(t);
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
    if (!active || morphing || comp.layout !== 'random') return false;
    morphing = true;
    if (morphTimer != null) { clearTimeout(morphTimer); morphTimer = null; }
    inner.style.transition = `opacity ${MORPH_FADE_OUT_MS}ms ease-in`;
    inner.style.opacity = '0';
    setTimeout(() => {
      morphing = false;
      if (!active) { inner.style.opacity = '1'; return; }
      const tiles = ctx.onMorph(comp.key); // feed re-composes in place
      if (tiles) { unmountTiles(); mountTiles(); }
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
    if (!active || comp.layout !== 'random' || !ctx.mosaicAutoChange()) return;
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
    get active() { return active; },

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

    setActive(on) {
      if (on === active) return;
      active = on;
      if (on) { mountTiles(); scheduleMorph(); }
      else { cancelMorph(); unmountTiles(); }
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
