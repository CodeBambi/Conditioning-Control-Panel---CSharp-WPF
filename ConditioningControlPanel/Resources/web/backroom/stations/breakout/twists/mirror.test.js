import test from 'node:test';
import assert from 'node:assert/strict';
import TWIST, { twinCol, twinOf, mirrorState, ageBeams, makeBeam, TWIN_DELAY, BEAM_LIFE, SEAM_FLARE, MAX_BEAMS, COLS } from './mirror.js';
import RENDER, { under, over } from './mirror-render.js';
import CUES, { twinPan } from '../cues/twist-mirror.js';
import REACTIONS from '../reactions/twist-mirror.js';
import { DOORS, parseBoard } from '../doors.js';

/* ------------------------------------------------------------------ a board on a bench */

function brick(row, col, extra = {}) {
  return { x: 40 + col * 54, y: 26 + row * 33, w: 48, h: 27, alive: true, row, col, ...extra };
}

/** A tiny game, a tiny ctx: exactly the shape twists/CONTRACT.md section 2 froze, and the game's own break path. */
function bench(cells, seed = 7) {
  const g = { w: 900, h: 600, reduced: false, time: 0, bricks: cells.slice() };
  const grid = new Map();
  for (const br of cells) grid.set(br.row * COLS + br.col, br);
  const events = [], broken = [], timers = [];
  let clock = 0, s = seed >>> 0;
  const rng = () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; };
  const ctx = {
    emit: (name, data) => events.push({ name, data }),
    rng, w: g.w, h: g.h,
    at: (row, col) => grid.get(row * COLS + col) || null,
    // The real ctx.breakBrick: steel and gate cleared first, then the game's break, then the twist hears it.
    breakBrick(br, ball) {
      if (!br || !br.alive) return;
      br.steel = false; br.gate = null;
      br.alive = false; broken.push(br);
      TWIST.onBreak(g, br, ball || null, ctx);
    },
    schedule(seconds, fn) {
      const timer = { at: clock + Math.max(0, Number(seconds) || 0), fn, dead: false };
      timers.push(timer);
      return () => { timer.dead = true; };
    },
    startRelapse() {},
    powers: { drop() {}, reset() {} },
  };
  TWIST.build(g, ctx);
  /** The ball takes a brick: the game marks it dead and then calls onBreak. */
  const ballBreak = (br) => { if (!br || !br.alive) return; br.alive = false; broken.push(br); TWIST.onBreak(g, br, null, ctx); };
  /** One sim step: due timers in due order, then the twist's update. */
  const run = (dt) => {
    clock += dt;
    const due = timers.filter(t => !t.dead && t.at <= clock).sort((a, b) => a.at - b.at);
    for (const t of due) t.dead = true;
    for (const t of due) t.fn(g, ctx);
    TWIST.update(g, dt, ctx);
  };
  const pops = () => events.filter(e => e.name === 'mirrorPop');
  return { g, ctx, events, broken, timers, ballBreak, run, pops };
}

/* ------------------------------------------------------------------ the shape */

test('mirror: the module keeps the shape twists/CONTRACT.md froze', () => {
  assert.equal(TWIST.id, 'mirror');
  for (const hook of ['build', 'onHit', 'onBreak', 'update', 'onCatch', 'wallCleared'])
    if (TWIST[hook] !== undefined) assert.equal(typeof TWIST[hook], 'function', hook + ' must be a function or absent');
  for (const hook of ['brick', 'under', 'over'])
    if (RENDER[hook] !== undefined) assert.equal(typeof RENDER[hook], 'function', hook + ' must be a function or absent');
  assert.equal(typeof CUES.mirrorPop, 'function');
  assert.equal(typeof REACTIONS.mirrorPop, 'function');
});

/* ------------------------------------------------------------------ the reflection */

test('mirror: the twin column is 15 - col, the edges are a pair, and twinOf finds the cell', () => {
  assert.deepEqual([0, 15, 4, 11, 7, 8].map(c => twinCol(c)), [15, 0, 11, 4, 8, 7]);
  for (let c = 0; c < COLS; c++) assert.equal(twinCol(twinCol(c)), c, 'the reflection of a reflection is itself');
  assert.equal(twinCol('nope'), -1, 'a bad column never picks a real cell');
  const a = brick(1, 2), b = brick(1, 13), { ctx } = bench([a, b]);
  assert.equal(twinOf(a, ctx), b);
  assert.equal(twinOf(b, ctx), a);
  assert.equal(twinOf(brick(1, 5), ctx), null, 'an empty cell is no twin');
  assert.equal(twinOf(null, ctx), null);
  // An odd-width board would put a brick on the fold: it has no twin, and must not pair with itself.
  const mid = brick(0, 2), solo = bench([mid]);
  assert.equal(twinOf(mid, solo.ctx, 5), null, 'col 2 of 5 reflects onto itself');
});

/* ------------------------------------------------------------------ the hop */

test('mirror: a break takes its twin one beat later, and says so once', () => {
  const a = brick(1, 2), b = brick(1, 13);
  const t = bench([a, b]);
  t.ballBreak(a);
  assert.equal(t.pops().length, 1, 'one mirrorPop');
  assert.deepEqual(
    { x: t.pops()[0].data.x, tx: t.pops()[0].data.tx },
    { x: a.x + a.w / 2, tx: b.x + b.w / 2 },
    'the event carries both centres');
  assert.equal(b.alive, true, 'the twin is still there while the beam crosses');
  t.run(TWIN_DELAY / 2);
  assert.equal(b.alive, true, 'and still there halfway');
  t.run(TWIN_DELAY);
  assert.equal(b.alive, false, 'the twin goes when the beat lands');
  assert.deepEqual(t.broken, [a, b]);
});

test('mirror: the twin never bounces back - one hop, one pop, whatever the order', () => {
  const a = brick(1, 0), b = brick(1, 15);
  const t = bench([a, b]);
  t.ballBreak(a);
  t.run(TWIN_DELAY + 0.01);
  t.run(0.5);
  assert.equal(t.pops().length, 1, 'the twin popping is not a second mirrorPop');
  assert.equal(t.broken.length, 2, 'two bricks, not a loop');
  assert.equal(a.mirrorEcho, undefined);
  assert.equal(b.mirrorEcho, false, 'the echo flag is spent, not left set');
  assert.equal(b.mirrorPending, false);
});

test('mirror: a steel twin comes down, because the mirror is the key', () => {
  const plain = brick(2, 14), sealed = brick(2, 1, { steel: true });
  const t = bench([plain, sealed]);
  t.ballBreak(plain);
  t.run(TWIN_DELAY + 0.01);
  assert.equal(sealed.alive, false, 'steel breaks through the mirror');
  assert.equal(sealed.steel, false, 'ctx.breakBrick cleared the seal first');
});

test('mirror: a power-up twin is flagged as a prize on the event', () => {
  const t = bench([brick(2, 13), brick(2, 2, { powerup: 'multiball' })]);
  t.ballBreak(t.g.bricks[0]);
  assert.equal(t.pops()[0].data.prize, true);
  const dull = bench([brick(3, 13), brick(3, 2)]);
  dull.ballBreak(dull.g.bricks[0]);
  assert.equal(dull.pops()[0].data.prize, false);
});

test('mirror: a twin reached first, a missing twin and a dead twin are all quiet', () => {
  const a = brick(1, 3), b = brick(1, 12);
  const t = bench([a, b]);
  t.ballBreak(a);                       // b is pending
  t.ballBreak(b);                       // the player gets there first
  assert.equal(t.pops().length, 1, 'b has no live twin left, so it says nothing');
  t.run(TWIN_DELAY + 0.01);
  assert.equal(t.broken.length, 2, 'the stale timer found b already gone');
  assert.equal(b.mirrorPending, false, 'and let go of the booking');
  // A brick over an empty cell, and one over a cell already broken: neither books anything.
  const q = bench([brick(1, 4), brick(2, 4), brick(2, 11, { alive: false })]);
  q.ballBreak(q.g.bricks[0]); q.ballBreak(q.g.bricks[1]);
  assert.equal(q.pops().length, 0);
  assert.equal(q.timers.length, 0, 'nothing was booked');
});

test('mirror: two pairs pop in the order they were lit', () => {
  const a = brick(1, 1), aT = brick(1, 14), b = brick(3, 5), bT = brick(3, 10);
  const t = bench([a, aT, b, bT]);
  t.ballBreak(a);
  t.run(0.02);
  t.ballBreak(b);
  t.run(TWIN_DELAY + 0.02);
  assert.deepEqual(t.broken, [a, b, aT, bT], 'due order, on the sim clock');
});

/* ------------------------------------------------------------------ the beams */

test('mirror: beams age on the sim clock and let go at their life', () => {
  const a = brick(1, 2), b = brick(1, 13);
  const t = bench([a, b]);
  t.ballBreak(a);
  const st = mirrorState(t.g);
  assert.equal(st.beams.length, 1);
  assert.equal(st.seam, 1, 'a pop flares the fold');
  t.run(BEAM_LIFE / 2);
  assert.equal(st.beams.length, 1, 'still crossing');
  t.run(BEAM_LIFE);
  assert.equal(t.g.mirror.beams.length, 0, 'gone at its life');
});

test('mirror: the seam decays to zero and stops there, a bad dt is ignored, and beams have a ceiling', () => {
  const st = { beams: [], seam: 1, pops: 0, cols: COLS };
  ageBeams(st, SEAM_FLARE / 2);
  assert.ok(st.seam > 0.45 && st.seam < 0.55);
  for (const dt of [SEAM_FLARE * 4, -3, NaN]) { ageBeams(st, dt); assert.equal(st.seam, 0); }
  const cells = [];
  for (let r = 0; r < MAX_BEAMS + 3; r++) { cells.push(brick(r, 0)); cells.push(brick(r, 15)); }
  const t = bench(cells);
  for (let r = 0; r < MAX_BEAMS + 3; r++) t.ballBreak(t.g.bricks[r * 2]);
  assert.equal(mirrorState(t.g).beams.length, MAX_BEAMS);
});

test('mirror: a beam is the two centres, and its bow is seeded, not random', () => {
  const a = brick(1, 2), b = brick(1, 13), beam = makeBeam(a, b, true, 0.05);
  assert.deepEqual([beam.x1, beam.y2, beam.prize, beam.age], [a.x + a.w / 2, b.y + b.h / 2, true, 0]);
  const bows = (seed) => {
    const t = bench([brick(1, 2), brick(1, 13), brick(2, 2), brick(2, 13)], seed);
    t.ballBreak(t.g.bricks[0]); t.ballBreak(t.g.bricks[2]);
    return mirrorState(t.g).beams.map(x => x.bow);
  };
  assert.deepEqual(bows(11), bows(11), 'the same seed draws the same board twice');
  assert.notDeepEqual(bows(11), bows(12), 'and the rng is really being used');
});

/* ------------------------------------------------------------------ the drawing */

/** A canvas that only remembers what it was asked to do. */
function recorder() {
  const calls = [];
  const grad = { addColorStop() {} };
  const api = {
    calls, lineDashOffset: 0, lineWidth: 0, lineCap: '', fillStyle: '', strokeStyle: '',
    createLinearGradient() { calls.push('gradient'); return grad; },
    fillRect() { calls.push('fillRect'); }, beginPath() { calls.push('beginPath'); },
    moveTo() { calls.push('moveTo'); }, lineTo() { calls.push('lineTo'); },
    quadraticCurveTo() { calls.push('curve'); }, stroke() { calls.push('stroke'); },
    arc() { calls.push('arc'); }, fill() { calls.push('fill'); },
    setLineDash(d) { calls.push('dash:' + (d.length ? 'on' : 'off')); },
  };
  return api;
}

test('mirror render: the fold is drawn from the brick band, even with nothing on it', () => {
  const snap = { w: 900, h: 600, reduced: false, bricks: [brick(0, 0), brick(3, 15)], mirror: { beams: [], seam: 0, cols: COLS } };
  const r = recorder();
  under(r, snap, 1.25);
  assert.ok(r.calls.includes('fillRect'), 'the band');
  assert.ok(r.calls.includes('stroke'), 'the line');
  const bare = recorder();
  under(bare, { w: 900, h: 600, reduced: false, bricks: [], mirror: null }, 0);
  assert.ok(bare.calls.length > 0, 'an empty wall still shows the fold');
  const noState = recorder();
  assert.doesNotThrow(() => over(noState, { w: 900, h: 600, bricks: [], mirror: null }, 0));
  assert.equal(noState.calls.length, 0, 'no pair, nothing over the wall');
});

test('mirror render: reduced motion keeps the fold and the line, and drops every moving part', () => {
  const bricks = [brick(0, 0), brick(3, 15)];
  const mirror = { beams: [{ x1: 10, y1: 10, x2: 200, y2: 10, age: 0.02, life: BEAM_LIFE, prize: false, bow: 0.09 }], seam: 1, cols: COLS };
  const moving = recorder();
  under(moving, { w: 900, h: 600, reduced: false, bricks, mirror }, 2);
  over(moving, { w: 900, h: 600, reduced: false, bricks, mirror }, 2);
  assert.ok(moving.lineDashOffset !== 0, 'the dashes crawl');
  assert.ok(moving.calls.includes('arc'), 'the spark runs the beam');

  const still = recorder();
  under(still, { w: 900, h: 600, reduced: true, bricks, mirror }, 2);
  assert.equal(still.lineDashOffset, 0, 'no crawl');
  assert.equal(still.calls.filter(c => c === 'gradient').length, 1, 'the band only: no travelling glints');
  const stillOver = recorder();
  over(stillOver, { w: 900, h: 600, reduced: true, bricks, mirror }, 2);
  assert.ok(stillOver.calls.includes('curve'), 'the pair is still drawn');
  assert.ok(!stillOver.calls.includes('arc'), 'but nothing moves along it');

  const spent = recorder();
  over(spent, { w: 900, h: 600, reduced: false, bricks: [], mirror: { beams: [{ x1: 0, y1: 0, x2: 9, y2: 0, age: BEAM_LIFE, life: BEAM_LIFE, bow: 0 }], seam: 0, cols: COLS } }, 1);
  assert.equal(spent.calls.length, 0, 'a spent beam draws nothing');
});

/* ------------------------------------------------------------------ the sound and the sparks */

test('mirror cue: the note is the brick note an octave up, panned to the twin, and grey says nothing', () => {
  const played = [];
  const synth = {
    tone: (hz, dur, level, o) => ({ hz, dur, level, ...o }),
    SEMI: n => Math.pow(2, n / 12), ROOT_HZ: 523.25,
    hitSemis: combo => (combo || 0) % 5, hitCutoff: () => 4000, saturation: 0.5,
    quantise: lead => 10 + (lead || 0), bus: { sfx: 'sfx' },
    play: (notes, at, bus) => played.push({ notes, at, bus }),
  };
  const d = { x: 100, y: 50, tx: 800, ty: 50, xN: 100 / 900, prize: false, combo: 3, state: 'colour' };
  assert.equal(CUES.mirrorPop(synth, d), true);
  assert.equal(played.length, 1);
  const base = synth.ROOT_HZ * synth.SEMI(synth.hitSemis(3));
  assert.ok(Math.abs(played[0].notes[0].hz - base * 2) < 1e-6, 'an octave up');
  assert.ok(Math.abs(played[0].notes[0].pan - 800 / 900) < 1e-9, 'panned to the twin');
  assert.ok(played[0].at >= 10 + TWIN_DELAY - 1e-9, 'asked for a sixteenth that lands with the twin');
  assert.equal(CUES.mirrorPop(synth, { ...d, state: 'grey' }), false);
  assert.equal(played.length, 1, 'grey is payload-free');
  // And the pan falls back to the other side when the numbers are not there.
  assert.ok(Math.abs(twinPan({ xN: 0.2 }) - 0.8) < 1e-9);
  assert.equal(twinPan({}), 0.5);
  assert.equal(twinPan({ x: 0, tx: 5, xN: 0 }), 1, 'a zero centre falls back and stays in range');
});

test('mirror reaction: two rings always, sparks only in colour with motion on', () => {
  const mk = (colour, reduced, rung = true) => {
    const stamps = [], sprays = [], flashes = [];
    const fx = {
      colour, reduced, stamps, rungs: () => rung, colours: { WHITE: [255, 255, 255] },
      P: { spray: (...a) => sprays.push(a) }, flash: v => flashes.push(v),
    };
    REACTIONS.mirrorPop(fx, { x: 10, y: 20, tx: 200, ty: 20, prize: true });
    return { stamps, sprays, flashes };
  };
  const full = mk(true, false);
  assert.equal(full.stamps.length, 2, 'here and there');
  assert.equal(full.sprays.length, 2);
  assert.equal(full.flashes.length, 1, 'a prize is worth a blink');
  const still = mk(true, true);
  assert.equal(still.stamps.length, 2, 'reduced motion keeps the pairing');
  assert.equal(still.sprays.length, 0);
  assert.equal(still.flashes.length, 0);
  assert.equal(mk(false, false).stamps.length, 0, 'grey gets nothing');
  assert.equal(mk(true, false, false).sprays.length, 0, 'a low particle rung skips the sparks');
});

/* ------------------------------------------------------------------ the authored board */

test('mirror: the real board hides each prize behind the other figure', () => {
  const door = DOORS.find(d => d.id === 'wardrobe');
  const board = door.boards.find(b => b.id === 'ward_mirror2');
  assert.equal(board.twist, 'mirror');
  const cells = new Map(parseBoard(board.rows).map(c => [c.row * COLS + c.col, c]));
  const ch = (r, c) => { const cell = cells.get(r * COLS + c); return cell ? cell.ch : '.'; };
  for (const [r, c] of [[2, 2], [5, 11]]) {
    const cell = cells.get(r * COLS + c);
    assert.ok(cell && cell.spec.powerup, 'a prize sits at ' + r + ',' + c);
    assert.equal(ch(r, twinCol(c)), '1', 'and its twin is an ordinary brick on the other figure');
    // Its own side is sealed: the ball cannot dig in, only the reflection opens it.
    assert.equal(ch(r, c - 1), 'X');
    assert.equal(ch(r, c + 1), 'X');
  }
  assert.equal(board.rows.length, 8);
  for (const row of board.rows) assert.equal(row.length, COLS);
});
