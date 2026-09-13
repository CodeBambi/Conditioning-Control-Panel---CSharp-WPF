/* emi.js - EMI on the wheel's perch (Law XIII, THE MASCOT GLANCE). Ported from blender-scripting
 * wheel/preview/emi-reactions.js: the face atlas cell on EMI_glass, spiral eyes painted while the wheel turns,
 * a 340 ms blend between faces, and arm poses on the authored shoulder hinges.
 *
 * Changes for the station: no idle sway (THE BREATH belongs to the jackpot star alone, Law III); a sleepy droop
 * for Snooze (slow, low, held); THE REVEAL's over-rotation for the pot; reduced motion sets the face and the
 * rest pose directly (Law VI). The HUD canvas mirrors the face so it is readable at any camera distance. */

import * as THREE from 'three';
import { FEEL, bezier } from './feel.js';

export const FACES = { idle0_0: 3, hearts: 7, spirals: 8, melt: 9, jackpot: 10 };
const W = 152, H = 137;

export function createEmi({ root, faceMesh, atlas, hud, reduced }) {
  const topper = root.getObjectByName('emi_topper'), left = root.getObjectByName('shoulderL'), right = root.getObjectByName('shoulderR');
  const rest = topper && { p: topper.position.clone(), s: topper.scale.clone(), r: topper.rotation.clone(),
                           l: left && left.rotation.clone(), rr: right && right.rotation.clone() };
  const img = atlas && atlas.image;
  const canvas = document.createElement('canvas'); canvas.width = W; canvas.height = H;
  const g = canvas.getContext('2d'), hudCtx = hud && hud.getContext('2d');
  const tex = new THREE.CanvasTexture(canvas);
  Object.assign(tex, { flipY: false, colorSpace: THREE.SRGBColorSpace, minFilter: THREE.LinearFilter, magFilter: THREE.LinearFilter, generateMipmaps: false });
  const from = document.createElement('canvas'); from.width = W; from.height = H;
  let face = 'idle0_0', blend = null, mode = 'idle', modeAt = 0, spinAt = 0, lastEyes = -Infinity;

  const cell = name => (FACES[name] ?? 3) * W;
  const mirror = () => { if (hudCtx) { hudCtx.clearRect(0, 0, W, H); hudCtx.drawImage(canvas, 0, 0); } };
  function paintCell(name) {
    if (!img) return;
    g.clearRect(0, 0, W, H); g.drawImage(img, cell(name), 0, W, H, 0, 0, W, H);
    tex.needsUpdate = true; mirror();
  }
  function paintEyes(t) {
    g.drawImage(img, cell('spirals'), 0, W, H, 0, 0, W, H);
    for (const [x, dir] of [[33, 1], [119, -1]]) {   // circular masks keep the atlas mouth and pixel eyes
      g.save(); g.beginPath(); g.arc(x, 53, 31, 0, Math.PI * 2); g.clip(); g.translate(x, 53); g.rotate((t / 210) * dir);
      g.drawImage(img, cell('spirals') + x - 31, 22, 62, 62, -31, -31, 62, 62); g.restore();
    }
    tex.needsUpdate = true; mirror();
  }
  if (faceMesh && img) { faceMesh.material = new THREE.MeshBasicMaterial({ map: tex }); paintCell(face); }

  return {
    get face() { return face; },
    get mode() { return mode; },
    /** The face to show (THE MASCOT GLANCE picks it); blends in 340 ms unless reduced. */
    setFace(name) {
      const next = FACES[name] !== undefined ? name : 'idle0_0';
      if (next === face && !blend) return;
      if (img && !reduced) { const f = from.getContext('2d'); f.clearRect(0, 0, W, H); f.drawImage(canvas, 0, 0); blend = { start: performance.now() }; }
      face = next;
      if (reduced || !img) paintCell(face);
    },
    /** 'idle' | 'spin' | 'win' | 'jackpot' | 'sleepy'. */
    setMode(m) { if (m !== mode) { mode = m; modeAt = performance.now(); if (m === 'spin') spinAt = modeAt; } },
    skip() { blend = null; modeAt = -Infinity; paintCell(face); },
    update(t) {
      if (!img) return;
      if (mode === 'spin' && !reduced) { blend = null; if (t - lastEyes >= 32) { paintEyes(t - spinAt); lastEyes = t; } }
      else if (blend) {
        const q = Math.min(1, (t - blend.start) / 340), mix = q * q * (3 - 2 * q);
        g.clearRect(0, 0, W, H); g.drawImage(from, 0, 0); g.globalAlpha = mix; g.drawImage(img, cell(face), 0, W, H, 0, 0, W, H); g.globalAlpha = 1;
        tex.needsUpdate = true; mirror();
        if (q === 1) blend = null;
      } else if (lastEyes > 0) { lastEyes = -Infinity; paintCell(face); }
      if (!topper) return;
      topper.position.copy(rest.p); topper.scale.copy(rest.s); topper.rotation.copy(rest.r);
      if (left) left.rotation.copy(rest.l);
      if (right) right.rotation.copy(rest.rr);
      if (hud) hud.style.transform = '';
      if (reduced) return;
      const age = t - modeAt;
      let lean = 0, lr = 0, rr = 0, fwd = 0, squash = 1, hop = 0, turn = 0, pop = 1;
      if (mode === 'spin') { const e = Math.min(age / 180, 1); lr = (0.28 + 0.07 * Math.sin(age / 180)) * e; rr = (0.28 - 0.07 * Math.sin(age / 180)) * e; fwd = -0.15 * e; }
      else if ((mode === 'win' || mode === 'jackpot') && age < 1000) {
        const lift = Math.sin(Math.min(age / 1000, 1) * Math.PI), jump = Math.max(0, Math.min(1, (age - 130) / 480));
        hop = Math.sin(jump * Math.PI) * (mode === 'jackpot' ? 0.014 : 0.009); squash = 1 - 0.07 * (age < 130 ? Math.sin((age / 130) * Math.PI) : 0);
        lr = (mode === 'jackpot' ? 2.05 : 1.2) * lift; rr = (mode === 'jackpot' ? 2.05 : 1.0) * lift; fwd = -0.12 * lift;
        if (mode === 'jackpot' && age < FEEL.REVEAL_MS) { const q = bezier(FEEL.REVEAL_EASE, age / FEEL.REVEAL_MS); turn = Math.PI * 2 * q; pop = 0.6 + 0.4 * q; }
      } else if (mode === 'sleepy') {   // Snooze: droops over 1.4 s and stays drowsy, the head nods once
        const e = Math.min(1, age / 1400) ** 2, nod = age > 1400 && age < 2600 ? Math.sin(((age - 1400) / 1200) * Math.PI) : 0;
        squash = 1 - 0.06 * e; lean = -0.07 * e - 0.03 * nod; lr = rr = 0.1 * e; fwd = 0.08 * e + 0.05 * nod;
      }
      if (left) { left.rotation.z -= lr; left.rotation.x += fwd; }
      if (right) { right.rotation.z += rr; right.rotation.x += fwd; }
      topper.position.y += hop; topper.rotation.z += lean; topper.rotation.y += turn;
      topper.scale.multiplyScalar(pop); topper.scale.y *= squash; topper.scale.x /= Math.sqrt(squash);
      if (hud) hud.style.transform = `translateY(${(-hop * 150).toFixed(1)}px) rotate(${(lean * 25).toFixed(2)}deg) scaleY(${squash.toFixed(3)})`;
    },
    dispose() { tex.dispose(); },
  };
}
