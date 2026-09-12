/* ============================================================================
 * board/camera.js - the orbit rig, the four view presets and their buttons.
 *
 * A hand-rolled orbit rig, not OrbitControls: the left button belongs to
 * drag.js (picking a man up), so nothing here ever claims it. What is claimed:
 *
 *   right / middle drag   orbit around the look-at target
 *   shift + that drag     pan the target across the board (plus a margin)
 *   wheel                 zoom, radius 5..18
 *   one finger            orbit, but ONLY when drag.js did not take the touch
 *   two fingers           pan the target, pinch to zoom
 *   keys 1 2 3 4          normal / top / side / opponent, with a tweened pan
 *
 * State is spherical around a look-at target: theta is the azimuth (0 sits on
 * the +Z side, which is white's home), phi is measured DOWN from straight up,
 * so phi 0 is a bird's eye and a big phi is a low camera. Everything the game
 * used to do to the camera still happens on top of that: setSide swings round
 * behind whoever is to move (only while the normal preset is on and the player
 * has not taken the camera themselves), and the ramp's sway is an additive
 * wobble applied last, after the rig has placed the camera for the frame.
 *
 * Wiring: scene.js builds the rig and forwards setSide / update / setCameraSway
 * into it; boot.js calls attachCameraUi to light the button row up.
 * ==========================================================================*/

import * as THREE from 'three';
import { fitScaleFor } from './frame.js';

const DEG = Math.PI / 180;

/** Every number that decides how the camera feels. One place, on purpose. */
export const CAM_TUNING = Object.freeze({
  radiusMin: 5,
  radiusMax: 18,
  phiMin: 1.5 * DEG,          // all but straight down; 0 exactly is degenerate
  phiMax: 82 * DEG,           // never below the board: 8 degrees of elevation
  panLimit: 5.0,              // how far off centre the look-at may wander
  orbitSpeed: 0.0075,         // radians per pixel dragged
  panSpeed: 0.0022,           // world units per pixel, per unit of radius
  zoomStep: 1.12,             // one wheel notch
  inertiaDamping: 6.0,        // e-fold rate of the spin left after a release
  inertiaFloor: 0.02,         // radians per second under which it just stops
  tweenSec: 0.9,              // a preset pan
  dolly: 0.12,                // the little push-in mid swing, as before
  swayPos: [0.55, 0.28],      // x, y of the ramp's additive wobble
  swayLook: 0.4,
  swayRoll: 0.035,
});

// The presets, in rig terms. `normal` is the framing the board has always had:
// radius 11.96 at 35.8 degrees of elevation is exactly the old 9.7 out / 7 up.
export const PRESETS = Object.freeze({
  normal: { phi: Math.atan2(9.7, 7), radius: Math.hypot(9.7, 7), target: [0, 0.10, 0] },
  top: { phi: 2.5 * DEG, radius: 12.6, target: [0, 0, 0] },
  side: { phi: 78 * DEG, radius: 11.5, target: [0, 0.12, 0] },
  opponent: { phi: Math.atan2(9.7, 7), radius: Math.hypot(9.7, 7), target: [0, 0.10, 0] },
});

const easeInOut = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
/** The short way round from a to b, in radians. */
const shortWay = (a, b) => a + Math.atan2(Math.sin(b - a), Math.cos(b - a));

function prefersReducedMotion() {
  if (typeof window === 'undefined') return false;
  const pbp = window.PBP;
  if (pbp && (pbp.reducedMotion || (pbp.settings && pbp.settings.reducedMotion))) return true;
  try { return !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

/**
 * @param camera     the PerspectiveCamera the rig drives
 * @param canvas     where the pointer and wheel events come from
 * @param isDragging optional () => boolean; a touch that picked a man up is
 *                   never an orbit. drag.js is built after the scene, so this
 *                   can also be handed over later with setDragGuard.
 */
export function createCameraRig({ camera, canvas, isDragging = null }) {
  const T = CAM_TUNING;
  let dragGuard = isDragging;

  let theta = 0;
  let phi = PRESETS.normal.phi;
  let radius = PRESETS.normal.radius;
  const target = new THREE.Vector3(0, 0.10, 0);

  let preset = 'normal';        // which button is lit, null once the user orbits
  let free = false;             // the player has taken the camera
  let moverSide = 'w';          // which way `normal` and `opponent` face
  let tween = null;             // { t, dur, dolly, from:{...}, to:{...} }
  let velTheta = 0;
  let velPhi = 0;

  const listeners = new Set();
  const notify = () => { for (const fn of listeners) { try { fn(state()); } catch { /* a listener is not the rig's problem */ } } };
  const state = () => ({ preset, free, theta, phi, radius, fit: fitScaleFor(camera.aspect, camera.fov), fov: camera.fov });

  const sideAngle = (side) => (side === 'b' ? Math.PI : 0);

  // --- the tween ------------------------------------------------------------
  function goTo(to, { dolly = false, instant = false } = {}) {
    const snap = instant || prefersReducedMotion();
    const want = {
      theta: to.theta != null ? shortWay(theta, to.theta) : theta,
      phi: to.phi != null ? clamp(to.phi, T.phiMin, T.phiMax) : phi,
      radius: to.radius != null ? clamp(to.radius, T.radiusMin, T.radiusMax) : radius,
      target: to.target ? to.target.clone() : target.clone(),
    };
    velTheta = 0; velPhi = 0;
    if (snap) {
      theta = want.theta; phi = want.phi; radius = want.radius; target.copy(want.target);
      tween = null;
      return;
    }
    tween = {
      t: 0, dur: T.tweenSec, dolly,
      from: { theta, phi, radius, target: target.clone() },
      to: want,
    };
  }

  /** Jump to one of the four views. Unknown names are ignored. */
  function goPreset(name, instant = false) {
    const p = PRESETS[name];
    if (!p) return;
    let want = { phi: p.phi, radius: p.radius, target: new THREE.Vector3(...p.target) };
    if (name === 'normal') want.theta = sideAngle(moverSide);
    else if (name === 'opponent') want.theta = sideAngle(moverSide) + Math.PI;
    else if (name === 'side') {
      // The a-file edge or the h-file edge, whichever is the shorter pan.
      const a = shortWay(theta, -Math.PI / 2);
      const h = shortWay(theta, Math.PI / 2);
      want.theta = Math.abs(a - theta) <= Math.abs(h - theta) ? a : h;
    }
    // `top` keeps the azimuth it is given, so the board does not spin under a
    // straight-down view for no reason.
    preset = name;
    free = false;
    goTo(want, { instant });
    notify();
  }

  /** The automatic swing behind whoever is to move. */
  function setSide(side, instant = false) {
    moverSide = side === 'b' ? 'b' : 'w';
    if (free || preset !== 'normal') return;   // the player is in charge, or a
    const to = sideAngle(moverSide);           // fixed view is on
    if (!instant && Math.abs(shortWay(theta, to) - theta) < 1e-4 && !tween) return;
    goTo({ theta: to }, { dolly: true, instant });
  }

  // --- input ----------------------------------------------------------------
  const pointers = new Map();   // live pointer id -> { x, y, type }
  let mode = null;              // 'orbit' | 'pan' | 'pinch' | null
  let candidate = null;         // a touch that may become an orbit
  let lastPinch = 0;

  function takeCamera() {
    if (tween) { tween = null; }
    if (!free || preset) { free = true; preset = null; notify(); }
  }

  function orbitBy(dx, dy, dt) {
    theta -= dx * T.orbitSpeed;
    phi = clamp(phi + dy * T.orbitSpeed, T.phiMin, T.phiMax);
    if (dt > 0) {
      velTheta = -dx * T.orbitSpeed / dt;
      velPhi = dy * T.orbitSpeed / dt;
    }
  }

  const right = new THREE.Vector3();
  const fwd = new THREE.Vector3();
  function panBy(dx, dy) {
    // Pan in the board plane: the camera's right, and the flat direction it is
    // looking, so dragging down pulls the far side of the board towards you.
    fwd.set(-Math.sin(theta), 0, -Math.cos(theta));
    right.set(fwd.z, 0, -fwd.x);
    const k = T.panSpeed * radius;
    target.x += (-dx * right.x - dy * fwd.x) * k;
    target.z += (-dx * right.z - dy * fwd.z) * k;
    target.x = clamp(target.x, -T.panLimit, T.panLimit);
    target.z = clamp(target.z, -T.panLimit, T.panLimit);
  }

  function zoomBy(factor) {
    radius = clamp(radius * factor, T.radiusMin, T.radiusMax);
    takeCamera();
  }

  function onDown(ev) {
    if (ev.pointerType === 'touch') {
      pointers.set(ev.pointerId, { x: ev.clientX, y: ev.clientY });
      if (pointers.size === 2) {
        mode = 'pinch';
        candidate = null;
        lastPinch = pinchSpan();
        takeCamera();
      } else if (pointers.size === 1) {
        // Decided on the first move instead: drag.js gets the same pointerdown
        // and has not had its turn yet.
        candidate = { x: ev.clientX, y: ev.clientY, id: ev.pointerId };
      }
      return;
    }
    if (ev.button !== 1 && ev.button !== 2) return;   // left belongs to drag.js
    pointers.set(ev.pointerId, { x: ev.clientX, y: ev.clientY });
    mode = ev.shiftKey ? 'pan' : 'orbit';
    if (mode === 'orbit') takeCamera();
    canvas.setPointerCapture?.(ev.pointerId);
    ev.preventDefault();
  }

  function pinchSpan() {
    const [a, b] = [...pointers.values()];
    return Math.hypot(a.x - b.x, a.y - b.y);
  }

  let lastMove = 0;
  function onMove(ev) {
    if (candidate && ev.pointerId === candidate.id) {
      if (dragGuard && dragGuard()) { candidate = null; return; }   // a man is up
      mode = 'orbit';
      pointers.set(ev.pointerId, { x: candidate.x, y: candidate.y });
      candidate = null;
      takeCamera();
    }
    const prev = pointers.get(ev.pointerId);
    if (!prev || !mode) return;
    const dx = ev.clientX - prev.x;
    const dy = ev.clientY - prev.y;
    prev.x = ev.clientX; prev.y = ev.clientY;
    const now = performance.now();
    const dt = lastMove ? Math.min(0.1, (now - lastMove) / 1000) : 0;
    lastMove = now;
    if (mode === 'pinch') {
      if (pointers.size < 2) return;
      const span = pinchSpan();
      if (lastPinch > 0 && span > 0) radius = clamp(radius * (lastPinch / span), T.radiusMin, T.radiusMax);
      lastPinch = span;
      panBy(dx / 2, dy / 2);
    } else if (mode === 'pan') {
      panBy(dx, dy);
    } else {
      orbitBy(dx, dy, dt);
    }
    ev.preventDefault();
  }

  function onUp(ev) {
    pointers.delete(ev.pointerId);
    if (candidate && ev.pointerId === candidate.id) candidate = null;
    if (pointers.size === 0) {
      mode = null;
      // A release after a pause is a stop, not a flick, and reduced motion
      // never coasts at all.
      if (performance.now() - lastMove > 120 || prefersReducedMotion()) { velTheta = 0; velPhi = 0; }
      lastMove = 0;
    }
    else if (mode === 'pinch') { mode = 'orbit'; lastPinch = 0; }
    canvas.releasePointerCapture?.(ev.pointerId);
  }

  function onWheel(ev) {
    zoomBy(ev.deltaY > 0 ? T.zoomStep : 1 / T.zoomStep);
    ev.preventDefault();
  }

  const onMenu = (ev) => ev.preventDefault();   // right-drag is an orbit here

  canvas.addEventListener('pointerdown', onDown);
  canvas.addEventListener('pointermove', onMove);
  canvas.addEventListener('pointerup', onUp);
  canvas.addEventListener('pointercancel', onUp);
  canvas.addEventListener('wheel', onWheel, { passive: false });
  canvas.addEventListener('contextmenu', onMenu);

  // --- the frame ------------------------------------------------------------
  const lookAt = new THREE.Vector3();

  function update(dt, sway = 0, clock = 0) {
    let dolly = 1;
    if (tween) {
      tween.t = Math.min(1, tween.t + dt / tween.dur);
      const e = easeInOut(tween.t);
      const f = tween.from;
      const t = tween.to;
      theta = f.theta + (t.theta - f.theta) * e;
      phi = f.phi + (t.phi - f.phi) * e;
      radius = f.radius + (t.radius - f.radius) * e;
      target.lerpVectors(f.target, t.target, e);
      if (tween.dolly) dolly = 1 + T.dolly * Math.sin(Math.PI * e);
      if (tween.t >= 1) tween = null;
    } else if (!mode && (velTheta || velPhi)) {
      const decay = Math.exp(-T.inertiaDamping * dt);
      theta += velTheta * dt;
      phi = clamp(phi + velPhi * dt, T.phiMin, T.phiMax);
      velTheta *= decay; velPhi *= decay;
      if (Math.abs(velTheta) < T.inertiaFloor && Math.abs(velPhi) < T.inertiaFloor) { velTheta = 0; velPhi = 0; }
    }

    // On a narrow screen every preset stands further back, so the whole
    // board is in the picture; on the wide window this was tuned on, fit is 1.
    const fit = fitScaleFor(camera.aspect, camera.fov);
    const r = radius * dolly * fit;
    const sinPhi = Math.sin(phi);
    camera.position.set(
      target.x + Math.sin(theta) * sinPhi * r + Math.sin(clock * 0.53) * T.swayPos[0] * sway,
      target.y + Math.cos(phi) * r + Math.sin(clock * 0.37 + 1.1) * T.swayPos[1] * sway,
      target.z + Math.cos(theta) * sinPhi * r,
    );
    lookAt.copy(target);
    lookAt.x += Math.sin(clock * 0.29) * T.swayLook * sway;
    camera.lookAt(lookAt);
    if (sway > 0) camera.rotateZ(Math.sin(clock * 0.23) * T.swayRoll * sway);
  }

  function dispose() {
    canvas.removeEventListener('pointerdown', onDown);
    canvas.removeEventListener('pointermove', onMove);
    canvas.removeEventListener('pointerup', onUp);
    canvas.removeEventListener('pointercancel', onUp);
    canvas.removeEventListener('wheel', onWheel);
    canvas.removeEventListener('contextmenu', onMenu);
    listeners.clear();
  }

  const api = {
    update, dispose, setSide,
    preset: goPreset,
    /** Straight-line access for the harness and for anything scripted. */
    set(next = {}, instant = true) { takeCamera(); goTo({ ...next, target: next.target ? new THREE.Vector3(...next.target) : null }, { instant }); },
    zoomBy,
    state,
    /** Elevation of the camera above the board plane, in degrees. */
    elevation() { return (Math.PI / 2 - phi) / DEG; },
    setDragGuard(fn) { dragGuard = fn; },
    onChange(fn) { listeners.add(fn); fn(state()); return () => listeners.delete(fn); },
    /** Light up the button row and the number keys. One call, from boot.js. */
    attachUi(root) { return attachCameraUi({ rig: api, root }); },
  };
  return api;
}

// --- the button row ---------------------------------------------------------
// Markup lives in index.html (#cam-views) so the page still reads as a page.
// Keys work whenever a text field does not have the focus; Escape is left
// alone, it belongs to whoever else wants it.

const KEYS = { Digit1: 'normal', Digit2: 'top', Digit3: 'side', Digit4: 'opponent' };

export function attachCameraUi({ rig, root = document.getElementById('cam-views') }) {
  if (!root) return () => {};
  const buttons = [...root.querySelectorAll('[data-view]')];
  const chip = root.querySelector('.cam-free');

  const onClick = (ev) => {
    const btn = ev.target.closest('[data-view]');
    if (btn) rig.preset(btn.dataset.view);
  };
  root.addEventListener('click', onClick);

  function typing() {
    const el = document.activeElement;
    if (!el) return false;
    const tag = el.tagName;
    return el.isContentEditable || tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
  }
  function onKey(ev) {
    if (ev.ctrlKey || ev.altKey || ev.metaKey || typing()) return;
    const name = KEYS[ev.code];
    if (!name) return;
    rig.preset(name);
    ev.preventDefault();
  }
  window.addEventListener('keydown', onKey);

  const off = rig.onChange((s) => {
    for (const b of buttons) b.classList.toggle('on', b.dataset.view === s.preset);
    if (chip) chip.hidden = !s.free;
  });

  return () => {
    root.removeEventListener('click', onClick);
    window.removeEventListener('keydown', onKey);
    off();
  };
}
