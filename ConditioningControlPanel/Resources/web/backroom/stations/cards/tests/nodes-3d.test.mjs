import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { REQUIRED, cardsNodes, slotName } from '../../../room/nodes-cards.js';
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
