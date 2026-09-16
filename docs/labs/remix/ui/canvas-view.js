// The live canvas: fits the output-size canvas into its box, renders frames, and owns everything
// spatial (rule 3): tile select, the dock drag (swap / split), caption drag, focus path drawing,
// Before the first drop the engine plays the bundled sample loops under the prompt.
import { ICONS } from './hud.js';

const reduced = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
const HOLD_MS = 350;      // touch: press-hold before the tile lifts (a deliberate tap stays a tap)
const MOVE_PX = 6;        // mouse: travel before the tile lifts
const SPLIT_MS = 2000;    // still over one tile this long: split mode
const EDGE_MS = 500;      // in the target's outer band this long: split mode
const EDGE_BAND = 0.15;   // the outer 15% of the target
const CENTRE = 0.15;      // 30% centre square (half each side of centre) keeps swap
const SIDES = { left: 'left of', right: 'right of', top: 'above', bottom: 'below' };

export function initCanvasView(ctx) {
  const p = ctx.project;
  const box = document.getElementById('stage-box');
  const fit = document.getElementById('stage-fit');
  const canvas = document.getElementById('stage');
  const overlay = document.getElementById('stage-overlay');
  const c2d = canvas.getContext('2d');
  let badge, captionEl, focusSvg, focusHint, tileEls = new Map();
  let drawing = null, drag = null;

  // ---- fit
  function refit() {
    const { w, h } = p.size;
    if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
    const bw = box.clientWidth, bh = box.clientHeight; if (!bw || !bh) return;
    const s = Math.min(bw / w, bh / h);
    const fw = Math.floor(w * s); fit.style.width = fw + 'px'; fit.style.height = Math.floor(h * s) + 'px';
    // a narrow stage takes the short badge; a cramped one (a sheet and its knobs open) drops it and the tab's label
    const narrow = fw < 420, tight = fw < 200;
    if (box.classList.contains('narrow') !== narrow || box.classList.contains('tight') !== tight) { box.classList.toggle('narrow', narrow); box.classList.toggle('tight', tight); updateFrame(); }
    draw();
  }
  new ResizeObserver(refit).observe(box);

  // ---- draw
  function draw() { try { p.renderFrame(p.frame, c2d); } catch (e) { console.error(e); } }

  // ---- helpers
  const frac = e => { const r = overlay.getBoundingClientRect(); return { x: Math.max(0, Math.min(1, (e.clientX - r.left) / r.width)), y: Math.max(0, Math.min(1, (e.clientY - r.top) / r.height)) }; };
  const rectsNow = () => p.rectsAt(p.frame);
  const FULL = { x: 0, y: 0, w: 1, h: 1 };
  const placeRect = (el, r) => { el.style.left = r.x * 100 + '%'; el.style.top = r.y * 100 + '%'; el.style.width = r.w * 100 + '%'; el.style.height = r.h * 100 + '%'; };
  const activeBlock = (effect) => {
    const sel = p.blocks.find(b => b.id === ctx.state.selectedBlockId);
    if (sel?.effect === effect) return sel;
    if (ctx.state.openPanel === effect) return p.blocks.find(b => b.effect === effect && b.target === ctx.currentTarget()) || null;
    return null;
  };
  const rectOfTarget = (target) => { if (target === 'canvas') return FULL; const i = p.tiles.findIndex(t => t.id === target); return (i >= 0 && rectsNow()[i]) || FULL; };

  // ---- build overlay (on change / state)
  function render() {
    if (drag) cancelDrag(true);
    if (canvas.width !== p.size.w || canvas.height !== p.size.h) refit();
    overlay.innerHTML = ''; tileEls = new Map();
    overlay.classList.toggle('drawing', false);
    if (ctx.isEmpty()) {
      const emptyEl = document.createElement('div'); emptyEl.className = 'empty';
      emptyEl.innerHTML = `<div class="empty-title">drop your gifs</div><div class="empty-line">up to 8. gifs, pictures, short videos. nothing is uploaded.</div>`;
      const add = document.createElement('button'); add.type = 'button'; add.className = 'empty-add'; add.innerHTML = `${ICONS.add.replace('<svg', '<svg width="20" height="20"')}<span>Add gifs</span>`;
      add.addEventListener('click', () => ctx.pickFiles()); emptyEl.appendChild(add); overlay.appendChild(emptyEl);
      badge = null; draw(); return;
    }
    const rects = rectsNow();
    p.tiles.forEach((t, i) => {
      const el = document.createElement('div'); el.className = 'tile-hit'; el.dataset.tile = t.id; el.tabIndex = 0; el.setAttribute('role', 'button');
      const m = ctx.mediaOf(t); el.setAttribute('aria-label', `${ctx.tileLabel(t)}: ${m?.name || ''}. Drag onto another gif to swap, hold there to dock beside it.`);
      placeRect(el, rects[i] || FULL); el.hidden = !rects[i];
      el.classList.toggle('selected', t.id === ctx.state.selectedTileId);
      el.innerHTML = `<span class="tile-name">${ctx.tileLabel(t)}</span>`;
      const x = document.createElement('button'); x.type = 'button'; x.className = 'tile-x'; x.innerHTML = ICONS.x; x.setAttribute('aria-label', `Remove ${ctx.tileLabel(t)}`);
      x.addEventListener('pointerdown', e => e.stopPropagation());
      x.addEventListener('click', e => { e.stopPropagation(); ctx.removeTile(t.id); });
      el.appendChild(x);
      bindTile(el, t);
      overlay.appendChild(el); tileEls.set(t.id, el);
    });
    // top-left: the canvas tab (tap = the whole canvas, hold = its strip) and the stage badge beside it
    const tl = document.createElement('div'); tl.className = 'stage-tl';
    const tab = document.createElement('button'); tab.type = 'button'; tab.className = 'canvas-tab'; tab.innerHTML = `${ICONS.canvas}<span>CANVAS</span>`;
    tab.title = 'Tap: the whole canvas. Hold: its strip.'; tab.setAttribute('aria-label', 'The whole canvas. Tap to select it, hold for its strip.');
    tab.classList.toggle('on', ctx.currentTarget() === 'canvas');
    bindTab(tab);
    badge = document.createElement('div'); badge.className = 'stage-badge';
    tl.append(tab, badge); overlay.appendChild(tl);
    // caption handle
    const cap = activeBlock('caption');
    if (cap && cap.mode !== 'flash') {
      captionEl = document.createElement('div'); captionEl.className = 'caption-handle'; captionEl.textContent = (cap.params.text || 'drop').toUpperCase();
      captionEl.title = 'Drag to place the caption'; captionEl.setAttribute('aria-label', 'Caption position, drag to move');
      const tr = rectOfTarget(cap.target); const pos = cap.params.pos || { x: .5, y: .78 };
      const fs = Math.max(14, fit.clientHeight * tr.h * (.1 + (cap.params.size ?? 60) / 400));
      captionEl.style.fontSize = fs + 'px';
      captionEl.style.left = (tr.x + pos.x * tr.w) * 100 + '%'; captionEl.style.top = (tr.y + pos.y * tr.h) * 100 + '%';
      bindCaption(captionEl, cap, tr);
      overlay.appendChild(captionEl);
    }
    // focus path
    const foc = activeBlock('focus');
    if (foc) {
      overlay.classList.add('drawing');
      focusSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg'); focusSvg.setAttribute('class', 'focus-svg'); focusSvg.setAttribute('viewBox', '0 0 1000 1000'); focusSvg.setAttribute('preserveAspectRatio', 'none');
      overlay.appendChild(focusSvg);
      focusHint = document.createElement('div'); focusHint.className = 'focus-hint'; focusHint.textContent = foc.params.path?.length > 1 ? 'PRESS AND DRAG TO REDRAW THE PATH' : 'PRESS AND DRAG TO DRAW THE PATH';
      overlay.appendChild(focusHint);
      drawFocus(foc);
    }
    updateFrame();
    draw();
  }

  function drawFocus(b, livePts) {
    if (!focusSvg) return;
    const tr = rectOfTarget(b.target); const pts = livePts || b.params.path || [];
    const P = q => `${(tr.x + q.x * tr.w) * 1000},${(tr.y + q.y * tr.h) * 1000}`;
    const d = pts.length ? 'M' + pts.map(P).join(' L') : '';
    const prog = Math.max(0, Math.min(1, (p.frame - b.start) / Math.max(1, b.end - b.start - 1)));
    let ring = '';
    if (pts.length) {
      const k = prog * (pts.length - 1), i0 = Math.floor(k), i1 = Math.min(pts.length - 1, i0 + 1), f = k - i0;
      const q = { x: pts[i0].x + (pts[i1].x - pts[i0].x) * f, y: pts[i0].y + (pts[i1].y - pts[i0].y) * f }; const [cx, cy] = P(q).split(',');
      // the radius the engine draws: min(tile w, tile h) * (.16 + radius * .55)
      const W = Math.max(1, fit.clientWidth), H = Math.max(1, fit.clientHeight);
      const rpx = Math.max(6, Math.min(tr.w * W, tr.h * H) * (.16 + (b.params.radius ?? 40) / 100 * .55));
      ring = `<ellipse cx="${cx}" cy="${cy}" rx="${rpx / W * 1000}" ry="${rpx / H * 1000}" stroke="#F0C24B" stroke-width="2.5" stroke-dasharray="10 7" fill="none" vector-effect="non-scaling-stroke"/><circle cx="${cx}" cy="${cy}" r="5" fill="#F0C24B"/>`;
    }
    focusSvg.innerHTML = `<path d="${d}" stroke="#F0C24B" stroke-width="2" stroke-dasharray="6 8" opacity=".8" fill="none" vector-effect="non-scaling-stroke"/>${ring}${pts.length ? `<circle cx="${P(pts[0]).split(',')[0]}" cy="${P(pts[0]).split(',')[1]}" r="4" fill="#F0C24B" opacity=".6"/>` : ''}`;
  }

  // per-frame updates: badge, moving rects, focus ring
  function updateFrame() {
    if (!badge || ctx.isEmpty()) return;
    const rects = rectsNow(); const on = rects.filter(Boolean).length; const n = p.tiles.length;
    const mode = p.layout.mode; const k = mode === 'deck' ? ((rects.findIndex(Boolean) + 1) || 1) : on;
    // phone, or a narrow stage: short form (the frame number lives in the transport row / timeline head)
    badge.textContent = (ctx.isMobile() || box.classList.contains('narrow')) ? (mode === 'flat' ? `FLAT ${n}` : `${mode.toUpperCase()} ${k}/${n}`)
      : mode === 'flat' ? `FLAT · ${n} ON · frame ${p.frame}` : `${mode.toUpperCase()} · ${k} of ${n} · frame ${p.frame}`;
    if (!drag) p.tiles.forEach((t, i) => { const el = tileEls.get(t.id); if (!el) return; const r = rects[i]; el.hidden = !r; if (r) placeRect(el, r); });
    const foc = activeBlock('focus'); if (foc && !drawing) drawFocus(foc);
  }

  // ---- tiles. Tap = select (or bin). The dock drag, VS Code editor-docking style:
  //   idle -> armed on pointerdown -> lifted after a 350 ms hold (touch) or 6 px of travel (mouse)
  //   lifted: a ghost follows the pointer, the source dims to 40%
  //   over another tile: pink outline + "drop to swap" (mode swap)
  //   still on that tile for 2 s, or 500 ms inside its outer 15% band: mode split
  //     the pointer picks left / right / top / bottom; the 30% centre square keeps swap
  //     the mosaic previews the dock (engine previewDock) with a 120 ms ease
  //   leaving the tile: back to swap, timers reset; a different tile restarts the 2 s
  //   drop on a tile: swapTiles / dockTile; drop elsewhere or Esc: the ghost snaps back
  //
  // Touch hold, reconciled with the strip sheet (phone):
  //   armed -(350 ms still)-> lifted: buzz, ghost (two or more tiles) or a held outline (one tile)
  //   lifted -(moves over 14 px)-> the swap / split drag above, exactly as before
  //   lifted -(released without moving)-> the ghost goes back and the gif's strip sheet opens
  //   armed -(moves over 14 px before 350 ms)-> a flick, nothing
  //   armed -(released before 350 ms)-> a tap: select (or bin)
  function bindTile(el, t) {
    let arm = null;
    const lift = (pointerId, a) => {
      a.lifted = true; buzz();
      if (p.tiles.length >= 2 && !drag) { startDrag(el, t, pointerId, a); if (drag) drag.arm = a; }
      else el.classList.add('held');
    };
    el.addEventListener('pointerdown', e => {
      if (e.button && e.button !== 0) return;
      if (overlay.classList.contains('drawing') || drag) return;
      arm = { x: e.clientX, y: e.clientY, id: e.pointerId, touch: e.pointerType !== 'mouse', hold: 0, lifted: false, moved: false };
      el.setPointerCapture(e.pointerId);
      if (arm.touch && !ctx.state.binMode) arm.hold = setTimeout(() => { if (arm && !arm.lifted) lift(e.pointerId, arm); }, HOLD_MS);
    });
    el.addEventListener('pointermove', e => {
      if (drag) { if (drag.arm && !drag.arm.moved && Math.hypot(e.clientX - drag.arm.x, e.clientY - drag.arm.y) > 14) drag.arm.moved = true; moveDrag(e); return; }
      if (!arm || ctx.state.binMode) return;
      const d = Math.hypot(e.clientX - arm.x, e.clientY - arm.y);
      if (!arm.touch && d > MOVE_PX) { const a = arm; arm = null; a.moved = true; startDrag(el, t, e.pointerId, a); moveDrag(e); }
      else if (arm.touch && d > 14) { clearTimeout(arm.hold); if (arm.lifted) el.classList.remove('held'); arm = null; } // a flick, not a hold
    });
    const end = e => {
      if (drag && e.pointerId === drag.pointerId) {
        const a = drag.arm; arm = null;
        if (e.type === 'pointercancel') cancelDrag();
        else if (a && !a.moved) { const resume = drag.resume; cancelDrag(true); if (resume) p.play(); ctx.holdTarget(t.id); } // held still: the strip, not a drop
        else endDrag(e);
        return;
      }
      if (!arm) return; const a = arm; clearTimeout(a.hold); arm = null; el.classList.remove('held');
      if (e.type === 'pointercancel') return;
      if (a.lifted) { ctx.holdTarget(t.id); return; }
      if (ctx.state.binMode) { ctx.removeTile(t.id); return; }
      ctx.selectTile(t.id);
    };
    el.addEventListener('pointerup', end); el.addEventListener('pointercancel', end);
    el.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); ctx.state.binMode ? ctx.removeTile(t.id) : ctx.selectTile(t.id); } });
  }

  // The canvas tab: tap = select the whole canvas (the tile deselects, panels target the canvas);
  // hold 350 ms = the canvas strip (phone: the strip sheet; desktop: the canvas lane lights up).
  function bindTab(tab) {
    let arm = null;
    tab.addEventListener('pointerdown', e => {
      if (e.button && e.button !== 0) return; e.stopPropagation();
      arm = { x: e.clientX, y: e.clientY, id: e.pointerId, hold: 0, lifted: false };
      try { tab.setPointerCapture(e.pointerId); } catch {}
      arm.hold = setTimeout(() => { if (arm) { arm.lifted = true; tab.classList.add('held'); buzz(); } }, HOLD_MS);
    });
    tab.addEventListener('pointermove', e => { if (arm && Math.hypot(e.clientX - arm.x, e.clientY - arm.y) > 14) { clearTimeout(arm.hold); arm = null; tab.classList.remove('held'); } });
    const end = e => {
      if (!arm) return; const a = arm; clearTimeout(a.hold); arm = null; tab.classList.remove('held');
      if (e.type === 'pointercancel') return;
      if (a.lifted) { ctx.holdTarget('canvas'); return; }
      pickCanvas();
    };
    tab.addEventListener('pointerup', end); tab.addEventListener('pointercancel', end);
    tab.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); pickCanvas(); } });
  }
  let pickTimer = 0;
  function pickCanvas() {
    ctx.selectTile(null);
    overlay.classList.add('picked'); clearTimeout(pickTimer); pickTimer = setTimeout(() => overlay.classList.remove('picked'), 700);
  }

  function startDrag(el, t, pointerId, from) {
    if (drag || p.tiles.length < 2) return;
    const resume = p.playing; p.pause();
    // grow: bring every tile on so the whole mosaic is there to rearrange
    const enter = Math.max(0, ...p.tiles.map(x => x.enterFrame || 0)) + 4;
    if (p.layout.mode !== 'flat' && p.layout.mode !== 'deck' && p.frame < enter) p.seek(Math.min(p.frames - 1, enter));
    const rects = rectsNow(); const i = p.tiles.indexOf(t); const r = rects[i] || FULL;
    const o = overlay.getBoundingClientRect(); const W = o.width, H = o.height;
    const ghost = document.createElement('canvas'); ghost.className = 'dock-ghost';
    const gw = Math.max(8, Math.round(r.w * W)), gh = Math.max(8, Math.round(r.h * H));
    ghost.width = gw; ghost.height = gh; ghost.style.width = gw + 'px'; ghost.style.height = gh + 'px';
    try { ghost.getContext('2d').drawImage(canvas, r.x * canvas.width, r.y * canvas.height, r.w * canvas.width, r.h * canvas.height, 0, 0, gw, gh); } catch {}
    const pill = document.createElement('div'); pill.className = 'dock-pill'; pill.hidden = true;
    const land = document.createElement('div'); land.className = 'dock-land'; land.hidden = true;
    overlay.append(land, ghost, pill);
    drag = { tile: t, el, pointerId, ox: r.x * W, oy: r.y * H, gx: from.x, gy: from.y, ghost, pill, land, resume, base: rects,
      target: null, targetEl: null, rect: null, mode: 'none', side: null, splitTimer: 0, edgeTimer: 0, previewing: false, last: null };
    el.classList.add('drag-source'); overlay.classList.add('docking');
    ghost.style.transform = `translate(${drag.ox}px, ${drag.oy}px)`;
    window.addEventListener('keydown', escDrag, true);
    buzz();
  }
  const buzz = () => { if (navigator.vibrate) try { navigator.vibrate(8); } catch {} };
  const escDrag = e => { if (e.key === 'Escape' && drag) { e.preventDefault(); e.stopPropagation(); cancelDrag(); } };
  const pickSide = (d, u, v) => {
    const dx = u - .5, dy = v - .5;
    if (Math.abs(dx) <= CENTRE && Math.abs(dy) <= CENTRE) return 'centre';
    const o = overlay.getBoundingClientRect();
    const px = Math.abs(dx) * d.rect.w * o.width, py = Math.abs(dy) * d.rect.h * o.height;
    return px >= py ? (dx < 0 ? 'left' : 'right') : (dy < 0 ? 'top' : 'bottom');
  };
  const pillText = d => d.mode === 'split' && d.side && d.side !== 'centre' ? `drop to dock ${SIDES[d.side]} ${ctx.tileLabel(d.target)}` : `drop to swap with ${ctx.tileLabel(d.target)}`;

  function moveDrag(e) {
    if (!drag || e.pointerId !== drag.pointerId) return;
    const d = drag; const o = overlay.getBoundingClientRect();
    d.ghost.style.transform = `translate(${d.ox + e.clientX - d.gx}px, ${d.oy + e.clientY - d.gy}px)`;
    const f = { x: (e.clientX - o.left) / o.width, y: (e.clientY - o.top) / o.height };
    // the tile under the pointer, by geometry, so the ghost is never in the way
    let hit = null, hitRect = null;
    p.tiles.forEach((t, i) => { const r = d.base[i]; if (!r || t.id === d.tile.id) return; if (f.x >= r.x && f.x < r.x + r.w && f.y >= r.y && f.y < r.y + r.h) { hit = t; hitRect = r; } });
    if (hit?.id !== d.target?.id) {
      clearTimers(d); clearPreview(d);
      d.targetEl?.classList.remove('dock-target', 'split');
      d.target = hit; d.targetEl = hit ? tileEls.get(hit.id) : null; d.mode = hit ? 'swap' : 'none'; d.side = null;
      d.targetEl?.classList.add('dock-target');
      if (hit) d.splitTimer = setTimeout(() => enterSplit(d), SPLIT_MS);
    }
    if (!hit) { d.pill.hidden = true; d.last = null; return; }
    d.rect = hitRect;
    const u = (f.x - hitRect.x) / hitRect.w, v = (f.y - hitRect.y) / hitRect.h;
    d.last = { u, v };
    const inBand = u < EDGE_BAND || u > 1 - EDGE_BAND || v < EDGE_BAND || v > 1 - EDGE_BAND;
    if (d.mode === 'swap') {
      if (inBand && !d.edgeTimer) d.edgeTimer = setTimeout(() => enterSplit(d), EDGE_MS);
      else if (!inBand && d.edgeTimer) { clearTimeout(d.edgeTimer); d.edgeTimer = 0; }
    }
    if (d.mode === 'split') { const side = pickSide(d, u, v); if (side !== d.side) { d.side = side; showPreview(d); } }
    d.pill.hidden = false; d.pill.textContent = pillText(d);
    d.pill.style.left = Math.max(80, Math.min(o.width - 80, f.x * o.width)) + 'px';
    d.pill.style.top = Math.max(18, f.y * o.height - 34) + 'px';
  }
  function enterSplit(d) {
    if (!drag || d !== drag || !d.target || d.mode === 'split') return;
    clearTimers(d); d.mode = 'split'; d.side = null;
    d.targetEl?.classList.add('split');
    buzz();
    if (d.last) { d.side = pickSide(d, d.last.u, d.last.v); showPreview(d); d.pill.textContent = pillText(d); }
  }
  function showPreview(d) {
    if (d.side === 'centre' || !d.side) { clearPreview(d); return; }
    const prev = p.previewDock(d.tile.id, d.target.id, d.side);
    overlay.classList.add('previewing'); d.previewing = true;
    for (const r of prev) {
      if (r.id === d.tile.id) { d.land.hidden = false; placeRect(d.land, r.rect); continue; }
      const el = tileEls.get(r.id); if (el) { el.hidden = false; placeRect(el, r.rect); }
    }
  }
  function clearPreview(d) {
    if (!d.previewing) return;
    d.previewing = false; d.land.hidden = true;
    p.tiles.forEach((t, i) => { const el = tileEls.get(t.id); if (!el) return; const r = d.base[i]; el.hidden = !r; if (r) placeRect(el, r); });
    setTimeout(() => { if (!drag?.previewing) overlay.classList.remove('previewing'); }, 130);
  }
  function clearTimers(d) { clearTimeout(d.splitTimer); clearTimeout(d.edgeTimer); d.splitTimer = 0; d.edgeTimer = 0; }
  function teardown(d) {
    clearTimers(d); window.removeEventListener('keydown', escDrag, true);
    d.el.classList.remove('drag-source'); d.targetEl?.classList.remove('dock-target', 'split');
    overlay.classList.remove('docking'); d.pill.remove(); d.land.remove();
    drag = null;
  }
  function cancelDrag(silent) {
    if (!drag) return; const d = drag;
    clearPreview(d);
    if (silent || reduced()) d.ghost.remove();
    else { d.ghost.classList.add('back'); d.ghost.style.transform = `translate(${d.ox}px, ${d.oy}px)`; d.ghost.style.opacity = '0'; setTimeout(() => d.ghost.remove(), 200); }
    teardown(d);
    if (d.resume && !silent) p.play();
  }
  function endDrag(e) {
    if (!drag || e.pointerId !== drag.pointerId) return;
    const d = drag;
    if (!d.target) { cancelDrag(); return; }
    const a = ctx.tileLabel(d.tile), b = ctx.tileLabel(d.target);
    d.previewing = false; overlay.classList.remove('previewing'); d.ghost.remove();
    teardown(d);
    if (d.mode === 'split' && d.side && d.side !== 'centre') { p.dockTile(d.tile.id, d.target.id, d.side); ctx.toast(`Docked ${a} ${SIDES[d.side]} ${b}`); }
    else { p.swapTiles(d.tile.id, d.target.id); ctx.toast(`Swapped ${a} and ${b}`); }
    ctx.syncHistory();
    if (d.resume) p.play();
  }

  function bindCaption(el, b, tr) {
    let cd = null;
    el.addEventListener('pointerdown', e => { e.stopPropagation(); cd = { id: e.pointerId }; el.setPointerCapture(e.pointerId); el.classList.add('grabbing'); if (p.playing) { p.pause(); cd.resume = true; } });
    el.addEventListener('pointermove', e => { if (!cd) return; const f = frac(e); const pos = { x: (f.x - tr.x) / tr.w, y: (f.y - tr.y) / tr.h }; p.setCaptionPos(b.id, pos); const q = p.blocks.find(x => x.id === b.id)?.params.pos || pos; el.style.left = (tr.x + q.x * tr.w) * 100 + '%'; el.style.top = (tr.y + q.y * tr.h) * 100 + '%'; });
    const end = () => { if (!cd) return; const r = cd.resume; cd = null; el.classList.remove('grabbing'); ctx.commit('caption position'); if (r) p.play(); };
    el.addEventListener('pointerup', end); el.addEventListener('pointercancel', end);
  }

  // focus path drawing on the overlay background
  overlay.addEventListener('pointerdown', e => {
    if (!overlay.classList.contains('drawing')) { if (e.target === overlay) ctx.selectTile(null); return; }
    if (e.target.closest('.stamp-btn, .caption-handle, .tile-x')) return;
    const b = activeBlock('focus'); if (!b) return;
    const tr = rectOfTarget(b.target); const f = frac(e);
    drawing = { b, tr, pts: [{ x: (f.x - tr.x) / tr.w, y: (f.y - tr.y) / tr.h }], resume: p.playing };
    p.pause(); overlay.setPointerCapture(e.pointerId); drawFocus(b, drawing.pts);
  });
  overlay.addEventListener('pointermove', e => {
    if (!drawing) return; const f = frac(e); const q = { x: (f.x - drawing.tr.x) / drawing.tr.w, y: (f.y - drawing.tr.y) / drawing.tr.h };
    const last = drawing.pts[drawing.pts.length - 1]; if (Math.hypot(q.x - last.x, q.y - last.y) > .012) { drawing.pts.push(q); drawFocus(drawing.b, drawing.pts); }
  });
  const endDraw = () => {
    if (!drawing) return; const d = drawing; drawing = null;
    if (d.pts.length < 2) d.pts.push({ x: Math.min(1, d.pts[0].x + .001), y: d.pts[0].y });
    p.setFocusPath(d.b.id, d.pts); ctx.commit('focus path'); ctx.toast('Path set. Play to see the ring travel.');
    if (d.resume) p.play();
  };
  overlay.addEventListener('pointerup', endDraw); overlay.addEventListener('pointercancel', endDraw);

  p.on('frame', () => { draw(); updateFrame(); });
  refit();
  return { render, refit, draw, isDragging: () => !!drag };
}
