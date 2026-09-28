/* ============================================================================
 * board/replay-shots.js - where each replay camera stands and what it looks at.
 *
 * The director (board/director.js) asks three things, once per panel per frame:
 *   pick(layout, clip)        -> the shot names for this replay, one per panel
 *   aim(shot, cam, ctx)       -> place and aim `cam` (position, lookAt, fov)
 *   occluders(shot, cam, ctx) -> men to hide for this panel's render only
 *
 * ctx = { clip, clipT, t, i, n, layout, dN, perp, mid, panel, bw, bh }
 *   clip   { from, to, hit, duration, attacker, victim, objs, hitInfo }
 *          (attacker/victim transforms are already set to clipT when aim runs)
 *   dN     unit vector from->to on the floor; perp is across it, toward the player
 *   panel  { cx, cy, w, h } the panel's bounding box, normalised 0..1 of the stage
 *   bw,bh  drawing-buffer size; the camera renders the whole buffer with a view
 *          offset that puts the stage centre on the panel centre
 * ==========================================================================*/
import * as THREE from 'three';
import { SHOTS } from './replay-plan.js';

const clamp01 = v => Math.max(0, Math.min(1, v));

export function createReplayShots({ group, random = Math.random } = {}) {
  const at = new THREE.Vector3();
  const back = new THREE.Vector3();

  function pick(layout /* , clip */) { return SHOTS[layout].slice(); }

  function aim(shot, cam, ctx) {
    const { clip, clipT, dN, mid, perp } = ctx;
    const prog = clamp01((clipT - (clip.hit - 1)) / 2.4);
    const to = clip.to;
    if (shot === 'low') {
      cam.position.copy(mid).addScaledVector(perp, 3.3 - .5 * prog).addScaledVector(dN, -.3 + .7 * prog); cam.position.y = .55;
      at.copy(mid).addScaledVector(dN, .45 * prog); at.y = .6;
      cam.fov = 34;
    } else if (shot === 'chase') {
      const a = clip.attacker.position;
      cam.position.copy(a).addScaledVector(dN, -2.1).addScaledVector(perp, .55); cam.position.y = 1.35 + Math.max(0, a.y) * .4;
      at.copy(to); at.y = .6;
      cam.fov = 48;
    } else {
      cam.position.copy(to).addScaledVector(dN, -1.25).addScaledVector(perp, .7 + .6 * prog); cam.position.y = 6.9 - 1.1 * prog;
      at.copy(to).addScaledVector(dN, -.4); at.y = 0;
      cam.fov = 32;
    }
    clearOfMen(cam.position, shot === 'low' ? perp : shot === 'chase' ? back.copy(dN).negate() : null, clip);
    cam.lookAt(at);
  }

  // A replay camera must never stand inside a man: his outline shell would wrap the
  // lens and wash the whole panel out. Step back along `away`, then up, until clear.
  function clearOfMen(pos, away, clip) {
    const blocked = () => group.children.some(p => p.userData.type && p !== clip.attacker && p !== clip.victim
      && Math.hypot(p.position.x - pos.x, p.position.z - pos.z) < .8 && pos.y < 2.1);
    for (let i = 0; i < 4 && away && blocked(); i++) pos.addScaledVector(away, .4);
    for (let i = 0; i < 6 && blocked(); i++) pos.y += .35;
  }

  function occluders(/* shot, cam, ctx */) { return []; }

  return { pick, aim, occluders };
}
