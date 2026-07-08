/* ============================================================================
 * junctions.js - branching paths on the tube ("The Junction").
 *
 * The tunnel is a single closed-loop treadmill (tunnel.js), so we can't fork the
 * baked spine. Instead a fork is staged as REAL divergent geometry laid over it:
 *
 *   1. TELEGRAPH - the tube splits AHEAD into two actual TubeGeometry corridors
 *      that peel off the spine left + right (a proper Y-throat), each branded with
 *      a trigger word, receding into the fog.
 *   2. STEER     - the player leans (fallNav laneVote: horizontal drag / A-D /
 *      arrows). The lean biases which mouth the camera drifts toward.
 *   3. COMMIT    - at the crossroad the chosen arm wins: the camera is handed a
 *      branch PROFILE (nav.setBranchProfile) that drives it down that corridor's
 *      centerline - and because the profile is sampled with a look-ahead, the POV
 *      genuinely YAWS into the turn. The loser corridor darkens, its throat irises
 *      shut, and it slides out of frame as you turn away. It dies.
 *      Do nothing and the coaxed mouth takes you (passivity is surrender).
 *
 * Each arm is a lateral offset OF the spine (offset 0 at the throat, swinging out
 * to SWING at mid-arm, back to 0 by the far end deep in fog) - so the winner's
 * centerline re-merges with the treadmill unseen and the fall continues.
 *
 * This module owns the geometry, the steer, and the commit; it reports the choice
 * via onCommit({side, branch, forced, passive}). The meaning (re-skin, brand
 * tally) is the game's job.
 *
 * "Closing path": a fork can be born with one mouth pre-SEALED (the run decides,
 * more often the deeper it gets) - the conditioning quietly removes the choice.
 * ==========================================================================*/

import * as THREE from 'three';

const ARM_LEN = 96;       // world units each diverging corridor runs before it re-merges (in fog)
const ARM_RADIUS = 5.5;   // matches the main tube RADIUS so the split reads as one tube parting
// A REAL bifurcation: each arm peels off the spine at a FIXED angle (half of the
// ~65-deg included fork) across the near field you actually see - a proper Y - then
// curves gently back onto the spine deep in the fog so the winner re-merges with the
// closed-loop treadmill unseen. DIVERGE_TAN is the lateral gain per world unit fallen.
const DIVERGE_DEG = 32.5;                                    // half of the ~65-deg fork
const DIVERGE_TAN = Math.tan(DIVERGE_DEG * Math.PI / 180);   // ~0.637 u out per u down
const RAMP_LEN = 34;      // world units the arm holds the full angle (the visible V) before it eases back
const PEAK_OFF = DIVERGE_TAN * RAMP_LEN;   // peak lateral offset (~21.7u) at the top of the ramp
const LEAD = 120;         // units of approach over which the mouths fade up out of the fog
const THROAT_U = 0.12;    // where along the arm the labelled hoop sits (a bit into the mouth)
const BEAT_US = [0.30, 0.46, 0.62];  // fainter hoops receding down each arm - the "first few beats" that sell a real corridor

// Linger: at the fork mouth the fall crawls (the game slows the tunnel via onLinger)
// so the choice can breathe. A decisive lean commits at once; else a 5s timer takes
// the passive / coaxed mouth (do nothing and the tube chooses for you).
const LINGER_TIMEOUT = 5.0;   // seconds to hover at the mouth before the fork auto-commits
const DECIDE_VOTE = 0.55;     // |laneVote| this firm commits immediately (a deliberate lean)

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const smoothstep = (t) => { t = clamp(t, 0, 1); return t * t * (3 - 2 * t); };
// world-unit lateral offset of an arm centerline at fraction u (0 = throat/split,
// 1 = far end, back on the spine): a linear ramp (the felt 65-deg angle) that eases
// back to zero across the fog tail so the winner re-merges with the treadmill unseen.
function armOff(u) {
  const s = clamp(u, 0, 1) * ARM_LEN;
  if (s <= RAMP_LEN) return DIVERGE_TAN * s;              // the real 65-deg V you see
  const k = (s - RAMP_LEN) / (ARM_LEN - RAMP_LEN);       // 0..1 across the fog tail
  return PEAK_OFF * (1 - smoothstep(k));                 // curve back onto the spine, unseen
}

// A word rendered onto a small canvas -> additive texture. Cached by "word|color"
// so a repeated trigger word doesn't re-rasterize.
const _labelCache = new Map();
function labelTexture(word, colorHex) {
  const key = word + '|' + colorHex;
  let tex = _labelCache.get(key);
  if (tex) return tex;
  const c = document.createElement('canvas');
  c.width = 512; c.height = 256;
  const g = c.getContext('2d');
  g.clearRect(0, 0, c.width, c.height);
  g.font = '700 150px system-ui, sans-serif';
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.shadowColor = colorHex;
  g.shadowBlur = 44;
  g.fillStyle = '#ffffff';
  g.fillText(word, 256, 138);
  g.shadowBlur = 0;
  g.fillStyle = colorHex;
  g.globalAlpha = 0.5;
  g.fillText(word, 256, 138);
  tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  _labelCache.set(key, tex);
  return tex;
}

export function createJunctions({ scene, layout, nav }) {
  let J = null;           // the live fork, or null when idle
  let active = false;     // only telegraph forks during a real descent
  const _m = new THREE.Matrix4();
  const api = { onCommit: null, onLinger: null };

  // sample an arm's world centerline point at fraction u (0 = throat, 1 = far end)
  function armPoint(side, Dc, u, out) {
    const fr = layout.frameAtDepth(Dc + u * ARM_LEN);
    return (out || new THREE.Vector3()).copy(fr.pos)
      .addScaledVector(fr.binormal, side * armOff(u));
  }

  // ---- one arm: a real diverging tube corridor + a labelled throat that seals --
  function buildArm(side, desc, Dc) {
    const col = new THREE.Color(desc.color);
    const group = new THREE.Group(); // arm meshes live in world space (curve is world)

    // the corridor: a tube swept along the arm's offset-of-spine centerline
    const M = 34, pts = [];
    for (let i = 0; i <= M; i++) pts.push(armPoint(side, Dc, i / M));
    const curve = new THREE.CatmullRomCurve3(pts, false, 'catmullrom', 0.5);
    const tubeGeo = new THREE.TubeGeometry(curve, 140, ARM_RADIUS, 18, false);
    const tubeMat = new THREE.MeshBasicMaterial({
      color: col.clone().multiplyScalar(0.55),
      transparent: true, opacity: 0, side: THREE.BackSide,
      depthWrite: false, fog: true,
    });
    const tube = new THREE.Mesh(tubeGeo, tubeMat);
    group.add(tube);

    // the throat: a bright labelled hoop sitting a little way into the mouth,
    // angled along the arm so it faces the falling camera. A dark seal disc irises
    // across it when this way closes.
    const dT = Dc + THROAT_U * ARM_LEN;
    const frT = layout.frameAtDepth(dT);
    const throat = new THREE.Group();
    armPoint(side, Dc, THROAT_U, throat.position);
    _m.makeBasis(frT.binormal, frT.normal, frT.tangent); // (right, up, forward)
    throat.quaternion.setFromRotationMatrix(_m);

    const ringGeo = new THREE.RingGeometry(1.55, 2.05, 40);
    const ringMat = new THREE.MeshBasicMaterial({
      color: col, transparent: true, opacity: 0,
      blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide,
    });
    const ring = new THREE.Mesh(ringGeo, ringMat);
    throat.add(ring);

    const labGeo = new THREE.PlaneGeometry(3.0, 1.5);
    const labMat = new THREE.MeshBasicMaterial({
      map: labelTexture(desc.word, '#' + col.getHexString()),
      transparent: true, opacity: 0,
      blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide,
    });
    const label = new THREE.Mesh(labGeo, labMat);
    label.position.z = -0.15; // a hair toward the falling camera
    throat.add(label);

    const sealGeo = new THREE.CircleGeometry(ARM_RADIUS * 0.92, 40);
    const sealMat = new THREE.MeshBasicMaterial({
      color: 0x140a16, transparent: true, opacity: 0,
      depthWrite: false, side: THREE.DoubleSide,
    });
    const seal = new THREE.Mesh(sealGeo, sealMat);
    seal.scale.setScalar(0.02);
    seal.position.z = 0.05;
    throat.add(seal);

    group.add(throat);

    // ---- "first few beats": fainter hoops receding down the corridor so the arm
    // reads as a real path that CONTINUES, not a stub - whichever way you pick, the
    // way ahead is already there. No labels; pure depth cue, angled along the arm.
    const beatMats = [], beatGeos = [];
    const _pA = new THREE.Vector3(), _pB = new THREE.Vector3(), _tan = new THREE.Vector3();
    const _up = new THREE.Vector3(0, 1, 0), _rt = new THREE.Vector3(), _u2 = new THREE.Vector3();
    const _bm = new THREE.Matrix4();
    for (const bu of BEAT_US) {
      const hoop = new THREE.Group();
      armPoint(side, Dc, bu, hoop.position);
      armPoint(side, Dc, Math.max(0, bu - 0.02), _pA);
      armPoint(side, Dc, Math.min(1, bu + 0.02), _pB);
      _tan.subVectors(_pB, _pA);
      if (_tan.lengthSq() < 1e-8) _tan.set(0, 0, 1);
      _tan.normalize();
      _rt.crossVectors(_tan, _up);
      if (_rt.lengthSq() < 1e-8) _rt.set(1, 0, 0);
      _rt.normalize();
      _u2.crossVectors(_rt, _tan).normalize();
      _bm.makeBasis(_rt, _u2, _tan);
      hoop.quaternion.setFromRotationMatrix(_bm);
      const bGeo = new THREE.RingGeometry(ARM_RADIUS * 0.62, ARM_RADIUS * 0.82, 32);
      const bMat = new THREE.MeshBasicMaterial({
        color: col, transparent: true, opacity: 0,
        blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide,
      });
      hoop.add(new THREE.Mesh(bGeo, bMat));
      group.add(hoop);
      beatMats.push(bMat); beatGeos.push(bGeo);
    }

    scene.add(group);
    return {
      side, desc, group, tube, tubeMat, ring, ringMat, label, labMat, seal, sealMat,
      tubeGeo, ringGeo, labGeo, sealGeo, beatMats, beatGeos,
      sealed: false, dying: false, _sealAnim: 0, _flare: 0,
      spin: (side < 0 ? -1 : 1) * 0.5,
    };
  }

  function reveal(arm, a) {
    if (arm.dying) return;
    arm.tubeMat.opacity = a * 0.55;
    arm.ringMat.opacity = a * 0.9;
    arm.labMat.opacity = a * 0.95;
    if (arm.beatMats) for (let i = 0; i < arm.beatMats.length; i++)
      arm.beatMats[i].opacity = a * Math.max(0.06, 0.34 - i * 0.09); // dimmer the deeper the beat
  }
  // iris the seal shut, darken the corridor: this way is closed now.
  function sealArm(arm, instant) {
    arm.sealed = true; arm.dying = true;
    arm.ringMat.color.multiplyScalar(0.5);
    if (instant) { arm.seal.scale.setScalar(1); arm.sealMat.opacity = 0.9; arm._sealAnim = 1; }
    else arm._sealAnim = 0.0001; // >0 => growing in update
  }
  function flareArm(arm) { arm._flare = 1; } // punches the winner's ring bright as you pass

  function destroyArm(arm) {
    scene.remove(arm.group);
    arm.tubeGeo.dispose(); arm.ringGeo.dispose(); arm.labGeo.dispose(); arm.sealGeo.dispose();
    arm.tubeMat.dispose(); arm.ringMat.dispose(); arm.labMat.dispose(); arm.sealMat.dispose();
    if (arm.beatGeos) for (const g of arm.beatGeos) g.dispose();
    if (arm.beatMats) for (const m of arm.beatMats) m.dispose();
    // label textures are cached + shared -> not disposed here
  }

  function teardown() {
    if (!J) return;
    // aborted mid-hover (run ended / setActive(false)): hand the tunnel speed back
    if (J.phase === 'linger' && api.onLinger) { try { api.onLinger(false); } catch (e) { /* ignore */ } }
    destroyArm(J.left);
    destroyArm(J.right);
    try { nav.setBranchProfile(null); nav.setLane(0); nav.setJunctionArmed(false); } catch (e) { /* ignore */ }
    J = null;
  }

  function commit(depth) {
    // the hover is over: hand the tunnel speed back before we ride out the arm
    if (api.onLinger) { try { api.onLinger(false); } catch (e) { /* ignore */ } }
    const Dc = J.atDepth;
    const vote = nav.getLaneVote();
    let forced = false, side;
    if (J.preseal === 'left') { side = 'right'; forced = true; }
    else if (J.preseal === 'right') { side = 'left'; forced = true; }
    else if (vote > 0.08) side = 'right';
    else if (vote < -0.08) side = 'left';
    else side = J.coaxSide;            // no lean -> the coaxed mouth takes you
    const passive = !forced && Math.abs(vote) <= 0.08;

    const winner = side === 'left' ? J.left : J.right;
    const loser = side === 'left' ? J.right : J.left;
    const winSign = side === 'left' ? -1 : 1;

    // hand the camera down the winner's centerline. Sampled at depth (pos) and
    // depth+lookahead (aim) inside fallNav -> the POV yaws into the corridor. The
    // lean the player was holding at the crossroad is bled off over the first bit
    // so entry is smooth, not a snap.
    const entryLean = nav.getLane();
    try {
      nav.setJunctionArmed(false);
      nav.setBranchProfile((d) => {
        const u = clamp((d - Dc) / ARM_LEN, 0, 1);
        const arm = winSign * armOff(u);
        const lean = entryLean * Math.max(0, 1 - u * 4); // fade the pre-lean over the first quarter
        return arm + lean;
      });
    } catch (e) { /* ignore */ }

    if (!loser.sealed) sealArm(loser, false);
    flareArm(winner);

    J.phase = 'trail';
    J.winner = winner;
    J.commitDepth = depth;
    J.trailEnd = Dc + ARM_LEN;
    if (api.onCommit) { try { api.onCommit({ side, branch: winner.desc, forced, passive }); } catch (e) { /* ignore */ } }
  }

  // reached the fork mouth: hover here (the game crawls the tunnel via onLinger)
  // while the choice can breathe. Commit on a decisive lean or the 5s timeout.
  function enterLinger() {
    J.phase = 'linger';
    J.lingerT = 0;
    if (api.onLinger) { try { api.onLinger(true); } catch (e) { /* ignore */ } }
  }

  function update(depth, dt) {
    if (!J) return;

    // live: spin the hoops, grow any sealing iris, decay winner flare
    for (const m of [J.left, J.right]) {
      m.ring.rotation.z += m.spin * dt;
      if (m._sealAnim > 0 && m._sealAnim < 1) {
        m._sealAnim = clamp(m._sealAnim + dt * 2.5, 0, 1);
        m.seal.scale.setScalar(m._sealAnim);
        m.sealMat.opacity = 0.9 * m._sealAnim;
        m.tubeMat.color.multiplyScalar(Math.max(0.90, 1 - dt * 1.4)); // corridor goes dark as it dies
      }
      if (m._flare > 0) {
        m._flare = Math.max(0, m._flare - dt * 2.2);
        m.ringMat.opacity = Math.min(1, m.ringMat.opacity + m._flare * 0.6);
        m.tubeMat.opacity = Math.min(0.7, m.tubeMat.opacity + m._flare * 0.2);
      }
    }

    if (J.phase === 'approach') {
      // you can't lean into an already-sealed mouth
      let vote = nav.getLaneVote();
      if (J.preseal === 'left' && vote < 0) nav.resetLaneVote();
      else if (J.preseal === 'right' && vote > 0) nav.resetLaneVote();
      // fade the corridors up as we close the gap
      const near = clamp((depth - (J.atDepth - LEAD)) / LEAD, 0, 1);
      reveal(J.left, near);
      reveal(J.right, near);
      if (depth >= J.atDepth) enterLinger();
    } else if (J.phase === 'linger') {
      // hovering at the Y (the tunnel has crawled). Full reveal; a firm deliberate
      // lean commits at once, otherwise the 5s timer takes the passive/coaxed mouth.
      reveal(J.left, 1);
      reveal(J.right, 1);
      let vote = nav.getLaneVote();
      if (J.preseal === 'left' && vote < 0) { nav.resetLaneVote(); vote = 0; }
      else if (J.preseal === 'right' && vote > 0) { nav.resetLaneVote(); vote = 0; }
      J.lingerT += dt;
      if (Math.abs(vote) >= DECIDE_VOTE || J.lingerT >= LINGER_TIMEOUT) commit(depth);
    } else if (J.phase === 'trail') {
      // the winner keeps drawing bright; the loser fades as it slides out of frame
      const k = clamp((depth - J.commitDepth) / (J.trailEnd - J.commitDepth), 0, 1);
      const loser = J.winner === J.left ? J.right : J.left;
      loser.tubeMat.opacity *= (1 - k * 0.08);
      loser.ringMat.opacity *= (1 - k * 0.08);
      loser.labMat.opacity *= (1 - k * 0.08);
      if (depth >= J.trailEnd) teardown();
    }
  }

  return {
    /** Arm a fork ahead. left/right = {word, color, brand, ...}; coaxSide = the
     * mouth that takes a passive faller; preseal = 'left'|'right'|null (born shut). */
    schedule({ atDepth, left, right, coaxSide = 'left', preseal = null }) {
      if (!active) return;
      if (J) teardown();
      J = {
        phase: 'approach', atDepth, coaxSide, preseal, winner: null, lingerT: 0,
        left: buildArm(-1, left, atDepth),
        right: buildArm(1, right, atDepth),
        commitDepth: 0, trailEnd: 0,
      };
      if (preseal === 'left') sealArm(J.left, true);
      else if (preseal === 'right') sealArm(J.right, true);
      try { nav.resetLaneVote(); nav.setJunctionArmed(true); } catch (e) { /* ignore */ }
    },
    update,
    isBusy: () => !!J,
    setActive(on) {
      active = !!on;
      if (!active && J) teardown();
    },
    get onCommit() { return api.onCommit; },
    set onCommit(fn) { api.onCommit = fn; },
    /** onLinger(true) fires when the faller reaches the fork mouth (crawl the tunnel
     * so the choice can breathe); onLinger(false) on commit/abort (hand speed back). */
    get onLinger() { return api.onLinger; },
    set onLinger(fn) { api.onLinger = fn; },
    dispose() {
      teardown();
    },
  };
}
