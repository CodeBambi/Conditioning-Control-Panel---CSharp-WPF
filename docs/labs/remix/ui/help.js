// Help: tooltips on the chrome and (?) cards with a live preview.
//
// Nothing here is wired at build time. Tooltips are resolved from the event target on the way up
// the tree, and the (?) buttons are put back by a MutationObserver, so the rest of the UI can
// re-render, re-anchor or re-shape itself as often as it likes and the help still lands.
// Hooks used, in order of preference: data-effect / data-hud, then aria-label, then the button's
// own words. Never an index.
import { HELP, TIPS, RULES } from './help-content.js';
import { createProject } from './engine-bridge.js';

const KEYS = Object.keys(HELP);
const HOVER_MS = 350;
const PRESS_MS = 500;
const SAMPLES = ['drift', 'pulse', 'spin'];

/* ------------------------------------------------------------------ style */
function injectCss() {
  if (document.querySelector('link[data-help-css]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = new URL('../help.css', import.meta.url).href;
  link.dataset.helpCss = '1';
  document.head.appendChild(link);
}

/* ------------------------------------------------------------- resolvers */
// Which card a HUD button belongs to. data-hud is the hook the HUD ships today; the aria-label
// and the printed label are the fallbacks if that markup moves.
export function hudKey(btn) {
  if (!btn) return null;
  const d = btn.dataset.effect || btn.dataset.hud || btn.dataset.panel;
  if (d && HELP[d]) return d;
  const label = (btn.textContent || '').trim().toLowerCase();
  if (HELP[label]) return label;
  if (label.startsWith('add')) return 'add';
  const aria = (btn.getAttribute('aria-label') || '').toLowerCase();
  for (const k of KEYS) if (aria.includes(k)) return k;
  return null;
}

// Which effect an open panel is editing.
export function panelKey(panel) {
  if (!panel) return null;
  const d = panel.dataset.effect || panel.dataset.panel;
  if (d && HELP[d]) return d;
  const aria = (panel.getAttribute('aria-label') || '').toLowerCase();
  for (const k of KEYS) if (aria.startsWith(k)) return k;
  const title = (panel.querySelector('.panel-title')?.textContent || '').trim().toLowerCase();
  if (HELP[title]) return title;
  return hudKey(document.querySelector('.hud-btn.active, .hud-btn[aria-pressed="true"]'));
}

const hit = (el, sel) => (el.closest ? el.closest(sel) : null);

// The one place that says what a control is. Returns a string or null.
export function tipFor(el) {
  if (!el || !el.closest) return null;
  const q = hit(el, '.help-q');
  if (q) return q.dataset.index ? TIPS.index : TIPS.help;

  const panel = hit(el, '.panel');
  if (panel) return panelTip(el, panel);

  // the Auto page
  if (hit(el, '#btn-roll')) return TIPS.roll;
  if (hit(el, '#roll-back')) return TIPS.rollback;
  if (hit(el, '#roll-fwd')) return TIPS.rollforward;
  if (hit(el, '#auto-next')) return TIPS.next;
  if (hit(el, '#caption-it')) return TIPS.captionit;
  if (hit(el, '#cap-skip')) return TIPS.skip;
  if (hit(el, '#cap-done')) return TIPS.done;
  if (hit(el, '#code-chip')) return TIPS.code;
  if (hit(el, '#code-type, #code-load, #code-field, #code-close')) return TIPS.codetype;
  if (hit(el, '#pick-gifs')) return TIPS.pickgifs;
  if (hit(el, '#pick-any')) return TIPS.pickany;
  if (hit(el, '#open-editor, #open-editor-add')) return TIPS.editor;
  if (hit(el, '#btn-back-auto')) return TIPS.backauto;
  if (hit(el, '#auto-orient')) return TIPS.orientation;
  if (hit(el, '#auto-length')) return TIPS.length;
  if (hit(el, '#auto-size')) return TIPS.size;
  if (hit(el, '#auto-pick')) return HELP.add.line;
  if (hit(el, '#cap-text')) return TIPS.captiontext;
  if (hit(el, '#cap-fonts .chip')) return TIPS.font;
  if (hit(el, '.door-notnow')) return TIPS.doornotnow;
  if (hit(el, '.door-after-line')) return TIPS.doorsafter;
  if (hit(el, '.door-dots')) return TIPS.doordots;
  if (hit(el, '.door')) return TIPS.doors;

  const hud = hit(el, '.hud-btn, [data-hud]');
  if (hud && !hud.classList.contains('hud-bin')) {
    const k = hudKey(hud);
    if (k) return HELP[k].line;
  }
  if (hit(el, '.hud-bin, #btn-bin')) return TIPS.bin;
  if (hit(el, '#btn-dice')) return HELP.dice.line;
  if (hit(el, '#btn-undo')) return TIPS.undo;
  if (hit(el, '#btn-redo')) return TIPS.redo;
  if (hit(el, '#btn-export')) return TIPS.export;
  if (hit(el, '#orient')) return TIPS.orientation;
  if (hit(el, '#length-chip')) return TIPS.length;
  if (hit(el, '#meter, #meter-label, .meter, .meter-label')) return TIPS.size;
  if (hit(el, '.stamp-btn, [data-stamp]')) return HELP.stamp.line;
  if (hit(el, '.thumb.add')) return HELP.add.line;
  if (hit(el, '.thumb[data-media]')) return TIPS.strip;
  if (hit(el, '.tl-play')) return TIPS.play;
  if (hit(el, '.ruler')) return TIPS.scrub;
  if (hit(el, '.loop-chip')) return HELP.loop.line;
  if (hit(el, '.play-glyph')) return TIPS.playmode;
  if (hit(el, '.corner-tab, [data-corner-tab], #corner-tab')) return TIPS.corner;
  if (hit(el, '#discord-safe') || (hit(el, '.tog') && document.querySelector('#discord-safe'))) return TIPS.discordSafe;
  return null;
}

// Chips and knobs read their words, so a re-render or a new mode keeps working.
function panelTip(el, panel) {
  const key = panelKey(panel);
  const H = key ? HELP[key] : null;
  if (hit(el, '.ghost-picks')) return TIPS.ghostpick;
  const chip = hit(el, '.chip');
  if (chip) {
    if (chip.classList.contains('flip')) return TIPS.flip;
    const word = (chip.textContent || '').trim().toLowerCase();
    if (H?.modes?.[word]) return H.modes[word];
    if (chip.classList.contains('swatch')) return H?.knobs?.colour || HELP.tint.knobs.colour;
    if (/^\d+\.\d+\s*s$/.test(word)) return HELP.layout.knobs.stage;
    if (/^\d+\s*s$/.test(word)) return TIPS.length;
    if (['landscape', 'portrait', 'square'].includes(word)) return TIPS.orientation;
    if (['display', 'mono', 'hand', 'block', 'script', 'round', 'pixel'].includes(word)) return TIPS.font;
    if (H?.knobs?.[word]) return H.knobs[word];
    return null;
  }
  const knob = hit(el, '.knob');
  if (knob && H?.knobs) {
    const name = (knob.querySelector('.knob-head span')?.textContent || el.getAttribute('aria-label') || '').trim().toLowerCase();
    if (H.knobs[name]) return H.knobs[name];
  }
  if (hit(el, '.panel-dice')) return TIPS.paneldice;
  if (hit(el, '.panel-remove')) return TIPS.removeblock;
  if (hit(el, '.text-field')) return TIPS.captiontext;
  return null;
}

/* -------------------------------------------------------------- tooltips */
// One element, moved around. Desktop only: a touch device never sees it.
function initTips(ctx) {
  let box = null, arrow = null, timer = 0, target = null, stashed = null;
  const fine = () => matchMedia('(pointer: fine)').matches && !ctx.isMobile();
  // Point at the control, not at the svg or span inside it.
  const anchorFor = el => (el.closest ? el.closest('button, input, a, .chip, .knob, .meter, .seg, .thumb, .ruler, [role="meter"]') : null) || el;

  function build() {
    if (box) return;
    box = document.createElement('div');
    box.className = 'help-tip';
    box.setAttribute('role', 'tooltip');
    arrow = document.createElement('i');
    box.append(document.createElement('span'), arrow);
    document.body.appendChild(box);
  }

  function place(el) {
    const r = el.getBoundingClientRect();
    box.style.left = '0px'; box.style.top = '0px';
    const w = box.offsetWidth, h = box.offsetHeight;
    let top = r.top - h - 10;
    const below = top < 8;
    if (below) top = r.bottom + 10;
    const left = Math.max(8, Math.min(innerWidth - w - 8, r.left + r.width / 2 - w / 2));
    box.style.left = `${Math.round(left)}px`;
    box.style.top = `${Math.round(top)}px`;
    box.classList.toggle('below', below);
    arrow.style.left = `${Math.round(Math.max(12, Math.min(w - 12, r.left + r.width / 2 - left)))}px`;
  }

  // The browser's own tip would sit under ours, so it steps aside while we are up.
  function show(el, text) {
    hide();
    build();
    target = el;
    box.firstChild.textContent = text;
    if (el.title) { stashed = [el, el.title]; el.removeAttribute('title'); }
    box.classList.add('on');
    place(el);
  }
  function hide() {
    clearTimeout(timer); timer = 0;
    if (stashed) { stashed[0].setAttribute('title', stashed[1]); stashed = null; }
    if (box) box.classList.remove('on');
    target = null;
  }

  function want(el, instant) {
    if (!fine() || !el) return;
    const host = anchorFor(el);
    if (host === target) return;
    const text = tipFor(el);
    if (!text) { hide(); return; }
    clearTimeout(timer);
    if (instant) show(host, text);
    else timer = setTimeout(() => show(host, text), HOVER_MS);
  }

  document.addEventListener('pointerover', e => {
    if (e.pointerType === 'touch') return;
    want(e.target, false);
  }, true);
  document.addEventListener('pointerout', e => {
    if (!target) return;
    if (e.relatedTarget && anchorFor(e.relatedTarget) === target) return;
    hide();
  }, true);
  document.addEventListener('focusin', e => {
    let vis = true;
    try { vis = e.target.matches(':focus-visible'); } catch { /* older engine: show it anyway */ }
    if (vis) want(e.target, true);
  }, true);
  document.addEventListener('focusout', hide, true);
  document.addEventListener('pointerdown', hide, true);
  addEventListener('keydown', e => { if (e.key === 'Escape') hide(); }, true);
  addEventListener('scroll', hide, true);
  addEventListener('wheel', hide, { capture: true, passive: true });
  addEventListener('resize', hide);
  addEventListener('blur', hide);

  return { hide, tipFor };
}

/* ------------------------------------------------------- preview project */
// A throwaway project per card: the bundled sample loops, two tiles (three for layout), the card's
// own effect over the whole length. It plays while the card is open and is disposed on close, so
// no rAF loop and no decoded frames outlive the card.
function previewRunner(ctx) {
  let blobs = null, project = null, block = null, off = null, gen = 0;

  // Fetched once for the life of the page. The decode is per card: the engine's dispose() lets go
  // of a project's frames, so two projects must not share one decoded clip.
  async function files() {
    if (!blobs) {
      blobs = [];
      for (const n of SAMPLES) {
        try {
          const res = await fetch(`assets/sample/${n}.gif`);
          if (res.ok) blobs.push(new File([await res.blob()], `${n}.gif`, { type: 'image/gif' }));
        } catch { /* a missing sample just means one tile fewer */ }
      }
    }
    return blobs;
  }

  async function start(key, canvas) {
    stop();
    const spec = HELP[key]?.preview;
    if (!spec || !canvas) return;
    const mine = ++gen;
    const list = await files();
    if (mine !== gen) return;
    const p = createProject({ orientation: ctx.project.orientation === 'portrait' ? 'portrait' : 'landscape' });
    const bail = () => { if (mine === gen) return false; try { p.dispose(); } catch { /* already gone */ } return true; };
    for (const f of list.slice(0, spec.layout ? 3 : 2)) {
      try {
        const m = await p.addMedia(f);
        if (bail()) return;
        p.addTile(m.id);
      } catch { /* skip a sample that will not decode */ }
    }
    if (bail()) return;
    p.setLayout(spec.layout ? { mode: spec.layout, stageMs: spec.stageMs || 600 } : { mode: 'flat' });
    let b = null;
    if (spec.effect) {
      try { b = p.addBlock({ effect: spec.effect, mode: spec.mode, target: 'canvas', start: 0, end: p.frames, params: spec.params || {} }); }
      catch { /* the card still shows the mosaic */ }
    }
    project = p; block = b;
    const c2d = canvas.getContext('2d');
    const draw = () => { try { p.drawFrame(c2d, p.frame, { w: canvas.width, h: canvas.height }); } catch { /* mid dispose */ } };
    off = p.on('frame', draw);
    draw();
    p.play();
  }

  function stop() {
    gen++;
    off?.(); off = null;
    if (project) { try { project.dispose(); } catch { /* already gone */ } }
    project = null; block = null;
  }

  // Chips on the card drive the preview. No commit: this project is thrown away.
  function setMode(mode) {
    if (!project) return;
    if (!block) { project.setLayout({ mode }); return; }
    const patch = { mode };
    if (block.effect === 'caption') {
      if (mode === 'flash') { patch.start = Math.round(project.frames * 0.62); patch.end = patch.start + 1; }
      else { patch.start = 0; patch.end = project.frames; }
    }
    try { block = project.updateBlock(block.id, patch) || block; } catch { /* keep the old mode */ }
  }
  const modeNow = () => (block ? block.mode : project?.layout.mode) || null;

  return { start, stop, setMode, modeNow, get project() { return project; }, get block() { return block; } };
}

/* ----------------------------------------------------------------- cards */
function initCards(ctx) {
  const preview = previewRunner(ctx);
  let veil = null, opener = null, shown = null;

  const el = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };
  const hudFor = key => document.querySelector(`.hud-btn[data-effect="${key}"], .hud-btn[data-hud="${key}"]`)
    || [...document.querySelectorAll('.hud-btn')].find(b => hudKey(b) === key) || null;

  function frame() {
    if (veil) return veil;
    veil = el('div', 'help-veil');
    veil.addEventListener('pointerdown', e => { if (e.target === veil) close(); });
    veil.addEventListener('keydown', onKey);
    document.body.appendChild(veil);
    return veil;
  }

  function onKey(e) {
    if (e.key === 'Escape') { e.stopPropagation(); close(); return; }
    if (e.key !== 'Tab') return;
    const stops = [...veil.querySelectorAll('button, [href], input, [tabindex]:not([tabindex="-1"])')].filter(x => x.offsetParent !== null);
    if (!stops.length) return;
    const first = stops[0], last = stops[stops.length - 1];
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  }

  function head(title, onBack) {
    const h = el('div', 'help-head');
    if (onBack) { const b = el('button', 'help-back', 'All cards'); b.type = 'button'; b.addEventListener('click', onBack); h.appendChild(b); }
    const t = el('h2', 'help-title', title); t.id = 'help-card-title'; h.appendChild(t);
    const x = el('button', 'help-close', 'Close'); x.type = 'button'; x.addEventListener('click', close); h.appendChild(x);
    return h;
  }

  function shell() {
    const v = frame();
    v.innerHTML = '';
    const card = el('div', 'help-card');
    card.setAttribute('role', 'dialog');
    card.setAttribute('aria-modal', 'true');
    card.setAttribute('aria-labelledby', 'help-card-title');
    v.appendChild(card);
    return card;
  }

  function open(key, from, backToIndex) {
    if (!HELP[key]) return;
    const H = HELP[key];
    preview.stop();
    if (!veil) opener = from || document.activeElement;
    shown = key;
    const card = shell();
    card.dataset.card = key;
    card.appendChild(head(H.title, backToIndex ? () => openIndex(null, true) : null));

    if (H.preview) {
      const box = el('div', 'help-prev');
      const cv = document.createElement('canvas');
      const portrait = ctx.project.orientation === 'portrait';
      cv.width = portrait ? 180 : 320; cv.height = portrait ? 320 : 180;
      cv.className = 'help-prev-canvas';
      cv.setAttribute('aria-label', `${H.title} on the sample loops`);
      box.appendChild(cv);
      box.appendChild(el('div', 'help-prev-note', 'live, on the sample loops'));
      card.appendChild(box);
      // the chips light up once the clips have decoded, a tick or two after the card opens
      preview.start(key, cv).then(markModes, () => {});
    }

    card.appendChild(el('p', 'help-lead', H.line));
    card.appendChild(el('p', 'help-body', H.body));

    const modes = Object.keys(H.modes || {}), knobs = Object.keys(H.knobs || {});
    if (modes.length || knobs.length) {
      const cols = el('div', 'help-cols');
      if (modes.length) cols.appendChild(list('Modes', modes.map(m => [m, H.modes[m]]), H.preview ? m => pickMode(m) : null));
      if (knobs.length) cols.appendChild(list('Knobs', knobs.map(k => [k, H.knobs[k]]), null));
      card.appendChild(cols);
    }

    const tip = el('div', 'help-tipbox');
    tip.append(el('span', 'help-tipmark', 'tip'), el('span', null, H.tip));
    card.appendChild(tip);

    const btn = hudFor(key);
    if (btn && !btn.disabled) {
      const act = el('div', 'help-actions');
      const go = el('button', 'help-try', key === 'add' ? 'Add gifs' : 'Try it');
      go.type = 'button';
      go.addEventListener('click', () => { close(); hudFor(key)?.click(); });
      act.appendChild(go);
      card.appendChild(act);
    }
    (card.querySelector('.help-close') || card).focus();
  }

  function list(name, rows, onPick) {
    const col = el('div', 'help-col');
    col.appendChild(el('h3', null, name));
    for (const [word, line] of rows) {
      const row = onPick ? el('button', 'help-row pick') : el('div', 'help-row');
      if (onPick) { row.type = 'button'; row.addEventListener('click', () => onPick(word)); }
      row.dataset.word = word;
      row.append(el('span', 'help-word', word), el('span', 'help-line', line));
      col.appendChild(row);
    }
    return col;
  }

  function pickMode(word) {
    preview.setMode(word);
    markModes();
  }
  function markModes() {
    const now = preview.modeNow();
    veil?.querySelectorAll('.help-row.pick').forEach(r => {
      const on = r.dataset.word === now;
      r.classList.toggle('on', on);
      r.setAttribute('aria-pressed', on ? 'true' : 'false');
    });
  }

  function openIndex(from, keepOpener) {
    preview.stop();
    if (!veil && !keepOpener) opener = from || document.activeElement;
    shown = 'index';
    const card = shell();
    card.dataset.card = 'index';
    card.classList.add('wide');
    card.appendChild(head('Every card', null));
    card.appendChild(el('p', 'help-lead', 'The roll, five effects, the canvas and the marks it carries. Tap one.'));
    const grid = el('div', 'help-grid');
    for (const k of KEYS) {
      const b = el('button', 'help-tile'); b.type = 'button'; b.dataset.card = k;
      b.append(el('span', 'help-tile-name', HELP[k].title), el('span', 'help-tile-line', HELP[k].line));
      b.addEventListener('click', () => open(k, null, true));
      grid.appendChild(b);
    }
    card.appendChild(grid);
    const foot = el('div', 'help-rules');
    foot.appendChild(el('h3', null, 'Three rules'));
    for (const [name, line] of RULES) {
      const r = el('div', 'help-rule');
      r.append(el('span', 'help-word', name), el('span', 'help-line', line));
      foot.appendChild(r);
    }
    card.appendChild(foot);
    (card.querySelector('.help-close') || card).focus();
  }

  function close() {
    if (!veil) return;
    preview.stop();
    veil.remove(); veil = null; shown = null;
    const back = opener; opener = null;
    if (back && document.contains(back)) { try { back.focus(); } catch { /* gone */ } }
  }

  addEventListener('pagehide', close);

  return { open, openIndex, close, isOpen: () => !!veil, showing: () => shown, preview };
}

/* --------------------------------------------- (?) buttons and the presses */
// The marks are the only DOM this file puts inside somebody else's markup, so they are re-applied
// after every mutation. decorate() is idempotent: a second pass adds nothing and the loop ends.
function initMarks(ctx, cards) {
  function mark(host, opts) {
    if (!host || host.querySelector(':scope > .help-q')) return;
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'help-q' + (opts.small ? ' small' : '');
    b.textContent = '?';
    b.setAttribute('aria-label', opts.label);
    b.setAttribute('aria-haspopup', 'dialog');
    if (opts.index) b.dataset.index = '1';
    if (opts.key) b.dataset.card = opts.key;
    b.addEventListener('click', e => {
      e.preventDefault(); e.stopPropagation();
      opts.index ? cards.openIndex(b) : cards.open(opts.key, b);
    });
    b.addEventListener('pointerdown', e => e.stopPropagation());
    if (opts.after && opts.after.parentNode === host) host.insertBefore(b, opts.after.nextSibling);
    else host.appendChild(b);
  }

  function decorate() {
    for (const panel of document.querySelectorAll('#panel-host .panel, .panel')) {
      const key = panelKey(panel);
      if (key) mark(panel.querySelector('.panel-head') || panel, { key, label: `What ${key} does` });
    }
    const top = document.querySelector('.top-left');
    const word = top?.querySelector('.wordmark');
    if (top) mark(top, { index: true, label: 'Help: every card', after: word });
    const autoTop = document.querySelector('#auto .auto-top');
    if (autoTop) mark(autoTop, { key: 'auto', label: 'What Roll does', after: autoTop.querySelector('.wordmark') });
    const vibes = document.querySelector('#vibes .vibes-title');
    if (vibes) mark(vibes, { key: 'timeline', small: true, label: 'What a vibe fills in' });
  }

  let queued = false;
  const obs = new MutationObserver(() => {
    if (queued) return;
    queued = true;
    requestAnimationFrame(() => { queued = false; decorate(); });
  });
  obs.observe(document.body, { childList: true, subtree: true });
  ctx.on('state', decorate);
  decorate();
  return { decorate };
}

// Touch: hold a HUD button for half a second for its card, and the panel does not open.
// Desktop: right click does the same.
function initPresses(ctx, cards) {
  let timer = 0, from = null, fired = false;
  const cancel = () => { clearTimeout(timer); timer = 0; from = null; };

  document.addEventListener('pointerdown', e => {
    if (e.pointerType !== 'touch') return;
    const btn = e.target.closest?.('.hud-btn');
    const key = btn && hudKey(btn);
    if (!key) return;
    from = { x: e.clientX, y: e.clientY, btn };
    fired = false;
    clearTimeout(timer);
    timer = setTimeout(() => { fired = true; timer = 0; cards.open(key, btn); }, PRESS_MS);
  }, true);
  document.addEventListener('pointermove', e => {
    if (from && Math.hypot(e.clientX - from.x, e.clientY - from.y) > 12) cancel();
  }, true);
  document.addEventListener('pointerup', cancel, true);
  document.addEventListener('pointercancel', cancel, true);
  // the tap that ends the long press must not also open the panel
  document.addEventListener('click', e => {
    if (!fired) return;
    fired = false;
    if (e.target.closest?.('.hud-btn')) { e.preventDefault(); e.stopPropagation(); }
  }, true);
  document.addEventListener('contextmenu', e => {
    const btn = e.target.closest?.('.hud-btn');
    const key = btn && hudKey(btn);
    if (!key) return;
    e.preventDefault();
    cards.open(key, btn);
  });
}

/* ------------------------------------------------------------------ boot */
export function initHelp(ctx) {
  injectCss();
  const tips = initTips(ctx);
  const cards = initCards(ctx);
  initMarks(ctx, cards);
  initPresses(ctx, cards);
  if (new URLSearchParams(location.search).get('debug') === '1') window.__remixHelp = { tips, cards, tipFor };
  return { tips, cards };
}
