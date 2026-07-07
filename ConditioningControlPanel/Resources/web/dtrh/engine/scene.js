/* ============================================================================
 * scene.js - 3D engine orchestrator for the Sissy Fall (fall.html).
 *
 * start({canvas, hud, tier, media, challenge}) wires renderer + endless loop
 * tunnel + velocity nav + card spawner + the bubble game + sissy audio, runs
 * the loop, and returns { dispose }. Frame-loop shape, drone bed, bloom and
 * resize/visibility handling mirror js/rabbit-hole/scene.js.
 *
 * Only ever loaded via dynamic import (main.js), after the 18+ gate.
 * ==========================================================================*/

import * as THREE from 'three';
import { Q, setQuality } from '../shared/quality.js';
import { createFog } from '../shared/fog.js';
import { isMuted, toggleMuted, onMuteChange } from '../shared/audioMute.js';
import { disposeTextures } from '../shared/assets.js';
import { buildLoopLayout, createTunnel, FOG_COLOR, FOG_DENSITY } from './tunnel.js';
import { createFallNav } from './fallNav.js';
import { createSpawner } from './spawner.js';
import { createBubbles } from './bubbles.js';
import { createDirector } from './director.js';
import { createFx } from './fx.js';
import { createPanel } from './panel.js';
import { createDriftChain } from './driftChain.js';
import { getLevel, setLevel, audioGroups } from './audioLevels.js';
import { getAudioCtx, closeAudioBus } from './audioBus.js';
import { S, updateSetting, onSettings, THEME_COLORS, THEME_PRESETS, applyThemePreset, intToHex, hexToInt } from './settings.js';

const DRONE_URL = '/dtrh/assets/audio/drone1.mp3'; // Explore uses drift-under-glass; the fall gets its own bed
const DRONE_FLOOR = 0.16;      // ambient music bed; the speed curve lifts it toward DRONE_MAX
const DRONE_MAX = 0.5;         // full-speed ceiling (the 'music' slider can push past this up to 2x)
// Mix hierarchy (sidechain ducks): spotlight video > drift voice > drone bed.
const DRONE_DUCK = 0.4;        // bed multiplier while a voice line plays
const DRONE_DUCK_SPOT = 0.25;  // bed multiplier while a spotlight video holds the stage
const VOICE_DUCK_SPOT = 0.15;  // voice multiplier under a spotlight (the stage owns the foreground)
const SPEED_REF = 28;          // speed that maps to full drone volume / speed meter 100%

// Voiceover: the drift chain (driftChain.js + the FALL_DRIFT corpus) is the
// fall's only voice - the old card barks / depth narration / giggles are gone.
// It is timer-driven, so pause/results/restart must stop/start it explicitly.

// Fullscreen: standard API with a webkit fallback (older/desktop Safari). iOS
// Safari on iPhone exposes no element fullscreen at all, so `supported` is
// false there and the dock button hides itself rather than sit dead.
const FS = (() => {
  const root = document.documentElement;
  const request = root.requestFullscreen || root.webkitRequestFullscreen || root.webkitRequestFullScreen;
  const exit = document.exitFullscreen || document.webkitExitFullscreen;
  const enabled = document.fullscreenEnabled || document.webkitFullscreenEnabled;
  return {
    supported: !!(request && exit && enabled),
    event: ('onwebkitfullscreenchange' in document) ? 'webkitfullscreenchange' : 'fullscreenchange',
    isActive: () => !!(document.fullscreenElement || document.webkitFullscreenElement),
    enter() { const r = request && request.call(root); if (r && r.catch) r.catch(() => {}); },
    leave() { const r = exit && exit.call(document); if (r && r.catch) r.catch(() => {}); },
  };
})();

// iPhone/iPad Safari has no element fullscreen API. There the dock button can't
// toggle anything, so it instead points at the one route that drops the address
// bar: Add to Home Screen -> launch from the icon (standalone). iPadOS 13+ lies
// about being a Mac, so sniff touch too.
const IS_IOS = /iP(hone|od|ad)/.test(navigator.platform)
  || (/Macintosh/.test(navigator.userAgent) && 'ontouchend' in document);

// `game` (optional): the DtRH run brain (game/chaosRun.js). When present the
// engine runs in GAME MODE - the Fall's own bubble field stands down (veil FX
// still fire), the director takes its intensity from the game, and the game's
// frame()/pause hooks ride the scene loop. Without it, this is The Fall as-is.
export async function start({ canvas, hud, tier, media, challenge, game = null }) {
  setQuality(tier); // resolve the Q knobs BEFORE any geometry/material builds

  // ---- renderer / scene / camera ------------------------------------------
  // high-performance: ask dual-GPU/phone drivers for the fast GPU profile -
  // this is a fullscreen fill-rate app, not a widget the OS should power-sip
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: Q.antialias, alpha: true, powerPreference: 'high-performance' });

  // Adaptive resolution: the tier caps DPR, then a governor in the frame loop
  // trims the render scale on the fly to hold framerate on weak phones (and
  // pushes it back up when there's headroom). This is the biggest mobile lever -
  // the tunnel + fog are full-screen fragment shaders, so fill-rate rules.
  const DPR_CAP = Math.min(window.devicePixelRatio || 1, Q.maxDpr);
  const DPR_MIN = Math.min(DPR_CAP, Q.tier === 'mobile' ? 0.75 : 1.0);
  let dprScale = Q.tier === 'mobile' ? 0.8 : 1; // mobile starts conservative, then adapts
  const effectiveDpr = () => Math.max(DPR_MIN, DPR_CAP * dprScale);
  function applyDpr() {
    renderer.setPixelRatio(effectiveDpr());
    renderer.setSize(window.innerWidth, window.innerHeight);
    if (composer) composer.setSize(window.innerWidth, window.innerHeight);
  }
  renderer.setPixelRatio(effectiveDpr());
  renderer.setSize(window.innerWidth, window.innerHeight);
  renderer.setClearColor(0x000000, 0); // let the CSS gradient show through

  const scene = new THREE.Scene();
  scene.fog = new THREE.FogExp2(FOG_COLOR, FOG_DENSITY);

  const camera = new THREE.PerspectiveCamera(70, window.innerWidth / window.innerHeight, 0.1, 400);

  // ---- tunnel + fog ---------------------------------------------------------
  const layout = buildLoopLayout();
  const tunnel = createTunnel(layout);
  scene.add(tunnel.mesh);

  const fog = createFog({ layout, cardCenters: [], titleCenters: [] });
  scene.add(fog.mesh);

  // mood zones + ribbons + sparkles + lightning (deeper = busier)
  const fx = createFx({ scene, layout, tunnelMat: tunnel.material, particleFog: fog });

  // Push theme colors to the DOM effect layer (pink filter, subliminal words,
  // spiral overlay, HUD accent) as CSS custom properties - the persona.js
  // setProperty pattern. THREE-side theming lives in fx.js/spawner.js.
  const rgbTriplet = (n) => `${(n >> 16) & 255},${(n >> 8) & 255},${n & 255}`;
  function applyCssTheme() {
    const r = document.documentElement.style;
    r.setProperty('--fx-accent-rgb', rgbTriplet(S.colLine));
    r.setProperty('--fx-spiral-rgb', rgbTriplet(S.colSpiral));
    r.setProperty('--pink', intToHex(S.colLine));
  }
  applyCssTheme();
  const offCssTheme = onSettings((key) => { if (typeof key === 'string' && key.startsWith('col')) applyCssTheme(); });

  // ---- game brain + audio ---------------------------------------------------
  // DtRH demotion: chaosRun owns intensity (elapsed/duration); the director is
  // reduced to the speed/boost presentation adapter it always was at heart.
  const director = createDirector({
    challenge,
    intensitySource: game ? () => game.moodIntensity() : null,
  });

  // ---- ambient drone bed (copied pattern from rabbit-hole/scene.js) ---------
  let drone = null, droneVol = DRONE_FLOOR, droneStarted = false;
  let droneCtx = null, droneGain = null, droneWatch = 0;
  let droneKickTries = 0, droneKickRest = 0, droneDuck = 1, voiceDuck = 1;
  try { drone = new Audio(DRONE_URL); drone.loop = true; drone.preload = 'auto'; drone.volume = DRONE_FLOOR; } catch (e) { drone = null; }
  function setDroneVolume(v) {
    if (droneGain) droneGain.gain.value = v;
    else if (drone) drone.volume = v;
  }
  function startDrone() {
    if (!drone || droneStarted) return;
    droneStarted = true;
    try {
      // the fall-wide shared context (audioBus.js) - one context for drone,
      // video-card gains and bubble SFX instead of three
      droneCtx = getAudioCtx();
      if (droneCtx) {
        const src = droneCtx.createMediaElementSource(drone);
        droneGain = droneCtx.createGain();
        droneGain.gain.value = droneVol;
        src.connect(droneGain);
        droneGain.connect(droneCtx.destination);
        drone.volume = 1; // loudness lives in the gain node now
      }
    } catch (e) { droneCtx = null; droneGain = null; }
    if (isMuted()) return;
    if (droneCtx && droneCtx.state === 'suspended') { const r = droneCtx.resume(); if (r && r.catch) r.catch(() => {}); }
    const p = drone.play(); if (p && p.catch) p.catch(() => {});
  }
  const offDroneMute = onMuteChange((m) => {
    if (!drone) return;
    if (m) { try { drone.pause(); } catch (e) { /* ignore */ } }
    else if (droneStarted && running && !document.hidden) {
      if (droneCtx && droneCtx.state === 'suspended') { const r = droneCtx.resume(); if (r && r.catch) r.catch(() => {}); }
      const p = drone.play(); if (p && p.catch) p.catch(() => {});
    }
  });

  // ---- navigation + cards ----------------------------------------------------
  const nav = createFallNav({
    camera, canvas, layout,
    getTargetSpeed: () => director.getTargetSpeed(),
    onFirstInteract: startDrone,
    onScrollBoost: (amt) => director.noteScroll(amt),
    isSpotlightActive: () => spawner.spotlightActive(),
    onSkipSpotlight: () => spawner.skipSpotlight(),
  });

  // onVeilPass fires as the camera punches through a tunnel veil - it triggers
  // that veil's screen effect + a synced trigger drop. bubbles is defined below;
  // the callback only runs once frames are live, so the closure is safe.
  const spawner = createSpawner({
    scene, layout, media, renderer, camera,
    // Game mode (M4): the Fall's field is paused for the whole session, so the
    // wash is forced through; the run brain decides whether the moment allows
    // it (running + not paused/covered). Without a game the classic path runs.
    onVeilPass: (kind) => {
      try {
        if (game) { if (game.allowVeil()) bubbles.triggerVeil(kind, true); }
        else bubbles.triggerVeil(kind);
      } catch (e) { /* ignore */ }
    },
  });

  // the continuous voice layer; depth is sampled at each pick for weighting
  const drift = createDriftChain({ getDepth: () => nav.getDepth() });

  // ---- HUD -------------------------------------------------------------------
  const panel = createPanel(hud);
  const hudBits = buildHud(hud, {
    challenge,
    supportsMediaAdd: media.supportsFS(),
    onMute: () => toggleMuted(),
    onAddMedia: () => { media.pickFolder().catch(() => {}); },
    onRestart: restartRun,
    onOptions: () => panel.toggle(),
    onResume: () => setGamePaused(false),
    onSkip: () => spawner.skipSpotlight(),
    // Game mode: the pause card gains a "surface" exit - the run ends early
    // but still pays out its recap (the WPF RequestStop contract).
    onSurface: game ? () => { setGamePaused(false); game.surface(); } : null,
  });
  const offHudMute = onMuteChange(hudBits.syncMute);
  hudBits.syncMute(isMuted());

  // ---- the bubble game (main mechanic) ----------------------------------------
  const bubbles = createBubbles({
    hud, canvas,
    onPop: (kind, gain, combo) => {
      director.notePop(kind, gain, combo);
      hudBits.setScore(bubbles.getScore(), combo);
      if (kind === 'lucky') nav.fovKick(3);
    },
    onEffect: (kind) => {
      director.noteEffect();
      nav.fovKick(2.2); // the tube visibly lunges when an effect fires
      if (kind === 'prism') fx.flashRandomTheme(10000); // ~10s tube color scramble
    },
    onMiss: () => {
      if (!director.isChallenge()) return; // ASMR mode: misses just drift away
      const ended = director.noteMiss();
      hudBits.setMisses(director.getMisses());
      if (ended) endRun();
    },
    onCombo: () => {},
  });

  // ---- ESC pause -----------------------------------------------------------------
  let gamePaused = false;
  function setGamePaused(v) {
    v = !!v;
    if (v === gamePaused) return;
    if (v && director.isOver()) return; // results screen owns the end state
    gamePaused = v;
    nav.setPaused(v);
    bubbles.setFrozen(v);
    spawner.setPaused(v);
    if (game) game.setPaused(v);
    hudBits.showPause(v);
    if (v) drift.stop(); else drift.start();
    if (drone && droneStarted) {
      if (v) { try { drone.pause(); } catch (e) { /* ignore */ } }
      else if (!isMuted() && !document.hidden) {
        if (droneCtx && droneCtx.state === 'suspended') { const r = droneCtx.resume(); if (r && r.catch) r.catch(() => {}); }
        const p = drone.play(); if (p && p.catch) p.catch(() => {});
      }
    }
  }
  function onKeyDown(e) {
    if (e.key !== 'Escape') return;
    if (panel.isOpen()) { panel.close(); return; }
    setGamePaused(!gamePaused);
  }
  window.addEventListener('keydown', onKeyDown);

  // ---- grab: hold an image/gif card in front of the POV ------------------------
  // pointerdown on a card seizes it (spawner pins it ahead of the camera and the
  // fall eases); pointerup/cancel lets it go. This rides ALONGSIDE fallNav's own
  // drag-to-look, so a held card simply stays centered wherever you look.
  function grabPointerDown(e) {
    if (gamePaused || director.isOver()) return;
    if (e.button != null && e.button !== 0) return; // primary button / touch only
    // clicks on UI chrome or a bubble are not grabs
    if (e.target instanceof Element &&
        e.target.closest('.sf-dock, .sf-panel, .sf-results, .sf-pause, .sf-skip, .sf-explore, .sf-uitoggle, .sf-fs-tip, .rh-bubble, .rh-fx-flashclip, .cf-bubble, .cf-hud, .cf-overlay')) return;
    const r = canvas.getBoundingClientRect();
    if (!r.width || !r.height) return;
    const nx = ((e.clientX - r.left) / r.width) * 2 - 1;
    const ny = -((e.clientY - r.top) / r.height) * 2 + 1;
    spawner.grabAtPointer(nx, ny, camera);
  }
  function grabPointerUp() { spawner.releaseGrab(); }
  canvas.addEventListener('pointerdown', grabPointerDown);
  window.addEventListener('pointerup', grabPointerUp);
  window.addEventListener('pointercancel', grabPointerUp);

  // ---- run end / restart (challenge mode) --------------------------------------
  function endRun() {
    bubbles.setPaused(true);
    drift.stop(); // the frame loop keeps running under results - silence the voice explicitly
    hudBits.showResults({
      score: bubbles.getScore(),
      depth: Math.round(nav.getDepth()),
      bestCombo: director.getBestCombo(),
    });
  }
  function restartRun() {
    hudBits.hideResults();
    director.reset();
    bubbles.reset();
    bubbles.setPaused(false);
    nav.reset();
    spawner.reset(0);
    hudBits.setScore(0, 0);
    hudBits.setMisses(0);
    drift.start();
  }

  // ---- optional bloom (gracefully skipped if addons unavailable) ---------------
  let composer = null;
  if (Q.bloom) try {
    const [{ EffectComposer }, { RenderPass }, { UnrealBloomPass }] = await Promise.all([
      import('three/addons/postprocessing/EffectComposer.js'),
      import('three/addons/postprocessing/RenderPass.js'),
      import('three/addons/postprocessing/UnrealBloomPass.js'),
    ]);
    composer = new EffectComposer(renderer);
    composer.addPass(new RenderPass(scene, camera));
    composer.addPass(new UnrealBloomPass(
      new THREE.Vector2(window.innerWidth, window.innerHeight), 0.42, 0.5, 0.5));
    composer.setSize(window.innerWidth, window.innerHeight);
  } catch (err) {
    console.warn('[sissy-fall] bloom unavailable, rendering without it:', err);
  }

  // Give the first user-media decodes a head start behind the loader, so the
  // opening plunge meets finished cards instead of hollow frames.
  await spawner.warm();

  // begin the voice chain (autoplay is unlocked - we got here via the begin click)
  drift.start();

  // ---- DtRH game mode ----------------------------------------------------------
  if (game) {
    hud.classList.add('sf-game-mode');   // hides the Fall's own score readout (CSS)
    startDrone();                        // no gesture gate in the hosted WebView2
    // The Fall's own bubble field stands down completely - setIntensity(0)
    // still targets COUNT_MIN, so pause it (clears + stops spawning). Veil
    // punch-through washes skip while paused; M4 routes veils into the game.
    bubbles.setPaused(true);
    game.attach({ nav, fx, director, hud, canvas });
  }

  // ---- loop --------------------------------------------------------------------
  const clock = new THREE.Clock();
  let raf = 0, running = true;
  let tunnelTime = 0;  // accumulated shader time: advances faster as the fall speeds up
  let hudTick = 0;
  let perfEMA = 1 / 60, perfCooldown = 1.5; // adaptive-resolution governor state

  // Panel diagnostics (always on, like __sfPipe): live render health. A phone
  // screenshot showing scale pinned at 0.5 says "GPU-bound - cut fill-rate";
  // good fps at full scale with visible hitches says "decode/memory spikes".
  window.__sfPerf = () => ({
    fps: Math.round(1 / Math.max(perfEMA, 1e-4)),
    scale: Math.round(dprScale * 100) / 100,
    dpr: Math.round(effectiveDpr() * 100) / 100,
    tier: Q.tier,
  });

  function frame() {
    if (!running) return;
    raf = requestAnimationFrame(frame);
    const rawDt = clock.getDelta();    // real frame period - drives the perf governor
    const dt = Math.min(rawDt, 0.05);  // clamped for a stable simulation step

    if (gamePaused) { // frozen frame under the pause overlay; ESC resumes
      if (composer) composer.render(); else renderer.render(scene, camera);
      return;
    }

    // Adaptive-resolution governor: hold ~55fps by trimming the render scale when
    // frames run long, raising it back toward the tier cap when there's slack.
    // Hysteresis (down <50fps, up >58fps) + a 0.6s cooldown keep it from thrashing.
    perfEMA += (rawDt - perfEMA) * 0.1;
    perfCooldown -= rawDt;
    if (perfCooldown <= 0 && clock.elapsedTime > 2) {
      perfCooldown = 0.6;
      const fps = 1 / Math.max(perfEMA, 1e-4);
      if (fps < 50 && dprScale > 0.5) { dprScale = Math.max(0.5, dprScale - 0.12); applyDpr(); }
      else if (fps > 58 && dprScale < 1) { dprScale = Math.min(1, dprScale + 0.08); applyDpr(); }
    }

    director.update(dt);
    director.setHold(spawner.isGrabbing()); // grabbing a card eases the fall (auto-restores on release)
    if (game) {
      // Game mode: chaosRun owns the field (its own spawner + rAF integrator);
      // the Fall's bubble game is paused for the whole session.
      game.frame(dt);
    } else {
      bubbles.setIntensity(director.getIntensity());
      bubbles.setRunTime(director.getRunTime()); // gates which bubble kinds can spawn
    }
    nav.update(dt);

    const depth = nav.getDepth();
    const speed = nav.getSpeed();
    spawner.update(camera, depth, dt, clock.elapsedTime);

    // Sidechain the mix: a spotlight video owns the whole foreground (the
    // voice whispers down under it, the bed drops hardest); otherwise a
    // speaking voice ducks the bed and swells back in the gaps. Everything
    // eases over ~0.5s so nothing pumps.
    const spotOn = spawner.spotlightActive();
    voiceDuck += ((spotOn ? VOICE_DUCK_SPOT : 1) - voiceDuck) * Math.min(1, dt * 2.5);
    drift.setDuck(voiceDuck);

    // drone: volume follows the fall speed; 1Hz watchdog vs. autoplay killings
    if (drone) {
      const raw = DRONE_FLOOR + (DRONE_MAX - DRONE_FLOOR) * Math.min(1, speed / SPEED_REF);
      droneVol += (raw - droneVol) * Math.min(1, dt * 2);
      const duckTarget = spotOn ? DRONE_DUCK_SPOT : (drift.isSpeaking() ? DRONE_DUCK : 1);
      droneDuck += (duckTarget - droneDuck) * Math.min(1, dt * 2.5);
      // the 'music' slider is a 0..1 multiplier over the speed-following bed
      setDroneVolume(isMuted() ? 0 : Math.max(0, Math.min(1, droneVol)) * getLevel('music') * droneDuck);
      droneWatch += dt;
      if (droneWatch >= 1) {
        droneWatch = 0;
        if (droneStarted && !isMuted() && !document.hidden) {
          if (!drone.paused) { droneKickTries = 0; }
          else if (clock.elapsedTime >= droneKickRest) {
            if (droneKickTries >= 3) { droneKickRest = clock.elapsedTime + 30; droneKickTries = 0; }
            else {
              droneKickTries++;
              if (droneCtx && droneCtx.state !== 'running') { const r = droneCtx.resume(); if (r && r.catch) r.catch(() => {}); }
              const p = drone.play(); if (p && p.catch) p.catch(() => {});
            }
          }
        }
      }
    }

    // shader time scales with speed so pops/chains visibly whip the rings past
    tunnelTime += dt * (0.5 + speed / 12);
    tunnel.material.uniforms.uTime.value = tunnelTime;
    // rush: 0 at the calm base speed, 1 flat out - heats the line work + fx
    const rushTarget = Math.min(1, Math.max(0, (speed - 8) / 20));
    const uRush = tunnel.material.uniforms.uRush;
    uRush.value += (rushTarget - uRush.value) * Math.min(1, dt * 3);
    fog.update(clock.elapsedTime);
    fx.update(depth, dt, clock.elapsedTime, director.getIntensity(), uRush.value);

    hudTick += dt;
    if (hudTick >= 0.25) {
      hudTick = 0;
      hudBits.setMeters(depth, speed / 6); // meter shows multiples of the calm base speed
      hudBits.setSkipVisible(spawner.spotlightActive()); // skip button rides with the video stage
      drift.setSpeed(speed); // whisper tempo tracks the fall speed (up to +30%)
    }

    if (composer) composer.render(); else renderer.render(scene, camera);
  }
  frame();

  // ---- resize / visibility ------------------------------------------------------
  let resizeTimer = 0;
  function applyResize() {
    const w = window.innerWidth, h = window.innerHeight;
    camera.aspect = w / h; camera.updateProjectionMatrix();
    applyDpr(); // re-applies the governed render scale at the new viewport size
  }
  function onResize() {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(applyResize, 150);
  }
  function onVisibility() {
    if (game) game.setHidden(document.hidden);
    if (document.hidden) {
      running = false;
      bubbles.setPaused(true);
      spawner.setPaused(true); // parks card + spotlight videos (no hidden-tab audio)
      if (drone) { try { drone.pause(); } catch (e) { /* ignore */ } }
    } else if (!running) {
      running = true;
      if (!director.isOver() && !game) bubbles.setPaused(false);   // game mode: field stays down
      if (!gamePaused) spawner.setPaused(false);
      if (drone && droneStarted && !isMuted() && !gamePaused) {
        if (droneCtx && droneCtx.state === 'suspended') { const r = droneCtx.resume(); if (r && r.catch) r.catch(() => {}); }
        const p = drone.play(); if (p && p.catch) p.catch(() => {});
      }
      clock.getDelta(); frame();
    }
  }
  window.addEventListener('resize', onResize);
  document.addEventListener('visibilitychange', onVisibility);

  // ---- teardown -------------------------------------------------------------------
  function dispose() {
    running = false;
    cancelAnimationFrame(raf);
    clearTimeout(resizeTimer);
    window.removeEventListener('resize', onResize);
    window.removeEventListener('keydown', onKeyDown);
    canvas.removeEventListener('pointerdown', grabPointerDown);
    window.removeEventListener('pointerup', grabPointerUp);
    window.removeEventListener('pointercancel', grabPointerUp);
    document.removeEventListener('visibilitychange', onVisibility);
    if (game) { try { game.dispose(); } catch (e) { /* ignore */ } }
    panel.dispose();
    fx.dispose();
    nav.dispose();
    spawner.dispose();
    bubbles.dispose();
    drift.dispose();
    offDroneMute();
    offHudMute();
    offCssTheme();
    if (drone) { try { drone.pause(); drone.src = ''; } catch (e) { /* ignore */ } drone = null; }
    droneCtx = null; droneGain = null;
    closeAudioBus(); // the one shared AudioContext (drone + video gains + SFX)
    fog.dispose();
    tunnel.dispose();
    hudBits.dispose();
    if (composer && composer.dispose) composer.dispose();
    disposeTextures();
    renderer.dispose();
  }

  // E2E hook (only when ?e2e is present): read-only engine state for automation.
  if (location.search.includes('e2e')) {
    window.__sf = {
      depth: () => nav.getDepth(),
      speed: () => nav.getSpeed(),
      trim: () => nav.getTrim(),
      cards: () => spawner.liveCount(),
      videos: () => spawner.liveVideoCount(),
      score: () => bubbles.getScore(),
      intensity: () => director.getIntensity(),
      misses: () => director.getMisses(),
      over: () => director.isOver(),
      paused: () => gamePaused,
      spotlight: () => spawner.spotlightActive(),
      spotlightInfo: () => spawner.spotlightInfo(),
      prefetched: () => spawner.prefetchCount(),
      cardKinds: () => spawner.liveKinds(),
      zone: () => fx.getZone(),
      drift: () => drift.debugState(),
    };
  }

  return { dispose };
}

// ---- HUD chrome (score / meters / hearts / dock / hint / results / pause) ----
function buildHud(hud, { challenge, supportsMediaAdd, onMute, onAddMedia, onRestart, onOptions, onResume, onSkip, onSurface = null }) {
  const bits = [];
  const el = (cls, parent) => {
    const d = document.createElement('div');
    d.className = cls;
    (parent || hud).appendChild(d);
    bits.push(d);
    return d;
  };

  const score = el('sf-score');
  const scoreVal = document.createElement('span');
  scoreVal.textContent = '0 XP';
  const comboVal = document.createElement('span');
  comboVal.className = 'sf-combo';
  score.appendChild(scoreVal);
  score.appendChild(comboVal);

  const meters = el('sf-meters');
  const depthVal = document.createElement('span');
  depthVal.className = 'sf-depth';
  depthVal.textContent = '0 m';
  const speedVal = document.createElement('span');
  speedVal.className = 'sf-speed';
  speedVal.textContent = '×1.0';
  meters.appendChild(depthVal);
  meters.appendChild(speedVal);

  let hearts = null;
  if (challenge) {
    hearts = el('sf-hearts');
    hearts.textContent = '♥'.repeat(10);
  }

  const dock = el('sf-dock');

  // audio: the mute button plus a caret that expands granular level sliders
  const audioWrap = document.createElement('div');
  audioWrap.className = 'sf-audio-wrap';
  const muteBtn = document.createElement('button');
  muteBtn.type = 'button';
  muteBtn.className = 'sf-dock-btn sf-audio-btn';
  muteBtn.addEventListener('click', onMute);
  const caretBtn = document.createElement('button');
  caretBtn.type = 'button';
  caretBtn.className = 'sf-caret';
  caretBtn.textContent = '^';
  caretBtn.setAttribute('aria-label', 'audio levels');
  caretBtn.style.pointerEvents = 'auto'; // robust even if a stale CSS misses the rule
  const audioPanel = document.createElement('div');
  audioPanel.className = 'sf-audio-panel';
  audioPanel.style.pointerEvents = 'auto';
  audioPanel.hidden = true;
  for (const g of audioGroups()) {
    const row = document.createElement('label');
    row.className = 'sf-audio-row';
    const head = document.createElement('div');
    head.className = 'sf-audio-rowhead';
    const name = document.createElement('span');
    name.textContent = g.label;
    const val = document.createElement('span');
    val.className = 'sf-audio-val';
    head.append(name, val);
    const slider = document.createElement('input');
    // music is a boostable multiplier (0..2); the rest are 0..1 gains
    slider.type = 'range'; slider.min = '0'; slider.max = g.key === 'music' ? '200' : '100'; slider.step = '1';
    const showVal = (v) => { val.textContent = Math.round(v * 100) + '%'; };
    slider.value = String(Math.round(getLevel(g.key) * 100));
    showVal(getLevel(g.key));
    slider.addEventListener('input', () => {
      const v = Number(slider.value) / 100;
      setLevel(g.key, v);
      showVal(v);
    });
    row.append(head, slider);
    audioPanel.appendChild(row);
  }
  caretBtn.addEventListener('click', () => {
    const open = audioPanel.hidden;
    audioPanel.hidden = !open;
    caretBtn.classList.toggle('is-open', open);
  });
  audioWrap.append(muteBtn, caretBtn, audioPanel);
  dock.appendChild(audioWrap);

  // paint: tunnel color/theme picker (preset swatches + per-element pickers)
  const themeWrap = document.createElement('div');
  themeWrap.className = 'sf-theme-wrap';
  const paintBtn = document.createElement('button');
  paintBtn.type = 'button';
  paintBtn.className = 'sf-dock-btn sf-paint-btn';
  paintBtn.textContent = '🎨';
  paintBtn.setAttribute('aria-label', 'tunnel colors');
  paintBtn.style.pointerEvents = 'auto';
  const themePanel = document.createElement('div');
  themePanel.className = 'sf-theme-panel';
  themePanel.style.pointerEvents = 'auto';
  themePanel.hidden = true;
  const colorInputs = {};
  const refreshInputs = () => {
    for (const c of THEME_COLORS) if (colorInputs[c.key]) colorInputs[c.key].value = intToHex(S[c.key]);
  };
  const swatches = document.createElement('div');
  swatches.className = 'sf-swatches';
  for (const name of Object.keys(THEME_PRESETS)) {
    const p = THEME_PRESETS[name];
    const sw = document.createElement('button');
    sw.type = 'button';
    sw.className = 'sf-swatch';
    sw.title = name;
    sw.style.background = `linear-gradient(135deg, ${intToHex(p.colLine)}, ${intToHex(p.colSpiral)})`;
    sw.addEventListener('click', () => { applyThemePreset(name); refreshInputs(); });
    swatches.appendChild(sw);
  }
  themePanel.appendChild(swatches);
  for (const c of THEME_COLORS) {
    const row = document.createElement('label');
    row.className = 'sf-color-row';
    const nm = document.createElement('span');
    nm.textContent = c.label;
    const inp = document.createElement('input');
    inp.type = 'color';
    inp.value = intToHex(S[c.key]);
    inp.addEventListener('input', () => updateSetting(c.key, hexToInt(inp.value)));
    colorInputs[c.key] = inp;
    row.append(nm, inp);
    themePanel.appendChild(row);
  }
  paintBtn.addEventListener('click', () => {
    const open = themePanel.hidden;
    themePanel.hidden = !open;
    paintBtn.classList.toggle('is-open', open);
  });
  themeWrap.append(paintBtn, themePanel);
  dock.appendChild(themeWrap);
  let addBtn = null;
  if (supportsMediaAdd) {
    addBtn = document.createElement('button');
    addBtn.type = 'button';
    addBtn.className = 'sf-dock-btn';
    addBtn.textContent = '+ media';
    addBtn.addEventListener('click', onAddMedia);
    dock.appendChild(addBtn);
  }
  const gearBtn = document.createElement('button');
  gearBtn.type = 'button';
  gearBtn.className = 'sf-dock-btn';
  gearBtn.textContent = '⚙ options';
  gearBtn.addEventListener('click', onOptions);
  dock.appendChild(gearBtn);

  // fullscreen - the big win on phones, where browser chrome eats the frame.
  // Real toggle on Android/desktop; on iPhone Safari (no fullscreen API) the
  // button instead explains the only route that works: Add to Home Screen.
  // Already launched standalone? Then there's no chrome to hide - skip it.
  let fsCleanup = null;
  if (FS.supported) {
    const fsBtn = document.createElement('button');
    fsBtn.type = 'button';
    fsBtn.className = 'sf-dock-btn sf-fs-btn';
    fsBtn.textContent = '⛶';
    const syncFs = () => {
      const on = FS.isActive();
      fsBtn.classList.toggle('is-on', on);
      fsBtn.setAttribute('aria-label', on ? 'exit fullscreen' : 'go fullscreen');
      fsBtn.title = on ? 'exit fullscreen' : 'go fullscreen';
    };
    syncFs();
    fsBtn.addEventListener('click', () => { FS.isActive() ? FS.leave() : FS.enter(); });
    document.addEventListener(FS.event, syncFs);
    fsCleanup = () => document.removeEventListener(FS.event, syncFs);
    dock.appendChild(fsBtn);
  } else if (IS_IOS && !navigator.standalone) {
    const fsBtn = document.createElement('button');
    fsBtn.type = 'button';
    fsBtn.className = 'sf-dock-btn sf-fs-btn';
    fsBtn.textContent = '⛶';
    fsBtn.setAttribute('aria-label', 'how to play fullscreen');
    fsBtn.title = 'fullscreen';
    const tip = document.createElement('div');
    tip.className = 'sf-fs-tip';
    tip.hidden = true;
    tip.textContent = "iPhone can't go fullscreen from Safari. Tap the Share button, then “Add to Home Screen” - open The Fall from its icon and it runs with no address bar.";
    hud.appendChild(tip);
    const onDocTap = (e) => { if (e.target !== fsBtn && !tip.contains(e.target)) hideTip(); };
    const showTip = () => { tip.hidden = false; fsBtn.classList.add('is-on'); document.addEventListener('pointerdown', onDocTap, true); };
    const hideTip = () => { tip.hidden = true; fsBtn.classList.remove('is-on'); document.removeEventListener('pointerdown', onDocTap, true); };
    fsBtn.addEventListener('click', (e) => { e.stopPropagation(); tip.hidden ? showTip() : hideTip(); });
    fsCleanup = () => { document.removeEventListener('pointerdown', onDocTap, true); tip.remove(); };
    dock.appendChild(fsBtn);
  }

  const hint = el('sf-hint');
  hint.textContent = 'pop the bubbles · scroll to fall faster · ⚙ top-left for controls';
  setTimeout(() => hint.classList.add('is-gone'), 9000);

  // Skip button - only shown while a video holds the stage. On desktop a
  // scroll-down also skips; on touch there is no wheel, so this tap target (plus
  // a swipe, handled in fallNav) is the way out of a clip.
  const skipBtn = el('sf-skip');
  skipBtn.hidden = true;
  const skipInner = document.createElement('button');
  skipInner.type = 'button';
  skipInner.className = 'sf-btn';
  skipInner.textContent = 'skip video ▸';
  skipInner.addEventListener('click', (e) => { e.stopPropagation(); if (onSkip) onSkip(); });
  skipBtn.appendChild(skipInner);

  // Quiet way back to the real app - a dim corner link that brightens on hover,
  // there for the curious without competing with the fall.
  const explore = document.createElement('a');
  explore.className = 'sf-explore';
  explore.href = '/explore.html';
  explore.textContent = '↗';
  explore.title = 'explore the app';
  explore.setAttribute('aria-label', 'explore the app');
  hud.appendChild(explore);
  bits.push(explore);

  // Master controls toggle - a small gear top-left (an area you rarely tap
  // mid-fall). All the chrome (dock + explore link + options panel) starts
  // HIDDEN for a clean fall; tapping the gear reveals it, tapping again hides
  // it. The skip button and score/meters are deliberately left out of this.
  const uiToggle = document.createElement('button');
  uiToggle.type = 'button';
  uiToggle.className = 'sf-uitoggle';
  uiToggle.textContent = '⚙';
  uiToggle.title = 'show / hide controls';
  uiToggle.setAttribute('aria-label', 'show or hide controls');
  hud.appendChild(uiToggle);
  bits.push(uiToggle);
  hud.classList.add('sf-chrome-hidden'); // default: controls hidden
  let chromeShown = false;
  uiToggle.addEventListener('click', (e) => {
    e.stopPropagation();
    chromeShown = !chromeShown;
    hud.classList.toggle('sf-chrome-hidden', !chromeShown);
    uiToggle.classList.toggle('is-on', chromeShown);
  });

  const pause = el('sf-pause');
  pause.hidden = true;
  const pauseCard = document.createElement('div');
  pauseCard.className = 'sf-results-card';
  const pauseH = document.createElement('h2');
  pauseH.textContent = 'paused';
  const pauseP = document.createElement('p');
  pauseP.textContent = 'the fall waits for you';
  const pauseBtn = document.createElement('button');
  pauseBtn.type = 'button';
  pauseBtn.className = 'sf-btn sf-btn-primary';
  pauseBtn.textContent = 'keep falling';
  pauseBtn.addEventListener('click', onResume);
  pauseCard.append(pauseH, pauseP, pauseBtn);
  if (onSurface) {
    const surfaceBtn = document.createElement('button');
    surfaceBtn.type = 'button';
    surfaceBtn.className = 'sf-btn';
    surfaceBtn.textContent = 'surface (end run)';
    surfaceBtn.addEventListener('click', onSurface);
    pauseCard.appendChild(surfaceBtn);
  }
  pause.appendChild(pauseCard);

  const results = el('sf-results');
  results.hidden = true;
  const resultsCard = document.createElement('div');
  resultsCard.className = 'sf-results-card';
  results.appendChild(resultsCard);

  return {
    setScore(s, combo) {
      scoreVal.textContent = `${s} XP`;
      comboVal.textContent = combo > 1 ? `×${combo}` : '';
    },
    setMeters(depth, speedX) {
      depthVal.textContent = `-${Math.round(depth).toLocaleString()} m`;
      speedVal.textContent = `×${speedX.toFixed(1)}`;
    },
    setMisses(n) {
      if (!hearts) return;
      const left = Math.max(0, 10 - n);
      hearts.textContent = '♥'.repeat(left) + '♡'.repeat(10 - left);
    },
    syncMute(muted) {
      muteBtn.textContent = muted ? '🔇 audio' : '🔊 audio';
      muteBtn.classList.toggle('is-off', !!muted);
    },
    showResults({ score: s, depth, bestCombo }) {
      resultsCard.innerHTML = '';
      const h2 = document.createElement('h2');
      h2.textContent = 'the fall is over';
      const p1 = document.createElement('p');
      p1.textContent = `${s} XP`;
      const p2 = document.createElement('p');
      p2.textContent = `you sank ${depth.toLocaleString()} m`;
      const p3 = document.createElement('p');
      p3.textContent = `best combo ×${Math.max(1, bestCombo)}`;
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'sf-btn sf-btn-primary';
      btn.textContent = 'fall again';
      btn.addEventListener('click', onRestart);
      resultsCard.append(h2, p1, p2, p3, btn);
      results.hidden = false;
    },
    hideResults() { results.hidden = true; },
    showPause(v) { pause.hidden = !v; },
    setSkipVisible(v) { if (skipBtn.hidden === !v) return; skipBtn.hidden = !v; },
    dispose() { if (fsCleanup) fsCleanup(); for (const b of bits) b.remove(); },
  };
}
