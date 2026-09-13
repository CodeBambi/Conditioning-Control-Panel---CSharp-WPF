/* ============================================================================
 * backroom/room/main.js - boot, the Back button, and the three ways out.
 *
 * Boot: subscribe, post `ready`, wait for `init`, read stations.json, draw the
 * room. The Back button and Escape are wired BEFORE any of that, so the room
 * can be left at every frame, including a boot that never finishes (Law VI).
 *
 * Leaving:
 *   Back / Escape with a station open  -> the station closes, the room stays.
 *   Back / Escape in the room          -> `exit`, then `exit-done` once settled.
 *   host `close` (app exit, panic)     -> settle inside 300 ms, `exit-done`.
 * ==========================================================================*/

import * as bridge from '../bridge.js';
import { normaliseStations } from './geometry.js';
import { createScene } from './scene.js';
import { createLoader } from './loader.js';

const PAGE_SETTLE_MS = 300;

const state = { sp: 0, reduced: false, motion: 'full', intensity: 'normal', lex: {}, open: null, suspended: false };
const spListeners = new Set();
let scene = null;
let loader = null;
let leaving = false;

const $ = (sel) => document.querySelector(sel);

function lex(key, fallback) {
  const v = state.lex && state.lex[key];
  return (typeof v === 'string' && v && v !== key) ? v : (fallback == null ? key : fallback);
}

function paintChrome() {
  const back = $('#br-back');
  back.textContent = lex('br_back', 'Back');
  back.setAttribute('aria-label', lex('br_back', 'Back'));
  $('#br-sp-label').textContent = lex('br_balance', 'SP');
  $('#br-sp-value').textContent = String(state.sp);
  document.title = lex('br_room_title', 'The Back Room');
  document.documentElement.classList.toggle('br-reduced', !!state.reduced);
  document.documentElement.classList.toggle('br-suspended', !!state.suspended);
}

function setSp(sp) {
  if (!Number.isFinite(sp)) return;
  state.sp = sp;
  paintChrome();
  for (const fn of Array.from(spListeners)) { try { fn(sp); } catch (e) { bridge.log('warn', 'onSp threw: ' + e); } }
}

/** Back, from anywhere. A station closes first; an empty room is left. */
async function back(reason) {
  if (leaving) return;
  if (loader && loader.current) {
    await loader.close();
    return;
  }
  leave(reason || 'back');
}

async function leave(reason) {
  if (leaving) return;
  leaving = true;
  document.documentElement.classList.add('br-leaving');
  bridge.send({ type: 'exit', reason });
  if (scene) scene.halt();
  if (loader) await loader.close(PAGE_SETTLE_MS - 60);
  bridge.send({ type: 'exit-done' });
}

function wireExits() {
  $('#br-back').addEventListener('click', () => back('back'));
  window.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    e.preventDefault();
    back('key');
  }, true);
  bridge.on('close', async () => {
    if (leaving) return;
    leaving = true;
    if (scene) scene.halt();
    if (loader) await loader.close(PAGE_SETTLE_MS - 60);
    bridge.send({ type: 'exit-done' });
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

async function start(init) {
  Object.assign(state, {
    sp: Number.isFinite(init.sp) ? init.sp : 0,
    reduced: !!init.reduced,
    motion: String(init.motion || 'full'),
    intensity: String(init.intensity || 'normal'),
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
    if (scene) scene.setReduced(state.reduced);
    paintChrome();
  });
  bridge.on('suspend', (m) => {
    state.suspended = !!m.on;
    if (scene) scene.pause(state.suspended);
    if (loader) loader.suspend(state.suspended);
    paintChrome();
  });

  const stations = await readStations();
  if (leaving) return;
  loader = createLoader({
    layer: $('#br-layer'),
    state,
    lex,
    onSp: (fn) => { spListeners.add(fn); return () => spListeners.delete(fn); },
    standUp: () => back('back'),
    log: (level, msg) => bridge.log(level, msg),
  });
  scene = createScene({
    mount: $('#br-stage'),
    stations,
    label: (s) => lex(s.labelKey, s.id),
    reduced: state.reduced,
    onArrive: (s) => { if (!leaving) loader.open(s); },
    log: (msg) => bridge.log('warn', msg),
  });
  document.documentElement.classList.add('br-ready');
  bridge.log('info', 'room up: ' + stations.length + ' stations, ' + stations.filter((s) => s.state === 'live').length + ' live');

  // Test seam for the smoke checks (never read by the room itself).
  window.__backroom = { state, stations, scene, loader, back, lex };
}

wireExits();
paintChrome();
bridge.once('init', (m) => {
  start(m).catch((e) => bridge.log('error', 'boot failed: ' + ((e && e.stack) || e)));
});
bridge.announceReady();
