// Placement uses rotated brick faces and the visible payload radius.
export function brickOverlap(x, y, radius, bricks) {
  let overlap = 0;
  for (const b of bricks) {
    if (!b.alive || b.finaleWord) continue;
    const a = b.angle || 0, dx = x - b.x - b.w / 2, dy = y - b.y - b.h / 2;
    const lx = dx * Math.cos(a) + dy * Math.sin(a);
    const ly = -dx * Math.sin(a) + dy * Math.cos(a);
    const distance = Math.hypot(Math.max(0, Math.abs(lx) - b.w / 2), Math.max(0, Math.abs(ly) - b.h / 2));
    overlap += Math.max(0, radius + 12 - distance) ** 2;
  }
  return overlap;
}
export function openPayloadSpot(x, y, radius, bricks, width, height, others = []) {
  const left = radius + 12, right = width - left;
  const top = Math.max(radius + 12, height * .30), bottom = height - radius - 76;
  const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
  let best, score = Infinity;
  function consider(cx, cy) {
    let blocked = brickOverlap(cx, cy, radius, bricks);
    for (const other of others) blocked += Math.max(0, radius + other.r + 16 - Math.hypot(cx-other.x, cy-other.y)) ** 2;
    const cost = blocked * 10000 + (cx-x) ** 2 + (cy-y) ** 2;
    if (cost < score) { score = cost; best = {x:cx, y:cy}; }
  }
  consider(clamp(x,left,right), clamp(y,top,bottom));
  for (let row=0;row<=6;row++) for (let col=0;col<=14;col++)
    consider(left+(right-left)*col/14, top+(bottom-top)*row/6);
  return best;
}
