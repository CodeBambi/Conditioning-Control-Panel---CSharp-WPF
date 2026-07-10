/* ============================================================================
 * junctions.js - branching paths on the tube ("The Junction"), v6.
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
 * each doorway is gated by its PRIZE CARD (the power-up's art + name +
 * description, supplied via desc.reward - a media card only as a no-prize
 * fallback). ONLY a direct click on a card (raycast -> pickIndex) shatters it
 * and dives - looking around and grabbing stay free the whole linger; doing
 * nothing just hovers. Only ~1 fork in 100 (the easter egg) is
 * a surrender fork whose 5s timeout takes the coaxed doorway (arrow keys steer
 * it); every other fork waits for the click (long failsafe so a fork with no
 * loadable card can never wedge the run). 3. DIVE - the chosen vein's
 * ride-curve is handed to fallNav.enterVein; the loser vein + card are
 * disposed at once and its doorway bricks up. 4. REBASE - at the vein's tail
 * fallNav fires onVeinEnd -> the scene builds a FRESH endless loop aligned to
 * the vein exit and rebases the treadmill onto it. onVeinEnd(exitFrame) is the
 * scene's job.
 *
 * Reports the choice via onCommit({index, branch, forced, passive, ...}) and
 * hands the scene the exit frame via onVeinEnd(frame).
 *
 * v6 generalizes the chamber to N doorways (2 or 3) and adds a second MODE:
 *   - mode 'fork'  - the classic branded power-up junction (2 doors);
 *   - mode 'draft' - the BOON DRAFT staged as a room: one door per dealt boon,
 *     its card as the gatekeeper. Clicking a card = taking that boon + diving
 *     its tube into the next chamber (the game resolves the draft in onCommit).
 *     Timeout / an external skipDraft() dives the coaxed door WITHOUT the
 *     shatter (no boon granted - the game banks +1 resistance instead).
 * The surrender fork is now an easter egg: ~1 fork in 100, and when it hits
 * the tube picks the path even OVER a click (commit overrides pickIndex).
 * ==========================================================================*/

import * as THREE from 'three';
import { RADIUS, FOG_COLOR, FOG_DENSITY } from './tunnel.js';
import { makeGlowTex, makeFrameTex, makeNamePanelTex } from './powerupDrops.js';
import { intToHex } from './settings.js';
import { loadTexture } from '../shared/assets.js';

const ART = 'https://ccp.art/';   // power-up card art CDN (matches powerupDrops.js)

const VEIN_LEN = 150;        // world units each diverging corridor runs (the "first few chunks")
const VEIN_RADIUS = RADIUS * 0.88; // narrower than the bore: two bore-width veins can NOT coexist at
                             //   the fork (they interpenetrate each other and the wall = two giant
                             //   colored balls), and a bore-width vein tail lies COPLANAR with the
                             //   fresh loop it hands off to (z-fighting saw-teeth). Nested a notch
                             //   inside, the corridor "opens out" into the new tube at the seam.
const DIVERGE_DEG = 45;      // peel angle off the spine tangent - ~90 deg between the two arms.
                             //   60 put the mouths nearly side-on: big wall gaps between them and a
                             //   harsh dive turn. 45 seats them forward-facing with a tight join.
// doorway azimuths per door count. Hole angular radius is ~18.5 deg at ROOM_R,
// so adjacent doors need >37 deg of separation: 3 doors at -50/0/+50 leaves
// ~13 deg of solid wall between rims. The CENTER door (az 0) runs straight down
// the old spine, so it takes a steeper plunge (see buildVein's tilt) to duck
// under the trunk instead of letting the anti-clip pass shove it around.
const DOOR_AZIMUTHS = { 2: [-DIVERGE_DEG, DIVERGE_DEG], 3: [-50, 0, 50] };

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
const PRIZE_SCALE = 2.2;     // prize-card scale: the 2.4x3.05 portrait card + its caption plate
                             //   together just fill the ~9.4u opening (bottom corners tuck behind
                             //   the portal lip)

const LINGER_TIMEOUT = 5.0;  // (surrender forks + presealed forks only) hover this long and the
                             //   tube chooses for you
const AUTO_PICK_CHANCE = 0.01; // the surrender fork is an EASTER EGG: ~1 fork in 100. When it
                             //   hits, the tube truly takes over - it auto-commits at 5s AND
                             //   overrides a click (commit ignores the picked door). At 1-in-10
                             //   it read as a bug; at 1-in-100 it reads as the hole waking up.
const LINGER_FAILSAFE = 30;  // hard cap on any linger: a fork whose cards never loaded (no user
                             //   media, decode failure) must still commit or the run wedges
const VOTE_PICK = 0.08;      // |laneVote| (arrow keys) past this steers the TIMEOUT choice only -
                             //   there is no lean-to-commit: entering a branch is click-on-card

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const deg2rad = (d) => d * Math.PI / 180;
const sstep = (a, b, x) => { x = clamp((x - a) / (b - a), 0, 1); return x * x * (3 - 2 * x); };

// caption panel under a doorway prize card: bold name in the kind's color
// (teal consumable / violet passive - powerupDrops' palette) + the wrapped
// description in white, on a soft dark plate for readability against the vein.
function makeRewardCaptionTex(reward) {
  const w = 640, h = 200, c = document.createElement('canvas'); c.width = w; c.height = h;
  const x = c.getContext('2d');
  // capCol: draft-room boon cards carry their theme color; power-up prizes
  // keep the teal-consumable / violet-passive split
  const col = reward.capCol || (reward.kind === 'consumable' ? '#66e0d0' : '#c178ff');
  const r = 26;
  x.beginPath();
  x.moveTo(r, 4);
  x.arcTo(w - 4, 4, w - 4, h - 4, r);
  x.arcTo(w - 4, h - 4, 4, h - 4, r);
  x.arcTo(4, h - 4, 4, 4, r);
  x.arcTo(4, 4, w - 4, 4, r);
  x.closePath();
  x.fillStyle = 'rgba(12,7,18,0.78)'; x.fill();
  x.strokeStyle = col; x.globalAlpha = 0.55; x.lineWidth = 3; x.stroke(); x.globalAlpha = 1;
  x.textAlign = 'center'; x.textBaseline = 'middle';
  x.font = 'bold 44px "Segoe UI", sans-serif';
  x.fillStyle = col;
  x.fillText(`${reward.glyph || '◈'} ${reward.name || ''}`.trim(), w / 2, 44);
  // wrap the description to at most 2 lines
  x.font = '27px "Segoe UI", sans-serif';
  x.fillStyle = 'rgba(255,255,255,0.88)';
  const words = String(reward.desc || '').split(/\s+/).filter(Boolean);
  const lines = []; let line = '';
  for (const wd of words) {
    if ((line + ' ' + wd).length > 42 && line) { lines.push(line); line = wd; }
    else line = line ? line + ' ' + wd : wd;
    if (lines.length === 2) break;
  }
  if (line && lines.length < 2) lines.push(line);
  else if (lines.length === 2 && line) lines[1] = lines[1].slice(0, 39) + '…';
  const y0 = lines.length > 1 ? 106 : 122;
  lines.forEach((l, i) => x.fillText(l, w / 2, y0 + i * 38));
  return new THREE.CanvasTexture(c);
}

// The Journey Rooms: the NEXT room's title, hung as big glowing writing above
// the draft-room doors (draft mode only). Split on the ampersand so the two
// beats stack ("CURIOSITY" / "& DENIAL"); the numeral rides small on top.
function makeRoomTitleTex(title) {
  const w = 1024, h = 340, c = document.createElement('canvas'); c.width = w; c.height = h;
  const x = c.getContext('2d');
  const col = typeof title.color === 'number' ? intToHex(title.color) : (title.color || '#ff69b4');
  const text = String(title.text || '').toUpperCase();
  const amp = text.indexOf(' & ');
  const lines = amp > 0 ? [text.slice(0, amp), '& ' + text.slice(amp + 3)] : [text];
  x.textAlign = 'center'; x.textBaseline = 'middle';
  // the numeral, small and quiet above the writing
  if (title.numeral) {
    x.font = '600 44px Georgia, "Times New Roman", serif';
    x.fillStyle = 'rgba(255,255,255,0.72)';
    x.fillText(`· ${title.numeral} ·`, w / 2, 42);
  }
  // the title itself: engraved serif caps with a soft self-colored glow. Two
  // passes - a wide blurred halo, then the crisp face - reads as neon-lit stone.
  x.font = '700 108px Georgia, "Times New Roman", serif';
  const y0 = lines.length > 1 ? 138 : 188;
  for (let i = 0; i < lines.length; i++) {
    const ly = y0 + i * 122;
    x.shadowColor = col; x.shadowBlur = 42;
    x.fillStyle = col;
    x.fillText(lines[i], w / 2, ly);
    x.shadowBlur = 0;
    x.fillStyle = 'rgba(255,246,252,0.94)';
    x.fillText(lines[i], w / 2, ly);
  }
  return new THREE.CanvasTexture(c);
}

// A biome square for the draft room's roulette row: a dark rounded plate rimmed
// in the biome's line color with its glyph as the face. Placeholder identity
// only - when per-biome art lands at ART biomes/<id>.png it replaces the map
// in-place (see buildBiomeRow's loadTexture hook).
function makeBiomeSquareTex(item) {
  const s = 128, c = document.createElement('canvas'); c.width = c.height = s;
  const x = c.getContext('2d');
  const col = typeof item.color === 'number' ? intToHex(item.color) : (item.color || '#ff69b4');
  const r = 18;
  x.beginPath();
  x.moveTo(r, 3);
  x.arcTo(s - 3, 3, s - 3, s - 3, r);
  x.arcTo(s - 3, s - 3, 3, s - 3, r);
  x.arcTo(3, s - 3, 3, 3, r);
  x.arcTo(3, 3, s - 3, 3, r);
  x.closePath();
  x.fillStyle = 'rgba(12,7,18,0.85)'; x.fill();
  x.strokeStyle = col; x.lineWidth = 5; x.globalAlpha = 0.9; x.stroke(); x.globalAlpha = 1;
  x.textAlign = 'center'; x.textBaseline = 'middle';
  x.font = '64px "Segoe UI Emoji", "Segoe UI", sans-serif';
  x.fillText(item.glyph || '◈', s / 2, s / 2 + 4);
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
  #define ROOM_HOLES 4
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
  const api = { onCommit: null, onLinger: null, onVeinEnd: null, onRevealFx: null };
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

  // ---- a doorway prize card: the power-up IS the gatekeeper --------------------
  // Mirrors powerupDrops' card build (glow + tinted frame + CDN art with a
  // name-panel fallback) at doorway scale, plus a caption plate underneath with
  // the name + description. Returns a handle shaped like the spawner's detached
  // card (group / getContent / hide / dispose) so the shatter + teardown paths
  // are shared; getPickMeshes() lists the click-to-dive raycast targets.
  function buildRewardCard(reward, pos, quat, index) {
    // explicit frame/glow (draft-room boon themes) beat the kind mapping
    const frameCol = reward.frameCol != null ? reward.frameCol
      : (reward.kind === 'consumable' ? 0x66e0d0 : 0xc178ff);
    const glowCol = reward.glowCol != null ? reward.glowCol
      : (reward.frameCol != null
        ? new THREE.Color(reward.frameCol).lerp(new THREE.Color(0xffffff), 0.35).getHex()
        : (reward.kind === 'consumable' ? 0xa9f0e6 : 0xe0b3ff));
    const group = new THREE.Group();
    group.position.copy(pos);
    group.quaternion.copy(quat);
    group.scale.setScalar(PRIZE_SCALE);
    group.userData = { type: 'veinmouth', index };
    const w = 2.4, h = 3.05, yOff = 0.35; // powerupDrops' portrait card, lifted to make room for the caption
    const geo = new THREE.PlaneGeometry(1, 1);
    let disposed = false;

    const glowMat = new THREE.MeshBasicMaterial({
      map: makeGlowTex(), color: new THREE.Color(glowCol), transparent: true, opacity: 0.85,
      depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide });
    const glow = new THREE.Mesh(geo, glowMat);
    glow.scale.set(w * 1.6, h * 1.5, 1); glow.position.set(0, yOff, -0.08); group.add(glow);

    const frameMat = new THREE.MeshBasicMaterial({
      map: makeFrameTex(), color: new THREE.Color(frameCol), transparent: true,
      depthWrite: false, side: THREE.DoubleSide });
    const frame = new THREE.Mesh(geo, frameMat);
    frame.scale.set(w * 1.08, h * 1.08, 1); frame.position.set(0, yOff, 0.01); group.add(frame);

    const contentMat = new THREE.MeshBasicMaterial({
      map: makeNamePanelTex({ glyph: reward.glyph, name: reward.name, activeUse: reward.kind === 'consumable' }, frameCol),
      transparent: true, depthWrite: false, side: THREE.DoubleSide });
    const content = new THREE.Mesh(geo, contentMat);
    content.scale.set(w, h, 1); content.position.set(0, yOff, 0.02); group.add(content);
    if (reward.id) new THREE.TextureLoader().load(`${ART}boons/${reward.id}.png`, (tex) => {
      if (disposed) { tex.dispose(); return; }
      tex.colorSpace = THREE.SRGBColorSpace;
      const old = contentMat.map; contentMat.map = tex; contentMat.needsUpdate = true;
      if (old) old.dispose();
    }, undefined, () => { /* keep the name panel */ });   // no id (gold pouch) = name panel only

    const capMat = new THREE.MeshBasicMaterial({
      map: makeRewardCaptionTex(reward), transparent: true,
      depthWrite: false, side: THREE.DoubleSide });
    const caption = new THREE.Mesh(geo, capMat);
    caption.scale.set(2.75, 0.86, 1);
    caption.position.set(0, yOff - h / 2 - 0.5, 0.02);
    group.add(caption);

    scene.add(group);
    return {
      group,
      getContent: () => content,
      getPickMeshes: () => [content, frame, caption],
      hide() { group.visible = false; },
      dispose() {
        if (disposed) return;
        disposed = true;
        if (group.parent) group.parent.remove(group);
        geo.dispose();
        if (glowMat.map) glowMat.map.dispose(); glowMat.dispose();
        if (frameMat.map) frameMat.map.dispose(); frameMat.dispose();
        if (contentMat.map) contentMat.map.dispose(); contentMat.dispose();
        if (capMat.map) capMat.map.dispose(); capMat.dispose();
      },
    };
  }

  // ---- one vein: a diverging corridor attached to the chamber's far wall ------
  function buildVein(index, azDeg, desc, fr, C) {
    const col = new THREE.Color(desc.color);

    // peel direction: off the tangent by the door's azimuth, tilted down
    // (Explore's dir). A CENTER door (az 0) would run coplanar with the old
    // trunk and get shoved around by the anti-clip pass below - give it a
    // deliberately steeper plunge so it dives UNDER the trunk by design.
    const tilt = azDeg === 0 ? -0.5 : -0.18;
    const dir = fr.tangent.clone().multiplyScalar(Math.cos(deg2rad(azDeg)))
      .addScaledVector(fr.binormal, Math.sin(deg2rad(azDeg)))
      .add(new THREE.Vector3(0, tilt, 0)).normalize();
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
    // the S-wobble curls away from the spine on the door's side; a center door
    // has no side, so it just picks one
    const curlSign = azDeg === 0 ? (Math.random() < 0.5 ? 1 : -1) : Math.sign(azDeg);
    for (let i = 1; i <= N; i++) {
      const ph = i / N;
      const taper = 1 - sstep(0.72, 1.0, ph);              // straight tail
      const curl = Math.sin(ph * Math.PI * 1.6) * 0.05 * taper; // two soft reversals
      head.applyAxisAngle(fr.normal, curlSign * curl);
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

    // the doorway card IS the prize: the power-up's card art (same glow/frame/
    // art-with-fallback look as a drifting drop) fills the opening, with the
    // name + description on a caption plate underneath - the item is the
    // gatekeeper, not a video. Falls back to a media card only when the game
    // had no prize to offer (dock full / everything owned).
    // NOTE the extra 180-deg Y flip: mouthQuat points the plane's FRONT face
    // into the vein, so the parked camera used to see the BACK - which mirrored
    // every texture (nameplate text read backwards). The seal disc keeps the
    // unflipped quat (untextured, DoubleSide - orientation is moot).
    const cardPos = mouth.clone().addScaledVector(dir, CARD_INTO);
    const cardQuat = mouthQuat.clone()
      .multiply(new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), Math.PI));
    let card = null;
    if (desc.forceWall) {
      card = null;   // deliberately prizeless (starved offer): schedule() walls it off
    } else if (desc.reward) {
      try { card = buildRewardCard(desc.reward, cardPos, cardQuat, index); }
      catch (e) { console.warn('[dtrh] doorway prize card build failed:', e); card = null; }
    } else if (spawner && spawner.createDetachedCard) {
      card = spawner.createDetachedCard({ pos: cardPos, quat: cardQuat, scale: CARD_SCALE });
      if (card && card.group) {
        card.group.userData = { type: 'veinmouth', index };
        scene.add(card.group);   // the handle doesn't self-attach; the mouth owns it
      }
    }
    // A doorway with no real prize is no longer gold-pouched (2026-07): it gets
    // WALLED OFF instead. We flag it here and let schedule()'s wall-off pass
    // brick the hole in the room shader and drape a cluster of the user's gifs
    // over the dead-end - a prizeless branch reads as a sealed, plastered wall
    // rather than an open hole with a consolation pouch. The guard in schedule
    // rescues one door with a gold pouch only if EVERY branch came up prizeless,
    // so a room is never fully sealed.
    const wallCandidate = !card;

    return {
      index, desc, curve, dir: dir.clone(), mouth: mouth.clone(),
      tube, mat, tubeGeo, seal, sealGeo, sealMat,
      endDisc, endDiscGeo, endDiscMat, endRing, endRingGeo, endRingMat,
      card, cardPos, cardQuat, sealed: false, dying: false, swayT: Math.random() * 6,
      wallCandidate, walled: false, wallCards: null,
    };
  }

  function reveal(vein, a) {
    if (vein.dying) return;
    vein.mat.uniforms.uOpacity.value = a;
  }

  function destroyVein(vein) {
    if (vein.destroyed) return;   // loser is killed at commit, then teardown sweeps both
    vein.destroyed = true;
    scene.remove(vein.tube); scene.remove(vein.seal);
    vein.tubeGeo.dispose(); vein.mat.dispose();
    vein.sealGeo.dispose(); vein.sealMat.dispose();
    if (vein.endDisc) { scene.remove(vein.endDisc); vein.endDiscGeo.dispose(); vein.endDiscMat.dispose(); vein.endDisc = null; }
    scene.remove(vein.endRing); vein.endRingGeo.dispose(); vein.endRingMat.dispose();
    if (vein.card) { try { vein.card.dispose(); } catch (e) { /* ignore */ } }
    if (vein.wallCards) {
      for (const c of vein.wallCards) { try { c.dispose && c.dispose(); } catch (e) { /* ignore */ } }
      vein.wallCards = null;
    }
  }

  // ---- the antechamber sphere: built once per fork, holes aligned to the pipes
  function buildRoom(C, entryDir, veins) {
    const geo = new THREE.SphereGeometry(ROOM_R, 48, 32);
    // hole order: [entry, doorway 0, doorway 1, (doorway 2)] - commit() bricks
    // up losers by index, so keep this order in sync with closeDoor below.
    // The shader loops a fixed ROOM_HOLES(4); unused slots get cos 2.0 (never
    // discards, rim term evaluates to zero) so a 2-door fork wastes nothing.
    const dirs = [entryDir.clone()], coss = [ENTRY_HOLE_COS], rims = [new THREE.Color(0xff8fd8)];
    for (const v of veins) {
      dirs.push(v.dir.clone());
      coss.push(VEIN_HOLE_COS);
      rims.push(v.mat.uniforms.uRimColor.value.clone());
    }
    while (dirs.length < 4) { dirs.push(new THREE.Vector3(0, 1, 0)); coss.push(2.0); rims.push(new THREE.Color(0x000000)); }
    const mat = new THREE.ShaderMaterial({
      uniforms: {
        uTime: { value: 0 },
        uBg1: { value: new THREE.Color(0x2b1024) },   // tube palette: the room IS more tube
        uBg2: { value: new THREE.Color(0x160a18) },
        uLineColor: { value: new THREE.Color(0xff69b4) },
        uFogColor: { value: fogCol().clone() },
        uFogDensity: { value: FOG_DENSITY },
        uHoleDir: { value: dirs },
        uHoleCos: { value: coss },
        uHoleRim: { value: rims },
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

  // ---- the room title: big writing above the doorway mouths (draft mode) -----
  // Centered on the mean door azimuth and lifted above the mouths (the doors
  // carry a downward tilt; the title takes the same heading raised), so from
  // the camera's mid-chamber park it hangs well over the doors - the next room
  // announcing itself over its own entrances. Lift 0.42 is the sweet spot:
  // clearly higher than the old 0.28 (which sat right behind the center-screen
  // settle card) yet still under the biome roulette row (buildBiomeRow, lift
  // 0.80) - any higher and the engraving's top line kisses the tiles. The
  // title also DELAYS its fade-in until the settle card is done (see update).
  function buildTitle(C, veins, title, park) {
    const mean = new THREE.Vector3();
    for (const v of veins) mean.add(v.dir);
    if (mean.lengthSq() < 1e-6) return null;
    mean.normalize();
    const dir = mean.clone().add(new THREE.Vector3(0, 0.42, 0)).normalize();
    const tex = makeRoomTitleTex(title);
    tex.colorSpace = THREE.SRGBColorSpace;
    const W = 15.5, H = W * (340 / 1024);
    const geo = new THREE.PlaneGeometry(W, H);
    const mat = new THREE.MeshBasicMaterial({
      map: tex, transparent: true, opacity: 0,
      // depthTest off + a high renderOrder: the writing always draws OVER the
      // room wall instead of z-fighting / poking through it (2026-07 fix). It's
      // also pulled forward off the far wall (0.72 vs the old 0.93) so it floats
      // clearly inside the chamber rather than hugging the plaster.
      depthWrite: false, depthTest: false, side: THREE.DoubleSide,
    });
    const mesh = new THREE.Mesh(geo, mat);
    mesh.renderOrder = 900;
    mesh.position.copy(C).addScaledVector(dir, ROOM_R * 0.72);
    mesh.lookAt(park);   // face the camera's park just inside the entry
    scene.add(mesh);
    return {
      mesh, geo, mat, tex, t: 0,
      dispose() { scene.remove(mesh); geo.dispose(); mat.dispose(); tex.dispose(); },
    };
  }

  // The biome roulette: a row of 16 squares (rooms I..IV left to right, four
  // each) hung ABOVE the room title - the marquee over the writing. On linger a highlight
  // sweeps the row like a casino wheel - fast wraps easing into a heavy crawl -
  // and lands on the biome the doors actually lead into (pre-rolled at run
  // start; the game passes its index). The doors stay DEAD until it lands:
  // pickIndex / getPickables / skipDraft / the draft clock all key off
  // J.revealDone (see update's linger branch).
  function buildBiomeRow(C, veins, spec, park) {
    const mean = new THREE.Vector3();
    for (const v of veins) mean.add(v.dir);
    if (mean.lengthSq() < 1e-6) return null;
    mean.normalize();
    // above the title (0.42 at 0.72R): the row crowns the room like a marquee
    const dir = mean.clone().add(new THREE.Vector3(0, 0.80, 0)).normalize();
    const group = new THREE.Group();
    group.position.copy(C).addScaledVector(dir, ROOM_R * 0.72);
    group.lookAt(park);   // children (plane +Z) face the camera's park
    scene.add(group);
    const SIZE = 1.05, PITCH = 1.18;
    const squares = spec.items.map((item, i) => {
      const tex = makeBiomeSquareTex(item);
      tex.colorSpace = THREE.SRGBColorSpace;
      const geo = new THREE.PlaneGeometry(SIZE, SIZE);
      const mat = new THREE.MeshBasicMaterial({
        map: tex, transparent: true, opacity: 0,
        depthWrite: false, depthTest: false, side: THREE.DoubleSide,
      });
      const mesh = new THREE.Mesh(geo, mat);
      mesh.renderOrder = 899;   // over the wall, under the title (900)
      mesh.position.set((i - (spec.items.length - 1) / 2) * PITCH, 0, 0);
      group.add(mesh);
      return { mesh, geo, mat, tex, id: item.id, punch: 0, baseA: 0.8 };
    });
    // the sweep cursor: an additive glow riding behind the highlighted square;
    // doubles as the settle flash when the wheel lands
    const curTex = makeGlowTex();
    const curGeo = new THREE.PlaneGeometry(SIZE * 2.4, SIZE * 2.4);
    const curMat = new THREE.MeshBasicMaterial({
      map: curTex, transparent: true, opacity: 0, blending: THREE.AdditiveBlending,
      depthWrite: false, depthTest: false,
    });
    const cursor = new THREE.Mesh(curGeo, curMat);
    cursor.renderOrder = 898;
    cursor.position.set(squares[0].mesh.position.x, 0, -0.02);
    group.add(cursor);
    // hop schedule, fixed at build: 2-3 full wraps landing EXACTLY on the
    // target (steps % 16 === targetIndex). Gaps grow cubically ~40ms -> ~260ms
    // so it reads spin -> crawl -> land; total ~3-4.5s, then the draft clock
    // starts. A precomputed schedule can never wedge the run.
    const steps = 32 + spec.targetIndex;
    const times = []; let acc = 0.35;   // a beat to register the room first
    for (let i = 0; i <= steps; i++) { times.push(acc); acc += 0.04 + 0.22 * Math.pow(i / steps, 3); }
    const row = {
      group, squares, cursor, curMat, curGeo, curTex,
      target: spec.targetIndex, times, t: 0, step: -1,
      done: false, flare: 0, breatheT: 0, disposed: false,
      dispose() {
        row.disposed = true;
        scene.remove(group);
        // placeholder canvas textures are ours; swapped-in art belongs to the
        // shared assets cache (scene.js disposeTextures owns it) - leave it
        for (const s of squares) { s.geo.dispose(); s.mat.dispose(); s.tex.dispose(); }
        curGeo.dispose(); curMat.dispose(); curTex.dispose();
      },
    };
    // art hook, kicked off after `row` exists (a cached texture fires the
    // callback synchronously): silent fallback keeps the glyph placeholder
    for (const s of squares) {
      loadTexture(`${ART}biomes/${s.id}.png`, (t) => {
        if (row.disposed) return;
        s.mat.map = t; s.mat.needsUpdate = true;
      });
    }
    return row;
  }

  // brick up a doorway: cos threshold 2.0 can never be exceeded, so the wall
  // renders solid there (and the rim glow dies with it). Used on the LOSERS at
  // commit - their corridors are destroyed, and an open hole with nothing
  // behind it would leak the page background.
  function closeDoor(index) {
    if (!J || !J.room) return;
    J.room.mat.uniforms.uHoleCos.value[1 + index] = 2.0;
  }

  // Rescue a prizeless door with a flat 100-gold pouch so a room is never fully
  // sealed (used only when EVERY branch came up empty - see the wall-off pass).
  function rescueDoorWithGold(v) {
    if (!v) return;
    v.desc.reward = { kind: 'gold', amount: 100, name: '100 Gold', glyph: '🪙',
      desc: 'a pouch of gold, no questions asked. spend it at the dollhouse.',
      frameCol: 0xf2c14e, capCol: '#f2c14e' };
    try { v.card = buildRewardCard(v.desc.reward, v.cardPos, v.cardQuat, v.index); }
    catch (e) { console.warn('[dtrh] gold rescue card build failed:', e); v.card = null; }
    v.wallCandidate = false;
  }

  // Wall off a prizeless doorway: brick the hole in the room shader (solid wall,
  // no open portal), cap the mouth with its dark seal disc, then drape a cluster
  // of the user's gifs/photos over the dead-end so it reads as a plastered wall
  // rather than a bare hole. Decorative cards are NOT raycast doorway targets
  // (the mouth has no prize card) and ride the wall like posters.
  function wallOffDoor(v) {
    if (!v) return;
    v.walled = true;
    v.sealed = true;
    closeDoor(v.index);
    if (v.sealMat) v.sealMat.opacity = 0.92;
    v.wallCards = [];
    if (!(spawner && spawner.createDetachedCard)) return;
    const n = 3 + ((Math.random() * 2) | 0);   // 3-4 gifs plastered over the entrance
    for (let k = 0; k < n; k++) {
      const off = new THREE.Vector3((Math.random() - 0.5) * 3.4, (Math.random() - 0.5) * 3.4, 0)
        .applyQuaternion(v.cardQuat);
      const pos = v.mouth.clone().addScaledVector(v.dir, 0.4 + Math.random() * 0.5).add(off);
      const quat = v.cardQuat.clone()
        .multiply(new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 0, 1), (Math.random() - 0.5) * 0.5));
      // 'still' = gifs/photos only: a cluster of muted videos is heavy, and a
      // failed video prefetch used to leave the wall bare (an "empty" doorway)
      const c = spawner.createDetachedCard({ pos, quat, scale: CARD_SCALE * (0.5 + Math.random() * 0.3), prefer: 'still' });
      if (c && c.group) { c.group.userData = { type: 'walldress' }; scene.add(c.group); v.wallCards.push(c); }
    }
  }

  function teardown() {
    if (!J) return;
    if (J.phase === 'linger' && api.onLinger) { try { api.onLinger(false, J.mode); } catch (e) { /* ignore */ } }
    for (const v of J.veins) destroyVein(v);
    if (J.title) { try { J.title.dispose(); } catch (e) { /* ignore */ } J.title = null; }
    if (J.revealRow) { try { J.revealRow.dispose(); } catch (e) { /* ignore */ } J.revealRow = null; }
    if (J.room) { scene.remove(J.room.mesh); J.room.geo.dispose(); J.room.mat.dispose(); J.room = null; }
    if (tunnel && tunnel.clearHoles) tunnel.clearHoles();  // stale-state safety; v5 sets no wall holes
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

  // ---- commit: pick a door, dive the winner, kill the losers ------------------
  function commit(reason, pickedIndex) {
    if (!J || (J.phase !== 'approach' && J.phase !== 'linger')) return; // no re-commit mid-dive/handoff
    if (api.onLinger) { try { api.onLinger(false, J.mode); } catch (e) { /* ignore */ } }

    const n = J.veins.length;
    const timedOut = reason === 'timeout';
    const skipped = reason === 'skip';
    let index = null, forced = false, passive = false, overridden = false;
    // where the tube itself would go: the arrow-key vote steers it (leftmost/
    // rightmost door), otherwise the coaxed door takes the passive faller
    const tubesPick = () => {
      const vote = nav.getLaneVote();
      if (vote > VOTE_PICK) return n - 1;
      if (vote < -VOTE_PICK) return 0;
      passive = true; return J.coaxIndex;
    };
    if (J.presealIndex != null) {
      for (let i = 0; i < n; i++) if (i !== J.presealIndex) { index = i; break; }
      forced = true;
    } else if (reason === 'click' && J.autoPick && J.mode === 'fork') {
      // the 1-in-100 easter egg: on a surrender fork the tube picks the path
      // even OVER a click - your door was never yours to choose
      index = tubesPick();
      passive = false;
      overridden = index !== pickedIndex;
    } else if (pickedIndex != null) {
      index = clamp(pickedIndex, 0, n - 1);
    } else {
      index = tubesPick();
    }

    // never dive a sealed / walled / prizeless mouth (a passive faller or lane
    // vote could land on one): fall through to the first door that's actually
    // open. schedule()'s guard guarantees at least one such door exists.
    if (!J.veins[index] || J.veins[index].sealed || J.veins[index].walled || !J.veins[index].card) {
      const open = J.veins.findIndex((v) => v && !v.sealed && !v.walled && v.card && !v.dying);
      if (open >= 0) { index = open; forced = true; }
    }

    const winner = J.veins[index];
    // the losers stop loading immediately: dispose their veins + cards, brick up
    // their doorways (the winner's stays open - its corridor backs it)
    for (const v of J.veins) {
      if (v === winner) continue;
      v.dying = true;
      destroyVein(v);
      closeDoor(v.index);
    }

    // a draft timeout/skip takes no boon: the tube just carries you out the
    // door - no shatter (a shatter reads as "you got this card"). Hide the
    // card so the camera doesn't fly through its plane.
    if (J.mode === 'draft' && (timedOut || skipped)) {
      if (winner.card && winner.card.hide) winner.card.hide();
    } else {
      spawnShatter(winner);           // the chosen card shatters
    }
    nav.setJunctionArmed(false);
    nav.enterVein(winner.curve, { length: VEIN_LEN });

    // the title's moment is over - the dive owns the eye now
    if (J.title) { try { J.title.dispose(); } catch (e) { /* ignore */ } J.title = null; }
    if (J.revealRow) { try { J.revealRow.dispose(); } catch (e) { /* ignore */ } J.revealRow = null; }

    J.phase = 'trail';
    J.winner = winner;
    if (api.onCommit) {
      try {
        api.onCommit({
          index, side: index === 0 ? 'left' : 'right', branch: winner.desc,
          forced, passive, overridden, timedOut, skipped, mode: J.mode,
        });
      } catch (e) { /* ignore */ }
    }
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
    // approach carries the camera in through the entry hole, and the doorways
    // (with their cards) sit ~40-50 deg off-axis in comfortable head-on view.
    try { nav.setForwardHold(true, J.atDepth + STOP_IN); } catch (e) { /* ignore */ }
    if (api.onLinger) { try { api.onLinger(true, J.mode); } catch (e) { /* ignore */ } }
  }

  // The roulette wheel is spinning: advance its hops on the fixed schedule.
  // Runs INSTEAD of the draft clock (see update's linger branch), so the
  // auto-resume timer never eats the show. The landing hop index is
  // steps % 16 === target by construction.
  function tickRevealSweep(dt) {
    const row = J.revealRow;
    row.t += dt;
    while (row.step < row.times.length - 1 && row.t >= row.times[row.step + 1]) {
      row.step++;
      const s = row.squares[row.step % row.squares.length];
      s.punch = 1;
      row.cursor.position.x = s.mesh.position.x;
      const landed = row.step === row.times.length - 1;
      if (landed) {
        row.done = true;
        J.revealDone = true;   // the doors come alive; the draft clock starts
        row.flare = 1;
        s.punch = 1.6;         // the winner pops...
        for (let i = 0; i < row.squares.length; i++) {
          if (i !== row.target) row.squares[i].baseA = 0.3;   // ...the losers dim
        }
      }
      if (api.onRevealFx) { try { api.onRevealFx(landed ? 'settle' : 'tick'); } catch (e) { /* ignore */ } }
      if (landed) break;
    }
  }

  // Per-frame dressing for the roulette row: approach fade-up (same fog fade
  // as the title), punch decay on the swept squares, the winner's breathe and
  // the settle flare. Cheap - 16 scalar lerps.
  function tickRevealAnim(row, depth, dt) {
    const near = J.phase === 'approach'
      ? clamp((depth - (J.atDepth - J.lead)) / J.lead, 0, 1) : 1;
    row.breatheT += dt;
    for (let i = 0; i < row.squares.length; i++) {
      const s = row.squares[i];
      s.punch *= Math.exp(-6 * dt);
      s.mesh.scale.setScalar(1 + 0.4 * s.punch + (row.done && i === row.target ? 0.35 : 0));
      let a = s.baseA + 0.2 * s.punch;
      if (row.done && i === row.target) a = 0.9 + 0.1 * Math.sin(row.breatheT * 1.4);
      s.mat.opacity = near * Math.min(1, a);
    }
    if (row.flare > 0) row.flare = Math.max(0, row.flare - dt * 2.2);
    row.cursor.scale.setScalar(1 + 1.5 * row.flare);
    row.curMat.opacity = near * (row.step >= 0 ? (row.done ? 0.25 + 0.65 * row.flare : 0.85) : 0);
  }

  function update(depth, dt) {
    tickShatters(dt);
    if (!J) return;

    // track the live (region-tinted, animated) fog color so the veins + their
    // far-end covers fade to the SAME haze as the surround - no dark-purple void
    // in a tinted region.
    const fc = fogCol();

    // keep vein rings flowing + card sway alive
    for (const m of J.veins) {
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
    // the room title breathes: a slow opacity swell so the writing feels lit,
    // not painted. Its reveal tracks the phase (fog fade-up on approach).
    // With a roulette in the room the title HOLDS BACK until the settle card
    // has said its piece (the big center-screen biome reveal, ~5.2s): the two
    // announce the same moment and used to collide mid-screen. lingerT only
    // starts counting at settle, so the ramp below trails the card's fade.
    if (J.title) {
      J.title.t += dt;
      const breathe = 0.86 + 0.14 * Math.sin(J.title.t * 1.4);
      const near = J.phase === 'approach'
        ? clamp((depth - (J.atDepth - J.lead)) / J.lead, 0, 1) : 1;
      const hadReveal = !!(J.revealRow || J.revealDone === false);
      const afterCard = hadReveal ? clamp((J.lingerT - 4.2) / 1.6, 0, 1) : 1;
      J.title.mat.opacity = near * breathe * afterCard;
    }
    // the roulette row rides the same reveal (its hops advance in the linger
    // branch below; this is just the every-frame dressing)
    if (J.revealRow) tickRevealAnim(J.revealRow, depth, dt);

    if (J.phase === 'approach') {
      const near = clamp((depth - (J.atDepth - J.lead)) / J.lead, 0, 1);
      for (const m of J.veins) reveal(m, near);
      // begin the glide-in a touch short of the fork; the hold eases the camera
      // through the entry hole to its mid-chamber park
      if (depth >= J.atDepth - STOP_BACK) enterLinger();
    } else if (J.phase === 'linger') {
      for (const m of J.veins) reveal(m, 1);
      // no lean-to-commit: the player is free to look around, grab posters/cards/
      // power-ups. Entering a branch is a click on its card (pickIndex); the arrow-
      // key vote only steers where the timeout surrender goes.
      if (J.presealIndex === 0 && nav.getLaneVote() < 0) nav.resetLaneVote();
      else if (J.presealIndex === J.veins.length - 1 && nav.getLaneVote() > 0) nav.resetLaneVote();
      // the biome roulette spins FIRST: while it runs the doors are dead
      // (pickIndex/getPickables/skipDraft check revealDone) and the draft
      // clock holds at zero - the timer starts counting when the wheel lands.
      if (J.revealRow && !J.revealDone) {
        tickRevealSweep(dt);
      } else {
        J.lingerT += dt;
        // draft rooms run the game's draft timer; surrender forks (the 1-in-100
        // easter egg) + presealed forks (no real choice anyway) auto-commit at
        // 5s; every other fork waits for the player's click, with a long
        // failsafe so a fork whose cards never loaded can't wedge the run.
        const wait = J.mode === 'draft'
          ? (J.lingerSec > 0 ? J.lingerSec : LINGER_FAILSAFE)
          : (J.presealIndex != null || J.autoPick) ? LINGER_TIMEOUT : LINGER_FAILSAFE;
        if (J.lingerT >= wait) commit('timeout');
      }
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
    /** Arm a chamber ahead. branches = [{word, color, brand, reward, ...}] (2 or
     * 3 doors); coaxIndex = the door that takes a passive faller; presealIndex =
     * a door born shut (fork mode only). mode 'draft' stages the boon draft:
     * lingerSec is the game's draft timer (timeout dives the coaxed door with
     * skipped semantics), lead shortens the telegraph. */
    schedule({ atDepth, branches, coaxIndex = 0, presealIndex = null, mode = 'fork', lingerSec = 0, lead = LEAD, title = null, biomeReveal = null }) {
      if (!active) return;
      if (J) teardown();
      const descs = (branches || []).slice(0, 3);
      if (descs.length < 2) return;
      const fr = layout.frameAtDepth(atDepth);
      // chamber center: X_RING past the fork, so the trunk pierces the near wall
      // exactly at the fork depth (see the constants block)
      const C = fr.pos.clone().addScaledVector(fr.tangent, X_RING);
      const az = DOOR_AZIMUTHS[descs.length] || DOOR_AZIMUTHS[2];
      const veins = descs.map((d, i) => buildVein(i, az[i], d, fr, C));
      // the camera's eventual mid-chamber park: what the title plane faces
      const park = fr.pos.clone().addScaledVector(fr.tangent, STOP_IN);
      J = {
        phase: 'approach', atDepth, coaxIndex, presealIndex, mode, lingerSec, lead,
        winner: null, lingerT: 0,
        // the rare surrender fork (5s and the tube chooses - even over a click)
        autoPick: mode === 'fork' && Math.random() < AUTO_PICK_CHANCE,
        veins,
        room: buildRoom(C, fr.tangent.clone().negate(), veins),
        // the Journey Rooms: the next room's title over its doors (draft rooms)
        title: title && title.text ? buildTitle(C, veins, title, park) : null,
        revealRow: null, revealDone: true,
      };
      // the biome roulette (draft rooms with a rolled biome only): while it
      // spins, revealDone=false keeps the doors dead + the draft clock at zero.
      // No payload (fork mode, scripted runs) = today's behavior untouched.
      if (mode === 'draft' && biomeReveal && biomeReveal.items && biomeReveal.items.length
          && biomeReveal.targetIndex >= 0 && biomeReveal.targetIndex < biomeReveal.items.length) {
        J.revealRow = buildBiomeRow(C, veins, biomeReveal, park);
        J.revealDone = !J.revealRow;
      }
      applyCut(atDepth);   // hide the trunk through the chamber's span
      if (presealIndex != null && veins[presealIndex]) {
        // A door born shut gets the full wall-off (brick + gif drape): the old
        // bare seal read as an "empty" doorway - an open glowing hole with a
        // dark disc and nothing in it, neither prize nor plaster.
        const v = veins[presealIndex];
        if (v.card) { v.card.dispose(); v.card = null; }
        wallOffDoor(v);
      }
      // Wall off any prizeless doorway (2026-07): brick + drape gifs instead of
      // parking a consolation gold pouch in an open hole. Guard: a room must keep
      // at least one ENTERABLE door (not sealed, not walled, has a card) - if the
      // preseal + starved offers between them would leave none, rescue one door
      // (never the presealed one) with a gold pouch and re-open it.
      const enterable = (v) => v && !v.sealed && !v.walled && v.card;
      if (!veins.some(enterable)) {
        const rescue = veins.find((v, i) => v && i !== presealIndex) || veins[0];
        if (rescue) { rescue.sealed = false; if (rescue.sealMat) rescue.sealMat.opacity = 0; rescueDoorWithGold(rescue); }
      }
      veins.forEach((v) => { if (v && v.wallCandidate && !v.card && !v.sealed) wallOffDoor(v); });
      try { nav.resetLaneVote(); nav.setJunctionArmed(true); } catch (e) { /* ignore */ }
    },
    update,
    isBusy: () => !!J,
    /** raycast (scene.js) calls this when a doorway card is clicked. Dead while
     * the biome roulette is still spinning (revealDone gates every entry path). */
    pickIndex(index) {
      if (!J || J.phase !== 'linger' || !J.revealDone) return false;
      const v = J.veins[index];
      if (!v || v.sealed || v.walled || !v.card) return false; // can't enter a sealed / walled mouth
      commit('click', index);
      return true;
    },
    /** draft mode: the game's resist button - dive the coaxed door, no boon
     * (commit reports skipped:true; the game banks +1 resistance). */
    skipDraft() {
      if (!J || J.mode !== 'draft' || J.phase !== 'linger' || !J.revealDone) return false;
      commit('skip');
      return true;
    },
    /** draft mode: seconds left on the linger timer (null outside a draft
     * linger, and null while the roulette spins - the clock hasn't started). */
    getDraftSecondsLeft() {
      if (!J || J.mode !== 'draft' || J.phase !== 'linger' || !(J.lingerSec > 0) || !J.revealDone) return null;
      return Math.max(0, Math.ceil(J.lingerSec - J.lingerT));
    },
    /** draft mode: true while the biome roulette is still spinning (doors dead,
     * chrome held back - the game shows it on the 'settle' reveal FX). */
    isRevealPending() {
      return !!(J && !J.revealDone);
    },
    /** draft mode: a reroll re-deals the door cards in place - swap each vein's
     * desc + prize card and retint its rings + portal rim to the new boon. */
    setDraftCards(branches) {
      if (!J || J.mode !== 'draft' || (J.phase !== 'approach' && J.phase !== 'linger')) return;
      J.veins.forEach((v, i) => {
        const d = branches && branches[i];
        if (!d || v.dying) return;
        v.desc = d;
        if (v.card) { try { v.card.dispose(); } catch (e) { /* ignore */ } v.card = null; }
        if (d.reward) v.card = buildRewardCard(d.reward, v.cardPos, v.cardQuat, i);
        if (d.color) {
          v.mat.uniforms.uColor.value.set(d.color);
          v.mat.uniforms.uRimColor.value.set(d.color).lerp(new THREE.Color(0xffffff), 0.4);
          if (J.room) J.room.mat.uniforms.uHoleRim.value[1 + i].copy(v.mat.uniforms.uRimColor.value);
        }
        // match the linger's full reveal (a swapped card must not pop in dark)
        if (J.phase === 'linger') reveal(v, 1);
      });
    },
    /** meshes the scene raycasts against while a fork is open: each doorway
     * prize card's face/frame/caption (getPickMeshes) - NOT the whole group,
     * whose oversized glow plane (~1.6x the card) would steal clicks aimed at
     * grabbables beside the doorway. Clicking anywhere else must never commit
     * (it looks/grabs). Media-card fallback exposes its content plane only. */
    getPickables() {
      if (!J || J.phase !== 'linger' || !J.revealDone) return [];
      const out = [];
      for (const m of J.veins) {
        if (m.dying || m.sealed || !m.card) continue;
        if (m.card.getPickMeshes) out.push(...m.card.getPickMeshes());
        else if (m.card.getContent) { const c = m.card.getContent(); if (c) out.push(c); }
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
    /** onRevealFx('tick'|'settle') - the roulette's casino noises (the game
     * owns the sfx bridge; the engine just narrates the hops). */
    get onRevealFx() { return api.onRevealFx; },
    set onRevealFx(fn) { api.onRevealFx = fn; },
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
