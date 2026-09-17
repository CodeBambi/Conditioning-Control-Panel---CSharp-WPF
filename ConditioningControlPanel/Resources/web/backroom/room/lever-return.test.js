import test from 'node:test';
import assert from 'node:assert/strict';
import { siliconeRebound, SILICONE_SETTLE_MS, customLeverReturn, releasedLeverAngle, customSpinLeverAngle, customSpinReturnMs } from './lever-return.js';
test('custom lever makes one small overshoot and a faster tiny correction', () => {
  for (const age of [-420, -1, 0, SILICONE_SETTLE_MS, 500]) assert.equal(siliconeRebound(age), 0);
  assert.equal(siliconeRebound(50), -0.10);
  assert.equal(siliconeRebound(140), 0.05);
  assert.ok(Math.abs(siliconeRebound(100)) < 1e-12);
  for(let ms=1;ms<240;ms++) assert.ok(Math.abs(siliconeRebound(ms))<=0.10);
});

test('whole custom lever overshoots backward only and rests after 90 ms', () => {
  assert.equal(customLeverReturn(45), -0.09);
  for (let ms=0; ms<=90; ms++) assert.ok(customLeverReturn(ms)<=0);
  for (const ms of [-1,0,90,200]) assert.equal(customLeverReturn(ms),0);
});

test('release never reverses before passing behind rest, then settles before tip flex', () => {
  for (const from of [0.1, 0.275, 0.5]) {
    let previous = from;
    for (let ms=0; ms<=220; ms++) {
      const angle=customSpinLeverAngle(ms,from);
      assert.ok(angle<=previous+1e-12); previous=angle;
    }
    assert.ok(Math.abs(previous+0.14)<1e-12);
    for(let ms=221;ms<=290;ms++) {
      const angle=releasedLeverAngle(ms,from);
      assert.ok(angle>=previous && angle<=0); previous=angle;
    }
    assert.equal(customSpinReturnMs(from),290);
    assert.equal(previous,0);
  }
  assert.equal(customSpinLeverAngle(120,0),0.5);
  assert.equal(customSpinLeverAngle(410,0),0);
});
