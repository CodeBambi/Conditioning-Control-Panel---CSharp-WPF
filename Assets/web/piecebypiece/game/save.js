import { Chess } from '../vendor/chess.js';
import { levelOf } from './search.js';
const KEY = 'pbp-solo-current-v1', OPTIONS = 'pbp-solo-options-v1';
export function soloOptions(value = null) {
  let saved = {};
  try { saved = JSON.parse(localStorage.getItem(OPTIONS) || '{}'); } catch { /* fresh defaults */ }
  const source = value || saved || {};
  const result = { level: levelOf(source.level),
    side: ['w', 'b', 'random'].includes(source.side) ? source.side : 'w',
    clockMs: [0, 300000, 900000].includes(Number(source.clockMs)) ? Number(source.clockMs) : 0 };
  if (value) try { localStorage.setItem(OPTIONS, JSON.stringify(result)); } catch { /* optional storage */ }
  return result;
}
// The IQ grades of the game in progress (game/iq.js), so a resumed game keeps its fall. The IQ lane
// hands its reader in once at boot; a page without it saves exactly what it always did.
let iqOf = null;
export function setSoloIq(read) { iqOf = typeof read === 'function' ? read : null; }
const iqNow = () => { try { return iqOf ? iqOf() : null; } catch { return null; } };
export function saveSolo(record) {
  try {
    const iq = iqNow();
    if (!record || record.result) localStorage.removeItem(KEY);
    else localStorage.setItem(KEY, JSON.stringify({ ...record, ...(iq ? { iq } : {}), version: 1, at: Date.now() }));
  } catch { /* a private window can still play */ }
}
/** A grade landed after the last save: put it in the save as it stands. */
export function patchSoloIq() {
  try {
    const iq = iqNow(), saved = iq && JSON.parse(localStorage.getItem(KEY) || 'null');
    if (saved && saved.version === 1) localStorage.setItem(KEY, JSON.stringify({ ...saved, iq }));
  } catch { /* the next save carries it */ }
}
export function readSolo() {
  try {
    const saved = JSON.parse(localStorage.getItem(KEY) || 'null');
    if (!saved || saved.version !== 1 || !['w', 'b'].includes(saved.me) || !Array.isArray(saved.moves) || saved.moves.length > 3000) return null;
    const chess = saved.fen ? new Chess(saved.fen) : new Chess();
    for (const move of saved.moves) chess.move(move);
    return chess.isGameOver() ? null : saved;
  } catch { return null; }
}
