import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import { createPresentationPacer } from '../presentation-pacer.js';

for (const refresh of [60, 120]) {
  for (const [device, fps] of [[{ userAgent: 'Android' }, 30], [{ hardwareConcurrency: 8 }, 60]]) {
    test(`${fps} presentation frames on a ${refresh} Hz display`, () => {
      const pacer = createPresentationPacer(device);
      const painted = [];
      for (let i = 0; i < refresh * 10; i++) {
        const now = i * 1000 / refresh;
        if (pacer.due(now)) painted.push(now);
      }
      assert.equal(painted.length, fps * 10);
      for (let i = 1; i < painted.length; i++) assert.ok(painted[i] - painted[i - 1] >= 1000 / fps - .001);
    });
  }
}
test('low memory and low core desktop devices share the room mobile budget', () => {
  assert.equal(createPresentationPacer({ deviceMemory: 4 }).fps, 30);
  assert.equal(createPresentationPacer({ hardwareConcurrency: 4 }).fps, 30);
  assert.equal(createPresentationPacer({ userAgent: 'Macintosh', maxTouchPoints: 5 }).fps, 30);
});
test('stall skips missed paints and resume paints immediately', () => {
  const pacer = createPresentationPacer({ userAgent: 'Android' });
  assert.equal(pacer.due(100), true);
  assert.equal(pacer.due(110), false);
  assert.equal(pacer.due(4100), true);
  assert.equal(pacer.due(4101), false);
  assert.equal(pacer.due(4102), false);
  pacer.reset();
  assert.equal(pacer.due(4103), true);
  assert.equal(pacer.due(4110), false);
});
test('actual station frame drains gameplay queues on skipped presentation frames', () => {
  const source = readFileSync(new URL('../station.js', import.meta.url), 'utf8');
  const frame = source.slice(source.indexOf('  function frame() {'), source.indexOf('  function renderStakes()'));
  const applied = [], painted = [], synced = [];
  let now = 0;
  const context = vm.createContext({
    raf: 0, alive: true, suspended: false, performance: { now: () => now },
    dress: () => ({ still: false }), lastStill: false, kit: {}, deck: null,
    queue: [{ at: 8 }, { at: 16 }, { at: 23 }], apply: (step, time) => applied.push([step.at, time]),
    moments: { breathing: () => false }, screenUntil: 0, TIMING: {},
    presentation: createPresentationPacer({ userAgent: 'Android' }),
    table: { draw: value => painted.push(value.now) }, t: () => '', decide: false,
    phase: 'decision', seating: false, sync: time => synced.push(time), requestAnimationFrame: () => 1,
  });
  vm.runInContext(frame, context);
  for (now of [0, 8.4, 16.7, 25]) vm.runInContext('frame()', context);
  assert.deepEqual(applied, [[8, 8.4], [16, 16.7], [23, 25]]);
  assert.deepEqual(painted, [0]);
  assert.deepEqual(synced, [0]);
});
