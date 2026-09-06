/* ============================================================================
 * chart/maker/app.js - Track Maker, the wiring (chart/MAKER.md, PR M1).
 *
 * One screen: pick an mp3, the maker finds its words, places a run of bubbles
 * at every trigger it heard, and gets out of the way. No tabs, no settings, no
 * modes. The state lives here, the pure moves live in model.js and the drawing
 * lives in timeline.js.
 * ==========================================================================*/

import { loadAudio, createPlayer, isAudioFile } from './audio.js';
import * as pick from './pick.js';
import * as preview from './preview.js';
import * as save from './save.js';
import { findWords, hashNote, isWords, scan } from './words.js';
import { generate, roadLine } from './generate.js';
import * as tl from './timeline.js';
import {
  DEFAULT_ON, DEFAULT_RECIPE, EFFECTS, KINDS, MIN_GAP_DEF, MIN_GAP_HI, MIN_GAP_LO, MIN_GAP_STEP,
  RECIPE_BY_ID, byT, clamp, fmtShort, isOn, pickLine, placeAll, placeHit, recipeFor, resetIds,
} from './model.js';

const $ = (id) => document.getElementById(id);

export const state = {
  audio: null, words: null, rows: [], setById: new Map(),
  hits: [], bubs: [], sel: new Set(), road: null,
  cfg: {}, minGap: MIN_GAP_DEF, durationSec: 0, peaks: null, perSec: 50,
};

const player = createPlayer();
let statusTimer = 0;
let pv = null;                          // the race in the bottom row, once there is a road

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

export function beads(recipe) {
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
      render();
      status(cb.checked ? row.set.name + ' placed at all ' + row.hits.length + ' of them' : row.set.name + ' cleared');
    });
    const who = document.createElement('span');
    who.className = 'who';
    who.textContent = row.set.name;
    who.style.color = row.set.color;
    const n = document.createElement('span');
    n.className = 'n';
    n.textContent = row.hits.length + ' in file';
    const box = beads(recipeFor(state.cfg, id));
    const ch = document.createElement('button');
    ch.type = 'button';
    ch.className = 'change';
    ch.textContent = 'change';
    ch.title = 'what ' + row.set.name + ' gets';
    ch.addEventListener('click', (ev) => { ev.preventDefault(); ev.stopPropagation(); pick.openRecipes(ch, id); });
    box.append(ch);
    el.append(cb, who, n, box);
    f.append(el);
  }
  host.replaceChildren(f);
}

export function bar() { $('what').textContent = pickLine(state); }

/** Every edit goes through here: draw it, then let the autosave catch up. */
export function render() { tl.render(); save.touch(); if (pv) pv.onEdit(); }

export function setGap(v) {
  state.minGap = Number(clamp(v, MIN_GAP_LO, MIN_GAP_HI).toFixed(1));
  $('gapv').textContent = state.minGap.toFixed(1) + ' s';
}

/* ---- the road ------------------------------------------------------------ */

/**
 * Run maker/generate.js over the peaks and the words and keep what it says.
 * The road only: every bubble the author placed or moved stays exactly where it
 * is, which is why `g` is safe to lean on while the file is playing.
 */
export function generateRoad(quiet = false) {
  if (!state.audio || !state.words) { if (!quiet) status('pick an mp3 with its words first'); return null; }
  state.road = generate({
    peaks: state.peaks, perSec: state.perSec, durationSec: state.durationSec,
    words: state.words, hits: state.hits, setById: state.setById,
  });
  if (pv) pv.ensure();                   // the first road is what turns the preview on
  render();
  if (!quiet) status(roadLine(state.road.counts));
  return state.road;
}

/* ---- the one card -------------------------------------------------------- */

/**
 * The file landed and the words were found: say what is in it and ask the one
 * question worth asking. It is the only modal in the tool, esc says start empty
 * and everything under it keeps working the moment it goes.
 */
export const card = {
  get open() { return !$('ask').hidden; },
  show(saved) {
    $('askname').textContent = (state.audio && state.audio.stem) || 'this track';
    $('askline').textContent = fmtShort(state.durationSec || 0) + ', ' + state.hits.length
      + ' trigger word' + (state.hits.length === 1 ? '' : 's') + ' in ' + state.rows.length + ' set' + (state.rows.length === 1 ? '' : 's');
    $('askgo').textContent = saved ? 'pick up where you left off' : 'generate the track';
    $('askalt').hidden = !saved;
    $('askwarn').hidden = !saved;
    $('ask').hidden = false;
    $('askgo').focus();
  },
  hide() { $('ask').hidden = true; },
};

function cardGo() {
  const saved = !$('askalt').hidden;
  card.hide();
  if (saved) return status('picked up where you left off: ' + state.bubs.length + ' on the track.', true);
  generateRoad();
}
function cardAgain() {
  card.hide();
  save.forget(state.audio.hash);
  useWords(state.words, 'again');
  const road = generateRoad(true);
  status('generated again. every trigger is back on its recipe. ' + (road ? roadLine(road.counts) : ''), true);
}
function cardSkip() {
  card.hide();
  status('started empty. the triggers are placed, the road is not: press g when you want it.', true);
}

/* ---- loading ------------------------------------------------------------- */

/** Throw the autosave away and place the whole track again from the recipes. */
export function startOver() {
  if (!state.audio || !state.words) return status('nothing to start over yet');
  save.forget(state.audio.hash);
  state.road = null;
  useWords(state.words, 'again');
  status('started over. every trigger is back on its recipe, and the road is gone. press g for a new one.');
}

function useWords(json, via) {
  state.words = json;
  if (via === 'again') state.road = null;
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
  bar();
  $('sub').textContent = 'words found and placed. play it, then slide what is off.';
  const mismatch = hashNote(json, state.audio && state.audio.hash);
  const heard = state.hits.length + ' trigger words in ' + state.rows.length + ' sets';
  status(mismatch || (via === 'name' ? 'words found by name. ' + heard : 'words found. ' + heard), !!mismatch);
  return true;
}

/** A track this machine was working on before. Only offered for the same audio. */
function restoreSaved() {
  const saved = state.audio && save.read(state.audio.hash);
  if (!saved) { $('startover').hidden = true; return false; }
  save.restoreInto(state, saved);
  setGap(saved.minGap || state.minGap);
  renderPanel();
  tl.render();
  bar();
  $('startover').hidden = false;
  return true;                                   // what to do about it is the card's question

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
  if (found) { useWords(found.json, found.via); card.show(restoreSaved()); return true; }
  $('sub').textContent = 'no words for this file yet. drop its .words.json here.';
  status('no words for this file yet. drop its .words.json here.', true);
  return false;
}

async function onJsonFile(file) {
  let json = null;
  try { json = JSON.parse(await file.text()); } catch (e) { json = null; }
  if (!isWords(json)) return status('that json is not a words file');
  if (!state.audio) return status('pick the mp3 first, then drop the words on it');
  useWords(json, 'dropped');
  card.show(false);
  return true;
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
    if (pv) pv.clock(t, playing);         // the frame's own clock is 4 Hz, not per frame
  }
  requestAnimationFrame(loop);
}

/* ---- wiring -------------------------------------------------------------- */

function playLabel() {
  $('play').innerHTML = (player.playing ? '⏸ pause' : '▶ play') + ' <kbd>space</kbd>';
  if (pv) pv.clock(player.time, player.playing);   // play and pause are news the frame wants now
}

export function seekTo(t) {
  const at = clamp(t, 0, state.durationSec || 0);
  player.seek(at);
  lastT = -1;
  if (pv) pv.seek(at, player.playing);
}

function init() {
  tl.init(state);
  setGap(MIN_GAP_DEF);
  renderPanel();
  $('pick').addEventListener('click', () => $('file').click());
  $('file').addEventListener('change', (ev) => { const f = ev.target.files[0]; if (f) takeFile(f); ev.target.value = ''; });
  $('play').addEventListener('click', () => { player.toggle(); playLabel(); });
  $('gen').addEventListener('click', () => generateRoad());
  $('askgo').addEventListener('click', cardGo);
  $('askalt').addEventListener('click', cardAgain);
  $('askskip').addEventListener('click', (ev) => { ev.preventDefault(); cardSkip(); });
  player.el.addEventListener('play', playLabel);
  player.el.addEventListener('pause', playLabel);
  $('zin').addEventListener('click', () => tl.zoomAt(1.25, null));
  $('zout').addEventListener('click', () => tl.zoomAt(1 / 1.25, null));
  $('gapm').addEventListener('click', () => { setGap(state.minGap - MIN_GAP_STEP); save.touch(); });
  $('gapp').addEventListener('click', () => { setGap(state.minGap + MIN_GAP_STEP); save.touch(); });

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
    if (card.open) { if (ev.code === 'Escape') { ev.preventDefault(); cardSkip(); } return; }
    if (ev.code === 'KeyG' && !ev.ctrlKey && !ev.metaKey) { ev.preventDefault(); generateRoad(); }
    else if (ev.code === 'Space') { ev.preventDefault(); player.toggle(); playLabel(); }
    else if (ev.code === 'Home') { ev.preventDefault(); tl.setView(0); seekTo(0); }
    else if (ev.code === 'End') { ev.preventDefault(); tl.setView(state.durationSec - tl.spanSec() * 0.5); }
    else if (ev.code === 'Equal' || ev.code === 'NumpadAdd') { ev.preventDefault(); tl.zoomAt(1.25, null); }
    else if (ev.code === 'Minus' || ev.code === 'NumpadSubtract') { ev.preventDefault(); tl.zoomAt(1 / 1.25, null); }
  });

  const shared = {
    state, status, bar, renderPanel, replaceSet, setGap, startOver, generateRoad,
    beads, render, pps: () => tl.view.pps, time: () => player.time, modal: () => card.open,
  };
  pick.install(shared);
  save.install(shared);
  pv = preview.install(shared);
  if (state.road) pv.ensure();            // a restored track already has a road to watch
  requestAnimationFrame(loop);
}

init();

/* the headless checks drive the page through this, exactly as a hand would. */
window.trackMaker = { state, tl, player, pick, save, status, replaceSet, renderPanel, seekTo, bar, startOver,
  generateRoad, card, preview: () => pv, RECIPE_BY_ID };
