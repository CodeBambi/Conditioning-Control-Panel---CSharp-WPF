/* ============================================================================
 * chart/maker/app.js - Track Maker, the wiring (chart/MAKER.md, PR M1).
 *
 * One screen: pick an mp3, the maker finds its words, places a run of bubbles
 * at every trigger it heard, and gets out of the way. No tabs, no settings, no
 * modes. The state lives here, the pure moves live in model.js and the drawing
 * lives in timeline.js.
 * ==========================================================================*/

import { loadAudio, createPlayer, isAudioFile } from './audio.js';
import { findWords, hashNote, isWords, scan } from './words.js';
import * as tl from './timeline.js';
import {
  DEFAULT_ON, DEFAULT_RECIPE, EFFECTS, KINDS, MIN_GAP_DEF, MIN_GAP_HI, MIN_GAP_LO, MIN_GAP_STEP,
  RECIPE_BY_ID, byT, clamp, isOn, placeAll, placeHit, recipeFor, resetIds,
} from './model.js';

const $ = (id) => document.getElementById(id);

export const state = {
  audio: null, words: null, rows: [], setById: new Map(),
  hits: [], bubs: [], sel: new Set(),
  cfg: {}, minGap: MIN_GAP_DEF, durationSec: 0, peaks: null, perSec: 50,
};

const player = createPlayer();
let statusTimer = 0;

/* ---- the status line ----------------------------------------------------- */

export function status(text, sticky = false) {
  const n = $('status');
  clearTimeout(statusTimer);
  if (!text) { n.hidden = true; return; }
  n.textContent = text;
  n.hidden = false;
  if (!sticky) statusTimer = setTimeout(() => { n.hidden = true; }, 7000);
}

/* ---- placing ------------------------------------------------------------- */

const hitsOf = (setId) => state.hits.filter((h) => h.setId === setId);

/** One trigger, placed again from its recipe. Hand moved bubbles of it go too. */
export function replaceSet(setId) {
  state.bubs = state.bubs.filter((b) => b.trig !== setId);
  if (isOn(state.cfg, setId)) {
    const r = recipeFor(state.cfg, setId);
    for (const h of hitsOf(setId)) state.bubs.push(...placeHit(h, r, state.minGap));
  }
  state.bubs = byT(state.bubs);
}

/* ---- the side panel ------------------------------------------------------ */

function beads(recipe) {
  const box = document.createElement('div');
  box.className = 'beads';
  for (const s of recipe.seq) {
    const [kind, eff] = s.split(':');
    const m = document.createElement('span');
    if (kind === 'wall') {
      m.className = 'mini wall';
      m.textContent = EFFECTS[eff][0];
      m.title = 'wall: ' + EFFECTS[eff][1];
    } else {
      m.className = 'mini ' + KINDS[kind].cls;
      m.textContent = KINDS[kind].glyph;
      m.title = KINDS[kind].name;
    }
    box.append(m);
  }
  return box;
}

export function renderPanel() {
  const host = $('recipes');
  if (!state.rows.length) {
    host.replaceChildren(Object.assign(document.createElement('p'), { textContent: 'no track yet.' }));
    return;
  }
  const f = document.createDocumentFragment();
  for (const row of state.rows) {
    const id = row.set.id;
    const el = document.createElement('label');
    el.className = 'recipe';
    el.dataset.set = id;
    const cb = document.createElement('input');
    cb.type = 'checkbox';
    cb.checked = isOn(state.cfg, id);
    cb.addEventListener('change', () => {
      state.cfg[id].on = cb.checked;
      replaceSet(id);
      tl.render();
      status(cb.checked ? row.set.name + ' placed at all ' + row.hits.length + ' of them' : row.set.name + ' cleared');
    });
    const who = document.createElement('span');
    who.className = 'who';
    who.textContent = row.set.name;
    who.style.color = row.set.color;
    const n = document.createElement('span');
    n.className = 'n';
    n.textContent = row.hits.length + ' in file';
    el.append(cb, who, n, beads(recipeFor(state.cfg, id)));
    f.append(el);
  }
  host.replaceChildren(f);
}

function setGap(v) {
  state.minGap = Number(clamp(v, MIN_GAP_LO, MIN_GAP_HI).toFixed(1));
  $('gapv').textContent = state.minGap.toFixed(1) + ' s';
}

/* ---- loading ------------------------------------------------------------- */

function useWords(json, via) {
  state.words = json;
  if (!state.durationSec) state.durationSec = Number(json.source && json.source.durationSec) || 0;
  state.rows = scan(json, state.durationSec || Infinity);
  state.setById = new Map(state.rows.map((r) => [r.set.id, r.set]));
  state.hits = byT(state.rows.flatMap((r) => r.hits));
  state.cfg = {};
  for (const r of state.rows) state.cfg[r.set.id] = { on: DEFAULT_ON.includes(r.set.id), recipe: DEFAULT_RECIPE[r.set.id] || 'triple' };
  resetIds();
  state.bubs = placeAll(state.hits, state.cfg, state.minGap);
  state.sel.clear();
  renderPanel();
  tl.render();
  $('sub').textContent = 'words found and placed. play it, then slide what is off.';
  const mismatch = hashNote(json, state.audio && state.audio.hash);
  const heard = state.hits.length + ' trigger words in ' + state.rows.length + ' sets';
  status(mismatch || (via === 'name' ? 'words found by name. ' + heard : 'words found. ' + heard), !!mismatch);
  return true;
}

async function onAudioFile(file) {
  status('reading ' + file.name, true);
  const a = await loadAudio(file, (m) => status(m, true));
  state.audio = a;
  state.durationSec = a.durationSec || 0;
  state.peaks = a.peaks;
  state.perSec = a.perSec;
  state.rows = []; state.hits = []; state.bubs = []; state.words = null;
  state.setById = new Map();
  state.sel.clear();
  player.open(a.url);
  $('pick').textContent = '\u{1F3B5} ' + a.name;
  $('trackname').textContent = a.stem;
  tl.setView(0);
  tl.render();
  renderPanel();
  status('finding the words for it', true);
  const found = await findWords(a.stem, a.hash);
  if (found) return useWords(found.json, found.via);
  $('sub').textContent = 'no words for this file yet. drop its .words.json here.';
  status('no words for this file yet. drop its .words.json here.', true);
  return false;
}

async function onJsonFile(file) {
  let json = null;
  try { json = JSON.parse(await file.text()); } catch (e) { json = null; }
  if (!isWords(json)) return status('that json is not a words file');
  if (!state.audio) return status('pick the mp3 first, then drop the words on it');
  return useWords(json, 'dropped');
}

const takeFile = (f) => (isAudioFile(f) ? onAudioFile(f) : /\.json$/i.test(f.name || '') ? onJsonFile(f) : status('that is not an mp3 or a words file'));

/* ---- the loop ------------------------------------------------------------ */

let lastT = -1;
function loop() {
  const t = player.time;
  const playing = player.playing;
  if (t !== lastT || playing) {
    lastT = t;
    if (playing && tl.shouldFollow(t)) tl.follow(t);
    tl.moveHead(t);
  }
  requestAnimationFrame(loop);
}

/* ---- wiring -------------------------------------------------------------- */

function playLabel() {
  $('play').innerHTML = (player.playing ? '⏸ pause' : '▶ play') + ' <kbd>space</kbd>';
}

export function seekTo(t) {
  player.seek(clamp(t, 0, state.durationSec || 0));
  lastT = -1;
}

function init() {
  tl.init(state);
  setGap(MIN_GAP_DEF);
  renderPanel();
  $('pick').addEventListener('click', () => $('file').click());
  $('file').addEventListener('change', (ev) => { const f = ev.target.files[0]; if (f) takeFile(f); ev.target.value = ''; });
  $('play').addEventListener('click', () => { player.toggle(); playLabel(); });
  player.el.addEventListener('play', playLabel);
  player.el.addEventListener('pause', playLabel);
  $('zin').addEventListener('click', () => tl.zoomAt(1.25, null));
  $('zout').addEventListener('click', () => tl.zoomAt(1 / 1.25, null));
  $('gapm').addEventListener('click', () => setGap(state.minGap - MIN_GAP_STEP));
  $('gapp').addEventListener('click', () => setGap(state.minGap + MIN_GAP_STEP));

  // click the audio row or the ruler to jump
  for (const id of ['audio', 'ruler']) {
    $(id).addEventListener('pointerdown', (ev) => {
      const r = $('seqs').getBoundingClientRect();
      seekTo(tl.tOf(ev.clientX - r.left));
      tl.moveHead(player.time);
    });
  }

  // drop an mp3 or a words file anywhere
  document.addEventListener('dragover', (ev) => { ev.preventDefault(); document.body.classList.add('dropping'); });
  document.addEventListener('dragleave', (ev) => { if (ev.relatedTarget === null) document.body.classList.remove('dropping'); });
  document.addEventListener('drop', (ev) => {
    ev.preventDefault();
    document.body.classList.remove('dropping');
    const f = ev.dataTransfer && ev.dataTransfer.files && ev.dataTransfer.files[0];
    if (f) takeFile(f);
  });

  // zoom on ctrl+wheel around the pointer, pan on a plain wheel
  $('lines').addEventListener('wheel', (ev) => {
    if (ev.target.closest('#side')) return;
    ev.preventDefault();
    if (ev.ctrlKey) tl.zoomAt(ev.deltaY < 0 ? 1.15 : 1 / 1.15, ev.clientX);
    else tl.panBy((ev.deltaY || ev.deltaX) / tl.view.pps);
  }, { passive: false });

  document.addEventListener('keydown', (ev) => {
    if (ev.target && /^(INPUT|TEXTAREA)$/.test(ev.target.tagName)) return;
    if (ev.code === 'Space') { ev.preventDefault(); player.toggle(); playLabel(); }
    else if (ev.code === 'Home') { ev.preventDefault(); tl.setView(0); seekTo(0); }
    else if (ev.code === 'End') { ev.preventDefault(); tl.setView(state.durationSec - tl.spanSec() * 0.5); }
    else if (ev.code === 'Equal' || ev.code === 'NumpadAdd') { ev.preventDefault(); tl.zoomAt(1.25, null); }
    else if (ev.code === 'Minus' || ev.code === 'NumpadSubtract') { ev.preventDefault(); tl.zoomAt(1 / 1.25, null); }
  });

  requestAnimationFrame(loop);
}

init();

/* the headless checks drive the page through this, exactly as a hand would. */
window.trackMaker = { state, tl, player, status, replaceSet, renderPanel, seekTo, RECIPE_BY_ID };
