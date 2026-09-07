/* ============================================================================
 * race/wordTags.js - the word on a tag over the bubble, big enough to read.
 *
 *   createWordTags({ scene, camera, viewport }) -> { update(list), measure(), dispose() }
 *
 * THE BUBBLE'S OWN FACE CANNOT HOLD A WORD. run.js's FOV_BASE 72 re-solves on a 390x844
 * phone to viewport.js MAX_VFOV, and a 1.15 m sprite then draws about 20 px tall at 20 m.
 * So the word gets its OWN sprite with its OWN screen-size floor, hung over the bubble on
 * the crisp full-res layer the bubbles already draw on (pixel.js CRISP_LAYER).
 *
 * THE FLOOR. A tag's cap height never drops under MIN_CAP_PX on a phone (the caption band
 * in race.css measures 17.9 px there, which is the size the owner already reads at). Past
 * TAG_HOLD_M the tag holds that size on the glass however far off it is; inside it the tag
 * is a fixed size in the world and grows with the bubble, up to MAX_GROW. The sum is done
 * in camera space each frame off the live vertical fov, so it holds at every window shape.
 *
 * THE STACK, which is the part the geometry forces. A line of word bubbles runs down ONE
 * lane toward the vanishing point, so their tags land almost on top of each other: on a
 * phone the whole 5 m to 30 m of road is about 14 px of screen height. A tag is therefore
 * LIFTED, near one first, until its box clears the box of the tag in front of it by
 * GAP_PX. The lift is worked out in pixels and spent in metres, so the line reads as a
 * rising stack of words in the order they are said, each still over its own lane. A tag
 * that would have to climb past LIFT_MAX_PX is not drawn: past that it is nearer the sky
 * than the road and belongs to no bubble the eye can follow.
 *
 * COST. A pool of POOL sprites, each with its own 256x64 canvas, redrawn only when the word
 * on it changes. No new render pass. The caller hands in the nearest few, already sorted.
 * ==========================================================================*/

import * as THREE from 'three';
import { CRISP_LAYER } from './pixel.js';

/** How many tags exist. The caller never hands in more than this many. */
export const POOL = 16;
/** And how many bubbles actually wear one: the nearest few, so the road ahead stays road. */
export const TAG_WEAR = 10;
/** The tag canvas. 4:1, one word wide, and small enough that a redraw is not felt. */
export const TEX_W = 256, TEX_H = 64;
/** The floor, in CSS pixels of cap height, and how it grows on a window wider than a phone. */
export const MIN_CAP_PX = 18, CAP_VW = 0.048, MAX_CAP_PX = 30, REF_W_PX = 390;
/** Nearer than this (camera distance) the tag stops holding the floor and grows with the bubble. */
export const TAG_HOLD_M = 9;
/** And the most it may grow to: the owner's call, so a near plate never covers the cup or EMI. */
export const MAX_GROW = 1.5;
/** The tag hangs this far over its bubble before anything is stacked on top of it. */
export const LIFT_BASE_PX = 26;
/** Clear air between two tags of one line, and the highest a tag may be pushed before it is dropped. */
export const GAP_PX = 4, LIFT_MAX_PX = 240;
/** Metres of road ahead over which a tag fades in. */
export const FADE_IN_M = 30, FADE_M = 8;
/** An accent word, and a trigger row's shared tag, are drawn this much bigger. */
export const BIG = 1.3;

/** The plate: dark card, one pixel of light on its edge, so the word reads on the tube and on the sky. */
const PLATE = 'rgba(16,13,28,0.82)', EDGE = 'rgba(255,255,255,0.34)', INK = '#ff8fd0';
const PAD_X = 14, RADIUS = 16;
/** The type. The HUD's own sans (styles.css --rh-font), heavy, lowercase, never a book face. */
const FALLBACK_FONT = "'Poppins', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif";
const FONT_PX = 40, FONT_MIN_PX = 22;

const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);

/** The HUD's font stack, read once off the document, so a tag is set in the same face as the chrome. */
function hudFont() {
  try {
    const v = getComputedStyle(document.documentElement).getPropertyValue('--rh-font');
    return (v && v.trim()) || FALLBACK_FONT;
  } catch (e) { return FALLBACK_FONT; }
}

export function createWordTags({ scene, camera, viewport = null } = {}) {
  const group = new THREE.Group();
  group.name = 'race-word-tags';
  group.renderOrder = 3;
  if (scene) scene.add(group);
  const family = hudFont();
  const pool = [];
  for (let i = 0; i < POOL; i++) {
    const canvas = document.createElement('canvas');
    canvas.width = TEX_W; canvas.height = TEX_H;
    const ctx = canvas.getContext('2d');
    const tex = new THREE.CanvasTexture(canvas);
    tex.colorSpace = THREE.SRGBColorSpace;
    const mat = new THREE.SpriteMaterial({ map: tex, transparent: true, depthWrite: false, depthTest: false, opacity: 0 });
    const sprite = new THREE.Sprite(mat);
    sprite.visible = false;
    sprite.layers.set(CRISP_LAYER);
    sprite.renderOrder = 3;
    group.add(sprite);
    // capFrac: how much of the 64 px texture the cap height actually is, measured at draw time,
    // because the type shrinks to fit a long word and the floor is about the LETTERS, not the plate.
    pool.push({ sprite, mat, ctx, tex, key: '', text: '', ink: '', capFrac: 0.5, seen: 0 });
  }

  /** One redraw. Only ever called when the word or its ink changed. */
  function draw(slot, text, ink) {
    const c = slot.ctx;
    c.clearRect(0, 0, TEX_W, TEX_H);
    let size = FONT_PX;
    c.font = `800 ${size}px ${family}`;
    let w = c.measureText(text).width;
    const room = TEX_W - PAD_X * 2 - 6;
    if (w > room) {                                   // a long word shrinks rather than spilling off the plate
      size = Math.max(FONT_MIN_PX, Math.floor(size * (room / w)));
      c.font = `800 ${size}px ${family}`;
      w = c.measureText(text).width;
    }
    const m = c.measureText('h');
    const cap = (m && m.actualBoundingBoxAscent) ? m.actualBoundingBoxAscent : size * 0.72;
    const plateW = Math.min(TEX_W - 2, w + PAD_X * 2), x0 = (TEX_W - plateW) / 2;
    c.beginPath();
    if (c.roundRect) c.roundRect(x0, 4, plateW, TEX_H - 8, RADIUS);
    else c.rect(x0, 4, plateW, TEX_H - 8);
    c.fillStyle = PLATE; c.fill();
    c.lineWidth = 1; c.strokeStyle = EDGE; c.stroke();
    c.font = `800 ${size}px ${family}`;
    c.textAlign = 'center'; c.textBaseline = 'middle';
    c.fillStyle = ink || INK;
    c.shadowColor = 'rgba(0,0,0,0.85)'; c.shadowBlur = 4;
    c.fillText(text, TEX_W / 2, TEX_H / 2 + 1);
    c.shadowBlur = 0;
    slot.tex.needsUpdate = true;
    slot.text = text; slot.ink = ink || INK;
    slot.capFrac = clamp(cap / TEX_H, 0.2, 0.95);
    // the widest the plate is, as a fraction of the sprite, so the overlap pass boxes the PLATE
    slot.wideFrac = plateW / TEX_W;
  }

  /** The slot already wearing this word, else the least recently seen one. */
  let stamp = 0;
  function claim(key, text, ink) {
    let free = null, oldest = pool[0];
    for (const s of pool) {
      if (s.key === key) { s.seen = ++stamp; if (s.text !== text || s.ink !== ink) draw(s, text, ink); return s; }
      if (!free && !s.key) free = s;
      if (s.seen < oldest.seen) oldest = s;
    }
    const s = free || oldest;
    s.key = key; s.seen = ++stamp;
    draw(s, text, ink);
    return s;
  }

  const _p = new THREE.Vector3();
  const boxes = [];                    // { top, bottom, left, right } of the tags already placed
  const shown = [];                    // what measure() reports, rebuilt each update
  let vpW = 0, vpH = 0, kPx = 0;

  /** Viewport in CSS pixels and the pixels-per-world-unit-at-one-metre the camera works out to. */
  function readViewport() {
    let w = 0, h = 0;
    if (viewport) { const v = viewport(); if (v) { w = v.w; h = v.h; } }
    if (!w || !h) { w = (typeof innerWidth === 'number' ? innerWidth : REF_W_PX); h = (typeof innerHeight === 'number' ? innerHeight : 844); }
    vpW = Math.max(1, w); vpH = Math.max(1, h);
    const vFov = (camera && camera.fov ? camera.fov : 72) * Math.PI / 180;
    kPx = vpH / (2 * Math.tan(vFov / 2));
  }

  /** The cap height a tag may never draw under on THIS window. */
  const floorPx = () => clamp(vpW * CAP_VW, MIN_CAP_PX, MAX_CAP_PX);

  /**
   * One frame.
   * @param list the nearest bubbles that want a tag, NEAREST FIRST, each
   *        `{ key, pos: Vector3 (world), w: string, ink?: string, big?: boolean, alpha: number, ahead: number }`
   *        `ahead` is metres of road in front of the kart (the fade rides on it).
   */
  function update(list) {
    readViewport();
    // the tags are placed in CAMERA space, so this frame's lens has to be settled first. Cheap:
    // three.js skips the work when nothing moved, and pixel.render() calls it again anyway.
    camera.updateMatrixWorld();
    const floor = floorPx();
    boxes.length = 0; shown.length = 0;
    const live = new Set();
    for (const item of (list || [])) {
      if (!item || !item.w) continue;
      if (shown.length >= TAG_WEAR) break;
      const slot = claim(item.key, String(item.w).toLowerCase(), item.ink || INK);
      live.add(slot);
      _p.copy(item.pos).applyMatrix4(camera.matrixWorldInverse);
      const dist = -_p.z;
      if (!(dist > 0.2)) { slot.sprite.visible = false; continue; }
      // THE FLOOR. Past TAG_HOLD_M, grow is 1: the world size rises with the distance, the two
      // cancel, and the tag holds `capPx` on the glass. Inside it the tag swells with its bubble,
      // up to MAX_GROW. The measured cap is therefore never under the floor.
      const big = item.big ? BIG : 1;
      const capPx = floor * big;
      const grow = clamp(TAG_HOLD_M / dist, 1, MAX_GROW);
      const worldH = (capPx * grow * dist) / (kPx * slot.capFrac);
      const halfH = worldH / 2, halfW = (worldH * (TEX_W / TEX_H) * (slot.wideFrac || 1)) / 2;
      // the stack: lift until this tag's box clears the one in front of it
      // LIFT_BASE_PX is clear air between the bubble's middle and the bottom edge of its own tag
      let lift = (LIFT_BASE_PX * dist) / kPx + halfH;
      const px = (v) => (v * kPx) / dist;
      let top = 0, bottom = 0, left = 0, right = 0, tries = 0;
      for (;;) {
        const cy = vpH / 2 - px(_p.y + lift), cx = vpW / 2 + px(_p.x);
        const hp = px(halfH), wp = px(halfW);
        top = cy - hp; bottom = cy + hp; left = cx - wp; right = cx + wp;
        let hitBottom = null;
        for (const b of boxes) {
          if (right < b.left - GAP_PX || left > b.right + GAP_PX) continue;
          if (bottom < b.top - GAP_PX || top > b.bottom + GAP_PX) continue;
          if (hitBottom == null || b.top < hitBottom) hitBottom = b.top;
        }
        if (hitBottom == null || ++tries > POOL) break;
        lift += ((bottom - hitBottom) + GAP_PX) * dist / kPx;   // clear the box in front, in metres
        if (px(lift) > LIFT_MAX_PX) break;
      }
      const liftPx = px(lift);
      const fade = clamp((FADE_IN_M - (item.ahead || 0)) / FADE_M, 0, 1);
      const alpha = clamp((item.alpha == null ? 1 : item.alpha) * fade, 0, 1);
      // off the glass in any direction, too high to belong to a bubble, or faded out: not drawn.
      if (liftPx > LIFT_MAX_PX || alpha <= 0.02 || top < 0 || bottom > vpH || left < 0 || right > vpW) { slot.sprite.visible = false; continue; }
      _p.y += lift;
      slot.sprite.position.copy(_p).applyMatrix4(camera.matrixWorld);
      slot.sprite.scale.set(worldH * (TEX_W / TEX_H), worldH, 1);
      slot.mat.opacity = alpha;
      slot.sprite.visible = true;
      boxes.push({ top, bottom, left, right });
      shown.push({ w: slot.text, capPx: capPx * grow, floorPx: capPx, top, bottom, left, right, dist,
        ahead: item.ahead || 0, alpha, big: !!item.big, rowN: item.rowN || 1, lift: liftPx });
    }
    for (const s of pool) if (!live.has(s)) { s.sprite.visible = false; s.key = ''; }
  }

  return {
    update,
    /** What is on the glass this frame, for race/smoke/label-px-check.mjs. Never read by the game. */
    measure() { return { vw: vpW, vh: vpH, floorPx: floorPx(), tags: shown.map((t) => ({ ...t })) }; },
    dispose() {
      if (scene) scene.remove(group);
      for (const s of pool) { s.mat.dispose(); s.tex.dispose(); }
      pool.length = 0; boxes.length = 0; shown.length = 0;
    },
  };
}

// self-check: node race/smoke/label-px-check.mjs drives a phone viewport over a worded road and
// measures every tag on the glass from 30 m down to the pop point.
