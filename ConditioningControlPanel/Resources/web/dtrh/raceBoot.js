/* ============================================================================
 * raceBoot.js - boots The Caucus Race page. Implements CONTRACT.md
 * "race/run.js + raceBoot.js + race.html (PR 5, integration)" and the
 * "race/menu.js + race/intro.js" section (the front door).
 *
 * Order: capability detect -> quality tier -> bridge handlers -> announceReady
 * -> wait for `init` (+ `manifest`, `favorites`) with a 4 s timeout -> hostMedia
 * -> createRace + createMenu -> the splash is a 1 s title flash (it covers the
 * module import, the world build and the glb fetch, and hosts the wait note
 * and boot errors) -> the MENU is the resting state -> `race` plays the intro
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
 * `?pixel=N` (0 = off) beats `race.options` which beats `settings.pixel` from
 * the host init.
 * ==========================================================================*/

import * as bridge from './bridge.js';
import { detectMode } from './shared/capability.js';
import { setQuality } from './shared/quality.js';
import { createHostMediaSource } from './hostMedia.js';

const INIT_TIMEOUT_MS = 4000, SPLASH_MS = 1000;
const params = new URLSearchParams(location.search);
const hosted = bridge.isHosted;
const host = hosted ? bridge : {
  on: bridge.on,
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
/** The one exit: the menu's `surface` and the host's exit-request (the End screen's own goes through run.js). */
function surface() {
  if (exiting) return;
  exiting = true;
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
    seed = settings.seedLock != null ? settings.seedLock : rollSeed();
    note('the road is drawing');
    race = createRace({ root, bridge: host, media, settings, seed });
    note('');
    host.log(`race booted: seed ${seed} (${opts.seed}), tier ${mode.tier}, hosted ${hosted}, manifest ${haveManifest}`);
    if (params.get('autostart') === '1') { startRun(false); return; }
    if (hudRoot) hudRoot.classList.add('is-lobby');   // the run's chrome stays out of the menu and the intro
    menu = createMenu({ root, renderer: race.renderer, pixel: race.pixel, audio: race.audio, settings, log: host.log });
    menu.onPick((id) => { if (id === 'race') startRun(true); else if (id === 'surface') surface(); });
    menu.seedCheck = () => {   // the menu may have changed the seed rule: rebuild the world once, before the intro
      const lock = seedFromOptions(menu.options);
      if (lock === settings.seedLock) return;
      settings.seedLock = lock; seed = lock != null ? lock : rollSeed();
      race.reseed(seed);
      host.log(`race reseeded: ${seed} (${menu.options.seed})`);
    };
    race.setStage(menu.stage);
    if (params.get('scene') === 'intro') { startRun(true); return; }
    setTimeout(() => { hideSplash(); menu.show(); }, Math.max(0, SPLASH_MS - (performance.now() - t0)));
  } catch (err) {
    fail(err);
  }
}

function hideSplash() {
  splash.classList.add('is-off');
  setTimeout(() => { splash.hidden = true; }, 600);
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
      race.setCameraOverride(cameraWhip(0.8));
    } else {
      race.setStage(null);
      if (hudRoot) hudRoot.classList.remove('is-lobby');
      race.start();
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
