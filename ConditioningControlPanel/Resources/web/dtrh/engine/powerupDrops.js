/* ============================================================================
 * powerupDrops.js - grab-in-the-tube rework (2026-07).
 *
 * The loadout is gone: toys/accessories/charms are DISCOVERED and GRABBED during
 * the fall. This module floats a single power-up card through the tube every
 * ~20-30s; grab it (pointer raycast, dispatched from scene.grabPointerDown) and
 * it flies to the HUD - a consumable icon (bottom dock) for an active toy, or a
 * relic (top strip) for a passive accessory/charm. The card VISUAL mirrors
 * engine/boonPick.js (glow + tinted frame + CDN art w/ name-panel fallback,
 * billboarded to the camera); the OFFER logic + effect application live in the
 * game layer (chaosRun.onPowerupGrabbed), reached via the onGrab callback.
 *
 * Unlike boonPick this does NOT park the fall - the card drifts by and you take
 * it on the way down (or miss it). One card at a time keeps it a treat, not a
 * stream. Suppressed (setSpawnEnabled(false)) during drafts / junctions / holds.
 * ==========================================================================*/

import * as THREE from 'three';
import { LIFETIME_BOONS, boonDefById, RANKS } from '../game/catalog.js';

const ART = 'https://ccp.art/';          // matches boonPick.js / overlays.js
const CARD_W = 2.4, CARD_H = 3.05;       // portrait, like the boon cards
const AHEAD = 42;                        // depth lead: spawn this far ahead of the camera
const CULL_BEHIND = 3;                   // cull once the camera has fallen this far past it
const MIN_GAP = 20, MAX_GAP = 30;        // seconds between drops (jittered)

// rarity -> palette (mirror boonPick THEMES, keyed by category since these aren't drafted boons)
const CAT_THEME = {
  skill:     { frame: 0x66e0d0, glow: 0xa9f0e6 },   // consumable toys
  accessory: { frame: 0xc178ff, glow: 0xe0b3ff },   // passives
  utility:   { frame: 0xccd6e6, glow: 0xeef2f8 },   // charms
};
const themeOf = (def) => CAT_THEME[def.cat] || CAT_THEME.utility;
const kindOf = (def) => (def.activeUse ? 'consumable' : 'passive');

// ---- shared textures (built once) ------------------------------------------
function makeGlowTex() {
  const s = 128, c = document.createElement('canvas'); c.width = c.height = s;
  const x = c.getContext('2d');
  const g = x.createRadialGradient(s / 2, s / 2, 0, s / 2, s / 2, s / 2);
  g.addColorStop(0, 'rgba(255,255,255,0.9)');
  g.addColorStop(0.4, 'rgba(255,255,255,0.35)');
  g.addColorStop(1, 'rgba(255,255,255,0)');
  x.fillStyle = g; x.fillRect(0, 0, s, s);
  return new THREE.CanvasTexture(c);
}
function makeFrameTex() {
  const w = 256, h = 320, c = document.createElement('canvas'); c.width = w; c.height = h;
  const x = c.getContext('2d');
  const r = 26, pad = 8, lw = 12;
  x.strokeStyle = '#ffffff'; x.lineWidth = lw; x.lineJoin = 'round';
  x.beginPath();
  x.moveTo(pad + r, pad);
  x.arcTo(w - pad, pad, w - pad, h - pad, r);
  x.arcTo(w - pad, h - pad, pad, h - pad, r);
  x.arcTo(pad, h - pad, pad, pad, r);
  x.arcTo(pad, pad, w - pad, pad, r);
  x.closePath(); x.stroke();
  return new THREE.CanvasTexture(c);
}
// canvas name-panel fallback (glyph + name), tinted per category - no CORS taint
function makeNamePanelTex(def, hexFrame) {
  const w = 256, h = 320, c = document.createElement('canvas'); c.width = w; c.height = h;
  const x = c.getContext('2d');
  const col = '#' + ('000000' + (hexFrame >>> 0).toString(16)).slice(-6);
  const g = x.createLinearGradient(0, 0, 0, h);
  g.addColorStop(0, 'rgba(24,16,34,0.96)');
  g.addColorStop(1, 'rgba(12,8,20,0.96)');
  x.fillStyle = g; x.fillRect(0, 0, w, h);
  x.fillStyle = col; x.globalAlpha = 0.16; x.fillRect(0, 0, w, h); x.globalAlpha = 1;
  x.fillStyle = 'rgba(255,255,255,0.92)';
  x.font = '64px "Segoe UI Emoji", "Segoe UI", sans-serif';
  x.textAlign = 'center'; x.textBaseline = 'middle';
  x.fillText(def.glyph || '◈', w / 2, h * 0.36);
  x.fillStyle = col;
  x.font = 'bold 24px "Segoe UI", sans-serif';
  const words = String(def.name || '').split(' ');
  const lines = []; let line = '';
  for (const wd of words) {
    if ((line + ' ' + wd).length > 13 && line) { lines.push(line); line = wd; }
    else line = line ? line + ' ' + wd : wd;
  }
  if (line) lines.push(line);
  const y0 = h * 0.66 - (lines.length - 1) * 15;
  lines.forEach((l, i) => x.fillText(l, w / 2, y0 + i * 30));
  x.font = '15px "Segoe UI", sans-serif'; x.fillStyle = 'rgba(255,255,255,0.5)';
  x.fillText(def.activeUse ? 'power-up' : 'charm', w / 2, h - 26);
  return new THREE.CanvasTexture(c);
}

export function createPowerupDrops({ scene, camera, layout, getMeta, canOffer, onGrab }) {
  const unitPlane = new THREE.PlaneGeometry(1, 1);
  const glowTex = makeGlowTex();
  const frameTex = makeFrameTex();
  const _euler = new THREE.Euler(), _q = new THREE.Quaternion(), _wp = new THREE.Vector3();

  let spawnEnabled = false;
  let timer = MIN_GAP + Math.random() * (MAX_GAP - MIN_GAP);
  let card = null;   // at most one live card

  // ---- pick which item to offer -------------------------------------------
  // Also exported: junction doorways call this to hang a prize on each card
  // (excludeIds keeps the two doorways from offering the same item).
  function pickOffer(excludeIds) {
    const skip = excludeIds ? new Set(excludeIds) : null;
    const meta = (getMeta && getMeta()) || {};
    const runs = meta.runsCompleted || 0;
    const rankIndex = RANKS.forRuns(runs);
    const levels = meta.lifetimeBoonLevels || {};
    const pool = [];
    for (const def of LIFETIME_BOONS) {
      if (skip && skip.has(def.id)) continue;                  // already on the other doorway
      if ((def.rankFloor || 0) > rankIndex) continue;          // still rank-locked
      const kind = kindOf(def);
      if (canOffer && !canOffer(def.id, kind)) continue;       // game veto (already grabbed / dock full)
      // weight undiscovered higher - discovery is the reward loop
      const discovered = (levels[def.id] | 0) >= 1;
      const weight = discovered ? 1 : 3;
      pool.push({ def, kind, weight });
    }
    if (!pool.length) return null;
    let total = 0; for (const p of pool) total += p.weight;
    let roll = Math.random() * total;
    for (const p of pool) { roll -= p.weight; if (roll <= 0) return p; }
    return pool[pool.length - 1];
  }

  // ---- build one drifting card at a fixed world point ahead ----------------
  function buildCard(def, kind, depth) {
    const th = themeOf(def);
    const fr = layout.frameAtDepth(depth);
    const group = new THREE.Group();
    group.position.copy(fr.pos);
    group.userData = { type: 'powerup', id: def.id, kind };
    const w = CARD_W, h = CARD_H;

    const glowMat = new THREE.MeshBasicMaterial({
      map: glowTex, color: new THREE.Color(th.glow), transparent: true, opacity: 0,
      depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide });
    const glow = new THREE.Mesh(unitPlane, glowMat);
    glow.scale.set(w * 1.7, h * 1.6, 1); glow.position.z = -0.08; group.add(glow);

    const frameMat = new THREE.MeshBasicMaterial({
      map: frameTex, color: new THREE.Color(th.frame), transparent: true, opacity: 0,
      depthWrite: false, side: THREE.DoubleSide });
    const frame = new THREE.Mesh(unitPlane, frameMat);
    frame.scale.set(w * 1.08, h * 1.08, 1); frame.position.z = 0.01; group.add(frame);

    let disposed = false;
    const contentMat = new THREE.MeshBasicMaterial({
      map: makeNamePanelTex(def, th.frame), transparent: true, opacity: 0,
      depthWrite: false, side: THREE.DoubleSide });
    const content = new THREE.Mesh(unitPlane, contentMat);
    content.scale.set(w, h, 1); content.position.z = 0.02; group.add(content);
    new THREE.TextureLoader().load(`${ART}boons/${def.id}.png`, (tex) => {
      if (disposed) { tex.dispose(); return; }
      tex.colorSpace = THREE.SRGBColorSpace;
      const old = contentMat.map; contentMat.map = tex; contentMat.needsUpdate = true;
      if (old) old.dispose();
    }, undefined, () => { /* keep the name panel */ });

    scene.add(group);
    let appear = 0, sway = Math.random() * 6, fade = 0;

    return {
      def, kind, group, cardDepth: depth,
      update(dt, camDepth, camQuat) {
        appear = Math.min(1, appear + dt * 2.6);
        sway += dt;
        _euler.set(0, 0, Math.sin(sway * 1.1) * 0.05);
        _q.setFromEuler(_euler);
        group.quaternion.copy(camQuat).multiply(_q);
        if (fade > 0) fade += dt;
        const fk = fade > 0 ? Math.min(1, fade / 0.3) : 0;
        const vis = appear * (1 - fk);
        glowMat.opacity = 0.9 * vis * (0.7 + 0.22 * Math.sin(sway * 2.4));
        frameMat.opacity = vis;
        contentMat.opacity = vis;
        group.scale.setScalar(1 - 0.14 * fk);
        return fk >= 1;   // fully faded -> disposable
      },
      startFade() { if (fade <= 0) fade = 0.001; },
      isFading: () => fade > 0,
      screenPos() {
        group.getWorldPosition(_wp); _wp.project(camera);
        return { x: (_wp.x * 0.5 + 0.5) * window.innerWidth, y: (-_wp.y * 0.5 + 0.5) * window.innerHeight };
      },
      dispose() {
        disposed = true; scene.remove(group);
        glowMat.dispose(); frameMat.dispose();
        if (contentMat.map) contentMat.map.dispose(); contentMat.dispose();
      },
    };
  }

  let _camDepth = 0;
  let blocked = null;   // junction chamber span (scene.setBlockedSpan): keep drops out of it
  function spawnDepth() { return _camDepth + AHEAD; }

  function trySpawn() {
    if (card) return;
    // a junction chamber owns the span ahead: a card in there floats in the
    // hidden-trunk cut or hides behind the opaque room. Drop it just SHORT of
    // the fork instead, or wait if the camera is already at the gate.
    let d = spawnDepth();
    if (blocked && d > blocked.lo && d < blocked.hi) {
      d = blocked.lo - 4;
      if (d < _camDepth + 12) { timer = 2; return; }
    }
    const offer = pickOffer();
    if (!offer) { timer = 6; return; }   // nothing to offer now; retry sooner
    card = buildCard(offer.def, offer.kind, d);
    timer = MIN_GAP + Math.random() * (MAX_GAP - MIN_GAP);
  }

  return {
    setSpawnEnabled(on) { spawnEnabled = !!on; },
    setBlockedSpan(s) { blocked = s || null; },
    // junction doorway prizes draw from the same offer pool + veto as the drops
    pickOffer,

    update(dt, camDepth, camQuat) {
      _camDepth = camDepth;
      if (card) {
        const done = card.update(dt, camDepth, camQuat || camera.quaternion);
        // cull: faded out, or the fall has carried us past it
        if ((done && card.isFading()) || camDepth > card.cardDepth + CULL_BEHIND) {
          if (!card.isFading() && camDepth > card.cardDepth + CULL_BEHIND) { card.startFade(); }
          else { card.dispose(); card = null; }
        }
      }
      if (!spawnEnabled) return;
      if (!card) {
        timer -= dt;
        if (timer <= 0) trySpawn();
      }
    },

    // pointer raycast targets (scene.grabPointerDown)
    getPickables() { return (card && !card.isFading()) ? [card.group] : []; },

    // grab the live card: hand its id/kind + on-screen position to the game, then fly it away
    grab(group) {
      if (!card || card.group !== group || card.isFading()) return false;
      const pos = card.screenPos();
      const g = card; card = null;   // detach so a second pointer can't double-grab
      try { if (onGrab) onGrab(g.def.id, g.kind, pos); } catch (e) { /* ignore */ }
      g.dispose();
      return true;
    },

    hasLive() { return !!(card && !card.isFading()); },

    dispose() {
      if (card) { card.dispose(); card = null; }
      unitPlane.dispose(); glowTex.dispose(); frameTex.dispose();
    },
  };
}
