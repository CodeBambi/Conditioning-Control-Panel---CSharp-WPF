/* mock-server.js - a stand-in for /v2/backroom/roulette/* for dev.html, the node tests and roulette-check.mjs.
 * NOT the table or the draw (that is CCP-Server backroom-roulette.js TABLE_V1 behind backroom-roulette-routes.js):
 * it answers with the binding shapes of CONTRACT 10.13.E and the refusals a page must cope with, from seeded
 * fixture outcomes.
 *   GET state   -> { ok, sp, open, tape|null (only while unplayed), table, wheel, rose, spots, floorMs }
 *   POST spin   {idem, count, bets, cursor?} -> { ok, idem, sp, spBefore, cost, won, capped, tape:{id, bets, played:0,
 *                  outcomes:[{i, pocket, color, wake, pay, fx}]} }
 *   POST cursor {tapeId, played} -> { ok }
 *   refusals    bad_request, bad_layout + why, tape_unplayed + sp + tape, too_fast + retryInMs, insufficient + sp,
 *               busy (HTTP 200), closed (HTTP 403)
 * handle() resolves like the host relay: {ok:true, status, body}, or a host refusal {ok:false, reason}.
 * script({pocket, wake}) forces the next draws; fail(op, reason, times) injects faults. */

const WHEEL = [0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26];
const ROSE = [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36];
const SPOTS = [...Array.from({ length: 37 }, (_, n) => 's' + n), 'rose', 'plum', 'sip', 'sink', 'deep'];
const CAP = 99999;
// The published table as the state route prints it (the harness pays from these numbers, the page never does).
const TABLE = {
  v: 1, minStake: 1, maxStake: 3, maxTape: 5, rtp: 1.02, wake: '1 in 20.7', rules: ['covers_all_refused'],
  kinds: [
    { kind: 'straight', covers: 1, spots: 37, pays: 36, woken: 72, odds: '1 in 37.00', rtp: 1.02 },
    { kind: 'row', covers: 12, spots: 3, pays: 3, woken: 6, odds: '1 in 3.08', rtp: 1.02 },
    { kind: 'color', covers: 18, spots: 2, pays: 2, woken: 4, odds: '1 in 2.06', rtp: 1.02 },
  ],
};

function mulberry32(a) {
  return () => {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const fnv = (s) => { let h = 0x811c9dc5; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193); } return h >>> 0; };
const kindOf = (spot) => (/^s\d+$/.test(spot) ? 'straight' : spot === 'rose' || spot === 'plum' ? 'color' : 'row');
function covers(spot) {
  if (/^s\d+$/.test(spot)) return [Number(spot.slice(1))];
  const all = Array.from({ length: 36 }, (_, i) => i + 1);
  if (spot === 'rose') return ROSE;
  if (spot === 'plum') return all.filter((n) => !ROSE.includes(n));
  const [a, b] = { sip: [1, 12], sink: [13, 24], deep: [25, 36] }[spot];
  return all.filter((n) => n >= a && n <= b);
}
const payOf = (kind) => TABLE.kinds.find((k) => k.kind === kind).pays;

export function createMockServer({ sp = 40, uid = 'dev-uid', open = true, floorMs = 6000, now = () => Date.now(), seed = 7 } = {}) {
  const user = { sp, n: 0, nextBuyAt: 0, tape: null, netSp: 0 };
  const receipts = new Map(), faults = [], forced = [], log = [];

  function layout(raw) {
    const bad = (why) => ({ ok: false, reason: 'bad_layout', why });
    if (!Array.isArray(raw) || !raw.length) return bad('empty');
    if (raw.length > TABLE.maxStake) return bad('too_many_spots');
    const seen = new Set(), bets = [];
    for (const b of raw) {
      if (!b || typeof b.spot !== 'string' || !SPOTS.includes(b.spot)) return bad('unknown_spot');
      if (!Number.isInteger(b.amt) || b.amt < 1 || b.amt > TABLE.maxStake) return bad('bad_amount');
      if (seen.has(b.spot)) return bad('duplicate_spot');
      seen.add(b.spot); bets.push({ spot: b.spot, amt: b.amt });
    }
    const stake = bets.reduce((s, b) => s + b.amt, 0);
    if (stake > TABLE.maxStake) return bad('stake_cap');
    if (new Set(bets.flatMap((b) => covers(b.spot)).filter((n) => n > 0)).size === 36) return bad('covers_all');
    bets.sort((a, b) => SPOTS.indexOf(a.spot) - SPOTS.indexOf(b.spot));
    return { ok: true, bets, stake };
  }

  function draw(k) {
    if (forced.length) return forced.shift();
    const rnd = mulberry32(fnv(`${uid}|${seed}|${k}`));
    return { pocket: Math.floor(rnd() * 37), wake: Math.floor(rnd() * 600) < 29 };
  }

  function outcome(bets, d, i) {
    const base = bets.reduce((s, b) => s + (covers(b.spot).includes(d.pocket) ? b.amt * payOf(kindOf(b.spot)) : 0), 0);
    const color = d.pocket === 0 ? 'mint' : ROSE.includes(d.pocket) ? 'rose' : 'plum';
    return { i, pocket: d.pocket, color, wake: !!d.wake, pay: d.wake ? base * 2 : base, fx: [] };
  }

  const tapeJson = (t) => (t ? structuredClone({ id: t.id, bets: t.bets, played: t.played, outcomes: t.outcomes }) : null);
  function applyCursor(c) {
    const t = user.tape;
    if (!t || !c || c.tapeId !== t.id || !Number.isInteger(c.played)) return false;
    const next = Math.min(Math.max(c.played, t.played), t.outcomes.length);
    if (next === t.played) return false;
    t.played = next;
    return true;
  }

  function spin(body) {
    const b = body || {};
    if (typeof b.idem !== 'string' || !/^[A-Za-z0-9_-]{16,64}$/.test(b.idem)) return { ok: false, reason: 'bad_request' };
    if (receipts.has(b.idem)) return structuredClone(receipts.get(b.idem));
    const count = b.count == null ? 1 : b.count;
    if (!Number.isInteger(count) || count < 1 || count > TABLE.maxTape || !Array.isArray(b.bets)) return { ok: false, reason: 'bad_request' };
    const l = layout(b.bets);
    if (!l.ok) return l;
    if (b.cursor) applyCursor(b.cursor);
    const t = user.tape;
    if (t && t.played < t.outcomes.length) return { ok: false, reason: 'tape_unplayed', sp: user.sp, tape: tapeJson(t) };
    if (now() < user.nextBuyAt) return { ok: false, reason: 'too_fast', retryInMs: Math.ceil(user.nextBuyAt - now()) };
    const cost = count * l.stake;
    if (user.sp < cost) return { ok: false, reason: 'insufficient', sp: user.sp };
    const outcomes = Array.from({ length: count }, (_, i) => outcome(l.bets, draw(user.n + i), i));
    const won = outcomes.reduce((s, o) => s + o.pay, 0);
    const raw = user.sp - cost + won, after = Math.max(0, Math.min(raw, CAP));
    const id = `r_${user.n.toString(36)}_${fnv(b.idem).toString(36)}`;
    user.tape = { id, bets: l.bets, played: 0, outcomes };
    user.n += count; user.nextBuyAt = now() + count * floorMs; user.netSp += after - user.sp;
    const receipt = { ok: true, idem: b.idem, sp: after, spBefore: user.sp, cost, won, capped: raw > CAP, tape: tapeJson(user.tape) };
    user.sp = after;
    receipts.set(b.idem, structuredClone(receipt));
    return receipt;
  }

  function state() {
    const t = user.tape && user.tape.played < user.tape.outcomes.length ? user.tape : null;
    return { ok: true, sp: user.sp, open, tape: tapeJson(t), table: structuredClone(TABLE), wheel: WHEEL.slice(), rose: ROSE.slice(), spots: SPOTS.slice(), floorMs };
  }

  async function handle(op, body = {}, idem) {
    log.push({ op, idem, body: structuredClone(body) });
    const f = faults.find((x) => x.op === op && x.times > 0);
    if (f) {
      f.times--;
      if (f.apply) await handle.raw(op, body);
      if (f.reason === 'closed') return { ok: true, status: 403, body: { ok: false, reason: 'closed' } };
      if (f.reason === 'busy' || f.reason === 'too_fast') return { ok: true, status: 200, body: { ok: false, reason: f.reason, ...(f.body || {}) } };
      return { ok: false, status: 0, reason: f.reason };   // host refusals: timeout, offline
    }
    return handle.raw(op, body);
  }
  handle.raw = async (op, body) => {
    if (op === 'state') return { ok: true, status: 200, body: state() };
    if (!open) return { ok: true, status: 403, body: { ok: false, reason: 'closed' } };
    if (op === 'spin') return { ok: true, status: 200, body: spin(body) };
    if (op === 'cursor') {
      const b = body || {};
      if (typeof b.tapeId !== 'string' || !Number.isInteger(b.played) || b.played < 0) return { ok: true, status: 200, body: { ok: false, reason: 'bad_request' } };
      applyCursor(b);
      return { ok: true, status: 200, body: { ok: true } };
    }
    return { ok: false, status: 0, reason: 'bad_op' };
  };

  return {
    handle, log, user, WHEEL, ROSE, SPOTS,
    /** Next `times` calls to `op` fail with `reason`; apply:true runs the op first (a lost reply). */
    fail(op, reason, times = 1, { apply = false, body } = {}) { faults.push({ op, reason, times, apply, body }); },
    /** Force the next draws: script({pocket: 17, wake: true}, {pocket: 0}). */
    script(...draws) { forced.push(...draws.map((d) => ({ pocket: Number(d.pocket) || 0, wake: !!d.wake }))); },
    setOpen(v) { open = !!v; },
    setFloor(ms) { floorMs = Math.max(0, Number(ms) || 0); user.nextBuyAt = Math.min(user.nextBuyAt, now() + floorMs); },
  };
}
