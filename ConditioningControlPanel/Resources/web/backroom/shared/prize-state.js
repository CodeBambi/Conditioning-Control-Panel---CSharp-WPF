// Server state is the only ownership source. Opening the room never replays a purchase.
export function prizeState(body) {
  if (body?.ok !== true || body.open === false || !Array.isArray(body.catalog)) return null;
  const owned = body.catalog.filter(row => row?.owned && typeof row.id === 'string').map(row => row.id);
  const grants = Array.isArray(body.prizes?.grants) ? body.prizes.grants : [];
  return { owned, racing: grants.some(id => /^rt\.original\.(0[0-9]|10)$/.test(id)), tracks: grants.filter(id => /^rt\.original\.(0[0-9]|10)$/.test(id)).map(id => Number(id.slice(-2))), demo: grants.includes('rt.original.00') || owned.includes('rt_demo') };
}
export function prizeFrame(seconds, still = false) {
  const p = still ? 1 : Math.max(0, Math.min(1, seconds / 1.25));
  const ease = p * p * (3 - 2 * p);
  return { done: p === 1, lift: p === 1 ? 0 : Math.sin(Math.PI * p) * .16,
    turn: p === 1 ? 0 : Math.sin(Math.PI * p) * .18, alpha: ease, stamp: Math.min(1, p / .7) };
}
