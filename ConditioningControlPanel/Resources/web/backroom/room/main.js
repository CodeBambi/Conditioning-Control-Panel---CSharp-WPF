/* ============================================================================
 * backroom/room/main.js - boot, the Back button, visiting, and the ways out.
 *
 * Boot: subscribe, post `ready`, wait for `init`, read stations.json, build the
 * 3D room. The Back button and Escape are wired BEFORE any of that, so the room
 * can be left at every frame, including a boot that never finishes (Law VI).
 *
 * Visiting (E or the Visit prompt near a station): the room holds (pose kept,
 * loop stopped, context kept), then the loader opens the station, or the
 * dust-sheet card for a `soon` one.
 *
 * Leaving:
 *   Back / Escape with a station open  -> the station closes, the room resumes
 *                                         on the exact spot and facing.
 *   Back / Escape in the room view     -> back to walking.
 *   Back / Escape in the room          -> `exit`, then `exit-done` once settled.
 *   host `close` (app exit, panic)     -> settle inside 300 ms, `exit-done`.
 * ==========================================================================*/

import * as bridge from '../bridge.js';
import { normaliseStations } from './walk.js';
import { createScene } from './scene.js';
import { createLoader } from './loader.js';
import { createHud } from './hud.js';

const PAGE_SETTLE_MS = 300;
const LABEL_FALLBACK = { br_room_label_jackpot: 'A little luck', br_room_label_status: 'Pull me', br_room_label_wheel_status: 'Your daily detour' };
const ADS = [
  { file: 'arcademy', key: 'br_ad_arcademy', fallback: 'The Arcademy' },
  { file: 'dtrh', key: 'br_ad_dtrh', fallback: 'Down the Rabbit Hole' },
  { file: 'focus-gaze', key: 'br_ad_focus_gaze', fallback: 'Focus Gaze' },
];

/** CONTRACT 10.13.A + 10.14: the host's hypno toggles and the room's own tunnel and melt switches. A missing frame or
 * key reads as on (the host is the enforcer). */
const readGates = (g) => {
  const q = g && typeof g === 'object' ? g : {};
  return Object.freeze({ flash: q.flash !== false, subliminal: q.subliminal !== false, spiral: q.spiral !== false,
    brainDrain: q.brainDrain !== false, tunnel: q.tunnel !== false, melt: q.melt !== false });
};
const INTENSITIES = ['calm', 'normal', 'full'];
const readChoice = (v, fallback) => (INTENSITIES.includes(v) ? v : fallback);

const state = { sp: 0, reduced: false, motion: 'full', intensity: 'normal', intensityChoice: 'normal', gates: readGates(null), lex: {}, open: null, suspended: false, userStill: false };
const spListeners = new Set();
const settingsListeners = new Set();
let scene = null, loader = null, hud = null, leaving = false, visiting = false;

const $ = (sel) => document.querySelector(sel);

function lex(key, fallback) {
  const v = state.lex && state.lex[key];
  return (typeof v === 'string' && v && v !== key) ? v : (fallback == null ? key : fallback);
}
const label = (row) => lex(row.labelKey, row.name);
const forcedStill = () => !!state.reduced || state.intensity === 'calm';
const still = () => forcedStill() || state.userStill;

function paintChrome() {
  const back = $('#br-back');
  back.textContent = lex('br_back', 'Back');
  back.setAttribute('aria-label', lex('br_back', 'Back'));
  $('#br-sp-label').textContent = lex('br_balance', 'SP');
  paintSpChip();
  document.title = lex('br_room_title', 'The Back Room');
  document.documentElement.classList.toggle('br-reduced', !!state.reduced);
  document.documentElement.classList.toggle('br-suspended', !!state.suspended);
}

/* THE SP CHIP (CONTRACT 7.1). It always shows Law I shownSp: the server balance minus the wins still
 * on a tape. The station tells the room what the tape still owes (spReadout.owe) and, while THE BANK
 * flies, the number to show (spReadout.set). The rule is the room's, so it holds on every repaint: a
 * balance frame, a station-result that moved state.sp, a closed station and a reopened one. */
const chip = { owed: 0, shown: null };
let chipFrame = 0;
function owedNow() {
  let n = chip.owed;
  if (typeof n === 'function') { try { n = n(); } catch (e) { n = 0; } }
  n = Number(n);
  return Number.isFinite(n) && n > 0 ? n : 0;
}
function paintSpChip() {
  const node = $('#br-sp-value');
  const v = String(chip.shown != null ? chip.shown : Math.max(0, state.sp - owedNow()));
  if (node && node.textContent !== v) node.textContent = v;
}
const spReadout = Object.freeze({
  /** A number shown as is (a BANK tick), or null to go back to the rule. */
  set(value) { const n = Number(value); chip.shown = value == null || !Number.isFinite(n) ? null : n; paintSpChip(); },
  /** The pays still on the tape: a number, or a reader the room calls on each repaint while the station is open. */
  owe(n) { chip.owed = typeof n === 'function' ? n : (Number(n) || 0); paintSpChip(); },
  /** THE THUD: a bank token landing on the chip. Reduced motion lights it instead of scaling it. */
  thud() {
    const box = $('.br-sp');
    if (!box || typeof box.animate !== 'function') return;
    if (state.reduced) { box.animate([{ boxShadow: '0 0 0 2px #ffcf6b' }, { boxShadow: '0 0 0 2px #ffcf6b' }], { duration: 520 }); return; }
    box.animate([{ transform: 'scale(1.3)', filter: 'brightness(2.2)' }, { transform: 'scale(.94)', offset: 0.55 }, { transform: 'scale(1)', filter: 'brightness(1)' }],
      { duration: 340, easing: 'cubic-bezier(.2,1.5,.4,1)' });
  },
  /** The chip's box, where THE BANK's tokens fly to and from. */
  target() { return $('.br-sp'); },
});
/** A station is gone: a reader freezes to its last answer and any flight value is dropped. */
function chipSettle() {
  chip.owed = owedNow();
  chip.shown = null;
  paintSpChip();
}
/** state.sp moved under a station-result: repaint next frame, after the station adopted the same reply. */
function spChanged() {
  if (chipFrame) return;
  chipFrame = requestAnimationFrame(() => { chipFrame = 0; paintSpChip(); });
}

function paintMotion() {
  if (scene) scene.setStill(still());
  if (!hud) return;
  hud.motion(still(), forcedStill());
  hud.options({ intensityChoice: state.intensityChoice, forcedCalm: !!state.reduced, tunnel: state.gates.tunnel, melt: state.gates.melt });
}

/** The room's Options (10.14): tell the host and show the press at once; the host's settings frame has the last word. */
function setOption(key, value) {
  if (key === 'intensity') {
    if (!INTENSITIES.includes(value)) return;
    state.intensityChoice = value;
  } else if (key === 'tunnel' || key === 'melt') {
    state.gates = readGates({ ...state.gates, [key]: !!value });
  } else return;
  bridge.send({ type: 'room-option', key, value });
  paintMotion();
}

function setSp(sp) {
  if (!Number.isFinite(sp)) return;
  state.sp = sp;
  paintChrome();
  for (const fn of Array.from(spListeners)) { try { fn(sp); } catch (e) { bridge.log('warn', 'onSp threw: ' + e); } }
}

async function visit(row) {
  if (leaving || visiting || !scene || !loader || scene.overview) return;
  visiting = true;
  scene.hold();
  hud.hideWhileVisiting(true);
  await loader.open(row, { variant: row.variant ? { id: row.variant, name: label(row), palette: row.fixture.palette } : null });
}

async function returnToRoom() {
  visiting = false;
  await loader.close();
  if (leaving || visiting) return;
  hud.hideWhileVisiting(false);
  if (scene) scene.release();
}

/** Back, from anywhere. A station closes first, then the room view; an empty room is left. */
async function back(reason) {
  if (leaving) return;
  if (loader && (loader.current || visiting)) { await returnToRoom(); return; }
  if (hud && hud.optionsOpen) { hud.closeOptions(); return; }
  if (scene && scene.overview) { scene.setOverview(false); hud.overview(false); return; }
  leave(reason || 'back');
}

async function settle() {
  if (scene) scene.halt();
  if (loader) await loader.close(PAGE_SETTLE_MS - 60);
  bridge.send({ type: 'exit-done' });
}

async function leave(reason) {
  if (leaving) return;
  leaving = true;
  document.documentElement.classList.add('br-leaving');
  bridge.send({ type: 'exit', reason });
  await settle();
}

const HUD = '.br-hud, #br-room-ui';
const hudButton = (t) => {
  const b = t && t.closest ? t.closest('button') : null;
  return b && b.closest(HUD) ? b : null;
};
/** Walking, or a station or card on screen. Only the room view and a boot that never finished take HUD keys. */
const hudKeysOff = () => visiting || !!(loader && loader.current)
  || (document.documentElement.classList.contains('br-ready') && !(scene && scene.overview));

/* Space and Enter never re-press the room's chrome (desk run: a clicked Back kept focus, and a later
 * Space closed the station, then the whole room). A HUD button drops focus when the pointer lets go,
 * and while walking or visiting the two keys on a HUD button are eaten and the focus dropped, so the
 * station's own keys (Space spins) reach it from the next press on. */
function wireHudKeys() {
  document.addEventListener('pointerup', (e) => { const b = hudButton(e.target); if (b) b.blur(); }, true);
  const guard = (e) => {
    if (e.code !== 'Space' && e.key !== ' ' && e.key !== 'Enter') return;
    const b = hudButton(e.target);
    if (!b || !hudKeysOff()) return;
    e.preventDefault();
    e.stopPropagation();
    b.blur();
  };
  window.addEventListener('keydown', guard, true);
  window.addEventListener('keyup', guard, true);
}

function wireExits() {
  wireHudKeys();
  $('#br-back').addEventListener('click', () => back('back'));
  window.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    e.preventDefault();
    back('key');
  }, true);
  bridge.on('close', async () => {
    if (leaving) return;
    leaving = true;
    await settle();
  });
}

async function readStations() {
  try {
    const res = await fetch('stations.json', { cache: 'no-store' });
    return normaliseStations(await res.json());
  } catch (e) {
    bridge.log('error', 'stations.json unreadable: ' + ((e && e.message) || e));
    return [];
  }
}

/** The wall pictures' deal, under its own station id so it never replaces a sit-down deal. */
function media() {
  const reqId = bridge.mintId();
  return bridge.request({ type: 'media-request', reqId, station: 'room' }, 'media', (m) => m.reqId === reqId, 6000,
    { reqId, seed: 0, gifs: [], words: [], timeout: true });
}

async function start(init) {
  Object.assign(state, {
    sp: Number.isFinite(init.sp) ? init.sp : 0,
    reduced: !!init.reduced,
    motion: String(init.motion || 'full'),
    intensity: String(init.intensity || 'normal'),
    gates: readGates(init.gates),
    intensityChoice: readChoice(init.intensityChoice, readChoice(init.intensity, 'normal')),
    lex: (init.lex && typeof init.lex === 'object') ? init.lex : {},
    open: typeof init.open === 'boolean' ? init.open : null,
  });
  bridge.markInitialized();
  paintChrome();

  bridge.on('balance', (m) => setSp(m.sp));
  bridge.on('settings', (m) => {
    state.motion = String(m.motion || state.motion);
    state.intensity = String(m.intensity || state.intensity);
    state.reduced = !!m.reduced;
    if (m.gates && typeof m.gates === 'object') state.gates = readGates(m.gates);
    state.intensityChoice = readChoice(m.intensityChoice, state.intensityChoice);
    paintChrome();
    paintMotion();
    const frame = { motion: state.motion, intensity: state.intensity, reduced: state.reduced, gates: state.gates };
    for (const fn of Array.from(settingsListeners)) { try { fn(frame); } catch (e) { bridge.log('warn', 'onSettings threw: ' + e); } }
  });
  bridge.on('suspend', (m) => {
    state.suspended = !!m.on;
    if (scene) scene.pause(state.suspended);   // a held room stays held either way
    if (loader) loader.suspend(state.suspended);
    paintChrome();
  });

  hud = createHud({
    root: $('#br-room-ui'), lex, label,
    onVisit: (row) => visit(row),
    onGo: (row) => { if (scene) { scene.go(row); hud.overview(false); } },
    onOverview: (on) => { if (scene) { scene.setOverview(on); hud.overview(scene.overview); } },
    onMotion: () => { if (forcedStill()) return; state.userStill = !state.userStill; paintMotion(); },
    onOption: setOption,
  });
  paintMotion();

  const stations = await readStations();
  if (leaving) return;
  hud.stations(stations);
  loader = createLoader({
    layer: $('#br-layer'),
    state,
    lex,
    onSp: (fn) => { spListeners.add(fn); return () => spListeners.delete(fn); },
    onSettings: (fn) => { settingsListeners.add(fn); return () => settingsListeners.delete(fn); },
    spReadout,
    spChanged,
    chipSettle,
    standUp: () => back('back'),
    log: (level, msg) => bridge.log(level, msg),
  });
  // Test seam for the smoke checks (never read by the room itself).
  window.__backroom = { state, stations, loader, back, lex, visit, get scene() { return scene; } };

  try {
    scene = await createScene({
      mount: $('#br-stage'),
      stations,
      base: 'room/assets/',
      faces: 'stations/slot/assets/emi-faces-slot.png',
      ads: ADS.map((a) => ({ url: 'room/assets/ads/' + a.file + '.webp', caption: lex(a.key, a.fallback) })),
      label: (row, key) => (key === '@name' ? label(row) : lex(key, LABEL_FALLBACK[key])),
      media,
      still: still(),
      onProgress: (f) => hud.progress(f),
      onNearest: (row) => hud.nearest(row),
      onVisit: (row) => visit(row),
      log: (msg) => bridge.log('warn', msg),
    });
  } catch (e) {
    bridge.log('error', 'room build failed: ' + ((e && e.stack) || e));
    hud.failed(lex('br_station_closed', 'Closed for a moment.'));
    return;
  }
  if (leaving) { scene.halt(); return; }
  // M toggles the view inside the scene; keep the HUD in step after it has.
  window.addEventListener('keydown', (e) => { if (e.code === 'KeyM') setTimeout(() => hud.overview(scene.overview), 0); });
  if (state.suspended) scene.pause(true);
  paintMotion();
  hud.ready();
  document.documentElement.classList.add('br-ready');
  bridge.log('info', 'room up: ' + stations.length + ' fixtures, ' + stations.filter((s) => s.state === 'live').length
    + ' live, built in ' + Math.round(scene.buildMs) + ' ms');
}

wireExits();
paintChrome();
bridge.once('init', (m) => {
  start(m).catch((e) => bridge.log('error', 'boot failed: ' + ((e && e.stack) || e)));
});
bridge.announceReady();
