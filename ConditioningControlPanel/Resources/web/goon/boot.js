/* ============================================================================
 * boot.js — entry point for the Goon Game page (1v1 endurance duel).
 *
 * Boot order (load-bearing):
 *   error seams -> register EVERY bridge handler -> announceReady() -> the host
 *   flushes `init` (identity/caps/consent/net) then `manifest` (media URLs) ->
 *   mount the title screen. Standalone (?solo=1) the same two frames are
 *   synthesized by bridge.js, so there is exactly ONE code path below.
 *
 * The match engine (core/*, owned in parallel) mounts on top of this shell; this
 * file only owns page lifecycle: handshake, liveness, exit, fullscreen, and the
 * boot deadlines that turn a wedge into a reported failure instead of an
 * eternally spinning loader.
 *
 * NOTHING may throw at module import — WebView2 has no devtools, so a throw is a
 * silent infinite loader. Every DOM touch below is inside a function guarded on
 * `document`; the imported modules (bridge/layers/media) are side-effect free by
 * contract, which is what lets the error seams a few lines down still be first.
 * ==========================================================================*/

import * as bridge from './bridge.js';
import { media } from './exec/media.js';
import * as layers from './exec/layers.js';

/* --- ERROR SEAMS FIRST: everything after this reports instead of vanishing. */
if (typeof window !== 'undefined') {
  try {
    window.addEventListener('error', (e) => {
      const src = e.filename ? ' @ ' + String(e.filename).split('/').pop() + ':' + e.lineno : '';
      bridge.log('error: ' + (e.message || 'script error') + src);
    });
    window.addEventListener('unhandledrejection', (e) => {
      const r = e && e.reason;
      bridge.log('promise: ' + ((r && (r.message || r.stack || r)) || 'unknown'));
    });
  } catch (_e) { /* never throw at import */ }
}

const WARM_MS = 12000;   // hosted: "warming up" line in the loader
const BOOT_MS = 45000;   // hosted: give up and tell C#
const EXIT_FALLBACK_MS = 1200;
const ESC_HOLD_MS = 1200;

/* ----------------------------------------------------------------------------
 * SESSION — the page's read-only view of who we are and what we agreed to.
 * Everything downstream (lobby, draft, HUD, executors) reads THIS, never the raw
 * init frame. The auth token is deliberately NOT here: it lives inside bridge's
 * net config so no screen can accidentally render or log it.
 * -------------------------------------------------------------------------- */
export const session = {
  protocol: bridge.PROTOCOL,
  hosted: bridge.isHosted,
  solo: false,
  identity: null,     // {unifiedId, displayName, appVersion}  (normalized below)
  caps: null,         // {haptics, brainDrain, camera, video, ...}
  consent: null,      // {liveDurationSec, toyCap, payloadMinGapMs}
  match: null,        // {mode, profile, skewMs} — standalone only; the host deals real matches
  net: null,          // {serverBase, viaHost, hasToken} — never the token itself
  prefs: null,
  fullscreen: false,
  haptics: null,      // last haptics-state {enabled, toys, cap}
  manifest: null,     // {images, videos, skipped, truncated}
  initAt: 0,
  ready: false,       // init + manifest both in
};

let gotInit = false;
let gotManifest = false;
let bootSettled = false;
let warmTimer = 0;
let deadlineTimer = 0;
let exitTimer = 0;
let exiting = false;

const hasDom = () => typeof document !== 'undefined';
const el = (id) => (hasDom() ? document.getElementById(id) : null);

/* ----------------------------------------------------------------------------
 * HANDLERS — one per type (bridge.on throws on a duplicate, on purpose).
 * `net-post-result` is consumed INSIDE bridge.js; registering it here would
 * throw at wiring time. That is the intended alarm, not a bug.
 * -------------------------------------------------------------------------- */
bridge.on('init', (m) => {
  gotInit = true;
  session.solo = !!m.solo;
  // Normalize identity ONCE, here: the host sends {unifiedId, displayName,
  // appVersion} (GoonHostService.OnPageReady) and a future mobile/web host may
  // send {id, name}. Screens read session.identity.displayName and nothing else.
  const idm = m.identity || {};
  session.identity = {
    unifiedId: String(idm.unifiedId || idm.id || ''),
    displayName: String(idm.displayName || idm.name || ''),
    appVersion: String(idm.appVersion || ''),
  };
  session.caps = m.caps || null;
  session.consent = m.consent || null;
  session.match = m.match || null;
  session.prefs = m.prefs || null;
  session.fullscreen = !!m.fullscreen;
  session.initAt = Date.now();
  session.net = bridge.configureNet(m.net || {});
  bridge.log('init: ' + (session.identity.displayName || '?')
    + ' solo=' + session.solo
    + ' mode=' + (session.match && session.match.mode || '-')
    + ' net=' + (session.net.viaHost ? 'via-host' : (session.net.serverBase || 'none')));
  armDeadline();
  settle();
});

bridge.on('manifest', (m) => {
  gotManifest = true;
  session.manifest = media.setManifest(m);
  bridge.log('manifest: ' + session.manifest.images + ' images, ' + session.manifest.videos + ' videos'
    + (session.manifest.skipped ? ', ' + session.manifest.skipped + ' skipped' : ''));
  armDeadline();
  settle();
});

bridge.on('fullscreen', (m) => {
  session.fullscreen = !!(m && m.on);
  paintStatus();
});

bridge.on('ping', (m) => bridge.send({ type: 'pong', t: m && m.t }));

bridge.on('end-run', () => finishExit('end-run'));

// Reserved: the host does not post this yet (caps.haptics is false until the
// haptics-v2 overhaul merges). Wired now so the toy HUD has a seam on day one.
bridge.on('haptics-state', (m) => {
  session.haptics = m || null;
  paintStatus();
});

/* ----------------------------------------------------------------------------
 * BOOT DEADLINES — progress-aware, like DtRH: the clock restarts on every
 * milestone (script start, init, manifest). A slow-but-progressing boot (asset
 * scan, AV-scanned module downloads) must never be misread as a wedge, but a
 * boot that genuinely never lands has to FAIL LOUDLY instead of spinning: the
 * host's watchdog stays quiet while rAF keeps beating, so silence is not enough.
 * -------------------------------------------------------------------------- */
function armDeadline() {
  if (bootSettled || !bridge.isHosted) return;
  try { clearTimeout(warmTimer); clearTimeout(deadlineTimer); } catch (_e) { /* ignore */ }
  warmTimer = setTimeout(() => {
    if (bootSettled) return;
    const note = el('gg-loader-note');
    if (note) note.textContent = 'warming up' + (gotInit ? '' : ' — waiting for the app') + '…';
    bridge.log('boot: 12s with no ' + (gotInit ? 'manifest' : 'init'));
  }, WARM_MS);
  deadlineTimer = setTimeout(() => {
    if (bootSettled) return;
    bootSettled = true;
    const why = 'boot deadline: ' + (gotInit ? '' : '[no init] ') + (gotManifest ? '' : '[no manifest] ')
      + Math.round(BOOT_MS / 1000) + 's since last progress';
    bridge.log(why);
    bridge.bootError(why);
    showLoaderFailure(why);
  }, BOOT_MS);
}

function settle() {
  if (!gotInit || !gotManifest || bootSettled) return;
  bootSettled = true;
  session.ready = true;
  try { clearTimeout(warmTimer); clearTimeout(deadlineTimer); } catch (_e) { /* ignore */ }
  mountTitle();
  bridge.log('boot ok');
}

/* ----------------------------------------------------------------------------
 * TITLE PLACEHOLDER — the real screens land with the UI wave. Everything here
 * exists so a human (and the driver's probe) can see the shell is alive:
 * the logo, the pitch line, a status readout, and the #gg-boot-ok marker.
 * -------------------------------------------------------------------------- */
function mountTitle() {
  if (!hasDom()) return;
  const scr = el('scr-title');
  if (!scr) return;
  scr.replaceChildren();

  const card = document.createElement('div');
  card.className = 'gg-card';
  card.style.textAlign = 'center';

  const logo = document.createElement('img');
  logo.className = 'gg-title-logo';
  logo.src = './assets/goon_game_logo.png';
  logo.alt = 'Goon Game';
  logo.decoding = 'async';
  // A missing/blocked image must never leave a blank title screen.
  logo.addEventListener('error', () => {
    logo.remove();
    const h = document.createElement('h1');
    h.className = 'gg-grad';
    h.textContent = 'Goon Game';
    card.prepend(h);
  });
  card.appendChild(logo);

  const kicker = document.createElement('p');
  kicker.className = 'gg-title-kicker';
  kicker.textContent = '1v1 · endurance duel · first to break loses';
  card.appendChild(kicker);

  const status = document.createElement('p');
  status.className = 'gg-title-status';
  status.id = 'gg-title-status';
  card.appendChild(status);

  const ok = document.createElement('span');
  ok.id = 'gg-boot-ok';
  ok.textContent = 'boot ok';
  card.appendChild(ok);

  scr.appendChild(card);
  scr.hidden = false;
  try { document.documentElement.setAttribute('data-gg-screen', 'title'); } catch (_e) { /* ignore */ }
  const loader = el('gg-loader');
  if (loader) loader.hidden = true;
  paintStatus();
}

function paintStatus() {
  const s = el('gg-title-status');
  if (!s) return;
  const m = session.manifest;
  const parts = [
    (session.hosted ? 'hosted' : 'standalone') + (session.solo ? ' · solo' : ''),
    'init: ' + (gotInit ? 'yes' : 'waiting'),
    'media: ' + (m ? (m.images + ' img / ' + m.videos + ' vid') : '—'),
    'player: ' + ((session.identity && session.identity.displayName) || '—'),
    'net: ' + (session.net ? (session.net.viaHost ? 'via host' : (session.net.serverBase || 'none')) : '—'),
    'mode: ' + ((session.match && (session.match.mode + '/' + session.match.profile)) || '—'),
    'fullscreen: ' + (session.fullscreen ? 'on' : 'off'),
  ];
  if (session.haptics) parts.push('haptics: ' + (session.haptics.enabled ? 'on' : 'off'));
  s.textContent = parts.join('  ·  ');
  const ok = el('gg-boot-ok');
  if (ok) {
    ok.dataset.hosted = String(session.hosted);
    ok.dataset.init = String(gotInit);
    ok.dataset.manifest = String(gotManifest);
  }
}

function showLoaderFailure(msg) {
  const note = el('gg-loader-note');
  if (note) note.textContent = 'could not start — ' + String(msg).slice(0, 120);
  const ring = hasDom() ? document.querySelector('.gg-loader-ring') : null;
  if (ring) ring.remove();
}

/* ----------------------------------------------------------------------------
 * EXIT HANDSHAKE (mirrors DtRH): we ask with `exit`, the host tears the match
 * down and answers `end-run`, and only then do we answer `exit-done` — which is
 * the host's cue that the page is finished and the window can go. The 1.2s
 * fallback exists because a host that never answers must not strand the player.
 * -------------------------------------------------------------------------- */
function requestExit(why) {
  if (exiting) return;
  exiting = true;
  bridge.log('exit requested (' + why + ')');
  bridge.send({ type: 'exit' });
  try { exitTimer = setTimeout(() => finishExit('fallback'), EXIT_FALLBACK_MS); } catch (_e) { finishExit('fallback'); }
}

function finishExit(why) {
  try { clearTimeout(exitTimer); } catch (_e) { /* ignore */ }
  exiting = true;
  try { layers.stopAll(); } catch (_e) { /* best effort */ }
  bridge.log('exit-done (' + why + ')');
  bridge.send({ type: 'exit-done' });
}

/* ----------------------------------------------------------------------------
 * INPUT — Escape (hold to leave) and F11 (window mode).
 * -------------------------------------------------------------------------- */
let escTimer = 0;
let escHeld = false;

function onKeyDown(e) {
  if (e.key === 'F11') {
    // Fullscreen is C#-OWNED (borderless window), never the browser Fullscreen
    // API — that one eats the first Escape, which would break the exit ladder.
    if (bridge.isHosted) {
      e.preventDefault();
      bridge.send({ type: 'fullscreen-set', on: !session.fullscreen });
    }
    return;
  }
  if (e.key !== 'Escape' || e.repeat) return;

  // TODO(match): route through mercy. Once the match engine lands, a TAP of Esc
  // in a LIVE match must open the mercy confirm (the dignified surrender), and
  // this HOLD-to-exit path must MERCY FIRST — declare mercy, let the result
  // handshake settle, and only then request exit — so leaving the window can
  // never be a way to dodge a loss or strand the opponent waiting on a
  // countersignature. Until the engine exists, Esc only does hold-to-exit.
  escHeld = true;
  try {
    escTimer = setTimeout(() => { if (escHeld) requestExit('hold-escape'); }, ESC_HOLD_MS);
  } catch (_e) { /* ignore */ }
}

function onKeyUp(e) {
  if (e.key !== 'Escape') return;
  escHeld = false;
  try { clearTimeout(escTimer); } catch (_e) { /* ignore */ }
}

/* ----------------------------------------------------------------------------
 * LIVENESS — a beating rAF posts every ~2s for the host's wedge watchdog. If the
 * main thread locks up, the SILENCE (not this code) is the signal. Hosted only:
 * standalone there is nobody listening and no reason to burn frames.
 * -------------------------------------------------------------------------- */
function startHeartbeat() {
  if (!bridge.isHosted || typeof requestAnimationFrame !== 'function') return;
  let last = 0;
  (function beat(now) {
    if (now - last > 2000) { last = now; bridge.send({ type: 'heartbeat', t: now }); }
    requestAnimationFrame(beat);
  })(0);
}

/* ----------------------------------------------------------------------------
 * GO — only in a real page. Under node (the import sweep) this whole block is
 * skipped, so every module stays provably side-effect free.
 * -------------------------------------------------------------------------- */
if (hasDom()) {
  try {
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
  } catch (_e) { /* ignore */ }
  layers.setFxHeat(0);
  startHeartbeat();
  armDeadline();
  bridge.announceReady();
  bridge.log('boot: ready posted, waiting for init + manifest');
}

/* Handy for the play-test driver and for anything that needs the shell's guts
 * without importing it (the C# side can evaluate window.__gg.session). */
if (typeof window !== 'undefined') {
  try { window.__gg = { session, bridge, media, layers, requestExit }; } catch (_e) { /* ignore */ }
}
