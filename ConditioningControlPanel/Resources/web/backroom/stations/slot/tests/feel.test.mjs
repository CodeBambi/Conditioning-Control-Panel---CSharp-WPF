import test from 'node:test';
import assert from 'node:assert/strict';
import { FEEL, tierOf, recipe, ladderSemis, winTokens, spendTokens, tickValues, glance, landPose, restPose,
         glanceHoldMs, breath, shiverPx, chaseMs, bezier, POSES } from '../feel.js';

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
