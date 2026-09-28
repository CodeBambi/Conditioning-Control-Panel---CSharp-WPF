/* ============================================================================
 * board/replay-shots.js - where each replay camera stands and what it looks at.
 *
 * The director (board/director.js) asks three things, once per panel per frame:
 *   pick(layout, clip)        -> the shots for this replay, one per panel
 *   aim(shot, cam, ctx)       -> place and aim `cam` (position, lookAt, fov)
 *   occluders(shot, cam, ctx) -> men to hide for this panel's render only
 *
 * ctx = { clip, clipT, t, i, n, layout, dN, perp, mid, panel, bw, bh }
 *   clip   { from, to, hit, duration, attacker, victim, objs, hitInfo, frames }
 *          (attacker/victim transforms are already set to clipT when aim runs)
 *   dN     unit vector from->to on the floor; perp is across it, toward the player
 *   panel  { cx, cy, w, h } the panel's bounding box, normalised 0..1 of the stage
 *   bw,bh  drawing-buffer size; the camera renders the whole buffer with a view
 *          offset that puts the stage centre on the panel centre
 *
 * FRAMING (owner, 2026-09-28: "the shots should get the action cleanly"). Every
 * shot is FITTED, not hand placed: the attacker and the victim over a window of
 * the clip round contact (and the victim's flight, for the wide ones) are
 * gathered as spheres and the lens is stood where they sit centred in the panel
 * and fill a planned share of it (board/replay-frame.js does the maths). The fit
 * is made once per panel, so the frame holds still and the action moves in it;
 * then the shot's own motion plays on top: a push toward the hit, a slow orbit,
 * a dolly zoom on impact, a turning overhead, a little handheld drift.
 * Men between the lens and the action, and men close enough to fill the frame,
 * are hidden for that panel only.
 * ==========================================================================*/
import * as THREE from 'three';
import { SHOT_BANK, FRAME, lensDir, fitFrame, clampLens, hides, motion, fovFor, pickShots, projectSpheres, basis }
  from './replay-frame.js';

const HEIGHT = { p: .62, n: .8, b: .9, r: .7, q: 1.02, k: 1.15 };   // as board/pieces.js, for a man without scaleBase
const v3 = a => [a.x, a.y, a.z];
const CEIL_TALL = .6;   // a knight's flip and the queen's stunts get this much more headroom

// A man's world column from a transform: base, top, radius.
const qa = new THREE.Quaternion(), va = new THREE.Vector3();
function column(obj, pos, quat, scale) {
  const u = obj.userData || {}, h0 = u.scaleBase || HEIGHT[u.type] || .8;
  const rest = u.artSource === 'placeholder' || !u.artSource ? h0 : 1;   // a lathe stands scaled up to its height
  const h = h0 * Math.max(.15, scale[1] / rest), r = FRAME.manR * Math.max(.5, Math.min(1.6, scale[0] / rest));
  va.set(0, h, 0).applyQuaternion(qa.set(quat[0], quat[1], quat[2], quat[3]));
  return { base: pos, top: [pos[0] + va.x, pos[1] + va.y, pos[2] + va.z], r };
}
const liveColumn = o => column(o, v3(o.position), [o.quaternion.x, o.quaternion.y, o.quaternion.z, o.quaternion.w], v3(o.scale));
const spheresOf = c => {
  const m = [(c.base[0] + c.top[0]) / 2, (c.base[1] + c.top[1]) / 2, (c.base[2] + c.top[2]) / 2];
  // the foot ball sits on the board, not half through it
  return [[c.base[0], c.base[1] + c.r * .6, c.base[2], c.r], [...m, c.r * 1.05], [...c.top, c.r * .8]];
};

export function createReplayShots({ group, random = Math.random } = {}) {
  const at = new THREE.Vector3(), fwd = new THREE.Vector3();
  const fits = new Map();      // panel index -> { key, shot, spec, fit }

  // ?shots=low,wide,xclose pins the shot names panel by panel, for a dev page or a screenshot harness.
  const pinned = (() => { try { return (new URLSearchParams(globalThis.location?.search || '').get('shots') || '').split(',').filter(k => SHOT_BANK[k]); } catch { return []; } })();
  function pick(layout, clip) {
    fits.clear();
    const out = pickShots(layout, clip?.hitInfo || null, random);
    pinned.forEach((name, i) => { if (out[i]) out[i] = { ...out[i], name, size: SHOT_BANK[name].size,
      el: (SHOT_BANK[name].el[0] + SHOT_BANK[name].el[1]) / 2, az: (SHOT_BANK[name].az[0] + SHOT_BANK[name].az[1]) / 2 }; });
    return out;
  }

  // Where the blow lands: the hit's own contact point, else the victim's square.
  function contactOf(clip) {
    const w = clip.hitInfo?.world;
    return w ? [w.x, Math.max(.15, w.y), w.z] : [clip.to.x, .4, clip.to.z];
  }
  function frameNear(clip, t) {
    let best = null, bd = Infinity;
    for (const x of clip.frames || []) { const d = Math.abs(x.t - t); if (d < bd) { bd = d; best = x; } }
    return best;
  }
  const stateCol = (o, s) => column(o, s.tr.slice(0, 3), s.tr.slice(3, 7), s.tr.slice(7, 10));

  // The subject, as spheres, over the shot's window of the clip.
  function subjectFor(spec, clip) {
    const c = contactOf(clip);
    if (spec.subject === 'contact') {
      // the contact, plus the two heads that meet there
      const out = [[...c, FRAME.contactR]];
      const f = frameNear(clip, clip.hit);
      for (const o of [clip.victim]) {
        const s = f && o && f.states.get(o);
        if (!s || !s.visible || !s.inGroup) continue;
        const top = stateCol(o, s).top;
        if (Math.hypot(top[0] - c[0], top[1] - c[1], top[2] - c[2]) < .9) out.push([...top, .22]);
      }
      return out;
    }
    const out = [];
    const [lo, hi] = spec.window;
    const tall = clip.hitInfo?.piece === 'n' || clip.hitInfo?.piece === 'q';
    const ceil = spec.ceil + (tall ? CEIL_TALL : 0);
    for (const f of clip.frames || []) {
      const u = f.t - clip.hit;
      if (u < lo || u > hi) continue;
      for (const o of [clip.attacker, clip.victim]) {
        const s = o && f.states.get(o);
        if (!s || !s.visible || !s.inGroup) continue;
        const pos = s.tr.slice(0, 3);
        if (o === clip.victim && u > .05) {
          if (!spec.flight && u > .25) continue;               // a close shot does not chase the flight
          if (Math.hypot(pos[0] - c[0], pos[2] - c[2]) > FRAME.flightReach) continue;
        }
        for (const q of spheresOf(stateCol(o, s))) { q[1] = Math.min(q[1], ceil); out.push(q); }
      }
    }
    if (!out.length) out.push([...c, .6]);
    return out;
  }

  // The middle of the action: halfway between the contact and the middle of the two
  // men as they meet. Every shot looks straight at it, so the blow is centred.
  function actionCentre(spec, clip) {
    const c = contactOf(clip);
    if (spec.subject === 'contact') return c;
    const near = subjectFor({ ...spec, window: [-.15, .15], flight: false }, clip);
    const lo = [0, 1, 2].map(k => Math.min(...near.map(q => q[k] - q[3]))), hi = [0, 1, 2].map(k => Math.max(...near.map(q => q[k] + q[3])));
    const m = [0, 1, 2].map(k => (lo[k] + hi[k]) / 2);
    return [(m[0] + c[0]) / 2, Math.min(spec.ceil, (m[1] + c[1]) / 2), (m[2] + c[2]) / 2];
  }

  function fitFor(shot, cam, ctx) {
    const { clip, panel, i } = ctx;
    const spec = SHOT_BANK[shot.name] || SHOT_BANK.close;
    const key = `${panel.w.toFixed(3)}:${panel.h.toFixed(3)}:${cam.aspect.toFixed(3)}`;
    const had = fits.get(i);
    if (had && had.key === key && had.shot === shot) return had;
    const dir = lensDir(shot.el, shot.az, v3(ctx.dN), v3(ctx.perp));
    const fit = fitFrame({ spheres: subjectFor(spec, clip), dir, fov: spec.fov, aspect: cam.aspect, panel, share: spec.share,
      centre: actionCentre(spec, clip) });
    const entry = { key, shot, spec, fit };
    fits.set(i, entry);
    return entry;
  }

  function aim(shot, cam, ctx) {
    if (typeof shot === 'string') shot = { name: shot, az: 0, el: SHOT_BANK[shot]?.el[0] ?? .3, seed: .5 };
    const { clip, clipT, dN, perp } = ctx;
    const { spec, fit } = fitFor(shot, cam, ctx);
    const m = motion(spec.motion, clipT - clip.hit, shot.seed);
    const dir = lensDir(Math.min(1.4, shot.el + m.el), shot.az + m.az, v3(dN), v3(perp));
    const d = fit.dist * m.dist, t = fit.target;
    let pos = clampLens([t[0] + (dir[0] + m.drift[0]) * d, t[1] + (dir[1] + m.drift[1]) * d, t[2] + (dir[2] + m.drift[2]) * d]);
    // Never inside the attacker or the victim (they are the two men never hidden).
    for (const o of [clip.attacker, clip.victim]) {
      if (!o || !o.visible || !o.parent) continue;
      const c = liveColumn(o), keep = c.r + .35;
      const dx = pos[0] - c.base[0], dz = pos[2] - c.base[2], hd = Math.hypot(dx, dz);
      if (hd < keep && pos[1] < Math.max(c.base[1], c.top[1]) + .3) {
        pos = hd > 1e-4 ? [c.base[0] + dx * keep / hd, pos[1], c.base[2] + dz * keep / hd]
          : [c.base[0] + dir[0] * keep, pos[1], c.base[2] + dir[2] * keep];
      }
    }
    cam.position.set(pos[0], pos[1], pos[2]);
    at.set(t[0] + m.drift[0] * d * .5, t[1] + m.drift[1] * d * .5, t[2] + m.drift[2] * d * .5);
    cam.fov = fovFor(spec.fov, m.fov);
    cam.lookAt(at);
  }

  // Men standing between the lens and the action, or so near the lens they fill it.
  function occluders(shot, cam, ctx) {
    const { clip, panel } = ctx;
    const pos = v3(cam.position);
    fwd.set(0, 0, -1).applyQuaternion(cam.quaternion).add(cam.position);
    const lens = { pos, ...basis(pos, v3(fwd)), fov: cam.fov, aspect: cam.aspect };
    const spheres = [[...contactOf(clip), .2]];
    for (const o of [clip.attacker, clip.victim]) if (o && o.visible && o.parent) spheres.push(...spheresOf(liveColumn(o)));
    const subject = projectSpheres(spheres, lens, panel);
    const out = [];
    for (const p of group.children) {
      if (!p.userData?.type || !p.visible || p === clip.attacker || p === clip.victim) continue;
      if (hides(liveColumn(p), lens, panel, subject)) out.push(p);
    }
    return out;
  }

  return { pick, aim, occluders };
}
