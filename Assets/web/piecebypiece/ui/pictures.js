/* ============================================================================
 * ui/pictures.js - "Pictures" in the game's Options (2026-09-28).
 *
 * Where Distraction's pictures come from: the player's own library, or one of
 * the Breakout / Goon Game flavours (a short list of Scrolller niches) mixed
 * in. The flavour table and the niche editing are the Goon Game's
 * ui/flavours.js, imported, not copied: that file is already the host-owned
 * port of breakout/flavours.js and carries the drift test against it. The chess
 * tree is desktop-only (no site vendors it), so the cross-tree import resolves
 * through ccp.game like the loom and rng imports already do.
 *
 * The HOST owns persistence and consent (PieceByPieceHostService.Media.cs):
 *   host -> page  pbp:media-state  { flavour, last, custom, online, appWide }
 *   host -> page  pbp:online-media { state, have, want, ... }   (status line only)
 *   page -> host  pbp:media-flavour { flavour, custom, subs, online }
 * A picked flavour is this window's online opt-in; "Own pictures" is online:false.
 * Nothing here touches localStorage.
 * ==========================================================================*/

import { FLAVOURS, MINE, flavourById, nichesOf, toggleNiche, addNiche, removeNiche,
  readMediaInit, mediaFlavourFrame } from '../../goon/ui/flavours.js';
import { onHostMessage, postToHost } from '../bridge.js';

const TILES = [...FLAVOURS, MINE];
const ADD_ERRORS = { bad: 'Letters, numbers and _ only.', dup: 'Already in.', full: 'That is plenty. Remove one first.' };

/** The frame this state sends, typed for the chess host. Pure. */
export function picturesFrame(state) {
  return { ...mediaFlavourFrame(state), type: 'pbp:media-flavour' };
}

/** The status line for an online-media frame. Pure. */
export function statusLine(state, status) {
  if (!state || state.online === false) return 'Your own pictures only.';
  if (!state.flavour && !state.appWide) return 'Pick one to mix in online pictures.';
  const s = status || {};
  if (s.state === 'loading') return s.have > 0 ? `Finding pictures, ${s.have} so far.` : 'Finding pictures.';
  if (s.state === 'ready') return `${s.have} online pictures in the mix.`;
  if (s.state === 'empty') return 'Nothing found there. Your own pictures carry on.';
  if (s.state === 'error') return 'Scrolller did not answer. Your own pictures carry on.';
  if (s.state === 'off' && state.flavour) return 'No niches on. Add one under Niches.';
  return '';
}

export function attachPictures(root) {
  if (!root || typeof document === 'undefined') return { setVisible() {}, dispose() {} };
  const $ = (id) => root.querySelector('#' + id);
  const tiles = $('flavour-pick'), pills = $('niche-pills'), box = $('niche-box');
  const form = $('niche-add'), input = $('niche-input'), note = $('niche-note'), status = $('pictures-status');
  let state = null;        // null until the host speaks: a plain browser shows nothing
  let online = null;       // last pbp:online-media
  let sent = '';           // the last frame sent, so a no-op close sends nothing

  const current = () => (state && state.online !== false ? flavourById(state.flavour) : null);

  function send() {
    if (!state) return;
    const frame = picturesFrame(state);
    const key = JSON.stringify(frame);
    if (key === sent) return;
    sent = key;
    postToHost(frame);
  }

  function tile(id, name, line, tint, pressed, onClick) {
    const b = document.createElement('button');
    b.type = 'button'; b.className = 'hud-btn flavour-tile';
    b.setAttribute('aria-pressed', String(pressed));
    if (tint) b.style.setProperty('--tint', tint);
    b.dataset.flavour = id;
    b.innerHTML = '<span></span><small></small>';
    b.firstChild.textContent = name; b.lastChild.textContent = line;
    b.addEventListener('click', onClick);
    return b;
  }

  function paint() {
    root.hidden = !state || root.dataset.off === '1';
    if (!state) return;
    const own = state.online === false;
    tiles.textContent = '';
    tiles.appendChild(tile('own', 'Own pictures', 'Your library only.', null, own, () => {
      state = { ...state, online: false, flavour: '' };
      send(); paint();
    }));
    for (const f of TILES) {
      tiles.appendChild(tile(f.id, f.name, f.line, f.tint, !own && state.flavour === f.id, () => {
        state = { ...state, online: true, flavour: f.id, last: f.id };
        send(); paint();
      }));
    }
    const f = current();
    box.hidden = !f;
    pills.textContent = '';
    if (f) {
      for (const n of nichesOf(f, state.custom[f.id])) {
        const p = document.createElement('button');
        p.type = 'button'; p.className = 'niche-pill' + (n.on ? ' is-on' : '');
        p.setAttribute('aria-pressed', String(n.on));
        p.textContent = 'r/' + n.name;
        p.title = n.kind === 'added' ? 'Right-click to remove' : '';
        p.addEventListener('click', () => { edit(c => toggleNiche(f, c, n.name)); });
        if (n.kind === 'added') p.addEventListener('contextmenu', (e) => { e.preventDefault(); edit(c => removeNiche(c, n.name)); });
        pills.appendChild(p);
      }
    }
    status.textContent = statusLine({ ...state, flavour: own ? '' : state.flavour }, online);
  }

  /** Niche edits wait for the box (or the options panel) to close: one fetch, not one per tap. */
  function edit(fn) {
    const f = current();
    if (!f) return;
    state = { ...state, custom: { ...state.custom, [f.id]: fn(state.custom[f.id]) } };
    paint();
  }

  const onSubmit = (e) => {
    e.preventDefault();
    const f = current();
    if (!f) return;
    const r = addNiche(f, state.custom[f.id], input.value);
    note.hidden = !r.error; note.textContent = ADD_ERRORS[r.error] || '';
    if (!r.error) input.value = '';
    state = { ...state, custom: { ...state.custom, [f.id]: r.custom } };
    paint();
  };
  form.addEventListener('submit', onSubmit);
  // typing r/ must not reach the board's own keys (Esc, arrows)
  const stopKeys = (e) => { if (e.key !== 'Enter' && e.key !== 'Escape') e.stopPropagation(); };
  input.addEventListener('keydown', stopKeys);
  const onToggle = () => { if (!box.open && state && state.flavour) send(); };
  box.addEventListener('toggle', onToggle);

  // the options panel closing flushes pending niche edits too
  const panel = root.closest('.game-options-panel');
  let watcher = null;
  if (panel && typeof MutationObserver === 'function') {
    watcher = new MutationObserver(() => { if (panel.hidden && state && state.flavour) send(); });
    watcher.observe(panel, { attributes: true, attributeFilter: ['hidden'] });
  }

  const off = onHostMessage((m) => {
    if (m.type === 'pbp:media-state') {
      const init = readMediaInit(m);
      if (!init) return;
      state = { ...init, last: typeof m.last === 'string' ? m.last : init.flavour, appWide: !!m.appWide };
      // the host already holds this state: sending it back would only restart the fetch
      sent = JSON.stringify(picturesFrame(state));
      paint();
    } else if (m.type === 'pbp:online-media') {
      online = { state: m.state, have: Number(m.have) || 0, want: Number(m.want) || 0 };
      if (state) status.textContent = statusLine(state, online);
    }
  });

  return {
    /** Distraction only: Classic has no pictures to choose. */
    setVisible(on) { root.dataset.off = on ? '0' : '1'; root.hidden = !state || !on; },
    dispose() {
      off();
      form.removeEventListener('submit', onSubmit);
      input.removeEventListener('keydown', stopKeys);
      box.removeEventListener('toggle', onToggle);
      if (watcher) watcher.disconnect();
    },
  };
}
