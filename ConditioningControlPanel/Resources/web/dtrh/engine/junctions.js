/* ============================================================================
 * junctions.js - branching paths on the tube ("The Junction"), v4.
 *
 * The old fork (v3) overlaid two TubeGeometry "arms" that were just lateral
 * OFFSETS of the same closed-loop spine - they swung out then eased back to 0 so
 * the winner re-merged with the treadmill "unseen". They sat INSIDE the bore
 * (tube-in-tube) and, because both returned to the spine, the branches
 * RECONNECTED. It read as fake and looked bad.
 *
 * v4 clones the Explore "Deeper hole" technique and makes the branches real:
 *
 *   1. TELEGRAPH - two mouths are CARVED INTO THE TUBE WALL. Each mouth feeds a
 *      `hole` to the main-tube shader (tunnel.setHoles) which discards the wall
 *      inside the mouth cylinder + lights a glowing rim, so a genuine opening
 *      appears. A diverging vein (its own TubeGeometry, trimmed flush to the
 *      bore by a clip shader) plunges away from each mouth and NEVER returns.
 *   2. CHOOSE   - a GIF/video card hangs in each mouth. Click one (raycast ->
 *      pickSide) to shatter it and dive; a decisive lean also commits; doing
 *      nothing hovers, then a 5s timeout takes the coaxed mouth (surrender).
 *   3. DIVE     - the chosen vein's ride-curve is handed to fallNav.enterVein:
 *      the camera leaves the treadmill and rides down the corridor (position
 *      lerp + quaternion slerp). The loser vein + its card are disposed AT ONCE.
 *   4. REBASE   - at the vein's tail fallNav fires onVeinEnd -> the scene builds
 *      a FRESH endless loop aligned to the vein exit (coaxial, fog-hidden) and
 *      rebases the treadmill onto it. The chosen branch has BECOME the new tube;
 *      the old trunk is gone. onVeinEnd(exitFrame) is the scene's job.
 *
 * Reports the choice via onCommit({side, branch, forced, passive}) (unchanged
 * tally contract) and hands the scene the exit frame via onVeinEnd(frame).
 * ==========================================================================*/

import * as THREE from 'three';
import { RADIUS, FOG_COLOR, FOG_DENSITY } from './tunnel.js';

const VEIN_LEN = 150;        // world units each diverging corridor runs (the "first few chunks")
const VEIN_RADIUS = RADIUS;  // match the main bore so the rebase handoff is coaxial/seamless
const DIVERGE_DEG = 60;      // peel angle off the spine tangent - ~120 deg between the two arms (a true bifurcation, not a shallow split)
const CUT_LEN = 170;         // world units of trunk ERASED past the fork (a true dead-end; the
                             //   only ways on are the two branch mouths). Long enough that the
                             //   loop reappears deep in fog beyond it.
const STOP_BACK = 24;        // units short of the fork the fall parks, so the whole Y is seen ahead
const HOLE_R = Math.min(VEIN_RADIUS * 1.06, RADIUS * 0.94); // mouth radius carved in the wall
const LEAD = 120;            // units of approach over which the mouths fade up out of the fog
const CARD_INTO = 3.2;       // how far into the mouth the media card hangs

const LINGER_TIMEOUT = 5.0;  // seconds hovering at the mouth before the fork auto-commits
const DECIDE_VOTE = 0.55;    // |laneVote| this firm commits immediately (a deliberate lean)

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const deg2rad = (d) => d * Math.PI / 180;

// ---- vein material: a glowing corridor, trimmed flush to the artery bore -----
// uClip* discards vein fragments still inside the main tube (so the vein emerges
// from the wall surface, not poking into the bore); a rim lights the cut. Kept
// visually close to the main tube (pink scrolling rings) so it reads as tube.
const VEIN_VERT = `
  varying vec2 vUv;
  varying vec3 vWorld;
  varying float vFogDepth;
  void main() {
    vUv = uv;
    vWorld = (modelMatrix * vec4(position, 1.0)).xyz;
    vec4 mv = modelViewMatrix * vec4(position, 1.0);
    vFogDepth = -mv.z;
    gl_Position = projectionMatrix * mv;
  }`;

const VEIN_FRAG = `
  precision highp float;
  varying vec2 vUv;
  varying vec3 vWorld;
  varying float vFogDepth;
  uniform float uTime, uOpacity, uRings, uScroll;
  uniform vec3 uColor, uRimColor, uFogColor;
  uniform float uFogDensity;
  uniform int uClipOn;
  uniform vec3 uClipPoint, uClipAxis;
  uniform float uClipR, uClipReach;

  float lineMask(float coord, float w) {
    float di = 0.5 - abs(fract(coord) - 0.5);
    float aa = max(w, 1.5 * fwidth(coord)); // screen-space AA: no sub-pixel line crawl when the fall hovers at a fork
    return 1.0 - smoothstep(0.0, aa, di);
  }

  void main() {
    // flush-trim to the artery bore + rim glow on the cut
    float edge = 1e9;
    if (uClipOn == 1) {
      vec3 d = vWorld - uClipPoint;
      float al = dot(d, uClipAxis);
      vec3 pe = d - al * uClipAxis;
      float dr = length(pe);
      if (abs(al) < uClipReach) {
        if (dr < uClipR) discard;
        edge = min(edge, dr - uClipR);
      }
    }
    float scroll = uTime * uScroll;
    float ring = lineMask(vUv.x * uRings - scroll, 0.06);
    float ringGlow = 0.4 + 1.8 * pow(0.5 + 0.5 * sin((vUv.x * uRings - scroll) * 6.2831), 3.0);
    vec3 col = uColor * 0.5 + uColor * ring * ringGlow;
    float rim = 1.0 - smoothstep(0.0, 0.9, edge);
    col += uRimColor * rim * (2.4 + 0.6 * sin(uTime * 2.0));
    float f = 1.0 - exp(-uFogDensity * uFogDensity * vFogDepth * vFogDepth);
    col = mix(col, uFogColor, clamp(f, 0.0, 1.0));
    gl_FragColor = vec4(col, uOpacity);
  }`;

export function createJunctions({ scene, layout, nav, tunnel, spawner }) {
  let J = null;           // the live fork, or null when idle
  let active = false;     // only telegraph forks during a real descent
  const api = { onCommit: null, onLinger: null, onVeinEnd: null };
  const _v = new THREE.Vector3(), _v2 = new THREE.Vector3(), _m = new THREE.Matrix4();
  const shatters = [];    // live shard bursts (ticked in update)

  // fallNav fires this at the vein tail: hand the scene the exit frame so it can
  // rebase the loop, then finish the dive (dispose winner, clear holes).
  if (nav && nav.setOnVeinEnd) nav.setOnVeinEnd(handleVeinEnd);

  // ---- the dead-end cap: wall off the trunk just past the split so the tube does
  // NOT continue straight ahead. The main tube is a closed loop that keeps going;
  // this opaque disc across the bore occludes the forward path, so the only exits
  // are the two carved branch mouths - a real bifurcation, not a 3-way. ----------
  // the live scene fog color (region-tinted, animated by fx.js). The veins must
  // fade to THIS, not the static FOG_COLOR - otherwise in a green/other-tinted
  // region they fade to dark purple and read as a black void against the surround.
  function fogCol() {
    return (scene.fog && scene.fog.color) ? scene.fog.color : new THREE.Color(FOG_COLOR);
  }

  // map a spine PARAMETER fraction (what `depth/loopDepth` is) to the tube's
  // ARC-LENGTH fraction (what the shader's vUv.x / cut window is in). The two
  // differ on the wavy loop, so the forward-cut has to be placed in arc space.
  function arcFrac(tParam) {
    const lens = layout.spine.getLengths();      // cached cumulative arc lengths, param-uniform
    const n = lens.length - 1;
    const total = lens[n] || 1;
    const x = (((tParam % 1) + 1) % 1) * n;
    const i = Math.floor(x), f = x - i;
    const a = lens[i], b = lens[Math.min(i + 1, n)];
    return (a + (b - a) * f) / total;
  }

  // erase the trunk from the fork forward CUT_LEN units so the tube truly ends at
  // the split (no straight-ahead path); the only exits are the two branch mouths.
  function applyCut(atDepth) {
    if (!tunnel || !tunnel.setCut) return;
    const lo = arcFrac(atDepth / layout.loopDepth);
    const hi = arcFrac((atDepth + CUT_LEN) / layout.loopDepth);
    tunnel.setCut(lo, hi);
  }

  // ---- one vein: a real diverging corridor + a carved mouth + a hanging card --
  function buildVein(side, desc, Dc) {
    const fr = layout.frameAtDepth(Dc);
    const col = new THREE.Color(desc.color);

    // peel direction: off the tangent toward this wall, tilted down (Explore's dir)
    const dir = fr.tangent.clone().multiplyScalar(Math.cos(deg2rad(DIVERGE_DEG)))
      .addScaledVector(fr.binormal, side * Math.sin(deg2rad(DIVERGE_DEG)))
      .add(new THREE.Vector3(0, -0.18, 0)).normalize();
    // the mouth starts a little INSIDE the bore (so the vein bulges out through
    // the wall); the hole cylinder is what the tube shader discards.
    const mouth = fr.pos.clone().addScaledVector(dir, RADIUS * 0.30);
    const hole = { point: mouth.clone(), axis: dir.clone(), r: HOLE_R };
    // trim the vein flush to the bore only in a SHORT band at the mouth (reach ~1R,
    // not 2R): past the fork the trunk is erased, so a long clip just carves a void
    // out of the corridor exactly where the camera enters it on the dive.
    const clip = { point: fr.pos.clone(), axis: fr.tangent.clone().normalize(), r: RADIUS, reach: RADIUS * 1.0 };

    // the corridor curve: walk a heading that peels off, keeps sinking, and adds
    // a gentle S-wobble so it reads as a real path that CONTINUES (never re-merges).
    const STEP = 6, N = Math.ceil(VEIN_LEN / STEP);
    const head = dir.clone();
    const pts = [fr.pos.clone()];
    let p = fr.pos.clone();
    for (let i = 1; i <= N; i++) {
      const ph = i / N;
      const curl = Math.sin(ph * Math.PI * 1.6) * 0.05;   // two soft reversals
      head.applyAxisAngle(fr.normal, side * curl);
      head.y -= 0.02;                                      // keep plunging
      head.normalize();
      p = p.clone().addScaledVector(head, STEP);
      pts.push(p.clone());
    }

    // anti-clip pass (ported from the Explore reference): the corridor snakes and
    // plunges, so a later bend can wander back into a fold of the (also-winding)
    // main tube and poke through its wall - which is exactly the "camera flies out
    // of bounds" you see mid-dive. Sample the artery spine, then push any vein point
    // that strays within (arteryR + veinR + margin) of it radially back out. The
    // lead-in near the fork is left alone so the flush junction seam is preserved.
    {
      const SN = 200;
      const arteryPts = [];
      for (let i = 0; i <= SN; i++) arteryPts.push(layout.spine.getPoint(i / SN));
      const clearance = RADIUS + VEIN_RADIUS + 2.0;
      const startIdx = Math.max(2, Math.ceil(pts.length * 0.15));
      const _d = new THREE.Vector3();
      for (let pass = 0; pass < 3; pass++) {
        for (let i = startIdx; i < pts.length; i++) {
          const pp = pts[i];
          let best = Infinity, bp = null;
          for (let j = 0; j < arteryPts.length; j++) {
            const dd = pp.distanceToSquared(arteryPts[j]);
            if (dd < best) { best = dd; bp = arteryPts[j]; }
          }
          const dist = Math.sqrt(best);
          if (bp && dist < clearance) {
            _d.subVectors(pp, bp);
            if (_d.lengthSq() < 1e-6) _d.set(0, -1, 0);
            _d.normalize();
            pp.addScaledVector(_d, (clearance - dist) * 0.6);
          }
        }
      }
    }
    const curve = new THREE.CatmullRomCurve3(pts, false, 'catmullrom', 0.5);

    const tubeGeo = new THREE.TubeGeometry(curve, 120, VEIN_RADIUS, 20, false);
    const mat = new THREE.ShaderMaterial({
      uniforms: {
        uTime: { value: 0 },
        uOpacity: { value: 0 },
        uRings: { value: Math.round(VEIN_LEN / 8) },
        uScroll: { value: 1.0 },
        uColor: { value: col.clone() },
        uRimColor: { value: col.clone().lerp(new THREE.Color(0xffffff), 0.4) },
        uFogColor: { value: fogCol().clone() },
        uFogDensity: { value: FOG_DENSITY },
        uClipOn: { value: 1 },
        uClipPoint: { value: clip.point },
        uClipAxis: { value: clip.axis },
        uClipR: { value: clip.r },
        uClipReach: { value: clip.reach },
      },
      vertexShader: VEIN_VERT,
      fragmentShader: VEIN_FRAG,
      transparent: true,
      side: THREE.DoubleSide,
      depthWrite: false,
      extensions: { derivatives: true }, // fwidth() line AA (core on WebGL2)
    });
    const tube = new THREE.Mesh(tubeGeo, mat);
    scene.add(tube);

    // invisible mouth disc: a raycast target spanning the opening (click to dive
    // even if the card is off to one side). Faces back up-vein toward the camera.
    const discGeo = new THREE.CircleGeometry(VEIN_RADIUS * 1.05, 24);
    const discMat = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0, depthWrite: false, side: THREE.DoubleSide });
    const disc = new THREE.Mesh(discGeo, discMat);
    disc.position.copy(mouth);
    // face back UP THE MAIN TUBE toward the approaching camera (not down the vein):
    // at a steep fork the mouth is nearly side-on, so aiming it at the POV keeps the
    // opening clickable and the card readable.
    disc.lookAt(mouth.clone().addScaledVector(fr.tangent, -1));
    disc.userData = { type: 'veinmouth', side };
    scene.add(disc);

    // dark seal disc, shown only when this mouth is born sealed (preseal)
    const sealGeo = new THREE.CircleGeometry(VEIN_RADIUS * 0.95, 32);
    const sealMat = new THREE.MeshBasicMaterial({ color: 0x140a16, transparent: true, opacity: 0, depthWrite: false, side: THREE.DoubleSide });
    const seal = new THREE.Mesh(sealGeo, sealMat);
    seal.position.copy(mouth).addScaledVector(dir, 0.2);
    seal.quaternion.copy(disc.quaternion);
    scene.add(seal);

    // the hanging media card: a GIF/video from the active pool, facing back up
    // the vein. Given a slow pendulum sway in update().
    const cardPos = mouth.clone().addScaledVector(dir, CARD_INTO);
    // hang it facing back up the main tube toward the incoming camera (readable +
    // clickable head-on even though the mouth itself is steeply side-on).
    _m.lookAt(cardPos, cardPos.clone().addScaledVector(fr.tangent, -1), new THREE.Vector3(0, 1, 0));
    const cardQuat = new THREE.Quaternion().setFromRotationMatrix(_m);
    let card = null;
    if (spawner && spawner.createDetachedCard) {
      card = spawner.createDetachedCard({ pos: cardPos, quat: cardQuat, scale: 1.0 });
      if (card && card.group) {
        card.group.userData = { type: 'veinmouth', side };
        scene.add(card.group);   // the handle doesn't self-attach; the mouth owns it
      }
    }

    return {
      side, desc, curve, dir: dir.clone(), mouth: mouth.clone(),
      tube, mat, tubeGeo, disc, discGeo, discMat, seal, sealGeo, sealMat,
      card, cardQuat, hole, sealed: false, dying: false, swayT: Math.random() * 6,
    };
  }

  function reveal(vein, a) {
    if (vein.dying) return;
    vein.mat.uniforms.uOpacity.value = a;
  }

  function destroyVein(vein) {
    if (vein.destroyed) return;   // loser is killed at commit, then teardown sweeps both
    vein.destroyed = true;
    scene.remove(vein.tube); scene.remove(vein.disc); scene.remove(vein.seal);
    vein.tubeGeo.dispose(); vein.mat.dispose();
    vein.discGeo.dispose(); vein.discMat.dispose();
    vein.sealGeo.dispose(); vein.sealMat.dispose();
    if (vein.card) { try { vein.card.dispose(); } catch (e) { /* ignore */ } }
  }

  function pushHoles() {
    if (!tunnel || !tunnel.setHoles || !J) return;
    const hs = [];
    if (!J.left.dying) hs.push(J.left.hole);
    if (!J.right.dying) hs.push(J.right.hole);
    tunnel.setHoles(hs);
  }

  function teardown() {
    if (!J) return;
    if (J.phase === 'linger' && api.onLinger) { try { api.onLinger(false); } catch (e) { /* ignore */ } }
    destroyVein(J.left);
    destroyVein(J.right);
    if (tunnel && tunnel.clearHoles) tunnel.clearHoles();
    if (tunnel && tunnel.clearCut) tunnel.clearCut();
    try { nav.setLane(0); nav.setJunctionArmed(false); nav.setForwardHold(false); } catch (e) { /* ignore */ }
    J = null;
  }

  // ---- shatter: fracture the chosen card into shards that fly out + fade ------
  function spawnShatter(vein) {
    const content = vein.card && vein.card.getContent && vein.card.getContent();
    if (!content || !content.material || !content.material.map) return;
    const tex = content.material.map;
    // card world transform + size
    content.updateWorldMatrix(true, false);
    const wm = content.matrixWorld.clone();
    const sc = new THREE.Vector3(); wm.decompose(new THREE.Vector3(), new THREE.Quaternion(), sc);
    const w = sc.x, h = sc.y;
    const grp = new THREE.Group();
    grp.applyMatrix4(wm);
    grp.scale.set(1, 1, 1); // shards carry their own sizes; strip the plane scale
    const N = 4, sw = w / N, sh = h / N;
    const shards = [];
    for (let iy = 0; iy < N; iy++) {
      for (let ix = 0; ix < N; ix++) {
        const g = new THREE.PlaneGeometry(sw * 0.92, sh * 0.92);
        const uv = g.attributes.uv;
        for (let k = 0; k < uv.count; k++) {
          uv.setXY(k, (ix + uv.getX(k)) / N, (iy + uv.getY(k)) / N);
        }
        uv.needsUpdate = true;
        const m = new THREE.MeshBasicMaterial({ map: tex, transparent: true, side: THREE.DoubleSide, depthWrite: false });
        const mesh = new THREE.Mesh(g, m);
        mesh.position.set((ix - (N - 1) / 2) * sw, (iy - (N - 1) / 2) * sh, 0);
        grp.add(mesh);
        const vel = new THREE.Vector3((ix - (N - 1) / 2) + (Math.random() - 0.5), (iy - (N - 1) / 2) + (Math.random() - 0.5), (Math.random() - 0.3) * 2)
          .multiplyScalar(0.9);
        shards.push({ mesh, mat: m, geo: g, vel, spin: (Math.random() - 0.5) * 8 });
      }
    }
    scene.add(grp);
    // keep the card texture alive for the shatter, then dispose the whole card
    shatters.push({ grp, shards, life: 0, dur: 0.6, card: vein.card });
    if (vein.card && vein.card.hide) vein.card.hide();
    vein.card = null; // ownership moves to the shatter (disposed when it ends)
  }

  function tickShatters(dt) {
    for (let i = shatters.length - 1; i >= 0; i--) {
      const s = shatters[i];
      s.life += dt;
      const k = clamp(s.life / s.dur, 0, 1);
      for (const sh of s.shards) {
        sh.mesh.position.addScaledVector(sh.vel, dt * 6);
        sh.vel.y -= dt * 4;               // gravity
        sh.mesh.rotation.z += sh.spin * dt;
        sh.mat.opacity = 1 - k;
      }
      if (k >= 1) {
        for (const sh of s.shards) { sh.geo.dispose(); sh.mat.dispose(); }
        scene.remove(s.grp);
        if (s.card && s.card.dispose) { try { s.card.dispose(); } catch (e) { /* ignore */ } }
        shatters.splice(i, 1);
      }
    }
  }

  // ---- commit: pick a side, dive the winner, kill the loser ------------------
  function commit(reason, forcedSide) {
    if (!J || J.phase === 'trail' || J.phase === 'done') return;
    if (api.onLinger) { try { api.onLinger(false); } catch (e) { /* ignore */ } }

    let side, forced = false, passive = false;
    if (J.preseal === 'left') { side = 'right'; forced = true; }
    else if (J.preseal === 'right') { side = 'left'; forced = true; }
    else if (forcedSide) { side = forcedSide; }
    else {
      const vote = nav.getLaneVote();
      if (vote > 0.08) side = 'right';
      else if (vote < -0.08) side = 'left';
      else { side = J.coaxSide; passive = true; }
    }

    const winner = side === 'left' ? J.left : J.right;
    const loser = side === 'left' ? J.right : J.left;

    // the loser stops loading immediately: dispose its vein + card, drop its hole
    loser.dying = true;
    destroyVein(loser);
    pushHoles();                      // only the winner's mouth stays carved

    spawnShatter(winner);             // the chosen card shatters
    nav.setJunctionArmed(false);
    nav.enterVein(winner.curve, { length: VEIN_LEN });

    J.phase = 'trail';
    J.winner = winner;
    if (api.onCommit) { try { api.onCommit({ side, branch: winner.desc, forced, passive }); } catch (e) { /* ignore */ } }
  }

  // fallNav reached the vein tail: give the scene the exit frame to rebase the
  // loop onto, then finish (teardown happens next tick so this frame still has
  // the vein under the camera).
  function handleVeinEnd() {
    if (!J || !J.winner) return;
    const exit = getExitFrame(J.winner);
    if (api.onVeinEnd) { try { api.onVeinEnd(exit); } catch (e) { /* ignore */ } }
    J.phase = 'done';
  }

  function getExitFrame(vein) {
    const pos = vein.curve.getPoint(1, new THREE.Vector3());
    const tangent = vein.curve.getTangent(1, new THREE.Vector3()).normalize();
    const binormal = _v.crossVectors(tangent, new THREE.Vector3(0, 1, 0));
    if (binormal.lengthSq() < 1e-5) binormal.set(1, 0, 0);
    binormal.normalize();
    const up = _v2.crossVectors(binormal, tangent).normalize().clone();
    return { pos: pos.clone(), tangent: tangent.clone(), up };
  }

  function enterLinger() {
    J.phase = 'linger';
    J.lingerT = 0;
    // park the fall a touch SHORT of the fork so the camera stops with the whole Y
    // ahead of it (not nosed into the mouths) instead of drifting past out of bounds.
    try { nav.setForwardHold(true, J.atDepth - STOP_BACK); } catch (e) { /* ignore */ }
    if (api.onLinger) { try { api.onLinger(true); } catch (e) { /* ignore */ } }
  }

  function update(depth, dt) {
    tickShatters(dt);
    if (!J) return;

    // track the live (region-tinted, animated) fog color so the veins fade to the
    // SAME haze as the surround - no dark-purple void in a tinted region.
    const fc = fogCol();

    // keep vein rings flowing + card sway alive
    for (const m of [J.left, J.right]) {
      if (m.dying) continue;
      m.mat.uniforms.uTime.value += dt;
      m.mat.uniforms.uFogColor.value.copy(fc);
      if (m.card && m.card.group) {
        m.swayT += dt;
        _m.makeRotationZ(Math.sin(m.swayT * 1.1) * 0.05);
        m.card.group.quaternion.copy(m.cardQuat).multiply(new THREE.Quaternion().setFromRotationMatrix(_m));
      }
    }

    if (J.phase === 'approach') {
      const near = clamp((depth - (J.atDepth - LEAD)) / LEAD, 0, 1);
      reveal(J.left, near); reveal(J.right, near);
      // park a touch SHORT of the fork so the whole Y is seen ahead, not from inside it
      if (depth >= J.atDepth - STOP_BACK) enterLinger();
    } else if (J.phase === 'linger') {
      reveal(J.left, 1); reveal(J.right, 1);
      let vote = nav.getLaneVote();
      if (J.preseal === 'left' && vote < 0) { nav.resetLaneVote(); vote = 0; }
      else if (J.preseal === 'right' && vote > 0) { nav.resetLaneVote(); vote = 0; }
      J.lingerT += dt;
      if (Math.abs(vote) >= DECIDE_VOTE) commit('lean');
      else if (J.lingerT >= LINGER_TIMEOUT) commit('timeout');
    } else if (J.phase === 'done') {
      // the scene has rebased; the winner vein is off-camera now - clean it up
      teardown();
    }
    // phase 'trail': fallNav is riding the vein; nothing to do until onVeinEnd
  }

  return {
    /** Arm a fork ahead. left/right = {word, color, brand, ...}; coaxSide = the
     * mouth that takes a passive faller; preseal = 'left'|'right'|null (born shut). */
    schedule({ atDepth, left, right, coaxSide = 'left', preseal = null }) {
      if (!active) return;
      if (J) teardown();
      J = {
        phase: 'approach', atDepth, coaxSide, preseal, winner: null, lingerT: 0,
        left: buildVein(-1, left, atDepth),
        right: buildVein(1, right, atDepth),
      };
      applyCut(atDepth);   // erase the trunk past the fork - a true dead-end split
      if (preseal === 'left') { J.left.sealed = true; J.left.sealMat.opacity = 0.9; if (J.left.card) { J.left.card.dispose(); J.left.card = null; } }
      else if (preseal === 'right') { J.right.sealed = true; J.right.sealMat.opacity = 0.9; if (J.right.card) { J.right.card.dispose(); J.right.card = null; } }
      pushHoles();
      try { nav.resetLaneVote(); nav.setJunctionArmed(true); } catch (e) { /* ignore */ }
    },
    update,
    isBusy: () => !!J,
    /** raycast (scene.js) calls this when a mouth card/disc is clicked. */
    pickSide(side) {
      if (!J || J.phase !== 'linger') return false;
      if (J.preseal === side) return false; // can't enter a sealed mouth
      commit('click', side);
      return true;
    },
    /** meshes the scene raycasts against while a fork is open (cards + discs). */
    getPickables() {
      if (!J || J.phase !== 'linger') return [];
      const out = [];
      for (const m of [J.left, J.right]) {
        if (m.dying || m.sealed) continue;
        out.push(m.disc);
        if (m.card && m.card.group) out.push(m.card.group);
      }
      return out;
    },
    setActive(on) {
      active = !!on;
      if (!active && J) teardown();
    },
    get onCommit() { return api.onCommit; },
    set onCommit(fn) { api.onCommit = fn; },
    get onLinger() { return api.onLinger; },
    set onLinger(fn) { api.onLinger = fn; },
    /** onVeinEnd(exitFrame) - the scene rebases the loop onto {pos,tangent,up}. */
    get onVeinEnd() { return api.onVeinEnd; },
    set onVeinEnd(fn) { api.onVeinEnd = fn; },
    dispose() {
      teardown();
      for (let i = shatters.length - 1; i >= 0; i--) {
        for (const sh of shatters[i].shards) { sh.geo.dispose(); sh.mat.dispose(); }
        scene.remove(shatters[i].grp);
        if (shatters[i].card && shatters[i].card.dispose) { try { shatters[i].card.dispose(); } catch (e) { /* ignore */ } }
      }
      shatters.length = 0;
    },
  };
}
