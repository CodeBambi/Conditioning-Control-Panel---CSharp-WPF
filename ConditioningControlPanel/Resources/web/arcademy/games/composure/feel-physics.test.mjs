import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { createWorld, PIECES } from './feel-physics.js';
const context = {};
runInNewContext(readFileSync(new URL('./feel-vendor/matter-0.20.0.min.js', import.meta.url), 'utf8'), context);
const M = context.Matter;
const advance = (world, frames = 360) => { for (let i = 0; i < frames; i++) world.step(); };

test('six distinctive pieces use the pinned offline physics engine', () => {
  assert.equal(M.version, '0.20.0');
  assert.equal(new Set(PIECES.map(p => p.name)).size, 6);
});
test('foundation lands and genuinely settles on the platform', () => {
  const world = createWorld(M), body = world.make(0);
  assert.equal(world.drop(body, 0), true); advance(world);
  assert.equal(world.settled(), true);
  assert.ok(body.position.y > 325 && body.position.y < 332);
  assert.ok(Math.abs(body.angle) < .02); world.destroy();
});
test('overlapping placement is refused, preserving the existing sculpture', () => {
  const world = createWorld(M); world.drop(world.make(0), 0); advance(world);
  const overlap = world.make(1, 180, 328);
  assert.equal(world.drop(overlap, 1), false);
  assert.equal(world.pieces.size, 1); world.destroy();
});
test('a fallen piece returns alone and leaves the foundation in place', () => {
  const world = createWorld(M); world.drop(world.make(0), 0); advance(world);
  world.drop(world.make(1, 320, 100), 1);
  const recovered = [];
  for (let i = 0; i < 180; i++) recovered.push(...world.step());
  assert.deepEqual(recovered, [1]); assert.equal(world.pieces.has(0), true);
  assert.equal(world.settled(), true); world.destroy();
});
test('taking a piece enables recovery and rotation without duplicate bodies', () => {
  const world = createWorld(M); world.drop(world.make(0), 0); advance(world);
  const body = world.take(0); assert.equal(world.pieces.size, 0);
  M.Body.setPosition(body, { x: 180, y: 100 }); M.Body.rotate(body, Math.PI / 2);
  assert.ok(body.bounds.max.y - body.bounds.min.y > 100);
  assert.equal(world.drop(body, 0), true); assert.equal(world.drop(body, 0), false);
  world.destroy(); assert.equal(M.Composite.allBodies(world.engine.world).length, 0);
});
test('all six pieces can form a stable physical sculpture', () => {
  const world = createWorld(M);
  for (let id = 0; id < 6; id++) {
    const surface = id ? Math.min(...[...world.pieces.values()].map(b => b.bounds.min.y)) : 344;
    const body = world.make(id, 180, 70), halfHeight = (body.bounds.max.y - body.bounds.min.y) / 2;
    M.Body.setPosition(body, { x: 180, y: surface - halfHeight - 8 });
    assert.equal(world.drop(body, id), true); advance(world, 240);
  }
  assert.equal(world.pieces.size, 6); assert.equal(world.settled(), true); world.destroy();
});
