import test from 'node:test';
import assert from 'node:assert/strict';
import { TIER, TIER_NAMES, STATIONS, PAY_STEPS, MOMENT_TIER,
         normTier, tierName, tierFromPay, tierOfMoment, houseTier } from '../tier.js';

/* The four stations' own recipes, imported so a drift in any of them fails HERE and not in a desk run. */
import { tierOf as slotTier } from '../../../stations/slot/feel.js';
import { tierOf as wheelTier, landMoment as wheelMoment } from '../../../stations/wheel/feel.js';
import { landMoment as rouletteMoment, landBeat, CALLOUTS as ROULETTE_CALLOUTS } from '../../../stations/roulette/feel.js';
import { CALLOUTS as CARD_CALLOUTS } from '../../../stations/cards/feel.js';

test('Law IX has five rungs and they are frozen', () => {
  assert.deepEqual(Object.values(TIER), [0, 1, 2, 3, 4]);
  assert.equal(TIER_NAMES.length, 5);
  assert.ok(Object.isFrozen(TIER) && Object.isFrozen(MOMENT_TIER) && Object.isFrozen(MOMENT_TIER.slot));
  assert.deepEqual(STATIONS.slice().sort(), ['cards', 'roulette', 'slot', 'wheel']);
});

test('normTier: anything at all lands on an integer rung, and nonsense is nothing', () => {
  assert.deepEqual([0, 1, 2, 3, 4].map(normTier), [0, 1, 2, 3, 4]);
  assert.equal(normTier(9), 4);
  assert.equal(normTier(3.9), 3);
  assert.equal(normTier(-2), 0);
  for (const junk of [undefined, null, NaN, 'big', {}, Infinity]) assert.equal(normTier(junk), 0, String(junk));
  assert.equal(tierName(3), 'big');
  assert.equal(tierName(null), 'none');
});

test('THE PAY LADDER: 0 is nothing, 400 is the hero, and the steps only go up', () => {
  assert.deepEqual([0, -5, 0.4].map(tierFromPay), [0, 0, 0]);
  assert.deepEqual([1, 9, 10, 39, 40, 399, 400, 5000].map(tierFromPay), [1, 1, 2, 2, 3, 3, 4, 4]);
  let last = Infinity;
  for (const [at] of PAY_STEPS) { assert.ok(at < last, 'PAY_STEPS must be highest first'); last = at; }
});

test('the slot: every payline in LINE_TIER agrees with the slot recipe that owns it', () => {
  for (const [line, rung] of Object.entries(MOMENT_TIER.slot)) {
    if (line === 'hold') continue;   // the emi2 hold is a re-spin, not a landing: the slot returns tier 0 by recipe
    const pay = rung === 0 ? 0 : 5;
    assert.equal(slotTier({ line, pay }), rung, `slot ${line}`);
    assert.equal(houseTier({ station: 'slot', moment: line, pay }), rung, `houseTier slot ${line}`);
  }
});

test('the wheel: every landing row agrees with the wheel recipe that owns it', () => {
  const rows = [
    [{ snoozed: true, pay: 0 }, 'snooze', 0],
    [{ pay: 3 }, 'small', 1],
    [{ pay: 15 }, 'mid', 2],          // the 10.19 roster: 15 and 30 are mid, 60 and 150 big
    [{ pay: 150 }, 'big', 3],
    [{ jackpotWon: true, pay: 550 }, 'jackpot', 4],
    [{ pay: 0, reward: { kind: 'double' } }, 'double', 2],
    [{ pay: 0, reward: { kind: 'decoration' } }, 'gift', 2],
    [{ pay: 0, reward: { kind: 'nothing' } }, 'empty', 0],
  ];
  for (const [r, row, rung] of rows) {
    assert.equal(wheelMoment(r), row, `landMoment ${row}`);
    assert.equal(wheelTier(r), rung, `wheel tierOf ${row}`);
    assert.equal(MOMENT_TIER.wheel[row], rung, `MOMENT_TIER.wheel ${row}`);
  }
});

test('the roulette and the cards: the rungs are their own callout sizes, small / big / hero', () => {
  const size = { small: 1, big: 3, hero: 4 };
  for (const [beat, callout] of Object.entries(ROULETTE_CALLOUTS)) {
    assert.equal(MOMENT_TIER.roulette[beat], size[callout.tier], `roulette ${beat}`);
  }
  for (const [id, callout] of Object.entries(CARD_CALLOUTS)) {
    assert.equal(MOMENT_TIER.cards[id], size[callout.tier], `cards ${id}`);
  }
  assert.equal(landBeat({ pay: 0 }), 'land.miss');
  assert.equal(MOMENT_TIER.roulette['land.miss'], 0);
  assert.equal(rouletteMoment({ pay: 4, straight: true }), 'roulette.land.big');
  assert.equal(MOMENT_TIER.roulette['roulette.land.big'], 3);
});

test('a tier 3 is a tier 3 wherever it was won', () => {
  const slot = houseTier({ station: 'slot', tier: slotTier({ line: 'gif3same', pay: 12 }) });
  const wheel = houseTier({ station: 'wheel', tier: wheelTier({ pay: 60 }) });
  const roulette = houseTier({ station: 'roulette', moment: landBeat({ pay: 12, straight: true }), pay: 12 });
  const cards = houseTier({ station: 'cards', moment: 'cards.streak', pay: 12 });
  assert.deepEqual([slot, wheel, roulette, cards], [3, 3, 3, 3]);
});

test('houseTier order: jackpot outright, none outright, the station tier taken as read', () => {
  assert.equal(houseTier({ jackpot: true, pay: 0, none: true }), 4);          // 1. a halved jackpot is the jackpot
  assert.equal(houseTier({ none: true, pay: 900, tier: 4 }), 0);              // 2. a push pays and is not a win
  assert.equal(houseTier({ station: 'slot', tier: 1, pay: 900 }), 1);         // 3. the slot calls spiral2 a 1, full stop
  assert.equal(houseTier({ station: 'wheel', tier: 0, pay: 900 }), 0);
});

test('houseTier rule 4: a moment station is raised by an honest pay, but a miss stays a miss', () => {
  assert.equal(houseTier({ station: 'roulette', moment: 'land.win', pay: 2 }), 1);
  assert.equal(houseTier({ station: 'roulette', moment: 'land.win', pay: 60 }), 3);
  assert.equal(houseTier({ station: 'roulette', moment: 'land.win', pay: 900 }), 4);
  assert.equal(houseTier({ station: 'roulette', moment: 'land.miss', pay: 900 }), 0);
  assert.equal(houseTier({ station: 'cards', moment: 'cards.push', pay: 500 }), 0);
  assert.equal(houseTier({ station: 'cards', moment: 'cards.win', pay: 1 }), 1);
});

test('houseTier rule 5: an unknown id falls through to the pay, it never throws', () => {
  assert.equal(tierOfMoment('slot', 'nope'), null);
  assert.equal(tierOfMoment('nowhere', 'small'), null);
  assert.equal(houseTier({ station: 'slot', moment: 'nope', pay: 40 }), 3);
  assert.equal(houseTier({}), 0);
  assert.equal(houseTier(), 0);
  assert.equal(houseTier(null), 0);
});
