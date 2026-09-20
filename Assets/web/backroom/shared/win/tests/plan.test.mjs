import test from 'node:test';
import assert from 'node:assert/strict';
import { PARTY, POSE_BY_TIER, MELT_POSE, winPlan, mergePlans, freshSit, sitPlan, afterParty } from '../plan.js';
import { BANK } from '../bank.js';
import { LADDER } from '../ladder.js';

const at = (tier, ctx) => winPlan(tier, ctx || {});
const FIELDS = ['tier', 'spent', 'bank', 'shower', 'ladder', 'octave', 'sparkle', 'reveal', 'glow', 'emi', 'partyMs', 'why'];

test('the plan is frozen, complete and the same shape at every rung', () => {
  for (const t of [0, 1, 2, 3, 4]) {
    const p = at(t);
    assert.ok(Object.isFrozen(p), `tier ${t} not frozen`);
    assert.deepEqual(Object.keys(p).sort(), FIELDS.slice().sort(), `tier ${t} fields`);
    assert.equal(p.tier, t);
  }
  assert.ok(Object.isFrozen(PARTY) && Object.isFrozen(PARTY.MS) && Object.isFrozen(POSE_BY_TIER));
});

test('Law IX: the party grows with the win, and never shrinks going up a rung', () => {
  const p = [0, 1, 2, 3, 4].map(t => at(t));
  for (let i = 1; i < p.length; i++) {
    assert.ok(p[i].partyMs >= p[i - 1].partyMs, `partyMs fell at ${i}`);
    assert.ok(p[i].bank >= p[i - 1].bank, `bank fell at ${i}`);
    assert.ok(p[i].ladder >= p[i - 1].ladder, `ladder fell at ${i}`);
    assert.ok(p[i].shower >= p[i - 1].shower, `shower fell at ${i}`);
    assert.ok(p[i].sparkle >= p[i - 1].sparkle, `sparkle fell at ${i}`);
  }
  assert.deepEqual(p.map(x => x.partyMs), [...PARTY.MS]);
});

test('Law IX: a small win NEVER gets confetti - no sparkle, no shower, no reveal', () => {
  for (const t of [1, 2]) {
    const p = at(t);
    assert.equal(p.sparkle, 0, `tier ${t} sparkled`);
    assert.equal(p.reveal, false, `tier ${t} revealed`);
  }
  assert.equal(at(1).shower, 0, 'a tier 1 showed from across the room');
  assert.ok(at(2).shower > 0, 'a tier 2 is the first rung the room sees');
  assert.equal(at(3).sparkle, 7);
  assert.equal(at(4).sparkle, 9);
  for (const t of [3, 4]) assert.ok(at(t).sparkle >= 5 && at(t).sparkle <= 9, 'sparkBurst clamps 5..9');
});

test('tier 0 is not a win: nothing is spent and the failure recipe owns the beat', () => {
  const p = at(0);
  assert.deepEqual([p.spent, p.bank, p.shower, p.ladder, p.sparkle, p.glow, p.partyMs], [0, 0, 0, 0, 0, 0, 0]);
  assert.equal(p.reveal, false);
  assert.equal(p.why, 'none');
  assert.equal(at(0, { melted: true }).emi, MELT_POSE);
});

test('Law XII: the value moves at every rung and every motion level but reduced', () => {
  for (const t of [1, 2, 3, 4]) {
    for (const ctx of [{}, { still: true }, { lite: true }, { melted: true }, { seen: 99 }]) {
      assert.ok(at(t, ctx).bank >= BANK.MIN, `tier ${t} lost its bank under ${JSON.stringify(ctx)}`);
    }
  }
  assert.equal(at(4, { reduced: true }).bank, 0, 'reduced motion takes the state, not a faster flight');
});

test('Law VI: reduced motion is the settled state - no travel, no wait, the cue still plays', () => {
  const p = at(4, { reduced: true });
  assert.deepEqual([p.bank, p.shower, p.sparkle, p.glow, p.partyMs], [0, 0, 0, 0, 0]);
  assert.equal(p.reveal, false);
  assert.ok(p.ladder > 1, 'the chime ladder survives reduced motion (Brake 9)');
  assert.equal(p.why, 'reduced');
  assert.equal(at(2, { reduced: true }).why, 'reduced');
});

test('Calm is not reduced motion: the decoration goes, the bank stays', () => {
  const p = at(4, { still: true });
  assert.equal(p.bank, BANK.MAX);
  assert.deepEqual([p.shower, p.sparkle, p.reveal], [0, 0, false]);
  assert.equal(p.partyMs, PARTY.MS[4], 'Calm still holds the beat - the readout is counting');
  assert.equal(p.glow, PARTY.GLOW_MS, 'a warm cut is not travel');
});

test('Brake 8: a lite board takes four tokens and no particles, and keeps the sound', () => {
  for (const t of [1, 2, 3, 4]) {
    const p = at(t, { lite: true });
    assert.ok(p.bank <= BANK.MAX_LITE, `tier ${t} flew ${p.bank} tokens on lite`);
    assert.equal(p.sparkle, 0, `tier ${t} sparkled on lite`);
    assert.ok(p.shower <= 2, `tier ${t} showered ${p.shower} on lite`);
    assert.equal(p.ladder, at(t).ladder, 'the sound still carries every beat');
  }
});

test('Brake 5: melted quiets it to one rung, drops the ladder an octave, keeps the value moving', () => {
  const p = at(4, { melted: true });
  assert.equal(p.spent, 1);
  assert.equal(p.why, 'melted');
  assert.equal(p.octave, LADDER.OCTAVE);
  assert.equal(p.ladder, 1);
  assert.deepEqual([p.shower, p.sparkle, p.glow, p.reveal], [0, 0, 0, false]);
  assert.ok(p.bank >= BANK.MIN, 'a melted win is quiet, not invisible');
  assert.equal(p.emi, MELT_POSE, 'Law XIII: she still reacts');
});

test('Brake 3: the first three get the fanfare, then a rung down, then the fortieth is a thud', () => {
  const seen = s => at(3, { seen: s });
  assert.deepEqual([0, 1, 2].map(s => seen(s).spent), [3, 3, 3]);
  assert.deepEqual([0, 1, 2].map(s => seen(s).why), [null, null, null]);
  assert.equal(seen(3).spent, 2);
  assert.equal(seen(3).why, 'repeat');
  assert.equal(seen(38).spent, 2);
  assert.equal(seen(39).spent, 1);
  assert.equal(seen(39).why, 'thud');
  assert.ok(seen(39).bank >= BANK.MIN, 'the fortieth is a thud AND the tokens');
  assert.equal(at(1, { seen: 99 }).spent, 1, 'it never shrinks below the chime');
});

test('Law IX: the hero plays once a sit-down, and the second jackpot is a very good big win', () => {
  const first = at(4, { heroesThisSit: 0 });
  assert.equal(first.reveal, true);
  assert.equal(first.why, null);
  const second = at(4, { heroesThisSit: 1 });
  assert.equal(second.spent, 3);
  assert.equal(second.reveal, false);
  assert.equal(second.why, 'capped');
  assert.ok(second.bank >= BANK.MIN && second.shower > 0, 'still a party, just not THE REVEAL');
  assert.equal(at(4, { heroesThisSit: 9, seen: 99 }).spent, 1, 'a capped hero is worn down like any other rung');
});

test('Law XIII: every plan names a pose', () => {
  for (const t of [0, 1, 2, 3, 4]) assert.ok(typeof at(t).emi === 'string' && at(t).emi.length, `tier ${t}`);
  assert.equal(at(4).emi, 'jackpot');
  assert.equal(at(1).emi, 'hearts');
  assert.equal(at(3).emi, 'spirals');
});

test('Brake 2: overlapping parties merge into the higher one, they never stack', () => {
  const small = at(1), big = at(3);
  assert.equal(mergePlans(small, big), big);
  assert.equal(mergePlans(big, small), big);
  assert.equal(mergePlans(big, at(3, { seen: 0 })).spent, 3, 'a tie keeps the first, it does not add');
  assert.equal(mergePlans(null, big), big);
  assert.equal(mergePlans(big, null), big);
  assert.equal(mergePlans(null, null), null);
  assert.ok(mergePlans(small, big).partyMs <= PARTY.MS[4], 'no merge ever costs more than the hero');
});

test('THE SIT-DOWN LEDGER counts the rung that was asked for, and only real parties', () => {
  let sit = freshSit();
  assert.ok(Object.isFrozen(sit) && Object.isFrozen(sit.seen));
  for (let i = 0; i < 3; i++) { assert.equal(sitPlan(3, sit).spent, 3, `win ${i}`); sit = afterParty(sit, sitPlan(3, sit)); }
  assert.equal(sit.seen[3], 3);
  assert.equal(sitPlan(3, sit).spent, 2, 'the fourth big win is a rung down');
  assert.equal(sit.heroes, 0);
  assert.equal(afterParty(sit, at(0)), sit, 'a no-win never counts toward Brake 3');
  assert.equal(afterParty(sit, at(2, { seen: 0 })).seen[2], 1, 'the rungs are counted apart');
});

test('THE SIT-DOWN LEDGER: the hero is counted only when the REVEAL actually played', () => {
  let sit = freshSit();
  const hero = sitPlan(4, sit);
  assert.equal(hero.reveal, true);
  sit = afterParty(sit, hero);
  assert.equal(sit.heroes, 1);
  assert.equal(sitPlan(4, sit).reveal, false, 'once a sit-down');
  const calm = afterParty(freshSit(), winPlan(4, { still: true }));
  assert.equal(calm.heroes, 0, 'a reveal that never played does not spend the sit-down hero');
  assert.equal(calm.seen[4], 1, 'but it was still a tier 4 party');
});

test('nonsense in, a settled plan out - winPlan never throws', () => {
  for (const junk of [undefined, null, NaN, 'hero', -3, 99]) {
    const p = winPlan(junk, junk);
    assert.ok(Object.isFrozen(p));
    assert.ok(p.spent >= 0 && p.spent <= 4);
  }
  assert.equal(winPlan(99).spent, 4);
  assert.equal(winPlan('hero').spent, 0);
});
