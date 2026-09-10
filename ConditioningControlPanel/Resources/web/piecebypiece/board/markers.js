/* ============================================================================
 * board/markers.js - where the man in your hand may land.
 *
 * The old hint was an emissive tint on the square itself, which is invisible on
 * a board made of pink and cream. These are objects instead: a soft dot resting
 * over every empty square the held man may move to, and a red ring drawn around
 * any man he may take (en passant counts, and its ring sits on the square the
 * victim actually stands on). The square he was picked up from gets a faint
 * outline, so it is clear where putting him back means.
 *
 * The board also remembers the move that was just played: setLastMove(from, to)
 * leaves a thin ring where the man was standing and a soft fill where he came
 * to rest, faint enough to sit under everything else and kept until the next
 * move. That memory is deliberately NOT part of the hint layer, which is wiped
 * every time a man is picked up and put down.
 *
 * Wiring:
 *   drag.js   builds one of these, calls show() on pick up and on hover, and
 *             pumps update(dt) from its own frame step
 *   scene.js  setHighlights() forwards here through setHighlightSink(), so a
 *             caller that only holds the view still lights squares up
 *   boot.js   watches the bus for a turn and hands the move over
 *
 * Markers never take a raycast (drag.js picks men, and a marker lying over a
 * square must not shadow the man standing on it) and they neither cast nor
 * receive shadows. Everything is unlit MeshBasicMaterial, so a marker reads the
 * same under the key light and out in the rim light's shade.
 * ==========================================================================*/

import * as THREE from 'three';
import { squareToWorld } from './scene.js';

/** Every number that decides how a hint looks. One place, on purpose. */
export const TUNING = Object.freeze({
  y: 0.012,            // a hair over the board top, which is y = 0
  popIn: 0.12,         // s, how long a marker takes to arrive
  fadeOut: 0.16,       // s, and how long it takes to leave
  overshoot: 1.7,      // back-ease on the pop; 0 would be a plain ease
  hoverEase: 16,       // per second, how fast a marker reacts to the cursor
  hoverScale: 1.34,    // how much bigger the one under the cursor gets
  hoverGlow: 1.55,     // and how much brighter
  pulseAmp: 0.035,     // idle breathe, so a live hint never looks like a decal
  pulseFreq: 1.5,
  dotRadius: 0.15,
  glowSize: 0.92,      // the soft halo plane under a dot or a ring
  ringInner: 0.33,
  ringOuter: 0.425,
  originInner: 0.40,
  originOuter: 0.435,
  moveColor: 0xFFF1DA,     // cream core
  moveGlow: 0xFF8FCB,      // pink halo
  moveOpacity: 0.92,
  moveGlowOpacity: 0.5,
  captureColor: 0xFF2E4F,  // the ring around a man you may take
  captureGlow: 0xFF1F44,
  captureOpacity: 0.95,
  captureGlowOpacity: 0.42,
  originColor: 0xF7E7CC,
  originOpacity: 0.3,
  // The last move, kept until the next one. Lower than the hints and drawn
  // before them, so a legal dot always sits on top of the memory.
  lastY: 0.008,
  lastFade: 0.30,          // s, in and out
  lastFromColor: 0xFFF1DA, // a thin ring around where he was standing
  lastFromOpacity: 0.21,
  lastToColor: 0xFFE6C9,   // and a soft fill where he came to rest
  lastToOpacity: 0.19,
  lastToSize: 1.02,
  lastRingInner: 0.385,
  lastRingOuter: 0.435,
});

const T = TUNING;
const NOPE = () => {};      // markers are not pickable, ever

function reducedMotion() {
  if (typeof window === 'undefined') return false;
  const pbp = window.PBP;
  if (pbp && (pbp.reducedMotion || (pbp.settings && pbp.settings.reducedMotion))) return true;
  try { return !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

/** Back-ease: a marker arrives a touch too big and settles. */
function pop(t) {
  const c = T.overshoot;
  const u = t - 1;
  return 1 + (c + 1) * u * u * u + c * u * u;
}

/** A radial gradient, so the halo has no edge to give itself away. */
function glowTexture() {
  const size = 64;
  const cv = document.createElement('canvas');
  cv.width = size;
  cv.height = size;
  const ctx = cv.getContext('2d');
  const g = ctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
  g.addColorStop(0, 'rgba(255,255,255,1)');
  g.addColorStop(0.45, 'rgba(255,255,255,0.55)');
  g.addColorStop(1, 'rgba(255,255,255,0)');
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, size, size);
  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

export function createMarkers({ group }) {
  const root = new THREE.Group();
  root.name = 'pbp-markers';
  group.add(root);

  const tex = glowTexture();
  const geo = {
    dot: new THREE.CircleGeometry(T.dotRadius, 28).rotateX(-Math.PI / 2),
    glow: new THREE.PlaneGeometry(T.glowSize, T.glowSize).rotateX(-Math.PI / 2),
    ring: new THREE.RingGeometry(T.ringInner, T.ringOuter, 44).rotateX(-Math.PI / 2),
    origin: new THREE.RingGeometry(T.originInner, T.originOuter, 44).rotateX(-Math.PI / 2),
    lastRing: new THREE.RingGeometry(T.lastRingInner, T.lastRingOuter, 44).rotateX(-Math.PI / 2),
    lastFill: new THREE.PlaneGeometry(T.lastToSize, T.lastToSize).rotateX(-Math.PI / 2),
  };

  const live = new Map();   // square -> marker
  const dying = [];
  let clock = 0;

  function layer(marker, key, color, opacity, mapped) {
    const material = new THREE.MeshBasicMaterial({
      color, transparent: true, opacity, depthWrite: false, fog: false,
      toneMapped: false, side: THREE.DoubleSide, map: mapped ? tex : null,
    });
    const mesh = new THREE.Mesh(geo[key], material);
    mesh.raycast = NOPE;
    mesh.castShadow = false;
    mesh.receiveShadow = false;
    mesh.renderOrder = 3;
    marker.node.add(mesh);
    marker.parts.push({ material, base: opacity });
  }

  function build(square, kind) {
    const still = reducedMotion();
    const m = {
      square, kind, node: new THREE.Group(), parts: [],
      t: still ? 1 : 0, hover: 0, want: 0, phase: Math.random() * 6.283,
    };
    m.node.raycast = NOPE;
    if (kind === 'capture') {
      layer(m, 'glow', T.captureGlow, T.captureGlowOpacity, true);
      layer(m, 'ring', T.captureColor, T.captureOpacity, false);
    } else if (kind === 'origin') {
      layer(m, 'origin', T.originColor, T.originOpacity, false);
    } else {
      layer(m, 'glow', T.moveGlow, T.moveGlowOpacity, true);
      layer(m, 'dot', T.moveColor, T.moveOpacity, false);
    }
    m.node.position.copy(squareToWorld(square, T.y));
    m.node.scale.setScalar(still ? 1 : 0.001);
    root.add(m.node);
    return m;
  }

  function drop(m) {
    for (const part of m.parts) part.material.dispose();
    root.remove(m.node);
  }

  /**
   * Set the whole hint layer at once. `list` is
   *   [{ square, kind: 'move' | 'capture' | 'origin', hover: boolean }]
   * A square that is already showing the same kind stays put and keeps its pop,
   * so moving the cursor does not restart every animation. `kind` is optional
   * and falls back to a plain move, which is what an older caller passes.
   */
  function show(list = []) {
    const want = new Map();
    for (const hint of list) {
      if (hint && hint.square) want.set(hint.square, hint);
    }
    for (const [square, m] of [...live]) {
      const hint = want.get(square);
      if (hint && (hint.kind || 'move') === m.kind) continue;
      live.delete(square);
      if (reducedMotion()) drop(m); else dying.push(m);
    }
    for (const [square, hint] of want) {
      let m = live.get(square);
      if (!m) { m = build(square, hint.kind || 'move'); live.set(square, m); }
      m.want = hint.hover ? 1 : 0;
      if (reducedMotion()) m.hover = m.want;
    }
  }

  function clear() { show([]); }

  // --- the last move ---------------------------------------------------------
  // A move leaves something behind: a thin cream ring on the square he was
  // standing on and a soft fill on the one he came to rest on, both faint, both
  // kept until the next move is played. They live in their own group, so the
  // show()/clear() cycle the hints go through every time a man is picked up
  // cannot touch them, and they draw before the hints, so a legal dot landing
  // on the same square still reads over the top.
  const lastRoot = new THREE.Group();
  lastRoot.name = 'pbp-lastmove';
  lastRoot.raycast = NOPE;
  root.add(lastRoot);
  let lastMark = null;      // { key, node, parts, t }
  const lastDying = [];

  function lastLayer(mark, key, color, opacity, square, mapped) {
    const material = new THREE.MeshBasicMaterial({
      color, transparent: true, opacity: 0, depthWrite: false, fog: false,
      toneMapped: false, side: THREE.DoubleSide, map: mapped ? tex : null,
    });
    const mesh = new THREE.Mesh(geo[key], material);
    mesh.raycast = NOPE;
    mesh.castShadow = false;
    mesh.receiveShadow = false;
    mesh.renderOrder = 2;
    mesh.position.copy(squareToWorld(square, T.lastY));
    mark.node.add(mesh);
    mark.parts.push({ material, base: opacity });
  }

  function dropLast(mark) {
    for (const part of mark.parts) part.material.dispose();
    lastRoot.remove(mark.node);
  }

  /**
   * Remember a move. `setLastMove('e2', 'e4')` marks it; `setLastMove()` or
   * `setLastMove(null)` forgets it, which is what a new game and a take-back
   * both want. Setting the same move twice is a no-op, so a caller may pump
   * this on every turn without restarting the fade.
   */
  function setLastMove(from = null, to = null) {
    const key = from || to ? String(from) + '>' + String(to) : '';
    if ((lastMark ? lastMark.key : '') === key) return;
    if (lastMark) { lastDying.push(lastMark); lastMark = null; }
    if (!key) return;
    const mark = { key, node: new THREE.Group(), parts: [], t: reducedMotion() ? 1 : 0 };
    mark.node.raycast = NOPE;
    if (to) lastLayer(mark, 'lastFill', T.lastToColor, T.lastToOpacity, to, true);
    if (from) lastLayer(mark, 'lastRing', T.lastFromColor, T.lastFromOpacity, from, false);
    lastRoot.add(mark.node);
    lastMark = mark;
  }

  function updateLast(dt) {
    const still = reducedMotion();
    if (lastMark) {
      if (lastMark.t < 1) lastMark.t = still ? 1 : Math.min(1, lastMark.t + dt / T.lastFade);
      for (const part of lastMark.parts) part.material.opacity = part.base * lastMark.t;
    }
    for (let i = lastDying.length - 1; i >= 0; i--) {
      const m = lastDying[i];
      m.t = still ? 0 : m.t - dt / T.lastFade;
      if (m.t <= 0) { dropLast(m); lastDying.splice(i, 1); continue; }
      for (const part of m.parts) part.material.opacity = part.base * m.t;
    }
  }

  function update(dt) {
    clock += dt;
    updateLast(dt);
    const still = reducedMotion();
    const k = still ? 1 : 1 - Math.exp(-T.hoverEase * dt);
    for (const m of live.values()) {
      if (m.t < 1) m.t = still ? 1 : Math.min(1, m.t + dt / T.popIn);
      m.hover += (m.want - m.hover) * k;
      const breathe = still ? 1 : 1 + Math.sin(clock * T.pulseFreq * 6.283 + m.phase) * T.pulseAmp;
      const grow = 1 + m.hover * (T.hoverScale - 1);
      m.node.scale.setScalar(Math.max(0.001, pop(m.t) * grow * breathe));
      const lift = 1 + m.hover * (T.hoverGlow - 1);
      for (const part of m.parts) part.material.opacity = Math.min(1, part.base * m.t * lift);
    }
    for (let i = dying.length - 1; i >= 0; i--) {
      const m = dying[i];
      m.t -= dt / T.fadeOut;
      if (m.t <= 0) { drop(m); dying.splice(i, 1); continue; }
      m.node.scale.setScalar(Math.max(0.001, m.t));
      for (const part of m.parts) part.material.opacity = part.base * m.t;
    }
  }

  function dispose() {
    for (const m of live.values()) drop(m);
    for (const m of dying) drop(m);
    live.clear();
    dying.length = 0;
    if (lastMark) { dropLast(lastMark); lastMark = null; }
    for (const m of lastDying) dropLast(m);
    lastDying.length = 0;
    for (const key of Object.keys(geo)) geo[key].dispose();
    tex.dispose();
    if (root.parent) root.parent.remove(root);
  }

  return {
    show, clear, update, dispose, root, setLastMove, lastRoot,
    count: () => live.size,
    lastMove: () => (lastMark ? lastMark.key : null),
  };
}
