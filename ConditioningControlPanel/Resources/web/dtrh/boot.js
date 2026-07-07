/* ============================================================================
 * boot.js - entry point for the in-app DtRH game page.
 *
 * Replaces the site's main.js: no age gate (the host app owns consent), no
 * drop screen (media arrives as a host manifest), no capability router (the
 * host guarantees a WebView2/Chromium with import maps - only a genuine WebGL
 * failure is handled, by reporting boot-error so C# can fall back).
 *
 * Boot order: register bridge handlers -> announceReady() -> host flushes
 * `init` + `manifest` -> start the engine. M1 boots straight into The Fall
 * (the endless descent) on the user's active preset; the Warren hub and the
 * chaos run brain mount here in later milestones.
 * ==========================================================================*/

import * as bridge from './bridge.js';
import { createHostMediaSource } from './hostMedia.js';
import { detectMode } from './shared/capability.js';

const dom = {
  canvas: document.getElementById('sf-canvas'),
  hud: document.getElementById('sf-hud'),
  loader: document.getElementById('sf-loader'),
  nope: document.getElementById('sf-nope'),
  nopeMsg: document.getElementById('sf-nope-msg'),
};

// Uncaught errors go to the host log - there are no devtools in the hosted page.
window.addEventListener('error', (e) => {
  const src = e.filename ? ` @ ${String(e.filename).split('/').pop()}:${e.lineno}` : '';
  bridge.log('error: ' + (e.message || 'script error') + src);
});
window.addEventListener('unhandledrejection', (e) => {
  const r = e.reason;
  bridge.log('promise: ' + ((r && (r.message || r.stack || r)) || 'unknown'));
});

const media = createHostMediaSource();
let engine = null;
let initMsg = null;
let haveManifest = false;
let started = false;

// Shared page state the run brain (M3+) reads; M2 just keeps it current.
export const hostState = {
  meta: null, metaRev: -1,          // latest chaos_meta snapshot from C#
  payloadCover: false,              // a native video is covering the page
  lastPayout: null,                 // last payout-result
};

const scenePromise = import('./engine/scene.js'); // download while init/manifest arrive

async function maybeStart() {
  if (started || !initMsg || !haveManifest) return;
  started = true;
  try {
    const mod = await scenePromise;
    engine = await mod.start({
      canvas: dom.canvas,
      hud: dom.hud,
      tier: detectMode().tier,
      media,
      challenge: false,   // DtRH is no-lose; the Fall's miss-death mode stays off
    });
    if (dom.loader) dom.loader.hidden = true;
    bridge.send({ type: 'run-started', mode: 'fall' });
  } catch (err) {
    bridge.log('3D boot failed: ' + (err && (err.stack || err.message) || err));
    bridge.send({ type: 'boot-error', msg: String(err && err.message || err) });
    if (dom.loader) dom.loader.hidden = true;
    if (dom.nope) dom.nope.hidden = false;
  }
}

function shutdown() {
  try { engine && engine.dispose && engine.dispose(); } catch (e) { /* best effort */ }
  engine = null;
  bridge.send({ type: 'exit-done' });
}

bridge.on('init', (m) => {
  initMsg = m;
  if (m.m2Test) import('./m2test.js').then((t) => t.run(bridge, hostState)).catch((e) => bridge.log('m2test load failed: ' + e));
  maybeStart();
});
bridge.on('manifest', (m) => { media.setManifest(m); haveManifest = true; maybeStart(); });
bridge.on('meta', (m) => { hostState.meta = m.state; hostState.metaRev = m.rev; });
bridge.on('payout-result', (m) => { hostState.lastPayout = m; bridge.log(`payout: base=${Math.round(m.baseXp)} final=${Math.round(m.finalXp)} sparks=${m.sparksEarned}${m.dryRun ? ' (dry)' : ''}`); });
bridge.on('payload-state', (m) => {
  hostState.payloadCover = m.kind === 'video' ? !!m.on : hostState.payloadCover;
  // M3 wires this into the run brain (pause spawns/clock while covered); the engine keeps
  // rendering for now - occlusion throttling is already disabled by the host's browser args.
});
bridge.on('end-run', () => shutdown());
bridge.on('ping', (m) => bridge.send({ type: 'pong', t: m.t }));

// Liveness for the host's wedge watchdog: a beating rAF posts every ~2s. If the page's
// main thread locks up, the silence (not this code) is the signal.
let lastBeat = 0;
(function beat(now) {
  if (now - lastBeat > 2000) { lastBeat = now; bridge.send({ type: 'heartbeat', t: now }); }
  requestAnimationFrame(beat);
})(0);

// Exit UX (M1): HOLD Escape ~1.2s to leave (a tap stays the engine's pause toggle).
// A radial fill on the HUD would be nicer - that arrives with the pause menu in M3.
let escDownAt = 0, escTimer = 0;
window.addEventListener('keydown', (e) => {
  if (e.key !== 'Escape' || e.repeat) return;
  escDownAt = performance.now();
  escTimer = setTimeout(() => {
    bridge.send({ type: 'exit' });
    shutdown();
  }, 1200);
});
window.addEventListener('keyup', (e) => {
  if (e.key !== 'Escape') return;
  clearTimeout(escTimer);
  escDownAt = 0;
});

bridge.announceReady();
bridge.log('boot: ready posted, waiting for init + manifest');
