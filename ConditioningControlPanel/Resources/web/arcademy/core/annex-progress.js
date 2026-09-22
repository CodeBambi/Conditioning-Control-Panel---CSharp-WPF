// Annex access counts stars across the current roster, including partial cards.
export const ANNEX_COMPLETION = 0.75;
export const CARD_STARS = 10;

export function annexProgress(roster, readCard, pending = {}) {
  const keys = [...new Set((roster || []).map(entry => entry.key))];
  const clamp = value => Number.isFinite(Number(value))
    ? Math.min(CARD_STARS, Math.max(0, Math.floor(Number(value)))) : 0;
  let stars = 0;
  for (const key of keys) {
    let count = 0;
    try { count = clamp(readCard(key)?.punches); } catch { /* Missing cards count as zero. */ }
    // The ceremony can receive the host's mint before the store echo arrives.
    if (key === pending.gameKey) count = Math.max(count, clamp(pending.card?.punches ?? pending.to));
    stars += count;
  }
  const required = Math.ceil(keys.length * CARD_STARS * ANNEX_COMPLETION);
  return { stars, required, eligible: keys.length > 0 && stars >= required };
}
