// node --test backroom/stations/roulette/tests/ : THE REWARD (CONTRACT 10.22) - the rung a landing is worth,
// the party the house allows it, the ladder's root, the melt pocket, the chips the tokens leave from and the
// hold that lets a party finish. Every restraint below is shared/win/plan.js's; this holds the roulette's own
// half of it and the seam between the two.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  FEEL, REWARD, CALLOUTS, landBeat, nextLaunchAt, rewardTier, winSound, ladderRoot, meltedBy, tokenSpots,
} from '../feel.js';
import { glyphFor } from '../glyphs.js';
import { freshSit, sitPlan, afterParty, winPlan } from '../../../shared/win/plan.js';
import { TIER, tierFromPay } from '../../../shared/win/tier.js';
import { LADDER, ladderPlan } from '../../../shared/win/ladder.js';
import { createBankRun, bankFlightMs } from '../../../shared/win/bank.js';
import { FX_DELAY_MS } from '../../../shared/hypno/callout.js';

/** A settled read (tape.readOutcome) for the rungs this table can actually reach. */
const read = (o = {}) => ({ i: 0, pocket: 17, color: 'rose', row: 'sink', wake: false, straight: false, pay: 0, hits: [], index: 5, ...o });
const MISS = read();
const SMALL = read({ pay: 2, hits: ['rose'] });                                  // a colour chip, one SP
const GOOD = read({ pay: 18, hits: ['rose', 'sink'] });                          // the biggest outside win on this table
const STRAIGHT = read({ pay: 36, straight: true, hits: ['s17'] });
const WAKE = read({ pay: 12, wake: true, hits: ['sink'] });
const FULL = read({ pay: 216, wake: true, straight: true, hits: ['s17'] });      // 3 SP straight up on a Spiral Wake

test('the rung a landing is worth: a miss is 0, a near miss is 0, the callout size raised by the pay', () => {
  assert.equal(rewardTier(MISS), TIER.NONE);
  assert.equal(rewardTier(MISS, [16]), TIER.NONE, 'a near miss is never a small win');
  assert.equal(rewardTier(read({ pay: 0, wake: true })), TIER.NONE, 'a woken miss is still a miss');
  assert.equal(rewardTier(SMALL), TIER.SMALL);
  assert.equal(rewardTier(GOOD), TIER.GOOD, 'pay 18 clears the 10 step, so an outside win reaches the bare 2');
  assert.equal(rewardTier(STRAIGHT), TIER.BIG);
  assert.equal(rewardTier(WAKE), TIER.BIG);
  assert.equal(rewardTier(FULL), TIER.HERO);
  assert.equal(rewardTier(null), TIER.NONE);
  assert.equal(rewardTier(undefined, null), TIER.NONE, 'never throws on junk');
});

test('Law IX: the pay only ever RAISES a landing, it never talks one down', () => {
  // A straight-up hit for one SP pays 36: the callout already calls it big, and 36 is only a tierFromPay 2.
  assert.equal(tierFromPay(36), TIER.GOOD);
  assert.equal(rewardTier(read({ pay: 36, straight: true, hits: ['s17'] })), TIER.BIG, 'the beat wins, not the pay');
  // And the hero is the hero whatever it paid: a Full Wake on a one-SP chip is still the top rung.
  assert.equal(rewardTier(read({ pay: 72, wake: true, straight: true, hits: ['s17'] })), TIER.HERO);
});

test('the rungs ARE the callout sizes: this table never invents a bare 2 of its own', () => {
  const SIZE = { small: TIER.SMALL, big: TIER.BIG, hero: TIER.HERO };
  for (const [beat, co] of Object.entries(CALLOUTS)) {
    if (beat === 'streak') continue;
    const r = beat === 'land.full' ? FULL : beat === 'land.straight' ? STRAIGHT : beat === 'land.wake' ? WAKE : SMALL;
    assert.equal(landBeat(r), beat, beat + ' is the beat these fields make');
    // The pay of each fixture is under its beat's own step, so what comes back is the callout's size exactly.
    assert.equal(rewardTier(r), SIZE[co.tier], beat + ' pays the rung its callout is sized at');
  }
  assert.ok(!Object.values(CALLOUTS).some((c) => c.tier === 'mid'), 'no callout is a 2, so nothing but a pay can reach one');
});

test('the win voice is the SPENT rung, and every rung names a voice the kit has', () => {
  assert.deepEqual([...REWARD.WIN_SOUND], ['settle', 'small', 'mid', 'big', 'hero']);
  assert.equal(winSound(0), 'settle');
  assert.equal(winSound(4), 'hero');
  assert.equal(winSound(9), 'hero', 'clamped, never undefined');
  assert.equal(winSound(-3), 'settle');
  assert.equal(winSound('big'), 'settle', 'junk is the floor, never a guess');
  // Brake 3 shrank a hero to a 3: the voice follows the SPENT rung, not the rung that was asked for.
  const sit = { seen: Object.freeze([0, 0, 0, 0, 0]), heroes: 1 };
  const plan = sitPlan(TIER.HERO, sit, {});
  assert.equal(plan.spent, TIER.BIG);
  assert.equal(winSound(plan.spent), 'big');
});

test('THE CHIME LADDER: the first pay is the root, a streak climbs a semitone a spin, capped at 7', () => {
  assert.equal(ladderRoot(1), 0, 'the first paying spin of a run is the root note');
  assert.equal(ladderRoot(2), 1);
  assert.equal(ladderRoot(5), 4);
  assert.equal(ladderRoot(8), LADDER.CAP, 'the cap holds');
  assert.equal(ladderRoot(90), LADDER.CAP);
  assert.equal(ladderRoot(0), 0, 'a miss has no root of its own to climb from');
  assert.equal(ladderRoot(-4), 0);
  // Brake 5 drops the WHOLE ladder an octave, and it is exactly plan.octave: the station never adds it twice.
  for (const streak of [1, 3, 7, 20]) {
    assert.equal(ladderRoot(streak, true), ladderRoot(streak, false) + LADDER.OCTAVE);
    assert.equal(ladderRoot(streak, true) - ladderRoot(streak, false), winPlan(TIER.BIG, { melted: true }).octave);
  }
});

test('the climb the station plays is exactly the notes the plan bought', () => {
  for (const tier of [1, 2, 3, 4]) {
    const plan = winPlan(tier, {});
    const steps = ladderPlan(plan.spent, plan.partyMs, false);
    assert.equal(steps.length, plan.ladder, 'tier ' + tier + ' plays plan.ladder notes, the landing cue included');
    assert.equal(steps[0].at, 0, 'the landing note is on the landing frame');
    for (let i = 1; i < steps.length; i++) assert.ok(steps[i].at - steps[i - 1].at >= LADDER.STROBE_MIN_MS, 'no step under the 6 Hz floor');
  }
  // Law VI: reduced motion has no count to climb over, and the cue still plays (the ladder survives).
  const red = winPlan(TIER.HERO, { reduced: true });
  assert.equal(red.partyMs, 0);
  assert.ok(red.ladder > 0);
  assert.equal(ladderPlan(red.spent, red.partyMs, false).length, 1, 'the landing note alone, never a faster climb');
  // Brake 5: melted is one note, an octave down.
  assert.equal(ladderPlan(winPlan(TIER.HERO, { melted: true }).spent, 500, true).length, 1);
});

test('Brake 5 at this table: a landing on a melt pocket is a focus state, and nothing else is', () => {
  assert.equal(meltedBy(read({ pocket: 4 })), true, '4 is a drop pocket (the ring runs spiral, eye, bubble, drop)');
  assert.equal(meltedBy(read({ pocket: 1 })), false);
  assert.equal(meltedBy(read({ pocket: 0 })), false, 'the house pocket has no glyph at all');
  assert.equal(meltedBy(null), false);
  let drops = 0;
  for (let n = 0; n <= 36; n++) if (meltedBy(read({ pocket: n }))) { drops++; assert.equal(glyphFor(n), 'drop'); }
  assert.equal(drops, 9, 'nine of the thirty-seven pockets melt you');
  // What that buys: the chime drops an octave, the decoration goes, THE BANK still flies (a melted win is
  // quiet, never invisible) - and every one of those is the plan's call, not this station's.
  const plan = winPlan(TIER.HERO, { melted: true });
  assert.equal(plan.spent, TIER.SMALL);
  assert.equal(plan.octave, LADDER.OCTAVE);
  assert.equal(plan.shower, 0); assert.equal(plan.sparkle, 0); assert.equal(plan.glow, 0); assert.equal(plan.reveal, false);
  assert.ok(plan.bank >= 3, 'the tokens still fly');
  assert.equal(plan.why, 'melted');
});

test('Law XII: the tokens are dealt over the PAYING chips, so every chip that paid sends something', () => {
  assert.deepEqual(tokenSpots(5, ['rose', 'sink']), ['rose', 'sink', 'rose', 'sink', 'rose']);
  assert.deepEqual(tokenSpots(3, ['s17']), ['s17', 's17', 's17']);
  assert.equal(tokenSpots(7, ['rose', 'sink', 'deep']).length, 7);
  for (const spot of ['rose', 'sink', 'deep']) assert.ok(tokenSpots(7, ['rose', 'sink', 'deep']).includes(spot));
  // No cell to leave from: the caller falls back to the landed pocket, so value still leaves where it was won.
  assert.deepEqual(tokenSpots(3, []), [null, null, null]);
  assert.deepEqual(tokenSpots(3, null), [null, null, null]);
  assert.deepEqual(tokenSpots(0, ['rose']), []);
  assert.deepEqual(tokenSpots(-2, ['rose']), []);
});

test('Law X: a party owns the station until it is finished, and a hold of 0 is the old hold exactly', () => {
  const launch = 1000, rest = launch + 6400, land = launch + 6000;
  const was = nextLaunchAt(launch, rest, { landMs: land, win: true });
  assert.equal(nextLaunchAt(launch, rest, { landMs: land, win: true, partyMs: 0 }), was, 'PURELY ADDITIVE');
  assert.equal(was, land + FEEL.WIN_HOLD_MS);
  // The 6 s hero climb is measured from the fx frame, and it is never cut off by the next launch.
  const hero = nextLaunchAt(launch, rest, { landMs: land, win: true, partyMs: 6000 });
  assert.equal(hero, land + FX_DELAY_MS + 6000);
  assert.ok(hero > was);
  // A party shorter than the callout changes nothing: the callout still gets its whole window.
  assert.equal(nextLaunchAt(launch, rest, { landMs: land, win: true, partyMs: 500 }), was);
  // A miss is quick, party or no party (nothing can hand a miss one).
  assert.equal(nextLaunchAt(launch, rest, { landMs: land, win: false, partyMs: 6000 }), Math.max(launch + FEEL.SPIN_MS, rest + FEEL.MIN_HOLD_MS));
});

test('Law IX over a sit-down at this table: the fanfare wears down and the hero plays once', () => {
  let sit = freshSit();
  const plan = () => { const p = sitPlan(rewardTier(SMALL), sit, {}); sit = afterParty(sit, p); return p; };
  for (let i = 0; i < 3; i++) assert.equal(plan().spent, TIER.SMALL, 'the first three small wins get what they are worth');
  assert.equal(plan().why, 'repeat', 'the fourth is worn down');
  // The hero: the first Full Wake of a visit takes THE REVEAL, the second is a very good tier 3.
  let s2 = freshSit();
  const first = sitPlan(rewardTier(FULL), s2, {});
  assert.equal(first.spent, TIER.HERO); assert.equal(first.reveal, true);
  s2 = afterParty(s2, first);
  const second = sitPlan(rewardTier(FULL), s2, {});
  assert.equal(second.spent, TIER.BIG); assert.equal(second.reveal, false); assert.equal(second.why, 'capped');
  // A jackpot under Calm never burns the hero: the reveal did not fire, so the ledger did not count one.
  let s3 = freshSit();
  const calm = sitPlan(rewardTier(FULL), s3, { still: true });
  assert.equal(calm.reveal, false);
  assert.ok(calm.bank >= 3, 'Calm is not reduced motion: the value still moves');
  assert.equal(calm.shower, 0); assert.equal(calm.sparkle, 0);
  s3 = afterParty(s3, calm);
  assert.equal(sitPlan(rewardTier(FULL), s3, {}).reveal, true, 'the hero is still owed');
});

test('10.22.B: the shower is skipped, never handed a 0 - a small win does not show from across the room', () => {
  const shower = (r, ctx = {}) => sitPlan(rewardTier(r), freshSit(), ctx).shower;
  assert.equal(shower(SMALL), 0, 'Law IX: a small win never gets confetti');
  assert.ok(shower(GOOD) > 0);
  assert.ok(shower(STRAIGHT) > shower(GOOD));
  assert.equal(shower(FULL), 4);
  assert.equal(shower(MISS), 0, 'the room is never told about a miss');
  assert.equal(shower(FULL, { still: true }), 0, 'Calm draws nothing across the room');
  assert.equal(shower(FULL, { reduced: true }), 0);
  assert.equal(shower(FULL, { melted: true }), 0);
  assert.ok(shower(FULL, { lite: true }) <= 2, 'Brake 8 caps it');
});

test('Law XII end to end: the chip leaves what it said and lands on the tape\'s own number', () => {
  // 40 SP on the chip, a 36 SP straight-up: the chip is pinned at 40 and THE BANK carries it to 76.
  const plan = sitPlan(rewardTier(STRAIGHT), freshSit(), {});
  const run = createBankRun({ kind: 'pay', n: plan.bank, fromValue: 40, toValue: 76, rollupMs: plan.partyMs, startMs: 0 });
  assert.equal(plan.bank, 5);
  let shown = 40, ticks = 0, thud = 0;
  for (let ms = 0; ms <= plan.partyMs + bankFlightMs(plan.bank) + 200; ms += 16) {
    for (const ev of run.step(ms).events) {
      if (ev.type === 'tick') { assert.ok(ev.value >= shown, 'a pay never ticks backwards'); shown = ev.value; if (!ev.tail) ticks++; }
      if (ev.type === 'land' && !ev.counting) { thud++; assert.equal(shown, 76, 'Law X: the mini-thud waits for the end of the count'); }
    }
    if (run.done) break;
  }
  assert.equal(ticks, plan.bank, 'one tick a token, on the landing');
  assert.equal(thud, 1, 'one mini-thud, on one frame');
  assert.equal(run.settled, 76);
  assert.equal(shown, 76);
});

test('Law VI: reduced motion takes the settled STATE - no tokens, no travel, the cue still plays', () => {
  const plan = sitPlan(rewardTier(FULL), freshSit(), { reduced: true });
  assert.equal(plan.bank, 0); assert.equal(plan.partyMs, 0); assert.equal(plan.reveal, false);
  assert.equal(plan.sparkle, 0); assert.equal(plan.glow, 0); assert.equal(plan.why, 'reduced');
  assert.ok(plan.ladder > 0, 'the cue survives: no sound without a visual is not the rule, silence is worse');
  // The station hands plan.bank === 0 straight to the engine as `reduced`, and it settles on the first step.
  const run = createBankRun({ kind: 'pay', n: 1, fromValue: 40, toValue: 256, reduced: true, startMs: 0 });
  const first = run.step(0);
  assert.deepEqual(first.tokens, []);
  assert.equal(first.shown, 256);
  assert.deepEqual(first.events.map((e) => e.type), ['tick', 'land', 'done']);
  assert.equal(run.done, true);
});
