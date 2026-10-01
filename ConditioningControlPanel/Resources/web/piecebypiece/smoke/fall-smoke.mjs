// node smoke/fall-smoke.mjs - the end card's IQ recap: whose seat it shows, the move
// numbers, the "Gone at move" line, the curve, and what the shelf and the profile keep.
import assert from 'node:assert/strict';
import { FALL, seatsShown, moveNumber, worstOf, goneLine, endOf, curvePoints, recapRows, recapHtml, plainIq, iqFromApi } from '../door/fall.js';
import { profileStats, finalIq, seatIq } from '../door/store.js';

const grade = (ply, san, best, loss, value, side = ply % 2 ? 'w' : 'b') => ({ ply, side, san, best, cp: loss * 12, loss, value, fen: 'not kept' });
const white = { start: 140, end: 61, low: 61, moves: [grade(1, 'e4', 'e4', 0, 140), grade(3, 'Qh5', 'Nf3', 4, 136), grade(37, 'Qxd5', 'Nf3', 30, 106), grade(39, 'Kh1', 'Rd1', 30, 76), grade(41, 'Rxe8', 'Rxe8', 15, 61)] };
const black = { start: 140, end: 120, low: 120, moves: [grade(2, 'e5', 'e5', 0, 140), grade(4, 'Nc6', 'Nc6', 20, 120)] };

// Move numbers: the full move that made the game `ply` half-moves long.
assert.equal(moveNumber(1), 1); assert.equal(moveNumber(2), 1); assert.equal(moveNumber(37), 19); assert.equal(moveNumber(38), 19);
assert.equal(moveNumber(0), 1, 'never a move zero');

// The worst grade is the biggest loss, the earliest on a tie, and only a real loss counts.
assert.equal(worstOf(white.moves).ply, 37, 'a tie goes to the earlier move: that is where it went');
assert.equal(worstOf([grade(1, 'e4', 'e4', 0, 140)]), null, 'nothing lost, nothing worst');
assert.equal(worstOf(null), null);

// The line: the better move only when there was one.
assert.equal(goneLine(white.moves[2]), 'Gone at move 19: Qxd5. Better: Nf3.');
assert.equal(goneLine({ ...white.moves[2], best: 'Qxd5' }), 'Gone at move 19: Qxd5.');
assert.equal(goneLine({ ...white.moves[2], best: null }), 'Gone at move 19: Qxd5.');
assert.equal(goneLine(null), 'Not one point lost.');
assert.ok(!/[–—!]/.test(goneLine(white.moves[2]) + goneLine(null)), 'no dashes, no exclamation marks');

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

console.log('fall: seats, move numbers, the gone line, the curve, the markup and the shelf passed');
