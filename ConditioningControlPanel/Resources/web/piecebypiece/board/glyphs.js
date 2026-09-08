/* ============================================================================
 * board/glyphs.js - the classic chess glyphs, floating over the men.
 *
 * The men are toys. From straight above a toy king and a toy queen are two
 * blobs, so as the camera climbs, a chess glyph fades in over each one: cream
 * outlined figures for white, lavender for black, the ones everybody already
 * reads. They are Sprites with a CanvasTexture, so there is one draw per man
 * and no geometry to keep in step.
 *
 * How they behave:
 *   - one sprite per man, parked at the piece's own height (userData.scaleBase,
 *     which is the top of the man whether he is a lathe or a glb) plus a lift
 *   - they follow whatever the piece does, being placed from its live position
 *     every frame, and go the moment the man leaves the board
 *   - depthTest off and a high render order, so a tall man never eats his own
 *     glyph, and raycast off, so they are invisible to drag.js
 *   - the fade is driven by the camera's actual elevation over the board, not
 *     by which preset is on: orbiting overhead by hand brings them in too
 *
 * Wiring: boot.js builds one of these. It ticks itself off scene.onBeforeRender
 * (chaining whatever was there), which the renderer calls once a frame with the
 * camera already up to date, so the board's frame loop needs no new line.
 * ==========================================================================*/

import * as THREE from 'three';

/** Every number that decides how the glyphs read. One place, on purpose. */
export const GLYPH_TUNING = Object.freeze({
  size: 0.70,          // sprite size in world units; a square is 1.0
  lift: 0.24,          // how far above the man's own head it floats
  settle: 0.72,        // ... and how much of that height it gives back as the
                       // camera comes overhead, where a tall float would throw
                       // the glyph a half square off its own man
  fadeLo: 56,          // degrees of elevation: fully out at or under this
  fadeHi: 74,          // and fully in at or over this, so about 65 is halfway
  fadeRate: 7.0,       // e-fold rate of the opacity chase
  px: 128,             // texture size, per glyph
  ink: '#241A3B',      // the outline both sides wear
  white: '#F5E6C8',
  black: '#AB90FF',
});

const WHITE = { k: '♔', q: '♕', r: '♖', b: '♗', n: '♘', p: '♙' };
const BLACK = { k: '♚', q: '♛', r: '♜', b: '♝', n: '♞', p: '♟' };
const LETTER = { k: 'K', q: 'Q', r: 'R', b: 'B', n: 'N', p: 'P' };
const FONT = '"Segoe UI Symbol", "Segoe UI", "DejaVu Sans", "Arial Unicode MS", system-ui, sans-serif';

const clamp01 = (v) => Math.max(0, Math.min(1, v));
const smooth = (t) => t * t * (3 - 2 * t);

function prefersReducedMotion() {
  if (typeof window === 'undefined') return false;
  const pbp = window.PBP;
  if (pbp && (pbp.reducedMotion || (pbp.settings && pbp.settings.reducedMotion))) return true;
  try { return !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

/**
 * Is this glyph actually in the fonts we asked for? A missing one is drawn as
 * the "no such glyph" box, so it measures the same width as a codepoint no
 * font can ever carry. Firefox and Edge both ship the chess figures on
 * Windows; this is the belt for anything that does not.
 */
function glyphAvailable(ctx, ch) {
  const tofu = ctx.measureText('\u{10FFFF}').width;   // no font has this one
  const w = ctx.measureText(ch).width;
  return w > 0 && Math.abs(w - tofu) > 0.1;
}

/** One glyph, drawn once, kept as a texture. */
function glyphTexture(type, side, T) {
  const px = T.px;
  const canvas = document.createElement('canvas');
  canvas.width = px; canvas.height = px;
  const ctx = canvas.getContext('2d');
  ctx.font = Math.round(px * 0.74) + 'px ' + FONT;
  const wanted = (side === 'w' ? WHITE : BLACK)[type];
  const ch = glyphAvailable(ctx, wanted) ? wanted : LETTER[type];
  if (ch !== wanted) ctx.font = '700 ' + Math.round(px * 0.6) + 'px ' + FONT;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.lineJoin = 'round';
  // A soft drop under it, then the outline, then the face: the glyph has to
  // hold up over a cream square and over a pink one.
  ctx.shadowColor = 'rgba(20, 14, 40, 0.55)';
  ctx.shadowBlur = px * 0.09;
  ctx.shadowOffsetY = px * 0.03;
  ctx.lineWidth = px * 0.085;
  ctx.strokeStyle = T.ink;
  ctx.strokeText(ch, px / 2, px * 0.53);
  ctx.shadowColor = 'transparent';
  ctx.fillStyle = side === 'w' ? T.white : T.black;
  ctx.fillText(ch, px / 2, px * 0.53);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 2;
  return tex;
}

/**
 * @param view    what createScene handed back (scene, camera, ...)
 * @param pieces  what createPieces handed back; `pieces.pieces` is the live
 *                square -> man map, and a captured man is out of it at once
 */
export function createGlyphs({ view, pieces, tuning = {} }) {
  const T = Object.freeze({ ...GLYPH_TUNING, ...tuning });
  const group = new THREE.Group();
  group.name = 'glyphs';
  group.renderOrder = 900;
  view.scene.add(group);

  const materials = new Map();   // "type:side" -> one shared SpriteMaterial
  const sprites = new Map();     // piece object -> its sprite
  const seen = new Set();
  let shown = 0;                 // live opacity, 0..1
  let last = 0;

  function materialFor(type, side) {
    const key = type + ':' + side;
    let mat = materials.get(key);
    if (!mat) {
      mat = new THREE.SpriteMaterial({
        map: glyphTexture(type, side, T),
        transparent: true, depthTest: false, depthWrite: false, fog: false,
        sizeAttenuation: true, opacity: shown,
      });
      materials.set(key, mat);
    }
    return mat;
  }

  function spriteFor(piece) {
    const sp = new THREE.Sprite(materialFor(piece.userData.type, piece.userData.side));
    sp.scale.setScalar(T.size);
    sp.renderOrder = 900;
    sp.castShadow = false;
    sp.receiveShadow = false;
    sp.raycast = () => {};   // never in drag.js's way
    return sp;
  }

  /** Degrees of the camera above the board plane, straight from the camera. */
  function elevation() {
    const p = view.camera.position;
    return Math.atan2(p.y, Math.hypot(p.x, p.z)) * 180 / Math.PI;
  }

  function update(dt) {
    // 32 men at most, so this is a plain walk of the live map: a man who has
    // been taken is out of it already, and a promoted one is a new object.
    seen.clear();
    for (const piece of pieces.pieces.values()) {
      seen.add(piece);
      let sp = sprites.get(piece);
      if (!sp) { sp = spriteFor(piece); sprites.set(piece, sp); group.add(sp); }
      // Straight down, a glyph floating a whole man's height above the board
      // lands off to one side of him, so the float shrinks as the view rises.
      const h = ((piece.userData.scaleBase || 1) + T.lift) * (1 - T.settle * shown);
      sp.position.set(piece.position.x, piece.position.y + h, piece.position.z);
    }
    for (const [piece, sp] of sprites) {
      if (seen.has(piece)) continue;
      group.remove(sp);
      sprites.delete(piece);
    }

    const want = smooth(clamp01((elevation() - T.fadeLo) / (T.fadeHi - T.fadeLo)));
    if (prefersReducedMotion()) shown = want;
    else shown += (want - shown) * (1 - Math.exp(-T.fadeRate * Math.max(0, dt)));
    if (Math.abs(want - shown) < 0.003) shown = want;
    group.visible = shown > 0.004;
    if (group.visible) for (const mat of materials.values()) mat.opacity = shown;
  }

  // The renderer calls this once a frame, with the camera already placed for
  // the frame. Anything that was on the scene before keeps its turn.
  const previous = view.scene.onBeforeRender;
  function tick() {
    const now = performance.now();
    const dt = last ? Math.min(0.1, (now - last) / 1000) : 0;
    last = now;
    update(dt);
  }
  view.scene.onBeforeRender = function onBeforeRender(...args) {
    if (previous) previous.apply(this, args);
    tick();
  };

  return {
    group,
    update,
    /** 0 when they are out of sight, 1 when they are fully in. */
    shown() { return shown; },
    elevation,
    dispose() {
      view.scene.onBeforeRender = previous || (() => {});
      for (const sp of sprites.values()) group.remove(sp);
      sprites.clear();
      for (const mat of materials.values()) { mat.map?.dispose(); mat.dispose(); }
      materials.clear();
      view.scene.remove(group);
    },
  };
}
