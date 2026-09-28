import { Chess } from '../vendor/chess.js';
const KEY = 'pbp-solo-current-v1', OPTIONS = 'pbp-solo-options-v1';
export function soloOptions(value = null) {
  let saved = {};
  try { saved = JSON.parse(localStorage.getItem(OPTIONS) || '{}'); } catch { /* fresh defaults */ }
  const source = value || saved || {};
  const result = { level: ['relaxed', 'club', 'sharp'].includes(source.level) ? source.level : 'club',
    side: ['w', 'b', 'random'].includes(source.side) ? source.side : 'w',
    clockMs: [0, 300000, 900000].includes(Number(source.clockMs)) ? Number(source.clockMs) : 0 };
  if (value) try { localStorage.setItem(OPTIONS, JSON.stringify(result)); } catch { /* optional storage */ }
  return result;
}
export function saveSolo(record) {
  try {
    if (!record || record.result) localStorage.removeItem(KEY);
    else localStorage.setItem(KEY, JSON.stringify({ ...record, version: 1, at: Date.now() }));
  } catch { /* a private window can still play */ }
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
