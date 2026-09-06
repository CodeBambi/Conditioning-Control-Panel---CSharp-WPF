/* ============================================================================
 * chart/maker/pick.js - picking, sliding, walls, undo (MAKER.md, PR M3).
 *
 * The one gesture the whole tool is built on: pick something, then drag it or
 * nudge it, and everything picked slides together. Shift picks every one like
 * it, so "every flash is a beat early" is one drag rather than forty. The only
 * thing that ever says no is the min gap: a picked bubble never lands closer
 * than that to one that stays put, so a pick pushed into its neighbour stops
 * instead of stacking on top of it.
 * ==========================================================================*/

import { EFFECTS, KINDS, RECIPES, alike, clamp, newId, recipeFor, slide } from './model.js';

const $ = (id) => document.getElementById(id);
const UNDO_MAX = 50;
const NUDGE = 0.1, NUDGE_BIG = 1;

let api = null, S = null;
let undoStack = [], redoStack = [], lastPush = 0;
let drag = null, fxFor = null, gridMode = 'wall', gridSet = null;

/* ---- undo ---------------------------------------------------------------- */

const shot = () => JSON.stringify({ bubs: S.bubs, hits: S.hits, cfg: S.cfg, minGap: S.minGap });

/** A snapshot, unless the last one is fresh and from the same gesture (held arrow keys). */
export function pushUndo(coalesce = '') {
  const now = Date.now();
  if (coalesce && coalesce === lastPush.tag && now - lastPush.at < 500) return;
  lastPush = { tag: coalesce, at: now };
  undoStack.push(shot());
  if (undoStack.length > UNDO_MAX) undoStack.shift();
  redoStack.length = 0;
}
export function dropUndo() { undoStack.pop(); }

function restore(json) {
  const o = JSON.parse(json);
  S.bubs = o.bubs; S.hits = o.hits; S.cfg = o.cfg; S.minGap = o.minGap;
  const live = new Set([...S.bubs.map((b) => b.id), ...S.hits.map((h) => h.id)]);
  for (const id of [...S.sel]) if (!live.has(id)) S.sel.delete(id);
  api.setGap(S.minGap);
  api.renderPanel();
  api.render();
}
export function undo() {
  if (!undoStack.length) return api.status('nothing to undo');
  redoStack.push(shot());
  restore(undoStack.pop());
  api.status('undone');
}
export function redo() {
  if (!redoStack.length) return api.status('nothing to redo');
  undoStack.push(shot());
  restore(redoStack.pop());
  api.status('back again');
}

/* ---- the grid (walls, and recipes) --------------------------------------- */

function openGrid(anchor, mode, setId) {
  const box = $('fx');
  gridMode = mode; gridSet = setId || null;
  $('fxtitle').textContent = mode === 'wall' ? 'what the wall does' : 'what ' + (S.setById.get(setId) || {}).name + ' gets';
  const grid = $('fxgrid');
  const f = document.createDocumentFragment();
  const cur = mode === 'wall' ? (fxFor ? fxFor.eff : null) : recipeFor(S.cfg, setId).id;
  const items = mode === 'wall'
    ? Object.entries(EFFECTS).map(([id, [gl, name, hint]]) => ({ id, gl, name, hint, seq: null }))
    : RECIPES.map((r) => ({ id: r.id, gl: '', name: r.name, hint: 'every ' + Math.max(S.minGap, r.gap).toFixed(2).replace(/0$/, '') + ' s', seq: r.seq }));
  for (const it of items) {
    const t = document.createElement('div');
    t.className = 'tile' + (it.id === cur ? ' on' : '');
    t.dataset.pickId = it.id;
    if (it.seq) t.append(api.beads({ seq: it.seq }));
    else t.append(Object.assign(document.createElement('span'), { className: 'gl', textContent: it.gl }));
    t.append(document.createTextNode(it.name));
    t.append(Object.assign(document.createElement('small'), { textContent: it.hint }));
    t.addEventListener('click', () => choose(it.id));
    f.append(t);
  }
  grid.replaceChildren(f);
  box.hidden = false;
  const r = anchor.getBoundingClientRect(), host = $('lines').getBoundingClientRect();
  box.style.left = clamp(r.left - host.left - 160, 8, Math.max(8, host.width - 390)) + 'px';
  box.style.top = Math.min(host.height - 40, r.bottom - host.top + 8) + 'px';
}
const closeGrid = () => { $('fx').hidden = true; };

function choose(id) {
  if (gridMode === 'wall') {
    pushUndo();
    let n = 0;
    for (const b of S.bubs) if (b.kind === 'wall' && (S.sel.has(b.id) || b === fxFor)) { b.eff = id; n++; }
    closeGrid();
    api.render();
    api.status(n > 1 ? n + ' walls set to ' + EFFECTS[id][1] : 'wall set to ' + EFFECTS[id][1]);
    return;
  }
  pushUndo();
  S.cfg[gridSet].on = true;
  S.cfg[gridSet].recipe = id;
  api.replaceSet(gridSet);
  closeGrid();
  api.renderPanel();
  api.render();
  const name = (S.setById.get(gridSet) || {}).name || gridSet;
  api.status(name + ' is on ' + id + ' now. every one of them was placed again, hand moved ones too.');
}

/** The panel's `change` link: the same popover, showing the six recipes. */
export function openRecipes(anchor, setId) { fxFor = null; openGrid(anchor, 'recipe', setId); }

/* ---- walls --------------------------------------------------------------- */

export function addWall() {
  if (!S.durationSec) return api.status('pick an mp3 first');
  pushUndo();
  const b = { id: newId('b'), t: api.time(), kind: 'wall', eff: 'melt', group: null, trig: null };
  S.bubs.push(b);
  S.bubs.sort((x, y) => x.t - y.t);
  S.sel.clear();
  S.sel.add(b.id);
  fxFor = b;
  api.render();
  const el = document.querySelector('[data-id="' + b.id + '"]') || $('playhead');
  openGrid(el, 'wall');
  api.status('a wall at the playhead. tap what it does.');
}

/* ---- picking and sliding ------------------------------------------------- */

function idsFor(el, shift) {
  if (el.classList.contains('band')) {
    const g = el.dataset.group;
    const first = S.bubs.find((b) => b.group === g);
    if (shift && first) return S.bubs.filter((b) => b.trig === first.trig).map((b) => b.id);
    return S.bubs.filter((b) => b.group === g).map((b) => b.id);
  }
  return shift ? alike(S, el.dataset.id) : [el.dataset.id];
}

function onDown(ev) {
  if (ev.button !== 0) return;
  if (ev.target.closest('#fx')) return;
  closeGrid();
  const el = ev.target.closest('.bub, .tag, .band');
  if (!el) {
    if (!ev.target.closest('#audio, #ruler') && ev.target.closest('.row')) { S.sel.clear(); api.render(); api.bar(); }
    return;
  }
  const ids = idsFor(el, ev.shiftKey);
  const wasOnly = S.sel.size === 1 && S.sel.has(el.dataset.id);
  if (ev.ctrlKey || ev.metaKey) for (const id of ids) (S.sel.has(id) ? S.sel.delete(id) : S.sel.add(id));
  else if (!ids.every((id) => S.sel.has(id))) { S.sel.clear(); for (const id of ids) S.sel.add(id); }
  if (el.classList.contains('wall') && wasOnly && !ev.shiftKey) {
    fxFor = S.bubs.find((b) => b.id === el.dataset.id);
    api.render();
    openGrid(document.querySelector('[data-id="' + el.dataset.id + '"]') || el, 'wall');
    return;
  }
  pushUndo();
  drag = { x: ev.clientX, moved: 0 };
  api.render();
  api.bar();
}

function onMove(ev) {
  if (!drag) return;
  const dx = ev.clientX - drag.x;
  drag.x = ev.clientX;
  drag.moved += Math.abs(dx);
  if (!dx) return;
  if (slide(S, dx / api.pps())) api.render();
}

function onUp() {
  if (drag && drag.moved < 2) dropUndo();     // a click is not an edit
  drag = null;
}

function nudge(dir, big) {
  if (!S.sel.size) return api.status('pick something first');
  pushUndo('nudge');
  const d = slide(S, dir * (big ? NUDGE_BIG : NUDGE));
  api.render();
  if (!d) api.status('that is as close as the min gap lets them go');
}

function removePick() {
  const n = [...S.sel].filter((id) => S.bubs.some((b) => b.id === id)).length;
  if (!n) return api.status('pick a bubble or a wall to remove one');
  pushUndo();
  S.bubs = S.bubs.filter((b) => !S.sel.has(b.id));
  S.sel.clear();
  api.render();
  api.bar();
  api.status(n + (n > 1 ? ' gone' : ' gone'));
}

function onKey(ev) {
  if (ev.target && /^(INPUT|TEXTAREA)$/.test(ev.target.tagName)) return;
  if (api.modal && api.modal()) return;             // the card is up: it owns the keyboard
  const ctrl = ev.ctrlKey || ev.metaKey;
  if (ctrl && ev.code === 'KeyZ' && !ev.shiftKey) { ev.preventDefault(); return undo(); }
  if (ctrl && (ev.code === 'KeyY' || (ev.code === 'KeyZ' && ev.shiftKey))) { ev.preventDefault(); return redo(); }
  if (ctrl) return;
  if (ev.code === 'ArrowLeft' || ev.code === 'ArrowRight') { ev.preventDefault(); return nudge(ev.code === 'ArrowLeft' ? -1 : 1, ev.shiftKey); }
  if (ev.code === 'Delete' || ev.code === 'Backspace') { ev.preventDefault(); return removePick(); }
  if (ev.code === 'Escape') { S.sel.clear(); closeGrid(); api.render(); return api.bar(); }
  if (ev.code === 'KeyW') { ev.preventDefault(); return addWall(); }
}

export function install(theApi) {
  api = theApi;
  S = theApi.state;
  document.addEventListener('pointerdown', onDown);
  document.addEventListener('pointermove', onMove);
  document.addEventListener('pointerup', onUp);
  document.addEventListener('keydown', onKey);
  $('addwall').addEventListener('click', addWall);
  $('undo').addEventListener('click', undo);
}

export { KINDS };
