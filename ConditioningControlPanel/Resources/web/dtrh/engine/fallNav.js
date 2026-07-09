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
const LANE_MAX = 2.0;        // junction steering: max lateral strafe off the spine (u) - tube RADIUS is 5.5
const LANE_LERP = 2.2;       // how fast the camera glides toward the target lane
const LANE_LEAN = 0.85;      // pre-commit lean fraction while a junction is armed (a felt bias, not a full swerve)
const INTRO_TIME = 7;        // seconds of the opening plunge
const INTRO_SPEED = 17;      // the plunge starts this fast, easing to the game speed
const FOV_PULSE_DECAY = 2.5; // 1/s - effect kicks bleed off
const SCROLL_BOOST = 3.2;    // speed added per firm wheel notch (device-normalized)

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const easeOutCubic = (t) => 1 - Math.pow(1 - t, 3);
const easeInOutCubic = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);

// Branch dive ("vein" rail): when a junction commits, the camera leaves the
// treadmill and rides a diverging vein's ride-curve. Ported from the Explore
// rabbit-hole navigation.js dive: a scalar `vt` walks the curve while the pose
// eases (position lerp + quaternion slerp) from the pre-dive snapshot.
const VEIN_AHEAD = 0.03;     // look-ahead fraction of the vein for aim (POV re-aims down it)
const DIVE_BLEND_IN = 0.85;  // seconds to ease into the corridor
const REBASE_BLEND = 0.6;    // seconds to settle back onto the fresh loop after handoff
const VEIN_BUILD_AT = 0.45;  // vt at which the scene builds the fresh loop COAXIALLY ahead
                             // (still fog-hidden) so the corridor telescopes into it - no void
const VEIN_END_AT = 0.985;   // vt at which the camera hands off onto that already-built loop

export function createFallNav({ camera, canvas, layout, getTargetSpeed, onFirstInteract,
  onScrollBoost, isSpotlightActive, onSkipSpotlight, onVeinEnd }) {
  let depth = 0;
  let speed = INTRO_SPEED;
  let trim = 1;               // player comfort factor, 0.5x-1.5x
  let introT = 0;             // 0..1 over INTRO_TIME
  let paused = false;
  let interacted = false;
  let fovPulse = 0;           // transient FOV kick from popped effects

  // Wave 2 game verbs: camera roll (a static dutch tilt per accepted sin, a
  // continuous spin for the Spun curse) and a speed-cap multiplier (Freefall).
  let rollRate = 0;           // deg/s continuous roll
  let rollSpin = 0;           // accumulated spin angle (deg)
  let rollOffsetT = 0, rollOffset = 0; // eased static tilt (deg)
  let capMult = 1;            // multiplies MAX_SPEED

  let lookYaw = 0, lookPitch = 0, targetYaw = 0, targetPitch = 0;
  // junction steering: a persistent lateral strafe off the spine. `laneNow` eases
  // toward `laneTarget` (set by the junction manager); `laneVote` is the player's
  // live -1..1 lean, sampled at the fork commit. Only steers while `junctionArmed`.
  let laneNow = 0, laneTarget = 0, laneVote = 0, junctionArmed = false;
  // branch detour: while set, `branchFn(depth) -> lateral meters` OVERRIDES the
  // lane lean and steers the camera down a diverging arm. Sampling it at `depth`
  // (position) and `depth + look-ahead` (aim) with the arm's OWN binormal at each
  // makes the forward vector tilt - the camera actually yaws into the turn.
  let branchFn = null;
  let branchRoll = 0;         // eased bank (deg) leaning into a branch turn

  // vein dive rail (see constants above). `track` is 'fall' on the treadmill,
  // 'vein' while riding a committed branch's ride-curve.
  let track = 'fall';
  let veinCurve = null, veinLen = 100, vt = 0, veinEndFired = false, veinBuilt = false;
  // junction hold: while a fork is open the fall PARKS at the fork depth (the
  // camera glides to a stop between the two mouths) instead of drifting past it
  // out of bounds. The junction manager sets it on linger, clears it on commit.
  let forwardHold = false, holdDepth = 0;
  let veinEndCb = onVeinEnd || null;
  // dive pose blend: ease from the snapshot (pre-dive) into each frame's target
  let blendT = 0, blendDur = 0;
  const _blendPos = new THREE.Vector3(), _blendQuat = new THREE.Quaternion(), _tgtQuat = new THREE.Quaternion();
  function startBlend(dur) { _blendPos.copy(camera.position); _blendQuat.copy(camera.quaternion); blendT = 0; blendDur = dur; }
  let dragging = false, lastX = 0, lastY = 0, dragType = 'mouse';
  let lookSuppressed = false; // held while a card paddle is grabbed: the mouse steers the card, not the look
  let swipeDY = 0, swipeSkipped = false; // per-touch-gesture: skip a spotlight on a decisive vertical swipe

  const baseFov = camera.fov;
  const UP = new THREE.Vector3(0, 1, 0);
  const _pos = new THREE.Vector3(), _ahead = new THREE.Vector3();
  const _fwd = new THREE.Vector3(), _right = new THREE.Vector3();
  const _up = new THREE.Vector3(), _look = new THREE.Vector3();

  // "face a wall point": while set, the aim eases off the tube-forward look onto
  // an external world target (a held wall poster) and back on release. faceW is
  // the 0..1 eased weight; _faceTgt is an internal copy so we never alias the
  // caller's reused scratch vector.
  let _faceTarget = null, _faceWant = false, faceW = 0;
  const FACE_LERP = 4.0;
  const _faceTgt = new THREE.Vector3();

  function firstInteract() {
    if (!interacted) { interacted = true; onFirstInteract && onFirstInteract(); }
  }

  // ---- input: scroll throttles the fall, drag to look -----------------------
  function onWheel(e) {
    if (paused) return;
    // wheel over UI chrome (options panel, overlays, the Warren/dollhouse) scrolls
    // the UI, not the fall - otherwise preventDefault() below eats the scroll
    if (e.target instanceof Element && e.target.closest('.sf-panel, .sf-results, .sf-pause, .wr-root')) return;
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
    } else if (junctionArmed && (e.key === 'ArrowLeft' || e.key === 'a' || e.key === 'A')) {
      laneVote = clamp(laneVote - 0.5, -1, 1); firstInteract(); e.preventDefault();
    } else if (junctionArmed && (e.key === 'ArrowRight' || e.key === 'd' || e.key === 'D')) {
      laneVote = clamp(laneVote + 0.5, -1, 1); firstInteract(); e.preventDefault();
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
    // holding a card paddle: the mouse pushes the card through the space, so the
    // drag must NOT also yaw the camera (the spawner reads the raw cursor itself)
    if (lookSuppressed) { firstInteract(); return; }
    if (junctionArmed) {
      // a fork is open: horizontal drag/swipe leans you toward a mouth (not a look)
      laneVote = clamp(laneVote + dx * 0.006, -1, 1);
      firstInteract();
      return;
    }
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
    let desired = clamp((getTargetSpeed ? getTargetSpeed() : 6) * trim, MIN_SPEED, MAX_SPEED * capMult);
    if (introT < 1) desired = INTRO_SPEED + (desired - INTRO_SPEED) * introE;
    speed += (desired - speed) * clamp(dt * ACCEL_LERP, 0, 1);
    speed = clamp(speed, MIN_SPEED, MAX_SPEED * capMult);
    if (blendDur > 0) blendT += dt;

    // look easing
    lookYaw += (targetYaw - lookYaw) * clamp(dt * LOOK_LERP, 0, 1);
    lookPitch += (targetPitch - lookPitch) * clamp(dt * LOOK_LERP, 0, 1);

    // face-a-wall-point easing: ease toward 1 while a poster is held, back to 0
    // on release. Keep _faceTarget alive through the ease-OUT so the camera pans
    // back the same way it came (not a snap); drop it once the weight settles.
    faceW += ((_faceWant ? 1 : 0) - faceW) * clamp(dt * FACE_LERP, 0, 1);
    if (!_faceWant && faceW < 0.001) { faceW = 0; _faceTarget = null; }

    fovPulse *= Math.exp(-FOV_PULSE_DECAY * dt);

    // camera roll: spin while a rate is set; unwind to upright the short way
    // once it clears (so a run ending never snaps the horizon back)
    if (rollRate) {
      rollSpin = (rollSpin + rollRate * dt) % 360;
    } else if (rollSpin) {
      let s = ((rollSpin % 360) + 540) % 360 - 180; // -180..180
      s *= Math.exp(-1.8 * dt);
      rollSpin = Math.abs(s) < 0.05 ? 0 : s;
    }
    rollOffset += (rollOffsetT - rollOffset) * clamp(dt * 1.5, 0, 1);

    // vein dive: ride the committed branch's ride-curve instead of the treadmill.
    // vt advances by the fall speed (the fall carries you down the vein, so the
    // player trim still applies). At the tail the vein hands off to a freshly
    // rebased loop via onVeinEnd -> scene rebuilds the tunnel -> rebaseTo().
    if (track === 'vein' && veinCurve) {
      vt = clamp(vt + (speed * dt) / Math.max(1, veinLen), 0, 1);
      veinCurve.getPoint(vt, _pos);
      veinCurve.getPoint(Math.min(1, vt + VEIN_AHEAD), _ahead);
      orientAndPlace(introE);
      // stage 1 (mid-ride): tell the scene to build the fresh loop coaxially at the
      // vein exit. It's ~half a vein ahead, still buried in fog, so the corridor now
      // telescopes into a real tube instead of ending in a black void.
      if (!veinBuilt && vt >= VEIN_BUILD_AT) {
        veinBuilt = true;
        if (veinEndCb) { try { veinEndCb(); } catch (e) { /* ignore */ } }
      }
      // stage 2 (tail): hand the camera off onto that already-built coaxial loop at
      // depth 0 (== the vein exit frame). The blend hides any hairline mismatch.
      if (!veinEndFired && vt >= VEIN_END_AT) {
        veinEndFired = true;
        track = 'fall';
        depth = 0;
        veinCurve = null;
        startBlend(REBASE_BLEND);
      }
      return;
    }

    // junction hold: park at the fork instead of advancing past it. Ease the
    // remaining creep to a stop so the camera settles between the two mouths.
    if (forwardHold) {
      depth += (holdDepth - depth) * clamp(dt * 3.0, 0, 1);
    } else {
      depth += speed * dt;
    }

    // place the camera on the loop, looking a little further along it
    const t = depth / layout.loopDepth;
    layout.pointAt(t, _pos);
    layout.pointAt(t + AHEAD, _ahead);

    // lateral placement. TWO regimes:
    //  - branch detour (branchFn set): the camera follows a diverging arm. Sample
    //    the arm offset at `depth` for position and at `depth + look-ahead` for the
    //    aim, each with THAT depth's own binormal - the mismatch tilts forward, so
    //    the POV yaws down the arm (a real turn, not a slide).
    //  - armed lean (junction telegraph): pre-commit bias toward the player's vote,
    //    strafing pos + aim by the SAME vector so forward stays parallel (no yaw).
    const aheadUnits = AHEAD * layout.loopDepth;
    if (branchFn) {
      const offHere = branchFn(depth);
      const offAhead = branchFn(depth + aheadUnits);
      const frH = layout.frameAtDepth(depth);
      _pos.addScaledVector(frH.binormal, offHere);
      const frA = layout.frameAtDepth(depth + aheadUnits);
      _ahead.addScaledVector(frA.binormal, offAhead);
      laneNow = offHere; laneTarget = 0;            // hand back smoothly when it clears
      // bank into the turn: lean proportional to how hard the aim diverges from pos
      const targetRoll = clamp(-(offAhead - offHere) * 3.5, -8, 8);
      branchRoll += (targetRoll - branchRoll) * clamp(dt * 4.0, 0, 1);
    } else {
      if (junctionArmed) laneTarget = laneVote * LANE_MAX * LANE_LEAN;
      laneNow += (laneTarget - laneNow) * clamp(dt * LANE_LERP, 0, 1);
      if (Math.abs(laneNow) > 1e-4) {
        const fr = layout.frameAtDepth(depth);
        _pos.addScaledVector(fr.binormal, laneNow);
        _ahead.addScaledVector(fr.binormal, laneNow);
      }
      if (branchRoll) branchRoll *= Math.exp(-4.0 * dt);
    }
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
    // swing the aim onto a held wall poster (any tube angle) and back on release
    if (faceW > 0.001 && _faceTarget) _look.lerp(_faceTarget, easeInOutCubic(faceW));
    camera.lookAt(_look);

    // dutch roll about the view axis (The Tilt / Spun / a bank into a branch)
    const roll = (rollOffset + rollSpin + branchRoll) * (Math.PI / 180);
    if (roll) camera.rotateZ(roll);

    const fov = baseFov + (1 - introE) * 8 + fovPulse; // wide during the plunge + effect kicks
    if (camera.fov !== fov) { camera.fov = fov; camera.updateProjectionMatrix(); }

    // dive blend: ease the pose from the pre-dive snapshot into this frame's
    // freshly-computed target (position lerp + quaternion slerp). The slerp is
    // what re-aims the camera down the new corridor without a one-frame snap.
    if (blendDur > 0) {
      const k = easeInOutCubic(clamp(blendT / blendDur, 0, 1));
      _tgtQuat.copy(camera.quaternion);
      camera.position.lerpVectors(_blendPos, _pos, k);
      camera.quaternion.slerpQuaternions(_blendQuat, _tgtQuat, k);
      if (blendT >= blendDur) blendDur = 0;
    }
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
    // Wave 2 game verbs
    setRollRate(degPerSec) { rollRate = degPerSec || 0; },
    setRollOffset(deg) { rollOffsetT = deg || 0; },
    setSpeedCapMult(f) { capMult = clamp(f || 1, 0.25, 3); },
    // while a card paddle is held, freeze the look at center so the mouse only
    // moves the card (called every frame from scene.js off spawner.isGrabbing())
    setLookSuppressed(v) { lookSuppressed = !!v; if (v) { targetYaw = 0; targetPitch = 0; } },
    // aim the camera at a world point (a held wall poster) and ease back to the
    // tube-forward look when passed null. Copies into an internal scratch; on
    // release we keep the last target through the ease-out so the pan reverses
    // smoothly (the drop happens in update once faceW settles at 0).
    setFaceTarget(v) {
      if (v) { _faceTgt.copy(v); _faceTarget = _faceTgt; _faceWant = true; }
      else { _faceWant = false; }
    },
    // ---- junction steering (drives the lateral lane; the fork manager owns it) --
    setLane(x) { laneTarget = clamp(x || 0, -LANE_MAX * 1.2, LANE_MAX * 1.2); },
    getLane: () => laneNow,
    getLaneVote: () => laneVote,
    resetLaneVote() { laneVote = 0; },
    setJunctionArmed(v) { junctionArmed = !!v; },
    // park the fall at `atDepth` (glide to a stop at the fork) until released.
    setForwardHold(v, atDepth) { forwardHold = !!v; if (v && atDepth != null) holdDepth = atDepth; },
    // active branch detour: fn(depth) -> lateral meters off the spine, or null to
    // release the camera back onto the rail. The junction manager owns this.
    setBranchProfile(fn) { branchFn = (typeof fn === 'function') ? fn : null; },
    // ---- vein dive rail (junction commit rides a diverging branch) -----------
    // Ride `rideCurve` (world-space CatmullRom, seeded at the spine at the fork).
    // opts.length = the vein's world length (scales how fast vt advances).
    enterVein(rideCurve, opts = {}) {
      if (!rideCurve) return;
      track = 'vein';
      veinCurve = rideCurve;
      veinLen = opts.length || 100;
      vt = 0; veinEndFired = false; veinBuilt = false;
      forwardHold = false;                                    // release the fork park; the vein carries us now
      targetYaw = targetPitch = lookYaw = lookPitch = 0;      // recenter the look for the dive
      laneNow = laneTarget = laneVote = 0; junctionArmed = false; branchFn = null;
      startBlend(DIVE_BLEND_IN);
      firstInteract();
    },
    // Hand the camera back onto a freshly rebased loop at world depth `depth0`
    // (the scene rebuilds tunnel geometry so this depth == the vein exit frame).
    rebaseTo(depth0) {
      depth = depth0 || 0;
      track = 'fall';
      veinCurve = null; vt = 0; veinEndFired = false;
      startBlend(REBASE_BLEND);
    },
    setOnVeinEnd(fn) { veinEndCb = fn || null; },
    isInVein: () => track === 'vein',
    getVeinT: () => vt,
    reset() {
      depth = 0; speed = INTRO_SPEED; trim = 1; introT = 0; fovPulse = 0;
      lookYaw = lookPitch = targetYaw = targetPitch = 0;
      rollRate = 0; rollSpin = 0; rollOffsetT = rollOffset = 0; capMult = 1;
      laneNow = laneTarget = laneVote = 0; junctionArmed = false;
      branchFn = null; branchRoll = 0;
      track = 'fall'; veinCurve = null; vt = 0; veinEndFired = false; veinBuilt = false;
      forwardHold = false;
      blendDur = 0; blendT = 0;
      _faceTarget = null; _faceWant = false; faceW = 0;
    },
    dispose,
  };
}
