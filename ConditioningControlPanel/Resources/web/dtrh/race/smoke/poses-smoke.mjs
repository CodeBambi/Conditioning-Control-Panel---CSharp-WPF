/* ============================================================================
 * race/smoke/poses-smoke.mjs - node smoke for race/emiPoses.js.
 *
 *   node race/smoke/poses-smoke.mjs      (exits 0 on pass, 1 on the first failure)
 *
 * emiPoses.js imports nothing, so this needs no three and no DOM: it stands up a
 * fake glb root (getObjectByName over the contract nodes, each with a rotation /
 * position / scale of plain numbers) and drives the layer on a fixed 1/60 step.
 *
 * It checks:
 *   1. every POSES entry names only the four contract pivots, with 3 numbers each,
 *      and a `next` that exists in the table;
 *   2. the springs settle: 4 seconds of `cheer` lands on the preset's numbers;
 *   3. the hold timer returns to `cruise` on its own;
 *   4. sided presets mirror (L and R swap, y and z and the root lean negate);
 *   5. the tuck's antDown is exactly zero upright and non-zero inverted, and the
 *      layer never touches a pivot the model does not have.
 * ==========================================================================*/

import { POSES, PIVOTS, resolvePose, createPoseLayer } from '../emiPoses.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const near = (a, b, eps = 1e-3) => Math.abs(a - b) <= eps;

/* ---- a fake glb root ---------------------------------------------------- */
const NODES = ['shoulderL', 'shoulderR', 'footL', 'footR', 'ant0'];
function makeNode(name) {
  return { name, rotation: { x: 0, y: 0, z: 0, set(a, b, c) { this.x = a; this.y = b; this.z = c; } } };
}
function makeModel(names = NODES) {
  const kids = new Map(names.map((n) => [n, makeNode(n)]));
  return {
    kids,
    rotation: { x: 0, y: 0, z: 0 }, position: { x: 0, y: 0, z: 0 },
    scale: { x: 1, y: 1, z: 1, set(a, b, c) { this.x = a; this.y = b; this.z = c; } },
    getObjectByName: (n) => kids.get(n) || null,
  };
}
const CTX = { t: 0, up: { x: 0, y: 1, z: 0 }, right: { x: 1, y: 0, z: 0 }, tangent: { x: 0, y: 0, z: 1 } };
// emi.js writes the antenna's mood rotation fresh every frame and THEN calls the layer, so the
// harness does the same: anything else lets the layer's `+=` on ant0 pile up frame over frame.
const run = (layer, sec, ctx = CTX, pre = null) => {
  for (let i = 0; i < Math.round(sec * 60); i++) { if (pre) pre(); layer.update(1 / 60, ctx); }
};

/* ---- 1. the table ------------------------------------------------------- */
const KEYS = new Set(['sided', 'w', 'zeta', 'hold', 'next', 'breath', 'fraught', 'antDown', 'root', ...PIVOTS]);
for (const [name, p] of Object.entries(POSES)) {
  const bad = Object.keys(p).filter((k) => !KEYS.has(k));
  ok(bad.length === 0, `${name}: names only contract keys${bad.length ? ' (stray: ' + bad.join(', ') + ')' : ''}`);
  for (const k of PIVOTS) {
    if (p[k] === undefined) continue;
    ok(Array.isArray(p[k]) && p[k].length === 3 && p[k].every((v) => typeof v === 'number' && isFinite(v)), `${name}.${k}: three finite radians`);
  }
  ok(p.w > 0 && p.zeta > 0 && p.zeta < 1, `${name}: spring overshoots (0 < zeta < 1)`);
  ok(!p.next || !!POSES[p.next], `${name}: next '${p.next || ''}' exists`);
  ok(!(p.hold === 0 && p.next), `${name}: a held-forever pose has no next`);
}
ok(!!POSES.cruise && !POSES.cruise.hold, 'cruise is the resting pose and never times out');

/* ---- 2. the springs settle ---------------------------------------------- */
{
  const m = makeModel(), L = createPoseLayer(m);
  L.set('cheer', { hold: 99 });
  run(L, 4);
  const c = POSES.cheer;
  ok(near(m.kids.get('shoulderL').rotation.x, c.shoulderL[0]), 'cheer settles shoulderL.x on the preset');
  ok(near(m.kids.get('shoulderR').rotation.z, c.shoulderR[2]), 'cheer settles shoulderR.z on the preset');
  ok(near(m.rotation.x, c.root.tilt), 'cheer settles the root tilt');
  ok(near(m.scale.y, c.root.squash, 5e-3), 'cheer settles the root squash');
  const finite = [m.rotation.x, m.rotation.z, m.position.y, m.scale.x, m.scale.y].every(Number.isFinite);
  ok(finite, 'no NaN anywhere in the root after 4 s');
}

/* ---- 3. the hold returns to cruise -------------------------------------- */
{
  const m = makeModel(), L = createPoseLayer(m);
  ok(L.set('landing'), 'set("landing") takes');
  ok(L.name === 'landing', 'the layer reports the pose it is holding');
  run(L, 0.1);
  ok(L.name === 'landing', 'still landing inside the hold');
  run(L, POSES.landing.hold + 3);
  ok(L.name === 'cruise', 'landing falls back to cruise once the hold runs out');
  ok(near(m.kids.get('shoulderL').rotation.x, POSES.cruise.shoulderL[0]), 'and the arms settle back on cruise');
  ok(near(m.scale.y, 1, 5e-3), 'and the squash comes back to 1');
  ok(L.set('nope') === false, 'an unknown pose name is ignored, not blanked');
  ok(L.name === 'cruise', 'and leaves the current pose alone');
  // boost chains through boostOut on its own
  L.set('boost');
  run(L, POSES.boost.hold + 0.05);
  ok(L.name === 'boostOut', 'boost chains into boostOut');
  run(L, POSES.boostOut.hold + 0.1);
  ok(L.name === 'cruise', 'and boostOut lands back on cruise');
}

/* ---- 4. sided presets mirror -------------------------------------------- */
{
  const R = resolvePose('drift', { side: 1 }), Lft = resolvePose('drift', { side: -1 });
  ok(near(Lft.shoulderL[0], R.shoulderR[0]), 'drift mirrors: L takes R.x');
  ok(near(Lft.shoulderL[2], -R.shoulderR[2]), 'drift mirrors: and negates z');
  ok(near(Lft.root.lean, -R.root.lean), 'drift mirrors the root lean');
  const t3 = resolvePose('drift', { side: 1, tier: 3 });
  ok(Math.abs(t3.root.lean) > Math.abs(R.root.lean), 'a fatter drift tier leans harder');
  ok(near(resolvePose('cheer', { side: -1 }).root.lean, resolvePose('cheer').root.lean), 'an unsided preset ignores side');
  ok(resolvePose('clamp').fraught === 1, 'clamp reports fraught to emi.js');
  ok(resolvePose('landingKerb').fraught > 0, 'a kerbed landing is fraught too');
}

/* ---- 5. antDown, and a half-finished pack ------------------------------- */
{
  const m = makeModel(), L = createPoseLayer(m), ant = m.kids.get('ant0');
  L.set('tuck'); run(L, 2, CTX, () => { ant.rotation.x = 0; ant.rotation.z = 0; });
  ok(near(ant.rotation.x, 0) && near(ant.rotation.z, 0), 'upright, the tuck leaves the antenna base alone');
  const inv = { t: 0, up: { x: 0, y: -1, z: 0 }, right: { x: 0.6, y: -0.4, z: 0 }, tangent: { x: 0, y: 0.3, z: 0.9 } };
  const zero = () => { ant.rotation.x = 0; ant.rotation.z = 0; };
  run(L, 2, inv, zero);
  ok(Math.abs(ant.rotation.z) > 0.05, 'inverted, the antenna base falls toward the real floor');
  ok(Math.abs(ant.rotation.x) <= 1.2 && Math.abs(ant.rotation.z) <= 1.2, 'and never past the ANT_MAX clamp');
  ok(POSES.tuck.hold === 0, 'the tuck holds until the wheel lets go');

  const bare = makeModel(['shoulderL']), B = createPoseLayer(bare);
  B.set('cheer'); run(B, 1);
  ok(Number.isFinite(bare.kids.get('shoulderL').rotation.x), 'a pack missing three pivots still drives the one it has');
  ok(createPoseLayer(null).update(1 / 60, CTX) === undefined, 'no model at all is a no-op, not a throw');
}

console.log(fails ? `\n${fails} failure(s)` : '\nposes-smoke: all good');
process.exit(fails ? 1 : 0);
