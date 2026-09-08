/* ============================================================================
 * board/outline.js - the comic-book line around every man.
 *
 * The men are pink on a pink board and read as blobs from a distance. This
 * draws a thin pink line around each one the way a comic inks a figure: an
 * INVERTED HULL. Every mesh a piece owns (body, crown, tiara) gets a twin that
 * shares its geometry, is drawn back-faces-only and is pushed out along the
 * normal by a screen-constant width, so the line is about two pixels wide
 * whether the man is near the camera or across the board. No post-processing
 * pass; the effects layer may add bloom later and a composer here would fight.
 *
 * The hull bends with the man. jiggle.js parks its spring uniforms on every
 * material it installs (material.userData.pbpJiggle), and the hull's shader
 * binds THOSE objects, so a squashed king wears a squashed outline for free.
 * The vertex code mirrors jiggle's BEND_VERTEX and BEND_NORMAL, built from the
 * same TUNING numbers; if jiggle ever exports its chunks, import them here.
 *
 * Wiring, all optional:
 *   boot.js   createOutline({ group, bus }) and update(camera, renderer) once a
 *             frame after jiggle.update, since it reads the flex uniforms
 *   drag.js   sets piece.userData.held; the hull thickens and brightens for it
 *   bus       'check' {side}: the checked king's line flickers for a beat
 *   settings  window.PBP.settings.outline (default true) turns it off
 * ==========================================================================*/

import * as THREE from 'three';
import { TUNING as J } from './jiggle.js';

/** Every number that decides how the line looks. */
export const TUNING = Object.freeze({
  widthPx: 2.0,             // rest width on screen, any distance
  heldWidthPx: 3.6,         // while the man is in the hand
  colorWhite: 0xFF2D95,     // white men: hotter than the board's pink so it reads on it
  colorBlack: 0xB0157A,     // black men: deep magenta, reads on cream and on pink
  heldColor: 0xFFC2E4,      // both sides brighten toward this in the hand
  hoverWidthPx: 2.6,        // the man under the cursor (drag.js sets userData.hover)
  hoverMix: 0.4,            // and how far toward heldColor he brightens
  flickerColor: 0xFFFFFF,   // check: the king's line snaps between his colour and this
  flickerHz: 9,
  flickerSec: 0.7,
  ease: 14,                 // width and colour follow their targets at this rate (1/s)
  cacheKey: 'pbp-hull-1',
});

const T = TUNING;
const f = (n) => (Number.isInteger(n) ? n.toFixed(1) : String(n));

// The same flex jiggle.js applies to the visible skin, applied to the hull's
// copy of the geometry, plus the bent normal it needs to push out along.
const PRELUDE = `
uniform vec2 uBend;
uniform float uSquash;
uniform float uHeight;
uniform float uPhase;
uniform float uTime;
uniform float uPixel;
uniform float uWidth;
uniform float uSign;
float pbpH(float y) { return clamp(y / max(uHeight, 0.0001), 0.0, 1.0); }
`;

const HULL_VERTEX = `#include <begin_vertex>
vec3 pbpN = normal;
{
  float h = pbpH(position.y);
  float wb = pow(h, ${f(J.bendWeightPow)});
  float ws = pow(h, ${f(J.squashWeightPow)});
  vec2 wave = uBend * (sin(h * ${f(J.rippleWaves)} - uTime * ${f(J.rippleSpeed)} + uPhase) * ${f(J.rippleGain)});
  vec2 off = (uBend + wave) * wb;
  transformed.y *= (1.0 - uSquash * ws);
  transformed.xz *= (1.0 + ${f(J.volumeGain)} * uSquash);
  transformed.x += off.x;
  transformed.z += off.y;
  vec2 slope = uBend * (${f(J.bendWeightPow)} * pow(h, ${f(J.bendWeightPow - 1)}) / max(uHeight, 0.0001));
  pbpN.y -= slope.x * pbpN.x + slope.y * pbpN.z;
  float sy = max(1.0 - uSquash * pow(h, ${f(J.squashWeightPow)}), 0.05);
  float sxz = max(1.0 + ${f(J.volumeGain)} * uSquash, 0.05);
  pbpN = normalize(vec3(pbpN.x / sxz, pbpN.y / sy, pbpN.z / sxz));
}`;

// Pushed out in VIEW space by (pixels x world-per-pixel-at-1m x distance), so
// the line is the same width on the near pawn and the far king, and so the
// object's own scale (the lathe placeholders are scaled) does not matter.
const HULL_PROJECT = `
vec4 mvPosition = modelViewMatrix * vec4(transformed, 1.0);
vec3 pbpVN = normalize(normalMatrix * pbpN);
mvPosition.xyz += pbpVN * (uSign * uPixel * uWidth * max(-mvPosition.z, 0.05));
gl_Position = projectionMatrix * mvPosition;`;

/**
 * True when a geometry's normals point INTO the body. Some of the exported
 * glbs (king, queen, rook as of 0908) came out of Blender inside-out: their
 * winding and normals agree with each other and both face inward, so the skin
 * shows its far side and a back-face hull would draw its near side over the
 * man. Lane H flips those in pieces.js; until then, and after, the hull
 * mirrors itself for such a mesh so the line reads either way. Measured once
 * per geometry: the sign of normal . (position - axis) over every vertex, which
 * is exact for a body turned around +Y and good enough for the leaning ones.
 */
function insideOut(geometry) {
  if (geometry.userData.pbpInsideOut != null) return geometry.userData.pbpInsideOut;
  const pos = geometry.attributes.position;
  const nor = geometry.attributes.normal;
  let out = 0;
  if (pos && nor) {
    if (!geometry.boundingBox) geometry.computeBoundingBox();
    const cy = (geometry.boundingBox.max.y + geometry.boundingBox.min.y) / 2;
    for (let i = 0; i < pos.count; i++) {
      if (pos.getX(i) * nor.getX(i) + (pos.getY(i) - cy) * nor.getY(i) + pos.getZ(i) * nor.getZ(i) > 0) out++;
    }
  }
  geometry.userData.pbpInsideOut = pos && nor ? out < pos.count * 0.5 : false;
  return geometry.userData.pbpInsideOut;
}

function enabled() {
  const s = typeof window !== 'undefined' && window.PBP && window.PBP.settings;
  return !s || s.outline !== false;
}

export function createOutline({ group, bus = null }) {
  const hulls = new Map();          // piece root -> { meshes, mats, color, flicker }
  const uPixel = { value: 0 };      // world units per pixel at distance 1, shared
  const tmpColor = new THREE.Color();
  const hoverColor = new THREE.Color(T.heldColor);
  let flickering = [];

  /** The flex uniforms jiggle installed on this man, or none if he has none. */
  function flexOf(piece) {
    const list = piece.userData.materials || [];
    for (const m of list) if (m && m.userData && m.userData.pbpJiggle) return m.userData.pbpJiggle;
    let found = null;
    piece.traverse((o) => {
      if (found || !o.isMesh) return;
      const mats = Array.isArray(o.material) ? o.material : [o.material];
      for (const m of mats) if (m && m.userData && m.userData.pbpJiggle) { found = m.userData.pbpJiggle; break; }
    });
    return found;
  }

  function hullMaterial(u, color, flipped = false) {
    const mat = new THREE.MeshBasicMaterial({ color, side: flipped ? THREE.FrontSide : THREE.BackSide, fog: false, toneMapped: false });
    const own = { uWidth: { value: T.widthPx }, uSign: { value: flipped ? -1 : 1 } };
    mat.userData.pbpHull = own;
    mat.onBeforeCompile = (shader) => {
      // Share the spring's objects: the hull reads the same bend the skin does.
      shader.uniforms.uBend = u.uBend;
      shader.uniforms.uSquash = u.uSquash;
      shader.uniforms.uHeight = u.uHeight;
      shader.uniforms.uPhase = u.uPhase;
      shader.uniforms.uTime = u.uTime;
      shader.uniforms.uPixel = uPixel;
      shader.uniforms.uWidth = own.uWidth;
      shader.uniforms.uSign = own.uSign;
      shader.vertexShader = PRELUDE + shader.vertexShader
        .replace('#include <begin_vertex>', HULL_VERTEX)
        .replace('#include <project_vertex>', HULL_PROJECT);
    };
    mat.customProgramCacheKey = () => T.cacheKey;
    return mat;
  }

  /** Dress one man with his line. Idempotent. */
  function attach(piece) {
    if (hulls.has(piece)) return;
    const u = flexOf(piece);
    if (!u) return;                      // not jiggled yet; try again next frame
    const base = piece.userData.side === 'b' ? T.colorBlack : T.colorWhite;
    const meshes = [];
    const mats = [];
    const skins = [];
    const sources = [];
    // The contact patch is a picture of shade lying on the board, not a part of
    // the man: it stays welded to the board while he rises, and a hull pinned to
    // his root would ride up with him and show as a flat square of line colour.
    const patch = piece.userData.contact || null;
    piece.traverse((o) => { if (o.isMesh && o.geometry && !o.userData.pbpHull && o !== patch) sources.push(o); });
    for (const src of sources) {
      const mat = hullMaterial(u, base, insideOut(src.geometry));
      const hull = new THREE.Mesh(src.geometry, mat);
      hull.userData.pbpHull = true;
      hull.castShadow = false;
      hull.receiveShadow = false;
      hull.raycast = () => {};          // the line is never something you can grab
      hull.renderOrder = -1;            // inked first; the skin paints over the inside
      hull.position.copy(src.position);
      hull.rotation.copy(src.rotation);
      hull.scale.copy(src.scale);
      src.parent.add(hull);
      meshes.push(hull);
      mats.push(mat);
      skins.push(Array.isArray(src.material) ? src.material[0] : src.material);
    }
    hulls.set(piece, { meshes, mats, skins, base, width: T.widthPx, color: new THREE.Color(base), flipped: sources.filter((s) => insideOut(s.geometry)).length });
  }

  function detach(piece) {
    const h = hulls.get(piece);
    if (!h) return;
    for (const m of h.meshes) { if (m.parent) m.parent.remove(m); }
    for (const m of h.mats) m.dispose();
    hulls.delete(piece);
  }

  /** The checked king's line snaps white for a beat. */
  function flicker(piece) {
    if (!piece || !hulls.has(piece)) return;
    flickering = flickering.filter((x) => x.piece !== piece);
    flickering.push({ piece, t: 0 });
  }

  /** Give one man's line another colour (`null` puts his side's back). The poses use it. */
  function setBase(piece, hex) {
    const h = piece && hulls.get(piece);
    if (!h) return;
    h.base = hex == null ? (piece.userData.side === 'b' ? T.colorBlack : T.colorWhite) : hex;
  }

  function kingOf(side) {
    for (const p of group.children) {
      if (p.userData && p.userData.type === 'k' && p.userData.side === side) return p;
    }
    return null;
  }

  let unsub = null;
  if (bus && typeof bus.on === 'function') {
    unsub = bus.on('check', (p) => flicker(kingOf(p && p.side === 'b' ? 'b' : 'w')));
  }

  /**
   * Once a frame, after jiggle.update. `camera` and `renderer` give the pixel
   * size; men built since the last frame get their line here.
   */
  function update(dt, camera, renderer) {
    const on = enabled();
    if (camera && renderer) {
      const h = renderer.domElement.height || 1;       // drawing-buffer pixels
      const p11 = camera.projectionMatrix.elements[5] || 1;
      uPixel.value = (2 / (p11 * h)) * (renderer.getPixelRatio ? renderer.getPixelRatio() : 1);
    }
    for (const piece of group.children) if (!hulls.has(piece)) attach(piece);
    for (const [piece, h] of hulls) if (!piece.parent) detach(piece);

    const k = 1 - Math.exp(-T.ease * (dt || 0.016));
    for (let i = flickering.length - 1; i >= 0; i--) {
      flickering[i].t += dt;
      if (flickering[i].t >= T.flickerSec) flickering.splice(i, 1);
    }
    for (const [piece, h] of hulls) {
      const held = !!piece.userData.held;
      const hover = !held && !!piece.userData.hover;   // the man under the cursor, one step up
      const flick = flickering.find((x) => x.piece === piece);
      const wantWidth = held ? T.heldWidthPx : hover ? T.hoverWidthPx : T.widthPx;
      tmpColor.setHex(held ? T.heldColor : h.base);
      if (hover) tmpColor.lerp(hoverColor, T.hoverMix);
      if (flick) {
        const onBeat = Math.floor(flick.t * T.flickerHz * 2) % 2 === 0;
        if (onBeat) tmpColor.setHex(T.flickerColor);
      }
      h.width += (wantWidth - h.width) * k;
      if (flick) h.color.copy(tmpColor); else h.color.lerp(tmpColor, k);
      const opacity = h.skins[0] ? h.skins[0].opacity : 1;
      for (let i = 0; i < h.meshes.length; i++) {
        const mesh = h.meshes[i];
        const mat = h.mats[i];
        mesh.visible = on;
        mat.color.copy(h.color);
        mat.userData.pbpHull.uWidth.value = h.width;
        if (opacity < 1) { mat.transparent = true; mat.opacity = opacity; }
        else if (mat.transparent) { mat.transparent = false; mat.opacity = 1; }
      }
    }
  }

  return {
    update, attach, detach, flicker, setBase,
    /** For the harness: how many men are inked and what the line is doing. */
    stats() {
      let held = 0;
      for (const [piece] of hulls) if (piece.userData.held) held++;
      const all = [...hulls.values()];
      return { pieces: hulls.size, meshes: all.reduce((n, h) => n + h.meshes.length, 0), insideOut: all.reduce((n, h) => n + h.flipped, 0), held, flickering: flickering.length, enabled: enabled(), pixel: uPixel.value };
    },
    dispose() {
      if (unsub) unsub();
      for (const piece of [...hulls.keys()]) detach(piece);
    },
  };
}
