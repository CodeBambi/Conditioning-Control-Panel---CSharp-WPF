/* ============================================================================
 * fallNav.js - velocity-model camera rail for the endless fall.
 *
 * Unlike the Explore nav (target-lerp scroll positioning), the fall moves BY
 * ITSELF: `depth` grows every frame at a speed the game director sets, and the
 * player only TRIMS it (wheel / arrows / vertical swipe scale a comfort factor
 * 0.5x-1.5x). Mouse / horizontal-touch drag re-aims the look, easing back to
 * center on release - the camera never leaves the rail (anti motion-sickness).
 *
 * orientAndPlace + the look-clamp constants are lifted from
 * js/rabbit-hole/navigation.js:422-450.
 * ==========================================================================*/

import * as THREE from 'three';

const MIN_SPEED = 0.15;      // floor: near-still lingering is possible
const MAX_SPEED = 30;        // hard cap (u/s)
const ACCEL_LERP = 2.5;      // how fast actual speed chases the target
const TRIM_MIN = 0.5, TRIM_MAX = 1.5;
const LOOK_LERP = 4.0;
const LOOK_MAX_YAW = 0.42;   // ~24 deg
const LOOK_MAX_PITCH = 0.34;
const AHEAD = 0.0104;        // look-ahead fraction of the loop (~10 world units)
const INTRO_TIME = 7;        // seconds of the opening plunge
const INTRO_SPEED = 22;      // the plunge starts this fast, easing to the game speed
const FOV_PULSE_DECAY = 2.5; // 1/s - effect kicks bleed off
const SCROLL_BOOST = 3.2;    // speed added per firm wheel notch (device-normalized)

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const easeOutCubic = (t) => 1 - Math.pow(1 - t, 3);

export function createFallNav({ camera, canvas, layout, getTargetSpeed, onFirstInteract,
  onScrollBoost, isSpotlightActive, onSkipSpotlight }) {
  let depth = 0;
  let speed = INTRO_SPEED;
  let trim = 1;               // player comfort factor, 0.5x-1.5x
  let introT = 0;             // 0..1 over INTRO_TIME
  let paused = false;
  let interacted = false;
  let fovPulse = 0;           // transient FOV kick from popped effects

  let lookYaw = 0, lookPitch = 0, targetYaw = 0, targetPitch = 0;
  let dragging = false, lastX = 0, lastY = 0, dragType = 'mouse';
  let swipeDY = 0, swipeSkipped = false; // per-touch-gesture: skip a spotlight on a decisive vertical swipe

  const baseFov = camera.fov;
  const UP = new THREE.Vector3(0, 1, 0);
  const _pos = new THREE.Vector3(), _ahead = new THREE.Vector3();
  const _fwd = new THREE.Vector3(), _right = new THREE.Vector3();
  const _up = new THREE.Vector3(), _look = new THREE.Vector3();

  function firstInteract() {
    if (!interacted) { interacted = true; onFirstInteract && onFirstInteract(); }
  }

  // ---- input: scroll throttles the fall, drag to look -----------------------
  function onWheel(e) {
    if (paused) return;
    // wheel over UI chrome (options panel, overlays) scrolls the UI, not the fall
    if (e.target instanceof Element && e.target.closest('.sf-panel, .sf-results, .sf-pause')) return;
    e.preventDefault();
    firstInteract();
    // a downward flick skips the current spotlight video
    if (e.deltaY > 0 && isSpotlightActive && isSpotlightActive()) {
      if (onSkipSpotlight) onSkipSpotlight();
      return;
    }
    // scroll accelerates the tube up to the cap (a capped, decaying boost, same
    // as a bubble hit); wheel-up eases off. deltaY magnitude varies wildly by
    // device (mouse ~100/notch, trackpad single digits), so normalize to a firm
    // impulse by sign and clamp the fractional magnitude before scaling.
    if (onScrollBoost) {
      const mag = clamp(Math.abs(e.deltaY) / 100, 0.5, 1.4);
      onScrollBoost(Math.sign(e.deltaY) * mag * SCROLL_BOOST);
    }
  }
  function onKey(e) {
    if (paused) return;
    if (e.target instanceof Element && e.target.closest('input, textarea, .sf-panel')) return;
    if (e.key === 'ArrowDown' || e.key === 'PageDown' || e.key === ' ') {
      trim = clamp(trim * 1.15, TRIM_MIN, TRIM_MAX); firstInteract(); e.preventDefault();
    } else if (e.key === 'ArrowUp' || e.key === 'PageUp') {
      trim = clamp(trim / 1.15, TRIM_MIN, TRIM_MAX); firstInteract(); e.preventDefault();
    } else if (e.key === '0') {
      trim = 1; firstInteract();
    }
  }
  function onPointerDown(e) {
    if (paused) return;
    dragging = true;
    dragType = e.pointerType || 'mouse';
    lastX = e.clientX; lastY = e.clientY;
    swipeDY = 0; swipeSkipped = false;
    canvas.setPointerCapture && canvas.setPointerCapture(e.pointerId);
  }
  function onPointerMove(e) {
    if (!dragging || paused) return;
    const dx = e.clientX - lastX, dy = e.clientY - lastY;
    lastX = e.clientX; lastY = e.clientY;
    if (dragType === 'touch') {
      // during a video stage there is no wheel to skip with - a decisive
      // vertical swipe cuts the clip instead of trimming speed
      if (isSpotlightActive && isSpotlightActive()) {
        swipeDY += dy;
        if (!swipeSkipped && Math.abs(swipeDY) > 44) {
          swipeSkipped = true;
          if (onSkipSpotlight) onSkipSpotlight();
        }
        firstInteract();
        return;
      }
      // touch: vertical drag trims speed (swipe up = faster), horizontal = look
      trim = clamp(trim * Math.exp(-dy * 0.002), TRIM_MIN, TRIM_MAX);
      targetYaw = clamp(targetYaw - dx * 0.002, -LOOK_MAX_YAW, LOOK_MAX_YAW);
    } else {
      targetYaw = clamp(targetYaw - dx * 0.0016, -LOOK_MAX_YAW, LOOK_MAX_YAW);
      targetPitch = clamp(targetPitch - dy * 0.0016, -LOOK_MAX_PITCH, LOOK_MAX_PITCH);
    }
    firstInteract();
  }
  function onPointerUp() {
    dragging = false;
    targetYaw = 0; targetPitch = 0; // ease look back to forward
  }

  window.addEventListener('wheel', onWheel, { passive: false });
  window.addEventListener('keydown', onKey);
  canvas.addEventListener('pointerdown', onPointerDown);
  window.addEventListener('pointermove', onPointerMove);
  window.addEventListener('pointerup', onPointerUp);

  function update(dt) {
    if (paused) return;

    let introE = 1;
    if (introT < 1) {
      introT = clamp(introT + dt / INTRO_TIME, 0, 1);
      introE = easeOutCubic(introT);
    }

    // desired speed: the director's target scaled by the player trim; during
    // the intro, blend from the plunge speed down into the game speed.
    let desired = clamp((getTargetSpeed ? getTargetSpeed() : 6) * trim, MIN_SPEED, MAX_SPEED);
    if (introT < 1) desired = INTRO_SPEED + (desired - INTRO_SPEED) * introE;
    speed += (desired - speed) * clamp(dt * ACCEL_LERP, 0, 1);
    speed = clamp(speed, MIN_SPEED, MAX_SPEED);
    depth += speed * dt;

    // look easing
    lookYaw += (targetYaw - lookYaw) * clamp(dt * LOOK_LERP, 0, 1);
    lookPitch += (targetPitch - lookPitch) * clamp(dt * LOOK_LERP, 0, 1);

    fovPulse *= Math.exp(-FOV_PULSE_DECAY * dt);

    // place the camera on the loop, looking a little further along it
    const t = depth / layout.loopDepth;
    layout.pointAt(t, _pos);
    layout.pointAt(t + AHEAD, _ahead);
    orientAndPlace(introE);
  }

  // Copied from navigation.js orientAndPlace (vein blend removed).
  function orientAndPlace(introE) {
    camera.position.copy(_pos);

    _fwd.subVectors(_ahead, _pos);
    if (_fwd.lengthSq() < 1e-8) _fwd.set(0, 0, -1);
    _fwd.normalize();
    _right.crossVectors(_fwd, UP);
    if (_right.lengthSq() < 1e-8) _right.set(1, 0, 0);
    _right.normalize();
    _up.crossVectors(_right, _fwd).normalize();

    _look.copy(_ahead)
      .addScaledVector(_right, Math.sin(lookYaw) * 6)
      .addScaledVector(_up, Math.sin(lookPitch) * 6);
    camera.lookAt(_look);

    const fov = baseFov + (1 - introE) * 8 + fovPulse; // wide during the plunge + effect kicks
    if (camera.fov !== fov) { camera.fov = fov; camera.updateProjectionMatrix(); }
  }

  function dispose() {
    window.removeEventListener('wheel', onWheel);
    window.removeEventListener('keydown', onKey);
    canvas.removeEventListener('pointerdown', onPointerDown);
    window.removeEventListener('pointermove', onPointerMove);
    window.removeEventListener('pointerup', onPointerUp);
  }

  return {
    update,
    getDepth: () => depth,
    getSpeed: () => speed,
    getTrim: () => trim,
    fovKick(amount) { fovPulse = Math.min(8, fovPulse + amount); },
    setPaused(p) { paused = !!p; if (p) dragging = false; },
    reset() {
      depth = 0; speed = INTRO_SPEED; trim = 1; introT = 0; fovPulse = 0;
      lookYaw = lookPitch = targetYaw = targetPitch = 0;
    },
    dispose,
  };
}
