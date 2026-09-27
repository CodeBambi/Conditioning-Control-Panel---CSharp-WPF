/* ============================================================================
 * smoke/rules-smoke.mjs - node checks for the referee, the clocks and a whole
 * hotseat game. No browser and no dependencies: run it with
 *
 *   node smoke/rules-smoke.mjs
 *
 * Exits non-zero on the first failure, with the check that broke.
 * ==========================================================================*/

import { createRules, readResult } from '../game/rules.js';
import { createClock, formatClock, DEFAULT_MS } from '../game/clock.js';
import { createHotseat } from '../game/hotseat.js';
import { createBus } from '../game/events.js';
import { createSolo } from '../game/solo.js';
import { createTurnHandoff, handoffSeconds } from '../ui/turn-handoff.js';
import { turnOpacity, turnRecipe, readTurn } from '../game/turn-loom.js';

let passed = 0;
const failures = [];

function ok(what, cond) {
  if (cond) { passed++; return; }
  failures.push(what);
}
function eq(what, got, want) {
  ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));
}

// The same online turn must look identical from either seat, even after reconnect.
{
  eq('Loom waits ten seconds', turnOpacity(9999), 0);
  eq('Loom halfway through its fade', turnOpacity(16000), .5);
  eq('Loom fully replaces squares', turnOpacity(22000), 1);
  eq('Loom clamps a long think', turnOpacity(90000), 1);
  const clock = createClock({ perSideMs: 0, now: () => 12000 }); clock.start('w');
  const rules = createRules();
  const game = { clock, rules, current: { matchId: 'shared-table' }, isOver: () => false,
    turn: () => rules.turn(), plies: () => rules.ply() };
  const first = readTurn(game, 'white-local-seed');
  eq('Loom ignores local seat seed online', first.key, readTurn(game, 'black-local-seed').key);
  eq('Loom recipe is deterministic', turnRecipe(first.key), turnRecipe(first.key));
  ok('Loom varies between turns', JSON.stringify(turnRecipe(first.key)) !== JSON.stringify(turnRecipe(first.key + '|next')));
  rules.move('e2', 'e4');
  eq('optimistic next turn cannot inherit old age', readTurn(game, 'local'), null);
  clock.press('b');
  ok('confirmed next turn gets a new recipe key', readTurn(game, 'local').key !== first.key);
  clock.stop();
  eq('a stopped game cannot grow the Loom', readTurn(game, 'local'), null);
}
// --- the referee -----------------------------------------------------------
{
  const r = createRules();
  eq('opening side to move', r.turn(), 'w');
  eq('32 men at the start', Object.keys(r.position()).length, 32);
  eq('a pawn has two opening moves', r.targets('e2').sort(), ['e3', 'e4']);
  eq('an empty square offers nothing', r.targets('e5'), []);
  ok('an illegal move is refused', r.move('e2', 'e5') === null);
  ok('a legal move is played', r.move('e2', 'e4') !== null);
  eq('the turn passes', r.turn(), 'b');
  eq('the position keeps its count', Object.keys(r.position()).length, 32);
}

// --- captures, en passant, castling, promotion -----------------------------
{
  const r = createRules();
  r.move('e2', 'e4'); r.move('d7', 'd5');
  const taken = r.move('e4', 'd5');
  eq('a capture reports its victim', [taken.captured, taken.capturedSide, taken.capturedSquare], ['p', 'b', 'd5']);
}
{
  const r = createRules('rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3');
  const ep = r.move('e5', 'f6');
  ok('en passant is legal', ep !== null);
  eq('en passant takes the pawn behind it', [ep.enPassant, ep.capturedSquare], [true, 'f5']);
}
{
  const r = createRules('r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1');
  const castle = r.move('e1', 'g1');
  eq('kingside castling is flagged', castle.castle, 'k');
  eq('the rook hops with the king', r.rookHop(castle), { from: 'h1', to: 'f1' });
}
{
  const r = createRules('8/P6k/8/8/8/8/7K/8 w - - 0 1');
  const promo = r.move('a7', 'a8', 'q');
  eq('a pawn promotes to what it was asked for', promo.promotion, 'q');
  eq('the new queen is on the board', r.position().a8, { type: 'q', side: 'w' });
}

// --- how a game ends -------------------------------------------------------
{
  const r = createRules();
  r.move('f2', 'f3'); r.move('e7', 'e5'); r.move('g2', 'g4');
  const mate = r.move('d8', 'h4');
  ok('mate is seen', mate.check && r.isOver());
  eq('the mated side loses', r.result(), { result: 'checkmate', winner: 'b', reason: 'checkmate' });
}
{
  const r = createRules('7k/5Q2/6K1/8/8/8/8/8 b - - 0 1');
  eq('stalemate is a draw', r.result().result, 'stalemate');
}
{
  const r = createRules('7k/8/6K1/8/8/8/8/8 w - - 0 1');
  eq('two bare kings cannot mate', r.result().reason, 'insufficient material');
}
{
  const r = createRules('7k/8/8/8/8/8/8/R6K b - - 99 60');
  ok('the last move before the draw is legal', r.move('h8', 'g8') !== null);
  eq('the fifty move rule draws', r.result().reason, 'fifty move rule');
  ok('readResult agrees with the wrapper', readResult(r.chess).result === 'draw');
}

// --- the clocks ------------------------------------------------------------
{
  let clock = 0;
  const c = createClock({ perSideMs: 5000, now: () => clock });
  eq('both sides start with the full budget', c.snapshot(), { w: 5000, b: 5000, total: 5000, active: null });
  eq('stopped clock has no current turn age', c.turnElapsedMs(), 0);
  c.start('w');
  clock += 1200;
  eq('only the side to move is charged', [c.remaining('w'), c.remaining('b')], [3800, 5000]);
  eq('draining a balance does not reset turn age', c.turnElapsedMs(), 1200);
  c.start('w');
  eq('starting the same active seat preserves turn age', c.turnElapsedMs(), 1200);
  c.press('b');
  eq('the next turn starts at zero', c.turnElapsedMs(), 0);
  clock += 800;
  eq('pressing the clock swaps who pays', [c.remaining('w'), c.remaining('b')], [3800, 4200]);
  eq('next side has its own turn age', c.turnElapsedMs(), 800);
  c.stop();
  eq('stopping clears the reported turn age', c.turnElapsedMs(), 0);
  clock += 10000;
  eq('a stopped clock charges nobody', [c.remaining('w'), c.remaining('b')], [3800, 4200]);
}
{
  let flagged = null;
  let clock = 0;
  const c = createClock({ perSideMs: 1000, now: () => clock, onFlag: (side) => { flagged = side; } });
  c.start('w');
  clock += 1500;
  c.remaining('w');
  eq('running out flags that side', flagged, 'w');
  eq('a clock never goes below zero', c.remaining('w'), 0);
  ok('the clock stops once it has flagged', !c.isRunning());
  eq('a flagged clock has no active turn age', c.turnElapsedMs(), 0);
}
eq('the clock reads like a clock', [formatClock(DEFAULT_MS), formatClock(64000), formatClock(9400), formatClock(-5)],
  ['15:00', '1:04', '0:09.4', '0:00.0']);

// --- a whole hotseat game --------------------------------------------------
{
  const bus = createBus();
  const seen = [];
  for (const type of ['turn', 'capture', 'check', 'gameover', 'local']) bus.on(type, (p) => seen.push([type, p]));
  const board = stubBoard();
  const game = createHotseat({ bus, board, clockMs: 60000 });
  game.start();
  eq('the first turn is white', seen[0], ['turn', { side: 'w', ply: 0, clocks: { w: 60000, b: 60000, total: 60000 }, total: 60000 }]);
  ok('an unplayable square cannot be picked up', !game.canPick('e5') && !game.canPick('e7'));
  ok('the side to move can be picked up', game.canPick('e2'));
  eq('legal targets come from the referee', game.legalTargets('g1').sort(), ['f3', 'h3']);
  ok('an illegal move is refused', game.tryMove('e2', 'e5') === null);
  game.tryMove('f2', 'f3'); game.tryMove('e7', 'e5'); game.tryMove('g2', 'g4');
  eq('the board followed every move', board.moves.length, 3);
  game.tryMove('d8', 'h4');
  const end = seen.filter((e) => e[0] === 'gameover').pop();
  eq('the game ends in mate for black', end[1], { result: 'checkmate', winner: 'b' });
  ok('the check event fired', seen.some((e) => e[0] === 'check'));
  ok('a finished game refuses more moves', game.tryMove('e1', 'f2') === null);
  ok('the clocks stopped with the game', !game.clock.isRunning());
}
{
  const bus = createBus();
  const board = stubBoard();
  const game = createHotseat({ bus, board, clockMs: 60000 });
  game.start();
  game.tryMove('e2', 'e4'); game.tryMove('d7', 'd5');
  let captured = null;
  bus.on('capture', (p) => { captured = p; });
  game.tryMove('e4', 'd5');
  eq('the capture event names the man that came off', captured, { by: 'w', piece: 'p', square: 'd5', victimSide: 'b' });
}
{
  const bus = createBus();
  const board = stubBoard();
  const game = createHotseat({ bus, board, clockMs: 60000, auto: 6 });
  game.start();
  for (let i = 0; i < 40 && game.autoRemaining() > 0; i++) game.update(1);
  ok('auto play stops after the moves it was given', game.autoRemaining() === 0);
  ok('auto play left a legal position behind', game.rules.ply() > 0 && game.rules.ply() <= 6);
}

// --- taking it back --------------------------------------------------------
{
  const r = createRules();
  ok('nothing to take back at the start', r.undo() === null);
  r.move('e2', 'e4');
  const back = r.undo();
  eq('the ply that came off', [back.from, back.to, back.side], ['e2', 'e4', 'w']);
  eq('the position is the one before it', r.fen().split(' ')[0], 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR');
  eq('and white has the move again', r.turn(), 'w');
}
{
  // A capture, taken back, puts the man that came off back on the board.
  const r = createRules();
  r.move('e2', 'e4'); r.move('d7', 'd5'); r.move('e4', 'd5');
  eq('the pawn was taken', Object.keys(r.position()).length, 31);
  const back = r.undo();
  eq('the take-back names the man that came off', back.captured, 'p');
  eq('and he is back on the board', Object.keys(r.position()).length, 32);
  eq('standing where he stood', r.pieceAt('d5'), { type: 'p', color: 'b' });
}
{
  // A promotion, taken back, is a pawn again.
  const r = createRules('8/P7/8/8/8/8/8/k6K w - - 0 1');
  r.move('a7', 'a8', 'q');
  eq('he came up a queen', r.pieceAt('a8').type, 'q');
  const back = r.undo();
  eq('the take-back knows it was a promotion', back.promotion, 'q');
  eq('and he is a pawn on his own square again', r.pieceAt('a7'), { type: 'p', color: 'w' });
  ok('with nothing left on the eighth', r.pieceAt('a8') === null);
}
{
  // Castling, taken back, brings the rook home too.
  const r = createRules('r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1');
  r.move('e1', 'g1');
  eq('the rook castled with him', r.pieceAt('f1').type, 'r');
  r.undo();
  eq('the king is home', r.pieceAt('e1').type, 'k');
  eq('and so is the rook', r.pieceAt('h1').type, 'r');
  ok('with nothing left on f1', r.pieceAt('f1') === null);
}
{
  const bus = createBus();
  const board = stubBoard();
  const game = createHotseat({ bus, board, clockMs: 60000 });
  game.start();
  let turns = [];
  let backs = [];
  bus.on('turn', (p) => turns.push(p.side));
  bus.on('takeback', (p) => backs.push(p));
  game.tryMove('e2', 'e4');
  eq('black to move after the ply', game.turn(), 'b');
  const undone = game.takeBack(1000);
  ok('the ply came back', !!undone);
  eq('white has the move again', game.turn(), 'w');
  eq('the takeback event names the ply', backs, [{ from: 'e2', to: 'e4', ply: 0 }]);
  eq('and the turn event says who moves', turns[turns.length - 1], 'w');
  eq('the man slid home', board.moves[board.moves.length - 1], ['e4', 'e2']);
  ok('a second take-back inside the gap is refused', game.takeBack(1200) === null);
  ok('and with no ply left there is nothing to take', game.takeBack(3000) === null);
}
{
  // The clocks go back to what they read before the ply.
  const bus = createBus();
  const board = stubBoard();
  const game = createHotseat({ bus, board, clockMs: 60000 });
  game.start();
  game.clock.debit('w', 9000);
  const before = game.clock.remaining('w');
  game.tryMove('e2', 'e4');
  game.clock.debit('b', 4000);
  game.takeBack(1000);
  ok('white got his thinking time back', Math.abs(game.clock.remaining('w') - before) < 200);
  ok('and black is not charged for a ply he never had', Math.abs(game.clock.remaining('b') - 60000) < 200);
}
{
  // A finished game is a record, not a board.
  const bus = createBus();
  const board = stubBoard();
  const game = createHotseat({ bus, board, clockMs: 60000 });
  game.start();
  game.tryMove('f2', 'f3'); game.tryMove('e7', 'e5'); game.tryMove('g2', 'g4'); game.tryMove('d8', 'h4');
  ok('mate ended it', game.isOver());
  ok('and there is no taking that back', game.takeBack(9000) === null);
}

// Untimed games never flag, and saved SAN retains full repetition history.
{
  let elapsed = 0, flags = 0;
  const clock = createClock({ perSideMs: 0, now: () => elapsed, onFlag: () => flags++ });
  clock.start('w'); elapsed = 3600000; clock.debit('w', 9999999);
  eq('untimed cannot flag', flags, 0);
  eq('untimed still measures the current turn', clock.turnElapsedMs(), 3600000);
  clock.press('b');
  eq('untimed turn age resets on the next side', clock.turnElapsedMs(), 0);
  eq('untimed display', formatClock(clock.remaining('w')), 'Untimed');
  ok('untimed snapshot marks mode', clock.snapshot().untimed && clock.snapshot().total === 0);
  clock.stop();
  const moves = ['Nf3', 'Nf6', 'Ng1', 'Ng8', 'Nf3', 'Nf6', 'Ng1'];
  const game = createHotseat({ bus: createBus(), board: stubBoard(), clockMs: 0, restore: { moves, clocks: { total: 0 } } });
  game.start();
  eq('resume keeps complete history', game.record().moves, moves);
  eq('resume keeps ply count', game.plies(), 7);
  game.tryMove('f6', 'g8');
  eq('resume preserves repetition draw', game.result()?.reason, 'threefold repetition');
  game.dispose();
  const timed = createClock({ perSideMs: 60000, now: () => elapsed });
  timed.restore({ w: 20000, b: 40000 }); timed.start('w'); elapsed += 2000;
  eq('restored clock runs from saved value', timed.remaining('w'), 18000);
  eq('restored turn starts when play resumes', timed.turnElapsedMs(), 2000);
  timed.restore({ w: 10000, b: 20000 });
  eq('restore stands the clock down', timed.turnElapsedMs(), 0);
  elapsed += 50000; timed.start('w');
  eq('time away is excluded from a resumed local turn', timed.turnElapsedMs(), 0);
  elapsed += 500; timed.reset();
  eq('reset clears the current turn age', timed.turnElapsedMs(), 0);
  timed.start('w');
  eq('a new game has a fresh turn age', timed.turnElapsedMs(), 0);
  timed.stop();
}
// Computer search runs under the move performance; application waits for the handoff.
{
  const bus = createBus(), board = stubBoard();
  let busy = true, request = null, terminated = 0;
  const worker = { postMessage(value) { request = value; }, terminate() { terminated++; } };
  board.anim = { busy: () => busy, skip: () => { busy = false; } };
  const game = createSolo({ bus, board, options: { side: 'w', clockMs: 0 }, workerFactory: () => worker });
  const handoff = board.turnHandoff = createTurnHandoff({ bus, game, board });
  const tick = dt => { handoff.update(dt); game.update(dt); };
  game.start();
  eq('initial deal does not show a fake handoff', handoff.debug(), null);
  game.tryMove('e2', 'e4');
  ok('computer starts searching before the human animation settles', request?.moves[0] === 'e4');
  worker.onmessage({ data: { id: request.id, move: { from: 'e7', to: 'e5' } } });
  tick(2);
  eq('an early computer result waits through the human animation', game.plies(), 1);
  busy = false; tick(.4);
  eq('handoff names the next solo player', handoff.debug()?.text, "Computer's turn");
  eq('reply still waits during the card', game.plies(), 1);
  tick(.26);
  eq('reply lands after the animation and short handoff', game.plies(), 2);
  eq('computer reply announces the human turn', handoff.debug()?.text, 'Your turn');
  game.tryMove('g1', 'f3');
  const stale = worker.onmessage, staleId = request.id;
  worker.onmessage({ data: { id: staleId, move: { from: 'b8', to: 'c6' } } });
  game.takeBack();
  stale({ data: { id: staleId, move: { from: 'b8', to: 'c6' } } });
  tick(2);
  eq('undo cancels both queued and late worker replies', game.plies(), 2);
  ok('undo stops the old search worker', terminated > 0);
  game.dispose(); handoff.dispose();
}
{
  const bus = createBus(), board = stubBoard();
  let request = null, busy = true;
  const worker = { postMessage(value) { request = value; }, terminate() {} };
  board.anim = { busy: () => busy, skip: () => { busy = false; } };
  const game = createSolo({ bus, board, options: { side: 'w', clockMs: 300000 }, workerFactory: () => worker });
  game.start(); game.clock.debit('b', 295000); game.tryMove('e2', 'e4');
  worker.onmessage({ data: { id: request.id, move: { from: 'e7', to: 'e5' } } });
  game.update(.01);
  eq('low clocks bypass the artificial computer pause', game.plies(), 2);
  ok('low-clock reply settles the earlier animation before moving', !busy);
  game.dispose();
}
{
  const bus = createBus(), board = {}, game = { isOnline: true, seats: ['w'], isOver: () => false,
    plies: () => 1, clock: { snapshot: () => ({ w: 60000, b: 60000 }) } };
  let menu = false;
  const handoff = createTurnHandoff({ bus, game, board, menuOpen: () => menu });
  bus.emit('turn', { side: 'w', ply: 0 }); bus.emit('turn', { side: 'b', ply: 1 }); handoff.update(.1);
  eq('online handoff names the opponent', handoff.debug()?.text, "Opponent's turn");
  menu = true; handoff.update(.1);
  eq('opening menu or replay clears the card', handoff.debug(), null);
  eq('low-clock handoff is shortened', handoffSeconds({ snapshot: () => ({ w: 20000, b: 60000 }) }), .2);
  handoff.dispose();
}

// --- report ----------------------------------------------------------------
function stubBoard() {
  const moves = [];
  return {
    moves,
    setSide() {},
    pieces: {
      setPosition() {},
      move(from, to) { moves.push([from, to]); },
      remove(sq) { moves.push(['x', sq]); },
    },
  };
}

console.log(`${passed} checks passed`);
for (const f of failures) console.log('FAILED ' + f);
process.exit(failures.length ? 1 : 0);
