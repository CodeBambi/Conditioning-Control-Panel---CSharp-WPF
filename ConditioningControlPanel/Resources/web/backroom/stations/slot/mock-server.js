/* mock-server.js - a stand-in for /v2/backroom/slot/* (CONTRACT.md section 3) for dev.html and
 * the node tests. NOT the paytable (that is CCP-Server, lane S1): it only reproduces the body shapes
 * and the rules the page must cope with (tape_unplayed, idem receipts, too_fast, insufficient,
 * closed, melt in draw order, inline free spins, one-column freeze mid-tape answered as
 * `freeze: {col, held, outcomes}` with `tape` as the stored tape's `{id, played}` only, section 10.10).
 * handle() resolves like the host relay: {ok:true, status, body} or a host refusal {ok:false, reason}. */

const STRIPS = [
  ['gif0', 'spiral0', 'sub0', 'gif1', 'emi', 'spiral1', 'gif2', 'sub1', 'spiral2', 'gif3', 'sub2', 'sub3', 'melt'],
  ['sub1', 'gif2', 'spiral1', 'melt', 'gif0', 'sub3', 'emi', 'spiral2', 'gif3', 'sub0', 'spiral0', 'gif1', 'sub2'],
  ['spiral2', 'gif3', 'sub2', 'gif1', 'spiral0', 'emi', 'sub0', 'gif0', 'melt', 'sub3', 'gif2', 'spiral1', 'sub1'],
];

const LINES = [
  { id: 'emi3', pays: 400, odds: '1 in 12,987', weight: 77, fx: ['fx.jackpot'] },   // table v5 (10.1), 77 per million
  { id: 'gif3same', pays: 40, odds: '1 in 150', fx: ['fx.gif_storm'] },
  { id: 'sub3', pays: 15, odds: '1 in 120', fx: ['fx.sub_cascade'] },
  { id: 'spiral3', pays: 10, odds: '1 in 120', fx: ['fx.spiral_full'], free: 3 },
  { id: 'gif3', pays: 3, odds: '1 in 12', fx: ['fx.gif_burst'] },
  { id: 'sub2', pays: 2, odds: '1 in 15', fx: ['fx.sub_pair'] },
  { id: 'spiral2', pays: 1, odds: '1 in 15', fx: ['fx.spiral_brief'], respin: 1 },
  { id: 'melt', pays: 0, odds: '1 in 20', fx: ['fx.melt'] },
];

const kindOf = s => (s.startsWith('gif') ? 'gif' : s.startsWith('sub') ? 'sub' : s.startsWith('spiral') ? 'spiral' : s);

/** The single best line for three payline symbols (section 4). */
export function lineFor(symbols) {
  const k = symbols.map(kindOf), count = t => k.filter(x => x === t).length;
  if (count('emi') === 3) return 'emi3';
  if (count('gif') === 3) return symbols[0] === symbols[1] && symbols[1] === symbols[2] ? 'gif3same' : 'gif3';
  if (count('sub') === 3) return 'sub3';
  if (count('spiral') === 3) return 'spiral3';
  if (count('sub') === 2) return 'sub2';
  if (count('spiral') === 2) return 'spiral2';
  return k.includes('melt') ? 'melt' : 'none';
}

function mulberry32(a) {
  return () => {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function createMockServer({ sp = 57, melt = 0, seed = 9013, floorMs = 3000, freezeFloorMs = 3000,
                                   now = () => Date.now(), open = true } = {}) {
  const rnd = mulberry32(seed);
  const jackRnd = mulberry32(seed ^ 0x5bd1e995);   // its own stream, so the strip draws stay seed-stable
  const JACK = LINES.find(l => l.id === 'emi3');
  const user = { sp, melt, tape: null, freezes: [], shown: ['gif0', 'spiral1', 'sub2'], nextBuyAt: 0, lastBuyAt: -1e9,
                 freezeReadyAt: 0 };
  const receipts = new Map();
  const faults = [];          // { op, reason, times, apply, body }
  const script = [];          // forced symbol rows for the next draws, e.g. ['emi','emi','emi']
  const log = [];

  const table = () => ({ v: 5, stake: 1, freezeCost: 1, jackpot: JACK.pays, rtp: 1.02, rtpFrozen: 1.02,
                         lines: LINES.map(({ id, pays, odds }) => ({ id, pays, odds })) });

  function drawRow(held) {
    if (script.length) return script.shift().map((s, c) => (held && held.col === c ? held.sym : s));
    if (jackRnd() < JACK.weight / 1e6) return [0, 1, 2].map(c => (held && held.col === c ? held.sym : 'emi'));
    // EMI x3 comes only from its weight; a strip row that lands on it is drawn again.
    let row;
    for (let tries = 0; tries < 20; tries++) {
      row = [0, 1, 2].map(c => (held && held.col === c ? held.sym : STRIPS[c][Math.floor(rnd() * 13)]));
      if (lineFor(row) !== 'emi3') break;
    }
    return row;
  }

  /** One spin plus everything it expands into, in draw order. A freeze is sealed from melt (10.2)
   *  and its re-spins keep the hold (10.3); a held melt reads as a blank (10.4). */
  function spin(kind, held, out) {
    const queue = [kind], sealed = kind === 'freeze';
    while (queue.length && out.length < 60) {
      const k = queue.shift();
      const symbols = drawRow(k === 'free' ? null : held);
      let line = lineFor(symbols);
      if (sealed && line === 'melt') line = 'none';
      const def = LINES.find(l => l.id === line), halved = !sealed && user.melt > 0;
      const pay = def ? (halved ? Math.floor(def.pays / 2) : def.pays) : 0;
      if (!sealed && user.melt > 0) user.melt--;
      if (line === 'melt') user.melt = 3;
      for (let i = 0; i < (def && def.free || 0); i++) queue.push('free');
      for (let i = 0; i < (def && def.respin || 0); i++) queue.push('respin');
      const subs = symbols.filter(s => kindOf(s) === 'sub');
      const fx = def ? [...def.fx] : [];
      if (subs.length && !fx.some(f => f.startsWith('fx.sub_'))) fx.push('fx.sub_single');
      out.push({ i: out.length, kind: k, stops: symbols.map((s, c) => STRIPS[c].indexOf(s)), symbols, line, pay,
                 halved, meltLeft: user.melt, freeLeft: queue.length, fx, subs });
    }
    return out;
  }

  /** Forward only, clamped, ignored for any tape but the stored one (server applyCursor). */
  function moveCursor(id, played) {
    if (user.tape && id === user.tape.id) user.tape.played = Math.min(user.tape.outcomes.length, Math.max(user.tape.played, played | 0));
  }

  /** The payline as of the stored cursor, or the last freeze made at that same cursor (server shownAt). */
  function shownAt() {
    const tp = user.tape, f = user.freezes.at(-1);
    let shown = tp ? (tp.played > 0 ? tp.outcomes[tp.played - 1].symbols : tp.before) : user.shown;
    if (f && f.tapeId === (tp ? tp.id : null) && f.at === (tp ? tp.played : 0)) shown = f.last;
    return shown;
  }

  /** Same order and body shapes as CCP-Server backroom-slot.js settle(). */
  function tape(body, idem) {
    if (!/^[A-Za-z0-9_-]{16,64}$/.test(idem || '')) return { ok: false, reason: 'bad_idem' };
    if (receipts.has(idem)) return receipts.get(idem);
    const freeze = body.freeze && [0, 1, 2].includes(body.freeze.col) ? body.freeze : null;
    const count = freeze ? 1 : Math.floor(Number(body.count));
    if (!(count >= 1 && count <= 20) || (freeze && body.count != null && body.count !== 1)) return { ok: false, reason: 'bad_count' };
    const tp = user.tape;
    if (body.cursor) moveCursor(body.cursor.tapeId, body.cursor.played);
    if (!freeze && tp && tp.played < tp.outcomes.length) {
      return { ok: false, reason: 'tape_unplayed', sp: user.sp, melt: user.melt, tape: structuredClone(tp) };
    }
    // 10.12: 3000 ms per outcome; a freeze waits 3000 ms after the last buy, and after a freeze max(3000, its outcomes x 3000).
    const t = now(), readyAt = freeze ? Math.max(user.lastBuyAt + freezeFloorMs, user.freezeReadyAt) : user.nextBuyAt;
    if (t < readyAt) return { ok: false, reason: 'too_fast', retryInMs: Math.ceil(readyAt - t) };
    const cost = freeze ? 1 + table().freezeCost : count;
    if (user.sp < cost) return { ok: false, reason: 'insufficient', sp: user.sp };
    const spBefore = user.sp, out = [];
    const held = freeze ? { col: freeze.col, sym: shownAt()[freeze.col] } : null;
    if (freeze) spin('freeze', held, out);
    else for (let i = 0; i < count; i++) spin('paid', null, out);
    const raw = spBefore - cost + out.reduce((s, o) => s + o.pay, 0), capped = raw > 99999;
    user.sp = Math.max(0, Math.min(99999, raw));
    const receipt = { ok: true, idem, sp: user.sp, spBefore, cost, capped, melt: user.melt, jackpot: JACK.pays };
    if (freeze) {
      user.freezes.push({ tapeId: tp ? tp.id : null, at: tp ? tp.played : 0, col: freeze.col, last: out.at(-1).symbols });
      user.nextBuyAt = Math.max(user.nextBuyAt, t) + out.length * floorMs;
      user.freezeReadyAt = t + Math.max(freezeFloorMs, out.length * floorMs);
      receipt.tape = tp ? { id: tp.id, played: tp.played } : null;
      receipt.freeze = { col: freeze.col, held: held.sym, outcomes: out };
    } else {
      const id = 't_' + Math.floor(rnd() * 0xffffffff).toString(16).padStart(8, '0');
      user.tape = { id, played: 0, before: shownAt(), outcomes: out };
      user.nextBuyAt = t + out.length * floorMs;
      receipt.tape = structuredClone({ id, played: 0, outcomes: out });
    }
    user.lastBuyAt = t;
    receipts.set(idem, receipt);
    return receipt;
  }

  function state() {
    const tp = user.tape;
    return { ok: true, sp: user.sp, open, melt: user.melt, tape: tp ? structuredClone({ id: tp.id, played: tp.played, outcomes: tp.outcomes }) : null,
             shown: shownAt(), table: table(), strips: STRIPS, floorMs };
  }

  async function handle(op, body = {}, idem) {
    log.push({ op, idem, body: structuredClone(body) });
    const f = faults.find(x => x.op === op && x.times > 0);
    if (f) {
      f.times--;
      if (f.apply) await handle.raw(op, body, idem);
      return f.reason === 'too_fast' || f.reason === 'busy' || f.reason === 'closed'
        ? { ok: true, status: f.reason === 'closed' ? 403 : f.reason === 'busy' ? 409 : 200,
            body: { ok: false, reason: f.reason, ...(f.body || {}) } }
        : { ok: false, status: 0, reason: f.reason };
    }
    return handle.raw(op, body, idem);
  }
  handle.raw = async (op, body, idem) => {
    if (!open) return { ok: true, status: 403, body: { ok: false, reason: 'closed' } };
    if (op === 'state') return { ok: true, status: 200, body: state() };
    if (op === 'cursor') { moveCursor(body.tapeId, body.played); return { ok: true, status: 200, body: { ok: true } }; }
    if (op === 'tape') return { ok: true, status: 200, body: tape(body, idem) };
    return { ok: false, status: 0, reason: 'bad_op' };
  };

  return {
    handle, log, user,
    /** Next `times` calls to `op` fail with `reason`; apply:true runs the op first (a lost reply). */
    fail(op, reason, times = 1, { apply = false, body } = {}) { faults.push({ op, reason, times, apply, body }); },
    /** Force the payline rows of the next draws, in order. */
    script(...rows) { script.push(...rows); },
    setOpen(v) { open = !!v; },
  };
}
