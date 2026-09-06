/* ============================================================================
 * raceBoot.js - boots The Caucus Race page. Implements CONTRACT.md
 * "race/run.js + raceBoot.js + race.html (PR 5, integration)".
 *
 * Order: capability detect -> quality tier -> bridge handlers -> announceReady
 * -> wait for `init` (+ `manifest`, `favorites`) with a 4 s timeout -> hostMedia
 * -> createRace -> start on the first key / pointer / gamepad press.
 *
 * Host messages owned here: init, manifest, favorites, ping, exit-request,
 * fullscreen (run.js owns pause + payout-result). Sent here: pong, boot-error,
 * fullscreen-set, exit + exit-done on a host exit-request.
 *
 * STANDALONE DEV MODE (no WebView2): `bridge.isHosted` is false, so `init` is
 * synthesised (masterVolume 60, reducedMotion from matchMedia, empty manifest)
 * and every would-be host message is logged to the console with a
 * `[race->host]` prefix. `?autostart=1` skips the splash.
 * ==========================================================================*/

import * as bridge from './bridge.js';
import { detectMode } from './shared/capability.js';
import { setQuality } from './shared/quality.js';
import { createHostMediaSource } from './hostMedia.js';

const INIT_TIMEOUT_MS = 4000;
const params = new URLSearchParams(location.search);
const hosted = bridge.isHosted;
const host = hosted ? bridge : {
  on: bridge.on,
  send: (m) => { try { console.log('[race->host]', JSON.stringify(m)); } catch (e) { console.log('[race->host]', m); } },
  log: (m) => console.log('[race->host] log', String(m)),
  announceReady: () => console.log('[race->host] ready (standalone)'),
};

const root = document.getElementById('race-root');
const splash = document.getElementById('race-splash');
const waitEl = document.getElementById('race-wait');
const media = createHostMediaSource();
let initMsg = null, haveManifest = false, race = null, booted = false, started = false, exiting = false;

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
bridge.on('exit-request', () => {
  if (exiting) return;
  exiting = true;
  try { if (race) race.dispose(); } catch (e) { host.log('exit dispose: ' + e); }
  host.send({ type: 'exit' });
  host.send({ type: 'exit-done' });
});

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
  try {
    const settings = (initMsg && initMsg.settings) || {};
    const seed = (Date.now() ^ Math.floor(Math.random() * 0x7fffffff)) >>> 0;
    note('the road is drawing');
    const { createRace } = await import('./race/run.js');
    race = createRace({ root, bridge: host, media, settings, seed });
    note('');
    host.log(`race booted: seed ${seed}, tier ${mode.tier}, hosted ${hosted}, manifest ${haveManifest}`);
    if (params.get('autostart') === '1') startRace();
  } catch (err) {
    fail(err);
  }
}

function startRace() {
  if (started || !race) return;
  started = true;
  splash.classList.add('is-off');
  setTimeout(() => { splash.hidden = true; }, 600);
  race.start();
}
// "press any key": keyboard, pointer, or a gamepad button (polled while the splash is up)
window.addEventListener('keydown', (e) => { if (!e.repeat && race && !started) startRace(); });
splash.addEventListener('pointerdown', () => { if (race && !started) startRace(); });
(function pollPad() {
  if (started) return;
  const pads = navigator.getGamepads ? navigator.getGamepads() : null;
  if (pads && race) for (const g of pads) if (g && g.buttons.some((b) => b && b.pressed)) { startRace(); return; }
  requestAnimationFrame(pollPad);
})();

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
