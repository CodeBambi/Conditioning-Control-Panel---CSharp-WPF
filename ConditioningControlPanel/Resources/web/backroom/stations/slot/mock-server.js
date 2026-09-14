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

/* ---------------------------------------------------------------------------------------------------
 * TABLE v7 (CONTRACT 10.16.A and 10.16.D), in ONE place so the server lane can reconcile it.
 *
 * `jar` is the decided 100 / 3 (10.16.A cost table, "decided 2026-09-14 evening"): a jar is a come-back
 * reason, not a rhythm. Its row's scale factor is 0.977450, so the six EVERYDAY pay weights below are the
 * v6 numbers times that factor, as a PLACEHOLDER: the server slot lane (S-b1) is re-solving the exact
 * factor against the sim and will polish `gif3` alone to land 1.0200, exactly as v6 polished it (10.14).
 * `emi3` (92), `emi2` (8,207) and `respin.emi` (7,555) are FIXED by 10.16.D and never scale; `melt` stays
 * 58,900; `none` takes the remainder.
 *
 * The mock does not draw everyday lines from these weights (it dresses them off STRIPS, as it always has,
 * which is what the page's A1/A2 tells are sized against). They are here so the page, dev.html and the node
 * tests see the shapes and the published odds the real table will carry, and so one edit reconciles them.
 * ------------------------------------------------------------------------------------------------- */
const DEN = 1e6;
const V7_SCALE = 0.977450;                               // 10.16.A cost table, row "100 / 3"
const scaled = v6 => Math.round(v6 * V7_SCALE);
const W7 = {
  emi3: 92,                     // 10.16.D: the direct draw, 1 in 10,870 (the TOTAL jackpot is 1 in 6,494)
  emi2: 8207,                   // 10.16.D: the natural rate of an EMI pair on reels 1+2, 1 in 122
  gif3same: scaled(6940),
  sub3: scaled(8675),
  spiral3: scaled(8675),
  gif3: scaled(85691),
  sub2: scaled(69394),
  spiral2: scaled(69394),
  melt: 58900,                  // does not scale
};
W7.none = DEN - Object.values(W7).reduce((a, b) => a + b, 0);
export const WEIGHTS_V7 = Object.freeze(W7);
/** 10.16.D: reel 3's own two-band re-spin draw. Whatever is left of DEN draws a non-EMI, non-melt reel 3. */
const RESPIN_W = Object.freeze({ emi: 7555 });
/** 10.16.A: the spiral jar, 100 spirals for 3 free spins. */
const JAR = Object.freeze({ size: 100, free: 3 });

const LINES = [
  { id: 'emi3', pays: 400, odds: '1 in 10,870', weight: WEIGHTS_V7.emi3, fx: ['fx.jackpot'] },
  // 10.16.D: emi2 pays 0 and has no fx of its own, because the re-spin IS the event.
  { id: 'emi2', pays: 0, odds: '1 in 122', weight: WEIGHTS_V7.emi2, fx: [], respin: 1 },
  { id: 'gif3same', pays: 40, odds: '1 in 157', fx: ['fx.gif_storm'] },
  { id: 'sub3', pays: 15, odds: '1 in 126', fx: ['fx.sub_cascade'] },
  { id: 'spiral3', pays: 10, odds: '1 in 126', fx: ['fx.spiral_full'], free: 3 },
  { id: 'gif3', pays: 3, odds: '1 in 13', fx: ['fx.gif_burst'] },
  { id: 'sub2', pays: 2, odds: '1 in 16', fx: ['fx.sub_pair'] },
  { id: 'spiral2', pays: 1, odds: '1 in 16', fx: ['fx.spiral_brief'], respin: 1 },
  { id: 'melt', pays: 0, odds: '1 in 17', fx: ['fx.melt'] },
];

const kindOf = s => (s.startsWith('gif') ? 'gif' : s.startsWith('sub') ? 'sub' : s.startsWith('spiral') ? 'spiral' : s);

/** The single best line for three payline symbols (section 4). */
export function lineFor(symbols) {
  const k = symbols.map(kindOf), count = t => k.filter(x => x === t).length;
  if (count('emi') === 3) return 'emi3';
  // 10.16.D: emi2 is read BEFORE melt, so `emi, emi, melt` is the chase and does NOT start the melt.
  // `emi, X, emi` and `X, emi, emi` are unchanged: the re-spin is about reels 1 and 2.
  if (k[0] === 'emi' && k[1] === 'emi') return 'emi2';
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

/** 10.16.C: the welcome-back comp the room's visit ping mints. `comp: true` takes the default 5 spins. */
function makeComp(comp) {
  if (!comp) return null;
  const c = comp === true ? {} : comp;
  const spins = Math.max(1, Math.floor(Number(c.spins) || 5));
  return { id: String(c.id || 'c_mock_welcome'), spins, day: Math.floor(Date.now() / 86400000) };
}

export function createMockServer({ sp = 57, melt = 0, seed = 9013, floorMs = 3000, freezeFloorMs = 3000,
                                   now = () => Date.now(), open = true, jar = 0, comp = null } = {}) {
  const rnd = mulberry32(seed);
  const jackRnd = mulberry32(seed ^ 0x5bd1e995);   // its own stream, so the strip draws stay seed-stable
  const respinRnd = mulberry32(seed ^ 0x27d4eb2d); // and the re-spin draws its own, for the same reason
  const JACK = LINES.find(l => l.id === 'emi3');
  // 10.16.A ensureSlot(): a stored jar can never load already full.
  const user = { sp, melt, tape: null, freezes: [], shown: ['gif0', 'spiral1', 'sub2'], nextBuyAt: 0, lastBuyAt: -1e9,
                 freezeReadyAt: 0, jar: Math.min(Math.max(Math.floor(Number(jar) || 0), 0), JAR.size - 1),
                 comp: makeComp(comp) };
  const receipts = new Map();
  const faults = [];          // { op, reason, times, apply, body }
  const script = [];          // forced symbol rows for the next draws, e.g. ['emi','emi','emi']
  const respinScript = [];    // forced reel-3 symbols for the next emi_respin draws, e.g. 'emi'
  const log = [];

  // 10.16.A/D: the published table carries jar, the re-spin block and the TOTAL jackpot odds, so the page
  // never has to add the direct draw and the re-spin's share together itself (published odds are the total).
  const table = () => ({ v: 7, stake: 1, freezeCost: 1, jackpot: JACK.pays, rtp: 1.02, rtpFrozen: 1.02,
                         jackpotOdds: '1 in 6,494', jar: { ...JAR },
                         respin: { emi: '1 in 132', jackpotShare: 0.4 },
                         lines: LINES.map(({ id, pays, odds, respin }) => ({ id, pays, odds, ...(respin ? { respin } : {}) })) });

  function drawRow(held) {
    if (script.length) return script.shift().map((s, c) => (held && held.col === c ? held.sym : s));
    const roll = jackRnd() * DEN;
    if (roll < WEIGHTS_V7.emi3) return [0, 1, 2].map(c => (held && held.col === c ? held.sym : 'emi'));
    // 10.16.D: the EMI pair on reels 1 and 2 is dressed at its own natural rate, reel 3 anything else (melt
    // included: `emi, emi, melt` reads emi2). Nothing is parked off the line more often than chance.
    if (!held && roll < WEIGHTS_V7.emi3 + WEIGHTS_V7.emi2) {
      const pool = STRIPS[2].filter(x => x !== 'emi');
      return ['emi', 'emi', pool[Math.floor(rnd() * pool.length)]];
    }
    // EMI x3 and the EMI pair come only from their weights; a strip row that lands on either is drawn again.
    let row;
    for (let tries = 0; tries < 20; tries++) {
      row = [0, 1, 2].map(c => (held && held.col === c ? held.sym : STRIPS[c][Math.floor(rnd() * 13)]));
      const line = lineFor(row);
      if (line !== 'emi3' && line !== 'emi2') break;
    }
    return row;
  }

  /** 10.16.D: reel 3 alone, from its own two-band weight set. It can never land `melt` (the symbol is not in
   *  its draw), is never halved, consumes no melt, and expands into nothing. */
  function respinRow() {
    if (respinScript.length) return ['emi', 'emi', respinScript.shift()];
    if (respinRnd() < RESPIN_W.emi / DEN) return ['emi', 'emi', 'emi'];
    const pool = STRIPS[2].filter(x => x !== 'emi' && x !== 'melt');   // the 11 non-EMI, non-melt symbols
    return ['emi', 'emi', pool[Math.floor(respinRnd() * pool.length)]];
  }

  /** One spin plus everything it expands into, in draw order. A freeze is sealed from melt (10.2)
   *  and its re-spins keep the hold (10.3); a held melt reads as a blank (10.4). A freeze is sealed from
   *  the jar and from emi2 as well (10.16.A, 10.16.D), so every held class keeps its v6 arithmetic. */
  function spin(kind, held, out) {
    const queue = [kind], sealed = kind === 'freeze';
    while (queue.length && out.length < 60) {
      const k = queue.shift();
      const row = k === 'emi_respin' ? respinRow() : drawRow(k === 'free' || k === 'jar' ? null : held);
      let line = lineFor(row);
      if (sealed && (line === 'melt' || line === 'emi2')) line = 'none';
      // 10.16.D: an emi_respin is 'emi3' or nothing at all, never a pair, never a melt.
      if (k === 'emi_respin' && line !== 'emi3') line = 'none';
      const def = LINES.find(l => l.id === line);
      // The re-spin is never halved and neither consumes nor starts melt, like a freeze outcome.
      const plain = !sealed && k !== 'emi_respin';
      const halved = plain && user.melt > 0;
      const pay = def ? (halved ? Math.floor(def.pays / 2) : def.pays) : 0;
      if (plain && user.melt > 0) user.melt--;
      if (line === 'melt') user.melt = 3;
      if (k !== 'emi_respin') {
        for (let i = 0; i < (def && def.free || 0); i++) queue.push('free');
        // 10.16.D: exactly one, and at the FRONT of the queue. The contract says "appends ... so drain() plays
        // it immediately after"; the two halves of that sentence only agree when the queue is otherwise empty
        // (a plain paid spin). The chase is the point, so "immediately after" is the half that wins, and the
        // page's no-press rule then holds in every case, nested draws included. Server lane: mirror this.
        if (line === 'emi2') queue.unshift('emi_respin');
        else for (let i = 0; i < (def && def.respin || 0); i++) queue.push('respin');
      }
      // 10.16.A THE SPIRAL JAR: the spirals SHOWN, in draw order, on plain-band outcomes only. A freeze and
      // everything it expanded into earn nothing (the seal that keeps rtpFrozen at exactly 1.0200); the
      // re-spin descends from a plain-band spin, so its spirals DO count.
      if (!sealed) {
        const spirals = row.filter(x => kindOf(x) === 'spiral').length;
        if (spirals) {
          const was = user.jar;
          user.jar = (was + spirals) % JAR.size;
          if (was + spirals >= JAR.size) for (let i = 0; i < JAR.free; i++) queue.push('jar');
        }
      }
      const subs = row.filter(x => kindOf(x) === 'sub');
      const fx = def ? [...def.fx] : [];
      if (subs.length && !fx.some(f => f.startsWith('fx.sub_'))) fx.push('fx.sub_single');
      out.push({ i: out.length, kind: k, stops: row.map((x, c) => STRIPS[c].indexOf(x)), symbols: row, line, pay,
                 halved, meltLeft: user.melt, freeLeft: queue.length, jarN: user.jar, fx, subs });
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
    // 10.16.C: with `comp`, `freeze` must be absent and `count` absent or exactly 5. parseTapeBody answers
    // null (-> bad_input) for anything else, before a single reel is drawn.
    const wantComp = body.comp != null ? String(body.comp) : null;
    if (wantComp !== null && (!/^c_[A-Za-z0-9_-]{4,48}$/.test(wantComp) || freeze
        || (body.count != null && Number(body.count) !== 5))) return { ok: false, reason: 'bad_input' };
    const compSpins = wantComp !== null && user.comp ? user.comp.spins : 5;
    const count = freeze ? 1 : wantComp !== null ? compSpins : Math.floor(Number(body.count));
    if (!(count >= 1 && count <= 20) || (freeze && body.count != null && body.count !== 1)) return { ok: false, reason: 'bad_count' };
    const tp = user.tape;
    if (body.cursor) moveCursor(body.cursor.tapeId, body.cursor.played);
    if (!freeze && tp && tp.played < tp.outcomes.length) {
      return { ok: false, reason: 'tape_unplayed', sp: user.sp, melt: user.melt, tape: structuredClone(tp) };
    }
    // 10.12: 3000 ms per outcome; a freeze waits 3000 ms after the last buy, and after a freeze max(3000, its outcomes x 3000).
    const t = now(), readyAt = freeze ? Math.max(user.lastBuyAt + freezeFloorMs, user.freezeReadyAt) : user.nextBuyAt;
    if (t < readyAt) return { ok: false, reason: 'too_fast', retryInMs: Math.ceil(readyAt - t) };
    // 10.16.C: an unspent comp is 0 SP, so `insufficient` cannot happen - that is the whole point of a comp.
    if (wantComp !== null && !user.comp) return { ok: false, reason: 'comp_none' };
    if (wantComp !== null && user.comp.id !== wantComp) return { ok: false, reason: 'comp_used' };
    const spent = wantComp !== null ? user.comp : null;
    const cost = freeze ? 1 + table().freezeCost : spent ? 0 : count;
    if (user.sp < cost) return { ok: false, reason: 'insufficient', sp: user.sp };
    const spBefore = user.sp, out = [];
    const held = freeze ? { col: freeze.col, sym: shownAt()[freeze.col] } : null;
    if (freeze) spin('freeze', held, out);
    else for (let i = 0; i < count; i++) spin('paid', null, out);
    const raw = spBefore - cost + out.reduce((s, o) => s + o.pay, 0), capped = raw > 99999;
    user.sp = Math.max(0, Math.min(99999, raw));
    const receipt = { ok: true, idem, sp: user.sp, spBefore, cost, capped, melt: user.melt, jar: user.jar, jackpot: JACK.pays };
    if (spent) { receipt.comp = { id: spent.id, spins: spent.spins }; user.comp = null; }   // settle() clears it
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
    // 10.16.A/C: the stored jar count and the comp the room's visit ping minted. This route never mints.
    return { ok: true, sp: user.sp, open, melt: user.melt, jar: user.jar,
             comp: user.comp ? { id: user.comp.id, spins: user.comp.spins } : null,
             tape: tp ? structuredClone({ id: tp.id, played: tp.played, outcomes: tp.outcomes }) : null,
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
    /** Force reel 3 of the next emi_respin draws, in order ('emi' lands the jackpot). */
    respin(...syms) { respinScript.push(...syms); },
    /** dev.html only: the jar and the comp the room's visit ping would have left behind. */
    setJar(n) { user.jar = Math.min(Math.max(Math.floor(Number(n) || 0), 0), JAR.size - 1); return user.jar; },
    giveComp(c = true) { user.comp = makeComp(c); return user.comp; },
    table: () => table(),
    setOpen(v) { open = !!v; },
  };
}
