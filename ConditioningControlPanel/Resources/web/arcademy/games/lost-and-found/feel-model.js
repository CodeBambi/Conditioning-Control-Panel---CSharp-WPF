import { makeRng, shuffled } from '../../core/rng.js';

// Each artwork occurs exactly once. Only exposed pictures enter the references.
export function deal(seed = 'paper-room') {
  const rng = makeRng(seed);
  const pictures = shuffled(Array.from({ length: 27 }, (_, i) => i), rng);
  return { piles: Array.from({ length: 9 }, (_, i) => pictures.slice(i * 3, i * 3 + 3)), order: shuffled(pictures, rng), cursor: 0, targets: [], found: 0, misses: 0 };
}

export function references(state) {
  const exposed = state.piles.map(p => p.at(-1)).filter(id => id !== undefined);
  state.targets = state.targets.filter(id => exposed.includes(id));
  for (let scan = 0; scan < state.order.length; scan++) {
    if (state.targets.length === 3) break;
    const id = state.order[state.cursor++ % state.order.length];
    if (exposed.includes(id) && !state.targets.includes(id)) state.targets.push(id);
  }
  return state.targets;
}

export function peel(state, seat) {
  const pile = state.piles[seat];
  if (!pile?.length) return false;
  const id = pile.at(-1);
  if (!state.targets.includes(id)) { state.misses++; return false; }
  pile.pop();
  state.found++;
  references(state);
  return true;
}
