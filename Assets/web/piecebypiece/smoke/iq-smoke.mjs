/* ============================================================================
 * smoke/iq-smoke.mjs - node checks for the IQ score: the loss curve, one
 * side's tracker, the grader on known positions, and the live grader driven
 * off the bus with a stand-in worker. No browser:
 *
 *   node smoke/iq-smoke.mjs
 *
 * Exits non-zero when a check fails, listing every one that did.
 * ==========================================================================*/

import { IQ, iqLoss, createIqTracker, createIqLive } from '../game/iq.js';
import { gradeMove } from '../game/search.js';
import { createRules } from '../game/rules.js';
import { createBus } from '../game/events.js';

let passed = 0;
const failures = [];
function ok(what, cond) { if (cond) { passed++; return; } failures.push(what); }
function eq(what, got, want) {
  ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));
}

// The curve: free slips, a hung piece hurts, the cap holds.
{
  eq('a game starts at 140', IQ.start, 140);
  eq('a best move is free', iqLoss(0), 0);
  eq('a small slip is free', iqLoss(45), 0);
  const knight = iqLoss(358), queen = iqLoss(924);
  ok(`a hung knight costs 15-20 (got ${knight})`, knight >= 15 && knight <= 20);
  ok(`a hung queen costs more than a knight (got ${queen})`, queen > knight);
  eq('a missed mate costs the cap', iqLoss(2180), IQ.cap);
  eq('nothing costs more than the cap', iqLoss(1e9), IQ.cap);
  let mono = true;
  for (let cp = 0, last = 0; cp < 3000; cp += 25) { const v = iqLoss(cp); if (v < last) mono = false; last = v; }
  ok('a bigger blunder never costs less', mono);
  eq('nonsense in costs nothing', iqLoss(NaN), 0);
}

// One side's tracker.
{
  const t = createIqTracker();
  eq('a fresh tracker sits at the start', t.value, 140);
  eq('no worst move before a loss', t.worst(), null);
  t.note({ ply: 1, side: 'w', san: 'e4', best: 'e4', cp: 0 });
  eq('a best move keeps the score', t.value, 140);
  const g = t.note({ ply: 3, side: 'w', san: 'Ng5', best: 'Nc3', cp: 358 });
  eq('a grade says what it cost', [g.loss, g.value], [iqLoss(358), 140 - iqLoss(358)]);
  t.note({ ply: 5, side: 'w', san: 'Qg4', best: 'Nc3', cp: 924 });
  eq('the worst move is the costliest', t.worst().san, 'Qg4');
  t.note({ ply: 7, side: 'w', san: 'Qh3', best: 'Qxf7#', cp: 924 });
  eq('a tie keeps the earlier move', t.worst().san, 'Qg4');
  const rec = t.toRecord();
  eq('the record has the shelf\'s shape: start, end, low', [rec.start, rec.end, rec.low], [140, t.value, t.value]);
  eq('the record keeps every move', rec.moves.map(m => m.ply), [1, 3, 5, 7]);
  t.note({ ply: 5, side: 'w', san: 'Qg4', best: 'Nc3', cp: 0 });
  eq('a regrade replaces the old grade', t.toRecord().moves.length, 4);
  const before = t.value;
  t.dropAfter(3);
  eq('a take-back drops the later grades', t.toRecord().moves.map(m => m.ply), [1, 3]);
  eq('but keeps what they cost', t.value, before);
  eq('and the record says what was spent', t.toRecord().spent, [{ ply: 7, loss: iqLoss(924) }]);
  t.spend({ ply: 5, cp: 924 });
  eq('a grade that lands after its take-back still costs', t.value, before - iqLoss(924));
  for (let i = 0; i < 20; i++) t.note({ ply: 9 + 2 * i, side: 'w', san: 'x', best: 'y', cp: 2000 });
  eq('the score never goes under the floor', t.value, IQ.floor);
  t.reset();
  eq('a reset is a fresh game', [t.value, t.toRecord().spent], [140, []]);
  t.restore([{ ply: 1, side: 'w', san: 'e4', best: 'e4', cp: 0 }], [{ ply: 3, loss: 16 }]);
  eq('a restore brings back what was spent', t.value, 124);
}

// The grader on known positions.
{
  const queen = gradeMove({ fen: 'rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2', move: { from: 'd1', to: 'g4' } });
  ok(`a hung queen gives a lot away (got ${queen && queen.cp})`, queen && queen.cp > 800);
  const best = gradeMove({ move: { from: 'e2', to: 'e4' } });
  ok(`a good opening move is nearly free (got ${best && best.cp})`, best && best.cp < IQ.free);
  const mate = 'r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4';
  const missed = gradeMove({ fen: mate, move: { from: 'h5', to: 'h3' } });
  eq('a missed mate in one names the mate', missed && missed.best, 'Qxf7#');
  eq('a missed mate in one costs the cap', missed && iqLoss(missed.cp), IQ.cap);
  eq('the mate itself is free', gradeMove({ fen: mate, move: { from: 'h5', to: 'f7' } })?.cp, 0);
  eq('a slower forced mate is free too', gradeMove({ fen: '7k/8/6K1/8/8/8/8/R7 w - - 0 1', move: { from: 'a1', to: 'b1' } })?.cp, 0);
  eq('an only move is free', gradeMove({ fen: '7k/8/8/8/8/8/6q1/7K w - - 0 1', move: { from: 'h1', to: 'g2' } }), { best: 'Kxg2', cp: 0, depth: 0 });
  eq('an illegal move is not graded', gradeMove({ move: { from: 'e2', to: 'e5' } }), null);
}

// The live grader: only our seat, in ply order, dropped on a take-back.
{
  const bus = createBus();
  const rules = createRules();
  const game = { rules };
  let made = 0;
  const workerFactory = () => {
    made++;
    const w = { onmessage: null, onerror: null, dead: false, terminate() { w.dead = true; },
      postMessage(data) { setTimeout(() => { if (!w.dead) w.onmessage?.({ data: { id: data.id, ...(gradeMove(data) || { error: true }) } }); }, 0); } };
    return w;
  };
  const iq = createIqLive({ bus, game, workerFactory });
  const heard = [];
  bus.on('iq', g => heard.push(g));
  bus.emit('local', { sides: ['w'], mode: 'solo' });
  eq('our seat is the only one graded', iq.sides(), ['w']);
  eq('the opponent has no score at all', [iq.value('b'), iq.track('b'), iq.worst('b')], [null, null, null]);
  const play = (from, to) => { rules.move(from, to); bus.emit('turn', { ply: rules.ply() }); };
  play('e2', 'e4'); play('d7', 'd5'); play('d1', 'g4'); play('c8', 'g4');
  await iq.settled(5000);
  eq('every local move landed, in order', heard.map(g => [g.ply, g.side, g.san]), [[1, 'w', 'e4'], [3, 'w', 'Qg4']]);
  eq('the hung queen is the worst move', iq.worst('w')?.san, 'Qg4');
  ok(`the hung queen cost points (IQ ${iq.value('w')})`, iq.value('w') <= 140 - iqLoss(800));
  const rec = iq.record();
  eq('the record has our seat only', Object.keys(rec), ['w']);
  eq('the record carries the moves', rec.w.moves.map(m => m.san), ['e4', 'Qg4']);
  const fallen = iq.value('w');
  rules.undo(); rules.undo(); bus.emit('takeback', { ply: rules.ply() }); bus.emit('turn', { ply: rules.ply() });
  await iq.settled(5000);
  eq('a take-back drops the grade with its move', iq.track('w').moves.map(m => m.san), ['e4']);
  eq('but not what the blunder cost', iq.value('w'), fallen);
  play('g1', 'f3');
  await iq.settled(5000);
  eq('the replayed ply is graded fresh', iq.track('w').moves.map(m => m.san), ['e4', 'Nf3']);
  // Ne5 walks out of the pin and hangs the queen, and is taken back before its grade comes in
  play('c8', 'g4');
  const pinned = iq.value('w');
  play('f3', 'e5'); rules.undo(); bus.emit('takeback', { ply: rules.ply() }); bus.emit('turn', { ply: rules.ply() });
  await iq.settled(5000);
  ok(`a blunder taken back before its grade still costs (IQ ${pinned} -> ${iq.value('w')})`, iq.value('w') < pinned && heard[heard.length - 1]?.gone === true);
  eq('and stays off the record', iq.track('w').moves.map(m => m.san), ['e4', 'Nf3']);
  // a resumed game keeps what still matches the board, and what the rest cost
  iq.restore({ w: { moves: [{ ply: 1, side: 'w', san: 'e4', best: 'e4', cp: 0 }, { ply: 3, side: 'w', san: 'Qg4', best: 'Nc3', cp: 924 }] } });
  eq('a restore keeps only moves still on the board', iq.track('w').moves.map(m => m.san), ['e4']);
  eq('and keeps the cost of the one that is not', iq.value('w'), 140 - iqLoss(924));
  const epoch = iq.epoch();
  bus.emit('newgame', { ply: 0 });
  eq('a new game starts clean', iq.value('w'), 140);
  ok('a new game moves the epoch on, so a late settle can tell', iq.epoch() !== epoch);
  eq('one worker served the whole game', made, 1);
  iq.dispose();
}

// The server corrects a guessed online seat: the score moves to the real side.
{
  const bus = createBus();
  let seats = ['w'];
  const iq = createIqLive({ bus, game: { rules: createRules(), get seats() { return seats; } }, workerFactory: () => ({ postMessage() {}, terminate() {} }) });
  bus.emit('local', { sides: ['w'], mode: 'online' });
  seats = ['b'];
  bus.emit('seat', { color: 'b' });
  eq('a corrected seat is the one with a score', [iq.sides(), iq.value('w'), iq.value('b')], [['b'], null, 140]);
  iq.dispose();
}

// A grader that cannot start leaves the score alone instead of throwing.
{
  const bus = createBus();
  const rules = createRules();
  const quiet = console.warn; console.warn = () => {};
  const iq = createIqLive({ bus, game: { rules }, workerFactory: () => { throw new Error('no workers here'); } });
  bus.emit('local', { sides: ['w', 'b'] });
  rules.move('e2', 'e4'); bus.emit('turn', {});
  await iq.settled(200);
  eq('no worker: the score stays at the start', [iq.value('w'), iq.value('b')], [140, 140]);
  iq.dispose();
  // a module worker that fails to load says so later, through onerror
  const late = createBus(), lateRules = createRules();
  const iq2 = createIqLive({ bus: late, game: { rules: lateRules }, workerFactory: () => {
    const w = { onmessage: null, onerror: null, terminate() {}, postMessage() { setTimeout(() => w.onerror?.({ message: 'failed to load' }), 0); } };
    return w;
  } });
  late.emit('local', { sides: ['w'] });
  lateRules.move('e2', 'e4'); late.emit('turn', {});
  await iq2.settled(500);
  console.warn = quiet;
  eq('a worker that fails late: the score stays at the start', iq2.value('w'), 140);
  iq2.dispose();
}

console.log(`${passed} checks passed`);
for (const f of failures) console.log('FAILED ' + f);
process.exit(failures.length ? 1 : 0);
