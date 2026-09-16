/* ============================================================================
 * door/store.js - the shelf of past games, and what the profile reads off it.
 *
 * Plain localStorage under one key, newest first, capped. Nothing here is
 * authoritative over anything: the host owns XP and rating, and when the
 * server starts keeping games this shelf becomes a cache of them. Every
 * function degrades to an empty shelf in a private window.
 *
 *   Game = { id, at, mode: 'hotseat' | 'online', me: 'w' | 'b' | null,
 *            opponent, moves: [san], plies, result: { result, winner, reason } | null,
 *            durationMs, captures: { w, b } }
 * ==========================================================================*/

export const STORE = Object.freeze({ key: 'pbp-games', cap: 50, nameKey: 'pbp-name' });

function read() {
  try {
    const raw = window.localStorage.getItem(STORE.key);
    const list = raw ? JSON.parse(raw) : [];
    return Array.isArray(list) ? list : [];
  } catch { return []; }
}

function write(list) {
  try { window.localStorage.setItem(STORE.key, JSON.stringify(list.slice(0, STORE.cap))); } catch { /* no shelf */ }
}

export function listGames() { return read(); }

export function getGame(id) { return read().find((g) => g.id === id) || null; }

export function saveGame(game) {
  const list = read();
  const rec = { id: game.id || ('g-' + Date.now().toString(36)), at: game.at || new Date().toISOString(), ...game };
  write([rec, ...list.filter((g) => g.id !== rec.id)]);
  return rec;
}

export function clearGames() { write([]); }

/** The player's own handle: what the host sent, else what they typed, else "you". */
export function playerName(settings) {
  if (settings && settings.playerName) return String(settings.playerName);
  try { return window.localStorage.getItem(STORE.nameKey) || 'you'; } catch { return 'you'; }
}

export function setPlayerName(name) {
  try { window.localStorage.setItem(STORE.nameKey, String(name || '').slice(0, 24)); } catch { /* no shelf */ }
}

/** Won / lost / drawn from the player's seat; null when the seat is unknown (hotseat). */
export function outcome(g) {
  if (!g || !g.result) return null;
  if (!g.result.winner) return 'draw';
  if (!g.me) return null;
  return g.result.winner === g.me ? 'win' : 'loss';
}

const PIECE_WORD = { p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen', k: 'king' };

/** The counts the profile shows. ELO is deliberately absent: the server rates, or nobody does. */
export function profileStats(list = read()) {
  let wins = 0, losses = 0, draws = 0, captures = 0, plies = 0, ms = 0;
  let streak = 0, streakOpen = true;
  const moved = { p: 0, n: 0, b: 0, r: 0, q: 0, k: 0 };
  for (const g of list) {
    const o = outcome(g);
    if (o === 'win') wins++; else if (o === 'loss') losses++; else if (o === 'draw') draws++;
    if (streakOpen) { if (o === 'win') streak++; else if (o === 'loss' || o === 'draw') streakOpen = false; }
    plies += g.plies || 0;
    ms += g.durationMs || 0;
    const caps = g.captures || {};
    captures += g.me ? (caps[g.me] || 0) : ((caps.w || 0) + (caps.b || 0));
    const moves = Array.isArray(g.moves) ? g.moves : [];
    moves.forEach((san, i) => {
      const side = i % 2 === 0 ? 'w' : 'b';
      if (g.me && side !== g.me) return;
      const c = san[0];
      const type = 'NBRQK'.includes(c) ? c.toLowerCase() : (san.startsWith('O') ? 'k' : 'p');
      moved[type]++;
    });
  }
  const fav = Object.entries(moved).sort((a, b) => b[1] - a[1])[0];
  return {
    games: list.length, wins, losses, draws, streak, captures, plies, ms,
    favourite: fav && fav[1] > 0 ? PIECE_WORD[fav[0]] : null,
    rating: null,   // "unrated" until the server says otherwise
  };
}

/** "4:12" for a duration, "31 moves" for plies, "today 21:04" for a date. */
export function fmtDuration(ms) {
  const s = Math.max(0, Math.round((ms || 0) / 1000));
  return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
}
export function fmtMoves(plies) { const n = Math.ceil((plies || 0) / 2); return n + (n === 1 ? ' move' : ' moves'); }
export function fmtWhen(iso, now = new Date()) {
  const d = new Date(iso);
  if (isNaN(d)) return '';
  const hm = String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
  const sameDay = d.toDateString() === now.toDateString();
  if (sameDay) return 'today ' + hm;
  const y = new Date(now); y.setDate(y.getDate() - 1);
  if (d.toDateString() === y.toDateString()) return 'yesterday ' + hm;
  return d.toLocaleDateString(undefined, { day: 'numeric', month: 'short' }) + ' ' + hm;
}
