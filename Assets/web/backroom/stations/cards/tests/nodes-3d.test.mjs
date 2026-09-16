import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { REQUIRED, cardsNodes, slotName, DEALER_CAPACITY, validCardSlot } from '../../../room/nodes-cards.js';
const glb = readFileSync(new URL('../../../room/assets/card-table.glb', import.meta.url));
const asset = JSON.parse(glb.subarray(20, 20 + glb.readUInt32LE(12)));
test('room model carries every authored blackjack anchor and card dimension', () => {
  const root = { traverse: (f) => asset.nodes.forEach(f) }, nodes = cardsNodes(root);
  assert.equal(Object.keys(nodes).length, REQUIRED.length);
  for (const n of asset.nodes.filter((n) => n.name.startsWith('card_slot_'))) {
    assert.equal(n.extras.card_width, .135); assert.equal(n.extras.card_height, .194);
  }
});
test('missing anchor fails with its name instead of deriving a placement', () => {
  assert.throws(() => cardsNodes({ traverse() {} }), /deck_shoe_mouth/);
  assert.equal(slotName('d', 5), 'card_slot_dealer_5'); assert.equal(slotName(1, 3), 'card_slot_p1_3');
});

// Enumerate all rank states, allowing unlimited copies: a conservative upper bound
// for six decks. Soft totals use one ace as eleven; S17 stops at every total >=17.
test('twelve authored dealer slots cover every S17 rank path', () => {
  const memo = new Map();
  function longest(hard, ace) {
    const key = hard + ':' + ace;
    if (memo.has(key)) return memo.get(key);
    const total = hard + (ace && hard <= 11 ? 10 : 0);
    if (total >= 17) return 0;
    let n = 0;
    for (let rank = 1; rank <= 10; rank++) n = Math.max(n, 1 + longest(hard + rank, ace || rank === 1));
    memo.set(key, n); return n;
  }
  let maximum = 0;
  for (let a = 1; a <= 10; a++) for (let b = 1; b <= 10; b++)
    maximum = Math.max(maximum, 2 + longest(a + b, a === 1 || b === 1));
  assert.equal(maximum, 12); assert.equal(DEALER_CAPACITY, maximum);
  for (let i = 0; i < maximum; i++) assert.ok(REQUIRED.includes(slotName('d', i)));
});

test('malformed card slots are rejected without a guessed placement', () => {
  for (const [owner, slot] of [['d',12], ['d',-1], ['d',1.5], [0,6], [1,6], [2,0], ['bad',0]])
    assert.equal(validCardSlot(owner, slot), false);
  assert.equal(validCardSlot('d',11),true); assert.equal(validCardSlot(1,5),true);
});
