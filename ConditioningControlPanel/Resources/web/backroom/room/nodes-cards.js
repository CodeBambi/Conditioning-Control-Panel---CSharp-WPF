// Named authored anchors only. Missing nodes are model requests, never inferred placements.
export const DEALER_CAPACITY = 12;
export const CARD_SLOTS = Object.freeze(['dealer', 'p0', 'p1'].flatMap((h) => Array.from({ length: h === 'dealer' ? DEALER_CAPACITY : 6 }, (_, i) => `card_slot_${h}_${i}`)));
export const validCardSlot = (owner, slot) => (owner === 'd' || owner === 0 || owner === 1) && Number.isInteger(slot) && slot >= 0 && slot < (owner === 'd' ? DEALER_CAPACITY : 6);
export const REQUIRED = Object.freeze([...CARD_SLOTS, 'deck_shoe_mouth', 'deck_shoe_base', 'bet_spot_0', 'bet_spot_1', 'table_lamp', 'felt_surface']);
export function cardsNodes(root) {
  const found = new Map(); root.traverse((n) => { if (REQUIRED.includes(n.name)) found.set(n.name, n); });
  const missing = REQUIRED.filter((name) => !found.has(name));
  if (missing.length) throw new Error('Cards model missing: ' + missing.join(', '));
  return Object.fromEntries(found);
}
export const slotName = (owner, slot) => `card_slot_${owner === 'd' ? 'dealer' : 'p' + owner}_${slot}`;
