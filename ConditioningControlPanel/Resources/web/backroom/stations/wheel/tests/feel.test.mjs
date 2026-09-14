import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as feel from '../feel.js';
import { FEEL, tierOf, recipe, winTokens, tickValues, tick, glance, landPose, shiverPx, breath, POSES } from '../feel.js';
import { readResult } from '../wheel.js';

const R = (o) => readResult({ day: '2026-09-14', sliceId: 'x', sliceIndex: 1, pay: 0, snoozeCarryPaid: 0, jackpot: false, jackpotFallback: false, snoozed: false, ...o });

test('tiers by pay: Snooze 0, 1-3 small, 5-20 bigger, 40 and 100 big, the pot on top', () => {
  assert.equal(tierOf(R({ snoozed: true })), 0);
  assert.deepEqual([1, 2, 3].map(pay => tierOf(R({ pay }))), [1, 1, 1]);
  assert.deepEqual([5, 8, 12, 20].map(pay => tierOf(R({ pay }))), [2, 2, 2, 2]);
  assert.deepEqual([40, 100].map(pay => tierOf(R({ pay }))), [3, 3]);
  assert.equal(tierOf(R({ pay: 100, jackpotFallback: true })), 3, 'a gated pot hit pays Dazed, a big win, not the reveal');
  assert.equal(tierOf(R({ pay: 550, jackpot: true })), 4);
  assert.equal(tierOf(null), 0);
});

test('the old size-based fx table is gone: moments decide the fullscreen (CONTRACT 10.13.F)', () => {
  for (const name of ['FX_BY_TIER', 'fxFor', 'usesGifs']) assert.equal(name in feel, false, name);
});

test('recipe: celebrate small on purpose, Snooze sleepy with a shiver, the reveal only for the pot', () => {
  const snooze = recipe(R({ snoozed: true }));
  assert.equal(snooze.sound, 'snooze'); assert.ok(snooze.sleepy && snooze.shiver && !snooze.tokens);
  const small = recipe(R({ pay: 2 }));
  assert.equal(small.sound, 'chime'); assert.ok(!small.jolt && !small.reveal && !small.gold && small.tokens);
  assert.equal(recipe(R({ pay: 8 })).sound, 'two');
  assert.equal(recipe(R({ pay: 40 })).sound, 'thud');
  const pot = recipe(R({ pay: 550, jackpot: true }));
  assert.ok(pot.reveal && pot.gold && pot.sparks && pot.sound === 'reveal');
  for (let tier = 0; tier < 4; tier++) assert.ok(FEEL.PARTY_MS[tier] <= 1000, 'no party over a second but the declared hero');
});

test('Law VI: reduced motion and Calm take the state (no shiver, no reveal travel), the cue stays', () => {
  const s = recipe(R({ snoozed: true }), { still: true });
  assert.ok(!s.shiver && s.sleepy && s.sound === 'snooze');
  const p = recipe(R({ pay: 550, jackpot: true }), { still: true });
  assert.ok(!p.reveal && !p.sparks && p.gold && p.sound === 'reveal');
});

test('THE BANK token counts and ticks', () => {
  assert.deepEqual([1, 2, 3, 4].map(t => winTokens(t, false)), [3, 4, 5, 7]);
  assert.deepEqual([1, 2, 3, 4].map(t => winTokens(t, true)), [3, 4, 4, 4], 'Calm (lite) flies 4 at most, the pot too');
  assert.deepEqual(tickValues(57, 62, 4), [58, 60, 61, 62]);
  assert.equal(tickValues(57, 607, 7).at(-1), 607);
});

test('CHIME LADDER: never more than 6 played ticks a second, climbs only as the wheel slows, capped at 7', () => {
  let ladder = null, played = [];
  for (let t = 0; t < 3000; t += 30) {   // fast crossings every 30 ms
    const r = tick(ladder, t, 30); ladder = r.ladder;
    if (r.play) { played.push(t); assert.equal(r.semis, 0, 'no climb while fast'); }
  }
  for (let i = 1; i < played.length; i++) assert.ok(played[i] - played[i - 1] >= FEEL.STROBE_MIN_MS);
  assert.ok(played.length <= 3000 / FEEL.STROBE_MIN_MS + 1);
  const steps = [];
  for (let i = 0, t = 10000; i < 12; i++, t += 400) { const r = tick(ladder, t, 400); ladder = r.ladder; steps.push(r.semis); }
  assert.deepEqual(steps, [1, 2, 3, 4, 5, 6, 7, 7, 7, 7, 7, 7]);
});

test('THE MASCOT GLANCE never repeats a pose, and lands on the right face', () => {
  for (const p of POSES) assert.notEqual(glance(p, p), p);
  assert.equal(landPose(R({ snoozed: true })), 'melt');
  assert.equal(landPose(R({ pay: 5 })), 'hearts');
  assert.equal(landPose(R({ pay: 550, jackpot: true })), 'jackpot');
});

test('THE SHIVER and THE BREATH stay in their bounds', () => {
  for (let ms = 0; ms < 300; ms += 5) assert.ok(Math.abs(shiverPx(ms)) <= FEEL.SHIVER_PX);
  assert.equal(shiverPx(FEEL.SHIVER_MS), 0);
  assert.equal(breath(0), 0);
  assert.ok(Math.abs(breath(FEEL.BREATH_MS / 2) - 1) < 1e-9);
  assert.ok(FEEL.BREATH_MS >= 2600 && FEEL.BREATH_MS <= 4000);
});
