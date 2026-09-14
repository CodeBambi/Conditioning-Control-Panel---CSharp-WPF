// One floating panel at a time (desktop: anchored to its HUD button; phone: bottom sheet). The phone's
// strip sheet mounts the same panel inline under a strip (`open(effect, { inline: hostEl })`).
// Rule 1: mode chips + at most two knobs. Rule 2: no time fields here. Driven by the table below.
import { ICONS } from './hud.js';

const SWATCHES = [['pink', '#FF69B4'], ['lavender', '#B8A6E8'], ['gold', '#F0C24B'], ['custom', 'linear-gradient(135deg,#3B2E5E,#C46A9E)']];
// a word on a picture wants plain white and plain black as well; a wash over the whole frame does not
const CAP_SWATCHES = [['pink', '#FF69B4'], ['lavender', '#B8A6E8'], ['gold', '#F0C24B'], ['white', '#FFFFFF'], ['black', '#101018'], ['custom', 'linear-gradient(135deg,#3B2E5E,#C46A9E)']];
export const PANELS = {
  tint: { modes: ['wash', 'creep'], colours: true, knobs: [['intensity', 'Intensity']],
    hint: { wash: 'A flat colour over everything under the block.', creep: 'Colour bleeds in from the corner and grows over the block.' } },
  spiral: { modes: ['over', 'through'], knobs: [['strength', 'Strength'], ['speed', 'Speed']],
    hint: { over: 'The spiral sits on top. Dice picks another arm style.', through: 'The spiral is a window. The tint colour shows through the arms.' } },
  glitch: { modes: ['ghost', 'double'], knobs: [['strength', 'Strength']], ghostPicks: true,
    hint: { ghost: 'Another gif from your strip, faded over the whole frame. The dice picks which one unless you pick one.', double: 'The same gif over itself, twice the size.' } },
  caption: { modes: ['text', 'window', 'flash'], text: true, fonts: true, colours: true, swatches: CAP_SWATCHES, knobs: [['glow', 'Glow'], ['size', 'Size']],
    hint: { text: 'The word sits on the whole canvas. Drag it where you want it. Its start and end are the block on the timeline.', window: 'The word is a cutout. The gif shows through the letters.', flash: 'One frame, full plate. Drag the pin on the timeline to choose when.' } },
  focus: { modes: ['ring', 'dark'], knobs: [['radius', 'Radius']],
    hint: { ring: 'Press and drag on the canvas to draw where the ring travels. Outside it blurs.', dark: 'Press and drag on the canvas to draw the path. Outside it goes dark.' } },
  layout: { modes: ['grow', 'flat', 'shuffle', 'mirror', 'stack', 'spread', 'slide', 'ripple', 'swallow', 'cover', 'tunnel', 'deal', 'carousel', 'hop', 'kenburns', 'pinwheel'], stage: true,
    hint: { grow: 'One becomes two, then all of them. Tiles slide in every stage.', flat: 'Everything on from the first frame.', shuffle: 'Tiles swap places on every stage. Same code, same shuffle.', mirror: 'The first gif in every slot, each one a stage deeper into its loop.', stack: 'They drop onto a pile one at a time, each at its own tilt. The pile never empties.', spread: 'One fills the frame, then it splits and the rest slide in. At the end the first one swallows it back.', slide: 'Rows drift past, every other row the other way. Nothing enters and nothing leaves.', ripple: 'A wave crosses the grid from one corner. Every tile puffs up as it passes.', swallow: 'One tile grows until it covers the rest, holds, then drops back into its slot.', cover: 'The first gif rides across the others at eight tenths size, once per loop.', deck: 'Flash Deck: one gif at a time, a cut every second. Pick a mode above to leave it.', tunnel: 'One gif nested inside itself, and the camera keeps pushing in. Two or three take turns on the rings.', deal: 'A deck in the corner, dealt out into a fan, then gathered back in reverse.', carousel: 'They ride a ring that turns towards you. Big and bright in front, small and dim at the back.', hop: 'Every tile hops one slot around the grid on each beat. A full turn and they are all home.', kenburns: 'Every tile pans and breathes inside its own gif. The grid holds still, the pictures do not.', pinwheel: 'One gif on every spoke, turning around a hub that breathes. It runs on a single gif.' } },
};
// the three system stacks, then the four that ship with the room (engine/fonts.js)
export const FONTS = [['display', 'Display', 'font-disp'], ['mono', 'Mono', 'font-mono'], ['hand', 'Hand', 'font-hand'],
  ['block', 'Block', 'font-block'], ['script', 'Script', 'font-script'], ['round', 'Round', 'font-round'], ['pixel', 'Pixel', 'font-pixel']];

export function initPanels(ctx) {
  const p = ctx.project;
  const host = document.getElementById('panel-host');
  let panel = null, current = null, ro = null; // current = { effect, blockId, target, inline }

  function findOrCreate(effect, target) {
    const had = p.blocks.some(b => b.effect === effect && b.target === target);
    const b = p.blockFor(effect, target);
    if (had) return b;
    if (effect === 'caption') {
      const other = p.blocks.find(x => x.effect === 'caption' && x.id !== b.id);
      const text = other?.params.text || p.captionText || '';
      if (text || other) p.updateBlock(b.id, { params: { text: text || b.params.text, font: other?.params.font || b.params.font } });
    }
    ctx.commit(`add ${effect}`);
    return b;
  }

  function open(effect, opts = {}) {
    close(true);
    if (!PANELS[effect]) return; // a shelved effect: the engine plays it, nothing here edits it
    let target = opts.target ?? ctx.currentTarget();
    if (effect === 'caption' && target !== 'canvas' && !opts.block) {
      // a word inside one tile of a mosaic is too small to read
      target = 'canvas'; ctx.selectTile(null); ctx.toast('Captions sit on the whole canvas');
    }
    let block = null;
    if (effect !== 'layout') { block = opts.block || findOrCreate(effect, target); ctx.state.selectedBlockId = block.id; }
    current = { effect, blockId: block?.id, target, inline: opts.inline || null };
    ctx.state.openPanel = effect;
    build();
    if (!current.inline) setTimeout(() => document.addEventListener('pointerdown', outside, true), 0);
    ctx.emit('state');
  }
  function close(silent) {
    if (!panel) return;
    panel.remove(); panel = null; current = null; ctx.state.openPanel = null;
    document.removeEventListener('pointerdown', outside, true);
    ro?.disconnect(); ro = null; document.documentElement.style.removeProperty('--sheet-h');
    host.classList.remove('open'); document.getElementById('app').classList.remove('panel-open');
    if (!silent) ctx.emit('state');
  }
  const outside = e => { if (!panel) return; if (panel.contains(e.target) || e.target.closest('.hud-btn')) return; close(); };

  function build() {
    const def = PANELS[current.effect]; const b = current.blockId ? p.blocks.find(x => x.id === current.blockId) : null;
    if (current.effect !== 'layout' && !b) { close(); return; }
    panel = document.createElement('div'); panel.className = 'panel'; panel.setAttribute('role', 'dialog'); panel.setAttribute('aria-label', `${current.effect} panel`);
    panel.innerHTML = '<div class="sheet-grab" aria-hidden="true"></div>';
    // head
    const head = el('div', 'panel-head'); const title = el('div', 'panel-title'); title.textContent = current.effect.toUpperCase(); head.appendChild(title);
    if (b) {
      const pill = el('button', 'target-pill'); pill.type = 'button';
      const tile = p.tiles.find(t => t.id === b.target);
      if (tile) { const m = ctx.mediaOf(tile); const mt = el('span', 'mini-thumb'); if (m?.thumb) { const cv = document.createElement('canvas'); cv.width = 44; cv.height = 32; cv.getContext('2d').drawImage(m.thumb, 0, 0, 44, 32); mt.appendChild(cv); } pill.append(mt, text(`painting ${ctx.tileLabel(tile)}`)); }
      else { pill.classList.add('canvas'); pill.append(text('painting canvas')); }
      const sel = p.tiles.find(t => t.id === ctx.state.selectedTileId);
      if (current.inline || current.effect === 'caption') {
        // inside a strip the target is the strip's; a caption is always the canvas
        pill.disabled = true; pill.classList.add('static');
        if (current.effect === 'caption') pill.title = 'Captions sit on the whole canvas';
      }
      else {
        pill.title = tile ? 'Tap to paint the whole canvas instead' : sel ? `Tap to paint only ${ctx.tileLabel(sel)}` : 'Select a gif on the canvas to paint just one';
        pill.addEventListener('click', () => { if (tile) open(current.effect, { target: 'canvas' }); else if (sel) open(current.effect, { target: sel.id }); else { if (ctx.isMobile()) close(); ctx.toast('Tap a gif on the canvas first to paint just that one.'); } });
      }
      head.appendChild(pill);
    } else {
      const pill = el('span', 'target-pill canvas'); pill.textContent = 'whole canvas'; head.appendChild(pill);
    }
    panel.appendChild(head);
    // mode chips
    const modes = el('div', 'chips fill'); modes.setAttribute('role', 'radiogroup');
    const curMode = b ? b.mode : p.layout.mode;
    for (const m of def.modes) { const c = chip(m, m === curMode); c.addEventListener('click', () => setMode(m)); modes.appendChild(c); }
    panel.appendChild(modes);
    if (current.effect === 'layout') {
      // flip: the same motion the other way round. Every mode has one, so the
      // row never moves. The gifs themselves are not mirrored.
      panel.appendChild(label('Direction'));
      const fr2 = el('div', 'chips fill');
      const fc = chip('flip', !!p.layout.flip); fc.classList.add('flip');
      fc.setAttribute('role', 'switch');
      fc.title = p.layout.flip ? 'Back to the way it came' : 'Send the motion the other way';
      fc.addEventListener('click', () => { ctx.toggleFlip(); rebuild(); });
      fr2.appendChild(fc); panel.appendChild(fr2);
      if (def.stage && (p.layout.mode === 'grow' || p.layout.mode === 'shuffle')) {
        panel.appendChild(label('Stage every'));
        const st = el('div', 'chips fill'); for (const ms of [400, 600, 900]) { const c = chip(`${(ms / 1000).toFixed(1)}s`, p.layout.stageMs === ms); c.addEventListener('click', () => { p.setLayout({ stageMs: ms }); ctx.commit('stage'); rebuild(); }); st.appendChild(c); } panel.appendChild(st);
      }
      if (ctx.isMobile()) {
        panel.appendChild(label('Frame'));
        const or = el('div', 'chips fill'); for (const o of ['landscape', 'portrait', 'square']) { const c = chip(o, p.orientation === o); c.addEventListener('click', () => { ctx.setOrientation(o); rebuild(); }); or.appendChild(c); } panel.appendChild(or);
        panel.appendChild(label('Length'));
        const ln = el('div', 'chips fill'); for (const s of [3, 5, 8]) { const c = chip(`${s}s`, Math.round(p.frames / p.fps) === s); c.addEventListener('click', () => { ctx.setLength(s); rebuild(); }); ln.appendChild(c); } panel.appendChild(ln);
      }
    }
    if (b && def.ghostPicks && b.mode === 'ghost') panel.appendChild(ghostRow(b));
    if (b && def.text) {
      const inp = document.createElement('input'); inp.type = 'text'; inp.className = 'text-field'; inp.maxLength = 40; inp.placeholder = 'say something'; inp.value = b.params.text || ''; inp.setAttribute('aria-label', 'Caption text'); inp.autocomplete = 'off'; inp.spellcheck = false;
      inp.addEventListener('input', () => { p.updateBlock(b.id, { params: { text: inp.value } }); p.setCaptionText(inp.value); });
      inp.addEventListener('change', () => ctx.commit('caption text'));
      inp.addEventListener('keydown', e => { if (e.key === 'Enter') { inp.blur(); } e.stopPropagation(); });
      inp.addEventListener('focus', () => setTimeout(() => { try { inp.scrollIntoView({ block: 'nearest' }); } catch {} }, 300));
      panel.appendChild(inp);
      const fr = el('div', 'chips fonts'); for (const [key, name, cls] of FONTS) { const c = chip(name, (b.params.font || 'display') === key); c.classList.add(cls); c.addEventListener('click', () => { p.updateBlock(b.id, { params: { font: key } }); ctx.commit('font'); rebuild(); }); fr.appendChild(c); } panel.appendChild(fr);
    }
    if (b && def.colours) {
      const row = el('div', 'chips swatches'); row.setAttribute('role', 'radiogroup'); row.setAttribute('aria-label', 'Colour');
      for (const [name, css] of (def.swatches || SWATCHES)) { const c = chip('', b.params.colour === name); c.classList.add('swatch'); c.title = name === 'custom' ? 'from the media colours' : name; c.setAttribute('aria-label', c.title); c.style.setProperty('--sw', css); c.innerHTML = '<i></i>'; c.addEventListener('click', () => { p.updateBlock(b.id, { params: { colour: name } }); ctx.commit('colour'); rebuild(); }); row.appendChild(c); }
      panel.appendChild(row);
    }
    if (b) for (const [key, name] of def.knobs) panel.appendChild(knob(b, key, name));
    // dice + remove row
    if (b) {
      const row = el('div', 'panel-row');
      const dice = el('button', 'panel-dice'); dice.type = 'button'; dice.innerHTML = ICONS.dice; dice.title = 'Reroll just this panel'; dice.setAttribute('aria-label', 'Reroll this effect');
      dice.addEventListener('click', () => { p.rerollBlock(b.id); ctx.commit('reroll'); rebuild(); ctx.toast(`${current.effect} rerolled`); });
      const rm = el('button', 'panel-remove'); rm.type = 'button'; rm.textContent = 'remove block'; rm.addEventListener('click', () => { close(); ctx.removeBlock(b.id); });
      row.append(dice, rm); panel.appendChild(row);
    }
    const hint = el('div', 'panel-hint'); hint.textContent = def.hint[curMode] || ''; panel.appendChild(hint);
    if (current.inline) { panel.classList.add('inline'); current.inline.innerHTML = ''; current.inline.appendChild(panel); return; }
    host.appendChild(panel); host.classList.add('open');
    position();
    if (ctx.isMobile()) {
      // phone: the stage above takes whatever the sheet leaves (its height rides on --sheet-h)
      document.getElementById('app').classList.add('panel-open');
      const size = () => { if (panel) document.documentElement.style.setProperty('--sheet-h', panel.offsetHeight + 'px'); };
      ro?.disconnect(); ro = new ResizeObserver(size); ro.observe(panel); size();
    }
  }
  const rebuild = () => { if (!current) return; const keep = current; panel?.remove(); panel = null; current = keep; build(); };

  function setMode(m) {
    const b = current.blockId ? p.blocks.find(x => x.id === current.blockId) : null;
    if (!b) { p.setLayout({ mode: m }); ctx.commit('layout'); rebuild(); return; }
    const patch = { mode: m };
    if (b.effect === 'caption') { if (m === 'flash') { const s = b.end - b.start <= 1 ? b.start : Math.round(p.frames * .62); patch.start = s; patch.end = s + 1; } else if (b.end - b.start <= 1) patch.end = Math.min(p.frames, b.start + 15); }
    p.updateBlock(b.id, patch); ctx.commit('mode'); rebuild();
  }
  /**
   * Which gif the ghost borrows. The dice chip leaves it to the seed; a thumb
   * pins it there, and a reroll no longer moves it. The gifs offered are the
   * ones the dice could reach: everything on the canvas but the one this
   * block is painting.
   */
  function ghostChoices(b) {
    const under = p.tiles.find(t => t.id === b.target);
    const onCanvas = new Set(p.tiles.map(t => t.mediaId));
    return p.media.filter(m => onCanvas.has(m.id) && m.id !== under?.mediaId);
  }
  function ghostRow(b) {
    const wrap = el('div'); wrap.appendChild(label('Ghost'));
    const list = ghostChoices(b);
    if (!list.length) { const n = el('div', 'field-note'); n.textContent = 'Put another gif on the canvas and the ghost can borrow it.'; wrap.appendChild(n); return wrap; }
    const row = el('div', 'ghost-picks'); row.setAttribute('role', 'radiogroup'); row.setAttribute('aria-label', 'Which gif the ghost borrows');
    const cur = b.params.source || 'dice';
    const pickIt = id => { p.updateBlock(b.id, { params: { source: id } }); ctx.commit('ghost source'); rebuild(); };
    const d = chip('dice', cur === 'dice'); d.classList.add('ghost-dice'); d.title = 'Let the seed pick';
    d.addEventListener('click', () => pickIt('dice')); row.appendChild(d);
    for (const m of list) {
      const t = el('button', 'thumb ghost-thumb'); t.type = 'button'; t.dataset.media = m.id;
      t.setAttribute('role', 'radio'); t.setAttribute('aria-checked', cur === m.id ? 'true' : 'false');
      t.classList.toggle('selected', cur === m.id);
      t.title = m.name; t.setAttribute('aria-label', `Ghost: ${m.name}`);
      if (m.thumb) { const cv = document.createElement('canvas'); cv.width = m.thumb.width; cv.height = m.thumb.height; cv.getContext('2d').drawImage(m.thumb, 0, 0); t.appendChild(cv); }
      t.addEventListener('click', () => pickIt(m.id)); row.appendChild(t);
    }
    wrap.appendChild(row); return wrap;
  }
  function knob(b, key, name) {
    const k = el('div', 'knob'); const head = el('div', 'knob-head'); const nm = el('span'); nm.textContent = name; const val = el('span', 'knob-val'); val.textContent = b.params[key] ?? 50; head.append(nm, val);
    const s = document.createElement('input'); s.type = 'range'; s.min = 0; s.max = 100; s.step = 1; s.value = b.params[key] ?? 50; s.className = 'slider'; s.setAttribute('aria-label', name);
    s.style.setProperty('--fill', s.value + '%'); if (b.effect === 'caption' || b.effect === 'tint') s.style.setProperty('--track', 'var(--pink)'); if (b.effect === 'focus') s.style.setProperty('--track', 'var(--gold)');
    s.addEventListener('input', () => { val.textContent = s.value; s.style.setProperty('--fill', s.value + '%'); p.updateBlock(b.id, { params: { [key]: +s.value } }); });
    s.addEventListener('change', () => ctx.commit(name.toLowerCase()));
    k.append(head, s); return k;
  }
  function position() {
    if (ctx.isMobile()) return;
    const btn = ctx.hudButton(current.effect); if (!btn) return;
    const hr = host.getBoundingClientRect(), br = btn.getBoundingClientRect();
    const right = btn.closest('.rail-r'); panel.classList.toggle('right', !!right);
    if (document.getElementById('app').dataset.hud === 'rows') {
      // rows above and below the stage: the panel hangs under its button, or sits over it from the bottom row
      panel.style.left = Math.max(8, Math.min(hr.width - panel.offsetWidth - 8, br.left - hr.left)) + 'px'; panel.style.right = '';
      panel.style.top = (right ? Math.max(8, hr.height - panel.offsetHeight - 8) : 8) + 'px';
      return;
    }
    const top = Math.max(8, Math.min(hr.height - panel.offsetHeight - 8, br.top - hr.top));
    panel.style.top = top + 'px'; if (right) { panel.style.right = '8px'; panel.style.left = ''; } else { panel.style.left = '8px'; panel.style.right = ''; }
  }
  const el = (t, c) => { const e = document.createElement(t); if (c) e.className = c; return e; };
  const text = s => document.createTextNode(s);
  const chip = (t, on) => { const c = el('button', 'chip' + (on ? ' on' : '')); c.type = 'button'; c.textContent = t; c.setAttribute('role', 'radio'); c.setAttribute('aria-checked', on ? 'true' : 'false'); return c; };
  const label = t => { const l = el('div', 'field-note'); l.textContent = t; l.style.marginTop = '0'; return l; };

  window.addEventListener('resize', () => { if (panel) position(); });
  return { open, close, rebuild, isOpen: () => !!panel, current: () => current, refresh: () => { if (!current) return; if (current.blockId && !p.blocks.find(b => b.id === current.blockId)) close(); else rebuild(); } };
}
