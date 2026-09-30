/* ============================================================================
 * shared/niches/picker.js - the picture picker and niche manager, one widget
 * for every game (2026-09-30, Piece by Piece first).
 *
 * Breakout's two surfaces, made into one component so the games stop growing
 * their own copies:
 *   mode 'first'   the first-start card. Five flavour tiles (Mine never here, as
 *                  in Breakout), each showing the niches inside; a tap selects one
 *                  and opens its niches right there, and Play starts. A plain grey
 *                  "No online pictures" choice starts at once. The pick is saved
 *                  by the host and the card is never shown again.
 *   mode 'manage'  the niche manager: Own, the app's (desktop, when on), the five
 *                  flavours and Mine as tabs; every niche a pill, bright on and dim
 *                  off; ONE r/ field to add (never a comma list); added niches
 *                  carry an x. Edits are local until the caller commits them
 *                  (applied once, when the manager closes: each change is a refetch).
 *
 * The component never talks to a host and never stores anything: state in
 * (shared/niches/choice.js readChoice), state out through onChange / onChoose.
 * Its CSS is injected once, scoped under .np-*, and takes the host's colours from
 * --np-* custom properties when a page sets them.
 *
 *   createNichePicker({ mode, state, onChange(state), onChoose(state), doc })
 *     -> { el, set(state), value(), status(text), focus(), dispose() }
 * ==========================================================================*/

import {
  FLAVOURS, MINE, flavourById, nichesOf, liveSubs, toggleNiche, addNiche, removeNiche,
  choose, editNiches, pickOf, nicheLine, ownLine,
} from './choice.js';

const CSS = `
.np { --np-ink: #f4e8ff; --np-dim: #bfb0d0; --np-faint: #8f8199; --np-card: #1a1024; --np-pill: #24152e;
  --np-on: #4a2757; --np-edge: #55405f; --np-pink: #ff91c9; --np-gold: #ffe9aa;
  display: flex; flex-direction: column; gap: 10px; min-width: 0; color: var(--np-ink); font: 14px/1.4 system-ui, "Segoe UI", sans-serif; text-align: left; }
.np[hidden], .np [hidden] { display: none !important; }
.np button { font: inherit; cursor: pointer; touch-action: manipulation; }
.np button:focus-visible, .np input:focus-visible { outline: 3px solid var(--np-gold); outline-offset: 3px; }
.np-head { text-align: center; }
.np-head h2 { margin: 0; font: 900 clamp(26px, 5vw, 44px)/1 "Arial Black", "Trebuchet MS", sans-serif; letter-spacing: 1px; color: #fff0dc; }
.np-head p { margin: 10px 0 4px; color: var(--np-dim); font-size: 13px; letter-spacing: .6px; }
.np-grid { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 10px; }
.np-tile { display: flex; flex-direction: column; align-items: center; justify-content: flex-end; gap: 5px; min-height: 132px; min-width: 0;
  padding: 14px 10px 12px; border: 1px solid color-mix(in srgb, var(--tint) 55%, #0000); border-radius: 12px; text-align: center;
  background: linear-gradient(180deg, color-mix(in srgb, var(--tint) 30%, var(--np-card)), var(--np-card) 72%); color: #fff0dc;
  box-shadow: 0 4px 0 #0a0710, 0 8px 24px color-mix(in srgb, var(--tint) 14%, #0000); transition: transform .15s, border-color .15s; }
.np-tile::before { content: ""; width: 30px; height: 30px; margin-bottom: auto; border-radius: 50%;
  background: radial-gradient(circle at 35% 30%, #fff, var(--tint) 45%, color-mix(in srgb, var(--tint) 40%, #000)); box-shadow: 0 0 16px color-mix(in srgb, var(--tint) 50%, #0000); }
.np-tile b { font: 800 17px/1.1 system-ui, "Segoe UI", sans-serif; }
.np-tile span { color: #c9b8cf; font-size: 12px; line-height: 1.3; }
.np-tile small { color: var(--np-faint); font-size: 9.5px; line-height: 1.35; overflow-wrap: anywhere; }
.np-tile:hover { transform: translateY(-3px); border-color: var(--tint); }
.np-tile[aria-pressed="true"] { border: 2px solid var(--tint); transform: translateY(-3px); }
.np-tile[aria-pressed="true"] b::after { content: " \\2713"; color: var(--tint); }
.np.still .np-tile, .np.still .np-tile:hover { transition: none; transform: none; }
.np-tabs { display: flex; flex-wrap: wrap; gap: 6px; }
.np-tab { padding: 6px 12px; border-radius: 16px; border: 1px solid color-mix(in srgb, var(--tint, #8a7a9c) 45%, #0000); background: var(--np-pill); color: #cbb6d4; font-size: 12.5px; font-weight: 600; }
.np-tab[aria-pressed="true"] { background: color-mix(in srgb, var(--tint, #8a7a9c) 26%, var(--np-pill)); border-color: var(--tint, #8a7a9c); color: #fff; }
.np-edit { display: flex; flex-direction: column; gap: 8px; padding: 10px; border: 1px solid #6b4a78; border-radius: 12px; background: #150d1d99; }
.np-edit-h { margin: 0; color: var(--np-dim); font-size: 12px; letter-spacing: .5px; }
.np-pills { display: flex; flex-wrap: wrap; gap: 6px; }
.np-pill { display: inline-flex; max-width: 100%; border: 1px solid var(--np-edge); border-radius: 16px; overflow: hidden; opacity: .55; }
.np-pill.is-on { opacity: 1; border-color: #eb88c9; }
.np-pill button { padding: 5px 10px; border: 0; border-radius: 0; background: var(--np-pill); color: #cbb6d4; font-size: 12px; overflow-wrap: anywhere; text-align: left; }
.np-pill.is-on button { background: var(--np-on); color: #fff; }
.np-pill .np-x { border-left: 1px solid var(--np-edge); padding: 5px 9px; font-size: 14px; line-height: 1; }
.np-add { display: flex; align-items: center; gap: 6px; }
.np-add > span { color: #cbb6d4; font-size: 15px; }
.np-add input { flex: 1; width: auto; min-width: 0; height: 30px; padding: 0 9px; border: 1px solid #9565a8; border-radius: 8px; background: var(--np-pill); color: #fff; font: 14px system-ui, sans-serif; }
.np-add button { padding: 6px 14px; border: 0; border-radius: 8px; background: #3a2f5e; color: var(--np-ink); font-size: 13px; }
.np-note, .np-status { margin: 0; font-size: 11.5px; line-height: 1.45; color: #a894b3; min-height: 1em; }
.np-status:empty { display: none; }
.np .np-play { align-self: center; min-width: min(100%, 260px); padding: 12px 22px; border: 0; border-radius: 12px; background: var(--np-pink); color: #1a1a3e; font-weight: 800; font-size: 16px; box-shadow: 0 4px 0 #8c3f6a; }
.np .np-play:hover { filter: brightness(1.08); }
.np .np-play:disabled { opacity: .45; cursor: default; filter: none; }
.np-foot { display: flex; flex-wrap: wrap; justify-content: center; gap: 8px; }
.np-keep { display: flex; flex-direction: column; align-items: center; gap: 2px; max-width: 100%; padding: 8px 16px; border: 1px solid #4a4a52; border-radius: 10px; background: #1b1b20; color: #9a9aa3; filter: grayscale(1); }
.np-keep b { font: 700 12px/1.2 system-ui, sans-serif; letter-spacing: 1.5px; text-transform: uppercase; }
.np-keep span { font-size: 10.5px; color: #77777f; }
.np-keep:hover { color: #d6d6dc; border-color: #8a8a94; }
.np-foot-note { margin: 0; text-align: center; color: var(--np-faint); font-size: 11px; letter-spacing: .4px; }
@media (max-width: 760px) { .np-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } .np-tile { min-height: 104px; }
  .np-grid .np-tile:last-child { grid-column: 1 / -1; justify-self: center; width: calc(50% - 5px); } }
@media (max-height: 520px) { .np-grid { grid-template-columns: repeat(5, minmax(0, 1fr)); } .np-tile { min-height: 84px; padding: 8px 6px; }
  .np-tile small, .np-tile::before { display: none; } .np-head p { margin: 4px 0 0; } }
`;

function injectCss(doc) {
  if (!doc || doc.getElementById('np-style')) return;
  const s = doc.createElement('style');
  s.id = 'np-style';
  s.textContent = CSS;
  (doc.head || doc.documentElement).appendChild(s);
}

const TABS = (state) => [
  { id: 'own', name: 'Own', tint: '#c9c9d2' },
  ...(state && state.appWide ? [{ id: 'app', name: 'The app\'s', tint: '#9fd0ff' }] : []),
  ...FLAVOURS.map((f) => ({ id: f.id, name: f.name, tint: f.tint })),
  { id: MINE.id, name: MINE.name, tint: MINE.tint },
];

export function createNichePicker(opts = {}) {
  const doc = opts.doc || (typeof document !== 'undefined' ? document : null);
  if (!doc) return { el: null, set() {}, value: () => null, status() {}, focus() {}, dispose() {} };
  injectCss(doc);
  const mode = opts.mode === 'first' ? 'first' : 'manage';
  const onChange = typeof opts.onChange === 'function' ? opts.onChange : () => {};
  const onChoose = typeof opts.onChoose === 'function' ? opts.onChoose : () => {};
  let state = opts.state || null;
  // The flavour whose niches are open. 'first' opens nothing until a tile is tapped.
  let open = mode === 'first' ? '' : (flavourById(pickOf(state)) ? pickOf(state) : '');
  let note = '';
  let statusText = '';

  const h = (tag, cls, text) => { const e = doc.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };
  const el = h('div', 'np np-' + mode);
  if (opts.still) el.classList.add('still');

  const head = h('div', 'np-head');
  const title = h('h2', null, opts.title || 'Pick your pictures');
  const lead = h('p', null, opts.line || 'Distraction mixes them into the game. Change them any time from Pictures.');
  head.append(title, lead);
  const grid = h('div', 'np-grid');
  grid.setAttribute('role', 'group'); grid.setAttribute('aria-label', 'Flavours');
  const tabs = h('div', 'np-tabs');
  tabs.setAttribute('role', 'group'); tabs.setAttribute('aria-label', 'Pictures');
  const edit = h('div', 'np-edit');
  const editH = h('p', 'np-edit-h');
  const pills = h('div', 'np-pills');
  const form = h('form', 'np-add');
  form.append(h('span', null, 'r/'));
  const input = h('input');
  Object.assign(input, { type: 'text', maxLength: 60, autocomplete: 'off', spellcheck: false, placeholder: 'add a niche' });
  input.setAttribute('aria-label', 'Add a niche');
  input.setAttribute('autocapitalize', 'none');
  const addBtn = h('button', null, 'Add'); addBtn.type = 'submit';
  form.append(input, addBtn);
  const noteEl = h('p', 'np-note'); noteEl.setAttribute('aria-live', 'polite');
  edit.append(editH, pills, form, noteEl);
  const play = h('button', 'np-play'); play.type = 'button';
  const foot = h('div', 'np-foot');
  const own = h('button', 'np-keep'); own.type = 'button'; own.dataset.pick = 'own';
  own.append(h('b', null, 'No online pictures'), h('span'));
  const app = h('button', 'np-keep'); app.type = 'button'; app.dataset.pick = 'app';
  app.append(h('b', null, 'Keep the app\'s pictures'), h('span', null, 'Your Scrolller niches from the app.'));
  foot.append(own, app);
  const footNote = h('p', 'np-foot-note', 'You can change this any time from Pictures.');
  const statusEl = h('p', 'np-status'); statusEl.setAttribute('role', 'status');

  if (mode === 'first') el.append(head, grid, edit, play, foot, footNote);
  else el.append(tabs, edit, statusEl);

  const change = (s, send) => { state = s; if (send) onChange(state); paint(); };

  function tile(f) {
    const b = h('button', 'np-tile');
    b.type = 'button'; b.dataset.pick = f.id;
    b.style.setProperty('--tint', f.tint);
    b.append(h('b', null, f.name), h('span', null, f.line), h('small'));
    return b;
  }

  function paintPills() {
    const f = flavourById(open);
    edit.hidden = !f || (!!state && state.canOnline === false);
    pills.textContent = '';
    if (!f) return;
    editH.textContent = mode === 'first' ? `Inside ${f.name}` : (f.id === MINE.id ? 'Your own niches' : `Inside ${f.name}`);
    const edits = (state && state.custom || {})[f.id];
    for (const n of nichesOf(f, edits)) {
      const pill = h('span', 'np-pill' + (n.on ? ' is-on' : ''));
      const tog = h('button', null, 'r/' + n.name);
      tog.type = 'button'; tog.dataset.niche = n.name;
      tog.setAttribute('aria-pressed', String(n.on));
      tog.title = n.on ? 'On. Click to switch it off.' : n.kind === 'extra' ? 'A suggestion. Click to switch it on.' : 'Off. Click to switch it on.';
      pill.append(tog);
      if (n.kind === 'added') {
        const x = h('button', 'np-x', String.fromCharCode(0xd7));
        x.type = 'button'; x.dataset.remove = n.name;
        x.setAttribute('aria-label', 'Remove r/' + n.name);
        pill.append(x);
      }
      pills.append(pill);
    }
    const live = liveSubs(f, edits).length;
    noteEl.textContent = note || (live ? `${live} on. Bright ones are on, dim ones are off.`
      : f.id === MINE.id ? 'Empty. Add a niche below.' : 'All switched off. Switch one on, or add one.');
  }

  function paint() {
    if (!state) { el.hidden = true; return; }
    el.hidden = false;
    const can = state.canOnline !== false;
    const pick = pickOf(state);
    if (mode === 'first') {
      grid.textContent = '';
      grid.hidden = !can;
      for (const f of FLAVOURS) {
        const b = tile(f);
        b.setAttribute('aria-pressed', String(open === f.id));
        b.querySelector('small').textContent = nicheLine(state, f.id);
        grid.append(b);
      }
      const f = flavourById(open);
      play.hidden = !f;
      play.textContent = f ? `Play with ${f.name}` : 'Play';
      play.disabled = !!f && liveSubs(f, (state.custom || {})[f.id]).length === 0;
      own.querySelector('span').textContent = ownLine(state);
      app.hidden = !state.appWide || !can;
      lead.textContent = can ? (opts.line || 'Distraction mixes them into the game. Change them any time from Pictures.')
        : 'Online pictures are not available here. The game uses what this device has.';
    } else {
      tabs.textContent = '';
      for (const t of TABS(state)) {
        if (!can && t.id !== 'own') continue;
        const b = h('button', 'np-tab', t.name);
        b.type = 'button'; b.dataset.pick = t.id;
        b.style.setProperty('--tint', t.tint);
        b.setAttribute('aria-pressed', String(t.id === pick));
        if (t.id === 'own') b.title = ownLine(state);
        tabs.append(b);
      }
      open = flavourById(pick) ? pick : '';
      statusEl.textContent = statusText;
    }
    paintPills();
  }

  function onClick(e) {
    const t = e.target;
    const rem = t.closest('[data-remove]');
    const tog = t.closest('[data-niche]');
    const pick = t.closest('[data-pick]');
    if (rem) { const r = editNiches(state, open, (f, c) => removeNiche(c, rem.dataset.remove)); note = ''; change(r.state, mode === 'manage'); return; }
    if (tog) { const r = editNiches(state, open, (f, c) => toggleNiche(f, c, tog.dataset.niche)); note = ''; change(r.state, mode === 'manage'); return; }
    if (t.closest('.np-play')) { if (flavourById(open)) onChoose(choose(state, open)); return; }
    if (!pick) return;
    const id = pick.dataset.pick;
    note = '';
    if (mode === 'first') {
      if (id === 'own' || id === 'app') { onChoose(choose(state, id)); return; }
      open = id;
      paint();
      try { play.focus({ preventScroll: true }); play.scrollIntoView({ block: 'nearest', behavior: opts.still ? 'auto' : 'smooth' }); } catch { /* fine */ }
      return;
    }
    change(choose(state, id), true);
  }

  function onSubmit(e) {
    e.preventDefault();
    if (!flavourById(open)) return;
    const typed = input.value;
    const r = editNiches(state, open, (f, c) => addNiche(f, c, typed));
    note = r.error;
    if (!r.error) input.value = '';
    change(r.state, mode === 'manage');
    try { input.focus({ preventScroll: true }); } catch { /* fine */ }
  }

  // Typing a niche must not reach the game's own keys (arrows, Space, letters); Escape and Enter pass.
  const stopKeys = (e) => { if (e.key !== 'Escape' && e.key !== 'Enter') e.stopPropagation(); };

  el.addEventListener('click', onClick);
  form.addEventListener('submit', onSubmit);
  input.addEventListener('keydown', stopKeys);
  input.addEventListener('keyup', stopKeys);
  input.addEventListener('keypress', stopKeys);
  paint();

  return {
    el,
    /** A new state from the host (or the caller): repaint, keeping the open flavour where it still makes sense. */
    set(s) { state = s || null; if (mode === 'manage') open = ''; paint(); },
    value: () => state,
    status(text) { statusText = String(text || ''); if (mode === 'manage') statusEl.textContent = statusText; },
    focus() {
      const first = mode === 'first' ? (grid.hidden ? own : grid.querySelector('[aria-pressed="true"]') || grid.querySelector('button'))
        : tabs.querySelector('[aria-pressed="true"]') || tabs.querySelector('button');
      try { first && first.focus({ preventScroll: true }); } catch { /* fine */ }
    },
    dispose() {
      el.removeEventListener('click', onClick);
      form.removeEventListener('submit', onSubmit);
      input.removeEventListener('keydown', stopKeys);
      input.removeEventListener('keyup', stopKeys);
      input.removeEventListener('keypress', stopKeys);
      el.remove();
    },
  };
}
