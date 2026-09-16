// Timeline: ruler + playhead scrub, canvas strip (canvas blocks + loop marker), selected tile's strip
// (filmstrip + its blocks + play glyph). Blocks: drag to move, drag an edge to resize, tap to open.
// The strip renderer, scrub and block drag live in `initLanes` so the docked timeline (desktop) and the
// phone's strip sheet draw the same strip with one copy of the code. The phone also gets a transport
// row under the top bar (play, time, undo, redo, loop) since its timeline is hidden until a hold.
import { ICONS } from './hud.js';

const NAMES = { drain: 'Drain', tint: 'Tint', spiral: 'Spiral', glitch: 'Glitch', caption: 'Caption', focus: 'Focus' };
const COLOUR_NAMES = { pink: 'pink', lavender: 'lavender', gold: 'gold', custom: 'from media' };
const PLAY_MODES = [['forward', 'Forward', 'plays as is'], ['hold', 'Hold', 'freeze until it enters'], ['rewind', 'Rewind', 'backwards'], ['boomerang', 'Boomerang', 'there and back']];
const LANE_H = () => document.documentElement.classList.contains('is-mobile') ? 40 : 30;

export function blockLabel(b, ctx) {
  const P = b.params || {};
  if (b.effect === 'caption') return b.mode === 'flash' ? `Caption · flash 1f` : `Caption · ${b.mode} · ${(P.text || 'drop').slice(0, 14)}`;
  if (b.effect === 'tint') return `Tint · ${b.mode} · ${COLOUR_NAMES[P.colour] || P.colour || ''} · ${P.intensity ?? ''}`;
  if (b.effect === 'focus') return `Focus · ${b.mode} · ${P.path?.length > 2 ? 'drawn path' : 'still'}`;
  if (b.effect === 'drain') return `Drain · ${b.mode}${b.target !== 'canvas' && b.mode === 'spread' ? ' from ' + ctx.tileLabelById(b.target) : ''} · ${P.strength ?? ''}`;
  if (b.effect === 'spiral') return `Spiral · ${b.mode} · ${P.strength ?? ''}`;
  return `${NAMES[b.effect] || b.effect} · ${b.mode} · ${P.strength ?? ''}`;
}

export const el = (tag, cls) => { const e = document.createElement(tag); if (cls) e.className = cls; return e; };

// The strip kit. `onBlock(b)` runs when a block is tapped (default: select it and open its panel).
export function initLanes(ctx, { onBlock } = {}) {
  const p = ctx.project;
  let menu = null, scrub = null;
  const openBlock = b => { ctx.selectBlock(b.id); (onBlock || ctx.openPanelFor)(b); };

  const pct = f => (f / p.frames * 100) + '%';
  const frameFromX = (e, lane) => { const r = lane.getBoundingClientRect(); return Math.max(0, Math.min(p.frames, Math.round((e.clientX - r.left) / r.width * p.frames))); };
  // rows for stacked blocks. A pin's label sticks out ~12% of the lane, so it holds that much room.
  const lanesFor = blocks => {
    const ends = []; const L = Math.ceil(p.frames * .12);
    return blocks.map(b => {
      const pin = b.end - b.start <= 1, flip = pin && b.start / p.frames > .72;
      const s = flip ? b.start - L : b.start, e = pin ? (flip ? b.start + 1 : b.start + L) : b.end;
      let li = ends.findIndex(x => x <= s); if (li < 0) { li = ends.length; ends.push(0); } ends[li] = e; return li;
    });
  };

  function renderRuler(ruler) {
    ruler.querySelectorAll('.tick, .tick-l').forEach(x => x.remove());
    if (!ruler.querySelector('.playhead-tri')) ruler.appendChild(el('div', 'playhead-tri'));
    const sec = p.frames / p.fps; const step = sec > 6 ? 1 : .5;
    for (let s = 0; s <= sec + 1e-6; s += step) { const t = el('div', 'tick'); t.style.left = pct(s * p.fps); const l = el('div', 'tick-l'); l.style.left = t.style.left; l.textContent = Number.isInteger(s) ? `${s}s` : `${s}`; ruler.append(t, l); }
  }

  // `extra` is headroom under stacked rows (the desktop canvas lane keeps the loop chip clear)
  function renderLane(lane, blocks, media, { extra = 0 } = {}) {
    lane.innerHTML = '';
    if (media?.thumb) {
      const fs = document.createElement('canvas'); fs.className = 'filmstrip'; const W = Math.max(200, lane.clientWidth || 600), H = Math.max(40, lane.clientHeight || 60); fs.width = W; fs.height = H; const c = fs.getContext('2d');
      const cell = ctx.isMobile() ? 18 : 26; for (let x = 0; x < W; x += cell) { c.drawImage(media.thumb, 0, 0, media.thumb.width, media.thumb.height, x, 0, cell - 1, H); c.fillStyle = 'rgba(20,20,43,.9)'; c.fillRect(x + cell - 1, 0, 1, H); }
      lane.appendChild(fs);
    }
    const sorted = blocks.slice().sort((a, b) => a.start - b.start); const lanes = lanesFor(sorted);
    const nl = Math.max(1, ...lanes.map(x => x + 1)); lane.style.minHeight = Math.max(44, 8 + nl * LANE_H() + (nl > 1 ? extra : 0)) + 'px';
    sorted.forEach((b, i) => {
      const e = el('div', 'blk'); e.dataset.block = b.id; e.style.setProperty('--c', `var(--c-${b.effect})`); e.tabIndex = 0; e.setAttribute('role', 'button');
      const pin = b.end - b.start <= 1; e.classList.toggle('pin', pin); e.classList.toggle('flip', pin && b.start / p.frames > .72);
      e.style.left = pct(b.start); e.style.width = pin ? '' : `calc(${(b.end - b.start) / p.frames * 100}% - 1px)`; e.style.top = (4 + lanes[i] * LANE_H()) + 'px';
      e.classList.toggle('selected', b.id === ctx.state.selectedBlockId);
      const label = blockLabel(b, ctx); e.setAttribute('aria-label', `${label}, frames ${b.start} to ${b.end}`); e.title = `${label} · ${b.start}-${b.end}`;
      e.innerHTML = `<span class="blk-edge l"></span><span class="blk-label">${label}</span><span class="blk-edge r"></span>`;
      bindBlock(e, b, lane);
      lane.appendChild(e);
    });
    const ph = el('div', 'playhead-line'); lane.appendChild(ph);
  }

  // move every playhead under `root` to the current frame
  function syncFrame(root) {
    const x = pct(p.frame + .5);
    root.querySelectorAll('.playhead-line, .playhead-tri').forEach(l => l.style.left = x);
  }
  const frameText = () => `${(p.frame / p.fps).toFixed(2)}s · f${p.frame}`;
  const playButton = cls => { const b = el('button', cls); b.type = 'button'; b.title = 'Play / pause (space)'; b.setAttribute('aria-label', 'Play or pause'); b.addEventListener('click', () => ctx.togglePlay()); return b; };
  const syncPlay = b => { b.innerHTML = p.playing ? ICONS.pause : ICONS.play; b.classList.toggle('on', p.playing); };
  const loopChip = () => {
    const chip = el('button', 'loop-chip'); chip.type = 'button'; chip.innerHTML = `${ICONS.loop}<span>LOOP · ${p.loop.toUpperCase()}</span>`; chip.title = 'Loop: clean cut, snap to a word, or seamless crossfade. Tap to cycle.';
    chip.addEventListener('pointerdown', e => e.stopPropagation()); chip.addEventListener('click', () => ctx.cycleLoop());
    return chip;
  };
  const playGlyph = t => {
    const glyph = el('button', 'play-glyph'); glyph.type = 'button'; glyph.innerHTML = ICONS.play; glyph.title = 'Play mode'; glyph.setAttribute('aria-label', `Play mode: ${t.playMode}`); glyph.setAttribute('aria-haspopup', 'menu');
    glyph.addEventListener('click', e => openGlyphMenu(e, t, glyph));
    return glyph;
  };
  const miniThumb = m => { const mt = el('span', 'mini-thumb'); if (m?.thumb) { const cv = document.createElement('canvas'); cv.width = 44; cv.height = 32; cv.getContext('2d').drawImage(m.thumb, 0, 0, 44, 32); mt.appendChild(cv); } return mt; };

  // Lanes are touch-action: pan-y, so a finger can still scroll the timeline. A touch only claims the
  // gesture (pointer capture) once it has moved sideways; a mouse scrubs from the press as before.
  const claim = (el, ev) => { try { el.setPointerCapture(ev.pointerId); } catch {} };
  function bindScrub(area) {
    let pending = null;
    const begin = e => { scrub = { resume: p.playing, area }; p.pause(); claim(area, e); p.seek(Math.min(p.frames - 1, frameFromX(e, area))); };
    area.addEventListener('pointerdown', e => {
      if (e.target.closest('.blk, .loop-chip, button')) return;
      if (e.pointerType === 'mouse') begin(e); else pending = { x: e.clientX, y: e.clientY, id: e.pointerId };
    });
    area.addEventListener('pointermove', e => {
      if (scrub?.area === area) { p.seek(Math.min(p.frames - 1, frameFromX(e, area))); return; }
      if (!pending || e.pointerId !== pending.id) return;
      const dx = Math.abs(e.clientX - pending.x), dy = Math.abs(e.clientY - pending.y);
      if (dy > 8 && dy > dx) pending = null; else if (dx > 6 && dx > dy) { pending = null; begin(e); }
    });
    const end = e => {
      if (pending && e.type === 'pointerup' && e.pointerId === pending.id) { pending = null; p.seek(Math.min(p.frames - 1, frameFromX(e, area))); return; }
      pending = null;
      if (scrub?.area !== area) return; const r = scrub.resume; scrub = null; if (r) p.play();
    };
    area.addEventListener('pointerup', end); area.addEventListener('pointercancel', end);
  }

  function bindBlock(e, b, lane) {
    let d = null;
    e.addEventListener('pointerdown', ev => {
      if (ev.button) return; ev.stopPropagation();
      const edge = ev.target.classList.contains('blk-edge') ? (ev.target.classList.contains('l') ? 'l' : 'r') : null;
      d = { edge, x0: ev.clientX, y0: ev.clientY, f0: frameFromX(ev, lane), start: b.start, end: b.end, moved: false, resume: p.playing, touch: ev.pointerType !== 'mouse' };
      if (!d.touch) claim(e, ev);
    });
    e.addEventListener('pointermove', ev => {
      if (!d) return;
      if (!d.moved && Math.abs(ev.clientX - d.x0) < 4) return;
      // a finger going up or down is scrolling the timeline, not moving the block
      if (!d.moved && d.touch && Math.abs(ev.clientY - d.y0) > Math.abs(ev.clientX - d.x0)) { d = null; return; }
      if (!d.moved) { d.moved = true; e.classList.add('hot'); p.pause(); if (d.touch) claim(e, ev); }
      const df = frameFromX(ev, lane) - d.f0; const len = d.end - d.start;
      let s = d.start, en = d.end;
      if (d.edge === 'l') s = Math.max(0, Math.min(d.end - 1, d.start + df));
      else if (d.edge === 'r') en = Math.min(p.frames, Math.max(d.start + 1, d.end + df));
      else { s = Math.max(0, Math.min(p.frames - len, d.start + df)); en = s + len; }
      if (b.effect === 'caption' && b.mode === 'flash') en = s + 1;
      p.updateBlock(b.id, { start: s, end: en });
      p.seek(Math.min(p.frames - 1, d.edge === 'r' ? en - 1 : s));
    });
    const end = ev => {
      if (!d) return; const dd = d; d = null; e.classList.remove('hot');
      if (dd.moved) { ctx.commit(dd.edge ? 'resize block' : 'move block'); if (dd.resume) p.play(); return; }
      if (ev.type === 'pointercancel') return;
      openBlock(b);
    };
    e.addEventListener('pointerup', end); e.addEventListener('pointercancel', end);
    e.addEventListener('keydown', ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); openBlock(b); } });
  }

  function openGlyphMenu(e, t, anchor) {
    closeMenu();
    menu = el('div', 'glyph-menu'); menu.setAttribute('role', 'menu');
    for (const [mode, name, hint] of PLAY_MODES) { const b = el('button'); b.type = 'button'; b.setAttribute('role', 'menuitemradio'); b.setAttribute('aria-checked', t.playMode === mode); b.classList.toggle('on', t.playMode === mode); b.innerHTML = `${ICONS.play}<span>${name}</span><span class="hint">${hint}</span>`; b.addEventListener('click', () => { p.setPlayMode(t.id, mode); ctx.commit('play mode'); closeMenu(); }); menu.appendChild(b); }
    const r = anchor.getBoundingClientRect();
    document.body.appendChild(menu);
    const mh = menu.offsetHeight || 140, mw = menu.offsetWidth || 220;
    menu.style.left = Math.max(8, Math.min(innerWidth - mw - 8, r.left)) + 'px';
    menu.style.top = (r.bottom + 6 + mh <= innerHeight ? r.bottom + 6 : Math.max(8, r.top - 6 - mh)) + 'px';
    menu.querySelector('button').focus();
    setTimeout(() => document.addEventListener('pointerdown', outsideMenu, true), 0);
  }
  const outsideMenu = ev => { if (menu && !menu.contains(ev.target)) closeMenu(); };
  function closeMenu() { if (!menu) return; menu.remove(); menu = null; document.removeEventListener('pointerdown', outsideMenu, true); }

  return { renderRuler, renderLane, bindScrub, syncFrame, frameText, playButton, syncPlay, loopChip, playGlyph, miniThumb, openGlyphMenu, closeMenu, isMenuOpen: () => !!menu };
}

export function initTimeline(ctx) {
  const p = ctx.project;
  const host = document.getElementById('timeline');
  const transport = document.getElementById('transport');
  const lanes = initLanes(ctx);
  let els = {}, tr = {}, flashTimer = 0;

  function build() {
    host.innerHTML = '';
    const head = el('div', 'tl-head'); head.innerHTML = `<span>TIMELINE</span>`;
    const play = lanes.playButton('tl-play');
    const fr = el('span', 'tl-frame'); head.append(play, fr); host.appendChild(head);
    const ruler = el('div', 'ruler'); ruler.setAttribute('aria-label', 'Drag to scrub'); host.appendChild(ruler);
    lanes.bindScrub(ruler);
    // canvas lane
    const cl = el('div', 'lane-label'); cl.innerHTML = `<div class="lane-title">CANVAS</div>`;
    const csub = el('div', 'lane-sub'); const cbtn = el('button'); cbtn.type = 'button'; cbtn.title = 'Open the layout panel'; cbtn.addEventListener('click', () => ctx.togglePanel('layout')); csub.appendChild(cbtn); cl.appendChild(csub);
    cl.dataset.lane = 'canvas';
    host.appendChild(cl);
    const clane = el('div', 'lane'); clane.dataset.target = 'canvas'; host.appendChild(clane); lanes.bindScrub(clane);
    // tile lane
    const tl = el('div', 'lane-label'); host.appendChild(tl);
    const tlane = el('div', 'lane'); host.appendChild(tlane); lanes.bindScrub(tlane);
    els = { play, fr, ruler, cbtn, clane, tl, tlane, cl };
    // phone transport row: play, time, undo, redo, loop
    if (transport) {
      transport.innerHTML = '';
      const tplay = lanes.playButton('tr-play');
      const tfr = el('span', 'tr-frame');
      const undo = el('button', 'tr-btn'); undo.type = 'button'; undo.innerHTML = ICONS.undo; undo.title = 'Undo'; undo.setAttribute('aria-label', 'Undo'); undo.addEventListener('click', () => ctx.undo());
      const redo = el('button', 'tr-btn'); redo.type = 'button'; redo.innerHTML = ICONS.redo; redo.title = 'Redo'; redo.setAttribute('aria-label', 'Redo'); redo.addEventListener('click', () => ctx.redo());
      const loopSlot = el('span', 'tr-loop');
      transport.append(tplay, tfr, undo, redo, loopSlot);
      tr = { play: tplay, fr: tfr, undo, redo, loopSlot };
    }
  }

  function render() {
    const keep = host.scrollTop;
    renderInner();
    if (keep && host.scrollTop !== keep) host.scrollTop = keep; // a rebuild must not lose the scroll position
  }
  function renderInner() {
    lanes.renderRuler(els.ruler);
    // canvas lane
    const lm = p.layout.mode;
    els.cbtn.textContent = lm === 'grow' || lm === 'shuffle' ? `${lm} · stage every ${(p.layout.stageMs / 1000).toFixed(1)}s`
      : lm === 'flat' ? 'flat · all on from 0s' : lm === 'deck' ? `deck · cut every ${(p.layout.deckHold / p.fps).toFixed(1)}s` : 'mirror · one gif, all slots';
    lanes.renderLane(els.clane, p.blocks.filter(b => b.target === 'canvas'), null, { extra: 28 });
    els.clane.appendChild(el('div', 'loop-end'));
    els.clane.appendChild(lanes.loopChip());
    // tile lane
    const t = p.tiles.find(t => t.id === ctx.state.selectedTileId);
    els.tlane.classList.toggle('sel', !!t); els.tlane.classList.toggle('empty-lane', !t); els.tlane.dataset.target = t ? t.id : '';
    if (!t) {
      els.tl.innerHTML = `<div class="lane-title">NO GIF SELECTED</div><div class="lane-sub">tap a gif on the canvas</div>`;
      els.tlane.innerHTML = `<div class="lane-empty-text">${ctx.isEmpty() ? 'add gifs to begin' : 'tap a gif on the canvas to see its strip'}</div>`;
    } else {
      const m = ctx.mediaOf(t);
      els.tl.innerHTML = '';
      const title = el('div', 'lane-title sel');
      const name = el('span'); name.textContent = ctx.tileLabel(t).toUpperCase();
      title.append(lanes.miniThumb(m), name, lanes.playGlyph(t)); els.tl.appendChild(title);
      const sub = el('div', 'lane-sub'); sub.textContent = `${t.playMode} · ${m?.srcFrames ?? '?'} frames, loops`; els.tl.appendChild(sub);
      lanes.renderLane(els.tlane, p.blocks.filter(b => b.target === t.id), m);
    }
    if (tr.loopSlot) { tr.loopSlot.innerHTML = ''; tr.loopSlot.appendChild(lanes.loopChip()); tr.undo.disabled = !p.canUndo; tr.redo.disabled = !p.canRedo; }
    renderFrame();
  }

  function renderFrame() {
    lanes.syncFrame(host);
    els.fr.textContent = lanes.frameText(); lanes.syncPlay(els.play);
    if (tr.fr) { tr.fr.textContent = lanes.frameText(); lanes.syncPlay(tr.play); }
  }

  // desktop: a hold on the canvas tab (or a gif) brings its lane into view and lights it for a moment
  function flashLane(target) {
    const lab = target === 'canvas' ? els.cl : els.tl, lane = target === 'canvas' ? els.clane : els.tlane; if (!lab) return;
    host.querySelectorAll('.flash').forEach(x => x.classList.remove('flash'));
    if (target === 'canvas') host.scrollTop = 0; else try { lab.scrollIntoView({ block: 'nearest' }); } catch {}
    clearTimeout(flashTimer);
    lab.classList.add('flash'); lane.classList.add('flash');
    flashTimer = setTimeout(() => { lab.classList.remove('flash'); lane.classList.remove('flash'); }, 900);
  }

  build();
  p.on('frame', renderFrame); p.on('play', renderFrame);
  new ResizeObserver(() => render()).observe(host);
  return { render, renderFrame, closeMenu: lanes.closeMenu, flashLane };
}
