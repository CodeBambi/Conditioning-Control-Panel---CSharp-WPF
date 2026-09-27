// Local board-boundary checkpoints. Starting a board again is deliberate.
export const ENDLESS_SAVE_KEY = 'bo.endless.checkpoint.v1';
export const MAX_ENDLESS_BOARD = 999999;
const finite = (n, lo, hi) => typeof n === 'number' && Number.isFinite(n) && n >= lo && n <= hi;
const integer = (n, lo, hi) => finite(n, lo, hi) && Number.isInteger(n);

export function validateCheckpoint(value) {
  if (!value || value.version !== 1 || !integer(value.seed, 0, 0xffffffff) ||
      !integer(value.from, 0, MAX_ENDLESS_BOARD) || !finite(value.savedSaturation, 0, 1) ||
      !integer(value.bestCombo, 0, 1000000000)) return null;
  return { version: 1, seed: value.seed, from: value.from,
    savedSaturation: value.savedSaturation, bestCombo: value.bestCombo };
}

export function readCheckpoint(storage) {
  try { return validateCheckpoint(JSON.parse(storage?.get(ENDLESS_SAVE_KEY) || 'null')); }
  catch { return null; }
}

export function writeCheckpoint(storage, checkpoint) {
  const value = validateCheckpoint(checkpoint);
  if (!value) return false;
  try { return storage?.set(ENDLESS_SAVE_KEY, JSON.stringify(value)) !== false && !!storage; }
  catch { return false; }
}

export function checkpointFromSnapshot(snapshot) {
  if (!snapshot?.endless) return null;
  return validateCheckpoint({ version: 1, seed: snapshot.endlessSeed, from: snapshot.stats?.walls,
    savedSaturation: snapshot.state === 'grey' ? snapshot.savedSat : snapshot.sat,
    bestCombo: snapshot.comboBest || 0 });
}

export function newEndlessSeed(previous) {
  let seed;
  try { seed = globalThis.crypto.getRandomValues(new Uint32Array(1))[0]; }
  catch { seed = (Date.now() ^ Math.floor(Math.random() * 0xffffffff)) >>> 0; }
  return seed === previous ? (seed + 1) >>> 0 : seed;
}

export function previewCheckpoint(query) {
  if (query.get('endless') !== '1' || (!query.has('seed') && !query.has('board'))) return null;
  const seed = Number(query.get('seed') ?? 2709), board = Number(query.get('board') ?? 1);
  const sat = Number(query.get('sat') ?? .15);
  const savedSaturation = Number.isFinite(sat) ? Math.max(0, Math.min(1, sat)) : .15;
  return validateCheckpoint({ version: 1, seed, from: board - 1, savedSaturation, bestCombo: 0 });
}
