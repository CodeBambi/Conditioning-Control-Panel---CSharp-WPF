import { test } from 'node:test';
import assert from 'node:assert/strict';
import { settleCells, recoilCells, leverRebound, latchTravel } from '../juice.js';
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
