import { test } from 'node:test';
import assert from 'node:assert/strict';
import { settleCells, recoilCells, leverRebound, latchTravel, latchPose, LATCH_DEPTH } from '../juice.js';
import { createSound } from '../sound.js';

test('reel impact is continuous, crosses home, and settles inside the existing thud', () => {
  assert.equal(settleCells(0), 0);
  assert.ok(settleCells(50) > 0);
  assert.ok(settleCells(160) < 0);
  assert.equal(settleCells(340), 0);
  assert.equal(settleCells(1000), 0);
  for (let ms = 0; ms <= 340; ms++) assert.ok(Math.abs(settleCells(ms)) <= 0.14);
});
test('startup recoil and lever rebound return exactly to rest on their budgets', () => {
  assert.equal(recoilCells(0), 0);
  assert.equal(recoilCells(50), -0.12);
  assert.equal(recoilCells(100), 0);
  assert.ok(leverRebound(50) < 0);
  assert.ok(leverRebound(150) > 0);
  assert.equal(leverRebound(200), 0);
  assert.equal(leverRebound(-1), 0);
});
test('freeze switch catches before springing back to its unchanged resting position', () => {
  assert.equal(latchTravel(0), 0);
  assert.equal(latchTravel(100), 0.012);
  assert.ok(latchTravel(200) < 0);
  assert.equal(latchTravel(300), 0);
  assert.equal(latchTravel(Infinity), 0);
});
test('freeze latch is a toggle: it stays down while held, and only springs back once the hold leaves it', () => {
  const held = { held: true, downAt: 1000 };
  assert.equal(latchPose(held, 1000), 0, 'the drop starts from rest');
  assert.ok(latchPose(held, 1050) > 0 && latchPose(held, 1050) < LATCH_DEPTH, 'mid-drop');
  assert.equal(latchPose(held, 1100), LATCH_DEPTH, 'fully down at 100 ms');
  assert.equal(latchPose(held, 60000), LATCH_DEPTH, 'and parked there a minute later, no spring-back');
  const released = { held: false, downAt: 1000, upAt: 5000 };
  assert.equal(latchPose(released, 5000), LATCH_DEPTH, 'the up starts from the parked depth');
  assert.ok(latchPose(released, 5100) < 0, 'overshoots past rest on the way up');
  assert.equal(latchPose(released, 5200), 0, 'and settles at rest');
  assert.equal(latchPose({ held: false }, 9000), 0, 'never held, never moved');
});
test('mechanical cues stop responding while suspended or disposed', () => {
  const played=[], stopped=[];
  const sound=createSound({play:(...args)=>played.push(args),stop:(name)=>stopped.push(name)});
  sound.freeze(); sound.freeze(true); sound.malus();
  assert.deepEqual(played, [['freeze-latch',{released:false}],['freeze-latch',{released:true}],['cabinet-knock']]);
  sound.suspend(true); sound.freeze(); sound.malus();
  assert.ok(stopped.includes('freeze-latch'));
  assert.ok(stopped.includes('cabinet-knock'));
  assert.equal(played.length,3);
  sound.suspend(false); sound.malus();
  assert.equal(played.length,4);
  sound.dispose(); sound.freeze(); sound.malus();
  assert.equal(played.length,4);
});
