/* ============================================================================
 * board/rim.js - the files and the ranks, cut into the plinth.
 *
 * The board had no coordinates at all, so "e4" meant counting squares from a
 * corner. These are four thin strips lying on the plinth's top rim, just
 * outside the squares: a to h along the near and far edges, 1 to 8 down the
 * left and the right. They are canvas text on unlit planes, cream at a low
 * alpha, so they read as something cut into the plinth rather than a label
 * printed over the game.
 *
 * Every strip is turned so it reads upright from its OWN side of the board.
 * A plane rotated flat has its texture pointing at -Z, which is exactly right
 * for a player sitting at +Z; the other three are spun about Y to match, and
 * their labels are handed over in the order they come out along the strip
 * after that spin, which is why two of the four arrays run backwards.
 *
 * They never take a raycast and they neither cast nor receive a shadow: a
 * coordinate is a thing you read, never a thing you can pick up. Drawn before
 * the hint markers, so a dot or a ring on the a file still sits over the top.
 *
 * Wiring: boot.js builds one of these onto view.boardGroup, and never has to
 * touch it again. Nothing about it moves, so there is no update.
 * ==========================================================================*/

import * as THREE from 'three';

export const FILES = 'abcdefgh';

/** Every number the coordinates are made of. One place, on purpose. */
export const TUNING = Object.freeze({
  half: 4.0,            // the squares end here: 8 of them, 1.0 each, centred
  at: 4.16,             // where the line of text sits, out from the centre
  depth: 0.30,          // and how deep the strip is: it must clear the squares,
                        // so `at` is never less than half + depth / 2
  y: -0.076,            // the plinth top is -0.08; this sits just proud of it
  cell: 192,            // canvas pixels per square
  rows: 58,             // and how tall the strip is, at the same 0.30 ratio
  font: '600 40px ui-monospace, "Cascadia Mono", "Consolas", monospace',
  ink: 'rgba(255, 242, 220, 0.95)',   // cream, lowercase, tabular by the font
  cut: 'rgba(24, 16, 40, 0.55)',      // the shadow in the groove, below the ink
  cutPx: 3,
  opacity: 0.5,         // and how much of all that actually lands on the plinth
});

const T = TUNING;
const NOPE = () => {};        // a coordinate is never the thing a pointer picks

/** One strip's picture: `labels` drawn left to right, one per square. */
function stripTexture(labels) {
  const cv = document.createElement('canvas');
  cv.width = T.cell * labels.length;
  cv.height = T.rows;
  const ctx = cv.getContext('2d');
  ctx.font = T.font;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  const y = T.rows * 0.5;
  for (let i = 0; i < labels.length; i++) {
    const x = T.cell * (i + 0.5);
    ctx.fillStyle = T.cut;
    ctx.fillText(labels[i], x, y + T.cutPx);
    ctx.fillStyle = T.ink;
    ctx.fillText(labels[i], x, y);
  }
  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

/**
 * The coordinates. `group` is the board group, so they travel with the board.
 *
 *   createRim({ group }) -> { root, dispose }
 */
export function createRim({ group }) {
  const root = new THREE.Group();
  root.name = 'pbp-rim';
  root.raycast = NOPE;
  group.add(root);

  const width = T.half * 2;
  const geo = new THREE.PlaneGeometry(width, T.depth).rotateX(-Math.PI / 2);
  const mid = Math.max(T.at, T.half + T.depth / 2);
  const made = [];

  function strip(labels, x, z, spin) {
    const map = stripTexture(labels);
    const material = new THREE.MeshBasicMaterial({
      map, transparent: true, opacity: T.opacity, depthWrite: false,
      fog: false, toneMapped: false, side: THREE.DoubleSide,
    });
    const mesh = new THREE.Mesh(geo, material);
    mesh.position.set(x, T.y, z);
    mesh.rotation.y = spin;
    mesh.raycast = NOPE;
    mesh.castShadow = false;
    mesh.receiveShadow = false;
    mesh.renderOrder = 1;
    root.add(mesh);
    made.push({ map, material });
    return mesh;
  }

  // Flat, the texture reads toward -Z, so the near edge needs no spin at all
  // and the files run a..h with the world's own +X. Every other side is that
  // same strip turned to face its own player, and the labels are reversed
  // wherever the spin reverses the direction they come out in.
  const files = FILES.split('');
  const ranks = ['1', '2', '3', '4', '5', '6', '7', '8'];
  strip(files, 0, mid, 0);                          // near edge, white's side
  strip([...files].reverse(), 0, -mid, Math.PI);    // far edge, black's side
  strip([...ranks].reverse(), -mid, 0, -Math.PI / 2); // left, rank 8 at -z
  strip(ranks, mid, 0, Math.PI / 2);                // right, rank 1 at +z

  function dispose() {
    for (const { map, material } of made) { map.dispose(); material.dispose(); }
    made.length = 0;
    geo.dispose();
    if (root.parent) root.parent.remove(root);
  }

  return { root, dispose };
}
