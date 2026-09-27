import {hasPayload} from './brick-strength.js';

// Count permanent pieces conservatively: even a payload counts as only one hit reward.
// Streams can disappear unaided, so they must never underwrite the escape budget.
export const permanentBrick = b => b.alive && !Number.isFinite(b.irisAge) &&
  !b.pendulumAnchor && !b.irisCore && !b.finaleWord;
export const metalActive = (brick, state) => !!brick.finaleMetal || (!!brick.greyMetal && state === 'grey');
export function distributeGreyMetal(bricks, breakoutN, progress = 0, enabled = true) {
  for (const b of bricks) b.greyMetal = false;
  if (!enabled || bricks.some(b => b.finaleRing || b.finaleFeed || b.finaleGate)) return 0;
  const permanent = bricks.filter(permanentBrick);
  const needed = Math.max(0, breakoutN - progress);
  const reserve = needed + Math.max(3, Math.ceil(needed * .25));
  const candidates = permanent.filter(b => !hasPayload(b) && !b.powerup && !b.strength && !b.hp && !b.pendulumGuard);
  const count = Math.min(candidates.length, Math.max(0, permanent.length - reserve),
    Math.max(1, Math.floor(permanent.length * .12)));
  // Stable scattered placement does not consume the level's payload random sequence.
  candidates.sort((a,b) => rank(a)-rank(b));
  for (let i=0;i<count;i++) candidates[i].greyMetal = true;
  return count;
}
function rank(b) {
  return ((Math.imul((b.row||0)+17,73856093)^Math.imul((b.col||0)+31,19349663)^Math.imul(Math.round(b.x),83492791))>>>0);
}
