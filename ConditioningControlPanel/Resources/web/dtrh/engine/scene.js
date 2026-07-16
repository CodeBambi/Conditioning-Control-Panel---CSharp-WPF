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
import { createWallPosters } from './wallPosters.js';
import { createJunctions } from './junctions.js';
import { createBoonPick } from './boonPick.js';
import { createPowerupDrops } from './powerupDrops.js';
import { createHubStations } from './hubStations.js';
import { createBubbles } from './bubbles.js';
import { createDirector } from './director.js';
import { createFx } from './fx.js';
import { createPanel } from './panel.js';
import { createDriftChain } from './driftChain.js';
import { getLevel, setLevel, audioGroups, getVoice, setVoice, voiceSets, onVoice } from './audioLevels.js';
import { getAudioCtx, getMasterOut, closeAudioBus } from './audioBus.js';
import { S, updateSetting, onSettings, THEME_COLORS, THEME_PRESETS, applyThemePreset, intToHex, hexToInt,
  getLooks, saveLook, clearLook, applyLook } from './settings.js';
import * as bridge from '../bridge.js';

const DRONE_URL = '/dtrh/assets/audio/drone1.mp3'; // Explore uses drift-under-glass; the fall gets its own bed
const MUSICBOX_URL = '/dtrh/assets/audio/musicbox1.mp3'; // THE BALLERINA's bed (crafted; graceful if absent)
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
//
// HOSTED (WPF ChaosWebViewHost): the browser Fullscreen API hard-eats the first
// Esc to exit fullscreen (unpreventable), which stole the game's Esc. So when
// running inside the host we DON'T use the browser API - `enter()`/`leave()` ask
// C# to borderless-toggle its own window (bridge), and Esc never leaves our grip.
// `subscribe(fn)` is the unified change hook (host echo when hosted, DOM event
// otherwise) and returns an unsubscribe fn.
const FS = (() => {
  const root = document.documentElement;
  const request = root.requestFullscreen || root.webkitRequestFullscreen || root.webkitRequestFullScreen;
  const exit = document.exitFullscreen || document.webkitExitFullscreen;
  const enabled = document.fullscreenEnabled || document.webkitFullscreenEnabled;

  if (bridge.isHosted) {
    let on = false;
    const listeners = new Set();
    // C# echoes the actual window state after every toggle (and it starts windowed).
    bridge.on('fullscreen', (m) => {
      on = !!m.on;
      for (const fn of listeners) { try { fn(); } catch (e) { /* ignore */ } }
    });
    return {
      supported: true,
      hosted: true,
      isActive: () => on,
      enter() { bridge.send({ type: 'fullscreen-set', on: true }); },
      leave() { bridge.send({ type: 'fullscreen-set', on: false }); },
      subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },
    };
  }

  const event = ('onwebkitfullscreenchange' in document) ? 'webkitfullscreenchange' : 'fullscreenchange';
  return {
    supported: !!(request && exit && enabled),
    hosted: false,
    event,
    isActive: () => !!(document.fullscreenElement || document.webkitFullscreenElement),
    enter() { const r = request && request.call(root); if (r && r.catch) r.catch(() => {}); },
    leave() { const r = exit && exit.call(document); if (r && r.catch) r.catch(() => {}); },
    subscribe(fn) { document.addEventListener(event, fn); return () => document.removeEventListener(event, fn); },
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
  let voiceSilenced = false;   // VN tutorial: hard-mute the drift voice while the persona speaks
  let runActive = false;       // game mode: the drift whisper + special tube moods live ONLY inside a descent (the Warren hub idles calm + quiet)
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
        droneGain.connect(getMasterOut() || droneCtx.destination);
        drone.volume = 1; // loudness lives in the gain node now
      }
    } catch (e) { droneCtx = null; droneGain = null; }
    if (isMuted()) return;
    if (droneCtx && droneCtx.state === 'suspended') { const r = droneCtx.resume(); if (r && r.catch) r.catch(() => {}); }
    const p = drone.play(); if (p && p.catch) p.catch(() => {});
    syncMusicBox();   // THE BALLERINA rides the same first-interact gate
  }
  const offDroneMute = onMuteChange((m) => {
    if (!drone) return;
    if (m) { try { drone.pause(); } catch (e) { /* ignore */ } }
    else if (droneStarted && running && !document.hidden) {
      if (droneCtx && droneCtx.state === 'suspended') { const r = droneCtx.resume(); if (r && r.catch) r.catch(() => {}); }
      const p = drone.play(); if (p && p.catch) p.catch(() => {});
    }
    syncMusicBox();
  });

  // ---- THE BALLERINA (crafted, Part 2): a music-box bed under the drone ------
  // Lazy element (built on first want), rides the same mute/hidden/pause paths
  // and the 'music' slider at ~0.35x the bed's live level. A missing asset flips
  // mboxFailed and the toggle reads "(asset missing)" - no layer, no errors.
  let mbox = null, mboxFailed = false;
  function musicBoxBroken() { return mboxFailed; }
  function syncMusicBox() {
    const want = !!S.musicBox && droneStarted && !mboxFailed
      && !isMuted() && !document.hidden && !(gamePaused && pauseHeld);
    if (want && !mbox) {
      try {
        mbox = new Audio(MUSICBOX_URL);
        mbox.loop = true;
        mbox.preload = 'auto';
        mbox.volume = 0;
        mbox.addEventListener('error', () => {
          mboxFailed = true;
          try { if (mbox) mbox.pause(); } catch (e) { /* ignore */ }
          mbox = null;
        }, { once: true });
      } catch (e) { mboxFailed = true; mbox = null; }
    }
    if (!mbox) return;
    if (want) { const p = mbox.play(); if (p && p.catch) p.catch(() => {}); }
    else { try { mbox.pause(); } catch (e) { /* ignore */ } }
  }
  const offMusicBox = onSettings((key) => { if (key === 'musicBox') syncMusicBox(); });

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

  // Four Chambers wall dressing: region-scaled posters plastered on the tube
  // wall (idle at density 0 until the game's run brain sets a region).
  const wall = createWallPosters({ scene, layout, media, renderer, camera });

  // Branching paths: in-world forks the game arms mid-chamber. Two diverging
  // veins are carved into the wall with hanging media cards; clicking a card (or
  // leaning) dives down it, then the branch becomes the new endless tube (rebase).
  const junctions = createJunctions({ scene, layout, nav, tunnel, spawner });
  const _jray = new THREE.Raycaster(), _jndc = new THREE.Vector2();

  // In-tube boon draft: at each loop clear the fall parks and themed boon cards
  // hang ahead - click one to shatter it and drop through (the old DOM draft).
  const boonPick = createBoonPick({ scene, camera, layout, nav, fx, hud });
  const _bray = new THREE.Raycaster(), _bndc = new THREE.Vector2();

  // Grab-in-the-tube power-ups: a floating card drifts through the tube every
  // ~60-90s; grabbing it flies the item to the HUD (consumable dock / relic strip).
  // The game (chaosRun) supplies the offer veto + the grab handler via attach().
  const powerupDrops = createPowerupDrops({
    scene, camera, layout,
    getMeta: () => (game && game.getMeta ? game.getMeta() : null),
    canOffer: (id, kind) => (game && game.canOfferPowerup ? game.canOfferPowerup(id, kind) : false),
    synergyBoost: (id) => (game && game.powerupSynergyBoost ? game.powerupSynergyBoost(id) : 1),
    onGrab: (id, kind, screenPos) => (game && game.onPowerupGrabbed) ? game.onPowerupGrabbed(id, kind, screenPos) : false,
  });
  const _uray = new THREE.Raycaster(), _undc = new THREE.Vector2();

  // The Dollhouse as an in-ambient menu: floating hub stations + the FALL IN
  // portal, shown while the Warren is up. The game layer (warren.js) owns the
  // defs + what a pick means; this is just the 3D presenter + pointer routing.
  const hubStations = createHubStations({ scene, camera, layout, nav, fx, hud });
  const _hray = new THREE.Raycaster(), _hndc = new THREE.Vector2();

  // Wall-poster hold: press-and-hold a wall gif to swing the camera onto it (a
  // closer look); release swings back. The fall eases to a near-stop meanwhile.
  const _pray = new THREE.Raycaster(), _pndc = new THREE.Vector2(), _pv = new THREE.Vector3();
  let _heldPoster = false;

  // ---- rebase: the junction dive handoff -------------------------------------
  // fallNav rode the chosen vein to its tail and reports the exit frame. Build a
  // fresh endless loop, rigid-transform it so its entry frame == the vein exit
  // (coaxial, fog-hidden), mutate `layout` IN PLACE so every captured ref follows,
  // rebuild the tunnel geometry (same material -> fx/tint bindings survive), hand
  // the camera back onto the rail, and re-seed the spine-coupled content. The old
  // trunk + the rejected branch are gone; the chosen branch IS the new tube.
  const _UP = new THREE.Vector3(0, 1, 0);
  function rebaseLoop(exit) {
    if (!exit) return;
    const nl = buildLoopLayout();
    const nf = nl.frameAt(0);                        // fresh loop's entry frame (pre-transform)
    const newM = new THREE.Matrix4().makeBasis(nf.binormal, nf.normal, nf.tangent);
    // exit basis, built the same way frameAt does (binormal = tangent x UP)
    const exBin = new THREE.Vector3().crossVectors(exit.tangent, _UP);
    if (exBin.lengthSq() < 1e-6) exBin.set(1, 0, 0);
    exBin.normalize();
    const exNorm = new THREE.Vector3().crossVectors(exBin, exit.tangent).normalize();
    const exM = new THREE.Matrix4().makeBasis(exBin, exNorm, exit.tangent);
    const Rq = new THREE.Quaternion().setFromRotationMatrix(exM.multiply(newM.invert()));
    // rigid-transform the closed spine so its entry lands on (and aims down) the vein exit
    for (const p of nl.spine.points) p.sub(nf.pos).applyQuaternion(Rq).add(exit.pos);
    nl.spine.updateArcLengths();
    // mutate the shared layout object in place (keeps identity for all captured refs)
    layout.spine = nl.spine;
    layout.pointAt = nl.pointAt;
    layout.frameAt = nl.frameAt;
    layout.frameAtDepth = nl.frameAtDepth;
    tunnel.rebuild(layout);                          // geometry-only swap
    // the closed ring's incoming tail (arc ~0.85..1.0) converges on this very
    // entry point (a closed ring must return to where it starts), so its last
    // stretch lies nearly coaxial with the vein corridor: left solid it z-fights
    // the vein wall (saw-tooth shimmer) and slices an opaque wall across the ride
    // path. HIDE it (discard, not fade) - safe because the ridden vein encloses
    // the camera for the whole ride, and its fog-colored end-annulus masks the
    // opened region at the seam. The window wraps a hair past 0 so no sliver of
    // wall survives right at the entry. junctions' handoff teardown clears the
    // cut the moment the camera is on the new loop, restoring the full ring
    // behind it.
    tunnel.setCut(0.85, 0.0005, true);
    // NB: the camera handoff is fallNav's job now - it rides the vein to the tail
    // and snaps onto this coaxial loop (depth 0 == the vein exit) itself. Calling
    // rebaseTo here would yank the camera off the vein the instant we build (~vt 0.45).
    // re-seed spine-coupled content onto the new loop
    try { spawner.clearLive(); } catch (e) { /* ignore */ }
    try { if (wall && wall.reset) wall.reset(); } catch (e) { /* ignore */ }
    try { if (fog && fog.rebase) fog.rebase(layout); } catch (e) { /* ignore */ }
    try { if (fx && fx.rebase) fx.rebase(); } catch (e) { /* ignore */ }
  }
  junctions.onVeinEnd = rebaseLoop;

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
    // Crafted unlocks (Part 2): hub-time ownership, read LAZILY at panel-open
    // (the meta snapshot lands after buildHud runs).
    craftedOwned: game ? (id) => { try { return !!game.craftedOwned(id); } catch (e) { return false; } } : () => false,
    musicBoxBroken: () => musicBoxBroken(),
  });
  const offHudMute = onMuteChange(hudBits.syncMute);
  hudBits.syncMute(isMuted());

  // ---- the bubble game (main mechanic) ----------------------------------------
  const bubbles = createBubbles({
    hud, canvas, media,
    onPop: (kind, gain, combo) => {
      director.notePop(kind, gain, combo);
      hudBits.setScore(bubbles.getScore(), combo);
      if (kind === 'lucky') fx.pulseFlash(0.5);
    },
    onEffect: (kind) => {
      director.noteEffect();
      fx.pulseFlash(0.45); // a brightness punch when an effect fires (no position pump)
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
  // THE HOURGLASS (crafted, Part 2): the pause card always opens (Esc ladder +
  // surface keep working, the pointer guard keeps pops from firing through the
  // dim), but the FREEZE is gated. Without the hourglass mid-descent the fall
  // keeps moving behind the card - "no pause down here". Hidden/covered holds
  // are OS-level and stay unconditional (they never route through here).
  let gamePaused = false;
  let pauseHeld = true;   // did the current pause actually freeze the world?
  function setGamePaused(v, forceHold = false) {
    v = !!v;
    if (v === gamePaused) return;
    if (v && director.isOver()) return; // results screen owns the end state
    const held = v
      ? (forceHold || !game || !game.canHoldPause || !!game.canHoldPause())
      : pauseHeld;   // unpause undoes exactly what the pause did
    gamePaused = v;
    pauseHeld = held;
    if (held) {
      nav.setPaused(v);
      bubbles.setFrozen(v);
      wall.setPaused(v);
      if (game) game.setPaused(v);
      if (v) drift.stop(); else if (!game || runActive) drift.start();
      if (drone && droneStarted) {
        if (v) { try { drone.pause(); } catch (e) { /* ignore */ } }
        else if (!isMuted() && !document.hidden) {
          if (droneCtx && droneCtx.state === 'suspended') { const r = droneCtx.resume(); if (r && r.catch) r.catch(() => {}); }
          const p = drone.play(); if (p && p.catch) p.catch(() => {});
        }
      }
    }
    syncSpawner();
    syncMusicBox();
    hudBits.showPause(v, { held });
    if (!v) pauseHeld = true;
  }
  // ---- fullscreen <-> pause reconciliation -------------------------------------
  // In element-fullscreen, Chromium/WebView2 eats the FIRST Esc to EXIT fullscreen
  // (a mandated behavior we can't disable), so it never reaches onKeyDown - the
  // player had to press Esc twice to pause. We instead treat that fullscreen-exit
  // as the pause: on any exit during a live run, open the pause card. A DELIBERATE
  // un-fullscreen (dock button / F11) sets suppressExitPause so it doesn't pause.
  let suppressExitPause = false;
  function toggleFullscreen() {
    if (FS.isActive()) {
      suppressExitPause = true;
      FS.leave();
      setTimeout(() => { suppressExitPause = false; }, 400); // self-heal if the exit event never fires
    } else FS.enter();
  }
  FS.subscribe(() => {
    if (FS.isActive()) return;                                  // only the EXIT edge
    if (suppressExitPause) { suppressExitPause = false; return; } // we left on purpose
    // Standalone browser only: the browser ate the first Esc to exit fullscreen,
    // so turn that exit into the pause. Hosted owns Esc directly (onKeyDown ladder).
    if (!FS.hosted && game && runActive && !gamePaused && !director.isOver()) setGamePaused(true, true);   // browser-eaten Esc: always a real hold
  });

  function onKeyDown(e) {
    if (e.key === 'F11') { e.preventDefault(); toggleFullscreen(); return; }  // F11 toggles fullscreen
    if (e.key !== 'Escape') return;
    if (panel.isOpen()) { panel.close(); return; }
    // M5: while the Warren is up it owns Esc (backs out of the dollhouse /
    // closes its modals) - the engine pause card belongs to live descents.
    if (game && game.onEsc && game.onEsc()) return;

    // Hosted (WPF): the browser no longer eats Esc for fullscreen, so we own the
    // whole ladder - 1) pause (stay fullscreen)  2) drop fullscreen (stay paused)
    // 3) leave the game gracefully. The panic-key double-tap is suppressed C#-side
    // while the DtRH window is up, so these presses never kill the app.
    if (FS.hosted) {
      if (!director.isOver() && !gamePaused) { setGamePaused(true); return; }   // 1) pause
      if (FS.isActive()) { toggleFullscreen(); return; }                        // 2) exit fullscreen
      bridge.send({ type: 'exit' });                                            // 3) graceful close
      return;
    }
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
        e.target.closest('.sf-dock, .sf-panel, .sf-results, .sf-pause, .sf-skip, .sf-explore, .sf-uitoggle, .sf-fs-tip, .rh-bubble, .rh-fx-flashclip, .cf-bubble, .cf-hud, .cf-eye-btn, .cf-overlay')) return;
    const r = canvas.getBoundingClientRect();
    if (!r.width || !r.height) return;
    const nx = ((e.clientX - r.left) / r.width) * 2 - 1;
    const ny = -((e.clientY - r.top) / r.height) * 2 + 1;
    // the hub menu is up: a click on a station opens/dives it, a click on empty
    // tube is a "miss" (the game blurs the focused station / closes its panel).
    // The hub owns the pointer entirely - no fall-through to grabs.
    if (hubStations.isBusy()) {
      _hray.setFromCamera(_hndc.set(nx, ny), camera);
      const stId = hubStations.pick(_hray);   // owns the rules: cards beat portal, ring-only portal
      if (stId) { if (game && game.onStationPick) game.onStationPick(stId); }
      else if (game && game.onStationMiss) game.onStationMiss();
      return;
    }
    // a boon draft is open: a click on a card shatters it + takes the boon
    if (boonPick && boonPick.isBusy()) {
      const picks = boonPick.getPickables();
      if (picks.length) {
        _bray.setFromCamera(_bndc.set(nx, ny), camera);
        const hits = _bray.intersectObjects(picks, true);
        for (const h of hits) {
          let o = h.object;
          while (o && !(o.userData && o.userData.type === 'booncard')) o = o.parent;
          if (o && o.userData && o.userData.type === 'booncard') { boonPick.pickIndex(o.userData.index); return; }
        }
      }
      return; // draft owns the pointer: never fall through to a card grab
    }
    // a fork is open: ONLY a direct click on a doorway card dives down that
    // branch - and only when that card is the CLOSEST interactive thing under
    // the cursor (a gif / power-up / poster drifting in front of a doorway must
    // grab, not dive). Anything else falls through - the player stays free to
    // look around and grab all around a junction (getPickables is empty outside
    // the linger, so approach + ride cost nothing).
    if (junctions && junctions.isBusy()) {
      const picks = junctions.getPickables();
      if (picks.length) {
        _jray.setFromCamera(_jndc.set(nx, ny), camera);
        const hits = _jray.intersectObjects(picks, true);
        let jDist = Infinity, jIdx = null;
        for (const h of hits) {
          let o = h.object;
          while (o && !(o.userData && o.userData.type === 'veinmouth')) o = o.parent;
          if (o && o.userData && o.userData.type === 'veinmouth') { jDist = h.distance; jIdx = o.userData.index; break; }
        }
        if (jIdx != null) {
          let dOther = Infinity;
          if (powerupDrops) {
            const uh = _jray.intersectObjects(powerupDrops.getPickables(), true);
            if (uh.length) dOther = Math.min(dOther, uh[0].distance);
          }
          if (wall && wall.getPickables && !nav.isInVein()) {
            const ph = _jray.intersectObjects(wall.getPickables(), false);
            if (ph.length) dOther = Math.min(dOther, ph[0].distance);
          }
          if (spawner.probeGrab) {
            const ds = spawner.probeGrab(nx, ny, camera);
            if (ds != null) dOther = Math.min(dOther, ds);
          }
          if (jDist <= dOther) { junctions.pickIndex(jIdx); return; }
        }
      }
    }
    // a power-up card drifting in the tube: grab it (flies to the HUD). Priority over
    // wall posters / falling cards so a deliberate grab always wins.
    if (powerupDrops) {
      const picks = powerupDrops.getPickables();
      if (picks.length) {
        _uray.setFromCamera(_undc.set(nx, ny), camera);
        const hits = _uray.intersectObjects(picks, true);
        for (const h of hits) {
          let o = h.object;
          while (o && !(o.userData && o.userData.type === 'powerup')) o = o.parent;
          if (o && o.userData && o.userData.type === 'powerup') { if (powerupDrops.grab(o)) return; }
        }
      }
    }
    // a wall poster: hold it to swing the camera and face it (a closer look)
    if (wall && wall.getPickables && !nav.isInVein()) {
      const picks = wall.getPickables();
      if (picks.length) {
        _pray.setFromCamera(_pndc.set(nx, ny), camera);
        const hits = _pray.intersectObjects(picks, false);
        for (const h of hits) {
          let o = h.object;
          while (o && !(o.userData && o.userData.type === 'poster')) o = o.parent;
          if (o && o.userData && o.userData.type === 'poster') {
            // Sticky Fingers: the hold RIPS the gif off the wall and wields it as
            // the paddle (spawner card around the same pool texture). Without the
            // boon the classic hold stays: swing the camera to face the poster.
            if (game && game.wieldPosters && game.wieldPosters()
                && wall.ripPoster && spawner.grabExternalCard && !spawner.isGrabbing() && !spawner.spotlightActive()) {
              const ripped = wall.ripPoster(o);   // null = the Gallery melted it off the wall
              // (no noteLike here: grabExternalCard's startGrab already notes the grab)
              if (ripped && spawner.grabExternalCard(ripped, camera)) fx.pulseFlash(0.4); // the snatch punch
              return;                 // this pointer owns the rip (wield or melt)
            }
            const rec = wall.grabPoster(o);
            if (rec) {
              _heldPoster = true;
              nav.setFaceTarget(wall.getHeldWorldPos(_pv));
              if (rec.assetName) spawner.noteLike(rec.assetName, 'image');
              fx.pulseFlash(0.4);   // a little punch on the turn-in (brightness, not a zoom)
              return;               // this pointer owns the poster
            }
          }
        }
      }
    }
    spawner.grabAtPointer(nx, ny, camera);
  }
  function grabPointerUp() {
    if (_heldPoster) { wall.releasePoster(); nav.setFaceTarget(null); _heldPoster = false; }
    spawner.releaseGrab();
  }
  // while a boon draft is open, hover-raycast the cards to drive the caption bar
  // (and while the hub menu is up, hover-lift the aimed station + cursor)
  function boonAimMove(e) {
    if (hubStations.isBusy()) {
      const r0 = canvas.getBoundingClientRect();
      if (!r0.width || !r0.height) return;
      const hx = ((e.clientX - r0.left) / r0.width) * 2 - 1;
      const hy = -((e.clientY - r0.top) / r0.height) * 2 + 1;
      _hray.setFromCamera(_hndc.set(hx, hy), camera);
      const aimed = hubStations.pick(_hray);   // same rules as clicking - hover never lies
      hubStations.setAimed(aimed);
      canvas.style.cursor = aimed ? 'pointer' : '';
      return;
    }
    if (!boonPick || !boonPick.isBusy()) {
      if (canvas.style.cursor) canvas.style.cursor = '';   // hub closed mid-hover: drop the hand cursor
      return;
    }
    const r = canvas.getBoundingClientRect();
    if (!r.width || !r.height) return;
    const nx = ((e.clientX - r.left) / r.width) * 2 - 1;
    const ny = -((e.clientY - r.top) / r.height) * 2 + 1;
    const picks = boonPick.getPickables();
    _bray.setFromCamera(_bndc.set(nx, ny), camera);
    const hits = picks.length ? _bray.intersectObjects(picks, true) : [];
    let idx = -1;
    for (const h of hits) {
      let o = h.object;
      while (o && !(o.userData && o.userData.type === 'booncard')) o = o.parent;
      if (o && o.userData && o.userData.type === 'booncard') { idx = o.userData.index; break; }
    }
    boonPick.setAimed(idx);
  }
  canvas.addEventListener('pointerdown', grabPointerDown);
  canvas.addEventListener('pointermove', boonAimMove);
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
  let composer = null, bloomPass = null;
  const BLOOM_BASE = 0.42;
  if (Q.bloom) try {
    const [{ EffectComposer }, { RenderPass }, { UnrealBloomPass }] = await Promise.all([
      import('three/addons/postprocessing/EffectComposer.js'),
      import('three/addons/postprocessing/RenderPass.js'),
      import('three/addons/postprocessing/UnrealBloomPass.js'),
    ]);
    composer = new EffectComposer(renderer);
    composer.addPass(new RenderPass(scene, camera));
    bloomPass = new UnrealBloomPass(
      new THREE.Vector2(window.innerWidth, window.innerHeight), BLOOM_BASE, 0.5, 0.5);
    composer.addPass(bloomPass);
    composer.setSize(window.innerWidth, window.innerHeight);
  } catch (err) {
    console.warn('[sissy-fall] bloom unavailable, rendering without it:', err);
  }

  // Give the first user-media decodes a head start behind the loader, so the
  // opening plunge meets finished cards instead of hollow frames.
  await spawner.warm();

  // begin the voice chain (autoplay is unlocked - we got here via the begin click).
  // Game mode boots into the Warren hub, NOT a descent: the drift whisper stays
  // silent (and the tube stays calm) until setRunActive(true) opens the hole.
  if (!game) drift.start();

  // Game mode: the run brain owns when the whisper + special tube moods turn on.
  // (Called from chaosRun on GO / recap; forces the hub into a calm, quiet idle.)
  // The tunnel's card/video spawner (proximity clips + spotlight takeovers) runs
  // ONLY inside a descent in game mode, plus the usual ESC-pause / hidden-tab
  // parks. One source of truth so the Warren hub never plays video behind it.
  function syncSpawner() {
    // a soft (un-held) pause leaves the conveyor running - the fall doesn't wait
    spawner.setPaused((gamePaused && pauseHeld) || document.hidden || (game && !runActive));
  }

  function setRunActive(on) {
    runActive = !!on;
    // The options panel is session-lived and shared hub<->descent: force it shut
    // on a run transition so opening it in the Warren (or leaving it open) never
    // strands it over the descent/hub.
    try { if (panel.isOpen()) panel.close(); } catch (e) { /* ignore */ }
    // In game mode the hub (Warren) owns its own chrome, so suppress the Fall's
    // redundant dock + gear there (the "double options" bug); a live descent
    // keeps them (that's the in-run gear the Eye toggle leaves visible).
    hud.classList.toggle('sf-hub', !!game && !runActive);
    try { fx.forceZone(runActive ? null : 'calm'); } catch (e) { /* ignore */ }
    try { junctions.setActive(runActive); } catch (e) { /* ignore */ } // forks only inside a descent
    syncSpawner();                        // park the video conveyor outside a descent
    if (runActive) { if (!voiceSilenced) drift.start(); }
    else drift.stop();
  }

  // ---- DtRH game mode ----------------------------------------------------------
  if (game) {
    hud.classList.add('sf-game-mode');   // hides the Fall's own score readout (CSS)
    setRunActive(false);                 // hub idles: no pink-fog/storm moods, no whisper
    startDrone();                        // no gesture gate in the hosted WebView2
    // The Fall's own bubble field stands down completely - setIntensity(0)
    // still targets COUNT_MIN, so pause it (clears + stops spawning). Veil
    // punch-through washes skip while paused; M4 routes veils into the game.
    bubbles.setPaused(true);
    // Wave 2: the game also gets the spawner - pickups, held-card projection,
    // forced spotlights and the auto-promotion gate all live there.
    game.attach({ nav, fx, director, hud, canvas, spawner, media, wall, drift, junctions, boonPick, powerupDrops, hubStations, flashBurst: (n) => bubbles.flashBurst(n),
      // the Ripple flings on-screen flash clips off screen along the hit angle
      flingFlashesNear: (px, py, r, probe) => bubbles.flingFlashesNear(px, py, r, probe),
      openOptions: () => panel.toggle(),
      // Fullscreen for the Warren's own dock (the Fall dock's button is hidden in
      // the hub - see the sf-hub CSS - so the hub carries its own).
      fullscreenSupported: FS.supported,
      isFullscreen: () => FS.isActive(),
      toggleFullscreen: () => toggleFullscreen(),
      onFullscreenChange: (cb) => FS.subscribe(cb),
      // Reveal earned option-panel dials from the meta snapshot (purchased "Dials").
      syncOptionUnlocks: (ids) => panel.setUnlocks(ids),
      // Live meta-rank for the panel's DESCENT section (deep pool variants).
      setOptionProgress: (p) => panel.setProgress(p),
      silenceVoice: (on) => { voiceSilenced = !!on; },
      // Four Chambers: per-chamber bloom weight (multiplier on the base
      // strength). No-ops gracefully on the mobile tier (no bloom pass).
      setBloomStrength: (mult) => { if (bloomPass) bloomPass.strength = BLOOM_BASE * (mult || 1); },
      setRunActive });
    // The lessons row (guided FTUE replay) only exists in game mode - the reset
    // op lives on the meta bridge the standalone Fall doesn't have.
    panel.setGameHooks({ resetOnboarding: () => game.resetOnboarding() });
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

    if (gamePaused && pauseHeld) { // frozen frame under the pause overlay; ESC resumes
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
    director.setHold(spawner.isGrabbing() || _heldPoster); // grabbing a card OR facing a wall gif eases the fall
    if (game) {
      // Game mode: chaosRun owns the field (its own spawner + rAF integrator);
      // the Fall's bubble game is paused for the whole session.
      game.frame(dt);
    } else {
      bubbles.setIntensity(director.getIntensity());
      bubbles.setRunTime(director.getRunTime()); // gates which bubble kinds can spawn
    }
    nav.setLookSuppressed(spawner.isGrabbing() || _heldPoster); // a held card paddle / faced poster steers, not drag-look
    if (_heldPoster) {
      wall.advanceHeld(nav.getDepth(), dt);         // ride the poster along + swing level with us first
      nav.setFaceTarget(wall.getHeldWorldPos(_pv)); // then aim at its fresh position
    }
    nav.update(dt);

    const depth = nav.getDepth();
    const speed = nav.getSpeed();
    // while a fork is alive, the content spawners keep out of the chamber's depth
    // span - anything placed in there floats in the trunk cut or hides behind the
    // opaque room, which starved the fork approach of grabbables.
    const jspan = junctions.getBlockedSpan ? junctions.getBlockedSpan() : null;
    spawner.setBlockedSpan(jspan);
    wall.setBlockedSpan(jspan);
    powerupDrops.setBlockedSpan(jspan);
    // heat + run time drive the tube-card progression: none until ~18s, then
    // sparse and thickening as the run deepens.
    spawner.update(camera, depth, dt, clock.elapsedTime, director.getIntensity(), director.getRunTime());
    wall.update(camera, depth, dt); // region-scaled wall posters (idle at density 0)
    junctions.update(depth, dt);    // branching-path forks (idle until the game arms one)
    boonPick.update(dt, depth);     // in-tube boon draft (idle until a loop clears)
    powerupDrops.update(dt, depth, camera.quaternion);   // floating grab-in-the-tube power-ups
    hubStations.update(dt);         // the Dollhouse stations (idle unless the Warren is up)

    // Sidechain the mix: a spotlight video owns the whole foreground (the
    // voice whispers down under it, the bed drops hardest); otherwise a
    // speaking voice ducks the bed and swells back in the gaps. Everything
    // eases over ~0.5s so nothing pumps.
    const spotOn = spawner.spotlightActive();
    voiceDuck += ((spotOn ? VOICE_DUCK_SPOT : 1) - voiceDuck) * Math.min(1, dt * 2.5);
    drift.setDuck(voiceSilenced ? 0 : voiceDuck);

    // drone: volume follows the fall speed; 1Hz watchdog vs. autoplay killings
    if (drone) {
      const raw = DRONE_FLOOR + (DRONE_MAX - DRONE_FLOOR) * Math.min(1, speed / SPEED_REF);
      droneVol += (raw - droneVol) * Math.min(1, dt * 2);
      const duckTarget = spotOn ? DRONE_DUCK_SPOT : (!voiceSilenced && drift.isSpeaking() ? DRONE_DUCK : 1);
      droneDuck += (duckTarget - droneDuck) * Math.min(1, dt * 2.5);
      // the 'music' slider is a 0..1 multiplier over the speed-following bed
      setDroneVolume(isMuted() ? 0 : Math.max(0, Math.min(1, droneVol)) * getLevel('music') * droneDuck);
      // THE BALLERINA: the music box rides the same envelope at ~0.35x
      if (mbox) mbox.volume = isMuted() ? 0
        : Math.max(0, Math.min(1, droneVol * getLevel('music') * droneDuck * 0.35));
      droneWatch += dt;
      if (droneWatch >= 1) {
        droneWatch = 0;
        syncMusicBox();   // 1Hz self-heal, same cadence as the drone watchdog
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
      syncSpawner(); // parks card + spotlight videos (no hidden-tab audio)
      if (drone) { try { drone.pause(); } catch (e) { /* ignore */ } }
      syncMusicBox();
    } else if (!running) {
      running = true;
      if (!director.isOver() && !game) bubbles.setPaused(false);   // game mode: field stays down
      syncSpawner();
      if (drone && droneStarted && !isMuted() && !(gamePaused && pauseHeld)) {
        if (droneCtx && droneCtx.state === 'suspended') { const r = droneCtx.resume(); if (r && r.catch) r.catch(() => {}); }
        const p = drone.play(); if (p && p.catch) p.catch(() => {});
      }
      syncMusicBox();
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
    powerupDrops.dispose();
    hubStations.dispose();
    canvas.removeEventListener('pointerdown', grabPointerDown);
    canvas.removeEventListener('pointermove', boonAimMove);
    window.removeEventListener('pointerup', grabPointerUp);
    window.removeEventListener('pointercancel', grabPointerUp);
    document.removeEventListener('visibilitychange', onVisibility);
    if (game) { try { game.dispose(); } catch (e) { /* ignore */ } }
    panel.dispose();
    fx.dispose();
    nav.dispose();
    spawner.dispose();
    wall.dispose();
    junctions.dispose();
    boonPick.dispose();
    bubbles.dispose();
    drift.dispose();
    offDroneMute();
    offHudMute();
    offCssTheme();
    offMusicBox();
    if (mbox) { try { mbox.pause(); mbox.src = ''; } catch (e) { /* ignore */ } mbox = null; }
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
function buildHud(hud, { challenge, supportsMediaAdd, onMute, onAddMedia, onRestart, onOptions, onResume, onSkip, onSurface = null,
  craftedOwned = () => false, musicBoxBroken = () => false }) {
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
  // voiceover set: the biome VO ships in two voices (sissy/bambi share one,
  // Circe's Lock has her own) - two buttons swap between them live. The
  // highlighted one is the EFFECTIVE voice (persona default until picked).
  // The picker is shown only when more than one voice is actually available:
  // Circe's set is biome-only (no backbone render yet), so it stays gated out
  // of voiceSets() until it's complete and this row simply doesn't render.
  let offVoiceSync = () => {};
  if (voiceSets().length > 1) {
    const voiceRow = document.createElement('div');
    voiceRow.className = 'sf-audio-row sf-voice-row';
    const voiceHead = document.createElement('div');
    voiceHead.className = 'sf-audio-rowhead';
    const voiceName = document.createElement('span');
    voiceName.textContent = 'voiceover';
    voiceHead.appendChild(voiceName);
    const voiceBtns = document.createElement('div');
    voiceBtns.className = 'sf-voice-btns';
    const voiceEls = new Map();
    const syncVoice = () => {
      const cur = getVoice();
      for (const [key, b] of voiceEls) b.classList.toggle('is-on', key === cur);
    };
    for (const v of voiceSets()) {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'sf-voice-btn';
      b.textContent = v.label;
      b.addEventListener('click', () => { setVoice(v.key); syncVoice(); });
      voiceEls.set(v.key, b);
      voiceBtns.appendChild(b);
    }
    voiceRow.append(voiceHead, voiceBtns);
    audioPanel.appendChild(voiceRow);
    offVoiceSync = onVoice(syncVoice);   // persona default can land after build
    syncVoice();
  }
  // THE BALLERINA (crafted, Part 2): the music-box toggle. Built always,
  // gated LAZILY at caret-open (ownership arrives after buildHud runs).
  const mbRow = document.createElement('div');
  mbRow.className = 'sf-voice-row sf-musicbox-row';
  mbRow.hidden = true;
  const mbHead = document.createElement('span');
  mbHead.className = 'sf-voice-head';
  mbHead.textContent = 'music box';
  const mbBtns = document.createElement('div');
  mbBtns.className = 'sf-voice-btns';
  const mbBtn = document.createElement('button');
  mbBtn.type = 'button';
  mbBtn.className = 'sf-voice-btn';
  const syncMbBtn = () => {
    if (musicBoxBroken()) {
      mbBtn.textContent = '🩰 (asset missing)';
      mbBtn.classList.remove('is-on');
      return;
    }
    mbBtn.textContent = S.musicBox ? '🩰 spinning' : '🩰 wound down';
    mbBtn.classList.toggle('is-on', !!S.musicBox);
  };
  mbBtn.addEventListener('click', () => {
    if (musicBoxBroken()) { syncMbBtn(); return; }
    updateSetting('musicBox', !S.musicBox);
    syncMbBtn();
  });
  syncMbBtn();
  mbBtns.appendChild(mbBtn);
  mbRow.append(mbHead, mbBtns);
  audioPanel.appendChild(mbRow);

  caretBtn.addEventListener('click', () => {
    const open = audioPanel.hidden;
    audioPanel.hidden = !open;
    caretBtn.classList.toggle('is-open', open);
    if (open) { mbRow.hidden = !craftedOwned('the_ballerina'); syncMbBtn(); }
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
  // THE RING BOX (crafted, Part 2): up to 3 kept Looks. Empty slot click keeps
  // the current colors; a kept slot click wears it; its little × is a two-step
  // forget. Gated LAZILY at paint-open (ownership arrives after buildHud runs).
  const looksRow = document.createElement('div');
  looksRow.className = 'sf-looks-row';
  looksRow.hidden = true;
  themePanel.appendChild(looksRow);
  const rebuildLooks = () => {
    looksRow.innerHTML = '';
    const head = document.createElement('span');
    head.className = 'sf-looks-head';
    head.textContent = '💍 kept looks';
    looksRow.appendChild(head);
    const looks = getLooks();
    for (let i = 0; i < 3; i++) {
      const slot = document.createElement('span');
      slot.className = 'sf-look-slot';
      const look = looks[i];
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'sf-swatch sf-look' + (look ? '' : ' is-empty');
      if (look) {
        b.style.background = `linear-gradient(135deg, ${intToHex(look.colLine)}, ${intToHex(look.colSpiral)})`;
        b.title = 'wear this look';
        b.addEventListener('click', () => { applyLook(i); refreshInputs(); });
      } else {
        b.textContent = '+';
        b.title = 'keep the current look';
        b.addEventListener('click', () => { saveLook(i); rebuildLooks(); });
      }
      slot.appendChild(b);
      if (look) {
        const x = document.createElement('button');
        x.type = 'button';
        x.className = 'sf-look-x';
        x.textContent = '×';
        x.title = 'forget this look';
        let armed = false;
        x.addEventListener('click', (e) => {
          e.stopPropagation();
          if (!armed) { armed = true; x.classList.add('is-armed'); x.title = 'click again to forget'; return; }
          clearLook(i);
          rebuildLooks();
        });
        slot.appendChild(x);
      }
      looksRow.appendChild(slot);
    }
  };
  paintBtn.addEventListener('click', () => {
    const open = themePanel.hidden;
    themePanel.hidden = !open;
    paintBtn.classList.toggle('is-open', open);
    if (open) {
      const owned = craftedOwned('the_ring_box');
      looksRow.hidden = !owned;
      if (owned) rebuildLooks();
    }
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
    fsBtn.textContent = '[ ]';
    const syncFs = () => {
      const on = FS.isActive();
      fsBtn.classList.toggle('is-on', on);
      fsBtn.textContent = on ? '] [' : '[ ]';
      fsBtn.setAttribute('aria-label', on ? 'exit fullscreen' : 'go fullscreen');
      fsBtn.title = on ? 'exit fullscreen (F11)' : 'go fullscreen (F11)';
    };
    syncFs();
    fsBtn.addEventListener('click', () => toggleFullscreen());
    fsCleanup = FS.subscribe(syncFs);
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

  // (The site's "explore the app" corner link is intentionally omitted in the
  // hosted DtRH game - navigation is locked to ccp.game, so it had nowhere to go.)

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
  // Hosted-only Esc ladder hint: tells the player the next Esc drops fullscreen,
  // then (once windowed) leaves. Hidden in the standalone browser build.
  const pauseHint = document.createElement('p');
  pauseHint.className = 'sf-pause-hint';
  pauseHint.hidden = true;
  const refreshPauseHint = () => {
    pauseHint.hidden = !FS.hosted;
    if (FS.hosted) pauseHint.textContent = FS.isActive()
      ? 'Press Esc again to exit fullscreen mode'
      : 'Press Esc again to leave';
  };
  const pauseBtn = document.createElement('button');
  pauseBtn.type = 'button';
  pauseBtn.className = 'sf-btn sf-btn-primary';
  pauseBtn.textContent = 'keep falling';
  pauseBtn.addEventListener('click', onResume);
  pauseCard.append(pauseH, pauseP, pauseHint, pauseBtn);
  // Stage-2 Esc drops fullscreen asynchronously (host round-trip), so keep the
  // hint in step when the state actually flips while the pause card is open.
  const stopHintSync = FS.subscribe(() => { if (!pause.hidden) refreshPauseHint(); });
  const priorFsCleanup = fsCleanup;
  fsCleanup = () => { try { if (priorFsCleanup) priorFsCleanup(); } finally { stopHintSync(); } };
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
    showPause(v, { held = true } = {}) {
      pause.hidden = !v;
      // THE HOURGLASS: a soft (un-held) pause names the missing craft.
      pauseH.textContent = held ? 'paused' : 'no pause down here';
      pauseP.textContent = held ? 'the fall waits for you'
        : 'the hole doesn\'t wait. craft the hourglass and it will.';
      pauseCard.classList.toggle('sf-pause-soft', v && !held);
      if (v) refreshPauseHint();
      // Keep the controls (⚙ options / audio / theme) reachable while paused:
      // reveal the chrome and (via .sf-paused CSS) lift it above the pause dim.
      // On resume, restore the player's own show/hide toggle state.
      hud.classList.toggle('sf-paused', v);
      if (v) hud.classList.remove('sf-chrome-hidden');
      else hud.classList.toggle('sf-chrome-hidden', !chromeShown);
      uiToggle.classList.toggle('is-on', v || chromeShown);
    },
    setSkipVisible(v) { if (skipBtn.hidden === !v) return; skipBtn.hidden = !v; },
    dispose() { if (fsCleanup) fsCleanup(); offVoiceSync(); for (const b of bits) b.remove(); },
  };
}
