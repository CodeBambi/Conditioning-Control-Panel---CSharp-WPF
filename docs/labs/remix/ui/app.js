import { createProject, pickOrientation, isZipFile, unzip } from './engine-bridge.js';
import { initHud, EFFECTS } from './hud.js';
import { initCanvasView } from './canvas-view.js';
import { initMediaStrip } from './media-strip.js';
import { initTimeline } from './timeline.js';
import { initPanels } from './panels.js';
import { initVibes } from './vibes.js';
import { initExportSheet, fmtMB } from './export-sheet.js';
import { initDropzone } from './dropzone.js';
import { initKeys } from './keys.js';
import { toast } from './toast.js';
import { initHelp } from './help.js';
import { initStripSheet } from './strip-sheet.js';
import { initAuto } from './auto.js';
import { initPool } from './pool.js';
import * as sound from './sound.js';

const q = new URLSearchParams(location.search);
const forceMobile = q.get('forcemobile') === '1';
const startInEditor = q.get('editor') === '1';
const detectMobile = () => forceMobile || (matchMedia('(pointer: coarse)').matches && innerWidth < 900) || innerWidth < 720;
document.documentElement.classList.toggle('is-mobile', detectMobile());

const project = createProject({ orientation: detectMobile() && !forceMobile ? 'portrait' : 'landscape' });
// sample: the bundled loops are on the canvas so the first screen is never blank; they go on the first real drop
// screen: 'auto' (the default page: add, roll, save; caption opt in) | 'editor' | 'vibes'. ?editor=1 boots into the editor.
const state = { selectedTileId: null, selectedBlockId: null, openPanel: null, binMode: false, screen: startInEditor ? 'editor' : 'auto', sample: false };
const listeners = {};
const ctx = {
  project, state, lastSize: null,
  on: (ev, fn) => ((listeners[ev] ||= new Set()).add(fn), () => listeners[ev].delete(fn)),
  emit: (ev, ...a) => listeners[ev]?.forEach(fn => fn(...a)),
  isMobile: () => document.documentElement.classList.contains('is-mobile'),
  isEmpty: () => state.sample || project.tiles.length === 0,
  toast,
  mediaOf: t => project.media.find(m => m.id === t?.mediaId),
  tileLabel: t => `gif ${project.tiles.indexOf(t) + 1}`,
  tileLabelById: id => { const t = project.tiles.find(t => t.id === id); return t ? ctx.tileLabel(t) : 'canvas'; },
  currentTarget: () => (!state.sample && state.selectedTileId && project.tiles.some(t => t.id === state.selectedTileId)) ? state.selectedTileId : 'canvas',
  commit: label => { project.commit(label); syncHistory(); },
  syncHistory: () => syncHistory(),
};
// sound is off unless the export sheet's switch turns it on; the ladder ticks on held rolls, if any
ctx.sound = sound; sound.listen(ctx);

if (q.get('debug') === '1') window.__remix = { project, ctx, state, hud: () => hud.placement() };

// ---- modules
const hud = initHud(ctx);
const canvasView = initCanvasView(ctx);
const strip = initMediaStrip(ctx);
const timeline = initTimeline(ctx);
const panels = initPanels(ctx);
const vibes = initVibes(ctx);
const exportSheet = initExportSheet(ctx);
const stripSheet = initStripSheet(ctx, panels);
const pool = initPool(ctx);
const auto = initAuto(ctx, { exportSheet, pool });
ctx.pool = pool;
ctx.hudButton = key => hud.button(key);

// ---- selection + panels
ctx.selectTile = id => {
  if (id && !project.tiles.some(t => t.id === id)) id = null;
  const was = state.selectedTileId; state.selectedTileId = id;
  if (was !== id) state.selectedBlockId = null;
  if (state.openPanel && EFFECTS.includes(state.openPanel) && was !== id) panels.open(state.openPanel);
  else ctx.emit('state');
};
ctx.selectBlock = id => { state.selectedBlockId = id; const b = project.blocks.find(b => b.id === id); if (b && b.target !== 'canvas' && b.target !== state.selectedTileId) state.selectedTileId = b.target; ctx.emit('state'); };
ctx.togglePanel = key => { if (state.openPanel === key) panels.close(); else panels.open(key); };
ctx.openPanelFor = b => panels.open(b.effect, { target: b.target, block: b });
ctx.closePanel = () => panels.close();
// a hold on a gif or the canvas tab: phone = its strip sheet; desktop = its lane lights up in the docked timeline
ctx.holdTarget = target => {
  if (ctx.isMobile()) { stripSheet.open(target); return; }
  if (target === 'canvas') ctx.selectTile(null); else ctx.selectTile(target);
  timeline.flashLane(target);
};
// ---- screens: the Auto page is the default, the editor is one link away and works on the same project
ctx.setScreen = s => {
  if (s === state.screen) return;
  const app = document.getElementById('app');
  if (s === 'auto') {
    panels.close(); stripSheet.close(); vibes.hide(); exportSheet.close(); ctx.setBin(false);
    state.selectedBlockId = null; state.screen = 'auto'; app.dataset.screen = 'auto'; auto.mount();
  } else {
    auto.unmount(); state.screen = 'editor'; app.dataset.screen = 'editor';
    if (!project.media.length && !pool.size()) loadSamples();
  }
  document.getElementById('btn-back-auto').hidden = s !== 'editor';
  canvasView.refit(); renderAll(); ctx.refreshSize();
};
ctx.setBin = on => { state.binMode = on && project.media.length > 0; document.getElementById('app').classList.toggle('bin-mode', state.binMode); if (state.binMode) { panels.close(); toast(ctx.isMobile() ? 'Tap a gif to remove it. Tap the bin again to stop.' : 'Tap a gif to remove it. Esc to stop.'); } ctx.emit('state'); };

// ---- media in
// The canvas takes its shape from the first gif that lands, once. After that the chip is the user's:
// a hand on it sticks even when more files arrive, and a second project never re-picks.
let orientChosen = false, orientPicked = false;
async function autoOrient(m) {
  if (orientChosen || orientPicked) return;
  orientPicked = true;
  const o = pickOrientation(m.srcW, m.srcH); // the file's own shape: m.w/m.h are the canvas it was drawn at
  if (o === project.orientation) return;
  await project.setOrientation(o); canvasView.refit();
}
// three ways in: the usual pick (gifs, pictures, videos), gifs only, and a bare input with no accept
// list. On Android the first two open the system Photo Picker (one long scroll of everything, and its
// albums show nothing under a type filter); the bare one opens the file browser with folders, search
// and Downloads, which is where a saved Discord gif lives.
const fileInputs = { all: document.getElementById('file-input'), gif: document.getElementById('file-input-gif'), any: document.getElementById('file-input-any') };
// a zip of gifs is welcome on a desktop; on a phone the accept list stays images and videos so the Photo Picker still opens
if (!detectMobile()) fileInputs.all.accept += ',.zip,application/zip';
ctx.pickFiles = (route = 'all') => { const el = fileInputs[route] || fileInputs.all; el.value = ''; el.click(); };
for (const el of Object.values(fileInputs)) el.addEventListener('change', () => { if (el.files?.length) ctx.addFiles(Array.from(el.files)); });
let vibesShown = sessionStorage.getItem('remix.vibesSeen') === '1';
// a zip in the drop opens here, in the browser, and its gifs join the list as if they were dropped one by one
async function expandZips(files) {
  const out = [];
  for (const f of files) {
    if (!isZipFile(f)) { out.push(f); continue; }
    try { const got = await unzip(f); out.push(...got); toast(`${got.length} files out of ${f.name}`); }
    catch (e) { toast(e?.message || `Could not open ${f.name}`, { gold: true }); }
  }
  return out;
}
// Every file lands in the pool (a cheap look, one small frame each). On the Auto page the first one
// rolls by itself and the page moves to Roll while the rest come in; once they are all in, the same code
// rolls again in place with the pool's picks. In the editor the new files go on the canvas, eight at most.
ctx.addFiles = async files => {
  files = await expandZips(files);
  if (!files.length) return;
  if (state.sample) clearSamples();
  const first = pool.size() === 0; let rolled = false;
  const { added } = await pool.add(files, { onEach: async e => {
    if (state.screen !== 'auto' || rolled) return;
    if (first) { await autoOrient(e); project.clearHistory(); }
    rolled = true; await pool.rollSet(null, { progressive: true }); auto.go('roll');
  } });
  if (!added) { if (!project.media.length && state.screen === 'editor') loadSamples(); return; }
  if (state.screen === 'auto') {
    if (rolled && added > 1) await pool.rollSet(project.seed, { replace: true, progressive: true });
    if (added > 1) toast(`${added} files are in`);
    return;
  }
  const want = project.tiles.map(t => t.mediaId);
  for (const e of pool.entries().slice(-added)) { if (want.length >= 8) { toast('Eight on the canvas. The rest wait in the pool.', { gold: true }); break; } want.push(e.id); }
  await pool.decodeInto(want); project.setMediaSet(want);
  if (!state.selectedTileId) state.selectedTileId = project.tiles[0]?.id || null;
  if (first) project.clearHistory(); else ctx.commit(added === 1 ? 'add media' : `add ${added} files`);
  if (!project.playing) project.play();
  if (first && !vibesShown) { vibesShown = true; sessionStorage.setItem('remix.vibesSeen', '1'); vibes.show(); }
  else { vibes.refresh(); toast(added === 1 ? `${files[0].name} is in` : `${added} files are in`); }
};
ctx.removeTile = id => {
  const t = project.tiles.find(t => t.id === id); if (!t) return; const label = ctx.tileLabel(t);
  project.removeTile(id); if (state.selectedTileId === id) state.selectedTileId = project.tiles[0]?.id || null; state.selectedBlockId = null;
  ctx.commit('remove tile'); if (!project.tiles.length) ctx.setBin(false);
  toast(`Took ${label} off the canvas`, { action: 'Undo', onAction: ctx.undo });
};
ctx.removeMedia = id => {
  const m = project.media.find(m => m.id === id); if (!m) return;
  project.removeMedia(id); pool.forget(id); if (!project.tiles.some(t => t.id === state.selectedTileId)) state.selectedTileId = project.tiles[0]?.id || null; state.selectedBlockId = null;
  ctx.commit('remove media'); if (!project.media.length) ctx.setBin(false);
  toast(`Removed ${m.name}`, { action: 'Undo', onAction: ctx.undo });
};
ctx.removeBlock = id => {
  const b = project.blocks.find(b => b.id === id); if (!b) return;
  if (panels.current()?.blockId === id) panels.close();
  project.removeBlock(id); if (state.selectedBlockId === id) state.selectedBlockId = null; ctx.commit('remove block');
  toast(`Removed ${b.effect} block`, { action: 'Undo', onAction: ctx.undo });
};

// ---- sample loops (engine assets) under the empty prompt
const SAMPLES = ['drift', 'pulse', 'spin'];
async function loadSamples() {
  if (state.sample || project.media.length) return;
  state.sample = true;
  for (const n of SAMPLES) {
    try {
      const res = await fetch(`assets/sample/${n}.gif`); if (!res.ok) continue;
      const m = await project.addMedia(new File([await res.blob()], `${n}.gif`, { type: 'image/gif' }));
      if (!state.sample) { project.removeMedia(m.id); return; } // a real drop landed meanwhile
      project.addTile(m.id);
    } catch { /* a missing sample just leaves that slot empty */ }
  }
  project.setLayout({ mode: 'flat' });
  project.clearHistory(); syncHistory();
  if (!project.playing) project.play();
}
function clearSamples() {
  state.sample = false;
  for (const m of project.media.slice()) project.removeMedia(m.id);
  state.selectedTileId = null; state.selectedBlockId = null;
  project.setLayout({ mode: 'grow' });
  project.clearHistory();
}

// ---- history
const btnUndo = document.getElementById('btn-undo'), btnRedo = document.getElementById('btn-redo');
function syncHistory() { btnUndo.disabled = !project.canUndo; btnRedo.disabled = !project.canRedo; }
project.on('history', syncHistory);
ctx.undo = () => { const l = project.undo(); if (l == null) return; syncHistory(); afterHistory(); toast(`Undid ${l}`); };
ctx.redo = () => { const l = project.redo(); if (l == null) return; syncHistory(); afterHistory(); toast(`Redid ${l}`); };
function afterHistory() { if (!project.tiles.some(t => t.id === state.selectedTileId)) state.selectedTileId = project.tiles[0]?.id || null; if (!project.blocks.some(b => b.id === state.selectedBlockId)) state.selectedBlockId = null; panels.refresh(); ctx.emit('state'); }
btnUndo.addEventListener('click', ctx.undo); btnRedo.addEventListener('click', ctx.redo);

// ---- top bar
const orient = document.getElementById('orient');
ctx.setOrientation = async o => {
  orientChosen = true; // a hand on the chip beats the auto pick, for good
  if (o === project.orientation) return;
  orient.querySelectorAll('.seg-btn').forEach(b => b.setAttribute('aria-checked', String(b.dataset.orient === o)));
  await project.setOrientation(o); ctx.commit('orientation'); canvasView.refit();
};
orient.addEventListener('click', e => { const b = e.target.closest('.seg-btn'); if (b) ctx.setOrientation(b.dataset.orient); });
const lengthChip = document.getElementById('length-chip');
ctx.setLength = s => { project.setDuration(s); ctx.commit('length'); toast(`${s} seconds`); };
ctx.cycleLength = () => { const cur = Math.round(project.frames / project.fps); const opts = [3, 5, 8]; ctx.setLength(opts[(opts.indexOf(cur) + 1) % 3]); };
lengthChip.addEventListener('click', ctx.cycleLength);
// flip: the whole layout turned around, the pictures left alone. F does it too.
ctx.toggleFlip = () => {
  const on = !project.layout.flip;
  project.setLayout({ flip: on }); ctx.commit('flip');
  toast(on ? 'Flipped. The motion runs the other way.' : 'Back the way it was.');
};
ctx.cycleLoop = () => { const o = ['clean', 'snap', 'seamless']; const n = o[(o.indexOf(project.loop) + 1) % 3]; project.setLoop(n); ctx.commit('loop'); toast(n === 'snap' ? 'Loop: snap. The last 3 frames cut to one word.' : n === 'seamless' ? 'Loop: seamless crossfade.' : 'Loop: clean cut back to frame 0.'); };
document.getElementById('btn-dice').addEventListener('click', () => { if (ctx.isEmpty()) { toast('Add a gif first.'); return; } project.surprise(); ctx.commit('surprise'); panels.refresh(); toast(`Rolled ${project.code}. Same code, same rolls.`); });
document.getElementById('btn-export').addEventListener('click', () => { if (ctx.isEmpty()) { toast('Add a gif first.'); return; } panels.close(); exportSheet.open(); });
ctx.togglePlay = () => project.playing ? project.pause() : project.play();

// ---- size meter
const meter = document.getElementById('meter'), meterFill = document.getElementById('meter-fill'), meterLabel = document.getElementById('meter-label');
let sizeTimer = 0, sizeSeq = 0;
ctx.refreshSize = async () => {
  if (ctx.isEmpty()) { ctx.lastSize = null; meterFill.style.width = '0'; meterLabel.textContent = ctx.isMobile() ? '--' : '-- / 10 MB'; meter.classList.remove('hot'); meterLabel.classList.remove('hot'); ctx.emit('size', null); return; }
  const seq = ++sizeSeq; meter.classList.add('busy');
  try { const bytes = await project.estimateSize(); if (seq !== sizeSeq) return; ctx.lastSize = bytes; const mb = bytes / 1048576; const hot = mb > 10;
    meterFill.style.width = Math.min(100, mb / 10 * 100) + '%'; meter.classList.toggle('hot', hot); meterLabel.classList.toggle('hot', hot); meter.setAttribute('aria-valuenow', mb.toFixed(1));
    meterLabel.textContent = ctx.isMobile() ? `${fmtMB(bytes)} MB` : `${fmtMB(bytes)} / 10 MB`; exportSheet.updateSize(); ctx.emit('size', bytes);
  } catch { } finally { if (seq === sizeSeq) meter.classList.remove('busy'); }
};
const scheduleSize = () => { clearTimeout(sizeTimer); sizeTimer = setTimeout(ctx.refreshSize, 500); };

// ---- render orchestration
function renderAll() {
  document.getElementById('app').dataset.orient = project.orientation; document.getElementById('auto').dataset.orient = project.orientation;
  lengthChip.textContent = `${(project.frames / project.fps).toFixed(1)} s · ${project.fps} fps`;
  orient.querySelectorAll('.seg-btn').forEach(b => b.setAttribute('aria-checked', String(b.dataset.orient === project.orientation)));
  hud.place(); hud.render(); canvasView.render(); strip.render(); timeline.render(); stripSheet.render(); syncHistory(); auto.render(); pool.render();
}
project.on('change', () => { renderAll(); scheduleSize(); });
project.on('media', () => strip.render());
ctx.on('state', renderAll);

// ---- escape ladder + keys
ctx.escape = () => {
  if (exportSheet.isOpen()) return exportSheet.close();
  if (state.screen === 'auto') { if (pool.isManagerOpen()) return pool.closeManager(); return auto.step() === 'caption' ? auto.go('roll') : null; }
  if (vibes.isOpen()) return vibes.hide();
  timeline.closeMenu();
  if (stripSheet.isOpen()) return stripSheet.close();
  if (strip.isOpen()) return strip.closeList();
  if (state.binMode) return ctx.setBin(false);
  if (panels.isOpen()) return panels.close();
  if (state.selectedBlockId) { state.selectedBlockId = null; return ctx.emit('state'); }
  if (state.selectedTileId) ctx.selectTile(null);
};
const inAuto = () => state.screen === 'auto';
initKeys({
  undo: ctx.undo, redo: ctx.redo, escape: ctx.escape,
  // Auto: Space on the pill is the pill's own press, arrows walk the roll history
  togglePlay: () => { if (inAuto() && document.activeElement?.tagName === 'BUTTON') return; ctx.togglePlay(); },
  nudge: d => { if (inAuto()) return; const b = project.blocks.find(b => b.id === state.selectedBlockId); if (!b) return; const len = b.end - b.start; const s = Math.max(0, Math.min(project.frames - len, b.start + d)); project.updateBlock(b.id, { start: s, end: s + len }); ctx.commit('nudge'); },
  remove: () => { if (inAuto()) return; if (state.selectedBlockId) ctx.removeBlock(state.selectedBlockId); else if (state.selectedTileId) ctx.removeTile(state.selectedTileId); },
  step: d => { if (inAuto()) { if (d < 0) auto.back(); else auto.forward(); return; } project.pause(); project.seek((project.frame + d + project.frames) % project.frames); },
  seek: i => { project.pause(); project.seek(i < 0 ? project.frames - 1 : i); },
  flip: () => { if (inAuto() || ctx.isEmpty()) return; ctx.toggleFlip(); panels.refresh(); },
});
initDropzone({ onFiles: ctx.addFiles });
initHelp(ctx);
window.addEventListener('resize', () => { const m = detectMobile(); if (m !== ctx.isMobile()) { document.documentElement.classList.toggle('is-mobile', m); stripSheet.close(); renderAll(); } else hud.place(); });
// the soft keyboard: iOS shrinks the visual viewport, not the layout one, so a fixed bottom sheet would sit
// under it. --kb is the shortfall and the phone panel rides up by that much.
if (window.visualViewport) {
  const vv = window.visualViewport;
  const kb = () => { const gap = Math.max(0, Math.round(innerHeight - vv.height - vv.offsetTop)); document.documentElement.style.setProperty('--kb', (gap > 60 ? gap : 0) + 'px'); };
  vv.addEventListener('resize', kb); vv.addEventListener('scroll', kb);
}
document.addEventListener('visibilitychange', () => { if (document.hidden && project.playing) { project.pause(); project._resume = true; } else if (!document.hidden && project._resume) { project._resume = false; project.play(); } });

document.getElementById('btn-back-auto').addEventListener('click', () => ctx.setScreen('auto'));
document.getElementById('btn-back-auto').hidden = state.screen !== 'editor';
document.getElementById('app').dataset.screen = state.screen;
if (state.screen === 'auto') auto.mount();
renderAll(); ctx.refreshSize();
if (state.screen === 'editor') loadSamples();
