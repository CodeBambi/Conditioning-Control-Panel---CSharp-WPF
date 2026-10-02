/* ============================================================================
 * smoke/think-smoke.mjs - the think ramp, driven through attachRamp in node.
 *
 *   node ConditioningControlPanel/Resources/web/piecebypiece/smoke/think-smoke.mjs
 *
 * ramp-smoke pins the numbers; this pins the wiring the owner asked for on
 * 2026-10-01: the screen climbs while YOU sit on your move, never on the
 * opponent's, and a loss surges and then goes quiet. And 2026-10-02: no floor,
 * a slower climb, and THE BREATH: a move takes everything to zero and keeps it
 * there until your own turn card (or a few seconds of your turn with no card). No DOM: the layers degrade to no-ops, and time
 * and frames are driven by hand.
 * ==========================================================================*/

import { createBus } from '../game/events.js';

let clock = 1000;
const frames = [];
globalThis.performance = { now: () => clock };
globalThis.requestAnimationFrame = (fn) => { frames.push(fn); return frames.length; };
globalThis.cancelAnimationFrame = () => {};

// the referee the ramp asks whose move it is, the menu over it, and the director
const game = { side: 'w', over: false, turn() { return this.side; }, isOver() { return this.over; } };
let paused = false;
let doorUp = true;
let held = false;
globalThis.window = { PBP: { game, isPaused: () => paused, door: { isUp: () => doorUp }, board: { director: { holding: () => held } } } };

// a stage that keeps its classes, so the snap's fast fades can be seen
const classes = new Set();
const stage = { style: {}, classList: {
  add: (...c) => c.forEach((x) => classes.add(x)),
  remove: (...c) => c.forEach((x) => classes.delete(x)),
  contains: (c) => classes.has(c),
  toggle(c, on) { const v = on === undefined ? !classes.has(c) : !!on; if (v) classes.add(c); else classes.delete(c); return v; },
} };

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
const ramp = attachRamp({ bus, seed: 'think-smoke', stage });
const dbg = () => ramp.debug();

/* ---- the menu: the referee idles at the start with white to move ------------ */
doorUp = false;   // even a page with no menu at all: nothing counts until a game is dealt
wait(3000);
check('no game dealt, nothing counts', dbg().think.ms === 0, String(dbg().think.ms));
doorUp = true;
wait(21000);
check('the menu never counts as thinking', dbg().think.ms === 0 && !dbg().think.counting, JSON.stringify(dbg().think));
check('so the menu stays calm', dbg().meter < 0.05, String(dbg().meter));

/* ---- solo, playing white -------------------------------------------------- */
doorUp = false;
bus.emit('local', { sides: ['w'] });
bus.emit('turn', { side: 'w', ply: 0, clocks: { w: 300000, b: 300000 }, total: 300000 });
check('the ramp runs in Distraction', dbg().enabled);

wait(10000);
const tenSeconds = dbg();
check('your own move counts as thinking', tenSeconds.think.counting);
check('10 s in, the screen is lifted a little', tenSeconds.meter > 0.08, String(tenSeconds.meter));
check('but the climb is slow now (owner: 20 s was too fast)', tenSeconds.meter < 0.3, String(tenSeconds.meter));

paused = true;
const pausedAt = dbg().think.ms;
wait(5000);
check('the pause card never counts', near(dbg().think.ms, pausedAt), `${dbg().think.ms} vs ${pausedAt}`);
paused = false;

held = true;
const heldAt = dbg().think.ms;
wait(4000);
check('a capture replay holding the screen never counts', near(dbg().think.ms, heldAt), `${dbg().think.ms} vs ${heldAt}`);
held = false;

doorUp = true;   // a card over a live board (the menu, the shelf): nothing counts behind it
const behindAt = dbg().think.ms;
wait(3000);
check('a menu over the board never counts', near(dbg().think.ms, behindAt), `${dbg().think.ms} vs ${behindAt}`);
doorUp = false;

wait(31000);
check('40 s of your own thinking is the top of the Normal ramp', near(dbg().meter, 1), String(dbg().meter));
const flashesBefore = dbg().layers.counts.flash || 0;
check('a long think pops pictures', flashesBefore > 0, String(flashesBefore));

// the move lands: white moved, black is on the move
game.side = 'b';
bus.emit('turn', { side: 'b', ply: 1, clocks: { w: 280000, b: 300000 }, total: 300000 });
const floor = dbg();
check('the move takes the screen all the way down: no floor', floor.meter === 0, String(floor.meter));
check('and starts the breath', floor.resting && floor.think.ms === 0);
check('the snap arms the fast fades', classes.has('is-snap'));
await new Promise((r) => setTimeout(r, RAMP_TUNING.think.snapMs + 100));
check('and lets them go again', !classes.has('is-snap'));

wait(15000);
check('the opponent\'s move never counts', dbg().think.ms === 0 && !dbg().think.counting);
check('so the screen stays clear while they think', dbg().meter === 0, String(dbg().meter));
check('and no picture pops for us during the breath', (dbg().layers.counts.flash || 0) === flashesBefore && (dbg().layers.counts.gifRain || 0) >= 0);

game.side = 'w';
bus.emit('turn', { side: 'w', ply: 2, clocks: { w: 280000, b: 285000 }, total: 300000 });
wait(1000);
check('our turn, but the card is not up yet: still breathing', dbg().resting && dbg().think.ms === 0, JSON.stringify(dbg().think));
bus.emit('turn-card', { side: 'b', style: 'slam', short: false, mine: false });
check('their turn card never ends our breath', dbg().resting);
bus.emit('turn-card', { side: 'w', style: 'slam', short: false, mine: true });
check('our turn card ends the breath', !dbg().resting);
wait(3000);
check('and the climb starts again from zero', dbg().think.ms > 2000 && dbg().think.ms < 3500, String(dbg().think.ms));

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

/* ---- back at the menu after a game: the referee is reset, nobody deals -------- */
doorUp = true;
game.over = false; game.side = 'w';
bus.emit('newgame', { ply: 0 });
wait(21000);
check('back at the menu, nothing climbs', dbg().think.ms === 0 && dbg().meter < 0.05, JSON.stringify(dbg().think));
doorUp = false;

/* ---- online: a resync is not a move, and the server's seat wins ---------------- */
bus.emit('local', { sides: ['w'], mode: 'online' });
bus.emit('turn', { side: 'w', ply: 0, clocks: { w: 300000, b: 300000 }, total: 300000 });
wait(30000);
const beforeResync = dbg().think.ms;
bus.emit('turn', { side: 'w', ply: 0, clocks: { w: 285000, b: 300000 }, total: 300000 });   // a reconnect
check('an online resync is not a move: the think goes on', near(dbg().think.ms, beforeResync) && dbg().meter > 0.5, String(dbg().meter));
bus.emit('seat', { color: 'b' });   // the lobby guessed white; the server says black
check('a corrected seat moves the climb to the real side', dbg().side === 'b' && dbg().think.ms === 0 && !dbg().think.counting);

/* ---- hotseat: every hand-over snaps ---------------------------------------------- */
bus.emit('local', { sides: ['w', 'b'] });
game.side = 'w';
bus.emit('turn', { side: 'w', ply: 0, clocks: { w: 300000, b: 300000 }, total: 300000 });
wait(40500);
check('hotseat: the side on the move climbs', near(dbg().meter, 1), String(dbg().meter));
game.side = 'b';
bus.emit('turn', { side: 'b', ply: 1, clocks: { w: 280000, b: 300000 }, total: 300000 });
check('hotseat: the hand-over is a breath too', dbg().side === 'b' && dbg().meter === 0 && dbg().resting, String(dbg().meter));
wait(RAMP_TUNING.think.wakeAfterMs + 300);
check('no card comes (a short clock): the breath ends on its own', !dbg().resting && dbg().think.ms > 0, JSON.stringify(dbg().think));

ramp.dispose();
console.warn = warn;

const total = passed + failures.length;
if (failures.length) {
  console.error(`FAIL  ${failures.length}/${total} think checks failed:`);
  for (const f of failures) console.error('  - ' + f);
  process.exit(1);
}
console.log(`ok  ${passed}/${total} think checks passed`);
