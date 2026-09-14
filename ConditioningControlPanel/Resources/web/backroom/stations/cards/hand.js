/* ============================================================================
 * stations/cards/hand.js - Soft Hand, pure (CONTRACT 10.13.E and F).
 *
 * Reads the server's publicHand and state bodies, decides which controls are
 * live, sorts a reply into what the page does next (the same idem on a busy
 * or a lost reply, the returned hand adopted on stale, illegal and hand_open)
 * and holds Law I: a settled hand's return is owed until the page shows it.
 *
 * Never the rules: legal moves, the hint, totals and pays all come from the
 * server. The only arithmetic here is a display total for the cards that are
 * face up on the felt while the dealer's hand is still being turned.
 * ==========================================================================*/

export const MOVES = Object.freeze(['hit', 'stand', 'double', 'split']);
export const IDEM_RE = /^[A-Za-z0-9_-]{16,64}$/;
export const HINT_KEY = 'br_cards_hint';
/** Below this balance the bet chip starts on the smallest stake (owner decision, 10.13.F). */
export const LOW_SP = 30;
export const RETRY = Object.freeze({ busyMs: 650, timeoutMs: 250, tries: 3, fastTries: 2, fastCapMs: 9000 });

const CODE_RE = /^[A2-9TJQK][shdc]$/;
const SUIT = { s: { glyph: '♠', red: false }, h: { glyph: '♥', red: true }, d: { glyph: '♦', red: true }, c: { glyph: '♣', red: false } };
const OUTCOMES = new Set(['win', 'lose', 'push', 'bust', 'blackjack', 'charlie']);

const int = (v, d = 0) => (Number.isFinite(Number(v)) ? Math.trunc(Number(v)) : d);

export const validCode = (c) => typeof c === 'string' && CODE_RE.test(c);
/** Blackjack value of a card code: A 1, T J Q K 10. */
export function cardValue(code) {
  if (!validCode(code)) return 0;
  const r = code[0];
  return r === 'A' ? 1 : 'TJQK'.includes(r) ? 10 : Number(r);
}
/** The rank printed on the card face: 'A', '2'..'10', 'J', 'Q', 'K'. */
export const rankLabel = (code) => (validCode(code) ? (code[0] === 'T' ? '10' : code[0]) : '');
export const suitOf = (code) => (validCode(code) ? SUIT[code[1]] : null);

/** A display total for face-up cards (an ace counts 11 while that does not bust). */
export function totalOf(cards) {
  let hard = 0, ace = false;
  for (const c of cards || []) { const v = cardValue(c); hard += v; if (v === 1) ace = true; }
  return ace && hard + 10 <= 21 ? { total: hard + 10, soft: true } : { total: hard, soft: false };
}

function readResult(r, n) {
  if (!r || typeof r !== 'object' || !Array.isArray(r.hands) || r.hands.length !== n) return null;
  const hands = r.hands.map((h) => ({
    outcome: OUTCOMES.has(h && h.outcome) ? h.outcome : 'lose',
    bet: Math.max(0, int(h && h.bet)), paid: Math.max(0, int(h && h.paid)), total: int(h && h.total),
  }));
  return { hands, dealerTotal: int(r.dealerTotal), dealerBlackjack: r.dealerBlackjack === true,
    wagered: int(r.wagered), returned: Math.max(0, int(r.returned)), net: int(r.net) };
}

/** A publicHand, checked. Null for anything that is not one (the page then shows an empty table). */
export function readHand(h) {
  if (!h || typeof h !== 'object' || typeof h.id !== 'string' || !h.id) return null;
  if (!Array.isArray(h.dealer) || !h.dealer.length || !h.dealer.every(validCode)) return null;
  if (!Array.isArray(h.hands) || h.hands.length < 1 || h.hands.length > 2) return null;
  const hands = [];
  for (const x of h.hands) {
    if (!x || !Array.isArray(x.cards) || !x.cards.length || !x.cards.every(validCode)) return null;
    hands.push({ cards: x.cards.slice(), bet: Math.max(0, int(x.bet)), done: x.done === true, doubled: x.doubled === true, split: x.split === true });
  }
  const done = h.done === true;
  const result = done ? readResult(h.result, hands.length) : null;
  if (done && !result) return null;
  return { id: h.id, step: Math.max(0, int(h.step)), stake: Math.max(0, int(h.stake)), dealer: h.dealer.slice(), hands,
    active: Math.min(hands.length - 1, Math.max(0, int(h.active))), done, result };
}

export const legalOf = (legal) => (Array.isArray(legal) ? MOVES.filter((m) => legal.includes(m)) : []);

/** GET state (or any body that carries hand, legal, hint). Missing fields read as a table with no hand. */
export function readState(body) {
  const b = body && typeof body === 'object' ? body : {};
  const rules = b.rules && typeof b.rules === 'object' ? b.rules : {};
  const stakes = Array.isArray(rules.stakes) && rules.stakes.length ? rules.stakes.map((s) => int(s)).filter((s) => s > 0) : [1, 2];
  return {
    sp: Math.max(0, int(b.sp)),
    open: b.open !== false,
    hand: readHand(b.hand),
    legal: legalOf(b.legal),
    hint: MOVES.includes(b.hint) ? b.hint : null,
    autoStandAt: typeof b.autoStandAt === 'string' ? b.autoStandAt : null,
    rules: { ...rules, stakes: stakes.length ? stakes : [1, 2] },
    floorMs: Math.max(0, int(b.floorMs, 8000)),
  };
}

export const isOpen = (hand) => !!hand && !hand.done;

/** The bet chip a sit-down starts on: the smallest stake below LOW_SP, else the largest the balance covers. */
export function defaultStake(sp, stakes = [1, 2]) {
  const s = stakes.slice().sort((a, b) => a - b);
  if (!s.length) return 1;
  if (!(Number(sp) >= LOW_SP)) return s[0];
  const fit = s.filter((x) => x <= sp);
  return fit.length ? fit[fit.length - 1] : s[0];
}

/**
 * Which controls are live. `screenUntil`: a fullscreen moment (bloom, win wash, losing edges) runs until then, and the
 * next deal is held ('screen') so nothing fullscreen can overlap a new decision.
 * @param {{phase:string, hand:Object|null, legal:string[], sp:number, stake:number, busy:boolean, animating:boolean, dealReadyAt:number, screenUntil:number, now:number}} o
 */
export function controls({ phase, hand, legal = [], sp = 0, stake = 1, busy = false, animating = false, dealReadyAt = 0, screenUntil = 0, now = 0 }) {
  const table = phase === 'play' && !busy && !animating;
  const open = isOpen(hand);
  // the moment reads first, even while the reply is still being laid down, so the button says why it is off
  const why = now < screenUntil ? 'screen' : !table ? 'busy' : open ? 'open' : sp < stake ? 'sp' : now < dealReadyAt ? 'floor' : null;
  const moves = {};
  for (const m of MOVES) moves[m] = table && open && legal.includes(m);
  return { deal: why === null, dealWhy: why, bet: table && !open, sit: table && !open, moves };
}

/** POST hit | stand | double | split body (the idem is added by the intent). */
export const moveBody = (hand) => ({ handId: hand.id, step: hand.step });

/**
 * What a station-request answer means for the page.
 *   ok        the body is a success receipt
 *   retry     same idem after waitMs (busy, a lost reply)
 *   wait      same idem after waitMs (too_fast: the deal floor or the limiter)
 *   adopt     the body carries the server's hand (stale, illegal, hand_open, auto_stood): show it
 *   refresh   read state again (no_hand; bad_request, a body this page shaped wrong)
 *   closed    the door is shut, or the host has no such op (bad_op)
 *   insufficient, failed
 */
export function classify(res) {
  const body = res && res.body && typeof res.body === 'object' ? res.body : null;
  if (res && res.ok && res.status === 403) return { kind: 'closed', reason: 'closed', body };
  if (res && res.ok && body && body.ok === true) return { kind: 'ok', body };
  const reason = String((body && body.reason) || (res && res.reason) || 'offline');
  switch (reason) {
    case 'closed': case 'bad_op': return { kind: 'closed', reason, body };
    case 'busy': return { kind: 'retry', reason, waitMs: RETRY.busyMs, body };
    case 'timeout': return { kind: 'retry', reason, waitMs: RETRY.timeoutMs, body };
    case 'too_fast': return { kind: 'wait', reason, waitMs: Math.min(RETRY.fastCapMs, Math.max(250, int(body && body.retryInMs, 1000))), body };
    case 'stale': case 'illegal': case 'hand_open': case 'auto_stood': return { kind: 'adopt', reason, body };
    case 'no_hand': case 'bad_request': return { kind: 'refresh', reason, body };
    case 'insufficient': return { kind: 'insufficient', reason, body };
    default: return { kind: 'failed', reason, body };
  }
}

/** One intent (a press): its idem is minted once and reused verbatim on every retry. */
export function createIntent(op, body, mint) {
  const idem = String(mint());
  return { op, idem, body: { ...(body || {}), idem }, tries: 0, fastTries: 0 };
}

/** May this intent go again after `c` (a classify result)? Counts the try. */
export function mayRetry(intent, c) {
  if (c.kind === 'retry') return ++intent.tries <= RETRY.tries;
  if (c.kind === 'wait') return ++intent.fastTries <= RETRY.fastTries;
  return false;
}

/**
 * LAW I. What a reply credited for the hand it carries, held back until that hand's settle frame shows.
 * Never more than the balance actually moved up by (a capped credit), never the auto-stood return of an older hand.
 */
export function owedFor(body) {
  const hand = readHand(body && body.hand);
  if (!hand || !hand.done) return 0;
  const credited = int(body.sp) - (int(body.spBefore) - int(body.cost));
  return Math.max(0, Math.min(hand.result.returned, credited));
}
export const shownSp = (sp, owed) => Math.max(0, int(sp) - Math.max(0, int(owed)));

/** The hint preference (off by default). Storage can throw or be missing; both read as off. */
export function readHintPref(storage) {
  try { return !!storage && storage.getItem(HINT_KEY) === '1'; } catch (e) { return false; }
}
export function writeHintPref(storage, on) {
  try { if (storage) { if (on) storage.setItem(HINT_KEY, '1'); else storage.removeItem(HINT_KEY); } } catch (e) { /* noop */ }
}
