/* ============================================================================
 * door/fall-card.js - the fall as a picture, 1200 x 630: for a channel, a
 * friend, or the player's own wall of shame.
 *
 * What it draws: who you lost to (or beat), where your IQ went and the line it
 * took, the move it left on and the better one, and the board as it stood
 * just before that move, with what you played in pink and the better move
 * ringed. Your own seat only, the same rule as the end card (door/fall.js).
 *
 *   fallCardModel(record) -> the words and squares (pure, node-testable)
 *   drawFallCard(canvas, record)  saveFallCard(record)  copyFallCard(record)
 *
 * Save is a plain <a download> of a PNG blob: the desktop host has no save
 * bridge and lets WebView2 download as a browser would. Copy writes an
 * image/png ClipboardItem, and says false when the page may not.
 * ==========================================================================*/

import { Chess } from '../vendor/chess.js';
import { recapRows, curvePoints, moveNumber } from './fall.js';
import { resultLine } from './replay.js';
import { outcome, fmtMoves } from './store.js';

export const CARD = Object.freeze({ w: 1200, h: 630, file: 'piece-by-piece-fall.png' });

const INK = Object.freeze({ bg0: '#25234f', bg1: '#141430', cream: '#F5E6C8', pink: '#FF69B4', ink: '#E9E2F5',
  light: '#4a4677', dark: '#2d2b55' });
const FONT = '"Segoe UI", system-ui, sans-serif';
const GLYPH = { k: '\u265A', q: '\u265B', r: '\u265C', b: '\u265D', n: '\u265E', p: '\u265F' };   // the filled set, coloured per side

/** The solo opponent's label (solo.js: "Computer", a middle dot, the level) -> "the Beginner computer"; anyone else by name. */
export function opponentWord(name) {
  const m = /^computer\s*[\u00b7:\-]\s*(.+)$/i.exec(String(name || '').trim());
  if (m) return `the ${m[1]} computer`;
  return name ? String(name) : 'a friend';
}

/** "by checkmate", "on time": the reason out of the result line. */
const reasonOf = (record) => resultLine(record || {}).replace(/^(You (won|lost)|White won|Black won|Draw)\s*/, '');

/**
 * The big line: "Lost to the Beginner computer". Two players here read from
 * the seat the picture is about ("Black lost by checkmate"), so the IQ under
 * it is plainly that seat's.
 */
export function fallHeadline(record, side = null) {
  const o = outcome(record);
  const who = opponentWord(record && record.opponent);
  if (o === 'loss') return `Lost to ${who}`;
  if (o === 'win') return `Beat ${who}`;
  if (o === 'draw') return `Drew with ${who}`;
  const winner = record && record.result && record.result.winner;
  if (!winner || (side !== 'w' && side !== 'b')) return resultLine(record || {});
  return [side === 'w' ? 'White' : 'Black', winner === side ? 'won' : 'lost', reasonOf(record)].filter(Boolean).join(' ');
}

/** The small line under it: "by checkmate, 31 moves", or just the moves when the headline gave the reason. */
export function fallSubline(record) {
  const moves = fmtMoves(record && record.plies);
  return outcome(record) ? [reasonOf(record), moves].filter(Boolean).join(', ') : moves;
}

/** The seat the picture is about: yours, or in a two-players-here game the side that lost. */
function rowFor(record) {
  const rows = recapRows(record);
  if (rows.length < 2) return rows[0] || null;
  const winner = record.result && record.result.winner;
  return rows.find((r) => winner && r.side !== winner) || rows[0];
}

/** The board just before the worst move, and the squares of what was played and what was better. */
export function boardBefore(record, worst) {
  try {
    const chess = record.fen ? new Chess(record.fen) : new Chess();
    const moves = Array.isArray(record.moves) ? record.moves : [];
    const upto = Math.max(0, Math.min(moves.length, ((worst && worst.ply) || 1) - 1));
    for (let i = 0; i < upto; i++) chess.move(moves[i]);
    const squares = (san) => {
      if (!san) return null;
      try { const m = new Chess(chess.fen()).move(san); return m ? { from: m.from, to: m.to } : null; } catch { return null; }
    };
    return {
      rows: chess.board().map((rank) => rank.map((p) => (p ? { type: p.type, side: p.color } : null))),
      played: squares(worst && worst.san),
      better: worst && worst.best && worst.best !== worst.san ? squares(worst.best) : null,
    };
  } catch { return null; }
}

/** Everything the picture says, without drawing it. Null when the record has no fall of yours. */
export function fallCardModel(record) {
  const row = record && rowFor(record);
  if (!row) return null;
  return {
    side: row.side, flip: row.side === 'b',
    headline: fallHeadline(record, row.side), subline: fallSubline(record),
    start: row.start, end: row.end, line: row.line, worst: row.worst,
    track: record.iq[row.side],
    board: row.worst ? boardBefore(record, row.worst) : null,
    boardLabel: row.worst ? `move ${moveNumber(row.worst.ply)}, before ${row.worst.san}` : '',
  };
}

/** Shrink a font until the text fits the width. */
function fit(ctx, text, weight, size, maxW) {
  let s = size;
  ctx.font = `${weight} ${s}px ${FONT}`;
  while (s > 12 && ctx.measureText(text).width > maxW) { s -= 2; ctx.font = `${weight} ${s}px ${FONT}`; }
  return s;
}

function drawBoard(ctx, b, x0, y0, size, flip) {
  const sq = size / 8;
  const at = (name) => {
    const f = name.charCodeAt(0) - 97, r = Number(name[1]) - 1;
    return flip ? [x0 + (7 - f) * sq, y0 + r * sq] : [x0 + f * sq, y0 + (7 - r) * sq];
  };
  for (let r = 0; r < 8; r++) for (let f = 0; f < 8; f++) {
    ctx.fillStyle = (r + f) % 2 ? INK.dark : INK.light;
    ctx.fillRect(x0 + f * sq, y0 + r * sq, sq, sq);
  }
  if (b.played) for (const s of [b.played.from, b.played.to]) {
    const [x, y] = at(s);
    ctx.fillStyle = 'rgba(255, 105, 180, 0.55)'; ctx.fillRect(x, y, sq, sq);
  }
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  ctx.font = `${Math.round(sq * 0.78)}px "Segoe UI Symbol", "Noto Sans Symbols 2", "DejaVu Sans", serif`;
  b.rows.forEach((rank, ri) => rank.forEach((p, fi) => {
    if (!p) return;
    const name = String.fromCharCode(97 + fi) + (8 - ri);
    const [x, y] = at(name);
    const g = GLYPH[p.type] + '\uFE0E';
    ctx.lineWidth = p.side === 'w' ? 2 : 1.2;
    ctx.strokeStyle = p.side === 'w' ? INK.bg1 : 'rgba(245, 230, 200, 0.7)';
    ctx.fillStyle = p.side === 'w' ? INK.cream : '#121228';
    ctx.strokeText(g, x + sq / 2, y + sq / 2 + 2);
    ctx.fillText(g, x + sq / 2, y + sq / 2 + 2);
  }));
  if (b.better) for (const s of [b.better.from, b.better.to]) {
    const [x, y] = at(s);
    ctx.strokeStyle = INK.cream; ctx.lineWidth = 3; ctx.strokeRect(x + 3, y + 3, sq - 6, sq - 6);
  }
  ctx.strokeStyle = 'rgba(245, 230, 200, 0.25)'; ctx.lineWidth = 1; ctx.strokeRect(x0 - 0.5, y0 - 0.5, size + 1, size + 1);
}

/** Draw the picture onto `canvas` (resized to 1200 x 630). False when there is nothing to draw. */
export function drawFallCard(canvas, record) {
  const m = fallCardModel(record);
  const ctx = m && canvas && canvas.getContext && canvas.getContext('2d');
  if (!ctx) return false;
  canvas.width = CARD.w; canvas.height = CARD.h;
  const bg = ctx.createLinearGradient(0, 0, CARD.w, CARD.h);
  bg.addColorStop(0, INK.bg0); bg.addColorStop(1, INK.bg1);
  ctx.fillStyle = bg; ctx.fillRect(0, 0, CARD.w, CARD.h);
  const glow = ctx.createRadialGradient(250, 300, 10, 250, 300, 360);
  glow.addColorStop(0, 'rgba(255, 105, 180, 0.16)'); glow.addColorStop(1, 'rgba(255, 105, 180, 0)');
  ctx.fillStyle = glow; ctx.fillRect(0, 0, CARD.w, CARD.h);

  const left = 64, textW = m.board ? 620 : CARD.w - 128;
  ctx.textAlign = 'left'; ctx.textBaseline = 'alphabetic';
  ctx.fillStyle = 'rgba(245, 230, 200, 0.55)'; ctx.font = `500 16px ${FONT}`;
  if ('letterSpacing' in ctx) ctx.letterSpacing = '6px';
  ctx.fillText('PIECE BY PIECE', left, 70);
  if ('letterSpacing' in ctx) ctx.letterSpacing = '0px';
  ctx.fillStyle = INK.cream; fit(ctx, m.headline, 500, 44, textW); ctx.fillText(m.headline, left, 146);
  ctx.fillStyle = 'rgba(233, 226, 245, 0.6)'; fit(ctx, m.subline, 400, 20, textW); ctx.fillText(m.subline, left, 182);

  // IQ 140 -> 61, the end in pink and big
  ctx.fillStyle = 'rgba(245, 230, 200, 0.6)'; ctx.font = `400 34px ${FONT}`;
  const from = `IQ ${m.start}`;
  ctx.fillText(from, left, 292);
  let x = left + ctx.measureText(from).width + 18;
  ctx.fillStyle = 'rgba(245, 230, 200, 0.45)'; ctx.fillText('\u2192', x, 292);
  x += ctx.measureText('\u2192').width + 18;
  ctx.fillStyle = INK.pink; ctx.font = `600 96px ${FONT}`; ctx.fillText(String(m.end), x, 300);

  // the curve
  const box = { x: left, y: 330, w: textW - 40, h: 140 };
  const c = curvePoints(m.track, { w: box.w, h: box.h, pad: 8 });
  ctx.strokeStyle = 'rgba(245, 230, 200, 0.18)'; ctx.lineWidth = 1; ctx.setLineDash([3, 5]);
  ctx.beginPath(); ctx.moveTo(box.x, box.y + box.h - 8); ctx.lineTo(box.x + box.w, box.y + box.h - 8); ctx.stroke();
  ctx.setLineDash([]);
  ctx.strokeStyle = INK.pink; ctx.lineWidth = 4; ctx.lineJoin = 'round'; ctx.lineCap = 'round';
  ctx.beginPath();
  c.points.forEach(([px, py], i) => (i ? ctx.lineTo(box.x + px, box.y + py) : ctx.moveTo(box.x + px, box.y + py)));
  ctx.stroke();
  if (c.worst) {
    ctx.fillStyle = INK.cream; ctx.strokeStyle = INK.pink; ctx.lineWidth = 3;
    ctx.beginPath(); ctx.arc(box.x + c.worst[0], box.y + c.worst[1], 8, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
  }
  ctx.fillStyle = INK.ink; fit(ctx, m.line, 400, 26, textW); ctx.fillText(m.line, left, 548);

  if (m.board) {
    const size = 400, bx = 736, by = 128;
    ctx.fillStyle = 'rgba(245, 230, 200, 0.55)'; ctx.font = `400 16px ${FONT}`; ctx.textAlign = 'left';
    ctx.fillText(m.boardLabel, bx, by - 16);
    drawBoard(ctx, m.board, bx, by, size, m.flip);
  }
  return true;
}

/** The picture as a PNG blob, or null. */
export function fallCardBlob(record) {
  try {
    const canvas = document.createElement('canvas');
    if (!drawFallCard(canvas, record)) return Promise.resolve(null);
    return new Promise((resolve) => { try { canvas.toBlob((b) => resolve(b || null), 'image/png'); } catch { resolve(null); } });
  } catch { return Promise.resolve(null); }
}

/** Download the picture. True when the download was handed to the page. */
export async function saveFallCard(record) {
  const blob = await fallCardBlob(record);
  if (!blob) return false;
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = CARD.file; a.hidden = true;
  document.body.append(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10000);
  return true;
}

/** Put the picture on the clipboard. False when the page may not (no clipboard, no focus, no picture). */
export async function copyFallCard(record) {
  try {
    if (!navigator.clipboard || typeof navigator.clipboard.write !== 'function' || typeof ClipboardItem === 'undefined') return false;
    // the blob is handed over as a promise, so the click's permission is still live when it lands
    const png = fallCardBlob(record).then((b) => b || Promise.reject(new Error('no picture')));
    await navigator.clipboard.write([new ClipboardItem({ 'image/png': png })]);
    return true;
  } catch { return false; }
}
