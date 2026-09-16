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

let passed = 0;
const failures = [];

function ok(what, cond) {
  if (cond) { passed++; return; }
  failures.push(what);
}
function eq(what, got, want) {
  ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));
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
  c.start('w');
  clock += 1200;
  eq('only the side to move is charged', [c.remaining('w'), c.remaining('b')], [3800, 5000]);
  c.press('b');
  clock += 800;
  eq('pressing the clock swaps who pays', [c.remaining('w'), c.remaining('b')], [3800, 4200]);
  c.stop();
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
