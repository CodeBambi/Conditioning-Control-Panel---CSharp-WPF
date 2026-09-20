// Fractions of eligible ordinary bricks. Special mechanisms keep their own health.
// Two-hit and three-hit shares can be tuned independently for each level.
export const STRENGTH_BY_WALL = Object.freeze([
  {two:.08,three:.03}, {two:.10,three:.04}, {two:.08,three:.03},
  {two:.12,three:.05}, {two:.12,three:.05}, {two:.10,three:.04},
  {two:.15,three:.07}, {two:0,three:0},
].map(Object.freeze));
export const hasPayload = b => b.gif === true || (typeof b.gif === 'number' && b.gif >= 0) || b.word || b.spiral || b.split || b.jackpot;
export const canStrengthen = b => b.alive && !hasPayload(b) && !b.strength && !b.hp &&
  !b.irisCore && !Number.isFinite(b.irisAge) && !b.pendulumAnchor && !b.pendulumGuard &&
  !b.finaleRing && !b.finaleDefense && !b.finaleWord && !b.finaleFeed && !b.finaleGate;
export function distributeStrength(bricks, mix = {}, rng = Math.random) {
  const candidates = bricks.filter(canStrengthen), payloads = bricks.filter(b => b.alive && hasPayload(b));
  const share = n => Math.max(0, Math.min(1, Number(n) || 0));
  const three = Math.min(candidates.length, Math.round(candidates.length * share(mix.three)));
  const two = Math.min(candidates.length - three, Math.round(candidates.length * share(mix.two)));
  if (!two && !three) return;
  // Weighted sampling prefers nearby effect/extra-ball neighbours without moving any brick.
  const ranked = candidates.map(b => {
    let distance = Infinity;
    for (const p of payloads) distance = Math.min(distance, Math.hypot(b.x+b.w/2-p.x-p.w/2,b.y+b.h/2-p.y-p.h/2));
    const weight = 1 + 8 / (1 + (distance / 65) ** 2);
    return {brick:b, rank:-Math.log(Math.max(.000001, Math.min(.999999, rng()))) / weight};
  }).sort((a,b)=>a.rank-b.rank);
  for (let i=0;i<two+three;i++) {
    const b=ranked[i].brick;b.strength=b.hp=i<three?3:2;
  }
}
