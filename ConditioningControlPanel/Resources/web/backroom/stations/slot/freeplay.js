/* freeplay.js - "Keep playing" (desk tester, 2026-09-18: "the ccp effects on the slots are good, but knowing
 * you have limited spins limits their effectiveness - can't sink into it when you know it'll be over before
 * you properly fall").
 *
 * A NO-STAKE spin. The reels and THE FLOW (feel.flowPlan: the spiral, the tunnel, the gif bursts, the holds,
 * the words) run exactly as a paid spin's would, off a row THIS file deals on the page from the strips the
 * server already handed the tape. It costs no SP, pays no prize and never asks the server. Money-safety:
 *   - nothing here has a `request`: no slot/spin, no slot/chase, no ledger op is made for a free spin;
 *   - every row deals pay 0 and kind 'demo', so feel.tierOf reads 0: no tokens, no bank, no shower;
 *   - the outcome never enters tape.js. It is not one of the tape's own objects, so tape.land() refuses it
 *     and the cursor, the melt, the jar and the free spins stay exactly where the server left them;
 *   - the jackpot (emi3), the chase (emi2) and the melt are never dealt: the first two are tape events with a
 *     server side, the third moves the readouts.
 * The cabinet says so on its face (br_slot_freeplay) so nobody thinks a demo row paid.
 * PURE: no DOM, no three, no fetch. tape.js is untouched: this stands beside it, never inside it. */

import { flowPlan } from './feel.js';
import { kindOf } from './symbols.js';
import { stopsFor } from './tape.js';

export const DEMO_KIND = 'demo';

/** The rows free play deals, each with the fx the server's table gives that line (mock-server.js LINES, table
 *  v7). Weighted toward the spiral, so the sinking is what repeats; `none` rows are the GIF tease (feel.teaseGif:
 *  exactly two GIFs flash once), so a free spin is never dead. */
export const DEMO_ROWS = Object.freeze([
  { line: 'spiral3', fx: ['fx.spiral_full'], weight: 4 },
  { line: 'spiral2', fx: ['fx.spiral_brief'], weight: 5 },
  { line: 'sub2', fx: ['fx.sub_pair'], weight: 4 },
  { line: 'sub3', fx: ['fx.sub_cascade'], weight: 2 },
  { line: 'gif3', fx: ['fx.gif_burst'], weight: 3 },
  { line: 'gif3same', fx: ['fx.gif_storm'], weight: 1 },
  { line: 'none', fx: [], weight: 3 },
]);

const ofKind = (strip, kind) => (Array.isArray(strip) ? strip.filter(s => kindOf(s).kind === kind) : []);
const pickFrom = (list, rnd) => (list.length ? list[Math.min(list.length - 1, Math.floor(rnd() * list.length))] : null);
const pick = (strip, kind, rnd) => pickFrom(ofKind(strip, kind), rnd);
/** A filler cell that cannot promote the row: a gif or a sub, never emi, melt or the line's own kind. */
const filler = (strip, not, rnd) => pick(strip, pickFrom(['gif', 'sub'].filter(k => k !== not), rnd) || 'gif', rnd);

/** The symbols for one demo line off `strips`, or null when the strips cannot show that line. */
export function dealRow(line, strips, rnd = Math.random) {
  if (!Array.isArray(strips) || strips.length < 3) return null;
  const [a, b, c] = strips;
  const three = kind => [pick(a, kind, rnd), pick(b, kind, rnd), pick(c, kind, rnd)];
  const two = kind => {
    const odd = Math.floor(rnd() * 3), row = [0, 1, 2].map(i => (i === odd ? filler(strips[i], kind, rnd) : pick(strips[i], kind, rnd)));
    return row;
  };
  let row;
  switch (line) {
    case 'spiral3': row = three('spiral'); break;
    case 'sub3': row = three('sub'); break;
    case 'spiral2': row = two('spiral'); break;
    case 'sub2': row = two('sub'); break;
    case 'gif3': {
      row = three('gif');
      if (row.every(Boolean) && row[0] === row[1] && row[1] === row[2]) {   // three of the same is the storm, not this row
        const other = ofKind(c, 'gif').filter(s => s !== row[0]);
        row[2] = other.length ? pickFrom(other, rnd) : null;
      }
      break;
    }
    case 'gif3same': {
      const common = ofKind(a, 'gif').filter(s => ofKind(b, 'gif').includes(s) && ofKind(c, 'gif').includes(s));
      const id = pickFrom(common, rnd);
      row = id ? [id, id, id] : [null, null, null];
      break;
    }
    case 'none': {   // the tease: exactly two GIFs and one spiral, which lineFor reads as `none`
      const odd = Math.floor(rnd() * 3);
      row = [0, 1, 2].map(i => (i === odd ? pick(strips[i], 'spiral', rnd) : pick(strips[i], 'gif', rnd)));
      break;
    }
    default: return null;
  }
  return row.every(Boolean) ? row : null;
}

/** THE ENDING is not advertised while a hold plays: the "spins left" counter and the ending copy show on the
 *  idle screen only. `unlockAt` is flow()'s next-press unlock (feel.unlockMs from the landing frame). */
export function counterShown({ pace = 'idle', unlockAt = 0, now = 0 } = {}) {
  return pace === 'idle' && !(now < unlockAt);
}

/**
 * One free-play dealer for a sit-down. `press({ strips, melt })` answers a tape-shaped step:
 * `{ kind: 'play', from: 'demo', demo: true, held: null, outcome }`, and the outcome carries `pay: 0`,
 * `kind: 'demo'`, the strips' stops, the line's fx and the melt it was told (so the readouts do not move).
 */
export function createFreePlay({ random = Math.random } = {}) {
  let i = 0;
  const total = DEMO_ROWS.reduce((s, r) => s + r.weight, 0);
  const weighted = () => {
    let at = random() * total;
    for (const r of DEMO_ROWS) { at -= r.weight; if (at < 0) return r; }
    return DEMO_ROWS[DEMO_ROWS.length - 1];
  };
  return {
    press({ strips = null, melt = 0 } = {}) {
      let row = weighted(), symbols = dealRow(row.line, strips, random);
      for (const r of DEMO_ROWS) { if (symbols) break; row = r; symbols = dealRow(r.line, strips, random); }
      if (!symbols) return { kind: 'refused', reason: 'empty' };
      const subs = symbols.filter(s => kindOf(s).kind === 'sub');
      const outcome = { i: i++, kind: DEMO_KIND, demo: true, stops: stopsFor(strips, symbols), symbols, line: row.line, pay: 0,
                        halved: false, meltLeft: Math.max(0, Math.floor(Number(melt) || 0)), freeLeft: 0, fx: [...row.fx], subs };
      return { kind: 'play', from: 'demo', demo: true, held: null, outcome };
    },
    /** The same client-side plan a paid row gets (station.js flow() builds it from the same call). */
    plan: o => flowPlan(o),
    count: () => i,
  };
}
