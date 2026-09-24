// Receiver-side bounty. A trailing player gets a larger comeback opportunity.
export function rollLockBounty(lead = 0, random = Math.random) {
  const behind = Math.max(0, Math.min(1, -(Number(lead) || 0) / 150));
  const low = 40 + Math.round(behind * 40);
  return low + Math.floor(Math.max(0, Math.min(0.999999, random())) * 41);
}
export function lockPrize(bounty, mistakes = 0) {
  const base = Math.max(0, Math.min(120, Math.round(Number(bounty) || 0)));
  const slips = Math.max(0, Math.floor(Number(mistakes) || 0));
  return Math.max(0, base - slips * Math.max(1, Math.ceil(base * 0.08)));
}
