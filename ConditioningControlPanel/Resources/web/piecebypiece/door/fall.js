/* ============================================================================
 * door/fall.js - the fall, read off the IQ track: what the end card says about
 * where your head went, and the numbers the share card draws.
 *
 * Pure: no DOM, no window, and nothing throws on a missing field. door.js
 * paints the strings; a record with no `iq` (an old save, a game from before
 * the grader) reads as nothing at all.
 *
 *   record.iq = { w?: Track, b?: Track }
 *   Track     = { start, end, low, worst, moves: Grade[] }
 *   Grade     = { ply, side, san, best, cp, loss, value }   (game/iq.js)
 *
 * YOUR OWN SEAT ONLY. Solo and online show `record.me` and nothing else, even
 * if a track for the other seat is somehow in the record: the opponent's head
 * is theirs (owner, 2026-10-01). A two-players-here game (me null) shows both
 * seats, a row each. An online or solo game with no known seat shows nothing.
 * ==========================================================================*/

export const FALL = Object.freeze({
  floor: 40,                                          // the bottom of every curve: IQ's own floor (game/iq.js)
  curve: Object.freeze({ w: 220, h: 54, pad: 5 }),    // the end card's line, in SVG units
  keep: Object.freeze(['ply', 'side', 'san', 'best', 'cp', 'loss', 'value']),   // what a saved grade carries
});

const SIDES = ['w', 'b'];
const num = (v) => (Number.isFinite(v) ? v : null);

function esc(s) { return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

/** A track the card can read: a start and a list of grades. */
export function isTrack(t) { return !!t && typeof t === 'object' && Number.isFinite(t.start) && Array.isArray(t.moves); }

/** The seats a record's recap shows (see the header). */
export function seatsShown(record) {
  const iq = record && record.iq;
  if (!iq || typeof iq !== 'object') return [];
  const me = record.me === 'w' || record.me === 'b' ? record.me : null;
  if (!me && record.mode !== 'hotseat') return [];
  return (me ? [me] : SIDES).filter((s) => isTrack(iq[s]));
}

/** The full-move number of the move that made the game `ply` half-moves long. */
export const moveNumber = (ply) => Math.max(1, Math.ceil((Number(ply) || 0) / 2));

/** The grade that cost the most; a bigger cp breaks a tie, then the earlier move (game/iq.js
 *  picks the same way). Null when nothing was lost. */
export function worstOf(moves) {
  let worst = null;
  for (const m of Array.isArray(moves) ? moves : []) {
    if (!m || !(m.loss > 0)) continue;
    const cp = Number(m.cp) || 0, wcp = worst ? Number(worst.cp) || 0 : 0;
    if (!worst || m.loss > worst.loss || (m.loss === worst.loss && (cp > wcp || (cp === wcp && m.ply < worst.ply)))) worst = m;
  }
  return worst;
}

/** "Gone at move 19: Qxd5. Better: Nf3." Better is left out when the move was the grader's own pick. */
export function goneLine(worst) {
  if (!worst || !(worst.loss > 0) || !worst.san) return 'Not one point lost.';
  const better = worst.best && worst.best !== worst.san ? ` Better: ${worst.best}.` : '';
  return `Gone at move ${moveNumber(worst.ply)}: ${worst.san}.${better}`;
}

/** Where IQ stands after the last grade, whatever the track calls it. */
export function endOf(t) {
  if (!isTrack(t)) return null;
  const last = t.moves.length ? num(t.moves[t.moves.length - 1].value) : null;
  return num(t.end) ?? num(t.value) ?? last ?? t.start;
}

/**
 * The curve: one point for the start and one per graded move, on a fixed
 * axis from the start down to IQ's floor, so a ten-point slip looks small
 * and a cliff looks like one. Returns SVG-ready numbers.
 */
export function curvePoints(track, { w = FALL.curve.w, h = FALL.curve.h, pad = FALL.curve.pad } = {}) {
  const moves = isTrack(track) ? track.moves.filter((m) => m && Number.isFinite(m.value)) : [];
  const start = isTrack(track) ? track.start : 0;
  const values = [start, ...moves.map((m) => m.value)];
  const top = Math.max(...values);
  const bottom = Math.min(FALL.floor, ...values);
  const span = top - bottom || 1;
  const n = values.length;
  const x = (i) => (n === 1 ? pad : pad + (i * (w - 2 * pad)) / (n - 1));
  const y = (v) => pad + ((top - v) / span) * (h - 2 * pad);
  const points = values.map((v, i) => [Math.round(x(i) * 10) / 10, Math.round(y(v) * 10) / 10]);
  // the dot goes where the track says its worst move was, else where the grades say
  const worst = (track && track.worst && track.worst.loss > 0) ? track.worst : worstOf(moves);
  const wi = worst ? moves.findIndex((m) => m.ply === worst.ply) + 1 : -1;
  return {
    w, h, points,
    path: points.map(([px, py], i) => `${i ? 'L' : 'M'}${px} ${py}`).join(' '),
    worst: wi > 0 ? points[wi] : null,
  };
}

/** One row per shown seat: the numbers and the line, ready to paint. */
export function recapRows(record) {
  return seatsShown(record).map((side) => {
    const t = record.iq[side];
    const end = endOf(t);
    const worst = (t.worst && t.worst.loss > 0) ? t.worst : worstOf(t.moves);
    const low = num(t.low) ?? Math.min(t.start, end, ...t.moves.map((m) => num(m && m.value) ?? t.start));
    return { side, start: Math.round(t.start), end: Math.round(end), low: Math.round(low), worst, line: goneLine(worst), curve: curvePoints(t) };
  });
}

/** The end card's recap, as markup for #door-recap ('' when there is nothing to say). */
export function recapHtml(record, { extra = '' } = {}) {
  const rows = recapRows(record);
  if (!rows.length) return '';
  const two = rows.length > 1;
  const body = rows.map((r) => {
    const c = r.curve;
    const dot = c.worst ? `<circle cx="${c.worst[0]}" cy="${c.worst[1]}" r="3.5"/>` : '';
    const who = two ? `<span class="fall-who">${r.side === 'w' ? 'white' : 'black'}</span>` : '';
    return `<div class="fall-row" data-side="${r.side}">${who}
      <p class="fall-iq"><span class="from">IQ ${r.start}</span> <span class="arrow">&rarr;</span> <b class="to">${r.end}</b></p>
      <svg class="fall-curve" viewBox="0 0 ${c.w} ${c.h}" aria-hidden="true"><line class="base" x1="0" y1="${c.h - FALL.curve.pad}" x2="${c.w}" y2="${c.h - FALL.curve.pad}"/><path d="${c.path}" pathLength="1"/>${dot}</svg>
      <p class="fall-line">${esc(r.line)}</p></div>`;
  }).join('');
  return `<div class="fall${two ? ' two' : ''}">${body}${extra}</div>`;
}

/**
 * "Watch the fall": the review from the position before your worst move,
 * through the capture that punished it (the first capture among the
 * opponent's next two moves), or through their one reply when nothing was
 * taken. Your own seat only: null for two players here, and for no loss.
 */
export function fallPlan(record) {
  const me = record && (record.me === 'w' || record.me === 'b') ? record.me : null;
  const t = me && record.iq ? record.iq[me] : null;
  if (!isTrack(t)) return null;
  const worst = (t.worst && t.worst.loss > 0) ? t.worst : worstOf(t.moves);
  const moves = Array.isArray(record.moves) ? record.moves : [];
  if (!worst || !(worst.ply >= 1) || worst.ply > moves.length) return null;
  const punish = [worst.ply + 1, worst.ply + 3].find((p) => p <= moves.length && String(moves[p - 1]).includes('x')) || null;
  return { from: worst.ply - 1, to: punish || Math.min(moves.length, worst.ply + 1), punish, worst };
}

/** A grade with only the fields a saved record keeps. */
function plainGrade(g) {
  if (!g || typeof g !== 'object') return null;
  const out = {};
  for (const k of FALL.keep) if (g[k] !== undefined) out[k] = g[k];
  return out;
}

/** A record's `iq`, trimmed to what the shelf keeps; null when there is no track in it. */
export function plainIq(iq) {
  if (!iq || typeof iq !== 'object') return null;
  const out = {};
  for (const side of SIDES) {
    const t = iq[side];
    if (!isTrack(t)) continue;
    const moves = t.moves.map(plainGrade).filter(Boolean);
    const track = { start: t.start, end: endOf(t), low: num(t.low), worst: plainGrade(t.worst && t.worst.loss > 0 ? t.worst : worstOf(moves)), moves };
    if (track.low == null) track.low = Math.min(track.start, track.end, ...moves.map((m) => num(m.value) ?? track.start));
    out[side] = track;
  }
  return Object.keys(out).length ? out : null;
}

/** Read the live grader (window.PBP.iq) into a record's `iq`. Null when it is missing or empty. */
export function iqFromApi(api) {
  try {
    if (!api || typeof api.sides !== 'function' || typeof api.track !== 'function') return null;
    const raw = {};
    for (const side of api.sides() || []) {
      if (side !== 'w' && side !== 'b') continue;
      const t = api.track(side);
      if (!t) continue;
      const worst = typeof api.worst === 'function' ? api.worst(side) : null;
      raw[side] = { ...t, end: num(t.value) ?? num(t.end), worst };
    }
    return plainIq(raw);
  } catch { return null; }
}

/**
 * The fall to keep once the grader has settled (door.js settleIq): the live
 * grader's while it still holds this game, else what the record carried at the
 * end. `epoch` is the grader's epoch() as the game ended; the menu, a rematch or
 * a new deal moves it on and empties the trackers, and an empty fall must never
 * be saved over a real one.
 */
export function keptIq(api, epoch, fromRecord, ours = true) {
  let same = !!ours;
  try { if (same && epoch != null) same = api.epoch() === epoch; } catch { same = false; }
  return (same && iqFromApi(api)) || plainIq(fromRecord);
}
