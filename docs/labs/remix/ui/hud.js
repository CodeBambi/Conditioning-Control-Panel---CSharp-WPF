// The eight HUD buttons and where they sit. The buttons go where the canvas has the least use for the
// pixels: a portrait canvas gets two columns of four on the sides of the stage, a landscape or square
// canvas two rows of four above and below it. Phone: that rule as is (`cols` / `rows`). Desktop: the
// mockup's side rails (`rails`) for landscape and square; a portrait canvas takes the rows only when the
// rails would leave it a smaller stage than the rows would (both are measured, the larger stage wins).
export const ICONS = {
  add: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><path d="M12 5v14"/><path d="M5 12h14"/></svg>',
  bin: '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 7h16"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M6 7l1 13h10l1-13"/><path d="M9 7V4h6v3"/></svg>',
  drain: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 5h16"/><path d="M6 5v6a6 6 0 0 0 12 0V5"/><path d="M9 17v3"/><path d="M14 17v4"/><path d="M12 17v2"/></svg>',
  tint: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3s6 7 6 11a6 6 0 0 1-12 0c0-4 6-11 6-11z"/></svg>',
  spiral: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" aria-hidden="true"><path d="M12 12a2 2 0 0 1 2 2 3 3 0 0 1-3 3 5 5 0 0 1-5-5 7 7 0 0 1 7-7 9 9 0 0 1 9 9"/></svg>',
  glitch: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" aria-hidden="true"><path d="M3 7h8"/><path d="M14 7h7"/><path d="M3 12h4"/><path d="M10 12h11"/><path d="M3 17h12"/><path d="M18 17h3"/></svg>',
  caption: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 6h14"/><path d="M12 6v13"/><path d="M8 19h8"/></svg>',
  focus: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="12" r="4"/><path d="M12 2v3"/><path d="M12 19v3"/><path d="M2 12h3"/><path d="M19 12h3"/></svg>',
  layout: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><rect x="3" y="3" width="8" height="18" rx="1.5"/><rect x="13" y="3" width="8" height="8" rx="1.5"/><rect x="13" y="13" width="8" height="8" rx="1.5"/></svg>',
  x: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12"/><path d="M18 6 6 18"/></svg>',
  play: '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M6 4l14 8-14 8z"/></svg>',
  pause: '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><rect x="5" y="4" width="5" height="16" rx="1"/><rect x="14" y="4" width="5" height="16" rx="1"/></svg>',
  loop: '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M17 2l4 4-4 4"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><path d="M7 22l-4-4 4-4"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/></svg>',
  dice: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="4"/><circle cx="8" cy="8" r="1.4" fill="currentColor"/><circle cx="16" cy="8" r="1.4" fill="currentColor"/><circle cx="12" cy="12" r="1.4" fill="currentColor"/><circle cx="8" cy="16" r="1.4" fill="currentColor"/><circle cx="16" cy="16" r="1.4" fill="currentColor"/></svg>',
  chev: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>',
  undo: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 14 4 9l5-5"/><path d="M4 9h10a6 6 0 0 1 0 12h-3"/></svg>',
  canvas: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 10V4h6"/><path d="M20 14v6h-6"/></svg>',
  redo: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m15 14 5-5-5-5"/><path d="M20 9H10a6 6 0 0 0 0 12h3"/></svg>',
};

// Drain is shelved: the engine still plays a drain block, no surface offers one.
const LEFT = [['add', 'Add gif'], ['tint', 'Tint'], ['spiral', 'Spiral'], ['glitch', 'Glitch']];
const RIGHT = [['caption', 'Caption'], ['focus', 'Focus'], ['layout', 'Layout']];
export const EFFECTS = ['tint', 'spiral', 'glitch', 'caption', 'focus'];

export function initHud(ctx) {
  const btns = {};
  const build = (host, items) => {
    host.innerHTML = '';
    for (const [key, label] of items) {
      const wrap = document.createElement('div'); wrap.className = 'hud-wrap';
      const b = document.createElement('button'); b.type = 'button'; b.className = 'hud-btn'; b.dataset.hud = key;
      b.style.setProperty('--c', `var(--c-${key})`);
      b.innerHTML = `${ICONS[key]}<span>${label}</span>`;
      b.setAttribute('aria-label', key === 'add' ? 'Add gifs, pictures or videos' : key === 'layout' ? 'Layout' : `${label} effect`);
      wrap.appendChild(b); btns[key] = b;
      if (key === 'add') {
        const bin = document.createElement('button'); bin.type = 'button'; bin.className = 'hud-bin'; bin.id = 'btn-bin';
        bin.title = 'Bin: tap, then tap a gif to remove it'; bin.setAttribute('aria-label', 'Remove a gif'); bin.setAttribute('aria-pressed', 'false');
        bin.innerHTML = ICONS.bin; wrap.appendChild(bin); btns.bin = bin;
        bin.addEventListener('click', () => ctx.setBin(!ctx.state.binMode));
      }
      b.addEventListener('click', () => {
        if (key === 'add') ctx.pickFiles();
        else ctx.togglePanel(key);
      });
      host.appendChild(wrap);
    }
  };
  build(document.getElementById('hud-left'), LEFT);
  build(document.getElementById('hud-right'), RIGHT);

  // ---- placement
  const RAIL_W = 132, ROW_H = 80, PAD_W = 48, PAD_H = 24, STRIP_H = 58;
  let placed = null, swapTimer = 0;
  const stageFor = (w, h) => { const { w: cw, h: ch } = ctx.project.size; const s = Math.max(0, Math.min(w / cw, h / ch)); return { w: Math.floor(cw * s), h: Math.floor(ch * s) }; };
  function place() {
    const app = document.getElementById('app'); const p = ctx.project; let mode, sizes = null;
    if (ctx.isMobile()) mode = p.orientation === 'portrait' ? 'cols' : 'rows';
    else if (p.orientation !== 'portrait') mode = 'rails';
    else {
      const work = document.getElementById('work'); const W = work.clientWidth, H = work.clientHeight;
      const strip = document.getElementById('media-strip').hidden ? 0 : STRIP_H;
      const rails = stageFor(W - 2 * RAIL_W - PAD_W, H - PAD_H - strip), rows = stageFor(W - PAD_W, H - 2 * ROW_H - PAD_H - strip);
      sizes = { rails, rows };
      mode = rows.w * rows.h > rails.w * rails.h ? 'rows' : 'rails';
    }
    placed = { mode, sizes };
    if (app.dataset.hud === mode) return mode;
    const was = app.dataset.hud; app.dataset.hud = mode;
    if (was) { // a short crossfade of the buttons (reduced motion cuts it to nothing via the global rule)
      const work = document.getElementById('work'); work.classList.remove('hud-swap'); void work.offsetWidth; work.classList.add('hud-swap');
      clearTimeout(swapTimer); swapTimer = setTimeout(() => work.classList.remove('hud-swap'), 260);
    }
    return mode;
  }

  function render() {
    const p = ctx.project; const hasTiles = !ctx.isEmpty();
    const target = ctx.currentTarget();
    for (const key of [...EFFECTS, 'layout']) {
      const b = btns[key];
      b.classList.toggle('active', ctx.state.openPanel === key);
      b.disabled = !hasTiles;
      b.classList.toggle('has-block', key !== 'layout' && p.blocks.some(x => x.effect === key && x.target === target));
      b.setAttribute('aria-pressed', ctx.state.openPanel === key ? 'true' : 'false');
    }
    btns.bin.disabled = p.media.length === 0 || ctx.state.sample;
    btns.bin.classList.toggle('active', ctx.state.binMode);
    btns.bin.setAttribute('aria-pressed', ctx.state.binMode ? 'true' : 'false');
    btns.add.disabled = p.media.length >= 8;
    btns.add.title = p.media.length >= 8 ? '8 is the limit. Remove one to add another.' : 'Add gifs, pictures or videos';
  }
  return { render, button: key => btns[key], place, placement: () => placed };
}
