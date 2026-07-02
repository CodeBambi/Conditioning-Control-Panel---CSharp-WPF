/* ============================================================================
 * main.js — bootstrap, render loop, and the C# <-> JS host bridge.
 *
 * Boot order: attach the message bridge (buffering anything early) -> resolve
 * quality -> build renderer/scene/camera + Zones/Tunnel/Fog/Rail/Powerups ->
 * flush buffered messages -> post {ready} -> start the loop. The host
 * (ChaosTunnelService) drives run-start / run-end / zone-hint / video-playing
 * over the bridge; the page posts sfx / powerup-click / exit-done back.
 * ==========================================================================*/
import * as THREE from 'three';
import { Q, setQuality, autoTier } from './quality.js';
import { Zones } from './zones.js';
import { Tunnel } from './tunnel.js';
import { Fog } from './fx.js';
import { Rail } from './camera.js';
import { Powerups } from './powerups.js';
import { Sfx } from './audio.js';

// ---- host bridge ----------------------------------------------------------
const wv = (window.chrome && window.chrome.webview) ? window.chrome.webview : null;
function post(msg) { try { if (wv) wv.postMessage(msg); } catch (_) {} }
const sfxBridge = new Sfx(post);
const host = { post: (type, data) => post(Object.assign({ type }, data || {})), sfx: (n, s) => sfxBridge.play(n, s) };

const params = new URLSearchParams(location.search);
const forcedTier = params.get('tier');
const demoMode = params.has('demo');
const autoMode = params.has('auto');   // dev/preview: self-start with no host bridge
const diagMode = params.has('diag');   // dev: overlay camera/geometry state for headless capture

// Buffer messages that arrive before the world is built.
let world = null;
const preBuffer = [];
function onMessage(e) {
  const d = e.data;
  if (!d || typeof d !== 'object') return;
  if (!world) { preBuffer.push(d); return; }
  world.handle(d);
}
if (wv) wv.addEventListener('message', onMessage);

// ---- world ----------------------------------------------------------------
function build() {
  setQuality(forcedTier || autoTier());

  const canvas = document.getElementById('c');
  const curtain = document.getElementById('curtain');
  const vig = document.getElementById('vig');
  if (autoMode) curtain.style.transition = 'none';   // dev preview: no timed fade to wait on

  const renderer = new THREE.WebGLRenderer({ canvas, antialias: Q.antialias, alpha: false, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, Q.maxDpr));
  renderer.setSize(window.innerWidth, window.innerHeight);
  renderer.setClearColor(0x000000, 1);

  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(74, window.innerWidth / window.innerHeight, 0.1, 500);

  const zones = new Zones();
  const tunnel = new Tunnel(scene, zones);
  const fog = new Fog(scene);
  const rail = new Rail(camera, tunnel, zones);
  const powerups = new Powerups(scene, camera, tunnel, host);

  // Optional bloom (desktop tier) — lazy, degrades gracefully.
  let composer = null;
  if (Q.bloom) {
    (async () => {
      try {
        const [{ EffectComposer }, { RenderPass }, { UnrealBloomPass }] = await Promise.all([
          import('three/addons/postprocessing/EffectComposer.js'),
          import('three/addons/postprocessing/RenderPass.js'),
          import('three/addons/postprocessing/UnrealBloomPass.js'),
        ]);
        const c = new EffectComposer(renderer);
        c.addPass(new RenderPass(scene, camera));
        c.addPass(new UnrealBloomPass(
          new THREE.Vector2(window.innerWidth, window.innerHeight), 0.42, 0.55, 0.72));
        composer = c;
      } catch (err) { post({ type: 'log', msg: 'bloom disabled: ' + (err && err.message) }); }
    })();
  }

  // ---- click -> powerup raycast (whole window is the play surface) --------
  window.addEventListener('pointerdown', (ev) => {
    powerups.click(ev.clientX, ev.clientY, window.innerWidth, window.innerHeight);
  });

  const clock = new THREE.Clock();
  let paused = false, running = false, rafId = 0, demoT = 0;
  let dtS = 1 / 60;      // smoothed dt: uneven rAF under the busy game reads as camera jitter
  let lastVig = -1;      // skip the per-frame DOM write when the vignette barely changed

  function resize() {
    const w = window.innerWidth, h = window.innerHeight;
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, Q.maxDpr));
    renderer.setSize(w, h);
    camera.aspect = w / h; camera.updateProjectionMatrix();
    if (composer) composer.setSize(w, h);
  }
  window.addEventListener('resize', resize);

  function frame() {
    rafId = requestAnimationFrame(frame);
    if (paused) return;
    let dt = clock.getDelta();
    if (dt > 0.05) dt = 0.05;
    // camera advance uses a smoothed dt so one late frame doesn't lurch the rail forward
    dtS += (dt - dtS) * 0.25;

    const f = rail.update(dtS);
    const render = zones.render(rail.s);
    tunnel.update(rail.s, dt, render);
    fog.update(f, render);
    powerups.update(rail.s, dt);
    zones.trim(rail.s);

    // zone-crossing whoosh — near-subliminal by design, scaled by speed
    if (zones.crossed(rail.s)) sfxBridge.play('tunnel_zone', 0.012 + 0.018 * rail.intensity01);

    // vignette follows speed for a rush feel (written only on real change)
    const vigNow = 0.12 + 0.55 * rail.intensity01;
    if (Math.abs(vigNow - lastVig) > 0.005) { lastVig = vigNow; vig.style.opacity = String(vigNow); }

    if (demoMode) { demoT -= dt; if (demoT <= 0) { powerups.spawn(null, 80); demoT = 4; } }

    if (composer) composer.render(); else renderer.render(scene, camera);
    if (diagMode) diag(f, render);
  }

  // ---- dev diagnostics (headless capture): is the camera really inside? -----
  let diagEl = null;
  function diag(f, render) {
    if (!diagEl) {
      diagEl = document.createElement('pre');
      diagEl.style.cssText = 'position:fixed;left:8px;top:8px;z-index:99;margin:0;padding:8px 10px;'
        + 'background:rgba(0,0,0,.6);color:#7CFC00;font:12px monospace;white-space:pre;pointer-events:none';
      document.body.appendChild(diagEl);
    }
    // distance from the camera to the tube CENTRELINE at its own arc length:
    // ~0 means we ride the centre (inside); >radius means we're off-axis/outside.
    const cp = camera.position;
    const offAxis = Math.hypot(cp.x - f.pos.x, cp.y - f.pos.y, cp.z - f.pos.z);
    const drawn = (tunnel._chunks || []).reduce((a, c) => a + (c.mesh && c.mesh.visible ? 1 : 0), 0);
    diagEl.textContent =
      `state ${rail.state}  s ${rail.s.toFixed(1)}  v ${rail.v.toFixed(1)}\n` +
      `cam  ${cp.x.toFixed(1)}, ${cp.y.toFixed(1)}, ${cp.z.toFixed(1)}\n` +
      `offAxis ${offAxis.toFixed(3)} (radius ${tunnel.radius})\n` +
      `zone ${zones.currentName(rail.s)}  chunks ${drawn}  side ${tunnel.material.side}`;
  }

  function start() { if (!running) { running = true; clock.getDelta(); frame(); } }
  function setPaused(p) {
    paused = p;
    if (!p) clock.getDelta();   // swallow the gap so dt doesn't spike on resume
  }

  world = {
    handle(d) {
      switch (d.type) {
        case 'run-start':
          curtain.style.opacity = '0';       // fade up from black
          fog.reset();
          rail.startIntro();
          setPaused(false);
          // no plunge SFX: the fall whoosh read harsh + loud; the ambient bed carries the drop
          break;
        case 'run-end':
          curtain.style.opacity = '1';        // fade to black
          rail.startExit();
          sfxBridge.play('tunnel_exit', 0.32);
          break;
        case 'streak':
          rail.setStreak(d.combo | 0);
          break;
        case 'zone-hint':
          zones.setHint(d.depth, d.intensity);
          break;
        case 'intensity':
          zones.setHint(zones._hint.depth, d.value);
          break;
        case 'video-playing':
          setPaused(!!d.on);                  // fullscreen video covers us -> stop rendering
          break;
        case 'spawn-powerup':
          powerups.spawn(d.id, d.ahead || 90);
          break;
        case 'quality':
          // geometry counts are fixed at build; we can still retune the DPR cheaply
          if (d.tier === 'low') { Q.maxDpr = Math.min(Q.maxDpr, 1.25); resize(); }
          break;
      }
    },
  };

  rail.onExitDone = () => { post({ type: 'exit-done' }); };

  // Flush anything the host sent during boot, then announce readiness.
  for (const d of preBuffer) world.handle(d);
  preBuffer.length = 0;

  start();
  post({ type: 'ready' });
  const jumpDiag = diagMode && params.has('at');
  if (autoMode && !wv && !jumpDiag) world.handle({ type: 'run-start' });   // preview with no C# host

  // dev: jump the rail to a given arc length so headless captures reach a zone
  // deep in the schedule (?at=900). Runs LAST so the auto run-start can't reset it.
  if (jumpDiag) {
    const at = parseFloat(params.get('at')) || 0;
    curtain.style.opacity = '0';
    zones.ensure(at + Q.aheadUnits);
    tunnel.generateTo(at + Q.aheadUnits);
    tunnel._buildAheadChunks(at, Infinity);   // teleport: bypass the 1-chunk/frame amortization
    rail.state = 'run'; rail.s = at; rail.v = 30;
  }
}

try { build(); }
catch (err) { post({ type: 'log', msg: 'build failed: ' + (err && (err.stack || err.message)) }); }
