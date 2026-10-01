/* ============================================================================
 * smoke/think-smoke.mjs - the think ramp, driven through attachRamp in node.
 *
 *   node ConditioningControlPanel/Resources/web/piecebypiece/smoke/think-smoke.mjs
 *
 * ramp-smoke pins the numbers; this pins the wiring the owner asked for on
 * 2026-10-01: the screen climbs while YOU sit on your move, never on the
 * opponent's, drops straight back to the floor when the move lands, and a loss
 * surges and then goes quiet. No DOM: the layers degrade to no-ops, and time
 * and frames are driven by hand.
 * ==========================================================================*/

import { createBus } from '../game/events.js';

let clock = 1000;
const frames = [];
globalThis.performance = { now: () => clock };
globalThis.requestAnimationFrame = (fn) => { frames.push(fn); return frames.length; };
globalThis.cancelAnimationFrame = () => {};

// the referee the ramp asks whose move it is
const game = { side: 'w', over: false, turn() { return this.side; }, isOver() { return this.over; } };
let paused = false;
globalThis.window = { PBP: { game, isPaused: () => paused } };

const { attachRamp } = await import('../ramp/index.js');
const { RAMP_TUNING } = await import('../ramp/meter.js');

let passed = 0;
const failures = [];
const check = (name, cond, detail) => { if (cond) passed += 1; else failures.push(name + (detail ? ' -> ' + detail : '')); };
const near = (a, b, eps = 1e-6) => Math.abs(a - b) <= eps;

/** Let `ms` pass in 90 ms beats, running whatever frame the ramp asked for. */
function wait(ms) {
  const end = clock + ms;
  while (clock < end) {
    clock = Math.min(end, clock + 90);
    const due = frames.splice(0);
    for (const fn of due) fn();
  }
}

const warn = console.warn;
console.warn = () => {};   // the ramp says it has no #fx, no #stage and no pictures: all expected here
const bus = createBus();
const ramp = attachRamp({ bus, seed: 'think-smoke' });
const dbg = () => ramp.debug();

/* ---- solo, playing white -------------------------------------------------- */
bus.emit('local', { sides: ['w'] });
bus.emit('turn', { side: 'w', ply: 0, clocks: { w: 300000, b: 300000 }, total: 300000 });
check('the ramp runs in Distraction', dbg().enabled);

wait(10000);
const tenSeconds = dbg();
check('your own move counts as thinking', tenSeconds.think.counting);
check('10 s in, the screen is lifted', tenSeconds.meter > 0.3, String(tenSeconds.meter));
check('but not to the top', tenSeconds.meter < 0.6, String(tenSeconds.meter));

paused = true;
const pausedAt = dbg().think.ms;
wait(5000);
check('the pause card never counts', near(dbg().think.ms, pausedAt), `${dbg().think.ms} vs ${pausedAt}`);
paused = false;

wait(11000);
check('20 s of your own thinking is the top of the ramp', near(dbg().meter, 1), String(dbg().meter));

// the move lands: white moved, black is on the move
game.side = 'b';
bus.emit('turn', { side: 'b', ply: 1, clocks: { w: 280000, b: 300000 }, total: 300000 });
const floor = dbg();
check('the move snaps the screen straight back to the floor', floor.meter < 0.15, String(floor.meter));
check('and the think clock is back at zero', floor.think.ms === 0);

wait(15000);
check('the opponent\'s move never counts', dbg().think.ms === 0 && !dbg().think.counting);
check('so the screen stays on the floor while they think', dbg().meter < 0.15, String(dbg().meter));

game.side = 'w';
bus.emit('turn', { side: 'w', ply: 2, clocks: { w: 280000, b: 285000 }, total: 300000 });
wait(3000);
check('your next move starts the climb again from zero', dbg().think.ms > 2000 && dbg().think.ms < 3500, String(dbg().think.ms));

/* ---- the fall --------------------------------------------------------------- */
bus.emit('gameover', { result: '0-1', winner: 'b' });
check('a loss surges to full', dbg().surge && near(dbg().meter, 1), JSON.stringify(dbg().surge));
wait(RAMP_TUNING.surge.holdMs + RAMP_TUNING.surge.drainMs / 2);
check('then drains', dbg().meter > 0.2 && dbg().meter < 0.8, String(dbg().meter));
wait(RAMP_TUNING.surge.drainMs);
check('and goes quiet once it has drained', !dbg().enabled && !dbg().surge);

/* ---- a win just goes quiet ------------------------------------------------------ */
game.over = false; game.side = 'w';
bus.emit('newgame', { ply: 0 });
bus.emit('turn', { side: 'w', ply: 0, clocks: { w: 300000, b: 300000 }, total: 300000 });
wait(2000);
bus.emit('gameover', { result: '1-0', winner: 'w' });
check('a win does not surge', !dbg().surge && !dbg().enabled);

/* ---- hotseat: every hand-over snaps ---------------------------------------------- */
bus.emit('local', { sides: ['w', 'b'] });
game.side = 'w';
bus.emit('turn', { side: 'w', ply: 0, clocks: { w: 300000, b: 300000 }, total: 300000 });
wait(20500);
check('hotseat: the side on the move climbs', near(dbg().meter, 1), String(dbg().meter));
game.side = 'b';
bus.emit('turn', { side: 'b', ply: 1, clocks: { w: 280000, b: 300000 }, total: 300000 });
check('hotseat: the next player starts on their own floor', dbg().side === 'b' && dbg().meter < 0.15, String(dbg().meter));

ramp.dispose();
console.warn = warn;

const total = passed + failures.length;
if (failures.length) {
  console.error(`FAIL  ${failures.length}/${total} think checks failed:`);
  for (const f of failures) console.error('  - ' + f);
  process.exit(1);
}
console.log(`ok  ${passed}/${total} think checks passed`);
