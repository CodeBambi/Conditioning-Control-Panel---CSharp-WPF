import test from 'node:test';
import assert from 'node:assert/strict';
import { beatAt, createClock } from './timeline.mjs';
test('beat boundaries, invalid time and final beat', () => {
 assert.equal(beatAt(-1), -1); assert.equal(beatAt(NaN), -1); assert.equal(beatAt(1.99), 0);
 assert.equal(beatAt(2), 1); assert.equal(beatAt(28), 7); assert.equal(beatAt(35), 8);
});
test('stop cancels completion and replay resets all beats', () => {
 const seen = []; let finishes = 0; const c = createClock(id => seen.push(id), () => finishes++);
 c.start(1000); c.tick(1000); c.tick(3000); c.stop(); assert.equal(c.tick(70000), null); assert.equal(finishes, 0);
 c.start(90000); c.tick(90000); c.tick(125000); c.tick(160000);
 assert.deepEqual(seen,[0,1,0,8]); assert.equal(finishes,1); assert.equal(c.running,false);
});
