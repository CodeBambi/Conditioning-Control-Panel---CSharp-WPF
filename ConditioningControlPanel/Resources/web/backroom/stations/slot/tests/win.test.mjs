/* win.test.mjs - lane BR2-rw-slot. THE REWARD PASS at the reference cabinet (CONTRACT 10.22).
 *
 * The slot is the bar the other three stations are being raised to, so this file's whole job is to prove the
 * bar did not move while the floor under it was replaced. Three things are held:
 *
 *  1. THE RETROFIT IS THE SAME NUMBERS. feel.js's ladder and bank maths are now shared/win/'s, by identity,
 *     and they still answer what the House Book says they answer.
 *  2. recipe() IS THE SAME TABLE. THE BRAKE moved into plan.js, so recipe is checked against an ORACLE - the
 *     exact table that stood in feel.js before this pass - over the whole grid of tier x seen x jackpots x
 *     melt. If plan.js and the cabinet ever disagree about what Brake 3 did, this fails.
 *  3. THE ADAPTER PLAYS THE SAME BEATS. stations/slot/bank.js is a thin skin over createBankRun now, so the
 *     tick order (Law X), the merge (Brake 2), the skip (Law VI) and the tear-down are driven through a DOM
 *     double and a hand-wound clock. Unit only: no browser, no screenshots.
 */

import test from 'node:test';
import assert from 'node:assert/strict';

import { FEEL, recipe, tierOf, meltedBy, isHold, ROLLUP_MS, rollupMs,
         ladderSemis, ladderPlan, winTokens, spendTokens, tickValues, rollupAt, rollupTicks,
         bankFlightMs, bankLandMs, JAR_TIER } from '../feel.js';
import * as sharedLadder from '../../../shared/win/ladder.js';
import * as sharedBank from '../../../shared/win/bank.js';
import { TIER, houseTier } from '../../../shared/win/tier.js';
import { winPlan, sitPlan, freshSit, afterParty, PARTY } from '../../../shared/win/plan.js';

const o = (line, pay, extra = {}) => ({ line, pay, meltLeft: 0, halved: false, ...extra });

/* ----------------------------------------------------------------------------
 * 1. THE RETROFIT: one ladder, one bank, and the same figures as ever
 * -------------------------------------------------------------------------- */

test('the slot climbs the ROOM\'s ladder and flies the ROOM\'s bank - the same functions, not copies of them', () => {
  assert.equal(ladderSemis, sharedLadder.ladderSemis);
  assert.equal(ladderPlan, sharedLadder.ladderPlan);
  for (const [a, b] of [[winTokens, sharedBank.winTokens], [spendTokens, sharedBank.spendTokens],
                        [tickValues, sharedBank.tickValues], [rollupAt, sharedBank.rollupAt],
                        [rollupTicks, sharedBank.rollupTicks], [bankFlightMs, sharedBank.bankFlightMs],
                        [bankLandMs, sharedBank.bankLandMs]]) assert.equal(a, b);
  // ...and the House Book's own numbers are still the House Book's own numbers.
  assert.equal(FEEL.BANK_FLY_MS, 560);
  assert.equal(FEEL.BANK_STAGGER_MS, 70);
  assert.equal(FEEL.LADDER_CAP, 7);
  assert.equal(FEEL.FANFARE_TIMES, 3);
  assert.equal(FEEL.THUD_ONLY_FROM, 40);
  assert.equal(FEEL.STROBE_MIN_MS, 1000 / 6);
  assert.deepEqual([0, 1, 2, 3, 4].map(t => winTokens(t, false)), [3, 3, 4, 5, 7]);
  assert.deepEqual([0, 1, 2, 3, 4].map(t => winTokens(t, true)), [3, 3, 4, 4, 4]);
  assert.equal(bankFlightMs(7), 560 + 6 * 70);
  assert.deepEqual([...ROLLUP_MS], [0, 500, 1200, 2000, 6000]);
  assert.deepEqual([...ROLLUP_MS], [...PARTY.MS]);   // THE PARTY's length by rung IS the rollup's
  assert.equal(rollupMs(o('emi3', 400)), 6000);
  assert.equal(rollupMs(40), 2000);                  // a bare 400 is a big line, never the jackpot
});

test('the rungs the room gives the slot are the rungs tierOf already gave it', () => {
  for (const [line, pay, want] of [['spiral2', 5, 1], ['sub2', 5, 1], ['gif3', 5, 1], ['spiral3', 10, 2],
                                  ['sub3', 10, 2], ['gif3same', 40, 3], ['emi3', 400, 4],
                                  ['none', 0, 0], ['melt', 0, 0]]) {
    assert.equal(houseTier({ station: 'slot', tier: tierOf(o(line, pay)) }), tierOf(o(line, pay)), line);
    assert.equal(houseTier({ station: 'slot', tier: tierOf(o(line, pay)) }), want, line);
  }
  // Rule 3: a station's own tierOf is AUTHORITATIVE and a pay never raises it. A 2-spiral line that happened
  // to pay 400 is still a tier 1 at this cabinet - the slot's table said so and the spine does not argue.
  assert.equal(houseTier({ station: 'slot', tier: tierOf(o('spiral2', 400)), pay: 400 }), 1);
  // ...and the jackpot is the jackpot however it was halved (Brake 5 quiets it, it does not demote it).
  assert.equal(houseTier({ station: 'slot', tier: tierOf(o('emi3', 200, { halved: true })) }), 4);
});

/* ----------------------------------------------------------------------------
 * 2. recipe(): THE BRAKE moved, the table did not
 * -------------------------------------------------------------------------- */

/** The verdict table EXACTLY as it stood in feel.js before the spine landed. Kept here, and only here, as the
 *  oracle the retrofit is measured against. If this and recipe() ever part company, one of them is a bug. */
function oracle(out, { seen = 0, jackpots = 0 } = {}) {
  const tier = tierOf(out), melted = meltedBy(out);
  const r = { tier, melted, party: 'fanfare', sound: 'chime', tokens: tier > 0, heat: tier, gold: false,
              chase: false, screen: false, jolt: false, reveal: false, sparks: false, shiver: false };
  if (isHold(out)) return { ...r, tier: 0, party: 'hold', sound: 'muted', tokens: false, heat: 0 };
  if (tier === 0) return { ...r, party: 'shiver', sound: 'muted', heat: 0, shiver: true };
  if (melted) return { ...r, party: 'melt', heat: Math.min(1, tier) };
  if (tier === 4 && jackpots === 0) {
    return { ...r, sound: 'reveal', gold: true, chase: true, screen: true, jolt: true, reveal: true, sparks: true };
  }
  if (seen >= 39) return { ...r, party: 'thud', sound: 'thud', heat: 0 };
  if (seen >= 3) return { ...r, party: 'bead', sound: 'chime' };
  return { ...r, sound: tier === 1 ? 'chime' : tier === 2 ? 'two' : 'thud',
           gold: tier === 4, chase: true, screen: tier >= 2, jolt: tier >= 2 };
}

const LINES = ['none', 'melt', 'emi2', 'spiral2', 'sub2', 'gif3', 'spiral3', 'sub3', 'gif3same', 'emi3'];
const SEENS = [0, 1, 2, 3, 4, 10, 38, 39, 40, 41, 120];

test('recipe: the Brake lives in plan.js now and the cabinet\'s table is byte-for-byte what it was', () => {
  let checked = 0;
  for (const line of LINES) {
    for (const pay of [0, 4, 10, 40, 400]) {
      for (const melted of [false, true]) {
        for (const seen of SEENS) {
          for (const jackpots of [0, 1, 3]) {
            const out = o(line, pay, melted ? { halved: true } : {});
            const ctx = { seen, jackpots };
            assert.deepEqual(recipe(out, ctx), oracle(out, ctx), `${line}/${pay}/melt${melted}/seen${seen}/j${jackpots}`);
            checked += 1;
          }
        }
      }
    }
  }
  assert.ok(checked > 3000, `the grid should be wide, was ${checked}`);
});

test('recipe reads plan.js\'s verdict: melt, the once-a-sit-down hero, the bead and the 40th thud', () => {
  const big = o('gif3same', 40), jack = o('emi3', 400);
  // Brake 5 first, ahead of everything: a melted jackpot is a melt party, not a reveal.
  assert.equal(winPlan(4, { melted: true }).why, 'melted');
  assert.equal(recipe(o('emi3', 400, { halved: true }), {}).party, 'melt');
  assert.equal(recipe(o('emi3', 400, { halved: true }), {}).reveal, false);
  // Law IX: the hero plays once a sit-down, and the second one is a very good tier 3.
  assert.equal(recipe(jack, { jackpots: 0 }).reveal, true);
  assert.equal(winPlan(4, { heroesThisSit: 1 }).why, 'capped');
  assert.equal(recipe(jack, { jackpots: 1 }).reveal, false);
  assert.equal(recipe(jack, { jackpots: 1 }).party, 'fanfare');
  // Brake 3 wears the CAPPED jackpot down too, which the plan says by spending under a whole tier 3.
  assert.equal(winPlan(4, { heroesThisSit: 1, seen: 5 }).spent, 2);
  assert.equal(recipe(jack, { jackpots: 1, seen: 5 }).party, 'bead');
  assert.equal(recipe(jack, { jackpots: 1, seen: 45 }).party, 'thud');
  // ...but a fresh hero is exempt from Brake 3 outright: it is capped by the count, not worn by it.
  assert.equal(recipe(jack, { jackpots: 0, seen: 45 }).reveal, true);
  // And the ordinary rungs: three fanfares, then the bead, then the thud.
  assert.deepEqual([0, 2, 3, 38, 39].map(seen => recipe(big, { seen }).party),
                   ['fanfare', 'fanfare', 'bead', 'bead', 'thud']);
});

/* ----------------------------------------------------------------------------
 * 3. WHAT THE LANDING SPENDS - the plan station.js hands each move
 * -------------------------------------------------------------------------- */

test('Law IX: a small win never shows from across the room, and a big one does', () => {
  // plan.shower is what ctx.revealedWin is given (10.22.B). 0 means the call is SKIPPED, because
  // room/coin-shower.js clamps 1..4 and would otherwise throw a 7-coin shower at a two-spiral line.
  assert.deepEqual([0, 1, 2, 3, 4].map(t => winPlan(t, {}).shower), [0, 0, 2, 3, 4]);
  assert.equal(winPlan(1, {}).shower, 0, 'a chime is a close-up event');
  assert.equal(winPlan(4, { still: true }).shower, 0, 'Calm settles the travel');
  assert.equal(winPlan(4, { melted: true }).shower, 0, 'Brake 5: no ceremonies');
  assert.equal(winPlan(4, { reduced: true }).shower, 0, 'Law VI: the state, never the travel');
  assert.equal(winPlan(4, { lite: true }).shower, 2, 'Brake 8: less, not none');
});

test('10.22.D THE SPARKLE BURST enters at tier 3 and never below it, never on lite, Calm, melt or reduced', () => {
  assert.deepEqual([0, 1, 2, 3, 4].map(t => winPlan(t, {}).sparkle), [0, 0, 0, 7, 9]);
  for (const ctx of [{ lite: true }, { still: true }, { melted: true }, { reduced: true }]) {
    assert.equal(winPlan(4, ctx).sparkle, 0, JSON.stringify(ctx));
  }
  // counterfx's own clamp is 5..9, so every count the plan ever hands sparkBurst is inside it.
  for (const t of [3, 4]) assert.ok(winPlan(t, {}).sparkle >= 5 && winPlan(t, {}).sparkle <= 9);
});

test('10.22.D THE GLOW rides every paying rung, keeps Calm, and goes out while melted or reduced', () => {
  assert.deepEqual([0, 1, 2, 3, 4].map(t => winPlan(t, {}).glow), [0, 480, 480, 480, 480]);
  assert.equal(winPlan(3, { still: true }).glow, 480, 'a warm cut is not travel');
  assert.equal(winPlan(3, { lite: true }).glow, 480);
  assert.equal(winPlan(3, { melted: true }).glow, 0);
  assert.equal(winPlan(3, { reduced: true }).glow, 0);
  assert.ok(FEEL.GLOW_OUT_MS === 480 && 480 <= FEEL.MOVE_CAP_MS, 'and it is inside the move cap');
});

test('Law XII: the value moves at every motion level but one, and only reduced motion takes it still', () => {
  for (const ctx of [{}, { still: true }, { lite: true }, { melted: true }]) {
    assert.ok(winPlan(4, ctx).bank >= 3, `the bank still flies: ${JSON.stringify(ctx)}`);
  }
  assert.equal(winPlan(4, { reduced: true }).bank, 0);
  assert.equal(winPlan(4, { reduced: true }).partyMs, 0);
  assert.ok(winPlan(4, { reduced: true }).ladder > 0, 'Brake 9: the cue survives the stillness');
  assert.equal(winPlan(4, { lite: true }).bank, 4, 'Brake 8');
});

test('the ladder the landing climbs is the plan\'s rung over the plan\'s own rollup', () => {
  // station.js hands ladderPlan(plan.spent, plan.partyMs, melted): the notes and the span come off ONE plan,
  // so a party the Brake shrank climbs a shorter ladder over a shorter count, not the raw tier's.
  const full = winPlan(3, {});
  assert.equal(ladderPlan(full.spent, full.partyMs, false).length, full.ladder);
  const worn = winPlan(3, { seen: 5 });
  assert.equal(worn.spent, 2);
  assert.equal(ladderPlan(worn.spent, worn.partyMs, false).length, worn.ladder);
  assert.ok(worn.partyMs < full.partyMs && worn.ladder < full.ladder, 'Brake 3 shrinks the whole beat');
  // Brake 5 flattens it to the landing note and drops the whole thing an octave.
  const melt = winPlan(4, { melted: true });
  assert.equal(melt.ladder, 1);
  assert.equal(melt.octave, -12);
  assert.equal(ladderSemis(3, true), 3 - 12);
  // Law VI: reduced motion has no rollup to climb over, so it is the landing note and nothing after it.
  const off = winPlan(4, { reduced: true });
  assert.equal(ladderPlan(off.spent, off.partyMs, false).length, 1);
  // Brake 7: never a step closer than 6 Hz, however short the span.
  for (const [t, ms] of [[4, 100], [3, 200], [2, 60]]) {
    const steps = ladderPlan(t, ms, false);
    for (let i = 1; i < steps.length; i += 1) assert.ok(steps[i].at - steps[i - 1].at >= FEEL.STROBE_MIN_MS);
  }
});

test('the sit-down ledger: one per open(), per RUNG, and a hero only burns when it actually played', () => {
  let sit = freshSit();
  const big = () => sitPlan(3, sit, {});
  for (let i = 0; i < 3; i += 1) { assert.equal(big().spent, 3); sit = afterParty(sit, big()); }
  assert.equal(big().spent, 2, 'the fourth tier 3 is a rung down (Brake 3)');
  assert.equal(sitPlan(1, sit, {}).spent, 1, 'and the tier 1 rung is untouched: `seen` is per rung');
  // Law IX: the jackpot's REVEAL is once a sit-down...
  let s2 = freshSit();
  const hero = sitPlan(4, s2, {});
  assert.equal(hero.reveal, true);
  s2 = afterParty(s2, hero);
  assert.equal(sitPlan(4, s2, {}).reveal, false);
  assert.equal(sitPlan(4, s2, {}).spent, 3);
  // ...and a jackpot that never got to be a hero does not spend the sit-down's one.
  let s3 = freshSit();
  const quiet = sitPlan(4, s3, { still: true });
  assert.equal(quiet.reveal, false);
  s3 = afterParty(s3, quiet);
  assert.equal(s3.heroes, 0);
  assert.equal(sitPlan(4, s3, {}).reveal, true);
  // B1 (10.16.A): the jar borrows spiral3's shape because it IS that event, so it wears the SAME rung down.
  let s4 = freshSit();
  for (let i = 0; i < 3; i += 1) s4 = afterParty(s4, sitPlan(JAR_TIER, s4, {}));
  assert.equal(s4.seen[2], 3);
  assert.equal(sitPlan(2, s4, {}).why, 'repeat');
});

/* ----------------------------------------------------------------------------
 * 4. THE ADAPTER - stations/slot/bank.js over createBankRun, on a DOM double
 * -------------------------------------------------------------------------- */

/** The smallest document the adapter touches: a layer with a rect, and `<i>` tokens with a style bag. */
function harness() {
  const made = [];
  const layer = {
    children: [],
    getBoundingClientRect: () => ({ left: 100, top: 50, width: 400, height: 300 }),
    append(el) { this.children.push(el); },
  };
  const doc = {
    createElement() {
      const el = { className: '', style: {}, gone: false, remove() { this.gone = true; layer.children = layer.children.filter(c => c !== this); } };
      made.push(el);
      return el;
    },
  };
  let clock = 0;
  const pending = [];
  const prev = { document: globalThis.document, raf: globalThis.requestAnimationFrame,
                 caf: globalThis.cancelAnimationFrame, now: globalThis.performance.now };
  globalThis.document = doc;
  globalThis.requestAnimationFrame = fn => { pending.push(fn); return pending.length; };
  globalThis.cancelAnimationFrame = () => {};
  globalThis.performance.now = () => clock;
  const events = [];
  return {
    layer, made, events,
    /** Run every frame the adapter has asked for, at `ms`. */
    tick(ms) {
      clock = ms;
      const due = pending.splice(0, pending.length);
      for (const fn of due) fn(clock);
    },
    get armed() { return pending.length > 0; },
    hooks: {
      onTick: (value, kind, tail) => events.push(['tick', value, kind, !!tail]),
      onLand: (kind, counting) => events.push(['land', kind, !!counting]),
      onDone: kind => events.push(['done', kind]),
    },
    restore() {
      globalThis.document = prev.document;
      globalThis.requestAnimationFrame = prev.raf;
      globalThis.cancelAnimationFrame = prev.caf;
      globalThis.performance.now = prev.now;
    },
  };
}

const at = () => ({ x: 140, y: 90 });
const to = () => ({ x: 420, y: 70 });

async function withBank(fn) {
  const h = harness();
  const { createBank } = await import('../bank.js');
  try { await fn(h, createBank); } finally { h.restore(); }
}

test('THE BANK pays: the readout ticks as each token LANDS, and lands on the tape\'s number (Laws I, X)', async () => {
  await withBank((h, createBank) => {
    const bank = createBank({ layer: h.layer, reduced: false, ...h.hooks });
    assert.equal(bank.start({ kind: 'pay', n: 5, fromValue: 100, toValue: 140, from: at, to, rollupMs: 0 }), 'flying');
    assert.equal(bank.busy, true);
    assert.equal(bank.kind, 'pay');
    assert.equal(h.made.length, 5, 'five slot-tokens in the layer');
    assert.equal(h.made[0].className, 'slot-token slot-token-pay');
    h.tick(0);
    assert.deepEqual(h.events, [], 'nothing has landed yet, so nothing has ticked (Law X)');
    for (let ms = 60; ms <= bankFlightMs(5) + 40; ms += 60) h.tick(ms);
    const ticks = h.events.filter(e => e[0] === 'tick');
    assert.equal(ticks.length, 5, 'one tick a landing');
    assert.deepEqual(ticks.map(e => e[1]), tickValues(100, 140, 5));
    assert.equal(ticks[ticks.length - 1][1], 140, 'the tape\'s number, never a rounding of it');
    assert.deepEqual(h.events.filter(e => e[0] !== 'tick'), [['land', 'pay', false], ['done', 'pay']]);
    assert.ok(h.made.every(el => el.gone), 'and every token came down');
    assert.equal(bank.busy, false);
  });
});

test('THE BANK\'s rollup: the tokens are down, the count carries on, and the mini-thud waits for the end', async () => {
  await withBank((h, createBank) => {
    const bank = createBank({ layer: h.layer, reduced: false, ...h.hooks });
    bank.start({ kind: 'pay', n: 7, fromValue: 0, toValue: 400, from: at, to, rollupMs: 6000 });
    for (let ms = 0; ms <= 1100; ms += 50) h.tick(ms);
    const lands = h.events.filter(e => e[0] === 'land');
    assert.deepEqual(lands, [['land', 'pay', true]], 'landed, STILL counting: hold the thud');
    assert.ok(!h.events.some(e => e[0] === 'done'));
    // ...and the tail's ticks are marked, so the station plays no second gesture for them (Law X).
    assert.ok(h.events.some(e => e[0] === 'tick' && e[3] === true));
    assert.ok(h.made.every(el => el.gone), 'the tokens came down on the frame the last one landed');
    for (let ms = 1200; ms <= 6100; ms += 100) h.tick(ms);
    assert.deepEqual(h.events.filter(e => e[0] !== 'tick'),
                     [['land', 'pay', true], ['land', 'pay', false], ['done', 'pay']]);
    assert.equal(h.events.filter(e => e[0] === 'tick').pop()[1], 400);
  });
});

test('THE BANK reversed: a spend ticks DOWN as each token LEAVES, and thuds into the tray', async () => {
  await withBank((h, createBank) => {
    const bank = createBank({ layer: h.layer, reduced: false, ...h.hooks });
    bank.start({ kind: 'spend', n: 4, fromValue: 200, toValue: 120, from: to, to: at });
    h.tick(0);
    assert.deepEqual(h.events, [['tick', tickValues(200, 120, 4)[0], 'spend', false]], 'the first one has already left');
    for (let ms = 40; ms <= bankFlightMs(4) + 40; ms += 40) h.tick(ms);
    const ticks = h.events.filter(e => e[0] === 'tick');
    assert.deepEqual(ticks.map(e => e[1]), tickValues(200, 120, 4));
    assert.equal(h.made[0].className, 'slot-token slot-token-spend');
    assert.deepEqual(h.events.filter(e => e[0] !== 'tick'), [['land', 'spend', false], ['done', 'spend']]);
  });
});

test('Brake 2: a pay landing inside a running pay MERGES into it - it never stacks a second party', async () => {
  await withBank((h, createBank) => {
    const bank = createBank({ layer: h.layer, reduced: false, ...h.hooks });
    bank.start({ kind: 'pay', n: 5, fromValue: 0, toValue: 50, from: at, to, rollupMs: 1200 });
    h.tick(600);   // two tokens down
    const before = h.events.filter(e => e[0] === 'tick').length;
    assert.ok(before > 0 && before < 5);
    assert.equal(bank.start({ kind: 'pay', n: 5, fromValue: 0, toValue: 90, from: at, to, rollupMs: 1200 }), 'merged');
    assert.equal(h.made.length, 5, 'and no second handful of tokens was ever made');
    for (let ms = 650; ms <= 1400; ms += 50) h.tick(ms);
    assert.equal(h.events.filter(e => e[0] === 'tick').pop()[1], 90, 'one party, and it lands on the newer total');
    assert.equal(h.events.filter(e => e[0] === 'done').length, 1);
  });
});

test('Brake 2: a spend arriving over a pay settles the pay first, it does not merge with it', async () => {
  await withBank((h, createBank) => {
    const bank = createBank({ layer: h.layer, reduced: false, ...h.hooks });
    bank.start({ kind: 'pay', n: 5, fromValue: 0, toValue: 50, from: at, to, rollupMs: 2000 });
    h.tick(300);
    assert.equal(bank.start({ kind: 'spend', n: 3, fromValue: 50, toValue: 30, from: to, to: at }), 'flying');
    const settled = h.events.filter(e => e[0] === 'tick' && e[2] === 'pay').pop();
    assert.equal(settled[1], 50, 'the pay went straight to the tape\'s value (Law I)');
    assert.ok(h.events.some(e => e[0] === 'done' && e[1] === 'pay'));
    assert.equal(bank.kind, 'spend');
  });
});

test('Law VI: skip settles on the tape\'s number, and land:false leaves quietly (Back, suspend)', async () => {
  await withBank((h, createBank) => {
    const bank = createBank({ layer: h.layer, reduced: false, ...h.hooks });
    bank.start({ kind: 'pay', n: 7, fromValue: 0, toValue: 400, from: at, to, rollupMs: 6000 });
    h.tick(400);
    h.events.length = 0;
    bank.skip({ land: true });   // a lever press: the whole settled state, mini-thud and +N included
    assert.deepEqual(h.events, [['tick', 400, 'pay', true], ['land', 'pay', false], ['done', 'pay']]);
    assert.ok(h.made.every(el => el.gone));
    assert.equal(bank.busy, false);
    // A second skip is nothing at all, and neither is a skip with no run under it.
    h.events.length = 0;
    bank.skip({ land: true });
    assert.deepEqual(h.events, []);
    // ...and Back leaves without the thud.
    bank.start({ kind: 'pay', n: 5, fromValue: 400, toValue: 460, from: at, to, rollupMs: 2000 });
    h.tick(2000);
    h.events.length = 0;
    bank.skip();
    assert.deepEqual(h.events, [['tick', 460, 'pay', true], ['done', 'pay']]);
  });
});

test('Law VI: reduced motion takes the STATE - no tokens, the settled number, the cue still played', async () => {
  await withBank((h, createBank) => {
    const bank = createBank({ layer: h.layer, reduced: true, ...h.hooks });
    assert.equal(bank.start({ kind: 'pay', n: 7, fromValue: 0, toValue: 400, from: at, to, rollupMs: 6000 }), 'state');
    assert.equal(h.made.length, 0, 'nothing was ever built to travel');
    assert.equal(h.armed, false, 'and no frame was ever asked for');
    assert.deepEqual(h.events, [['tick', 400, 'pay', true], ['land', 'pay', false], ['done', 'pay']]);
    assert.equal(bank.busy, false);
  });
});

test('dispose(): the tokens come off and nothing is announced (the cabinet is going away)', async () => {
  await withBank((h, createBank) => {
    const bank = createBank({ layer: h.layer, reduced: false, ...h.hooks });
    bank.start({ kind: 'pay', n: 5, fromValue: 0, toValue: 50, from: at, to });
    h.tick(300);
    h.events.length = 0;
    bank.dispose();
    assert.deepEqual(h.events, []);
    assert.ok(h.made.every(el => el.gone));
    assert.equal(bank.busy, false);
  });
});

test('the tokens fly the House Book\'s arc: a handful, not a line, and they never outrun the flight', async () => {
  await withBank((h, createBank) => {
    const bank = createBank({ layer: h.layer, reduced: false, ...h.hooks });
    bank.start({ kind: 'pay', n: 7, fromValue: 0, toValue: 400, from: at, to, rollupMs: 6000 });
    h.tick(0);
    // Token 0 has left and is on the layer's own axes (the rect's left/top taken off client px).
    assert.equal(h.made[0].style.opacity, '1');
    assert.match(h.made[0].style.transform, /^translate\(/);
    assert.equal(h.made[6].style.opacity, '0', 'the last one has not left yet');
    h.tick(300);
    const lanes = new Set(h.made.slice(0, 5).map(el => el.style.transform.split(',')[0]));
    assert.ok(lanes.size > 1, 'three lanes, so seven tokens are a handful');
    // TIER is the room's, and the cabinet's tokens are sized off it, never off a raw pay.
    assert.equal(winTokens(TIER.HERO, false), 7);
  });
});
