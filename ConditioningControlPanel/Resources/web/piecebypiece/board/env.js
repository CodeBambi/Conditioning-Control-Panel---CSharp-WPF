/* ============================================================================
 * board/env.js - the room the toys reflect.
 *
 * The men are silicone under a clearcoat and the king and queen wear real
 * metal, and metal with nothing to mirror is flat paint: the crown read as a
 * dark shape sitting on a head rather than gold. So the board gets a room to
 * look at. It is not a picture anyone sees, it is a tiny scene rendered once
 * into a cube map: a dark plum box, a big cream panel standing where the key
 * light comes from, a pink one opposite it where the rim light is, a soft
 * ceiling, and a faint warm glow off the floor so an underside has something to
 * pick up. The two big ones stand at the height of a man rather than hanging
 * overhead, because a crown is a standing band and mirrors sideways.
 *
 * It goes on `scene.environment`, never `scene.background`. The backdrop stays
 * the flat fog colour the board has always sat in, so nothing about the look of
 * the page changes; only what the shiny bits have to reflect.
 *
 * Wiring: boot.js builds one of these after the scene, and pumps update(dt) so
 * a man that was rebuilt (a promotion, the glb art arriving) is dressed too.
 * The reflection strength is per material, because silicone is not chrome: the
 * body takes a hint of the room and the jewellery takes all of it. The board
 * and the plinth set their own, in scene.js, where they are built.
 *
 * The one sharp edge: three only reads a material's own `envMapIntensity` when
 * that material owns an `envMap`. A material lit by `scene.environment` alone
 * has its intensity uniform overwritten with `scene.environmentIntensity`, so
 * every surface in the game would take the room at exactly the same strength.
 * That is why dressing hands each material the texture as well as the number.
 * ==========================================================================*/

import * as THREE from 'three';

/** Every number the room is made of. One place, on purpose. */
export const ENV = Object.freeze({
  wall: 0x241833,          // the plum box everything sits inside
  wallGain: 0.9,
  creamColor: 0xFFF2E2,    // the big soft panel on the key side
  creamGain: 2.9,          // above 1 on purpose: this is what makes a highlight
  creamSize: [10.0, 7.2],
  creamAt: [4.4, 3.1, 4.6],   // at the height of a man, not up in the ceiling:
                              // a crown is a standing band and reflects sideways
  pinkColor: 0xFF7FC2,     // and the one facing it, on the rim light's side
  pinkGain: 1.6,
  pinkSize: [8.4, 5.6],
  pinkAt: [-5.2, 2.5, -5.0],
  topColor: 0xFFF6EC,      // a soft ceiling, for the top of a crown and a tiara
  topGain: 1.15,
  topSize: [13.0, 13.0],
  topAt: [0.8, 9.0, 1.2],
  floorColor: 0x8A6BA8,    // the bounce off the plinth, so a flank is never void
  floorGain: 0.32,
  floorSize: 14,
  floorAt: -3.4,
  blur: 0.035,             // PMREM sigma; the panels want edges, not hard ones
  skin: 0.48,              // how much room a silicone body takes (subtle)
  metal: 1.0,              // and how much the crown and the tiara take (all)
  loose: 0.14,             // anything nobody has dressed, the board included
  sweep: 0.3,              // s between passes that look for undressed materials
});

/** One flat panel, aimed at the middle of the board. `gain` may exceed 1. */
function panel(scene, hex, gain, size, at) {
  const color = new THREE.Color(hex).multiplyScalar(gain);
  const mesh = new THREE.Mesh(
    new THREE.PlaneGeometry(size[0], size[1]),
    new THREE.MeshBasicMaterial({ color, side: THREE.DoubleSide, toneMapped: false }),
  );
  mesh.position.set(at[0], at[1], at[2]);
  mesh.lookAt(0, 0, 0);
  scene.add(mesh);
  return mesh;
}

/** The scene that only ever gets rendered into a cube map. */
function buildRoom() {
  const room = new THREE.Scene();
  const wall = new THREE.Mesh(
    new THREE.BoxGeometry(40, 26, 40),
    new THREE.MeshBasicMaterial({
      color: new THREE.Color(ENV.wall).multiplyScalar(ENV.wallGain),
      side: THREE.BackSide, toneMapped: false,
    }),
  );
  room.add(wall);
  panel(room, ENV.creamColor, ENV.creamGain, ENV.creamSize, ENV.creamAt);
  panel(room, ENV.pinkColor, ENV.pinkGain, ENV.pinkSize, ENV.pinkAt);
  panel(room, ENV.topColor, ENV.topGain, ENV.topSize, ENV.topAt);
  const floor = new THREE.Mesh(
    new THREE.PlaneGeometry(ENV.floorSize, ENV.floorSize).rotateX(-Math.PI / 2),
    new THREE.MeshBasicMaterial({
      color: new THREE.Color(ENV.floorColor).multiplyScalar(ENV.floorGain),
      side: THREE.DoubleSide, toneMapped: false,
    }),
  );
  floor.position.y = ENV.floorAt;
  room.add(floor);
  return room;
}

function tearDown(room) {
  room.traverse((o) => {
    if (o.geometry) o.geometry.dispose();
    if (o.material) o.material.dispose();
  });
}

/**
 * Build the room, hang it on the scene, and hand back the dresser.
 *
 *   createEnv({ renderer, scene }) -> { texture, ms, dress, update, dispose }
 *
 * `ms` is what the one-off cost actually was on this machine, so a slow boot
 * has a number to point at.
 */
export function createEnv({ renderer, scene }) {
  const t0 = (typeof performance !== 'undefined' ? performance.now() : 0);
  const pmrem = new THREE.PMREMGenerator(renderer);
  const room = buildRoom();
  const rt = pmrem.fromScene(room, ENV.blur, 0.1, 80);
  tearDown(room);
  pmrem.dispose();
  scene.environment = rt.texture;
  scene.environmentIntensity = ENV.loose;
  const ms = (typeof performance !== 'undefined' ? performance.now() : 0) - t0;

  // A material is dressed once and remembered, so the sweep below is a walk
  // over about a hundred objects and nothing else.
  const done = new WeakSet();

  /**
   * How much room one material takes. The silicone knows itself: pieces.js
   * stamps `userData.pbpPatch = 'skin'` on the body material so jiggle.js can
   * tell a body from a jewel, and the same stamp answers this question. Every
   * other lit material in the piece group is jewellery, which is metal, and
   * metal takes the room whole.
   */
  function gainFor(material) {
    return material.userData && material.userData.pbpPatch === 'skin' ? ENV.skin : ENV.metal;
  }

  /**
   * `own` is a material that already carries the strength it wants, which is
   * how the board and the plinth work: scene.js sets their numbers where they
   * are built, and all this hands over is the texture that makes three honour
   * them. Everything else is a man, and is given both.
   */
  function dressOne(material, own) {
    if (!material || done.has(material)) return false;
    done.add(material);
    if (!('envMapIntensity' in material)) return false;   // unlit, nothing to reflect
    if (!material.envMap) material.envMap = rt.texture;
    if (!own) material.envMapIntensity = gainFor(material);
    material.needsUpdate = true;
    return true;
  }

  function dressGroup(group, own) {
    let n = 0;
    if (!group) return n;
    group.traverse((o) => {
      const m = o.material;
      if (!m) return;
      if (Array.isArray(m)) { for (const one of m) if (dressOne(one, own)) n++; }
      else if (dressOne(m, own)) n++;
    });
    return n;
  }

  /** Every man standing right now. Safe to call as often as you like. */
  function dress(pieceGroup) { return dressGroup(pieceGroup, false); }

  /** The squares and the plinth, once: they keep the strength scene.js set. */
  function dressBoard(boardGroup) { return dressGroup(boardGroup, true); }

  // The men are rebuilt whenever the art arrives or a pawn promotes, and a new
  // build brings new materials. Rather than reach into pieces.js for a hook,
  // this looks for undressed ones a few times a second, which is cheap enough
  // to be invisible and cannot go stale.
  let since = ENV.sweep;
  function update(dt, pieceGroup) {
    since += dt;
    if (since < ENV.sweep) return 0;
    since = 0;
    return dress(pieceGroup);
  }

  function dispose() {
    if (scene.environment === rt.texture) scene.environment = null;
    rt.dispose();
  }

  return { texture: rt.texture, ms, dress, dressBoard, update, dispose };
}
