import assert from 'node:assert/strict';
import { CAPTURES, createCaptureDeck, capturePose, CAPTURE_SECONDS } from '../board/captures.js';

for (const random of [() => 0, () => .999999, Math.random]) {
  const pick = createCaptureDeck(random);
  for (const type of Object.keys(CAPTURES)) {
    let previous;
    for (let bag = 0; bag < 20; bag++) {
      const ids = new Set();
      for (let i = 0; i < 3; i++) {
        const style = pick(type);
        assert.notEqual(style.id, previous, 'no consecutive repeats, including bag boundaries');
        previous = style.id; ids.add(style.id);
      }
      assert.equal(ids.size, 3, 'every bag contains every variation');
    }
  }
}
for (const styles of Object.values(CAPTURES)) for (const style of styles) {
  for (const reduced of [false, true]) {
    for (let t = 0; t < 1.1; t += .01) {
      const pose = capturePose(style, t, reduced);
      assert.ok(Object.values(pose).every(v => typeof v === 'boolean' || Number.isFinite(v)));
      assert.ok(pose.opacity >= 0 && pose.opacity <= 1);
      assert.ok(Math.abs(pose.forward) < .7 && Math.abs(pose.sideways) < .4, 'tail stays close to capture');
      if (reduced) { assert.equal(pose.spin, 0); assert.equal(pose.lift, 0); }
    }
    assert.ok(capturePose(style, CAPTURE_SECONDS + .001, reduced).done, 'exit before the parade deadline');
  }
}
console.log('18 capture variants: coverage, no repeats, bounded trajectories, finite poses and reduced motion passed');
