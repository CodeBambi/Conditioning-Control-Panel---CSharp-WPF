/* mock-server.js - a stand-in for /v2/backroom/slot/* (CONTRACT.md section 3) for dev.html and
 * the node tests. NOT the paytable (that is CCP-Server, lane S1): it only reproduces the body shapes
 * and the rules the page must cope with (tape_unplayed, idem receipts, too_fast, insufficient,
 * closed, melt in draw order, inline free spins, one-column freeze mid-tape).
 * handle() resolves like the host relay: {ok:true, status, body} or a host refusal {ok:false, reason}. */

const STRIPS = [
  ['gif0', 'spiral0', 'sub0', 'gif1', 'emi', 'spiral1', 'gif2', 'sub1', 'spiral2', 'gif3', 'sub2', 'sub3', 'melt'],
  ['sub1', 'gif2', 'spiral1', 'melt', 'gif0', 'sub3', 'emi', 'spiral2', 'gif3', 'sub0', 'spiral0', 'gif1', 'sub2'],
  ['spiral2', 'gif3', 'sub2', 'gif1', 'spiral0', 'emi', 'sub0', 'gif0', 'melt', 'sub3', 'gif2', 'spiral1', 'sub1'],
];

const LINES = [
  { id: 'emi3', pays: 2500, odds: '1 in 25,000', fx: ['fx.jackpot'] },
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

export function createMockServer({ sp = 57, melt = 0, seed = 9013, floorMs = 800, now = () => Date.now(),
                                   open = true } = {}) {
  const rnd = mulberry32(seed);
  const user = { sp, melt, tape: null, shown: ['gif0', 'spiral1', 'sub2'], nextBuyAt: 0, lastBuyAt: -1e9 };
  const receipts = new Map();
  const faults = [];          // { op, reason, times, apply, body }
  const script = [];          // forced symbol rows for the next draws, e.g. ['emi','emi','emi']
  const log = [];

  const table = () => ({ v: 3, stake: 1, freezeCost: 1, jackpot: 2500, rtp: 1.02, rtpFrozen: 1.02,
                         lines: LINES.map(({ id, pays, odds }) => ({ id, pays, odds })) });

  function drawRow(held) {
    if (script.length) return script.shift().map((s, c) => (held && held.col === c ? held.sym : s));
    return [0, 1, 2].map(c => (held && held.col === c ? held.sym : STRIPS[c][Math.floor(rnd() * 13)]));
  }

  /** One spin plus everything it expands into, consuming melt in draw order. */
  function spin(kind, held, out) {
    const queue = [kind];
    while (queue.length && out.length < 60) {
      const k = queue.shift();
      const symbols = drawRow(k === 'freeze' || k === 'paid' ? held : null);
      const line = lineFor(symbols), def = LINES.find(l => l.id === line);
      let pay = def ? def.pays : 0, halved = false;
      if (user.melt > 0) { user.melt--; if (pay) { pay = Math.floor(pay / 2); halved = true; } }
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

  function tape(body, idem) {
    if (!/^[A-Za-z0-9_-]{16,64}$/.test(idem || '')) return { ok: false, reason: 'bad_idem' };
    if (receipts.has(idem)) return receipts.get(idem);
    const count = Math.floor(Number(body.count));
    const freeze = body.freeze && [0, 1, 2].includes(body.freeze.col) ? body.freeze : null;
    if (!(count >= 1 && count <= 20) || (freeze && count !== 1)) return { ok: false, reason: 'bad_count' };
    const cur = body.cursor;
    if (cur && user.tape && cur.tapeId === user.tape.id) user.tape.played = Math.max(user.tape.played, cur.played | 0);
    const cost = freeze ? 1 + table().freezeCost : count;
    if (user.sp < cost) return { ok: false, reason: 'insufficient', sp: user.sp };
    const t = now();
    if (!freeze && user.tape && user.tape.played !== user.tape.outcomes.length) {
      return { ok: false, reason: 'tape_unplayed', sp: user.sp, tape: structuredClone(user.tape) };
    }
    if (!freeze && t < user.nextBuyAt) return { ok: false, reason: 'too_fast', retryInMs: user.nextBuyAt - t };
    if (freeze && t - user.lastBuyAt < 700) return { ok: false, reason: 'too_fast', retryInMs: 700 - (t - user.lastBuyAt) };
    const spBefore = user.sp, out = [];
    const tp = user.tape;
    const at = tp && tp.played > 0 ? tp.outcomes[tp.played - 1].symbols : user.shown;
    const held = freeze ? { col: freeze.col, sym: (user.lastFreeze || at)[freeze.col] } : null;
    if (freeze) spin('freeze', held, out);
    else for (let i = 0; i < count; i++) spin('paid', null, out);
    const won = out.reduce((s, o) => s + o.pay, 0);
    const raw = spBefore - cost + won, capped = raw > 99999;
    user.sp = Math.min(99999, raw);
    const id = (freeze ? 'f_' : 't_') + Math.floor(rnd() * 0xffffffff).toString(16).padStart(8, '0');
    const tapeOut = { id, played: 0, outcomes: out };
    if (freeze) user.lastFreeze = out[out.length - 1].symbols;
    else { user.tape = structuredClone(tapeOut); user.lastFreeze = null; user.nextBuyAt = t + out.length * floorMs; }
    user.lastBuyAt = t;
    const receipt = { ok: true, idem, sp: user.sp, spBefore, cost, capped, melt: user.melt, jackpot: 2500, tape: tapeOut };
    receipts.set(idem, receipt);
    return receipt;
  }

  function state() {
    return { ok: true, sp: user.sp, open, melt: user.melt, tape: user.tape ? structuredClone(user.tape) : null,
             shown: user.shown, table: table(), strips: STRIPS, floorMs };
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
    if (op === 'cursor') {
      if (user.tape && body.tapeId === user.tape.id) user.tape.played = Math.max(user.tape.played, body.played | 0);
      return { ok: true, status: 200, body: { ok: true } };
    }
    if (op === 'tape') return { ok: true, status: 200, body: tape(body, idem) };
    return { ok: false, status: 0, reason: 'bad_op' };
  };

  return {
    handle,
    log,
    user,
    /** Next `times` calls to `op` fail with `reason`; apply:true runs the op first (a lost reply). */
    fail(op, reason, times = 1, { apply = false, body } = {}) { faults.push({ op, reason, times, apply, body }); },
    /** Force the payline rows of the next draws, in order. */
    script(...rows) { script.push(...rows); },
    setOpen(v) { open = !!v; },
  };
}
