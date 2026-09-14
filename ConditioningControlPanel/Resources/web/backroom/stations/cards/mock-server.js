/* mock-server.js - a stand-in for /v2/backroom/cards/* for dev.html, the node tests and cards-check.mjs. NOT the
 * table (that is CCP-Server backroom-cards.js RULES_V1 behind backroom-cards-routes.js): a small engine written from
 * the rule list in CONTRACT 10.13.E so the page meets every body shape and refusal it must cope with.
 *   GET state        -> { ok, sp, open, hand, legal, hint, autoStandAt, rules, floorMs }
 *   POST deal        { idem, stake }             -> { ok, idem, sp, spBefore, cost, returned, capped, hand, legal, hint, autoStood?, autoStandAt }
 *   POST hit|stand|double|split { idem, handId, step } -> the same success shape
 *   refusals         closed (403), bad_request, hand_open, no_hand, stale, illegal, insufficient, too_fast, auto_stood, busy
 * Shoes are scripted fixtures: script('As', '9d', 'Kh', '7c', ...) sets the next hand's cards in deal order (player,
 * dealer up, player, dealer hole, then draws); unscripted cards come from a seeded stream. The hint here is a rough
 * stand-in, never the server's strategy table. handle() resolves like the host relay: {ok:true, status, body} or a
 * host refusal {ok:false, reason}. */

const CAP = 99999, CHARLIE = 6, DAY_MS = 86400000, IDEM = /^[A-Za-z0-9_-]{16,64}$/;
export const MOCK_RULES = Object.freeze({ v: 1, decks: 6, dealerHitsSoft17: false, blackjackPays: 2, charlie: CHARLIE, stakes: [1, 2],
  doubleAfterSplit: true, splitAcesOneCard: true, maxHands: 2 });

function mulberry32(a) {
  return () => { a |= 0; a = (a + 0x6d2b79f5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
const fnv = (s) => { let h = 0x811c9dc5; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193); } return h >>> 0; };
const val = (c) => (c[0] === 'A' ? 1 : 'TJQK'.includes(c[0]) ? 10 : Number(c[0]));
function total(cards) { let t = 0, a = false; for (const c of cards) { t += val(c); if (val(c) === 1) a = true; } return a && t + 10 <= 21 ? { total: t + 10, soft: true } : { total: t, soft: false }; }
const natural = (cards) => cards.length === 2 && total(cards).total === 21;
const clone = (x) => (x == null ? x : JSON.parse(JSON.stringify(x)));

export function createMockServer({ sp = 57, now = () => Date.now(), open = true, floorMs = 8000, seed = 11, autoStandMs = DAY_MS } = {}) {
  const user = { sp, hand: null, openedAt: 0, n: 0, nextDealAt: 0, netSp: 0 };
  const receipts = new Map(), faults = [], scripts = [], log = [];
  let rnd = mulberry32(seed);

  function shoeFor() {
    const fixed = scripts.length ? scripts.shift() : [];
    return { fixed, drawn: [] };
  }
  function draw(h) {
    const i = h.next++;
    if (i < h.shoe.fixed.length) return h.shoe.fixed[i];
    while (h.shoe.drawn.length <= i - h.shoe.fixed.length) h.shoe.drawn.push('A23456789TJQK'[Math.floor(rnd() * 13)] + 'shdc'[Math.floor(rnd() * 4)]);
    return h.shoe.drawn[i - h.shoe.fixed.length];
  }

  function legal(h, free) {
    if (!h || h.done) return [];
    const x = h.hands[h.active], out = ['hit', 'stand'], two = x.cards.length === 2;
    const splitAces = x.split && val(x.cards[0]) === 1;
    if (two && !splitAces && free >= x.bet) out.push('double');
    if (two && !x.split && h.hands.length < 2 && val(x.cards[0]) === val(x.cards[1]) && free >= h.stake) out.push('split');
    return out;
  }
  function hint(h, free) {
    const moves = legal(h, free);
    if (!moves.length) return null;
    const x = h.hands[h.active], up = val(h.up) === 1 ? 11 : val(h.up), { total: t, soft } = total(x.cards);
    if (moves.includes('split') && [1, 8].includes(val(x.cards[0]))) return 'split';
    if ((t === 10 || t === 11) && moves.includes('double') && up < t) return 'double';
    if (soft) return t >= 19 || (t === 18 && up < 9) ? 'stand' : 'hit';
    if (t >= 17 || (t >= 13 && up <= 6) || (t === 12 && up >= 4 && up <= 6)) return 'stand';
    return 'hit';
  }
  function finish(h) {
    const dealer = [h.up, h.hole], dealerBj = natural(dealer);
    const playerBj = h.hands.length === 1 && !h.hands[0].split && natural(h.hands[0].cards);
    if (!dealerBj && !playerBj && h.hands.some((x) => total(x.cards).total <= 21 && x.cards.length < CHARLIE)) {
      while (total(dealer).total < 17) dealer.push(draw(h));
    }
    const d = total(dealer).total;
    let wagered = 0, returned = 0;
    const hands = h.hands.map((x) => {
      const t = total(x.cards).total;
      let outcome, paid;
      if (dealerBj) { outcome = playerBj ? 'push' : 'lose'; paid = playerBj ? x.bet : 0; }
      else if (playerBj) { outcome = 'blackjack'; paid = x.bet * 3; }
      else if (t > 21) { outcome = 'bust'; paid = 0; }
      else if (x.cards.length >= CHARLIE) { outcome = 'charlie'; paid = x.bet * 2; }
      else if (d > 21 || t > d) { outcome = 'win'; paid = x.bet * 2; }
      else if (t === d) { outcome = 'push'; paid = x.bet; }
      else { outcome = 'lose'; paid = 0; }
      wagered += x.bet; returned += paid;
      return { outcome, bet: x.bet, paid, total: t };
    });
    Object.assign(h, { dealer, done: true, result: { hands, dealerTotal: d, dealerBlackjack: dealerBj, wagered, returned, net: returned - wagered } });
  }
  const close = (x) => { if (total(x.cards).total >= 21 || x.cards.length >= CHARLIE) x.done = true; };
  function advance(h) {
    while (h.active < h.hands.length && h.hands[h.active].done) h.active++;
    if (h.active >= h.hands.length) { h.active = h.hands.length - 1; finish(h); }
  }
  const pub = (h) => (h ? { id: h.id, step: h.step, stake: h.stake, dealer: h.done ? h.dealer.slice() : [h.up],
    hands: h.hands.map((x) => ({ cards: x.cards.slice(), bet: x.bet, done: x.done, doubled: x.doubled, split: x.split, ...total(x.cards) })),
    active: h.active, done: h.done, result: h.done ? clone(h.result) : null } : null);
  const view = (h, free) => ({ hand: pub(h), legal: legal(h, free), hint: hint(h, free) });
  const standAt = () => (user.hand && !user.hand.done ? new Date(user.openedAt + autoStandMs).toISOString() : null);
  const credit = (amount) => { const before = user.sp; const after = before >= CAP ? before : Math.min(before + amount, CAP); user.sp = after; return before + amount > after; };
  function expire() {
    if (!(user.hand && !user.hand.done && now() - user.openedAt >= autoStandMs)) return null;
    for (const x of user.hand.hands) x.done = true;
    finish(user.hand);
    credit(user.hand.result.returned);
    return user.hand.result.returned;
  }
  const settle = (idem, body) => { user.netSp += body.sp - body.spBefore; receipts.set(idem, clone(body)); return body; };

  function state() {
    return { ok: true, sp: user.sp, open, ...view(user.hand, user.sp), autoStandAt: standAt(), rules: clone(MOCK_RULES), floorMs };
  }
  function deal(body) {
    const idem = body && IDEM.test(body.idem || '') ? body.idem : null;
    if (!idem || ![1, 2].includes(body.stake)) return { ok: false, reason: 'bad_request' };
    if (receipts.has(idem)) return clone(receipts.get(idem));
    const spBefore = user.sp;
    let returned = 0, capped = false, autoStood = null;
    const auto = expire();
    if (auto != null) { returned += auto; autoStood = pub(user.hand); }
    if (user.hand && !user.hand.done) return { ok: false, reason: 'hand_open', ...view(user.hand, user.sp) };
    if (now() < user.nextDealAt) return { ok: false, reason: 'too_fast', retryInMs: user.nextDealAt - now(), ...(autoStood ? { autoStood } : {}) };
    if (user.sp < body.stake) return { ok: false, reason: 'insufficient', sp: user.sp, ...(autoStood ? { autoStood } : {}) };
    const shoe = shoeFor(), n = user.n;
    const h = { id: `h_${n.toString(36)}_${fnv(idem).toString(36)}`, n, step: 0, stake: body.stake, next: 4, shoe, done: false, result: null, active: 0, dealer: null };
    h.hands = [{ cards: [draw({ ...h, next: 0 }), draw({ ...h, next: 2 })], bet: body.stake, done: false, doubled: false, split: false }];
    h.up = draw({ ...h, next: 1 }); h.hole = draw({ ...h, next: 3 });
    user.sp -= body.stake;
    if (natural([h.up, h.hole]) || natural(h.hands[0].cards)) { h.hands[0].done = true; finish(h); capped = credit(h.result.returned); returned += h.result.returned; }
    Object.assign(user, { hand: h, n: n + 1, openedAt: now(), nextDealAt: now() + floorMs });
    const out = { ok: true, idem, sp: user.sp, spBefore, cost: body.stake, returned, capped, ...view(h, user.sp), autoStandAt: standAt() };
    if (autoStood) out.autoStood = autoStood;
    return settle(idem, out);
  }
  function move(op, body) {
    const idem = body && IDEM.test(body.idem || '') ? body.idem : null;
    if (!idem || typeof body.handId !== 'string' || !Number.isInteger(body.step) || body.step < 0) return { ok: false, reason: 'bad_request' };
    if (receipts.has(idem)) return clone(receipts.get(idem));
    const spBefore = user.sp;
    if (expire() != null) return { ok: false, reason: 'auto_stood', hand: pub(user.hand), sp: user.sp };
    const h = user.hand;
    if (!h || h.done || h.id !== body.handId) return { ok: false, reason: 'no_hand' };
    if (body.step !== h.step) return { ok: false, reason: 'stale', ...view(h, user.sp) };
    if (!legal(h, user.sp).includes(op)) return { ok: false, reason: 'illegal', ...view(h, user.sp) };
    const x = h.hands[h.active];
    const cost = op === 'double' ? x.bet : op === 'split' ? h.stake : 0;
    user.sp -= cost; h.step++;
    if (op === 'stand') x.done = true;
    if (op === 'hit') { x.cards.push(draw(h)); close(x); }
    if (op === 'double') { x.bet *= 2; x.doubled = true; x.cards.push(draw(h)); x.done = true; }
    if (op === 'split') {
      const aces = val(x.cards[0]) === 1;
      h.hands = x.cards.map((c) => ({ cards: [c, draw(h)], bet: h.stake, done: false, doubled: false, split: true }));
      for (const y of h.hands) { if (aces) y.done = true; else close(y); }
      h.active = 0;
    }
    advance(h);
    let returned = 0, capped = false;
    if (h.done) { returned = h.result.returned; capped = credit(returned); }
    return settle(idem, { ok: true, idem, sp: user.sp, spBefore, cost, returned, capped, ...view(h, user.sp), autoStandAt: standAt() });
  }

  async function handle(op, body = {}, idem) {
    log.push({ op, idem, body: clone(body) });
    const f = faults.find((x) => x.op === op && x.times > 0);
    if (f) {
      f.times--;
      if (f.apply) await handle.raw(op, body);
      if (f.reason === 'closed') return { ok: true, status: 403, body: { ok: false, reason: 'closed' } };
      if (['busy', 'too_fast'].includes(f.reason)) return { ok: true, status: 200, body: { ok: false, reason: f.reason, ...(f.body || {}) } };
      return { ok: false, status: 0, reason: f.reason };   // host refusals: timeout, offline
    }
    return handle.raw(op, body);
  }
  handle.raw = async (op, body) => {
    if (!open) return { ok: true, status: 403, body: { ok: false, reason: 'closed' } };
    if (op === 'state') return { ok: true, status: 200, body: state() };
    if (op === 'deal') return { ok: true, status: 200, body: deal(body) };
    if (['hit', 'stand', 'double', 'split'].includes(op)) return { ok: true, status: 200, body: move(op, body) };
    return { ok: false, status: 0, reason: 'bad_op' };
  };

  return {
    handle, log, user,
    /** The next hand's cards in deal order: player, dealer up, player, dealer hole, then draws. One call per hand. */
    script(...codes) { scripts.push(codes); },
    /** Next `times` calls to `op` fail with `reason`; apply:true runs the op first (a lost reply). */
    fail(op, reason, times = 1, { apply = false, body } = {}) { faults.push({ op, reason, times, apply, body }); },
    setOpen(v) { open = !!v; },
    /** Make the open hand a day old, so the next POST auto-stands it. */
    age() { user.openedAt = now() - autoStandMs - 1; },
    /** Drop the deal floor (tests), or set it. */
    setFloor(ms) { floorMs = ms; user.nextDealAt = Math.min(user.nextDealAt, now() + ms); },
    reseed(s) { rnd = mulberry32(s); },
  };
}
