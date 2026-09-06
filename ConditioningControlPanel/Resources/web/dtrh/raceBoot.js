/* ============================================================================
 * raceBoot.js - boots Racing Thoughts page. Implements CONTRACT.md
 * "race/run.js + raceBoot.js + race.html (PR 5, integration)" and the
 * "race/menu.js + race/intro.js" section (the front door).
 *
 * Order: capability detect -> quality tier -> bridge handlers -> announceReady
 * -> wait for `init` (+ `manifest`, `favorites`) with a 4 s timeout -> hostMedia
 * -> createRace + createMenu -> the splash is a 1 s title flash (it covers the
 * module import, the world build and the glb fetch, and hosts the wait note
 * and boot errors) -> on a first open the four introduction cards
 * (race/cards.js) -> the MENU is the resting state -> `race` plays the intro
 * on the menu stage -> the run starts under the camera whip. `surface` from
 * the menu is the same exit the End screen takes.
 *
 * Host messages owned here: init, manifest, favorites, ping, exit-request,
 * fullscreen (run.js owns pause + payout-result). Sent here: pong, boot-error,
 * fullscreen-set, exit + exit-done on a host exit-request or a menu surface.
 *
 * STANDALONE DEV MODE (no WebView2): `bridge.isHosted` is false, so `init` is
 * synthesised (masterVolume 60, reducedMotion from matchMedia, empty manifest)
 * and every would-be host message is logged to the console with a
 * `[race->host]` prefix. Query switches: `?autostart=1` skips the menu AND the
 * intro and boots straight into the run (headless checks depend on it);
 * `?intro=0` keeps the menu and skips the intro; `?scene=intro` boots straight
 * into the intro and `?hold=ms` freezes it at that intro time (screenshots);
 * `?cards=1` forces the introduction cards and `?cards=0` skips them (without
 * either they show once, gated on localStorage `race.cards`), and `?card=N`
 * opens them on card N (1..4, a screenshot aid the way `?hold=` is one);
 * `?pixel=N` (0 = off) beats `race.options` which beats `settings.pixel` from
 * the host init.
 *
 * TRACK CHARTS (CHART.md): the host posts track-progress / track-chart /
 * track-clock / track-ended / track-error and this file hands them to the run.
 * Standalone there is no host, so `?chart=demo&dur=240` builds the demo chart and
 * `?chart=<url>` fetches one; `?audio=<url>` plays an <audio> element and its
 * currentTime is the clock, and without it the clock is wall time. Either way the
 * page ticks race.trackClock on the host's own 250 ms cadence.
 * ==========================================================================*/

import * as bridge from './bridge.js';
import { detectMode } from './shared/capability.js';
import { setQuality } from './shared/quality.js';
import { createHostMediaSource } from './hostMedia.js';

const INIT_TIMEOUT_MS = 4000, SPLASH_MS = 1000, TRACK_TICK_MS = 250;
const params = new URLSearchParams(location.search);
const hosted = bridge.isHosted;
const host = hosted ? bridge : {
  on: bridge.on,
  isHosted: false,
  send: (m) => { try { console.log('[race->host]', JSON.stringify(m)); } catch (e) { console.log('[race->host]', m); } },
  log: (m) => console.log('[race->host] log', String(m)),
  announceReady: () => console.log('[race->host] ready (standalone)'),
};

const root = document.getElementById('race-root');
const hudRoot = document.querySelector('#race-root .race-hud');
const splash = document.getElementById('race-splash');
const waitEl = document.getElementById('race-wait');
const media = createHostMediaSource();
const rollSeed = () => (Date.now() ^ Math.floor(Math.random() * 0x7fffffff)) >>> 0;
let initMsg = null, haveManifest = false, race = null, menu = null, booted = false, started = false, exiting = false;
let settings = {}, seed = 0;
let trackProgress = null, trackReady = null, errorTimer = 0, startTrackClock = null, trackTimer = 0, trackAudio = null;

const note = (t) => { if (waitEl) waitEl.textContent = t || ''; };
function fail(err) {
  const msg = String((err && (err.stack || err.message)) || err || 'unknown').slice(0, 600);
  console.error('[race] boot-error', msg);
  note('something broke. the host has the log.');
  host.send({ type: 'boot-error', msg, message: msg });
}
window.addEventListener('error', (e) => {
  const src = e.filename ? ` @ ${String(e.filename).split('/').pop()}:${e.lineno}` : '';
  if (!started) fail((e.message || 'script error') + src);
  else host.log('error: ' + (e.message || 'script error') + src);
});
window.addEventListener('unhandledrejection', (e) => {
  const r = e.reason;
  const msg = (r && (r.stack || r.message)) || r || 'unknown';
  if (!started) fail('promise: ' + msg); else host.log('promise: ' + msg);
});

// ---- capability -> quality tier ----
const mode = detectMode();
if (mode.hardBlock || !mode.canTry3d) {
  fail('no webgl here: ' + (mode.reason || mode.mode));
} else {
  setQuality(mode.tier);
}

// ---- host wiring ----
bridge.on('init', (m) => { initMsg = m; maybeBoot(); });
bridge.on('manifest', (m) => { try { media.setManifest(m); } catch (e) { host.log('manifest: ' + e); } haveManifest = true; maybeBoot(); });
bridge.on('favorites', (m) => { try { media.setFavorites(m && m.names || []); } catch (e) { host.log('favorites: ' + e); } });
bridge.on('ping', (m) => host.send({ type: 'pong', t: m && m.t }));
bridge.on('fullscreen', (m) => host.send({ type: 'fullscreen-set', on: !!(m && m.on) }));
bridge.on('exit-request', surface);
// ---- track charts (CHART.md host protocol). The run owns the clock; this only relays. ----
bridge.on('track-chart', (m) => {
  if (!race || !m || !m.chart) return;
  try { (race.track && started) ? race.replaceTrack(m.chart) : race.setTrack(m.chart); }
  catch (err) { host.log('track-chart: ' + ((err && err.message) || err)); trackError(String((err && err.message) || err)); return; }
  const t = race.track, st = race.trackStats ? race.trackStats() : null;
  trackReady = t ? { stage: 'ready', name: t.name, durationSec: t.durationSec, countable: st ? st.countable : 0, partial: !!m.partial } : null;
  plate(trackReady);
});
bridge.on('track-clock', (m) => { if (race && m) race.trackClock(Number(m.t) || 0, m.playing !== false); });
bridge.on('track-ended', () => { if (race) race.trackEnded(); });
bridge.on('track-error', (m) => trackError((m && m.message) || 'the track would not load'));
bridge.on('track-progress', (m) => {
  trackProgress = m || null;
  host.log(`track-progress: ${(m && m.stage) || '?'} ${Math.round(((m && m.pct) || 0) * 100)}% ${(m && m.name) || ''}`);
  if (!m) return;
  // a cancelled dialog leaves whatever was loaded before in place; any other stage is the host at work
  plate(m.stage === 'cancelled' ? trackReady : { stage: m.stage, pct: m.pct, name: m.name });
});
/** The menu's plate, when there is a menu to show it on (the run has the toast instead). */
function plate(state) { try { if (menu && !started) menu.setTrack(state); } catch (e) { host.log('plate: ' + e); } }
function trackError(message) {
  host.log('track-error: ' + message);
  try { if (race && race.hud) race.hud.toast(String(message).slice(0, 60).toLowerCase(), 'effect'); } catch (e) { /* no hud yet */ }
  plate({ stage: 'error', message: String(message).slice(0, 80).toLowerCase() });
  errorTimer = setTimeout(() => plate(trackReady), 4000);
}
/** The one exit: the menu's `surface` and the host's exit-request (the End screen's own goes through run.js). */
function surface() {
  if (exiting) return;
  exiting = true;
  stopTrackClock();
  if (errorTimer) clearTimeout(errorTimer);
  try { if (menu) menu.dispose(); } catch (e) { host.log('menu dispose: ' + e); }
  try { if (race) race.dispose(); } catch (e) { host.log('exit dispose: ' + e); }
  host.send({ type: 'exit' });
  host.send({ type: 'exit-done' });
}

function synthInit() {
  return {
    type: 'init', protocol: 1, modId: null, modContent: null,
    settings: { masterVolume: 60, reducedMotion: !!(matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches) },
  };
}

function maybeBoot() {
  if (booted || !initMsg || !haveManifest) return;
  boot();
}

async function boot() {
  if (booted) return;
  booted = true;
  const t0 = performance.now();
  try {
    const [{ createRace }, { createMenu, loadOptions, seedFromOptions, wantsReducedMotion }] = await Promise.all([import('./race/run.js'), import('./race/menu.js')]);
    const opts = loadOptions();
    settings = { ...((initMsg && initMsg.settings) || {}) };
    if (opts.pixel !== undefined) settings.pixel = opts.pixel;
    if (params.has('pixel') && params.get('pixel') !== '') settings.pixel = Number(params.get('pixel'));
    settings.reducedMotion = wantsReducedMotion(opts, settings.reducedMotion != null ? settings.reducedMotion : !!(matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches));
    settings.musicVolume = opts.music; settings.sfxVolume = opts.sfx;   // for the audio pass; audio.js reads masterVolume today
    settings.seedLock = seedFromOptions(opts);
    settings.trackPick = hosted;   // the file dialog is the host's; standalone loads a track off the query string
    seed = settings.seedLock != null ? settings.seedLock : rollSeed();
    note('the road is drawing');
    race = createRace({ root, bridge: host, media, settings, seed });
    note('');
    host.log(`race booted: seed ${seed} (${opts.seed}), tier ${mode.tier}, hosted ${hosted}, manifest ${haveManifest}`);
    await standaloneTrack();
    if (params.get('autostart') === '1') { startRun(false); return; }
    if (hudRoot) hudRoot.classList.add('is-lobby');   // the run's chrome stays out of the menu and the intro
    menu = createMenu({ root, renderer: race.renderer, pixel: race.pixel, audio: race.audio, settings, log: host.log });
    menu.onPick((id) => {
      if (id === 'race') startRun(true);
      else if (id === 'surface') surface();
      else if (id === 'track') { plate({ stage: 'picking' }); host.send({ type: 'track-pick' }); }
      else if (id === 'clear') { host.send({ type: 'track-cancel' }); race.setTrack(null); trackReady = null; plate(null); }
      else if (id === 'story') { menu.hide(); menu.refreshView(); showCards().then(() => { if (!exiting && !started) menu.show(); }); }
    });
    if (trackReady) plate(trackReady); else if (trackProgress && trackProgress.stage !== 'cancelled') plate(trackProgress);
    menu.seedCheck = () => {   // the menu may have changed the seed rule: rebuild the world once, before the intro
      const lock = seedFromOptions(menu.options);
      if (lock === settings.seedLock) return;
      settings.seedLock = lock; seed = lock != null ? lock : rollSeed();
      race.reseed(seed);
      host.log(`race reseeded: ${seed} (${menu.options.seed})`);
    };
    race.setStage(menu.stage);
    if (params.get('scene') === 'intro') { startRun(true); return; }
    setTimeout(() => { hideSplash(); firstCards(); }, Math.max(0, SPLASH_MS - (performance.now() - t0)));
  } catch (err) {
    fail(err);
  }
}

/**
 * No host, but a track was asked for on the query string: `?chart=demo&dur=240` builds the demo
 * chart, `?chart=<url>` fetches one. `?audio=<url>` plays the file and its currentTime becomes the
 * clock; without it the clock is wall time from the moment the run starts. Nothing here runs hosted.
 */
async function standaloneTrack() {
  const want = params.get('chart');
  if (hosted || !want || !race) return;
  let chart = null;
  try {
    const mod = await import('./race/chart.js');
    if (want === 'demo') chart = mod.demoChart({ durationSec: Number(params.get('dur')) || 240 });
    else chart = mod.normalizeChart(await (await fetch(want)).json());
    race.setTrack(chart);
    const st = race.trackStats ? race.trackStats() : null;
    trackReady = { stage: 'ready', name: chart.source.name, durationSec: chart.source.durationSec, countable: st ? st.countable : 0, partial: false };
  } catch (err) {
    trackError('chart: ' + ((err && err.message) || err));
    return;
  }
  const src = params.get('audio');
  if (src) {
    try { trackAudio = new Audio(src); trackAudio.preload = 'auto'; } catch (e) { trackAudio = null; }
  }
  const dur = chart.source.durationSec;
  startTrackClock = () => {
    if (trackTimer) return;
    if (trackAudio) { const p = trackAudio.play(); if (p && p.catch) p.catch((e) => host.log('track audio: ' + e)); }
    race.trackClock(0, true);
    trackTimer = setInterval(() => {
      // with audio the file is the authority and its currentTime snaps the clock. Without it the run
      // integrates the wall itself (race/track.js), and a second clock here would only race it, so
      // the tick just watches for the end. Same 250 ms cadence either way, same as the host's.
      if (trackAudio) race.trackClock(trackAudio.currentTime, !trackAudio.paused);
      if ((race.track ? race.track.t : dur) >= dur) { stopTrackClock(); race.trackEnded(); }
    }, TRACK_TICK_MS);
  };
  host.log(`track: ${chart.source.name}, ${Math.round(dur)}s, clock ${trackAudio ? 'audio' : 'wall'}`);
}
function stopTrackClock() {
  if (trackTimer) { clearInterval(trackTimer); trackTimer = 0; }
  if (trackAudio) { try { trackAudio.pause(); } catch (e) { /* already gone */ } }
}

function hideSplash() {
  splash.classList.add('is-off');
  setTimeout(() => { splash.hidden = true; }, 600);
}
/**
 * The four introduction cards (race/cards.js) on the menu's own stage framing. Resolves when they
 * end, read through or escaped; either way they count as seen. A card layer that will not build is
 * never worth the front door, so it is logged and swallowed.
 */
async function showCards() {
  try {
    const { createCards } = await import('./race/cards.js');
    const reducedMotion = menu ? (menu.options.motion === 'on' || (menu.options.motion === 'system' && settings.reducedMotion)) : !!settings.reducedMotion;
    const cards = createCards({ root, audio: race && race.audio, reducedMotion, log: host.log, start: (Number(params.get('card')) || 1) - 1 });
    await cards.show();   // show() writes the gate itself, so a window closed mid-read still counts
    cards.dispose();
  } catch (err) {
    host.log('cards: ' + ((err && err.message) || err));
  }
}
/** First open: the cards, then the menu. `?cards=1` forces them, `?cards=0` says never. */
async function firstCards() {
  const want = params.get('cards');
  if (want !== '0' && !started) {
    try {
      const { cardsSeen } = await import('./race/cards.js');
      if (want === '1' || !cardsSeen()) { menu.refreshView(); await showCards(); }
    } catch (err) { host.log('cards gate: ' + ((err && err.message) || err)); }
  }
  if (!exiting && !started) menu.show();
}
/** race: the intro on the menu stage, then the run under the camera whip. autostart / intro=0 go straight to the run. */
async function startRun(withIntro) {
  if (started || !race) return;
  started = true;
  hideSplash();
  try {
    if (menu) { menu.hide(); menu.seedCheck(); }
    if (menu && withIntro && params.get('intro') !== '0') {
      const { createIntro, cameraWhip } = await import('./race/intro.js');
      const reducedMotion = menu.options.motion === 'on' || (menu.options.motion === 'system' && settings.reducedMotion);
      const intro = createIntro({ stage: menu.stage.live, hud: race.hud, audio: race.audio, reducedMotion, log: host.log });
      const hold = Number(params.get('hold')) / 1000;   // screenshot aid: freeze the intro at that intro time
      race.setStage(hold > 0 ? { update(dt) { if (intro.time < hold) intro.update(dt); }, render: intro.render } : intro);
      await intro.play();
      if (exiting) return;
      race.setStage(null);
      intro.dispose();
      if (hudRoot) hudRoot.classList.remove('is-lobby');
      race.start();
      if (startTrackClock) startTrackClock();
      race.setCameraOverride(cameraWhip(0.8));
    } else {
      race.setStage(null);
      if (hudRoot) hudRoot.classList.remove('is-lobby');
      race.start();
      if (startTrackClock) startTrackClock();
    }
  } catch (err) {
    host.log('start: ' + ((err && err.stack) || err));
    try { race.setStage(null); race.start(); } catch (e) { fail(e); }
  }
}

// ---- go ----
bridge.announceReady();
host.log('race: ready posted, waiting for init + manifest');
if (!hosted) {
  initMsg = synthInit();
  media.setManifest({ images: [], videos: [], skipped: 0, truncated: false });
  haveManifest = true;
  maybeBoot();
} else {
  setTimeout(() => {
    if (booted) return;
    host.log('race: init timeout, booting with ' + (initMsg ? 'init' : 'defaults') + (haveManifest ? '' : ', no manifest'));
    if (!initMsg) initMsg = synthInit();
    boot();
  }, INIT_TIMEOUT_MS);
}
