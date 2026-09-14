import test from 'node:test';
import assert from 'node:assert/strict';
import { FEEL, tierOf, recipe, ladderSemis, winTokens, spendTokens, tickValues, glance, landPose, restPose,
         glanceHoldMs, breath, shiverPx, chaseMs, bezier, POSES, livePair, anticipation, almost, ALMOST,
         ROLLUP_MS, rollupMs, rollupAt, rollupTicks, bankFlightMs, bankLandMs, ladderPlan, paylinePulses, paylineGlow, PAYLINE_PULSE_MIN_MS,
         ATTRACT, WIGGLE, attractOk, attractCells, emiLandings, wiggleCells,
         RESPIN_KIND, isHold, playsWithoutPress, respinKeep, respinHold, spiralReels, jarPlan, jarParty, JAR_TIER,
         COMP_ID, compOffer, compAvailable } from '../feel.js';
import { ANTICIPATION, PACE, respinStopMs, respinMs, outcomeMs, reelsMs } from '../pace.js';

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

/* ---- The playbook's Tier A (CONTRACT 10.15): A1 the anticipation reel, A2 THE ALMOST on the strip.
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

/* ---- playbook A3: THE BANK's proportional rollup, and the ladder that climbs with it ---- */

test('A3 rollup: one frozen table, 500 ms at tier 1 up to the 6 s jackpot climb', () => {
  assert.ok(Object.isFrozen(ROLLUP_MS));
  assert.deepEqual([...ROLLUP_MS], [0, 500, 1200, 2000, 6000]);
  assert.deepEqual([0, 1, 2, 3, 4].map(t => rollupMs(t)), [0, 500, 1200, 2000, 6000]);
  for (let t = 1; t <= 4; t++) assert.ok(rollupMs(t) > rollupMs(t - 1), `tier ${t} must roll longer than ${t - 1}`);
  assert.equal(rollupMs(1), 500, 'tier 1 keeps the flat House Book count-up');
});

test('A3 rollup: an outcome or a raw pay reads through the same tiers', () => {
  assert.equal(rollupMs({ line: 'emi3', pay: 400 }), 6000);
  assert.equal(rollupMs({ line: 'gif3same', pay: 40 }), 2000);
  assert.equal(rollupMs({ line: 'none', pay: 0 }), 0);
  assert.equal(rollupMs(40), 2000); assert.equal(rollupMs(12), 1200); assert.equal(rollupMs(5), 500);
  assert.equal(rollupMs(null), 0); assert.equal(rollupMs('nonsense'), 0);
});

test('A3: the tokens never move, only the count-up runs past them', () => {
  assert.equal(bankFlightMs(1), FEEL.BANK_FLY_MS);
  assert.equal(bankFlightMs(7), FEEL.BANK_FLY_MS + 6 * FEEL.BANK_STAGGER_MS);
  assert.equal(bankLandMs(0), FEEL.BANK_FLY_MS);
  assert.equal(bankLandMs(3), FEEL.BANK_FLY_MS + 3 * FEEL.BANK_STAGGER_MS);
  for (const tier of [1, 2, 3, 4]) {
    const n = winTokens(tier);
    assert.ok(n >= FEEL.BANK_MIN && n <= FEEL.BANK_MAX, 'the token count stays inside the House Book caps');
    assert.ok(winTokens(tier, true) <= FEEL.BANK_MAX_LITE, 'Calm still flies 4 at most');
  }
  // Only tiers 2 and up outlast their tokens; tier 1's flat count-up is already inside the flight.
  assert.ok(rollupMs(1) < bankFlightMs(winTokens(1)));
  for (const tier of [2, 3, 4]) assert.ok(rollupMs(tier) > bankFlightMs(winTokens(tier)));
});

test('A3: the count-up curve lands exactly on the settled value (Law I)', () => {
  assert.equal(rollupAt(50, 90, 0), 50);
  assert.equal(rollupAt(50, 90, 1), 90);
  assert.equal(rollupAt(50, 90, 2), 90);
  assert.equal(rollupAt(50, 90, -1), 50);
  let prev = -Infinity;
  for (let q = 0; q <= 1.0001; q += 0.05) { const v = rollupAt(50, 90, q); assert.ok(v >= prev); prev = v; }
});

test('A3: without a longer rollup the landings are the old even ladder, ending on `to`', () => {
  assert.deepEqual(rollupTicks(50, 90, 4, 0), tickValues(50, 90, 4));
  assert.deepEqual(rollupTicks(50, 90, 4, 100), tickValues(50, 90, 4));
  assert.deepEqual(rollupTicks(57, 47, 3, 0), tickValues(57, 47, 3));
  assert.equal(rollupTicks(10, 13, 7, 0).at(-1), 13);
});

test('A3: with a rollup the landings are the count at that moment and the tail finishes the job', () => {
  const n = winTokens(4), ms = rollupMs(4), ticks = rollupTicks(0, 400, n, ms);
  assert.equal(ticks.length, n);
  assert.ok(ticks.every((v, i) => i === 0 || v >= ticks[i - 1]), 'the readout never counts backwards');
  assert.ok(ticks[0] > 0, 'it has started counting by the first landing');
  assert.ok(ticks.at(-1) < 400, 'the tokens land well before the jackpot count settles');
  assert.equal(rollupAt(ticks.at(-1), 400, 1), 400, 'the tail still ends on the settled value');
  const two = rollupTicks(0, 15, winTokens(2), rollupMs(2));
  assert.ok(two.at(-1) < 15 && two.at(-1) > 0);
});

test('A3: THE CHIME LADDER spreads across the rollup, never over 6 Hz, never over the cap', () => {
  for (const tier of [1, 2, 3, 4]) {
    const plan = ladderPlan(tier, rollupMs(tier));
    assert.ok(plan.length >= 1 && plan.length <= FEEL.LADDER_CAP, `tier ${tier} plan length`);
    assert.equal(plan[0].at, 0); assert.equal(plan[0].semis, 0);
    assert.ok(plan.at(-1).at <= rollupMs(tier), 'the ladder finishes inside the rollup');
    for (let i = 1; i < plan.length; i++) {
      assert.ok(plan[i].at - plan[i - 1].at >= FEEL.STROBE_MIN_MS - 0.5, `tier ${tier} step ${i} under the strobe floor`);
      assert.equal(plan[i].semis, plan[i - 1].semis + 1, 'a semitone a step');
    }
  }
  assert.equal(ladderPlan(1, rollupMs(1)).length, 1, 'Law IX: a small win rings once');
  assert.ok(ladderPlan(4, rollupMs(4)).length > ladderPlan(2, rollupMs(2)).length, 'a bigger win climbs further');
  assert.equal(ladderPlan(4, rollupMs(4), true).length, 1, 'Brake 5: melted gets no climb');
  assert.equal(ladderPlan(4, 0).length, 1);
});

/* ---- playbook A6: the payline frame ---- */

test('A6: tier 1 gets one soft pulse, reduced motion and melt get a steady frame', () => {
  assert.equal(paylinePulses(1), 1);
  assert.equal(paylinePulses(0), 1);
  assert.equal(paylinePulses(4, { reduced: true }), 0);
  assert.equal(paylinePulses(4, { melted: true }), 0);
  assert.ok(paylinePulses(4) > paylinePulses(2), 'a bigger win pulses more over its longer rollup');
});

test('A6: the frame never pulses faster than 2 Hz', () => {
  for (const tier of [1, 2, 3, 4]) {
    const pulses = paylinePulses(tier), hold = Math.max(rollupMs(tier), 1);
    assert.ok(hold / pulses >= PAYLINE_PULSE_MIN_MS - 1e-9, `tier ${tier} pulse period under 500 ms`);
  }
  assert.ok(PAYLINE_PULSE_MIN_MS >= 500);
});

test('A6: the frame holds for the rollup, then THE GLOW goes out over 480 ms', () => {
  const hold = rollupMs(3), pulses = paylinePulses(3);
  assert.equal(paylineGlow(-1, hold, pulses), 0);
  assert.equal(paylineGlow(hold + FEEL.GLOW_OUT_MS, hold, pulses), 0);
  assert.ok(paylineGlow(hold + FEEL.GLOW_OUT_MS - 1, hold, pulses) > 0, 'it is still fading a frame before the end');
  for (let ms = 0; ms <= hold + FEEL.GLOW_OUT_MS; ms += 17) {
    const lit = paylineGlow(ms, hold, pulses);
    assert.ok(lit >= 0 && lit <= 1, `lit out of range at ${ms}`);
    if (ms < hold) assert.ok(lit > 0, 'Brake 9: the frame never goes dark before the fade');
  }
  assert.ok(Math.abs(paylineGlow(hold + FEEL.GLOW_OUT_MS / 2, hold, 0) - 0.5) < 1e-9, 'the fade is linear across 480 ms');
});

test('A6: a steady frame is steady, and a pulsed one breathes without going dark', () => {
  const hold = 1200;
  for (let ms = 0; ms <= hold; ms += 37) assert.equal(paylineGlow(ms, hold, 0), 1);
  const lit = Array.from({ length: 40 }, (_, i) => paylineGlow((i * hold) / 39, hold, 2));
  assert.ok(Math.max(...lit) > 0.98 && Math.min(...lit) >= 0.55);
});

/* ---- A4 attract mode --------------------------------------------------------------------------- */

const seated = (over = {}) => ({ seated: true, phase: 'idle', busy: false, banking: false, meltLeft: 0,
                                 calm: false, reduced: false, suspended: false, ...over });

test('A4 attract: about 25 s idle, a chase every ~8 s, a wink every ~12 s, and it eases home under the move cap', () => {
  assert.ok(ATTRACT.IDLE_MS >= 20000 && ATTRACT.IDLE_MS <= 30000, `idle ${ATTRACT.IDLE_MS}`);
  assert.ok(ATTRACT.CHASE_MS >= 7000 && ATTRACT.CHASE_MS <= 9000);
  assert.ok(ATTRACT.WINK_MS >= 11000 && ATTRACT.WINK_MS <= 13000);
  assert.ok(ATTRACT.CHASE_PASS_MS < ATTRACT.CHASE_MS && ATTRACT.WINK_FIRST_MS < ATTRACT.WINK_MS);
  assert.ok(ATTRACT.SETTLE_MS <= FEEL.MOVE_CAP_MS, 'leaving attract is utility motion, not a hero');
});

test('A4 attract: seated and idle only, and the whole of it is off while melted, on Calm and under reduced motion', () => {
  assert.equal(attractOk(seated()), true);
  assert.equal(attractOk(), false, 'nothing attracts before anyone sits down');
  assert.equal(attractOk(seated({ seated: false })), false);
  for (const phase of ['breath', 'spin', 'reveal']) assert.equal(attractOk(seated({ phase })), false, phase);
  assert.equal(attractOk(seated({ busy: true })), false, 'a tape playing is not idle');
  assert.equal(attractOk(seated({ banking: true })), false, 'a rollup running is not idle');
  assert.equal(attractOk(seated({ meltLeft: 1 })), false, 'Brake 5: no ceremonies while melted');
  assert.equal(attractOk(seated({ calm: true })), false);
  assert.equal(attractOk(seated({ reduced: true })), false);
  assert.equal(attractOk(seated({ suspended: true })), false);
});

test('A4 THE DRIFT is a continuous slow roll, never a landing', () => {
  assert.equal(attractCells(0), 0);
  assert.equal(attractCells(-500), 0);
  const a = attractCells(1000), b = attractCells(2000);
  assert.ok(b > a && Math.abs(b - 2 * a) < 1e-9, 'it keeps counting at one speed');
  assert.ok(a > 0.15 && a < 1, `a cell every ${(1 / a).toFixed(1)} s reads as a drift, not a spin`);
});

/* ---- A5 the EMI land-wiggle -------------------------------------------------------------------- */

const row = (symbols, line = 'none', pay = 0) => ({ symbols, line, pay, stops: [0, 0, 0], meltLeft: 0 });

test('A5 emiLandings: every reel showing EMI, a losing spin included', () => {
  assert.deepEqual(emiLandings(row(['emi', 'gif1', 'sub0'])), [0]);
  assert.deepEqual(emiLandings(row(['gif1', 'sub0', 'emi'])), [2]);
  assert.deepEqual(emiLandings(row(['emi', 'spiral0', 'emi'])), [0, 2]);
  assert.deepEqual(emiLandings(row(['spiral0', 'spiral1', 'gif2'], 'spiral2', 5)), []);
  assert.deepEqual(emiLandings(row(['emi', 'spiral0', 'spiral1'], 'spiral2', 5)), [0], 'it rides a paying spin too');
});

test('A5 emiLandings: the jackpot is already THE REVEAL, so that spin wiggles nothing (one hero per beat)', () => {
  assert.deepEqual(emiLandings(row(['emi', 'emi', 'emi'], 'emi3', 400)), []);
  assert.deepEqual(emiLandings(row(['emi', 'emi', 'emi'], 'emi3', 200)), [], 'halved by the melt it is still the jackpot');
  assert.deepEqual(emiLandings(null), []);
  assert.deepEqual(emiLandings({ line: 'none', pay: 0 }), []);
});

test('A5 the wiggle: two oscillations, about 300 ms, a fraction of a cell, ending exactly on the stop', () => {
  assert.ok(WIGGLE.MS <= FEEL.MOVE_CAP_MS && WIGGLE.MS >= 200 && WIGGLE.MS <= 400, `${WIGGLE.MS} ms`);
  assert.equal(WIGGLE.CYCLES, 2);
  assert.ok(WIGGLE.CELLS > 0 && WIGGLE.CELLS < 0.5, 'a nudge, never a cell');
  assert.equal(wiggleCells(0), 0);
  assert.equal(wiggleCells(WIGGLE.MS), 0, 'it comes back to the stop');
  assert.equal(wiggleCells(-1), 0);
  assert.equal(wiggleCells(WIGGLE.MS + 200), 0, 'and stays there');
  const peak = Math.max(...Array.from({ length: WIGGLE.MS }, (_, i) => Math.abs(wiggleCells(i))));
  assert.ok(peak <= WIGGLE.CELLS && peak > WIGGLE.CELLS * 0.5, `peak ${peak}`);
  const signs = Array.from({ length: WIGGLE.MS }, (_, i) => Math.sign(wiggleCells(i)));
  const flips = signs.filter((v, i) => i > 0 && v !== 0 && signs[i - 1] !== 0 && v !== signs[i - 1]).length;
  assert.equal(flips, 3, 'two oscillations either side of the stop');
});


/* ---------------------------------------------------------------------------------------------------
 * The playbook, Tier B and C (CONTRACT 10.16): B1 the spiral jar, B3 the welcome-back comp,
 * C1 the EMI pair free re-spin.
 * ------------------------------------------------------------------------------------------------ */

const spin = (symbols, extra = {}) => ({ kind: 'paid', symbols, line: 'none', pay: 0, meltLeft: 0, halved: false, ...extra });

test('B1 spiralReels: the spirals SHOWN, left to right, in the order their reels thud', () => {
  assert.deepEqual(spiralReels(spin(['spiral0', 'gif1', 'sub0'])), [0]);
  assert.deepEqual(spiralReels(spin(['gif1', 'spiral2', 'spiral1'])), [1, 2]);
  assert.deepEqual(spiralReels(spin(['spiral0', 'spiral1', 'spiral2'])), [0, 1, 2]);
  assert.deepEqual(spiralReels(spin(['gif0', 'sub1', 'emi'])), []);
  assert.deepEqual(spiralReels(null), []);
  assert.deepEqual(spiralReels({ line: 'none' }), [], 'an outcome with no symbols ticks nothing');
});

test('B1 jarPlan: one tick a spiral, and the last value is the tape\'s own jarN (Law I)', () => {
  const p = jarPlan(spin(['spiral0', 'gif1', 'spiral2'], { jarN: 19 }), 17, 100);
  assert.deepEqual(p.reels, [0, 2]);
  assert.deepEqual(p.values, [18, 19]);
  assert.equal(p.full, false);
  assert.equal(p.from, 17);
  assert.equal(p.to, 19);
  assert.equal(p.size, 100);
  // Law I: if the server disagrees with the page's arithmetic, the server wins.
  assert.deepEqual(jarPlan(spin(['spiral0', 'gif1', 'spiral2'], { jarN: 30 }), 17, 100).values, [18, 30]);
});

test('B1 jarPlan: a full jar drops by the size and keeps the remainder (24 + 2 fires at 25 and leaves 1)', () => {
  const p = jarPlan(spin(['spiral0', 'spiral1', 'gif2'], { jarN: 1 }), 24, 25);
  assert.equal(p.full, true, 'it spilled');
  assert.deepEqual(p.values, [0, 1], 'the tube wraps on the tick that fills it, and the remainder stays');
  assert.equal(p.to, 1);
  const at100 = jarPlan(spin(['spiral0', 'gif1', 'spiral2'], { jarN: 0 }), 98, 100);
  assert.equal(at100.full, true);
  assert.deepEqual(at100.values, [99, 0]);
  // Three spirals is at most 3 of 100, so one outcome can never fire the jar twice.
  assert.equal(jarPlan(spin(['spiral0', 'spiral1', 'spiral2'], { jarN: 2 }), 99, 100).values.length, 3);
});

test('B1 jarPlan: a freeze earns nothing, and neither does anything it expanded into', () => {
  const held = { kind: 'freeze', symbols: ['spiral0', 'spiral1', 'spiral2'], line: 'spiral3', pay: 10, jarN: 40 };
  assert.deepEqual(jarPlan(held, 40, 100).reels, [], 'a freeze never ticks the tube');
  assert.equal(jarPlan(held, 40, 100).to, 40, 'it carries back the jar it did not change');
  // A free spin a FREEZE won is sealed too, and the tape says so by carrying the same jarN back.
  const sealedFree = spin(['spiral0', 'spiral1', 'sub0'], { kind: 'free', jarN: 40 });
  assert.deepEqual(jarPlan(sealedFree, 40, 100).reels, []);
  // A free spin a PLAIN spiral3 won does earn, and ticks.
  assert.deepEqual(jarPlan(spin(['spiral0', 'spiral1', 'sub0'], { kind: 'free', jarN: 42 }), 40, 100).reels, [0, 1]);
});

test('B1 jarPlan: no jar in the table means no tube and no ticks', () => {
  const p = jarPlan(spin(['spiral0', 'spiral1', 'spiral2'], { jarN: 3 }), 0, 0);
  assert.equal(p.size, 0);
  assert.equal(p.full, false, 'nothing can fill a jar that is not published');
});

test('B1 jarParty: a full jar is tier 2, and Brake 2 drops its note inside a bigger win', () => {
  const r = jarParty({});
  assert.equal(r.tier, JAR_TIER);
  assert.equal(JAR_TIER, 2);
  assert.equal(r.sound, 'two', 'two notes and a jolt (Law IX)');
  assert.equal(r.jolt, true);
  assert.equal(r.chase, true);
  assert.equal(r.screen, true);
  assert.equal(r.tokens, false, 'the jar pays free SPINS, so THE BANK flies nothing');
  assert.equal(jarParty({ winTier: 2 }), null, 'the same outcome won tier 2: one hero per beat');
  assert.equal(jarParty({ winTier: 4 }), null);
  const over1 = jarParty({ winTier: 1 });
  assert.ok(over1, 'the jar is the bigger of the two, so it is the one that plays');
  assert.equal(over1.tier, 2);
  assert.equal(over1.sound, null, 'Brake 2: the same outcome also won, so the jar note is dropped');
  assert.equal(jarParty({}).sound, 'two', 'and on a no-pay spin it keeps its two notes');
});

test('B1 jarParty: Calm is the fill only, melted takes the melt party (Brake 5), Brake 3 shrinks it', () => {
  assert.equal(jarParty({ calm: true }), null, 'Calm: no party at all (fx.spiral_full still fires)');
  assert.equal(jarParty({ calm: true, winTier: 0 }), null);
  assert.equal(jarParty({ melted: true }).party, 'melt');
  assert.equal(jarParty({ melted: true }).heat, 1, 'no ceremonies while melted');
  assert.equal(jarParty({ seen: 0 }).party, 'fanfare');
  assert.equal(jarParty({ seen: FEEL.FANFARE_TIMES }).party, 'bead');
  assert.equal(jarParty({ seen: FEEL.THUD_ONLY_FROM }).party, 'thud');
});

test('C1 the emi2 landing: a muted thud and nothing else, no shiver and no ALMOST', () => {
  const pair = { line: 'emi2', pay: 0, meltLeft: 0, halved: false, symbols: ['emi', 'emi', 'melt'] };
  assert.equal(isHold(pair), true);
  assert.equal(isHold(o('none', 0)), false);
  const r = recipe(pair);
  assert.equal(r.tier, 0);
  assert.equal(r.party, 'hold');
  assert.equal(r.sound, 'muted', 'Brake 6: never silence');
  assert.equal(r.shiver, false, 'A2 is the release when the pair MISSES; here it has not missed yet');
  assert.equal(r.tokens, false);
  assert.equal(r.heat, 0);
  // and a plain no-pay spin still gets THE SHIVER, so the quiet party is the emi2's alone.
  assert.equal(recipe(o('none', 0)).party, 'shiver');
  assert.equal(recipe(o('none', 0)).shiver, true);
  // THE ALMOST never fires on it: almost() only reads `line === 'none'`.
  const strips = [['emi', 'gif0'], ['emi', 'gif0'], ['sub0', 'emi']];
  assert.equal(almost({ ...pair, stops: [0, 0, 0] }, strips), null);
});

test('C1 the re-spin plays itself: no second press, reels 1 and 2 stay', () => {
  assert.equal(RESPIN_KIND, 'emi_respin');
  assert.equal(playsWithoutPress('emi_respin'), true);
  for (const k of ['paid', 'free', 'respin', 'freeze', 'jar', null, undefined]) assert.equal(playsWithoutPress(k), false, String(k));
  assert.deepEqual(respinKeep('emi_respin'), [0, 1]);
  for (const k of ['paid', 'free', 'respin', 'freeze', 'jar']) assert.deepEqual(respinKeep(k), []);
});

test('C1 the re-spin hold: the FULL 1,400 ms EMI row, gold, never halved, not even while melted', () => {
  const h = respinHold();
  assert.equal(h.kind, 'emi');
  assert.equal(h.holdMs, ANTICIPATION.emi);
  assert.equal(h.holdMs, 1400);
  assert.equal(h.gold, true);
  assert.equal(h.respin, true);
  // A1's own EMI hold IS halved and never gold while melted (10.15, Brake 5). This beat is not A1.
  const pair = { line: 'emi2', pay: 0, symbols: ['emi', 'emi', 'sub0'], stops: [0, 0, 0] };
  const a1 = anticipation(pair, null, { melted: true });
  assert.equal(a1.holdMs, 700);
  assert.equal(a1.gold, false);
  assert.equal(respinHold().holdMs, 1400, 'the re-spin does not move');
});

test('C1 the pace: reel 3 alone takes no stagger, and outcomeMs knows the kind', () => {
  assert.equal(respinStopMs(), PACE.SPIN_MS + ANTICIPATION.emi);
  assert.equal(respinMs(), PACE.SPIN_MS + ANTICIPATION.emi + PACE.THUD_MS);
  assert.equal(outcomeMs(PACE, 0, 'emi_respin'), respinMs() + PACE.REVEAL_MS + PACE.BREATH_MS);
  assert.equal(outcomeMs(PACE, 0, 'paid'), reelsMs() + PACE.REVEAL_MS + PACE.BREATH_MS);
  assert.ok(respinStopMs() < reelsMs(PACE, ANTICIPATION.emi), 'one reel is quicker than three');
  assert.equal(outcomeMs(), 4020, 'a plain outcome is unchanged');
});

test('B3 compOffer: a stored comp, read defensively', () => {
  assert.deepEqual(compOffer({ id: 'c_abcd_1z', spins: 5 }), { id: 'c_abcd_1z', spins: 5 });
  assert.equal(compOffer(null), null);
  assert.equal(compOffer({ id: 'c_abcd_1z', spins: 0 }), null);
  assert.equal(compOffer({ id: 'nope', spins: 5 }), null, 'the id shape is the server\'s own');
  assert.equal(compOffer({ id: 'c_ab', spins: 5 }), null, 'too short');
  assert.equal(compOffer({ id: 'c_' + 'a'.repeat(49), spins: 5 }), null, 'too long');
  assert.ok(COMP_ID.test('c_' + 'a'.repeat(48)));
  assert.equal(compOffer('c_abcd_1z'), null, 'a bare string is not a comp');
});

test('B3 compAvailable: spendable once, and a spent one never comes back', () => {
  const c = { id: 'c_abcd_1z', spins: 5 };
  assert.equal(compAvailable(c), true);
  assert.equal(compAvailable(c, true), false, 'already spent this sit-down');
  assert.equal(compAvailable(null), false);
  assert.equal(compAvailable({ id: 'x', spins: 5 }), false);
});
