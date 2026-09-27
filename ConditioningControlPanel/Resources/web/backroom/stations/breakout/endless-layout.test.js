import { test } from 'node:test';
import assert from 'node:assert/strict';
import { endlessBoard, seededRandom } from './endless-layout.js';
import { advancePendulum, curtainPose } from './pendulum.js';
import { tidePose } from './tide.js';

const signature = board => JSON.stringify(board);
test('resume recreates exact geometry independently of other boards and physics randomness', () => {
  for (let seed = 0; seed < 200; seed++) {
    const a = endlessBoard(seed, 17), rng = seededRandom(seed);
    for (let i = 0; i < 50; i++) rng();
    endlessBoard(seed, 10000);
    assert.equal(signature(a), signature(endlessBoard(seed, 17)));
  }
  assert.notEqual(signature(endlessBoard(10, 17)), signature(endlessBoard(11, 17)));
  assert.deepEqual(Array.from({ length:50 }, seededRandom('same')), Array.from({ length:50 }, seededRandom('same')));
});

test('seeded schedules offer every family, no immediate repeats, and a regular breather', () => {
  for (let seed = 0; seed < 200; seed++) {
    const kinds = new Set(), pairs = new Set(); let previous;
    for (let i = 0; i < 40; i++) {
      const b = endlessBoard(seed, i); kinds.add(b.kind); pairs.add(b.mechanics.join('+'));
      assert.notEqual(b.kind, previous); previous = b.kind;
      assert.equal(b.breather, i % 4 === 3);
      assert.ok(b.bricks.length >= 40 && b.bricks.length <= 110, `${b.kind} count ${b.bricks.length}`);
      assert.ok(b.bricks.filter(x => !x.pendulumAnchor && !x.strength).length >= 30);
      assert.ok(b.bricks.filter(x => x.split).length <= 3, 'authored multiball stays sparse');
    }
    assert.equal(kinds.size, 11);
    for (const pair of ['dome+pendulums', 'dome+tide', 'tide+pendulums', 'reform+dome']) assert.ok(pairs.has(pair));
  }
});

test('the opening showcase has two reachable releases around an open spiral', () => {
  for (let seed = 0; seed < 200; seed++) {
    const b = endlessBoard(seed, 0);
    assert.equal(b.dome, true); assert.equal(b.pendulums.length, 2);
    for (const [id, p] of b.pendulums.entries()) {
      assert.equal(p.id, id); assert.ok(p.struck instanceof Set);
      assert.ok(Math.hypot(p.x - 640, p.y - 360) < 205, 'bob can enter the spiral on release');
      assert.equal(b.bricks.filter(x => x.pendulumAnchor && x.pendulumId === id).length, 1);
      assert.equal(b.bricks.filter(x => x.curtain && x.pendulumId === id).length, 16);
    }
    for (const brick of b.bricks) assert.ok(Math.hypot(brick.x + brick.w / 2 - 640, brick.y + brick.h / 2 - 360) > 150);
  }
});

test('all generated collision faces remain finite, in court, and clear of the paddle', () => {
  for (let seed = 0; seed < 200; seed++) for (let i = 0; i < 20; i++) {
    const b = endlessBoard(seed, i);
    for (const brick of b.bricks) {
      for (const key of ['x', 'y', 'w', 'h', 'row', 'col']) assert.ok(Number.isFinite(brick[key]));
      const co = Math.abs(Math.cos(brick.angle || 0)), si = Math.abs(Math.sin(brick.angle || 0));
      const rx = (brick.w * co + brick.h * si) / 2, ry = (brick.w * si + brick.h * co) / 2;
      const cx = brick.x + brick.w / 2, cy = brick.y + brick.h / 2;
      assert.ok(cx - rx >= 10 && cx + rx <= 1270 && cy - ry >= 10 && cy + ry <= 530, b.kind);
    }
  }
});

test('moving pieces keep their own identity and leave static machinery separate', () => {
  const boards = Array.from({ length:24 }, (_, i) => endlessBoard(77, i));
  for (const b of boards) {
    assert.equal(!!b.tide, b.bricks.some(x => x.endlessTide));
    assert.equal(!!b.reform, b.bricks.some(x => x.endlessReform));
    if (b.reform) assert.ok(b.bricks.every(x => x.endlessReform && !x.curtain && !x.endlessTide));
    for (const brick of b.bricks.filter(x => x.endlessTide)) {
      const pose = tidePose(brick.row, brick.col, 0);
      for (const key of ['x','y','w','h','angle']) assert.ok(Math.abs(brick[key] - pose[key]) < 1e-9);
    }
    for (const p of b.pendulums || []) {
      p.energy = 1;
      for (let i = 0; i < 300; i++) {
        advancePendulum(p, .025, 1280, 720, false);
        assert.ok(p.x - p.r > 0 && p.x + p.r < 1280 && p.y + p.r < 530);
        for (const brick of b.bricks.filter(x => x.curtain && x.pendulumId === p.id)) {
          const pose = curtainPose(p, brick.curtainRow, brick.curtainCol);
          assert.ok(pose.x > 0 && pose.x + pose.w < 1280 && pose.y > 0 && pose.y + pose.h < 530);
        }
      }
    }
  }
});

test('fixed seeds vary shapes beyond repeating a finite list of walls', () => {
  const layouts = new Set();
  for (let seed = 0; seed < 200; seed++) layouts.add(signature(endlessBoard(seed, 0).bricks));
  assert.equal(layouts.size, 200);
  assert.throws(() => endlessBoard(1, -1), RangeError);
  assert.throws(() => endlessBoard(1, Infinity), RangeError);
});

test('small portal recipes keep both exit faces clear for balls and demolition weights', () => {
  for(let seed=0;seed<200;seed++)for(const index of [10,12,13]) {
    const b=endlessBoard(seed,index);
    assert.equal(b.portals.length,2);assert.equal(b.portals[0].pair,b.portals[1].pair);
    for(const p of b.portals)for(const r of [8,26])for(const sign of [-1,0,1])for(const face of [-1,1]) {
      assert.equal(p.halfLength,65);assert.equal(p.depth,16);
      const tangent=(p.halfLength-r)*sign,nx=Math.cos(p.angle),ny=Math.sin(p.angle);
      const cx=p.x+nx*(r+p.depth+2)*face-ny*tangent,cy=p.y+ny*(r+p.depth+2)*face+nx*tangent;
      assert.ok(cx-r>0&&cx+r<1280&&cy-r>0&&cy+r<590,'full emergence stays in court above paddle');
      for(const brick of b.bricks) {
        const co=Math.cos(brick.angle||0),si=Math.sin(brick.angle||0);
        const dx=cx-brick.x-brick.w/2,dy=cy-brick.y-brick.h/2;
        const ax=Math.abs(dx*co+dy*si)-brick.w/2,ay=Math.abs(-dx*si+dy*co)-brick.h/2;
        assert.ok(Math.hypot(Math.max(0,ax),Math.max(0,ay))>=r,`${b.kind} exit overlaps brick`);
      }
    }
    if(index===12)assert.ok(b.bricks.some(x=>x.portalCargo&&x.spiral)&&b.bricks.some(x=>x.portalCargo&&x.gif>=0));
  }
});
