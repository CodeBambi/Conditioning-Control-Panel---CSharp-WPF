// Phone strip sheet. The timeline is hidden on phones; hold a gif (or the canvas tab) and its strip
// rises on a bottom sheet over a dimmed page: ruler + scrub, filmstrip + blocks + play glyph (a gif)
// or blocks + loop marker + loop chip (the canvas). Tap a block and its knobs open under the strip,
// inside the same sheet (panels.js renders them inline). Tap outside, the x, or Esc to close.
import { ICONS } from './hud.js';
import { initLanes, el } from './timeline.js';

export function initStripSheet(ctx, panels) {
  const p = ctx.project;
  const app = document.getElementById('app');
  const lanes = initLanes(ctx, { onBlock: b => openKnobs(b) });
  let host = null, els = {}, target = null, ro = null;

  function open(t) {
    if (!ctx.isMobile()) return false;
    if (t !== 'canvas' && !p.tiles.some(x => x.id === t)) return false;
    if (host && target !== t) closeKnobs();
    if (!host) build();
    target = t;
    if (t === 'canvas') { if (ctx.state.selectedTileId) ctx.selectTile(null); }
    else if (ctx.state.selectedTileId !== t) ctx.selectTile(t);
    render();
    return true;
  }
  function close() {
    if (!host) return;
    closeKnobs(); lanes.closeMenu();
    ro?.disconnect(); ro = null;
    host.remove(); host = null; els = {}; target = null;
    app.classList.remove('strip-open'); document.documentElement.style.removeProperty('--sheet-h');
    document.removeEventListener('pointerdown', outside, true);
  }
  // anything outside the sheet closes it; the transport row, the glyph menu and a toast's undo stay live
  const outside = e => { if (!host) return; if (els.sheet.contains(e.target) || e.target.closest('.transport, .glyph-menu, .toast-host')) return; close(); };

  function build() {
    host = el('div', 'strip-host'); host.setAttribute('role', 'dialog'); host.setAttribute('aria-label', 'Strip');
    const sheet = el('div', 'strip-sheet'); sheet.innerHTML = '<div class="sheet-grab" aria-hidden="true"></div>';
    const head = el('div', 'strip-head');
    const title = el('div', 'strip-title');
    const slot = el('span', 'strip-slot');
    const spacer = el('span', 'strip-spacer');
    const play = lanes.playButton('strip-play');
    const x = el('button', 'strip-close'); x.type = 'button'; x.innerHTML = ICONS.x.replace('width="11" height="11"', 'width="14" height="14"'); x.setAttribute('aria-label', 'Close the strip'); x.addEventListener('click', close);
    head.append(title, slot, spacer, play, x);
    const ruler = el('div', 'ruler'); ruler.setAttribute('aria-label', 'Drag to scrub'); lanes.bindScrub(ruler);
    const lane = el('div', 'lane'); lanes.bindScrub(lane);
    const knobs = el('div', 'strip-knobs');
    const hint = el('div', 'strip-hint');
    sheet.append(head, ruler, lane, knobs, hint);
    host.appendChild(sheet); document.body.appendChild(host);
    els = { sheet, title, slot, play, ruler, lane, knobs, hint };
    app.classList.add('strip-open');
    // the stage above takes whatever the sheet leaves: its height rides on --sheet-h
    const size = () => document.documentElement.style.setProperty('--sheet-h', sheet.offsetHeight + 'px');
    ro = new ResizeObserver(size); ro.observe(sheet); size();
    setTimeout(() => document.addEventListener('pointerdown', outside, true), 0);
  }

  function render() {
    if (!host) return;
    const t = target === 'canvas' ? null : p.tiles.find(x => x.id === target);
    if (target !== 'canvas' && !t) { close(); return; }
    // the sheet follows the selection: tap another gif on the stage and its strip comes up
    if (t && ctx.state.selectedTileId && ctx.state.selectedTileId !== t.id) { target = ctx.state.selectedTileId; closeKnobs(); return render(); }
    lanes.renderRuler(els.ruler);
    els.title.innerHTML = ''; els.slot.innerHTML = '';
    els.lane.classList.toggle('sel', !!t); els.lane.dataset.target = target;
    if (t) {
      const m = ctx.mediaOf(t);
      const name = el('span'); name.textContent = ctx.tileLabel(t).toUpperCase();
      els.title.classList.remove('canvas'); els.title.append(lanes.miniThumb(m), name);
      els.slot.appendChild(lanes.playGlyph(t));
      lanes.renderLane(els.lane, p.blocks.filter(b => b.target === t.id), m);
      els.hint.textContent = p.blocks.some(b => b.target === t.id) ? 'tap a block for its knobs. drag it to move, pull an edge to trim.' : `no effects on ${ctx.tileLabel(t)} yet. pick one from the buttons.`;
    } else {
      els.title.classList.add('canvas'); els.title.innerHTML = `${ICONS.canvas}<span>CANVAS</span>`;
      els.slot.appendChild(lanes.loopChip());
      lanes.renderLane(els.lane, p.blocks.filter(b => b.target === 'canvas'), null);
      els.lane.appendChild(el('div', 'loop-end'));
      els.hint.textContent = p.blocks.some(b => b.target === 'canvas') ? 'tap a block for its knobs. drag it to move, pull an edge to trim.' : 'no effects on the whole canvas yet. pick one from the buttons.';
    }
    const cur = panels.current();
    if (cur?.inline && cur.blockId && !p.blocks.some(b => b.id === cur.blockId)) closeKnobs();
    renderFrame();
  }
  function renderFrame() { if (!host) return; lanes.syncFrame(els.sheet); lanes.syncPlay(els.play); }

  function openKnobs(b) {
    if (!host) return;
    panels.open(b.effect, { target: b.target, block: b, inline: els.knobs });
    requestAnimationFrame(() => { try { els.knobs.scrollIntoView({ block: 'nearest', behavior: 'smooth' }); } catch {} });
  }
  function closeKnobs() { if (panels.current()?.inline) panels.close(); }

  p.on('frame', renderFrame); p.on('play', renderFrame);
  return { open, close, render, isOpen: () => !!host, target: () => target };
}
