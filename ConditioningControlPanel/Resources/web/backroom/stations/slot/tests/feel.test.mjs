import test from 'node:test';
import assert from 'node:assert/strict';
import { FEEL, tierOf, recipe, ladderSemis, winTokens, spendTokens, tickValues, glance, landPose, restPose,
         glanceHoldMs, breath, shiverPx, chaseMs, bezier, POSES, livePair, anticipation, almost, ALMOST } from '../feel.js';

const o = (line, pay, extra = {}) => ({ line, pay, meltLeft: 0, halved: false, ...extra });

test('House Book numbers: thud, bank, breath, glance, caps', () => {
  assert.equal(FEEL.THUD_MS, 340);
  assert.deepEqual(FEEL.THUD_EASE, [0.2, 1.5, 0.4, 1]);
  assert.equal(FEEL.BANK_FLY_MS, 560);
  assert.equal(FEEL.BANK_STAGGER_MS, 70);
  assert.ok(FEEL.BREATH_MS >= 2600 && FEEL.BREATH_MS <= 4000);
  assert.ok(FEEL.GLANCE_HOLD_MS >= 400 && FEEL.GLANCE_HOLD_MELT_MS <= 800 && glanceHoldMs(true) > glanceHoldMs(false));
  assert.ok(FEEL.LEAN_MS < FEEL.ANSWER_MS);
  for (const ms of [FEEL.THUD_MS, FEEL.SHIVER_MS, FEEL.BANK_FLY_MS, FEEL.GLOW_OUT_MS]) assert.ok(ms <= FEEL.MOVE_CAP_MS, `${ms} over the move cap`);
});

test('Law IX tiers by line, the jackpot stays the jackpot when halved', () => {
  assert.equal(tierOf(o('none', 0)), 0);
  assert.equal(tierOf(o('melt', 0)), 0);
  assert.deepEqual(['spiral2', 'sub2', 'gif3', 'spiral3', 'sub3', 'gif3same', 'emi3'].map(l => tierOf(o(l, 5))), [1, 1, 1, 2, 2, 3, 4]);
  assert.equal(tierOf(o('emi3', 200, { halved: true })), 4);
});

test('The Brake: a no-pay spin shivers with a muted thud, never silence', () => {
  const r = recipe(o('none', 0));
  assert.equal(r.shiver, true); assert.equal(r.sound, 'muted'); assert.equal(r.tokens, false);
});

test('The Brake: repetition shrinks the party, fanfare x3, then chime + bead, the 40th a thud and tokens', () => {
  const at = seen => recipe(o('gif3', 3), { seen });
  assert.deepEqual([0, 1, 2].map(s => at(s).party), ['fanfare', 'fanfare', 'fanfare']);
  assert.equal(at(3).party, 'bead'); assert.equal(at(3).sound, 'chime'); assert.equal(at(3).chase, false);
  assert.equal(at(38).party, 'bead');
  const late = at(39);
  assert.equal(late.party, 'thud'); assert.equal(late.sound, 'thud'); assert.equal(late.tokens, true); assert.equal(late.heat, 0);
});

test('Law IX sizes the party: chime, two notes + jolt, the thud, the reveal once a run', () => {
  assert.equal(recipe(o('gif3', 3)).sound, 'chime');
  assert.equal(recipe(o('sub3', 15)).sound, 'two'); assert.equal(recipe(o('sub3', 15)).jolt, true);
  assert.equal(recipe(o('gif3same', 40)).sound, 'thud');
  const jp = recipe(o('emi3', 400));
  assert.ok(jp.reveal && jp.gold && jp.sparks && jp.sound === 'reveal');
  const again = recipe(o('emi3', 400), { jackpots: 1 });
  assert.equal(again.reveal, false); assert.equal(again.sound, 'thud');
  assert.equal(recipe(o('gif3', 3)).reveal, false, 'only the jackpot reveals');
});

test('The Brake: never during melt, no ceremonies, tokens still move', () => {
  const r = recipe(o('gif3same', 20, { halved: true, meltLeft: 2 }));
  assert.equal(r.party, 'melt');
  assert.ok(!r.chase && !r.screen && !r.jolt && !r.reveal && !r.sparks);
  assert.equal(r.tokens, true);
  assert.equal(recipe(o('emi3', 200, { halved: true })).reveal, false);
});

test('THE CHIME LADDER: +1 a step, cap 7, an octave down while melted', () => {
  assert.deepEqual([0, 1, 2, 7, 12].map(s => ladderSemis(s, false)), [0, 1, 2, 7, 7]);
  assert.deepEqual([0, 3, 9].map(s => ladderSemis(s, true)), [-12, -9, -5]);
});

test('THE BANK: 3-7 tokens, 4 on lite, ticks land on the exact value', () => {
  assert.deepEqual([1, 2, 3, 4].map(t => winTokens(t)), [3, 4, 5, 7]);
  assert.ok([1, 2, 3, 4].every(t => winTokens(t, true) <= 4 && winTokens(t, true) >= 3));
  assert.ok(spendTokens(10) >= 3 && spendTokens(10) <= 7);
  assert.deepEqual(tickValues(50, 90, 4), [60, 70, 80, 90]);
  assert.deepEqual(tickValues(57, 47, 3), [54, 50, 47]);
  assert.equal(tickValues(10, 13, 7).at(-1), 13);
});

test('THE MASCOT GLANCE: never the same pose twice in a row', () => {
  for (const p of POSES) assert.notEqual(glance(p, p), p);
  assert.equal(glance('spirals', 'hearts'), 'hearts');
  assert.equal(landPose(o('emi3', 400)), 'jackpot');
  assert.equal(landPose(o('none', 0, { meltLeft: 2 })), 'melt');
  assert.equal(restPose(0), 'idle0_0');
  let prev = 'idle0_0';
  for (const want of ['spirals', 'spirals', 'hearts', 'hearts', 'idle0_0', 'idle0_0']) { const n = glance(prev, want); assert.notEqual(n, prev); prev = n; }
});

test('THE BREATH loops 0..1..0; THE SHIVER is 4 px, three cycles, then still', () => {
  assert.equal(breath(0), 0);
  assert.ok(Math.abs(breath(FEEL.BREATH_MS / 2) - 1) < 1e-9);
  const peak = Math.max(...Array.from({ length: 250 }, (_, i) => Math.abs(shiverPx(i))));
  assert.ok(peak <= 4 && peak > 2.5);
  assert.equal(shiverPx(FEEL.SHIVER_MS), 0);
});

test('No strobe over 6 Hz in the marquee chase; the thud curve overshoots', () => {
  for (let h = 0; h <= 4; h += 0.5) assert.ok(chaseMs(h) >= FEEL.STROBE_MIN_MS);
  const peak = Math.max(...Array.from({ length: 50 }, (_, i) => bezier(FEEL.THUD_EASE, i / 49)));
  assert.ok(peak > 1.05 && Math.abs(bezier(FEEL.THUD_EASE, 1) - 1) < 1e-6);
});

/* ---- The playbook's Tier A (CONTRACT 10.14): A1 the anticipation reel, A2 THE ALMOST on the strip.
 *      Both read a result the tape already carries, so every case below is a pure read. ---- */

// The mock server's table v5 strips (mock-server.js STRIPS), 13 cells a reel.
const STRIPS = [
  ['gif0', 'spiral0', 'sub0', 'gif1', 'emi', 'spiral1', 'gif2', 'sub1', 'spiral2', 'gif3', 'sub2', 'sub3', 'melt'],
  ['sub1', 'gif2', 'spiral1', 'melt', 'gif0', 'sub3', 'emi', 'spiral2', 'gif3', 'sub0', 'spiral0', 'gif1', 'sub2'],
  ['spiral2', 'gif3', 'sub2', 'gif1', 'spiral0', 'emi', 'sub0', 'gif0', 'melt', 'sub3', 'gif2', 'spiral1', 'sub1'],
];
const at = (r, k) => STRIPS[r][k];
/** An outcome the way the tape carries it: stops per reel, the symbols they land on. */
const out = (stops, line = 'none', extra = {}) =>
  ({ stops, symbols: stops.map((k, r) => at(r, k)), line, pay: line === 'none' ? 0 : 3, meltLeft: 0, ...extra });
/** reel 3's stop index for a symbol id. */
const k2 = id => STRIPS[2].indexOf(id);
const k0 = id => STRIPS[0].indexOf(id), k1 = id => STRIPS[1].indexOf(id);

test('A live pair is the same gif id, two spirals, two subs or two EMI, nothing else', () => {
  assert.equal(livePair(out([k0('gif1'), k1('gif1'), 0]), STRIPS), 'gif');
  assert.equal(livePair(out([k0('gif1'), k1('gif2'), 0]), STRIPS), 'none', 'two different gifs are not the live pair');
  assert.equal(livePair(out([k0('spiral0'), k1('spiral2'), 0]), STRIPS), 'spiral');
  assert.equal(livePair(out([k0('sub3'), k1('sub0'), 0]), STRIPS), 'sub');
  assert.equal(livePair(out([k0('emi'), k1('emi'), 0]), STRIPS), 'emi');
  assert.equal(livePair(out([k0('melt'), k1('melt'), 0]), STRIPS), 'none', 'melt is never a live pair');
  assert.equal(livePair(out([k0('spiral0'), k1('sub0'), 0]), STRIPS), 'none');
  assert.equal(livePair(null, STRIPS), 'none');
});

test('A1 holds reel 3 by tier: gif 900, spiral and sub 1,100, EMI 1,400 and gold', () => {
  const hold = o => anticipation(o, STRIPS);
  assert.deepEqual(hold(out([k0('gif1'), k1('gif1'), 0])), { kind: 'gif', holdMs: 900, gold: false });
  assert.deepEqual(hold(out([k0('spiral0'), k1('spiral2'), 0])), { kind: 'spiral', holdMs: 1100, gold: false });
  assert.deepEqual(hold(out([k0('sub3'), k1('sub0'), 0])), { kind: 'sub', holdMs: 1100, gold: false });
  assert.deepEqual(hold(out([k0('emi'), k1('emi'), 0])), { kind: 'emi', holdMs: 1400, gold: true });
  assert.deepEqual(hold(out([k0('gif1'), k1('gif2'), 0])), { kind: 'none', holdMs: 0, gold: false });
  assert.deepEqual(hold(null), { kind: 'none', holdMs: 0, gold: false });
});

test('A1 Brake 5: melted halves the hold and never goes gold; a frozen reel 3 never holds', () => {
  const melted = o => anticipation(o, STRIPS, { melted: true });
  assert.equal(melted(out([k0('gif1'), k1('gif1'), 0])).holdMs, 450);
  assert.equal(melted(out([k0('spiral0'), k1('spiral2'), 0])).holdMs, 550);
  assert.equal(melted(out([k0('emi'), k1('emi'), 0])).holdMs, 700);
  assert.equal(melted(out([k0('emi'), k1('emi'), 0])).gold, false, 'no gold while melted');
  assert.equal(anticipation(out([k0('emi'), k1('emi'), 0]), STRIPS, { held: 2 }).holdMs, 0);
  assert.equal(anticipation(out([k0('emi'), k1('emi'), 0]), STRIPS, { held: 0 }).holdMs, 1400, 'a frozen reel 1 still pairs');
});

test('A1 reads the strips when the outcome carries stops only', () => {
  const o = { stops: [k0('sub3'), k1('sub0'), 0], line: 'none', pay: 0 };
  assert.equal(anticipation(o, STRIPS).holdMs, 1100);
  assert.equal(anticipation(o, null).holdMs, 0, 'no strips, no guess');
});

test('A2 finds the off-by-one cell in both directions and around the ring', () => {
  // spiral pair, reel 3 on emi (cell 5): the cell below is spiral0 (cell 4).
  const below = almost(out([k0('spiral0'), k1('spiral2'), k2('emi')]), STRIPS);
  assert.deepEqual(below, { reel: 2, dir: -1, cell: k2('spiral0'), symbol: 'spiral0', kind: 'spiral' });
  // spiral pair, reel 3 on gif1 (cell 3): below is sub2, above is spiral0 (cell 4).
  const above = almost(out([k0('spiral0'), k1('spiral2'), k2('gif1')]), STRIPS);
  assert.deepEqual(above, { reel: 2, dir: 1, cell: k2('spiral0'), symbol: 'spiral0', kind: 'spiral' });
  // The strip is a ring: cell 0 and cell 12 are neighbours, both ways.
  const wrapDown = almost(out([k0('sub3'), k1('sub0'), 0]), STRIPS);          // cell 0 spiral2, below is sub1 (12)
  assert.deepEqual(wrapDown, { reel: 2, dir: -1, cell: 12, symbol: 'sub1', kind: 'sub' });
  // A ring wrap upward needs a reel 3 whose cell 11 does not fit and whose cell 0 does.
  const ring = [STRIPS[0], STRIPS[1], ['spiral0', 'melt', 'sub0', 'gif0', 'sub1', 'gif1', 'sub2', 'gif2', 'sub3', 'gif3', 'melt', 'melt', 'melt']];
  const wrapUp = almost({ stops: [k0('spiral0'), k1('spiral2'), 12], symbols: ['spiral0', 'spiral2', 'melt'], line: 'none', pay: 0 }, ring);
  assert.deepEqual(wrapUp, { reel: 2, dir: 1, cell: 0, symbol: 'spiral0', kind: 'spiral' });
  // EMI pair one cell short of the jackpot.
  const emi = almost(out([k0('emi'), k1('emi'), k2('spiral0')]), STRIPS);
  assert.deepEqual(emi, { reel: 2, dir: 1, cell: k2('emi'), symbol: 'emi', kind: 'emi' });
});

test('A2 on a gif pair wants the SAME gif, and fires at most once', () => {
  // gif1 pair, reel 3 on spiral0 (cell 4): below is gif1 (cell 3).
  const same = almost(out([k0('gif1'), k1('gif1'), k2('spiral0')]), STRIPS);
  assert.deepEqual(same, { reel: 2, dir: -1, cell: k2('gif1'), symbol: 'gif1', kind: 'gif' });
  // gif1 pair, reel 3 on sub2 (cell 2): neighbours are gif3 (1) and gif1 (3). Only gif1 completes it,
  // and the first fitting direction is the only tell: one gesture, one beat.
  const other = almost(out([k0('gif1'), k1('gif1'), k2('sub2')]), STRIPS);
  assert.deepEqual(other, { reel: 2, dir: 1, cell: k2('gif1'), symbol: 'gif1', kind: 'gif' });
  // gif0 pair, reel 3 on sub2 (cell 2): gif3 and gif1 are the neighbours, neither is gif0.
  assert.equal(almost(out([k0('gif0'), k1('gif0'), k2('sub2')]), STRIPS), null);
});

test('A2 stays quiet without a live pair, on a paying line, on a frozen reel 3 and without strips', () => {
  assert.equal(almost(out([k0('spiral0'), k1('sub0'), k2('spiral2')]), STRIPS), null, 'no pair, no tell');
  assert.equal(almost(out([k0('spiral0'), k1('spiral2'), k2('emi')], 'spiral3'), STRIPS), null, 'a pay is not an almost');
  assert.equal(almost(out([k0('spiral0'), k1('spiral2'), k2('emi')]), STRIPS, { held: 2 }), null);
  assert.equal(almost(out([k0('spiral0'), k1('spiral2'), k2('emi')]), null), null);
  assert.equal(almost(null, STRIPS), null);
  // Neither neighbour fits: sub pair, reel 3 on gif1 (cell 3), neighbours sub2 (2)... which does fit.
  // So take the melt cell (8): neighbours gif0 (7) and sub3 (9); a gif pair finds neither.
  assert.equal(almost(out([k0('gif2'), k1('gif2'), 8]), STRIPS), null);
});

test('A2 tell fits the House Book: 620-1,400 ms, one 120 ms snap back', () => {
  assert.ok(ALMOST.TELL_MS >= 620 && ALMOST.TELL_MS <= 1400);
  assert.equal(ALMOST.SNAP_MS, 120);
  assert.ok(ALMOST.SNAP_MS < ALMOST.TELL_MS);
});

