// node smoke/clock-pause-smoke.mjs - the Esc pause holds a local clock and its turn age, and gives both back on resume.
import assert from 'node:assert/strict';
import { createClock } from '../game/clock.js';

let t = 0;
const clock = createClock({ perSideMs: 60000, now: () => t });
clock.start('w');
t = 5000;
assert.equal(clock.remaining('w'), 55000);
assert.equal(clock.pause(), true);
assert.equal(clock.isPaused(), true);
t = 65000;                                        // a minute on the pause card
assert.equal(clock.remaining('w'), 55000, 'paused time is never charged');
assert.equal(clock.turnElapsedMs(), 5000, 'the turn spiral does not grow while paused');
assert.equal(clock.flagged(), null);
assert.equal(clock.resume(), true);
t = 67000;
assert.equal(clock.remaining('w'), 53000);
assert.equal(clock.turnElapsedMs(), 7000);
assert.equal(clock.pause(), true);
clock.stop();
assert.equal(clock.isPaused(), false, 'stop clears a pause');
assert.equal(clock.resume(), false);
clock.stop();

const idle = createClock({ now: () => t });
assert.equal(idle.pause(), false, 'nothing to pause before a game starts');
console.log('clock pause smoke: ok');
