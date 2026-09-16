/* mock-server.js - a stand-in for /v2/backroom/wheel/* for dev.html and the node tests. NOT the table or the
 * draw (that is CCP-Server backroom-wheel.js, table v2): it reproduces the body shapes and the rules the page
 * must cope with, from the binding API and the server lane's routes (PRs #158-159):
 *   GET state  -> { ok, sp, open, day, spun, result|null, snoozeCarry, nextResetAt,
 *                   jackpot:{amount, odds, wonToday, eligible, mustHit}, slices:[{id,label,pay,width,odds}], floorMs }
 *   POST spin  -> { ok, sp, result:{day, sliceId, sliceIndex, pay, snoozeCarryPaid, jackpot, jackpotFallback,
 *                   snoozed, total, capped}, jackpot, snoozeCarry, nextResetAt }
 *   refusals   -> already_spun (+ result, sp, jackpot, snoozeCarry, nextResetAt), bad_request, busy and
 *                 too_fast (HTTP 200), closed (HTTP 403). A slice that cannot be won now prints odds "never".
 * handle() resolves like the host relay: {ok:true, status, body} or a host refusal {ok:false, reason}.
 * Everything is injectable: now(), the account age, the pot's seed day, a forced next slice, faults. */

const DAY_MS = 86400000, CAP = 99999;
const SLICES = [   // server TABLE_V2: id, label, pay, weight over 10,000, drawn width in degrees
  ['jackpot', 'JACKPOT', 0, 0, 7.2], ['sip_a', 'Sip', 1, 935, 31.3], ['glow', 'Glow', 5, 1200, 42], ['sparkle_a', 'Sparkle', 2, 1250, 44],
  ['dreamy', 'Dreamy', 20, 200, 8], ['sip_b', 'Sip', 1, 933, 31.3], ['shimmer', 'Shimmer', 8, 700, 25], ['snooze', 'Snooze', 0, 600, 21.6],
  ['twinkle', 'Twinkle', 3, 1500, 52], ['deep', 'Deep', 40, 70, 5], ['sparkle_b', 'Sparkle', 2, 1250, 44], ['dazzle', 'Dazzle', 12, 400, 14.4],
  ['sip_c', 'Sip', 1, 933, 31.2], ['dazed', 'Dazed', 100, 29, 3],
].map(([id, label, pay, weight, width]) => ({ id, label, pay, weight, width }));

function mulberry32(a) {
  return () => {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const fnv = s => { let h = 0x811c9dc5; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193); } return h >>> 0; };
const oneIn = x => (x ? `1 in ${Math.round(x).toLocaleString('en-US')}` : 'never');
const dayKey = day => new Date(day * DAY_MS).toISOString().slice(0, 10);

/** CONTRACT 10.16.E: TABLE_V3's jackpot, with mustHitBy pinned to the cap. */
const JACKPOT = Object.freeze({ start: 250, perDay: 25, cap: 1000, mustHitBy: 1000, targetDays: 10 });

export function createMockServer({ sp = 57, now = () => Date.now(), uid = 'dev-uid', open = true, carry = 0, eligible = true,
                                   potSeedDay = null, spinners = 700, floorMs = 3000 } = {}) {
  const user = { sp, carry, day: -1, result: null, eligible };
  const pot = { seedDay: potSeedDay, wonDay: null };
  const receipts = new Map(), faults = [], script = [], log = [];
  const dayNow = () => Math.floor(now() / DAY_MS);

  function jackpot(day) {
    if (pot.seedDay == null) pot.seedDay = day;
    const amount = Math.min(JACKPOT.start + JACKPOT.perDay * Math.max(0, day - pot.seedDay), JACKPOT.cap);
    const p = 1 - Math.pow(1 - 1 / JACKPOT.targetDays, 1 / Math.max(100, spinners));
    const wonToday = pot.wonDay === day;
    // A room fact, whatever THIS account's age: the pot is at the line and nobody has taken it today.
    const mustHit = amount >= JACKPOT.mustHitBy && !wonToday;
    return { amount, odds: oneIn(1 / p), wonToday, eligible: user.eligible, mustHit, p };
  }
  const jackpotJson = day => { const { p, ...j } = jackpot(day); return j; };

  function slices(day) {
    const j = jackpot(day), canWin = j.eligible && !j.wonToday;
    return SLICES.map(s => {
      let chance = s.id === 'jackpot' ? (canWin ? j.p : 0) : (1 - j.p) * s.weight / 10000;
      if (!canWin && s.id === 'dazed') chance += j.p;
      return { id: s.id, label: s.label, pay: s.id === 'jackpot' ? j.amount : s.pay, width: s.width, odds: oneIn(chance && 1 / chance) };
    });
  }

  function draw(day) {
    if (script.length) return script.shift();
    const rnd = mulberry32(fnv(`${uid}|${day}|wheel`)), j = jackpot(day);
    // must-hit-by (10.16.E): the same rng call is still spent, so a seeded re-draw stays aligned.
    const roll = rnd();
    if ((j.mustHit && j.eligible) || roll < j.p) return 'jackpot';
    let r = Math.floor(rnd() * 10000);
    for (const s of SLICES) { if ((r -= s.weight) < 0) return s.id; }
    return SLICES.at(-1).id;
  }

  function state() {
    const day = dayNow(), spun = user.day === day && !!user.result;
    return { ok: true, sp: user.sp, open, day: dayKey(day), spun, result: spun ? structuredClone(user.result) : null,
             snoozeCarry: user.carry, nextResetAt: new Date((day + 1) * DAY_MS).toISOString(), jackpot: jackpotJson(day),
             slices: slices(day), floorMs };
  }

  function spin(body) {
    const idem = body && typeof body.idem === 'string' && /^[A-Za-z0-9_-]{16,64}$/.test(body.idem) ? body.idem : null;
    if (!idem) return { ok: false, reason: 'bad_request' };
    if (receipts.has(idem)) return structuredClone(receipts.get(idem));
    const day = dayNow(), next = new Date((day + 1) * DAY_MS).toISOString();
    if (user.day === day && user.result) {
      return { ok: false, reason: 'already_spun', sp: user.sp, result: structuredClone(user.result), jackpot: jackpotJson(day), snoozeCarry: user.carry, nextResetAt: next };
    }
    const j = jackpot(day);
    let id = draw(day), fallback = false;
    if (id === 'jackpot' && (!user.eligible || j.wonToday)) { id = 'dazed'; fallback = true; }
    const index = SLICES.findIndex(s => s.id === id), s = SLICES[index], snoozed = id === 'snooze', won = id === 'jackpot';
    const pay = won ? j.amount : s.pay, carryPaid = snoozed ? 0 : user.carry, total = pay + carryPaid;
    const after = user.sp >= CAP ? user.sp : Math.min(user.sp + total, CAP);
    const result = { day: dayKey(day), sliceId: id, sliceIndex: index, pay, snoozeCarryPaid: carryPaid, jackpot: won,
                     jackpotFallback: fallback, snoozed, total, capped: user.sp + total > after };
    user.carry = snoozed ? user.carry + 2 : 0;
    if (won) { pot.wonDay = day; pot.seedDay = day + 1; }
    Object.assign(user, { sp: after, day, result });
    const out = { ok: true, sp: after, result, jackpot: jackpotJson(day), snoozeCarry: user.carry, nextResetAt: next };
    receipts.set(idem, structuredClone(out));
    return out;
  }

  async function handle(op, body = {}, idem) {
    log.push({ op, idem, body: structuredClone(body) });
    const f = faults.find(x => x.op === op && x.times > 0);
    if (f) {
      f.times--;
      if (f.apply) await handle.raw(op, body, idem);
      if (f.reason === 'closed') return { ok: true, status: 403, body: { ok: false, reason: 'closed' } };
      if (f.reason === 'busy' || f.reason === 'too_fast') return { ok: true, status: 200, body: { ok: false, reason: f.reason, ...(f.body || {}) } };
      return { ok: false, status: 0, reason: f.reason };   // host refusals: timeout, offline
    }
    return handle.raw(op, body, idem);
  }
  handle.raw = async (op, body) => {
    if (!open) return { ok: true, status: 403, body: { ok: false, reason: 'closed' } };
    if (op === 'state') return { ok: true, status: 200, body: state() };
    if (op === 'spin') return { ok: true, status: 200, body: spin(body) };
    return { ok: false, status: 0, reason: 'bad_op' };
  };

  return {
    handle, log, user, pot, SLICES,
    /** Next `times` calls to `op` fail with `reason`; apply:true runs the op first (a lost reply). */
    fail(op, reason, times = 1, { apply = false, body } = {}) { faults.push({ op, reason, times, apply, body }); },
    /** Force the next draws, by slice id ('jackpot', 'snooze', 'deep', ...). */
    script(...ids) { script.push(...ids); },
    setOpen(v) { open = !!v; },
    setEligible(v) { user.eligible = !!v; },
    /** The pot as if nobody has won it for `days` days (30 puts it on the must-hit line). */
    potAge(days) { pot.seedDay = dayNow() - days; pot.wonDay = null; },
    /** Park the pot at the cap with nobody having taken it: `jackpot.mustHit` reads true. */
    mustHit() { pot.seedDay = dayNow() - Math.ceil((JACKPOT.cap - JACKPOT.start) / JACKPOT.perDay); pot.wonDay = null; },
    wonToday() { pot.wonDay = dayNow(); pot.seedDay = dayNow() + 1; },
  };
}
