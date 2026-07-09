/* ============================================================================
 * junctions.js - branching paths on the tube ("The Junction"), v5.
 *
 * v4 carved the two branch mouths straight into the trunk wall. At bore scale
 * that never had room to breathe: the two veins clipped through each other and
 * through the trunk right at the fork. v5 nests the whole bifurcation in an
 * ANTECHAMBER - a spherical room ~2.7 bores wide seated at the fork:
 *
 *   - the trunk pierces the chamber's near wall exactly at the fork depth (the
 *     room center sits X_RING past it, so the entry ring radius == the bore);
 *   - the two vein corridors attach to the chamber's FAR wall at +/-45 deg,
 *     each poking a short collar into the room (pipe-into-tank);
 *   - every connection is an angular hole cut in the sphere shader, a hair
 *     NARROWER than the pipe feeding it, so each hole rim is always backed by
 *     pipe wall (the renderer clear is transparent - an unbacked discard
 *     leaks the page background as a black void);
 *   - the trunk's own wall is hidden (discard) through the chamber's span:
 *     safe by construction, every sightline through the gap terminates on the
 *     chamber's opaque far interior wall or a vein interior.
 *
 * Flow: 1. TELEGRAPH - the chamber + doorways fade up out of the fog on
 * approach. 2. CHOOSE - the camera glides in and parks just inside the entry;
 * a media card fills ~85% of each doorway, each wearing its PRIZE (a power-up
 * nameplate the game supplies via desc.reward). ONLY a direct click on a card
 * (raycast -> pickSide) shatters it and dives - looking around and grabbing
 * stay free the whole linger; doing nothing just hovers. Only ~1 fork in 10 is
 * a surrender fork whose 5s timeout takes the coaxed doorway (arrow keys steer
 * it); every other fork waits for the click (long failsafe so a fork with no
 * loadable card can never wedge the run). 3. DIVE - the chosen vein's
 * ride-curve is handed to fallNav.enterVein; the loser vein + card are
 * disposed at once and its doorway bricks up. 4. REBASE - at the vein's tail
 * fallNav fires onVeinEnd -> the scene builds a FRESH endless loop aligned to
 * the vein exit and rebases the treadmill onto it. onVeinEnd(exitFrame) is the
 * scene's job.
 *
 * Reports the choice via onCommit({side, branch, forced, passive}) (unchanged
 * tally contract) and hands the scene the exit frame via onVeinEnd(frame).
 * ==========================================================================*/

import * as THREE from 'three';
import { RADIUS, FOG_COLOR, FOG_DENSITY } from './tunnel.js';

const VEIN_LEN = 150;        // world units each diverging corridor runs (the "first few chunks")
const VEIN_RADIUS = RADIUS * 0.88; // narrower than the bore: two bore-width veins can NOT coexist at
                             //   the fork (they interpenetrate each other and the wall = two giant
                             //   colored balls), and a bore-width vein tail lies COPLANAR with the
                             //   fresh loop it hands off to (z-fighting saw-teeth). Nested a notch
                             //   inside, the corridor "opens out" into the new tube at the seam.
const DIVERGE_DEG = 45;      // peel angle off the spine tangent - ~90 deg between the two arms.
                             //   60 put the mouths nearly side-on: big wall gaps between them and a
                             //   harsh dive turn. 45 seats them forward-facing with a tight join.

// ---- the antechamber: a spherical room the bifurcation nests in -------------
const ROOM_R = RADIUS * 2.7; // chamber radius (~14.9): both doorways fit on the far wall with ~50 deg
                             //   of solid wall between their rims - nothing clips anything.
const X_RING = Math.sqrt(ROOM_R * ROOM_R - RADIUS * RADIUS);
                             // the room center sits this far past the fork, so the trunk pierces the
                             //   near wall EXACTLY at the fork depth (entry ring radius == bore radius)
const MOUTH_IN = 2.0;        // vein collar reach into the chamber (pipe-into-tank lip)
const ROOM_CUT_IN = 4;       // trunk wall survives this far past the entry ring (a short throat collar
                             //   inside the room, so the wall-to-wall seam is overlapped, never a gap)
const CUT_END = X_RING + ROOM_R + 8; // trunk hidden from ROOM_CUT_IN until fully outside the far wall
// hole apertures (cos of the angular radius, measured from the room center).
// Each hole is a hair NARROWER than the pipe that feeds it, so the hole rim is
// always backed by pipe wall behind it - no page-background leak on the seam.
const ENTRY_HOLE_COS = Math.sqrt(1 - Math.pow((RADIUS * 0.96) / ROOM_R, 2));
const VEIN_HOLE_COS = Math.sqrt(1 - Math.pow((VEIN_RADIUS * 0.97) / ROOM_R, 2));

const STOP_BACK = 24;        // the glide-in engages this far short of the fork...
const STOP_IN = 2;           // ...and eases to a stop just INSIDE the entry ring - a step back
                             //   from the old mid-chamber park (8), which put the cards so close
                             //   they filled the view; from the threshold both doorways sit
                             //   comfortably framed ahead
const LEAD = 120;            // units of approach over which the doorways fade up out of the fog
const CARD_INTO = 1.6;       // how far into the doorway the media card sits (framed by the collar)
const CARD_SCALE = 3.2;      // content is ~2.5u wide at scale 1; x3.2 spans ~8u of the ~9.4u opening

const LINGER_TIMEOUT = 5.0;  // (surrender forks + presealed forks only) hover this long and the
                             //   tube chooses for you
const AUTO_PICK_CHANCE = 0.10; // ~1 fork in 10 is a surrender fork; the other 9 wait for the
                             //   player's click (the choice is theirs - the tube picking felt
                             //   like it "triggered often" when EVERY fork auto-committed at 5s)
const LINGER_FAILSAFE = 30;  // hard cap on any linger: a fork whose cards never loaded (no user
                             //   media, decode failure) must still commit or the run wedges
const VOTE_PICK = 0.08;      // |laneVote| (arrow keys) past this steers the TIMEOUT choice only -
                             //   there is no lean-to-commit: entering a branch is click-on-card

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const deg2rad = (d) => d * Math.PI / 180;
const sstep = (a, b, x) => { x = clamp((x - a) / (b - a), 0, 1); return x * x * (3 - 2 * x); };

// nameplate chip for a doorway prize: glyph + name on a dark pill, rimmed in the
// item kind's color (teal consumable / violet passive - powerupDrops' palette).
// Overlaid ON the media card so the choice reads as "this road pays THIS".
function makeRewardBadgeTex(reward) {
  const w = 512, h = 128, c = document.createElement('canvas'); c.width = w; c.height = h;
  const x = c.getContext('2d');
  const col = reward.kind === 'consumable' ? '#66e0d0' : '#c178ff';
  const r = 30;
  x.beginPath();
  x.moveTo(r, 6);
  x.arcTo(w - 6, 6, w - 6, h - 6, r);
  x.arcTo(w - 6, h - 6, 6, h - 6, r);
  x.arcTo(6, h - 6, 6, 6, r);
  x.arcTo(6, 6, w - 6, 6, r);
  x.closePath();
  x.fillStyle = 'rgba(14,8,20,0.82)'; x.fill();
  x.strokeStyle = col; x.lineWidth = 5; x.stroke();
  x.textBaseline = 'middle';
  x.textAlign = 'left';
  x.font = '56px "Segoe UI Emoji", "Segoe UI", sans-serif';
  x.fillStyle = 'rgba(255,255,255,0.95)';
  x.fillText(reward.glyph || '◈', 26, h / 2 + 2);
  x.font = 'bold 44px "Segoe UI", sans-serif';
  x.fillStyle = col;
  let name = String(reward.name || '');
  if (name.length > 16) name = name.slice(0, 15) + '…';
  x.fillText(name, 104, h / 2 + 2);
  return new THREE.CanvasTexture(c);
}

// ---- vein material: a glowing corridor off the chamber wall ------------------
// Kept visually close to the main tube (pink scrolling rings) so it reads as
// tube. The uClip* flush-trim survives in the shader but idles (uClipOn=0):
// v5 veins meet the CHAMBER wall through its shader holes, not the trunk wall,
// so there is nothing to trim - the portal lip glow lives on the room now.
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
    float aa = clamp(1.5 * fwidth(coord), w, 0.5); // screen-space AA (clamped so grazing rings don't wash out): no sub-pixel crawl when the fall hovers at a fork
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
    // dark wall + glowing theme-colored line work, like the main tube - NOT a
    // solid color fill (a 0.5 fill made each vein read as a giant flat ball).
    // The branch's identity color lives in the rings and the mouth rim.
    vec3 col = uColor * 0.12 + uColor * ring * ringGlow;
    float rim = 1.0 - smoothstep(0.0, 0.9, edge);
    col += uRimColor * rim * (1.3 + 0.4 * sin(uTime * 2.0));
    float f = 1.0 - exp(-uFogDensity * uFogDensity * vFogDepth * vFogDepth);
    col = mix(col, uFogColor, clamp(f, 0.0, 1.0));
    gl_FragColor = vec4(col, uOpacity);
  }`;

// ---- antechamber material: the spherical room's interior wall ----------------
// BackSide + opaque (no uOpacity - the room is born 120u+ ahead, fully buried
// in fog, so distance haze IS the reveal; a translucent room would blend the
// transparent page background through its own wall). Three angular holes are
// cut where the pipes connect: entry (the trunk, around -tangent) and the two
// doorways (the vein dirs). Each hole's rim glows in the pipe's identity color.
// The mesh carries no rotation/scale, so object-space position IS the offset
// from the room center - normalize(position) gives the hole test direction.
const ROOM_VERT = `
  varying vec2 vUv;
  varying vec3 vObjDir;
  varying float vFogDepth;
  void main() {
    vUv = uv;
    vObjDir = position;
    vec4 mv = modelViewMatrix * vec4(position, 1.0);
    vFogDepth = -mv.z;
    gl_Position = projectionMatrix * mv;
  }`;

const ROOM_FRAG = `
  precision highp float;
  #define ROOM_HOLES 3
  varying vec2 vUv;
  varying vec3 vObjDir;
  varying float vFogDepth;
  uniform float uTime, uFogDensity;
  uniform vec3 uBg1, uBg2, uLineColor, uFogColor;
  uniform vec3 uHoleDir[ROOM_HOLES];
  uniform float uHoleCos[ROOM_HOLES]; // wall discarded where dot > this; 2.0 = bricked up (loser)
  uniform vec3 uHoleRim[ROOM_HOLES];

  float lineMask(float coord, float w) {
    float di = 0.5 - abs(fract(coord) - 0.5);
    float aa = clamp(1.5 * fwidth(coord), w, 0.5); // screen-space AA, same as the tube's line work
    return 1.0 - smoothstep(0.0, aa, di);
  }

  void main() {
    vec3 dirN = normalize(vObjDir);
    vec3 rim = vec3(0.0);
    for (int i = 0; i < ROOM_HOLES; i++) {
      float ca = dot(dirN, uHoleDir[i]);
      if (ca > uHoleCos[i]) discard;              // the connection opening
      // glowing portal lip just outside the cut (~3 deg band in cos space)
      float g = 1.0 - smoothstep(0.0, 0.022, uHoleCos[i] - ca);
      rim += uHoleRim[i] * g * (1.5 + 0.5 * sin(uTime * 2.0));
    }
    // dark chamber wall + faint latitude bands and slowly wheeling meridian
    // spokes (pinched out at the poles), matching the tube's palette
    vec3 base = mix(uBg1, uBg2, 0.5 + 0.5 * sin(vUv.y * 12.566));
    float lat = lineMask(vUv.y * 10.0, 0.05);
    float lon = lineMask(vUv.x * 22.0 - uTime * 0.25, 0.05) * sin(vUv.y * 3.14159);
    vec3 col = base + uLineColor * (lat * 0.30 + lon * 0.22);
    col += rim;
    float f = 1.0 - exp(-uFogDensity * uFogDensity * vFogDepth * vFogDepth);
    col = mix(col, uFogColor, clamp(f, 0.0, 1.0));
    gl_FragColor = vec4(col, 1.0);
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

  // the live scene fog color (region-tinted, animated by fx.js). The veins + their
  // far-end covers must fade to THIS, not the static FOG_COLOR - otherwise in a
  // green/other-tinted region they fade to dark purple and read as a void.
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

  // hide the trunk where it runs through the chamber: from just past the entry
  // ring to past the far wall (beyond that the sphere's opaque far wall occludes
  // the rest of the loop). DISCARD, not fade, and safe by construction: from
  // every reachable viewpoint (trunk bore on approach, mid-chamber, inside a
  // ridden vein) each sightline through the missing wall terminates on the
  // chamber's opaque far interior wall or a vein interior - never the page
  // background.
  function applyCut(atDepth) {
    if (!tunnel || !tunnel.setCut) return;
    const lo = arcFrac((atDepth + ROOM_CUT_IN) / layout.loopDepth);
    const hi = arcFrac((atDepth + CUT_END) / layout.loopDepth);
    tunnel.setCut(lo, hi, true);
  }

  // ---- one vein: a diverging corridor attached to the chamber's far wall ------
  function buildVein(side, desc, fr, C) {
    const col = new THREE.Color(desc.color);

    // peel direction: off the tangent toward this wall, tilted down (Explore's dir)
    const dir = fr.tangent.clone().multiplyScalar(Math.cos(deg2rad(DIVERGE_DEG)))
      .addScaledVector(fr.binormal, side * Math.sin(deg2rad(DIVERGE_DEG)))
      .add(new THREE.Vector3(0, -0.18, 0)).normalize();
    // the corridor starts a hair INSIDE the chamber (a short collar poking
    // through the wall hole - pipe-into-tank), then runs outward through the
    // sphere's doorway, which is cut a touch narrower than this bore.
    const mouth = C.clone().addScaledVector(dir, ROOM_R - MOUTH_IN);

    // the corridor curve: walk a heading that peels off, keeps sinking, and adds
    // a gentle S-wobble so it reads as a real path that CONTINUES (never re-merges).
    // The curl + plunge TAPER to zero over the last ~28% so the tail is STRAIGHT:
    // exit tangent == the camera's approach heading, so the fresh loop the scene
    // seats at the exit telescopes in dead ahead (not off-axis to one side).
    const STEP = 6, N = Math.ceil(VEIN_LEN / STEP);
    const head = dir.clone();
    const pts = [mouth.clone()];
    let p = mouth.clone();
    for (let i = 1; i <= N; i++) {
      const ph = i / N;
      const taper = 1 - sstep(0.72, 1.0, ph);              // straight tail
      const curl = Math.sin(ph * Math.PI * 1.6) * 0.05 * taper; // two soft reversals
      head.applyAxisAngle(fr.normal, side * curl);
      head.y -= 0.02 * taper;                              // keep plunging (eases off)
      head.normalize();
      p = p.clone().addScaledVector(head, STEP);
      pts.push(p.clone());
    }

    // anti-clip pass (ported from the Explore reference): the corridor snakes and
    // plunges, so a later bend can wander back into a fold of the (also-winding)
    // main tube and poke through its wall - which is exactly the "camera flies out
    // of bounds" you see mid-dive. Sample the artery spine, then push any vein point
    // that strays within (arteryR + veinR + margin) of it radially back out. The
    // lead-in near the chamber is left alone (the first few points seat the collar
    // against the room wall by design; they clear the spine on their own past that).
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
        uClipOn: { value: 0 },   // no trunk trim in v5: the vein meets the CHAMBER wall
        uClipPoint: { value: new THREE.Vector3() },
        uClipAxis: { value: new THREE.Vector3(0, 0, 1) },
        uClipR: { value: 0 },
        uClipReach: { value: 0 },
      },
      vertexShader: VEIN_VERT,
      fragmentShader: VEIN_FRAG,
      transparent: true,               // for the fog fade-in reveal (uOpacity)
      // interior-only, like the main tube: from the trunk you look INTO the mouth
      // and see a corridor receding - not the outside of a colored cylinder.
      // DoubleSide showed both veins' exteriors as two giant balls pasted over
      // each other (and depthWrite:false let whichever drew last win per-pixel).
      side: THREE.BackSide,
      depthWrite: true,
      extensions: { derivatives: true }, // fwidth() line AA (core on WebGL2)
    });
    const tube = new THREE.Mesh(tubeGeo, mat);
    scene.add(tube);

    // everything seated at the mouth (seal disc, media card) faces back down the
    // doorway axis at the room's middle, where the camera parks. NOTE: there is
    // deliberately NO invisible click-catcher disc across the opening anymore -
    // the media card is the ONLY raycast target, so a click beside it looks
    // around / grabs instead of diving.
    _m.lookAt(mouth, mouth.clone().addScaledVector(dir, -1), new THREE.Vector3(0, 1, 0));
    const mouthQuat = new THREE.Quaternion().setFromRotationMatrix(_m);

    // dark seal disc, shown only when this mouth is born sealed (preseal)
    const sealGeo = new THREE.CircleGeometry(VEIN_RADIUS * 0.95, 32);
    const sealMat = new THREE.MeshBasicMaterial({ color: 0x140a16, transparent: true, opacity: 0, depthWrite: false, side: THREE.DoubleSide });
    const seal = new THREE.Mesh(sealGeo, sealMat);
    seal.position.copy(mouth).addScaledVector(dir, 0.2);
    seal.quaternion.copy(mouthQuat);
    scene.add(seal);

    // far-end covers: the corridor is an OPEN tube, and until the scene builds the
    // fresh loop at its exit there is nothing beyond the far rim but the page
    // background - a black disc hanging at the end of the ride. A fog-colored disc
    // caps the opening (removed at the build point in handleVeinEnd, so the new
    // tube then shows through), and a fog-colored annulus stays the whole ride to
    // mask the radial step out to the wider loop entry + the hidden loop-tail arc
    // behind it. Both are seated a hair short of the exit (no coplanar clash with
    // the loop's entry rim) and track the live region fog in update().
    const endPos = curve.getPoint(0.995);
    const endTan = curve.getTangent(0.995).normalize();
    const endDiscGeo = new THREE.CircleGeometry(VEIN_RADIUS * 1.02, 24);
    const endDiscMat = new THREE.MeshBasicMaterial({ color: fogCol().clone(), side: THREE.DoubleSide });
    const endDisc = new THREE.Mesh(endDiscGeo, endDiscMat);
    endDisc.position.copy(endPos);
    endDisc.lookAt(endPos.clone().addScaledVector(endTan, -1));
    scene.add(endDisc);
    const endRingGeo = new THREE.RingGeometry(VEIN_RADIUS * 0.96, RADIUS * 1.2, 32);
    const endRingMat = new THREE.MeshBasicMaterial({ color: fogCol().clone(), side: THREE.DoubleSide });
    const endRing = new THREE.Mesh(endRingGeo, endRingMat);
    endRing.position.copy(endPos);
    endRing.quaternion.copy(endDisc.quaternion);
    scene.add(endRing);

    // the doorway media card: a GIF/video from the active pool, scaled to span
    // ~85% of the opening - the choice IS the doorway. It sits just inside the
    // collar (framed by the portal lip) facing the parked camera, with a slow
    // pendulum sway in update(). Corners of square-ish content tuck behind the
    // chamber wall/lip - reads as slotted into the portal, and the wall's depth
    // occludes them cleanly.
    const cardPos = mouth.clone().addScaledVector(dir, CARD_INTO);
    const cardQuat = mouthQuat.clone();
    let card = null;
    if (spawner && spawner.createDetachedCard) {
      card = spawner.createDetachedCard({ pos: cardPos, quat: cardQuat, scale: CARD_SCALE });
      if (card && card.group) {
        card.group.userData = { type: 'veinmouth', side };
        scene.add(card.group);   // the handle doesn't self-attach; the mouth owns it
      }
    }

    // the doorway's PRIZE, overlaid on the card: a nameplate chip near its lower
    // edge (the game hands the offer in via desc.reward - id/kind/name/glyph).
    // A child of the card group so it sways with it and is a click-to-dive
    // target like the card face; its geometry/material go through destroyBadge.
    let badge = null, badgeGeo = null, badgeMat = null;
    if (desc.reward && card && card.group) {
      badgeGeo = new THREE.PlaneGeometry(2.3, 0.575);
      badgeMat = new THREE.MeshBasicMaterial({
        map: makeRewardBadgeTex(desc.reward),
        transparent: true, depthWrite: false, side: THREE.DoubleSide,
      });
      badge = new THREE.Mesh(badgeGeo, badgeMat);
      badge.position.set(0, -0.9, 0.15);   // lower-center of the card face, a hair proud
      card.group.add(badge);
    }

    return {
      side, desc, curve, dir: dir.clone(), mouth: mouth.clone(),
      tube, mat, tubeGeo, seal, sealGeo, sealMat,
      endDisc, endDiscGeo, endDiscMat, endRing, endRingGeo, endRingMat,
      card, cardQuat, badge, badgeGeo, badgeMat,
      sealed: false, dying: false, swayT: Math.random() * 6,
    };
  }

  function reveal(vein, a) {
    if (vein.dying) return;
    vein.mat.uniforms.uOpacity.value = a;
  }

  // the prize nameplate rides the card's group but owns its own geo/mat/canvas
  // texture - shed it separately (the card shatter keeps the card texture alive,
  // and a presealed mouth drops its card before the vein is destroyed).
  function destroyBadge(vein) {
    if (!vein.badge) return;
    if (vein.badge.parent) vein.badge.parent.remove(vein.badge);
    vein.badgeGeo.dispose();
    if (vein.badgeMat.map) vein.badgeMat.map.dispose();
    vein.badgeMat.dispose();
    vein.badge = null;
  }

  function destroyVein(vein) {
    if (vein.destroyed) return;   // loser is killed at commit, then teardown sweeps both
    vein.destroyed = true;
    destroyBadge(vein);
    scene.remove(vein.tube); scene.remove(vein.seal);
    vein.tubeGeo.dispose(); vein.mat.dispose();
    vein.sealGeo.dispose(); vein.sealMat.dispose();
    if (vein.endDisc) { scene.remove(vein.endDisc); vein.endDiscGeo.dispose(); vein.endDiscMat.dispose(); vein.endDisc = null; }
    scene.remove(vein.endRing); vein.endRingGeo.dispose(); vein.endRingMat.dispose();
    if (vein.card) { try { vein.card.dispose(); } catch (e) { /* ignore */ } }
  }

  // ---- the antechamber sphere: built once per fork, holes aligned to the pipes
  function buildRoom(C, entryDir, leftVein, rightVein) {
    const geo = new THREE.SphereGeometry(ROOM_R, 48, 32);
    const mat = new THREE.ShaderMaterial({
      uniforms: {
        uTime: { value: 0 },
        uBg1: { value: new THREE.Color(0x2b1024) },   // tube palette: the room IS more tube
        uBg2: { value: new THREE.Color(0x160a18) },
        uLineColor: { value: new THREE.Color(0xff69b4) },
        uFogColor: { value: fogCol().clone() },
        uFogDensity: { value: FOG_DENSITY },
        // hole order: [entry, left doorway, right doorway] - commit() bricks up
        // the loser by index, so keep this order in sync with closeDoor below
        uHoleDir: { value: [entryDir.clone(), leftVein.dir.clone(), rightVein.dir.clone()] },
        uHoleCos: { value: [ENTRY_HOLE_COS, VEIN_HOLE_COS, VEIN_HOLE_COS] },
        uHoleRim: {
          value: [
            new THREE.Color(0xff8fd8),
            leftVein.mat.uniforms.uRimColor.value.clone(),
            rightVein.mat.uniforms.uRimColor.value.clone(),
          ],
        },
      },
      vertexShader: ROOM_VERT,
      fragmentShader: ROOM_FRAG,
      side: THREE.BackSide,      // interior-only, like every pipe in this scene
      extensions: { derivatives: true }, // fwidth() line AA (core on WebGL2)
    });
    const mesh = new THREE.Mesh(geo, mat);
    mesh.position.copy(C);
    scene.add(mesh);
    return { mesh, geo, mat };
  }

  // brick up a doorway: cos threshold 2.0 can never be exceeded, so the wall
  // renders solid there (and the rim glow dies with it). Used on the LOSER at
  // commit - its corridor is destroyed, and an open hole with nothing behind it
  // would leak the page background.
  function closeDoor(side) {
    if (!J || !J.room) return;
    J.room.mat.uniforms.uHoleCos.value[side === 'left' ? 1 : 2] = 2.0;
  }

  function teardown() {
    if (!J) return;
    if (J.phase === 'linger' && api.onLinger) { try { api.onLinger(false); } catch (e) { /* ignore */ } }
    destroyVein(J.left);
    destroyVein(J.right);
    if (J.room) { scene.remove(J.room.mesh); J.room.geo.dispose(); J.room.mat.dispose(); J.room = null; }
    if (tunnel && tunnel.clearHoles) tunnel.clearHoles();  // stale-state safety; v5 sets no wall holes
    if (tunnel && tunnel.clearCut) tunnel.clearCut();
    try { nav.setLane(0); nav.setJunctionArmed(false); nav.setForwardHold(false); } catch (e) { /* ignore */ }
    J = null;
  }

  // ---- shatter: fracture the chosen card into shards that fly out + fade ------
  function spawnShatter(vein) {
    destroyBadge(vein);   // the nameplate doesn't shatter - the prize flies to the HUD instead
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
    if (!J || (J.phase !== 'approach' && J.phase !== 'linger')) return; // no re-commit mid-dive/handoff
    if (api.onLinger) { try { api.onLinger(false); } catch (e) { /* ignore */ } }

    let side, forced = false, passive = false;
    if (J.preseal === 'left') { side = 'right'; forced = true; }
    else if (J.preseal === 'right') { side = 'left'; forced = true; }
    else if (forcedSide) { side = forcedSide; }
    else {
      const vote = nav.getLaneVote();
      if (vote > VOTE_PICK) side = 'right';
      else if (vote < -VOTE_PICK) side = 'left';
      else { side = J.coaxSide; passive = true; }
    }

    const winner = side === 'left' ? J.left : J.right;
    const loser = side === 'left' ? J.right : J.left;

    // the loser stops loading immediately: dispose its vein + card, brick up its
    // doorway (the winner's stays open - its corridor backs it)
    loser.dying = true;
    destroyVein(loser);
    closeDoor(loser.side === -1 ? 'left' : 'right');

    spawnShatter(winner);             // the chosen card shatters
    nav.setJunctionArmed(false);
    nav.enterVein(winner.curve, { length: VEIN_LEN });

    J.phase = 'trail';
    J.winner = winner;
    if (api.onCommit) { try { api.onCommit({ side, branch: winner.desc, forced, passive }); } catch (e) { /* ignore */ } }
  }

  // fallNav's BUILD point (vt = VEIN_BUILD_AT, MID-ride, not the tail): give the
  // scene the exit frame so it builds the fresh loop coaxially ahead, fog-hidden.
  // Do NOT tear down here - the camera still has half the corridor to ride, and
  // tearing down now destroys the winner vein under it (the "drift through black
  // void, tube rejoins from the side" bug). Teardown waits in update() until
  // fallNav actually hands off onto the new loop (track back to 'fall').
  function handleVeinEnd() {
    if (!J || !J.winner) return;
    const exit = getExitFrame(J.winner);
    if (api.onVeinEnd) { try { api.onVeinEnd(exit); } catch (e) { /* ignore */ } }
    // the fresh loop now exists at the exit: uncap the corridor's far end so the
    // new tube shows through the opening (the annulus stays, masking the seam).
    const w = J.winner;
    if (w.endDisc) { scene.remove(w.endDisc); w.endDiscGeo.dispose(); w.endDiscMat.dispose(); w.endDisc = null; }
    J.phase = 'handoff';
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
    // glide through the entry and ease to a stop mid-chamber: the hold's eased
    // approach carries the camera in through the entry hole, and both doorways
    // (with their cards) sit ~40 deg off-axis in comfortable head-on view.
    try { nav.setForwardHold(true, J.atDepth + STOP_IN); } catch (e) { /* ignore */ }
    if (api.onLinger) { try { api.onLinger(true); } catch (e) { /* ignore */ } }
  }

  function update(depth, dt) {
    tickShatters(dt);
    if (!J) return;

    // track the live (region-tinted, animated) fog color so the veins + their
    // far-end covers fade to the SAME haze as the surround - no dark-purple void
    // in a tinted region.
    const fc = fogCol();

    // keep vein rings flowing + card sway alive
    for (const m of [J.left, J.right]) {
      if (m.dying) continue;
      m.mat.uniforms.uTime.value += dt;
      m.mat.uniforms.uFogColor.value.copy(fc);
      if (m.endDisc) m.endDiscMat.color.copy(fc);
      m.endRingMat.color.copy(fc);
      if (m.card && m.card.group) {
        m.swayT += dt;
        _m.makeRotationZ(Math.sin(m.swayT * 1.1) * 0.05);
        m.card.group.quaternion.copy(m.cardQuat).multiply(new THREE.Quaternion().setFromRotationMatrix(_m));
      }
    }
    if (J.room) {
      J.room.mat.uniforms.uTime.value += dt;
      J.room.mat.uniforms.uFogColor.value.copy(fc);
    }

    if (J.phase === 'approach') {
      const near = clamp((depth - (J.atDepth - LEAD)) / LEAD, 0, 1);
      reveal(J.left, near); reveal(J.right, near);
      // begin the glide-in a touch short of the fork; the hold eases the camera
      // through the entry hole to its mid-chamber park
      if (depth >= J.atDepth - STOP_BACK) enterLinger();
    } else if (J.phase === 'linger') {
      reveal(J.left, 1); reveal(J.right, 1);
      // no lean-to-commit: the player is free to look around, grab posters/cards/
      // power-ups. Entering a branch is a click on its card (pickSide); the arrow-
      // key vote only steers where the timeout surrender goes.
      if (J.preseal === 'left' && nav.getLaneVote() < 0) nav.resetLaneVote();
      else if (J.preseal === 'right' && nav.getLaneVote() > 0) nav.resetLaneVote();
      J.lingerT += dt;
      // surrender forks (~1 in 10) + presealed forks (no real choice anyway)
      // auto-commit at 5s; every other fork waits for the player's click, with
      // a long failsafe so a fork whose cards never loaded can't wedge the run.
      const wait = (J.preseal || J.autoPick) ? LINGER_TIMEOUT : LINGER_FAILSAFE;
      if (J.lingerT >= wait) commit('timeout');
    } else if (J.phase === 'handoff') {
      // the scene has rebased (fresh loop built coaxially at the exit); keep the
      // winner vein ENCLOSING the camera until fallNav hands off onto that loop
      // (vt >= VEIN_END_AT -> track 'fall'). Then everything - vein, holes, the
      // fresh loop's seam fade - is behind the camera and safe to clean up.
      if (!nav.isInVein()) teardown();
    }
    // phase 'trail': fallNav is riding the vein; nothing to do until onVeinEnd
  }

  return {
    /** Arm a fork ahead. left/right = {word, color, brand, ...}; coaxSide = the
     * mouth that takes a passive faller; preseal = 'left'|'right'|null (born shut). */
    schedule({ atDepth, left, right, coaxSide = 'left', preseal = null }) {
      if (!active) return;
      if (J) teardown();
      const fr = layout.frameAtDepth(atDepth);
      // chamber center: X_RING past the fork, so the trunk pierces the near wall
      // exactly at the fork depth (see the constants block)
      const C = fr.pos.clone().addScaledVector(fr.tangent, X_RING);
      const L = buildVein(-1, left, fr, C);
      const R = buildVein(1, right, fr, C);
      J = {
        phase: 'approach', atDepth, coaxSide, preseal, winner: null, lingerT: 0,
        autoPick: Math.random() < AUTO_PICK_CHANCE,   // the rare surrender fork (5s and the tube chooses)
        left: L, right: R,
        room: buildRoom(C, fr.tangent.clone().negate(), L, R),
      };
      applyCut(atDepth);   // hide the trunk through the chamber's span
      if (preseal === 'left') { J.left.sealed = true; J.left.sealMat.opacity = 0.9; if (J.left.card) { destroyBadge(J.left); J.left.card.dispose(); J.left.card = null; } }
      else if (preseal === 'right') { J.right.sealed = true; J.right.sealMat.opacity = 0.9; if (J.right.card) { destroyBadge(J.right); J.right.card.dispose(); J.right.card = null; } }
      try { nav.resetLaneVote(); nav.setJunctionArmed(true); } catch (e) { /* ignore */ }
    },
    update,
    isBusy: () => !!J,
    /** raycast (scene.js) calls this when a doorway card is clicked. */
    pickSide(side) {
      if (!J || J.phase !== 'linger') return false;
      if (J.preseal === side) return false; // can't enter a sealed mouth
      commit('click', side);
      return true;
    },
    /** meshes the scene raycasts against while a fork is open: each doorway
     * card's CONTENT plane + its prize nameplate - NOT the whole card group,
     * whose oversized glow plane (~1.7x the card) was still stealing clicks
     * aimed at grabbables beside the doorway. Clicking anywhere else must never
     * commit (it looks/grabs). */
    getPickables() {
      if (!J || J.phase !== 'linger') return [];
      const out = [];
      for (const m of [J.left, J.right]) {
        if (m.dying || m.sealed) continue;
        const c = m.card && m.card.getContent && m.card.getContent();
        if (c) out.push(c);
        if (m.badge) out.push(m.badge);
      }
      return out;
    },
    /** While a fork is alive, the depth span the chamber owns. Content spawned
     * in here floats in the hidden-trunk cut or hides behind the opaque room -
     * that starved the whole approach of grabbables (posters/cards/power-ups
     * all spawn AHEAD of the camera, straight into this span). The spawners
     * clamp their throws short of it / skip it. */
    getBlockedSpan() {
      return J ? { lo: J.atDepth - 4, hi: J.atDepth + CUT_END } : null;
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
