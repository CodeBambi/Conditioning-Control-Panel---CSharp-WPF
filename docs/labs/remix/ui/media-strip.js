// Media strip under the canvas: thumbs, dashed add tile, count chip, expandable list with per-item remove.
import { ICONS } from './hud.js';

export function initMediaStrip(ctx) {
  const p = ctx.project;
  const host = document.getElementById('media-strip');
  let listEl = null;

  const tileOf = m => p.tiles.find(t => t.mediaId === m.id);
  const thumbEl = (m, small) => {
    const b = document.createElement('button'); b.type = 'button'; b.className = 'thumb'; b.dataset.media = m.id;
    if (m.thumb) { const cv = document.createElement('canvas'); cv.width = m.thumb.width; cv.height = m.thumb.height; cv.getContext('2d').drawImage(m.thumb, 0, 0); b.appendChild(cv); }
    if (!small && m.kind !== 'gif') { const k = document.createElement('span'); k.className = 'thumb-kind'; k.textContent = m.kind === 'video' ? 'vid' : m.kind === 'spiral' ? 'spiral' : 'still'; b.appendChild(k); }
    return b;
  };

  function render() {
    host.innerHTML = '';
    if (p.media.length === 0 || ctx.state.sample) { host.hidden = true; closeList(); return; }
    host.hidden = false;
    const thumbs = document.createElement('div'); thumbs.className = 'strip-thumbs';
    for (const m of p.media) {
      const t = tileOf(m); const b = thumbEl(m);
      b.classList.toggle('selected', !!t && t.id === ctx.state.selectedTileId);
      b.classList.toggle('off', !t);
      b.title = ctx.state.binMode ? `Remove ${m.name}` : t ? `${ctx.tileLabel(t)} · ${m.name}` : `${m.name}: tap to put it on the canvas`;
      b.setAttribute('aria-label', b.title);
      bindThumb(b, m);
      thumbs.appendChild(b);
    }
    if (p.media.length < 8) {
      const add = document.createElement('button'); add.type = 'button'; add.className = 'thumb add'; add.innerHTML = ICONS.add.replace('<svg', '<svg width="18" height="18"');
      add.title = 'Add more'; add.setAttribute('aria-label', 'Add more gifs'); add.addEventListener('click', () => ctx.pickFiles()); thumbs.appendChild(add);
    }
    host.appendChild(thumbs);
    const sp = document.createElement('div'); sp.className = 'strip-spacer'; host.appendChild(sp);
    const count = document.createElement('button'); count.type = 'button'; count.className = 'strip-count'; count.setAttribute('aria-expanded', listEl ? 'true' : 'false');
    const on = p.tiles.length; count.innerHTML = `<span>${ctx.isMobile() ? `${p.media.length} MEDIA` : `${p.media.length} MEDIA · ${on} ON CANVAS`}</span>${ICONS.chev}`; count.title = 'Open the media list';
    count.addEventListener('click', () => listEl ? closeList() : openList());
    host.appendChild(count);
    if (listEl) renderList();
  }

  function bindThumb(b, m) {
    let start = null, dragging = false, over = null;
    b.addEventListener('pointerdown', e => { if (e.button) return; start = { x: e.clientX, y: e.clientY }; b.setPointerCapture(e.pointerId); });
    b.addEventListener('pointermove', e => {
      if (!start) return;
      if (!dragging && Math.hypot(e.clientX - start.x, e.clientY - start.y) > 8 && tileOf(m) && !ctx.state.binMode) { dragging = true; b.classList.add('dragging'); }
      if (!dragging) return;
      const hit = document.elementFromPoint(e.clientX, e.clientY)?.closest?.('.thumb[data-media]');
      if (over && over !== hit) over.classList.remove('drop-target');
      over = hit && hit !== b && tileOf(p.media.find(x => x.id === hit.dataset.media)) ? hit : null; over?.classList.add('drop-target');
    });
    const end = e => {
      if (!start) return; start = null;
      if (dragging) {
        dragging = false; b.classList.remove('dragging');
        if (over) { const tm = p.media.find(x => x.id === over.dataset.media); over.classList.remove('drop-target'); const to = p.tiles.findIndex(t => t.mediaId === tm.id); p.moveTile(tileOf(m).id, to); ctx.commit('reorder'); }
        over = null; return;
      }
      if (e.type === 'pointercancel') return;
      if (ctx.state.binMode) { ctx.removeMedia(m.id); return; }
      const t = tileOf(m);
      if (t) ctx.selectTile(t.id);
      else if (p.tiles.length >= 8) ctx.toast('8 tiles is the limit. Take one off first.', { gold: true });
      else { const nt = p.addTile(m.id); ctx.commit('add tile'); ctx.selectTile(nt.id); ctx.toast(`${m.name} is on the canvas`); }
    };
    b.addEventListener('pointerup', end); b.addEventListener('pointercancel', end);
  }

  function openList() { if (listEl) return; listEl = document.createElement('div'); listEl.className = 'strip-list'; listEl.setAttribute('role', 'dialog'); listEl.setAttribute('aria-label', 'Media list'); document.getElementById('centre').appendChild(listEl); renderList(); setTimeout(() => document.addEventListener('pointerdown', outside, true), 0); render(); }
  function closeList() { if (!listEl) return; listEl.remove(); listEl = null; document.removeEventListener('pointerdown', outside, true); const c = host.querySelector('.strip-count'); c?.setAttribute('aria-expanded', 'false'); }
  const outside = e => { if (listEl && !listEl.contains(e.target) && !e.target.closest('.strip-count')) closeList(); };
  function renderList() {
    listEl.innerHTML = '';
    for (const m of p.media) {
      const t = tileOf(m); const row = document.createElement('div'); row.className = 'strip-row';
      row.appendChild(thumbEl(m, true));
      const name = document.createElement('div'); name.className = 'strip-row-name'; name.textContent = m.name; name.title = m.name; row.appendChild(name);
      const meta = document.createElement('div'); meta.className = 'strip-row-meta'; meta.textContent = `${m.kind} · ${m.srcFrames} f · ${m.w}x${m.h}`; row.appendChild(meta);
      const tog = document.createElement('button'); tog.type = 'button'; tog.className = 'strip-row-btn'; tog.textContent = t ? 'take off' : 'put on';
      tog.addEventListener('click', () => { if (t) { ctx.removeTile(t.id); } else if (p.tiles.length < 8) { const nt = p.addTile(m.id); ctx.commit('add tile'); ctx.selectTile(nt.id); } else ctx.toast('8 tiles is the limit. Take one off first.', { gold: true }); });
      row.appendChild(tog);
      const rm = document.createElement('button'); rm.type = 'button'; rm.className = 'strip-row-btn danger'; rm.textContent = 'remove'; rm.setAttribute('aria-label', `Remove ${m.name}`);
      rm.addEventListener('click', () => ctx.removeMedia(m.id)); row.appendChild(rm);
      listEl.appendChild(row);
    }
  }
  return { render, closeList, isOpen: () => !!listEl };
}
