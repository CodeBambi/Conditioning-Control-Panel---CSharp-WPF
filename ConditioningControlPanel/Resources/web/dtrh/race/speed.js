/* ============================================================================
 * race/speed.js - the sense of speed for The Caucus Race.
 *
 * Three cheap layers, all additive, none of them paint over the road:
 *  - streaks: short lines around the camera edge (a child of the camera) that
 *    scroll outward and lengthen with speed, brightest under boost
 *  - wind: a stream of thin segments in track space past the cup; the kart
 *    moves through them at its true speed so they never lie about it
 *  - vignette: an optional mild pink radial pulse (a DOM layer under the HUD)
 *    that comes up fast on boost and fades slowly
 * The ground rush itself is the rooms' scrolling road texture, not ours.
 *
 *   createSpeedFx({ scene, camera, root, reducedMotion }) -> { update(dt, kartState, layout), dispose }
 *
 * Reduced motion: no streaks, no vignette, the wind at half strength.
 * ==========================================================================*/

import * as THREE from 'three';
import { KART_BASE_SPEED, KART_MAX_SPEED, ROAD_HALF_W } from './consts.js';

const STREAK_N = 56, WIND_N = 90;
const STREAK_Z = 2;                 // camera-space depth of the streak plane
const DEG = Math.PI / 180;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const rand = (a, b) => a + Math.random() * (b - a);

export function createSpeedFx({ scene, camera, root, reducedMotion = false }) {
  // ---- streaks (camera space) ----
  const streaks = [];
  for (let i = 0; i < STREAK_N; i++) streaks.push({ a: rand(0, Math.PI * 2), r0: rand(0.5, 0.95), p: Math.random(), spin: rand(0.6, 1.4) });
  const sGeo = new THREE.BufferGeometry();
  const sPos = new Float32Array(STREAK_N * 6), sCol = new Float32Array(STREAK_N * 6);
  sGeo.setAttribute('position', new THREE.BufferAttribute(sPos, 3));
  sGeo.setAttribute('color', new THREE.BufferAttribute(sCol, 3));
  const sMat = new THREE.LineBasicMaterial({ vertexColors: true, transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthTest: false, depthWrite: false, fog: false });
  const sLines = new THREE.LineSegments(sGeo, sMat);
  sLines.frustumCulled = false; sLines.renderOrder = 5; sLines.visible = false;
  let cameraAdded = false;
  if (!reducedMotion) {
    if (!camera.parent) { scene.add(camera); cameraAdded = true; }
    camera.add(sLines);
  }

  // ---- wind (track space) ----
  const wind = [];
  for (let i = 0; i < WIND_N; i++) wind.push({ d: 0, x: 0, h: 0, len: 0.6, live: false });
  const wGeo = new THREE.BufferGeometry();
  const wPos = new Float32Array(WIND_N * 6);
  wGeo.setAttribute('position', new THREE.BufferAttribute(wPos, 3));
  const wMat = new THREE.LineBasicMaterial({ color: 0xffd9ef, transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false, fog: true });
  const wLines = new THREE.LineSegments(wGeo, wMat);
  wLines.frustumCulled = false; wLines.renderOrder = 4;
  scene.add(wLines);
  const _a = new THREE.Vector3(), _b = new THREE.Vector3();

  // ---- vignette (DOM, under the HUD chrome at z3) ----
  let vig = null;
  if (!reducedMotion && root) {
    vig = document.createElement('div');
    vig.className = 'race-speed-vignette';
    vig.setAttribute('aria-hidden', 'true');
    vig.style.cssText = 'position:absolute;inset:0;pointer-events:none;z-index:2;opacity:0;'
      + 'background:radial-gradient(ellipse at center, rgba(255,105,180,0) 52%, rgba(255,105,180,.42) 100%);';
    root.appendChild(vig);
  }

  let boost = 0, rush = 0;

  function update(dt, ks, layout) {
    if (!(dt > 0) || !ks) return;
    const boostT = ks.boostSec > 0 ? 1 : 0;
    boost += (boostT - boost) * Math.min(1, dt * (boostT ? 8 : 1.6));
    // 0 a touch under cruise, 1 at the cap: cruise shows a whisper, boost the full streak
    const rushT = clamp((ks.speed - KART_BASE_SPEED * 0.85) / (KART_MAX_SPEED - KART_BASE_SPEED * 0.85), 0, 1);
    rush += (rushT - rush) * Math.min(1, dt * 4);
    const heat = clamp(rush * 0.7 + boost * 0.5, 0, 1);

    // streaks
    if (!reducedMotion) {
      sLines.visible = heat > 0.02;
      if (sLines.visible) {
        const th = Math.tan(camera.fov * 0.5 * DEG) * STREAK_Z, tw = th * camera.aspect;
        const scroll = (0.35 + 1.6 * heat) * dt, len = 0.03 + 0.22 * heat;
        for (let i = 0; i < STREAK_N; i++) {
          const s = streaks[i];
          s.p += scroll * s.spin; if (s.p >= 1) s.p -= 1;
          const r = s.r0 + s.p * 0.45, r2 = r + len;
          const c = Math.cos(s.a), sn = Math.sin(s.a);
          const o = i * 6;
          sPos[o] = c * r * tw; sPos[o + 1] = sn * r * th; sPos[o + 2] = -STREAK_Z;
          sPos[o + 3] = c * r2 * tw; sPos[o + 4] = sn * r2 * th; sPos[o + 5] = -STREAK_Z;
          const k = (1 - s.p) * (0.4 + 0.6 * clamp((r - 0.5) / 0.6, 0, 1));
          sCol[o] = 0.35 * k; sCol[o + 1] = 0.2 * k; sCol[o + 2] = 0.3 * k;
          sCol[o + 3] = k; sCol[o + 4] = 0.55 * k + 0.3 * boost; sCol[o + 5] = 0.8 * k;
        }
        sGeo.attributes.position.needsUpdate = true; sGeo.attributes.color.needsUpdate = true;
        sMat.opacity = 0.12 + 0.6 * heat;
      }
    }

    // wind: segments live in track space; respawn ahead once the cup is past them
    if (layout) {
      const T = layout.totalDepth;
      const rel = (d) => { let r = (d - ks.d) % T; if (r > T / 2) r -= T; else if (r <= -T / 2) r += T; return r; };
      const segLen = clamp(ks.speed * 0.045, 0.35, 1.7);
      for (let i = 0; i < WIND_N; i++) {
        const p = wind[i];
        if (!p.live || rel(p.d) < -2.5) {
          p.live = true;
          p.d = layout.wrap(ks.d + rand(8, 34)); p.x = rand(-4.6, 4.6);
          // over the road it stays high (the ribbon keeps its own texture); in the gutters it can skim low
          p.h = Math.abs(p.x) < ROAD_HALF_W + 0.3 ? rand(1.5, 4.8) : rand(0.25, 4.8);
          p.len = segLen * rand(0.6, 1.2);
        }
        layout.toWorld(p.d, p.x, p.h, _a); layout.toWorld(layout.wrap(p.d + p.len), p.x, p.h, _b);
        const o = i * 6;
        wPos[o] = _a.x; wPos[o + 1] = _a.y; wPos[o + 2] = _a.z;
        wPos[o + 3] = _b.x; wPos[o + 4] = _b.y; wPos[o + 5] = _b.z;
      }
      wGeo.attributes.position.needsUpdate = true;
      wMat.opacity = (0.1 + 0.45 * heat) * (reducedMotion ? 0.5 : 1);
    }

    if (vig) vig.style.opacity = String((0.85 * boost).toFixed(3));
  }

  function dispose() {
    if (sLines.parent) sLines.parent.remove(sLines);
    if (cameraAdded && camera.parent === scene) scene.remove(camera);
    scene.remove(wLines);
    sGeo.dispose(); sMat.dispose(); wGeo.dispose(); wMat.dispose();
    if (vig) vig.remove();
  }

  return { update, dispose };
}

// self-check: node --check is the bar (the DOM vignette and the camera are only touched inside createSpeedFx).
