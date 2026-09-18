import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TAU, layoutOf, kindOf, sliceAt, landingAngle, resultIndex, readResult, landingRotation, restRotation,
         planLanding, rotationAt, settleMs, SETTLE_EPS_RAD, countdown, shownSp } from '../wheel.js';

// Server TABLE_V2 order and drawn widths (degrees), as GET state sends them.
const WIDTHS = [['jackpot', 7.2], ['sip_a', 31.3], ['glow', 42], ['sparkle_a', 44], ['dreamy', 8], ['sip_b', 31.3], ['shimmer', 25],
  ['snooze', 21.6], ['twinkle', 52], ['deep', 5], ['sparkle_b', 44], ['dazzle', 14.4], ['sip_c', 31.2], ['dazed', 3]];
const SLICES = WIDTHS.map(([id, width]) => ({ id, label: id, pay: id === 'jackpot' ? 475 : 1, width, odds: '1 in 10' }));
const L = layoutOf(SLICES);
const mod = a => ((a % TAU) + TAU) % TAU;
const inside = (s, r) => sliceAt(L, r) === s;

test('layout covers the circle, slice 0 centred on the pointer, kinds by id', () => {
  assert.equal(L.length, 14);
  assert.ok(Math.abs(L.reduce((s, x) => s + x.span, 0) - TAU) < 1e-9);
  assert.ok(Math.abs(L[0].mid) < 1e-12);
  for (let i = 1; i < L.length; i++) assert.ok(Math.abs(L[i].start - L[i - 1].end) < 1e-12);
  assert.equal(L.find(s => s.id === 'jackpot').kind, 'jackpot');
  assert.equal(L.find(s => s.id === 'snooze').kind, 'malus');
  assert.equal(kindOf({ id: 'glow' }), 'prize');
  assert.equal(sliceAt(L, 0).id, 'jackpot');
});

test('layouts the model cannot draw are refused, not guessed', () => {
  assert.equal(layoutOf(null), null);
  assert.equal(layoutOf(SLICES.slice(0, 2)), null);
  assert.equal(layoutOf([...SLICES.slice(0, 13), { id: 'x', width: 0 }]), null);
  assert.equal(layoutOf([{ id: 'a', width: 1 }, { id: 'b', width: 1 }, { id: 'c', width: 1000 }]), null);   // under 1 degree
});

test('every sliceIndex lands inside its drawn slice, from any start, either direction, any drag strength', () => {
  for (const s of L) {
    for (const day of ['2026-09-14', '2026-09-15', '2027-01-01']) {
      const land = landingAngle(L, s.index, day);
      assert.ok(land > s.start && land < s.end, `${s.id} ${day}`);
      assert.ok(Math.abs(land - s.mid) <= s.span * 0.3 + 1e-12, 'the middle 60 percent');
      for (const from of [0, 1.3, -7.7, 40.2]) {
        for (const omega of [0.004, -0.004, 0.03, -0.03, 0.0001]) {
          const plan = planLanding({ from, omega, landing: land });
          assert.ok(inside(s, plan.to), `${s.id} from ${from} omega ${omega}`);
          assert.equal(Math.sign(plan.to - plan.from), Math.sign(omega), 'turns the way it was flung');
          assert.ok(Math.abs(plan.to - plan.from) >= TAU, 'at least one whole turn');
          assert.ok(plan.ms >= 3200 && plan.ms <= 5600);
          assert.equal(rotationAt(plan, 0), from);
          assert.equal(rotationAt(plan, plan.ms), plan.to);
          assert.equal(rotationAt(plan, plan.ms * 3), plan.to);
        }
      }
    }
  }
});

test('THE SETTLE ends the landing when the picture is still, not when the clock runs out', () => {
  for (const [omega, landing] of [[0.01, 2], [-0.012, 4], [0.008, 0.3], [-0.005, 5.5]]) {
    const plan = planLanding({ from: 0, omega, landing });
    const at = settleMs(plan);
    assert.ok(at > 0 && at < plan.ms, `settle ${at} of ${plan.ms}`);
    // What is left to travel at the settle is the snap, and it is under the eye's threshold at the rim.
    const left = Math.abs(plan.to - rotationAt(plan, at));
    assert.ok(left <= SETTLE_EPS_RAD * 1.0001, `left ${left} rad`);
    // The trimmed tail is real: a tenth of the plan clock or more, which the warp stretches into seconds.
    assert.ok(plan.ms - at > plan.ms * 0.1, `tail ${plan.ms - at} ms`);
  }
  // A nudge shorter than the epsilon has no dead tail to trim, and a broken plan asks for no time at all.
  assert.equal(settleMs({ from: 0, to: 0.001, ms: 700 }), 700);
  assert.equal(settleMs({ from: 0, to: 9, ms: 0 }), 0);
  assert.equal(settleMs(null), 0);
});

test('the plan starts at the coast speed when the duration is not clamped', () => {
  for (const [omega, landing] of [[0.01, 2], [-0.012, 4], [0.008, 0.3]]) {
    const plan = planLanding({ from: 0, omega, landing });
    assert.ok(plan.ms > 3200 && plan.ms < 5600, `unclamped ${plan.ms}`);
    const v0 = rotationAt(plan, 0.01) / 0.01;
    assert.ok(Math.abs(v0 - omega) / Math.abs(omega) < 0.01, `v0 ${v0} omega ${omega}`);
  }
});

test('landingRotation keeps the direction and the residue', () => {
  const r = landingRotation(1, 0.5, -1, 3);
  assert.ok(r < 1 - 3 * TAU);
  assert.ok(Math.abs(mod(r) - 0.5) < 1e-9);
  assert.ok(Math.abs(mod(landingRotation(-2, 0.5, 1, 0)) - 0.5) < 1e-9);
});

test('a stored result replays the same landing every reopen, in its slice', () => {
  const stored = { day: '2026-09-14', sliceId: 'twinkle', sliceIndex: 8, pay: 3, snoozeCarryPaid: 0, jackpot: false, total: 3 };
  const a = restRotation(L, stored, stored.day), b = restRotation(L, stored, stored.day);
  assert.equal(a, b);
  assert.equal(sliceAt(L, a).id, 'twinkle');
  assert.ok(a >= 0 && a < TAU);
  assert.equal(resultIndex(L, { sliceId: 'deep', sliceIndex: 3 }), 9, 'an index that disagrees with the id loses to the id');
  assert.equal(resultIndex(L, { sliceId: 'nope', sliceIndex: 99 }), -1);
  assert.equal(restRotation(L, { sliceId: 'nope' }, 'x'), null);
});

test('readResult reads the binding shape, and total defaults to pay plus carry', () => {
  const r = readResult({ day: '2026-09-14', sliceId: 'jackpot', sliceIndex: 0, pay: 475, snoozeCarryPaid: 2, jackpot: true,
                         jackpotFallback: false, snoozed: false, total: 477, capped: true });
  assert.deepEqual(r, { day: '2026-09-14', sliceId: 'jackpot', sliceIndex: 0, pay: 475, carryPaid: 2, total: 477,
                        jackpotWon: true, fallback: false, snoozed: false, capped: true });
  assert.equal(readResult({ pay: 5, snoozeCarryPaid: 4 }).total, 9);
  assert.equal(readResult({ jackpot: 475 }).jackpotWon, false, 'only a boolean true is a won pot');
  assert.equal(readResult(null), null);
});

test('countdown to nextResetAt', () => {
  const at = '2026-09-15T00:00:00.000Z', base = Date.parse(at);
  assert.deepEqual(countdown(at, base - (5 * 3600 + 12 * 60 + 33) * 1000), { ms: 18753000, due: false, text: '05:12:33' });
  assert.equal(countdown(at, base - 400).text, '00:00:01', 'rounds up, never shows 00:00:00 early');
  assert.deepEqual(countdown(at, base + 5000), { ms: 0, due: true, text: '00:00:00' });
  assert.equal(countdown(at, base - 30 * 3600 * 1000).text, '30:00:00');
  assert.equal(countdown('not a date', base), null);
});

test('Law I: the readout is never ahead of the server', () => {
  assert.equal(shownSp(60, 5), 55, 'a pay not landed yet is held back');
  assert.equal(shownSp(60, 0), 60);
  assert.equal(shownSp(3, 10), 0);
  assert.equal(shownSp(60, 5, 58), 58, 'a bank tick shows as is');
  assert.equal(shownSp(60, 5, 99), 60, 'but never above the server');
  assert.equal(shownSp('x', 'y'), 0);
});
