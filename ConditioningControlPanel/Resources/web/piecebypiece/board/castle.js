// The rook clears the back rank before crossing, then returns beside the king.
// A delay on a straight path cannot prevent two pieces passing through each other.
export const CASTLE = Object.freeze({ duration: .90, clearance: 1.05, lift: .22 });
const smooth = t => { t = Math.max(0, Math.min(1, t)); return t * t * t * (t * (t * 6 - 15) + 10); };
export function castlePose(from, to, progress) {
  const p = Math.max(0, Math.min(1, progress));
  const travel = smooth((p - .22) / .56);
  const outside = smooth(p / .22) * (1 - smooth((p - .78) / .22));
  return {
    x: from.x + (to.x - from.x) * travel,
    y: from.y + (to.y - from.y) * smooth(p) + Math.sin(Math.PI * p) * CASTLE.lift,
    z: from.z + (to.z - from.z) * smooth(p) + Math.sign(from.z) * outside * CASTLE.clearance,
  };
}
