import { intro } from './intro.js';
import { createRacePortal, consumeRoomPose } from './race-portal.js';
import { sliceText } from '../stations/wheel/rewards.js';
import { setWheelFace } from './wheel-face.js';
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
 *   A tap on the room while seated, or a
 *   step back (S, down, the stick)      -> the same close, through the same Back.
 *   Back / Escape in the room view     -> back to walking.
 *   Back / Escape in the room          -> `exit`, then `exit-done` once settled.
 *   host `close` (app exit, panic)     -> settle inside 300 ms, `exit-done`.
 *
 * THE FLOOR BELL (CONTRACT 10.16.B). `GET bell/state` on room open and again
 * after each station close. NEVER polled, and never while a station holds the
 * screen: the rotation in hud.js is a text timer that makes no request. The
 * same reply carries the opt-in, which is one more switch row inside the 10.14
 * Options panel, and, when the server room lane sends it, the wheel's must-hit
 * flag (10.16.E).
 * ==========================================================================*/

import * as bridge from '../bridge.js';
import { prizeState } from '../shared/prize-state.js';
import { normaliseStations } from './walk.js';
import { createScene } from './scene.js';
import { createLoader } from './loader.js';
import { createHud } from './hud.js';
import { createRoomRewards, createDoubleCharm } from './rewards.js';
import { kit } from '../shared/sound/kit.js';
import { getMusic } from '../shared/sound/music.js';
import { quality } from '../shared/quality.js';
import { spendFlight, clearSpendFlights } from './spend-flight.js';
import { createBalanceFeedback } from './balance-feedback.js';
const rewards = createRoomRewards();
let doubleCharm = null, music = null;
function applyRewards(body) { if (leaving) return; if (rewards.apply(body)) { scene?.setRewards(rewards.snapshot()); doubleCharm?.paint(); } }

const PAGE_SETTLE_MS = 300;
const BELL_TIMEOUT_MS = 6000;
const LABEL_FALLBACK = { br_room_label_jackpot: 'A little luck', br_room_label_status: 'Pull me', br_room_label_wheel_status: 'Your daily detour' };
/** The wheel fixture's own screen, the room's jackpot chip while the pot must fall (10.16.E). */
const WHEEL_SCREEN = Object.freeze({ key: 'wheel', node: 'status_screen', key_off: 'br_room_label_wheel_status' });
const ADS = [
  { file: 'arcademy', key: 'br_ad_arcademy', fallback: 'The Arcademy' },
  { file: 'dtrh', key: 'br_ad_dtrh', fallback: 'Down the Rabbit Hole' },
  { file: 'focus-gaze', key: 'br_ad_focus_gaze', fallback: 'Focus Gaze' },
];

/** CONTRACT 10.13.A + 10.14: the host's hypno toggles and the room's own tunnel and melt switches. A missing frame or
 * key reads as on (the host is the enforcer). `tunnel` is the Back Room's own tunnel vision toggle (owner, 2026-09-14):
 * fx-tunnel reads it, not brainDrain. */
const readGates = (g) => {
  const q = g && typeof g === 'object' ? g : {};
  return Object.freeze({ flash: q.flash !== false, subliminal: q.subliminal !== false, spiral: q.spiral !== false,
    brainDrain: q.brainDrain !== false, tunnel: q.tunnel !== false, melt: q.melt !== false });
};
const INTENSITIES = ['calm', 'normal', 'full'];
const readChoice = (v, fallback) => (INTENSITIES.includes(v) ? v : fallback);

const state = { sp: 0, reduced: false, motion: 'full', intensity: 'normal', intensityChoice: 'normal', gates: readGates(null), lex: {}, open: null, suspended: false, userStill: false,
  // The room's own picture source and mix, both owned by the host. These defaults only hold for the
  // few frames before init lands, and they are the quiet ones on purpose.
  media: { source: 'auto', effective: 'local', subs: [], off: [], cap: 8, consented: false },
  levels: { sub: 1, sfx: 1, music: 0.15 } };
/** The floor bell: what the last `bell/state` said. Never a timer, never a poll. */
const bell = { entries: [], optIn: false, mustHit: false, fetching: false, fetches: 0 };
const spListeners = new Set();
const settingsListeners = new Set();
let scene = null, loader = null, hud = null, leaving = false, visiting = false;

let racingOwnership = null, raceOpening = false;
const previewHost = typeof window.__brSettings === 'object' && typeof window.__hostEmit === 'function';
const racePortal = createRacePortal({
  hosted: !previewHost, send: bridge.send, on: bridge.on,
  getPose: () => scene?.navigationPose(), racePath: '/backroom/racing/race.html',
  beforeNavigate: () => { leaving = true; hud?.stop(); doubleCharm?.dispose(); scene?.halt(); kit.dispose(); window.__fxCancelAll?.(); },
  onRefused: m => { raceOpening = false; scene?.release(); if (m.reason === 'locked') visit(window.__backroom.stations.find(r => r.id === 'counter')); },
});
function applyPrizes(body, bought) {
  const snapshot = prizeState(body);
  if (!snapshot) return;
  racingOwnership = snapshot;
  scene?.setPrizes(snapshot, bought);
}
async function openRace() {
  if (leaving || visiting || raceOpening || !scene || scene.seated || scene.transitioning || loader?.current) return false;
  raceOpening = true;
  if (!previewHost) {
    // Native access is canonical even when the prize counter is temporarily closed.
    scene.hold();
    if (!racePortal.open()) { scene.release(); raceOpening = false; return false; }
    return true;
  }
  try {
    // Refresh access before leaving; no station can be abandoned with a paid result pending.
    const reqId = bridge.mintId();
    const res = await bridge.request({type:'station-request', reqId, station:'counter', op:'state', body:{}},
      'station-result', m => m.reqId === reqId, BELL_TIMEOUT_MS);
    if (leaving || visiting || scene.seated || loader?.current) return false;
    const freshOwnership = res?.ok ? prizeState(res.body) : null;
    if (freshOwnership) applyPrizes(res.body);
    if (!freshOwnership?.racing) {
      raceOpening = false;
      await visit(window.__backroom.stations.find(r => r.id === 'counter'));
      return false;
    }
    scene.hold();
    if (!racePortal.open()) { scene.release(); return false; }
    return true;
  } finally { if (!racePortal.pending) raceOpening = false; }
}

const $ = (sel) => document.querySelector(sel);

function lex(key, fallback) {
  const v = state.lex && state.lex[key];
  return (typeof v === 'string' && v && v !== key) ? v : (fallback == null ? key : fallback);
}
const label = (row) => lex(row.labelKey, row.name);
const forcedStill = () => !!state.reduced || state.intensity === 'calm';
const still = () => forcedStill() || state.userStill;

function paintChrome() {
  intro.configure(lex, still() || state.motion === 'off' || state.motion === 'still' || state.motion === 'reduced');
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
const balanceFeedback = createBalanceFeedback({ target: () => $('.br-sp'), still: () => still() || state.motion === 'off' });
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
  balanceFeedback.update(Number(v));
}
const spReadout = Object.freeze({
  /** A number shown as is (a BANK tick), or null to go back to the rule. */
  set(value) { const n = Number(value); chip.shown = value == null || !Number.isFinite(n) ? null : n; paintSpChip(); },
  /** The pays still on the tape: a number, or a reader the room calls on each repaint while the station is open. */
  owe(n) { chip.owed = typeof n === 'function' ? n : (Number(n) || 0); paintSpChip(); },
  /**
   * THE THUD: a bank token landing on the chip. Reduced motion lights it instead of scaling it.
   *
   * THE GLOW IS NOT HERE, and the integration pass took it back out (CONTRACT 10.22.D). The room lane
   * put a warmGlow on this chip; all four stations already fire their own, gated on `plan.glow`, and
   * three of them fire it on THIS VERY NODE (`spReadout.target()` is `.br-sp`). Doing it here as well
   * is both a double call on one frame and a law leak, because .thud() is NOT a win channel:
   * stations/cards/station.js thuds on a LOSS, stations/counter/cards.js thuds on a PRIZE PURCHASE,
   * and Brake 5 puts `plan.glow` at 0 for a melted win that still thuds. A warm gold cut on any of
   * those says LOOK, YOU WON to a player who did not. The plan decides the glow; the chip does not.
   * The roulette is the one station that glows its own +N badge instead, which is its answer to
   * 10.22.D and not an omission.
   */
  thud() {
    const box = $('.br-sp');
    if (!box || typeof box.animate !== 'function') return;
    if (state.reduced) { box.animate([{ boxShadow: '0 0 0 2px #ffcf6b' }, { boxShadow: '0 0 0 2px #ffcf6b' }], { duration: 520 }); return; }
    box.animate([{ transform: 'scale(1.3)', filter: 'brightness(2.2)' }, { transform: 'scale(.94)', offset: 0.55 }, { transform: 'scale(1)', filter: 'brightness(1)' }],
      { duration: 340, easing: 'cubic-bezier(.2,1.5,.4,1)' });
  },
  /** The chip's box, where THE BANK's tokens fly to and from. */
  target() { return $('.br-sp'); },
  spend(amount, target) { spendFlight({from:$('.br-sp'), to:target, amount, still:still() || state.motion==='off'}); },
});
/** A station is gone: a reader freezes to its last answer and any flight value is dropped. */
function chipSettle() {
  clearSpendFlights();
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
  intro.configure(lex, still() || state.motion === 'off' || state.motion === 'still' || state.motion === 'reduced');
  if(still() || state.motion==='off')clearSpendFlights();
  if (scene) scene.setStill(still());
  if (!hud) return;
  hud.motion(still(), forcedStill());
  hud.options({ intensityChoice: state.intensityChoice, forcedCalm: !!state.reduced, tunnel: state.gates.tunnel,
                melt: state.gates.melt, media: state.media, levels: state.levels });
}

/** The room's Options (10.14): tell the host and show the press at once; the host's settings frame has the last word. */
function setOption(key, value) {
  if (key === 'intensity') {
    if (!INTENSITIES.includes(value)) return;
    state.intensityChoice = value;
  } else if (key === 'tunnel' || key === 'melt') {
    state.gates = readGates({ ...state.gates, [key]: !!value });
  } else if (key === 'mediaSource') {
    // Optimistic, like the switches: paint the press now, let the settings frame correct it. The host
    // resolves 'auto' and refuses online without consent, so `effective` can come back as something
    // else entirely and the picker will say so.
    state.media = { ...state.media, source: String(value) };
  } else if (key === 'mediaSubAdd' || key === 'mediaSubRemove' || key === 'mediaSubToggle') {
    // No optimism here: the host validates the name and owns the list, and a wrong guess would show
    // a niche that is not really there. The frame comes straight back.
  } else if (key === 'subVolume' || key === 'sfxVolume' || key === 'musicVolume') {
    // Already applied by levels.preview(); this is the persist.
  } else return;
  bridge.send({ type: 'room-option', key, value });
  paintMotion();
}

/* ------------------------------------------------------------------ the room's own three levels
 * The kit carries the buses (subliminal / sfx / bed); the soundtrack is its own element; the spoken
 * word is the host's, on the app's output device. This is the one place that knows all three, so the
 * HUD gets a flat {sub, sfx, music} and does not have to.
 *
 * `music` drives two things that want different curves. The soundtrack sits at .15 because the mp3s
 * are loud; the ambience and spiral beds already sit at BED_LEVEL, 24 dB under everything. Scaling
 * both by the same number would leave the beds inaudible at the default. So the beds take the slider
 * normalised against that default: at .15 the mix is exactly what the room has always played, below
 * it everything fades together, above it the soundtrack keeps climbing and the beds stay put. */
const MUSIC_BASE = 0.15;
const bedFactor = (v) => Math.max(0, Math.min(1, v / MUSIC_BASE));

function applyLevel(key, v) {
  const level = Math.max(0, Math.min(1, Number(v) || 0));
  if (key === 'sub') kit.setSub?.(level);
  else if (key === 'sfx') kit.setSfx?.(level);
  else if (key === 'music') { music?.setVolume(level); kit.setBed?.(bedFactor(level)); }
}

function applyLevels() {
  for (const key of ['sub', 'sfx', 'music']) applyLevel(key, state.levels[key]);
}

const LEVEL_OPTION = { sub: 'subVolume', sfx: 'sfxVolume', music: 'musicVolume' };
const levels = {
  get sub() { return state.levels.sub; },
  get sfx() { return state.levels.sfx; },
  get music() { return state.levels.music; },
  /** Dragging: hear it immediately, tell nobody. */
  preview(key, v) { if (LEVEL_OPTION[key]) { state.levels[key] = v; applyLevel(key, v); } },
  /** Let go: now it is a setting. 0..100 on the wire, because that is how the host stores it. */
  commit(key, v) {
    if (!LEVEL_OPTION[key]) return;
    this.preview(key, v);
    setOption(LEVEL_OPTION[key], Math.round(Math.max(0, Math.min(1, v)) * 100));
  },
};

/** init.audio / settings.audio: three 0..1 numbers, anything else left as it was. */
function readLevels(a) {
  const out = { ...state.levels };
  for (const key of ['sub', 'sfx', 'music']) {
    const v = a?.[key];
    if (Number.isFinite(v)) out[key] = Math.max(0, Math.min(1, v));
  }
  return out;
}

/** init.media / settings.media, shape-checked; an absent field keeps what the room had. */
function readMedia(m) {
  if (!m || typeof m !== 'object') return state.media;
  const names = (v) => (Array.isArray(v) ? v.filter((s) => typeof s === 'string') : state.media.subs);
  return {
    source: typeof m.source === 'string' ? m.source : state.media.source,
    effective: typeof m.effective === 'string' ? m.effective : state.media.effective,
    subs: names(m.subs),
    off: Array.isArray(m.off) ? m.off.filter((s) => typeof s === 'string') : state.media.off,
    cap: Number.isFinite(m.cap) ? m.cap : state.media.cap,
    consented: typeof m.consented === 'boolean' ? m.consented : state.media.consented,
  };
}

function setSp(sp) {
  if (!Number.isFinite(sp)) return;
  state.sp = sp;
  paintChrome();
  for (const fn of Array.from(spListeners)) { try { fn(sp); } catch (e) { bridge.log('warn', 'onSp threw: ' + e); } }
}

async function visit(row) {
  if (leaving || visiting || !scene || !loader || scene.overview || scene.transitioning || scene.seated) return;
  if(row?.key==='race'){await openRace();return;}
  if(raceOpening)return;
  if(row?.key==='customization'){scene.customization.open();return;}
  visiting = true;
  scene.prepareVisit();
  await loader.open(row, { variant: row.variant ? { id: row.variant, name: label(row), palette: row.fixture.palette } : null });
}

async function returnToRoom() {
  visiting = false;
  await loader.close();
  if (leaving || visiting) return;
  hud.hideWhileVisiting(false);
  if (scene) scene.release();
  refreshPrizes();
  refreshBell('station-close');   // the only other time the bell is read (10.16.B)
}

/** Back, from anywhere. A station closes first, then the room view; an empty room is left. */
async function back(reason) {
  if (leaving) return;
  if (scene?.documents.close()) return;
  if (loader?.canLeave?.() === false) return;
  if(scene?.customization?.dismiss())return;
  if (loader && (loader.current || visiting)) { await returnToRoom(); return; }
  if (hud && hud.optionsOpen) { hud.closeOptions(); return; }
  if (scene && scene.overview) { scene.setOverview(false); hud.overview(false); return; }
  leave(reason || 'back');
}

async function settle() {
  intro.finish(true);
  music?.dispose();
  if (hud) hud.stop();
  doubleCharm?.dispose(); doubleCharm = null;
  if (scene) scene.halt();
  kit.dispose();   // the ambience and every voice go with the room
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

/** The ambience bed starts on the first gesture (the browser's rule for a context) and loops while the room is open. */
function wireAmbience() {
  const wake = () => { if (kit.arm()) kit.play('ambience'); };
  window.addEventListener('pointerdown', wake, { once: true, capture: true });
  window.addEventListener('keydown', wake, { once: true, capture: true });
}

function wireExits() {
  wireHudKeys();
  wireAmbience();
  $('#br-back').addEventListener('click', () => back('back'));
  window.addEventListener('keydown', (e) => {
    if (scene?.documents.opened) return;
    if (e.key !== 'Escape') return;
    e.preventDefault();
    if (scene?.dismissEmi()) return;
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

/* --------------------------------------------------------------- THE FLOOR BELL
 * CONTRACT 10.16.B. The room is the only caller: `GET bell/state` on room open
 * and after each station close, and nothing else. `seated()` is the whole fetch
 * policy, so a station holding the screen never costs a request. */

const seated = () => visiting || !!(loader && loader.current);

/** The relay, under the station id `bell` (host Ops row: GET state, POST opt). */
function bellRequest(op, body) {
  const reqId = bridge.mintId();
  return bridge.request(
    { type: 'station-request', reqId, station: 'bell', op, body: body || {} },
    'station-result', (m) => m.reqId === reqId, BELL_TIMEOUT_MS);
}

/** The wheel fixture's screen and the bell's standing line, both off `bell.mustHit` (10.16.E). */
function paintMustHit() {
  if (hud) hud.bellStanding(bell.mustHit ? lex('br_wheel_must_hit_room', 'The pot has to fall today') : null);
  if (!scene || typeof scene.setLabel !== 'function') return;
  scene.setLabel(WHEEL_SCREEN.key, WHEEL_SCREEN.node,
    bell.mustHit ? lex('br_wheel_must_hit', 'MUST HIT') : lex(WHEEL_SCREEN.key_off, LABEL_FALLBACK[WHEEL_SCREEN.key_off]));
}

/** Read the bell. Refuses itself while seated (Law: never a request behind a station). */
async function refreshBell(why) {
  if (leaving || seated() || bell.fetching) return;
  bell.fetching = true;
  bell.fetches++;
  try {
    const rewardRevision = rewards.revision;
    const res = await bellRequest('state', {});
    const b = res && res.ok && res.body && typeof res.body === 'object' ? res.body : null;
    if (!b || b.ok === false) return;   // closed, too_fast, offline: keep the lines we have
    if (rewards.revision === rewardRevision) applyRewards(b);
    if (Array.isArray(b.entries)) bell.entries = b.entries;
    bell.optIn = b.optIn === true;
    bell.mustHit = !!(b.jackpot && b.jackpot.mustHit === true);
    if (leaving || !hud) return;
    hud.bell(bell.entries);
    hud.bellOptIn(bell.optIn);
    paintMustHit();
  } catch (e) {
    bridge.log('warn', 'bell ' + (why || '') + ' threw: ' + ((e && e.message) || e));
  } finally {
    bell.fetching = false;
  }
}

/** The opt-in row in the 10.14 Options panel. Not a `room-option`: the floor bell is the user's own
 * setting, so it goes straight to `bell/opt`. The tick goes on at once and the server's answer puts
 * it back if it refuses. */
async function setBellOptIn(on) {
  const want = !!on;
  bell.optIn = want;
  if (hud) hud.bellOptIn(want);
  const res = await bellRequest('opt', { on: want });
  const b = res && res.ok && res.body && typeof res.body === 'object' ? res.body : null;
  bell.optIn = b && b.ok !== false ? b.optIn === true : !want;
  if (hud) hud.bellOptIn(bell.optIn);
}

/** The wall pictures' deal, under its own station id so it never replaces a sit-down deal. */
function media() {
  const reqId = bridge.mintId();
  return bridge.request({ type: 'media-request', reqId, station: 'room', count: 8 }, 'media', (m) => m.reqId === reqId, 6000,
    { reqId, seed: 0, gifs: [], words: [], timeout: true });
}

/** Room Service uses the same authenticated station relay and balance as the games. */
async function requestDecorations(op, body = {}) {
  if (leaving) return { ok: false, reason: 'closed' };
  const reqId = bridge.mintId();
  const res = await bridge.request({ type: 'station-request', reqId, station: 'decorations', op,
    body, ...(body.idem ? { idem: body.idem } : {}) }, 'station-result', m => m.reqId === reqId, BELL_TIMEOUT_MS);
  if (leaving) return { ok: false, reason: 'closed' };
  if (res?.body && typeof res.body === 'object' && ('ok' in res.body || 'decorations' in res.body)) return res.body;
  return { ok: false, reason: res?.reason || 'offline' };
}

async function refreshPrizes() {
  const reqId = bridge.mintId();
  const res = await bridge.request({type:'station-request', reqId, station:'counter', op:'state', body:{}},
    'station-result', m => m.reqId === reqId, BELL_TIMEOUT_MS);
  // applyPrizes, not scene.setPrizes: the counter's reply is also where the racing cabinet learns
  // whether to stand up, so the snapshot has to reach racingOwnership and not only the display.
  if (!leaving && !seated() && res?.ok) applyPrizes(res.body);
}

async function start(init) {
  if (leaving) return;
  rewards.reset();
  const ownedTracks = Array.isArray(init.racingTracks) ? init.racingTracks.filter(n => Number.isInteger(n) && n >= 0 && n <= 10) : [];
  if (ownedTracks.length) racingOwnership = { owned: [], tracks: ownedTracks, demo: ownedTracks.includes(0), racing: true };
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
  state.levels = readLevels(init.audio);
  state.media = readMedia(init.media);
  music = getMusic({ master: 1 });
  applyLevels();   // the host's stored levels win over whatever the music element remembered locally
  bridge.markInitialized();
  balanceFeedback.reset(state.sp);
  paintChrome();

  bridge.on('balance', (m) => setSp(m.sp));
  bridge.on('settings', (m) => {
    state.motion = String(m.motion || state.motion);
    state.intensity = String(m.intensity || state.intensity);
    state.reduced = !!m.reduced;
    if (m.gates && typeof m.gates === 'object') state.gates = readGates(m.gates);
    state.intensityChoice = readChoice(m.intensityChoice, state.intensityChoice);
    if (m.audio) { state.levels = readLevels(m.audio); applyLevels(); }
    // THE LIVE SOURCE SWITCH (10.13.C). room/screens.js and stations/slot/station.js have listened for
    // this event since the phone build; on desktop nothing ever fired it, so the picker could not have
    // worked even once it existed. Only a change in the EFFECTIVE source counts: flipping between two
    // settings that resolve to the same pool must not throw away a dealt wall.
    if (m.media) {
      const was = state.media.effective;
      state.media = readMedia(m.media);
      if (state.media.effective !== was) {
        try { window.dispatchEvent(new Event('br-media-changed')); }
        catch (e) { bridge.log('warn', 'media change event threw: ' + e); }
      }
    }
    paintChrome();
    paintMotion();
    kit.setTrim(state.intensity === 'calm' ? 0.6 : 1);   // a quieter floor under Calm, over the three buses
    const frame = { motion: state.userStill ? 'off' : state.motion, intensity: state.intensity, reduced: state.reduced, gates: state.gates };
    for (const fn of Array.from(settingsListeners)) { try { fn(frame); } catch (e) { bridge.log('warn', 'onSettings threw: ' + e); } }
  });
  bridge.on('suspend', (m) => {
    state.suspended = !!m.on;
    music?.suspend(state.suspended);
    kit.suspend(state.suspended);   // Law VI: every voice and the ambience hold; the ambience comes back on resume
    if (scene) scene.pause(state.suspended);   // a held room stays held either way
    if (loader) loader.suspend(state.suspended);
    paintChrome();
  });

  hud = createHud({
    root: $('#br-room-ui'), lex, label, music, quality, levels,
    onVisit: (row) => visit(row),
    onGo: (row) => { if (scene) { scene.go(row); hud.overview(false); } },
    onOverview: (on) => { if (scene) { scene.setOverview(on); hud.overview(scene.overview); } },
    onMotion: () => { if (forcedStill()) return; state.userStill = !state.userStill; paintMotion();
      const frame={motion:state.userStill?'off':state.motion,intensity:state.intensity,reduced:state.reduced,gates:state.gates};
      for(const fn of Array.from(settingsListeners)){try{fn(frame);}catch(e){bridge.log('warn','onSettings threw: '+e);}}
    },
    onOption: setOption,
    onBellOpt: (on) => { setBellOptIn(on); },
  });
  paintMotion();
  hud.bellOptIn(bell.optIn);
  doubleCharm = createDoubleCharm({mount:$('.br-sp'),lex,read:now=>rewards.snapshot(now)});

  const stations = await readStations();
  if (leaving) return;
  hud.stations(stations);
  loader = createLoader({
    layer: $('#br-layer'),
    state,
    stage: (row) => {
      scene.release();
      const stage = scene.stage(row);
      if (!stage) { scene.hold(); return null; }
      hud.hideWhileVisiting(false);
      hud.seated(true);
      return { ...stage, get ready(){return stage.ready;}, dispose() { stage.dispose(); hud.seated(false); } };
    },
    approach:row=>{
      scene.release();const trip=scene.stage(row);if(!trip){scene.hold();return null;}
      hud.hideWhileVisiting(false);hud.seated(true);
      return {arrived:trip.arrived.then(ok=>{if(ok){if(row.id!=="counter")scene.hold();hud.hideWhileVisiting(row.id!=="counter");}return ok;}),
        dispose(){scene.release();trip.dispose();hud.seated(false);}};
    },
    lex,
    onSp: (fn) => { spListeners.add(fn); return () => spListeners.delete(fn); },
    onSettings: (fn) => { settingsListeners.add(fn); return () => settingsListeners.delete(fn); },
    spReadout,
    spChanged,
    chipSettle,
    prizesChanged: (body, bought) => scene?.setPrizes(prizeState(body), bought),
    rewardLanded: body => applyRewards(body),
    revealedWin: (key,amount,tier,text)=>scene?.celebrate(key,amount,tier,text),
    standUp: () => back('back'),
    log: (level, msg) => bridge.log(level, msg),
  });
  // Test seam for the smoke checks (never read by the room itself).
  window.__backroom = { openRace, state, stations, loader, back, lex, visit, bell, refreshBell, rewards, levels, get hud() { return hud; }, get scene() { return scene; } };

  try {
    scene = await createScene({
      mount: $('#br-stage'),
      stations,
      base: 'room/assets/',
      faces: 'stations/slot/assets/emi-faces-slot.png',
      ads: ADS.map((a) => ({ url: 'room/assets/ads/' + a.file + '.webp', caption: lex(a.key, a.fallback) })),
      label: (row, key) => (key === '@name' ? label(row) : lex(key, LABEL_FALLBACK[key])),
      media, lex,
      still: still(),
      cameraMotion:()=>({off:state.userStill||state.motion==='off'||state.motion==='still',reduced:state.reduced||state.motion==='reduced'||state.intensity==='calm'}),
      onProgress: (f) => { hud.progress(f); intro.progress(f); },
      onNearest: (row) => hud.nearest(row),
      onVisit: (row) => visit(row),
      // The room asking to stand up (a tap on the floor, a step back): the Back path, so the station settles first.
      onLeave: () => back('room'),
      canLeave: () => loader?.canLeave?.() !== false,
      log: (msg) => bridge.log('warn', msg),
    });
  } catch (e) {
    bridge.log('error', 'room build failed: ' + ((e && e.stack) || e));
    intro.finish(true);
    hud.failed(lex('br_station_closed', 'Closed for a moment.'));
    return;
  }
  if (leaving) { scene.halt(); return; }
  // Loading the shop is independent of the room reveal; unavailable servers leave the furnished room usable.
  scene.customization.configureShop({ request: requestDecorations, getBalance: () => state.sp,
    onBalance: sp => { if (!leaving) setSp(sp); } }).catch(e => bridge.log('warn', 'Room Service unavailable: ' + e));
  // M toggles the view inside the scene; keep the HUD in step after it has.
  window.addEventListener('keydown', (e) => { if (e.code === 'KeyM') setTimeout(() => hud.overview(scene.overview), 0); });
  if (racingOwnership) scene.setPrizes(racingOwnership);
  const returnPose = consumeRoomPose();
  if (returnPose) scene.pose(returnPose.position, returnPose.yaw, returnPose.pitch);
  if (state.suspended) scene.pause(true);
  paintMotion();
  hud.ready();
  paintMustHit();
  refreshPrizes();
  refreshBell('room-open');
  // One read on room entry paints the fixture from the same table used when seated.
  if (stations.some(row => row.id === 'wheel' && row.state === 'live') && !seated()) {
    const reqId = bridge.mintId();
    bridge.request({type:'station-request',reqId,station:'wheel',op:'state',body:{}},
      'station-result', m => m.reqId === reqId, BELL_TIMEOUT_MS).then(res => {
        if (!leaving && !seated() && res?.ok && res.body?.ok) setWheelFace(scene?.scene, res.body.slices, s=>sliceText(s,lex,n=>Number(n||0).toLocaleString()));
      }).catch(() => {});
  }
  document.documentElement.classList.add('br-ready');
  intro.finish();
  bridge.log('info', 'room up: ' + stations.length + ' fixtures, ' + stations.filter((s) => s.state === 'live').length
    + ' live, built in ' + Math.round(scene.buildMs) + ' ms');
}

wireExits();
paintChrome();
bridge.once('init', (m) => {
  start(m).catch((e) => { intro.finish(true); bridge.log('error', 'boot failed: ' + ((e && e.stack) || e)); });
});
bridge.announceReady();
