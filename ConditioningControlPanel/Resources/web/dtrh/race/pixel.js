/* ============================================================================
 * race/pixel.js - the big-pixel look for Racing Thoughts, with a crisp layer.
 *
 * Two-pass render while a block size is on:
 *   (a) the WORLD (THREE layer 0) renders into a low-resolution render target
 *       sized canvas / block (nearest filtering, no mipmaps, a depth texture);
 *   (b) a fullscreen blit upscales it onto the full-resolution canvas with
 *       nearest sampling and writes gl_FragDepth from the depth texture, so
 *       whatever draws next still occludes correctly; the blit also fades the
 *       far tunnel to the fog colour past FAR_FADE metres (a vanishing point,
 *       not a ring tangle);
 *   (c) the CRISP layer (THREE layer 1: bubble sprites, pop shards, the wall
 *       media posters) renders on top at full resolution, autoClear off.
 * Off (block 0) is one ordinary full-res render with every layer on. The DOM
 * HUD and the payloadFx media layers sit above the canvas, so they stay crisp.
 *
 * Textures in the scene get NearestFilter + no mipmaps while a block is on
 * (walk the scene once after build with retexture(scene); textures that load
 * later go through filterTexture(tex)); OFF restores what each texture had.
 *
 *   createPixelizer({ renderer, canvas, block, log }) ->
 *     { block, setBlock(n), cycle(), resize(w, h), render(scene, camera),
 *       filterTexture(tex), retexture(scene), label(), stats, dispose() }
 *
 * PIXEL_STEPS is the P-key cycle: off, 2, 3, 4, 6 screen pixels per block, and
 * pixelDefault() is where a player who never touched the option starts: off on
 * glass, the smallest block on a mouse.
 * Put an object on the crisp layer with `obj.layers.set(CRISP_LAYER)`.
 * `stats` is last frame's draw calls + triangles summed over the passes plus
 * the frame time (avg + p95 ms over the last FRAME_WIN frames); with `log`
 * given, a line goes out every PERF_LOG_SEC seconds. THE GOVERNOR: on a
 * high-DPI screen an average frame above SLOW_MS drops the canvas to one
 * device pixel per CSS pixel, and one under FAST_MS restores the native ratio.
 * ==========================================================================*/

import * as THREE from 'three';
import { Q } from '../shared/quality.js';

export const PIXEL_STEPS = [0, 2, 3, 4, 6];
/** The smallest block there is, `off` aside. This is the default a mouse gets. */
export const PIXEL_MIN = PIXEL_STEPS[1];
/**
 * THE OLD DEFAULT, kept for one job: the migration in race/menu.js loadOptions. Everybody who never
 * opened the option still has this number written into `race.options` (the menu saves the whole
 * table whatever the player touched), so a saved 3 is read there as "never chosen" and takes the new
 * default instead. Nothing else may read it: what a fresh page gets is pixelDefault() below.
 */
export const PIXEL_LEGACY_DEFAULT = 3;
export const CRISP_LAYER = 1;
const WORLD_MASK = 1 << 0, CRISP_MASK = 1 << CRISP_LAYER, ALL_MASK = WORLD_MASK | CRISP_MASK;
const MAP_KEYS = ['map', 'emissiveMap', 'alphaMap', 'normalMap', 'roughnessMap', 'metalnessMap', 'aoMap'];
const FAR_FADE = [42, 80];     // metres: the world pass fades to the fog colour across this band
const PERF_LOG_SEC = 5;
const FRAME_WIN = 300;         // frames kept for the avg / p95
const SLOW_MS = 24, FAST_MS = 13;   // governor thresholds on the avg frame (about 42 and 77 fps)

/**
 * THE DEFAULT BLOCK, which is not one number any more. On glass the big pixels cost the most and
 * read the worst: a phone panel is already tiny, and a 3 px block on it turned the road to mush
 * (the owner, phone testing 2026-09-09). So a COARSE POINTER starts with the look OFF and a fine
 * one starts at PIXEL_MIN, the gentlest block on the cycle. The option row still walks the whole
 * range either way: this decides where a player who never touched it begins, nothing more.
 *
 * The test is `(pointer: coarse)` and only that - a Windows laptop with a touchscreen has a mouse
 * as well, reads as fine, and is a pc. `?coarse=1` / `?coarse=0` force the answer for a headless
 * check (race/smoke/popped-check.mjs), the way race/touch.js takes `?touch=`.
 */
export function pixelDefault(win) {
  const w = win || (typeof window !== 'undefined' ? window : null);
  if (!w) return PIXEL_MIN;
  try {
    const q = new URLSearchParams(w.location.search).get('coarse');
    if (q === '1' || q === 'on') return 0;
    if (q === '0' || q === 'off') return PIXEL_MIN;
  } catch (e) { /* no location, or a page that hides it: fall through to the device test */ }
  try { if (typeof w.matchMedia === 'function' && w.matchMedia('(pointer: coarse)').matches) return 0; }
  catch (e) { /* no matchMedia: a mouse is the safer guess */ }
  return PIXEL_MIN;
}

/** Snap any input to a step: 0 (off) or the nearest listed block size. */
export function normalizeBlock(n) {
  const v = Number(n);
  if (!isFinite(v) || v <= 1) return 0;
  let best = PIXEL_STEPS[1];
  for (const s of PIXEL_STEPS) if (s > 0 && Math.abs(s - v) < Math.abs(best - v)) best = s;
  return best;
}

// The blit: one triangle over the whole canvas. Colour is nearest-sampled from the low-res
// target; depth comes back out through gl_FragDepth (WebGL2 only) so the crisp pass can
// depth-test against the world. <colorspace_fragment> encodes for the canvas the way every
// three material does (the target stores sRGB, sampling hands back linear).
const BLIT_VERT = `
  varying vec2 vUv;
  void main() { vUv = position.xy * 0.5 + 0.5; gl_Position = vec4(position.xy, 0.0, 1.0); }`;
const BLIT_FRAG = `
  #include <packing>
  uniform sampler2D tDiffuse;
  uniform sampler2D tDepth;
  uniform vec3 uFogColor;
  uniform vec2 uFade;
  uniform float uNear, uFar;
  varying vec2 vUv;
  void main() {
    vec4 c = texture2D(tDiffuse, vUv);
    #ifdef WRITE_DEPTH
      float z = texture2D(tDepth, vUv).r;
      gl_FragDepth = z;
      float dist = -perspectiveDepthToViewZ(z, uNear, uFar);
      c.rgb = mix(c.rgb, uFogColor, smoothstep(uFade.x, uFade.y, dist));
    #endif
    gl_FragColor = vec4(c.rgb, 1.0);
    #include <colorspace_fragment>
  }`;

export function createPixelizer({ renderer, canvas, block = pixelDefault(), log = null }) {
  let cur = normalizeBlock(block);
  let w = 1, h = 1;
  let dprCap = Infinity;         // the governor's lid on the device pixel ratio (1 while slow)
  const screenDpr = () => Math.min(window.devicePixelRatio || 1, Q.maxDpr, 1.5);
  const nativeDpr = () => Math.min(screenDpr(), dprCap);
  const caps = renderer.capabilities || {};
  const depthOk = caps.isWebGL2 !== false;   // r163+ is WebGL2 only; the guard keeps an older fork honest

  // ---- the low-res target + the blit ------------------------------------------------------
  let rt = null, rtW = 0, rtH = 0;
  const blitGeo = new THREE.BufferGeometry();
  blitGeo.setAttribute('position', new THREE.Float32BufferAttribute([-1, -1, 0, 3, -1, 0, -1, 3, 0], 3));
  const blitMat = new THREE.ShaderMaterial({
    uniforms: { tDiffuse: { value: null }, tDepth: { value: null }, uFogColor: { value: new THREE.Color(0) },
      uFade: { value: new THREE.Vector2(FAR_FADE[0], FAR_FADE[1]) }, uNear: { value: 0.1 }, uFar: { value: 400 } },
    vertexShader: BLIT_VERT, fragmentShader: BLIT_FRAG,
    depthTest: true, depthWrite: true, depthFunc: THREE.AlwaysDepth, fog: false, lights: false,
  });
  if (depthOk) blitMat.defines.WRITE_DEPTH = 1;
  const blitScene = new THREE.Scene();
  const blitMesh = new THREE.Mesh(blitGeo, blitMat);
  blitMesh.frustumCulled = false;
  blitScene.add(blitMesh);
  const blitCam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);

  function dropTarget() {
    if (!rt) return;
    if (rt.depthTexture) rt.depthTexture.dispose();
    rt.dispose(); rt = null; rtW = rtH = 0;
  }
  function ensureTarget() {
    const tw = Math.max(1, Math.ceil(w / cur)), th = Math.max(1, Math.ceil(h / cur));
    if (rt && tw === rtW && th === rtH) return;
    dropTarget();
    rtW = tw; rtH = th;
    const depthTexture = depthOk ? new THREE.DepthTexture(tw, th, THREE.UnsignedIntType) : null;
    rt = new THREE.WebGLRenderTarget(tw, th, {
      minFilter: THREE.NearestFilter, magFilter: THREE.NearestFilter, generateMipmaps: false,
      depthBuffer: true, stencilBuffer: false, depthTexture, colorSpace: THREE.SRGBColorSpace,
    });
    blitMat.uniforms.tDiffuse.value = rt.texture;
    blitMat.uniforms.tDepth.value = depthTexture;
  }

  function apply() {
    renderer.setPixelRatio(nativeDpr());
    renderer.setSize(w, h, false);
    if (canvas) canvas.style.imageRendering = '';   // the blit does the nearest upscale now
    if (cur > 0) ensureTarget(); else dropTarget();
  }
  function resize(width, height) { w = Math.max(1, width | 0); h = Math.max(1, height | 0); apply(); }
  function setBlock(n) { cur = normalizeBlock(n); apply(); return cur; }
  function cycle() {
    const i = PIXEL_STEPS.indexOf(cur);
    return setBlock(PIXEL_STEPS[(i + 1) % PIXEL_STEPS.length]);
  }

  // ---- the frame ---------------------------------------------------------------------------
  const stats = { calls: 0, triangles: 0, passes: 0, frameMs: 0, frameP95: 0 };
  let fCalls = 0, fTris = 0, fPasses = 0, perfLast = 0;
  const info = renderer.info;
  function tally() { if (!info || !info.render) return; fCalls += info.render.calls; fTris += info.render.triangles; fPasses++; }
  // frame gaps in a ring; a gap over 250 ms is a tab switch or a hitch, not a frame
  const gaps = new Float32Array(FRAME_WIN);
  let gapN = 0, gapI = 0, lastFrameAt = 0;
  const sorted = new Float32Array(FRAME_WIN);
  function frameStats() {
    if (!gapN) return { avg: 0, p95: 0 };
    let sum = 0;
    for (let i = 0; i < gapN; i++) { sum += gaps[i]; sorted[i] = gaps[i]; }
    const view = sorted.subarray(0, gapN); view.sort();
    return { avg: sum / gapN, p95: view[Math.min(gapN - 1, Math.floor(gapN * 0.95))] };
  }
  function govern(avg) {
    if (gapN < 60) return;
    const native = screenDpr();
    if (avg > SLOW_MS && dprCap > 1 && native > 1) { dprCap = 1; apply(); if (log) log(`[race-perf] governor: dpr 1 (avg frame ${avg.toFixed(1)} ms)`); }
    else if (avg < FAST_MS && dprCap < native) { dprCap = Infinity; apply(); if (log) log(`[race-perf] governor: dpr native (avg frame ${avg.toFixed(1)} ms)`); }
  }
  function closeFrame() {
    stats.calls = fCalls; stats.triangles = fTris; stats.passes = fPasses;
    fCalls = fTris = fPasses = 0;
    const nowMs = performance.now();
    if (lastFrameAt) { const g = nowMs - lastFrameAt; if (g < 250) { gaps[gapI] = g; gapI = (gapI + 1) % FRAME_WIN; if (gapN < FRAME_WIN) gapN++; } }
    lastFrameAt = nowMs;
    const now = nowMs / 1000;
    if (!perfLast) perfLast = now;
    if (now - perfLast < PERF_LOG_SEC) return;
    perfLast = now;
    const { avg, p95 } = frameStats();
    stats.frameMs = avg; stats.frameP95 = p95;
    govern(avg);
    if (!log) return;
    try { log(`[race-perf] t+${Math.round(now)}s calls ${stats.calls} tris ${stats.triangles} frame ${avg.toFixed(1)}ms p95 ${p95.toFixed(1)} dpr ${renderer.getPixelRatio().toFixed(2)} ${label()}${rt ? ` rt ${rtW}x${rtH}` : ''}`); } catch (e) { /* host gone */ }
  }

  function render(scene, camera) {
    if (cur === 0 || !rt) {
      camera.layers.mask = ALL_MASK;
      renderer.setRenderTarget(null); renderer.autoClear = true;
      renderer.render(scene, camera); tally();
      closeFrame();
      return;
    }
    // (a) the world, low-res
    camera.layers.mask = WORLD_MASK;
    renderer.setRenderTarget(rt); renderer.autoClear = true;
    renderer.render(scene, camera); tally();
    // (b) the blit: colour + depth up to the canvas, the far end folded into the fog
    renderer.setRenderTarget(null);
    const u = blitMat.uniforms;
    if (scene.fog && scene.fog.color) u.uFogColor.value.copy(scene.fog.color);
    else if (scene.background && scene.background.isColor) u.uFogColor.value.copy(scene.background);
    u.uNear.value = camera.near; u.uFar.value = camera.far;
    renderer.render(blitScene, blitCam); tally();
    // (c) the crisp layer on top, full-res; a Color background would force a clear, so it steps aside
    const bg = scene.background;
    scene.background = null;
    renderer.autoClear = false;
    camera.layers.mask = CRISP_MASK;
    renderer.render(scene, camera); tally();
    renderer.autoClear = true;
    scene.background = bg;
    camera.layers.mask = ALL_MASK;
    closeFrame();
  }

  // ---- textures ----------------------------------------------------------------------------
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
  /** Walk every material in the scene (maps + shader sampler uniforms). Crisp-layer objects keep theirs. */
  function retexture(scene) {
    if (!scene || !scene.traverse) return;
    scene.traverse((o) => {
      if (o.layers && (o.layers.mask & CRISP_MASK) && !(o.layers.mask & WORLD_MASK)) return;
      const mats = Array.isArray(o.material) ? o.material : o.material ? [o.material] : [];
      for (const m of mats) {
        for (const k of MAP_KEYS) if (m[k]) filterTexture(m[k]);
        if (m.uniforms) for (const u of Object.values(m.uniforms)) if (u && u.value && u.value.isTexture) filterTexture(u.value);
      }
    });
  }

  function label() { return cur > 0 ? `pixels ${cur}` : 'pixels off'; }

  /** Free the render target and the blit material (call from the race's dispose). */
  function dispose() { dropTarget(); blitMat.dispose(); blitGeo.dispose(); }

  return {
    get block() { return cur; },
    setBlock, cycle, resize, render, filterTexture, retexture, label, stats, dispose,
  };
}

// self-check: node --check is the bar; normalizeBlock(2.6) -> 3, normalizeBlock('0') -> 0, normalizeBlock(9) -> 6.
