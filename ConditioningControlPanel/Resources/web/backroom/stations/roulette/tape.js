/* ============================================================================
 * tape.js - the Velvet Vortex bets and tape, PURE (CONTRACT 10.13.E and F).
 * No DOM, no canvas, no fetch: node:test drives it against mock-server.js.
 *
 * The server owns every rule that moves SP (TABLE_V1 in backroom-roulette.js):
 * the pays, the wake, the stake cap and the cover-all refusal. This file only
 *   - builds a layout from chips placed on the mat (whole SP, at most 3 a spin),
 *   - runs a client cover-all check that ONLY disables Spin and says why, with
 *     the server's own reason word (`covers_all`); the server still decides,
 *   - reads a settled outcome for the text readout (which spots the pocket hit),
 *   - keeps Law I: shownSp = sp - the pays still unplayed on the tape,
 *   - says what a station-result means: go, retry with the SAME idem, adopt an
 *     unplayed tape, or a refusal to show.
 * Spot names come from state.spots and pocket colours from state.rose; the rows
 * of twelve are the contract's own names (sip 1-12, sink 13-24, deep 25-36).
 * ==========================================================================*/

export const MAX_CHIPS = 3;   // TABLE_V1.maxStake: whole SP per spin, all spots together
export const MAX_SPINS = 5;   // TABLE_V1.maxTape: spins of one layout per request
export const ROWS = Object.freeze({ sip: Object.freeze([1, 12]), sink: Object.freeze([13, 24]), deep: Object.freeze([25, 36]) });
export const IDEM_RE = /^[A-Za-z0-9_-]{16,64}$/;

const RETRY_MS = Object.freeze({ busy: 650, timeout: 250, offline: 250 });
export const MAX_TRIES = 3;
const TOO_FAST_WAIT_MAX = 6500;   // a floor longer than this is shown as text, not waited on

export function mintId() {
  const bytes = new Uint8Array(16);
  globalThis.crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

const range = (a, b) => Array.from({ length: b - a + 1 }, (_, i) => a + i);
const roseSet = (rose) => new Set((Array.isArray(rose) ? rose : []).map(Number));

/** 'straight' | 'color' | 'row' | null, from the spot id alone. */
export function spotKind(spot) {
  const s = String(spot);
  if (/^s(\d|[12]\d|3[0-6])$/.test(s)) return 'straight';
  if (s === 'rose' || s === 'plum') return 'color';
  if (Object.prototype.hasOwnProperty.call(ROWS, s)) return 'row';
  return null;
}

/** The pockets a spot covers. Rose from state.rose; plum is every other number 1-36. */
export function coversOf(spot, rose) {
  const kind = spotKind(spot);
  if (kind === 'straight') return [Number(String(spot).slice(1))];
  if (kind === 'row') return range(ROWS[spot][0], ROWS[spot][1]);
  if (kind === 'color') {
    const r = roseSet(rose);
    return range(1, 36).filter((n) => (spot === 'rose' ? r.has(n) : !r.has(n)));
  }
  return [];
}

/** Every number 1-36 covered by the union of the layout (zero never counts). */
export function coversAll(bets, rose) {
  const hit = new Set();
  for (const b of bets || []) for (const n of coversOf(b.spot, rose)) if (n > 0) hit.add(n);
  return hit.size === 36;
}

export const chipTotal = (chips) => Object.values(chips || {}).reduce((s, n) => s + (Number(n) || 0), 0);

/** One more chip on `spot`. Refuses past MAX_CHIPS in all, or a spot the table does not have. */
export function addChip(chips, spot, { spots = null, max = MAX_CHIPS } = {}) {
  const c = { ...(chips || {}) };
  if (!spotKind(spot) || (Array.isArray(spots) && !spots.includes(spot))) return { chips: c, ok: false, why: 'unknown_spot' };
  if (chipTotal(c) >= max) return { chips: c, ok: false, why: 'stake_cap' };
  c[spot] = (c[spot] || 0) + 1;
  return { chips: c, ok: true };
}

/** One chip off `spot` (the spot goes when it has none). */
export function removeChip(chips, spot) {
  const c = { ...(chips || {}) };
  if (c[spot] > 1) c[spot] -= 1; else delete c[spot];
  return c;
}

/** chips {spot: amt} -> bets [{spot, amt}] in the table's spot order (state.spots). */
export function betsOf(chips, spots) {
  const order = Array.isArray(spots) ? spots : [];
  const idx = (s) => { const i = order.indexOf(s); return i < 0 ? 1e6 : i; };
  return Object.entries(chips || {}).filter(([, n]) => n > 0).map(([spot, amt]) => ({ spot, amt }))
    .sort((a, b) => idx(a.spot) - idx(b.spot) || (a.spot < b.spot ? -1 : 1));
}

export const chipsOf = (bets) => Object.fromEntries((bets || []).map((b) => [b.spot, b.amt]));

/**
 * Can Spin be pressed? { ok, why, stake, cost }. `why`: 'empty' | 'covers_all' (the server's word) |
 * 'insufficient'. Only a hint for the button: the server decides every layout.
 */
export function checkLayout(chips, { rose, count = 1, sp = Infinity } = {}) {
  const bets = Object.entries(chips || {}).filter(([, n]) => n > 0).map(([spot, amt]) => ({ spot, amt }));
  const stake = chipTotal(chips);
  const n = Math.max(1, Math.min(MAX_SPINS, Math.trunc(Number(count) || 1)));
  const cost = stake * n;
  if (!bets.length) return { ok: false, why: 'empty', stake, cost };
  if (coversAll(bets, rose)) return { ok: false, why: 'covers_all', stake, cost };
  if (Number(sp) < cost) return { ok: false, why: 'insufficient', stake, cost };
  return { ok: true, why: null, stake, cost };
}

/** A tape from a receipt, a state read or a tape_unplayed refusal, or null. */
export function adoptTape(t) {
  if (!t || typeof t !== 'object' || typeof t.id !== 'string' || !Array.isArray(t.outcomes) || !t.outcomes.length) return null;
  const outcomes = t.outcomes.map((o, i) => ({
    i: Number.isInteger(o && o.i) ? o.i : i,
    pocket: Math.trunc(Number(o && o.pocket)) || 0,
    color: String((o && o.color) || ''),
    wake: !!(o && o.wake),
    pay: Math.max(0, Math.trunc(Number(o && o.pay) || 0)),
  }));
  const played = Math.max(0, Math.min(outcomes.length, Math.trunc(Number(t.played) || 0)));
  const bets = Array.isArray(t.bets) ? t.bets.filter((b) => b && spotKind(b.spot)).map((b) => ({ spot: b.spot, amt: Math.trunc(Number(b.amt) || 0) })) : [];
  return { id: t.id, bets, played, outcomes };
}

/** The pays still on the tape after `played` (Law I). */
export function owed(tape) {
  if (!tape) return 0;
  return tape.outcomes.slice(tape.played).reduce((s, o) => s + o.pay, 0);
}

/** Law I: what the SP readout shows. */
export const shownSp = (sp, tape) => Math.max(0, (Number(sp) || 0) - owed(tape));

/** Where the cursor stands, for `cursor` in the next spin and the flush on close. Null when fully played or none. */
export function cursorOf(tape) {
  return tape && tape.played > 0 ? { tapeId: tape.id, played: tape.played } : null;
}

/** The pocket's colour name: 'zero' | 'rose' | 'plum'. */
export const colorName = (pocket, rose) => (Number(pocket) === 0 ? 'zero' : roseSet(rose).has(Number(pocket)) ? 'rose' : 'plum');

/** The row of twelve a pocket sits in, or null for zero. */
export function rowOf(pocket) {
  const n = Number(pocket);
  for (const [id, [a, b]] of Object.entries(ROWS)) if (n >= a && n <= b) return id;
  return null;
}

/**
 * A settled outcome, read for the page: which of the layout's spots the pocket hit, whether one of them is the
 * straight on that pocket, and the wheel index to paint. Never recomputes `pay`: that is the server's.
 */
export function readOutcome(o, bets, { rose, wheel } = {}) {
  const pocket = Number(o.pocket);
  const hits = (bets || []).filter((b) => coversOf(b.spot, rose).includes(pocket)).map((b) => b.spot);
  const index = Array.isArray(wheel) ? wheel.map(Number).indexOf(pocket) : -1;
  return { i: o.i, pocket, color: colorName(pocket, rose), row: rowOf(pocket), wake: !!o.wake, pay: o.pay,
    hits, straight: hits.includes('s' + pocket), index };
}

/**
 * What a station-result means.
 *   { kind: 'ok', body }                          a receipt
 *   { kind: 'retry', waitMs, reason }             the SAME idem again after waitMs (busy, timeout, offline, a short too_fast)
 *   { kind: 'tape', tape, body }                  tape_unplayed: adopt and play that tape, nothing was bought
 *   { kind: 'closed' }                            the door (403, or a host 'closed')
 *   { kind: 'refused', reason, why?, body }       show it as text
 * `tries` is how many sends this intent has had; after MAX_TRIES a retryable answer is a refusal.
 */
export function classify(res, tries = 1) {
  const body = res && res.body && typeof res.body === 'object' ? res.body : null;
  if (res && res.ok && res.status === 403) return { kind: 'closed' };
  if (res && res.ok && body && body.ok) return { kind: 'ok', body };
  const reason = (body && body.reason) || (res && res.reason) || 'offline';
  if (reason === 'closed') return { kind: 'closed' };
  if (reason === 'tape_unplayed') {
    const tape = adoptTape(body && body.tape);
    if (tape) return { kind: 'tape', tape, body };
  }
  const spent = tries >= MAX_TRIES;
  if (RETRY_MS[reason] && !spent) return { kind: 'retry', waitMs: RETRY_MS[reason], reason };
  if (reason === 'too_fast' && !spent) {
    const ms = Math.ceil(Number(body && body.retryInMs) || 1000);
    if (ms <= TOO_FAST_WAIT_MAX) return { kind: 'retry', waitMs: ms + 60, reason };
  }
  return { kind: 'refused', reason, why: body && body.why ? String(body.why) : undefined, body: body || {} };
}

/** The spin body for this intent. The cursor of a played tape rides along (the server folds it in first). */
export function spinBody({ idem, count, chips, spots, tape }) {
  const body = { idem, count: Math.max(1, Math.min(MAX_SPINS, Math.trunc(Number(count) || 1))), bets: betsOf(chips, spots) };
  const cur = cursorOf(tape);
  if (cur) body.cursor = cur;
  return body;
}
