// node smoke/fall-smoke.mjs - the end card's IQ recap: whose seat it shows, the move
// numbers, the "Gone at move" line, the curve, and what the shelf and the profile keep.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { FALL, seatsShown, moveNumber, worstOf, goneLine, endOf, curvePoints, recapRows, recapHtml, plainIq, iqFromApi, keptIq, fallPlan } from '../door/fall.js';
import { profileStats, finalIq, seatIq } from '../door/store.js';
import { createIqTracker, createIqLive, IQ } from '../game/iq.js';
import { Chess } from '../vendor/chess.js';
import { opponentWord, fallHeadline, fallSubline, boardBefore, fallCardModel } from '../door/fall-card.js';

const grade = (ply, san, best, loss, value, side = ply % 2 ? 'w' : 'b') => ({ ply, side, san, best, cp: loss * 12, loss, value, fen: 'not kept' });
const white = { start: 140, end: 61, low: 61, moves: [grade(1, 'e4', 'e4', 0, 140), grade(3, 'Qh5', 'Nf3', 4, 136), grade(37, 'Qxd5', 'Nf3', 30, 106), grade(39, 'Kh1', 'Rd1', 30, 76), grade(41, 'Rxe8', 'Rxe8', 15, 61)] };
const black = { start: 140, end: 120, low: 120, moves: [grade(2, 'e5', 'e5', 0, 140), grade(4, 'Nc6', 'Nc6', 20, 120)] };

// Move numbers: the full move that made the game `ply` half-moves long.
assert.equal(moveNumber(1), 1); assert.equal(moveNumber(2), 1); assert.equal(moveNumber(37), 19); assert.equal(moveNumber(38), 19);
assert.equal(moveNumber(0), 1, 'never a move zero');

// The worst grade is the biggest loss, the earliest on a tie, and only a real loss counts.
assert.equal(worstOf(white.moves).ply, 37, 'a tie goes to the earlier move: that is where it went');
assert.equal(worstOf([grade(1, 'e4', 'e4', 0, 140)]), null, 'nothing lost, nothing worst');
assert.equal(worstOf([{ ...grade(3, 'a3', 'e4', 25, 115), cp: 500 }, { ...grade(5, 'Qh5', 'Nf3', 25, 90), cp: 900 }]).ply, 5, 'a bigger cp breaks a tie first, as game/iq.js does');
assert.equal(worstOf(null), null);

// The line: the better move only when there was one.
assert.equal(goneLine(white.moves[2]), 'Gone at move 19: Qxd5. Better: Nf3.');
assert.equal(goneLine({ ...white.moves[2], best: 'Qxd5' }), 'Gone at move 19: Qxd5.');
assert.equal(goneLine({ ...white.moves[2], best: null }), 'Gone at move 19: Qxd5.');
assert.equal(goneLine(null), 'Not one point lost.');
assert.ok(!/[\u2013\u2014!]/.test(goneLine(white.moves[2]) + goneLine(null)), 'no dashes, no exclamation marks');

// Whose seat: solo and online show your own seat only, even with the other seat in the record.
assert.deepEqual(seatsShown({ mode: 'solo', me: 'b', iq: { w: white, b: black } }), ['b']);
assert.deepEqual(seatsShown({ mode: 'online', me: 'w', iq: { w: white, b: black } }), ['w']);
assert.deepEqual(seatsShown({ mode: 'online', me: null, iq: { w: white } }), [], 'no known seat online: show nothing rather than guess');
assert.deepEqual(seatsShown({ mode: 'hotseat', me: null, iq: { w: white, b: black } }), ['w', 'b'], 'two players here: both heads');
assert.deepEqual(seatsShown({ mode: 'hotseat', me: null, iq: { b: black } }), ['b']);
assert.deepEqual(seatsShown({ mode: 'solo', me: 'w' }), [], 'an old save has no fall');
assert.deepEqual(seatsShown({ mode: 'solo', me: 'w', iq: { w: { start: 'x', moves: [] } } }), [], 'a broken track is skipped');

// The curve: one point per grade plus the start, on a fixed axis down to the floor, never rising.
const c = curvePoints(white);
assert.equal(c.points.length, white.moves.length + 1);
assert.equal(c.points[0][0], FALL.curve.pad, 'starts at the left pad');
assert.equal(c.points.at(-1)[0], FALL.curve.w - FALL.curve.pad, 'ends at the right pad');
assert.equal(c.points[0][1], FALL.curve.pad, 'the start sits at the top');
for (let i = 1; i < c.points.length; i++) assert.ok(c.points[i][1] >= c.points[i - 1][1], 'a drain only goes down');
assert.deepEqual(c.worst, c.points[3], 'the dot sits on the worst move');
assert.deepEqual(curvePoints({ ...white, worst: white.moves[3] }).worst, c.points[4], "the track's own worst move wins");
assert.match(c.path, /^M5 5 L/);
const floorY = FALL.curve.h - FALL.curve.pad;
const small = curvePoints({ start: 140, moves: [grade(1, 'a3', 'e4', 10, 130)] });
assert.ok(small.points[1][1] < floorY / 3, 'a ten point slip looks small on the fixed axis');
const flat = curvePoints({ start: 140, moves: [] });
assert.equal(flat.points.length, 1); assert.equal(flat.worst, null);
const deep = curvePoints({ start: 140, moves: [grade(1, 'f3', 'e4', 30, 20)] });
assert.ok(deep.points[1][1] <= floorY + 1e-9, 'below the usual floor still fits the box');

// The rows and the markup.
const rows = recapRows({ mode: 'solo', me: 'w', iq: { w: white, b: black } });
assert.equal(rows.length, 1);
assert.deepEqual([rows[0].start, rows[0].end, rows[0].low], [140, 61, 61]);
assert.equal(rows[0].line, 'Gone at move 19: Qxd5. Better: Nf3.');
const html = recapHtml({ mode: 'solo', me: 'w', iq: { w: white, b: black } });
assert.match(html, /IQ 140<\/span> <span class="arrow">&rarr;<\/span> <b class="to">61<\/b>/);
assert.ok(!html.includes('120'), "the opponent's IQ never reaches your card");
assert.ok(!html.includes('fall-who'), 'one seat needs no label');
const both = recapHtml({ mode: 'hotseat', me: null, iq: { w: white, b: black } });
assert.match(both, /class="fall two"/); assert.match(both, />white</); assert.match(both, />black</);
assert.equal(recapHtml({ mode: 'solo', me: 'w' }), '', 'nothing to say, no markup');
assert.ok(recapHtml({ mode: 'solo', me: 'w', iq: { w: { ...white, moves: [{ ...white.moves[2], san: '<b>' }] } } }).includes('&lt;b&gt;'), 'moves are escaped');
assert.match(recapHtml({ mode: 'solo', me: 'w', iq: { w: white } }, { extra: '<i class="x"></i>' }), /<i class="x"><\/i><\/div>$/);

// What the shelf keeps: the contract's fields, an end, a low and a worst, nothing else.
const kept = plainIq({ w: { start: 140, value: 76, moves: white.moves.slice(0, 4) } });
assert.equal(kept.w.end, 76, "the grader's `value` reads as the end");
assert.equal(kept.w.low, 76);
assert.equal(kept.w.worst.ply, 37);
assert.equal(kept.w.moves[0].fen, undefined, 'only the grade fields are kept');
assert.equal(plainIq(null), null); assert.equal(plainIq({ w: { moves: [] } }), null);
assert.equal(endOf({ start: 140, moves: [] }), 140);

// The live grader, read through its contract; anything off reads as nothing.
const api = { start: 140, sides: () => ['b'], value: () => 120, track: (s) => (s === 'b' ? { start: 140, value: 120, low: 120, moves: black.moves } : null), worst: () => black.moves[1], settled: async () => {} };
const fromApi = iqFromApi(api);
assert.deepEqual(Object.keys(fromApi), ['b']);
assert.equal(fromApi.b.end, 120); assert.equal(fromApi.b.worst.san, 'Nc6');
assert.equal(iqFromApi(null), null);
assert.equal(iqFromApi({ sides: () => { throw new Error('gone'); }, track: () => null }), null);

// The grader's own record (game/iq.js) reads straight into the recap: the contract, end to end.
const tracker = createIqTracker();
tracker.note({ ply: 1, side: 'w', san: 'e4', best: 'e4', cp: 0 });
tracker.note({ ply: 3, side: 'w', san: 'Qh5', best: 'Nf3', cp: 140 });
tracker.note({ ply: 5, side: 'w', san: 'Qxf7', best: 'Nf3', cp: 900 });
const graded = { mode: 'solo', me: 'w', iq: plainIq({ w: tracker.toRecord() }) };
const [row] = recapRows(graded);
assert.equal(row.start, IQ.start);
assert.equal(row.end, tracker.value, "the card's end is the grader's value");
assert.equal(row.worst.ply, 5);
assert.equal(row.line, 'Gone at move 3: Qxf7. Better: Nf3.');
assert.equal(finalIq(graded), tracker.value);

// The shelf and the profile: your seat's final IQ; the lowest across games; hotseat counts for neither.
const games = [
  { me: 'w', result: { winner: 'b' }, iq: { w: white } },
  { me: 'b', result: { winner: 'w' }, iq: { b: { start: 140, end: 88, low: 88, moves: [] } } },
  { me: null, result: { winner: 'w' }, iq: { w: { start: 140, end: 10, low: 10, moves: [] } } },
  { me: 'w', result: null },
];
assert.equal(finalIq(games[0]), 61); assert.equal(finalIq(games[2]), null); assert.equal(finalIq(games[3]), null);
assert.equal(seatIq(games[2]), null);
assert.equal(profileStats(games).lowIq, 61);
assert.equal(profileStats([games[3]]).lowIq, null, 'no graded game, no stat');

// Watch the fall, and the picture: a real game. 3...Nf6 lets 4.Qxf7 mate.
const scholar = { mode: 'solo', me: 'b', opponent: 'Computer \u00b7 Beginner', plies: 7, result: { result: 'checkmate', winner: 'w', reason: 'checkmate' },
  moves: ['e4', 'e5', 'Bc4', 'Nc6', 'Qh5', 'Nf6', 'Qxf7#'],
  iq: { b: { start: 140, end: 110, low: 110, moves: [grade(2, 'e5', 'e5', 0, 140), grade(4, 'Nc6', 'Nc6', 0, 140), grade(6, 'Nf6', 'g6', 30, 110)] } } };
const plan = fallPlan(scholar);
assert.deepEqual([plan.from, plan.to, plan.punish, plan.worst.san], [5, 7, 7, 'Nf6'], 'from just before the move, through the capture that punished it');
assert.equal(fallPlan({ ...scholar, moves: scholar.moves.slice(0, 5), plies: 5 }), null, 'a worst move the record does not reach is not watched');
assert.deepEqual([fallPlan({ ...scholar, moves: scholar.moves.slice(0, 6), plies: 6 }).to, fallPlan({ ...scholar, moves: scholar.moves.slice(0, 6), plies: 6 }).punish], [6, null], 'the last move of the game: just that move');
const quiet = { ...scholar, moves: ['e4', 'e5', 'Bc4', 'Nc6', 'Qh5', 'Nf6', 'Qd1', 'd6', 'Qe2'], plies: 9 };
assert.deepEqual([fallPlan(quiet).to, fallPlan(quiet).punish], [7, null], 'nothing taken: through their one reply');
const late = { ...scholar, moves: ['e4', 'e5', 'Bc4', 'Nc6', 'Qh5', 'Nf6', 'd3', 'a6', 'Qxf7#'], plies: 9 };
assert.equal(fallPlan(late).punish, 9, 'a capture on their second move after it still counts');
assert.equal(fallPlan({ ...scholar, mode: 'hotseat', me: null }), null, 'two players here: no fall to watch');
assert.equal(fallPlan({ ...scholar, iq: { b: { ...scholar.iq.b, moves: [grade(2, 'e5', 'e5', 0, 140)] } } }), null, 'no loss, no fall');

assert.equal(opponentWord('Computer \u00b7 Beginner'), 'the Beginner computer');
assert.equal(opponentWord('Sam'), 'Sam'); assert.equal(opponentWord(''), 'a friend');
assert.equal(fallHeadline(scholar), 'Lost to the Beginner computer');
assert.equal(fallHeadline({ ...scholar, result: { winner: 'b', reason: 'resign' }, opponent: 'Sam' }), 'Beat Sam');
assert.equal(fallHeadline({ ...scholar, result: { winner: null, reason: 'stalemate' } }), 'Drew with the Beginner computer');
assert.equal(fallSubline(scholar), 'by checkmate, 4 moves');
const scholarHere = { ...scholar, mode: 'hotseat', me: null, opponent: 'a friend here' };
assert.equal(fallHeadline(scholarHere, 'b'), 'Black lost by checkmate', 'two players here: from the seat on the picture');
assert.equal(fallHeadline(scholarHere, 'w'), 'White won by checkmate');
assert.equal(fallSubline(scholarHere), '4 moves', 'the reason is not said twice');

const before = boardBefore(scholar, plan.worst);
assert.deepEqual(before.played, { from: 'g8', to: 'f6' }, 'what was played, in pink');
assert.deepEqual(before.better, { from: 'g7', to: 'g6' }, 'the better move, ringed');
assert.deepEqual(before.rows[3][7], { type: 'q', side: 'w' }, 'the queen stands on h5 before the move');
assert.equal(before.rows[2][5], null, 'f6 is still empty');
assert.equal(boardBefore(scholar, { ...plan.worst, best: 'Nf6' }).better, null, 'no ring when the move was the best one');
assert.equal(boardBefore({ ...scholar, moves: ['e4', 'nonsense'] }, plan.worst), null, 'a broken record draws no board');

const model = fallCardModel(scholar);
assert.deepEqual([model.side, model.flip, model.start, model.end], ['b', true, 140, 110]);
assert.equal(model.line, 'Gone at move 3: Nf6. Better: g6.');
assert.equal(model.boardLabel, 'move 3, before Nf6');
assert.equal(fallCardModel({ ...scholar, iq: null }), null, 'no fall, no picture');
const twoHere = fallCardModel({ ...scholar, mode: 'hotseat', me: null, iq: { w: white, b: scholar.iq.b } });
assert.equal(twoHere.side, 'b', 'two players here: the picture is about the side that lost');
assert.equal(twoHere.headline, 'Black lost by checkmate');
for (const s of [model.headline, model.subline, model.line, model.boardLabel]) assert.ok(!/[\u2013\u2014!]/.test(s), 'plain copy');

// The director lets the fall's review play its replay, and only while it is open.
const director = readFileSync(new URL('../board/director.js', import.meta.url), 'utf8');
assert.match(director, /const menuUp = \(\) => !underDoor && /, 'the door stands the replay down unless the fall is being watched');
assert.match(director, /allowUnderDoor\(on\) \{ underDoor = !!on; if \(!underDoor\) dropReplay\(\); \}/);
const door = readFileSync(new URL('../door/door.js', import.meta.url), 'utf8');
assert.match(door, /function closeReplay\(\) \{\s+stopReplay\(\);\s+replay = null;\s+try \{ board\.director\?\.allowUnderDoor\?\.\(false\); \}/, 'closing any review turns it off again');

// The settle (door.js settleIq): the menu or a rematch before the last grade came in starts the
// grader over, and the card and the shelf keep the record's fall, never the next game's empty one.
{
  const handlers = {};
  const bus = { on(n, f) { (handlers[n] ||= []).push(f); return () => {}; }, emit(n, p) { for (const f of handlers[n] || []) f(p); } };
  const chess = new Chess();
  const worker = { inbox: [], postMessage(m) { this.inbox.push(m); }, terminate() {}, onmessage: null, onerror: null };
  const live = createIqLive({ bus, game: { rules: { chess }, seats: ['w'] }, workerFactory: () => worker });
  const answer = (cp) => { const m = worker.inbox.shift(); worker.onmessage({ data: { id: m.id, best: 'Nf3', cp } }); };
  bus.emit('local', { sides: ['w'], mode: 'solo' });
  chess.move('e4'); bus.emit('turn', {}); answer(0);
  chess.move('e5'); bus.emit('turn', {});
  chess.move('Qh5'); bus.emit('turn', {}); answer(400);
  chess.move('Nc6'); bus.emit('turn', {});
  chess.move('Qxf7+'); bus.emit('gameover', {});   // its grade is still out
  const atEnd = live.record(), epoch = live.epoch();
  assert.equal(keptIq(live, epoch, atEnd).w.moves.length, 2, 'nothing moved on: the live grader is the fall');
  bus.emit('newgame', { ply: 0 });                  // the menu
  answer(900);                                      // lands for a game that is gone
  assert.equal(keptIq(live, epoch, atEnd).w.end, atEnd.w.end, "after the menu the record's fall is kept, not an empty one");
  assert.equal(keptIq(live, epoch, atEnd).w.moves.length, 2);
  assert.equal(keptIq(live, null, atEnd, false).w.end, atEnd.w.end, 'a game dealt meanwhile: the record');
  live.dispose();
}

console.log('fall: seats, move numbers, the gone line, the curve, the markup, the shelf, the watch plan, the picture and the settle passed');
