/* ============================================================================
 * race/pixel.js - the big-pixel look for The Caucus Race.
 *
 * Renders the 3D scene at a low internal resolution and lets the browser
 * upscale it with nearest neighbour: `renderer.setPixelRatio(1 / block)` plus
 * `renderer.setSize(w, h, false)` keeps the canvas CSS size untouched, and
 * `image-rendering: pixelated` on the canvas does the blocky upscale. The DOM
 * HUD and the payloadFx media layers sit above the canvas, so they stay crisp.
 *
 * Textures in the scene get NearestFilter + no mipmaps while a block is on
 * (walk the scene once after build with retexture(scene); textures that load
 * later go through filterTexture(tex)); OFF restores what each texture had.
 *
 *   createPixelizer({ renderer, canvas, block }) ->
 *     { block, setBlock(n), cycle(), resize(w, h), filterTexture(tex), retexture(scene), label() }
 *
 * PIXEL_STEPS is the P-key cycle: off, 2, 3, 4, 6 screen pixels per block.
 * ==========================================================================*/

import * as THREE from 'three';
import { Q } from '../shared/quality.js';

export const PIXEL_STEPS = [0, 2, 3, 4, 6];
export const PIXEL_DEFAULT = 3;
const MAP_KEYS = ['map', 'emissiveMap', 'alphaMap', 'normalMap', 'roughnessMap', 'metalnessMap', 'aoMap'];

/** Snap any input to a step: 0 (off) or the nearest listed block size. */
export function normalizeBlock(n) {
  const v = Number(n);
  if (!isFinite(v) || v <= 1) return 0;
  let best = PIXEL_STEPS[1];
  for (const s of PIXEL_STEPS) if (s > 0 && Math.abs(s - v) < Math.abs(best - v)) best = s;
  return best;
}

export function createPixelizer({ renderer, canvas, block = PIXEL_DEFAULT }) {
  let cur = normalizeBlock(block);
  let w = 1, h = 1;
  const nativeDpr = () => Math.min(window.devicePixelRatio || 1, Q.maxDpr, 1.5);

  function apply() {
    renderer.setPixelRatio(cur > 0 ? 1 / cur : nativeDpr());
    renderer.setSize(w, h, false);
    if (canvas) canvas.style.imageRendering = cur > 0 ? 'pixelated' : '';
  }
  function resize(width, height) { w = Math.max(1, width | 0); h = Math.max(1, height | 0); apply(); }
  function setBlock(n) { cur = normalizeBlock(n); apply(); return cur; }
  function cycle() {
    const i = PIXEL_STEPS.indexOf(cur);
    return setBlock(PIXEL_STEPS[(i + 1) % PIXEL_STEPS.length]);
  }

  /** Apply the current mode to one texture (remembers its original filters the first time). */
  function filterTexture(tex) {
    if (!tex || !tex.isTexture) return;
    const ud = tex.userData || (tex.userData = {});
    if (!ud.pxOrig) ud.pxOrig = { mag: tex.magFilter, min: tex.minFilter, mip: tex.generateMipmaps };
    const o = ud.pxOrig;
    const mag = cur > 0 ? THREE.NearestFilter : o.mag;
    const min = cur > 0 ? THREE.NearestFilter : o.min;
    const mip = cur > 0 ? false : o.mip;
    if (tex.magFilter === mag && tex.minFilter === min && tex.generateMipmaps === mip) return;
    tex.magFilter = mag; tex.minFilter = min; tex.generateMipmaps = mip;
    tex.needsUpdate = true;
  }
  /** Walk every material in the scene (maps + shader sampler uniforms). */
  function retexture(scene) {
    if (!scene || !scene.traverse) return;
    scene.traverse((o) => {
      const mats = Array.isArray(o.material) ? o.material : o.material ? [o.material] : [];
      for (const m of mats) {
        for (const k of MAP_KEYS) if (m[k]) filterTexture(m[k]);
        if (m.uniforms) for (const u of Object.values(m.uniforms)) if (u && u.value && u.value.isTexture) filterTexture(u.value);
      }
    });
  }

  return {
    get block() { return cur; },
    setBlock, cycle, resize, filterTexture, retexture,
    label() { return cur > 0 ? `pixels ${cur}` : 'pixels off'; },
  };
}

// self-check: node --check is the bar; normalizeBlock(2.6) -> 3, normalizeBlock('0') -> 0, normalizeBlock(9) -> 6.
