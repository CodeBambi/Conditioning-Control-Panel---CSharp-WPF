/* tape.js - the slot's tape client (CONTRACT.md sections 2, 3, 9.1). PURE: no DOM, no three, no
 * fetch; request(op, body, idem?), sleep(ms), mint() and onMelt(left) arrive injected, so node:test
 * drives it against mock-server.js.
 *
 * The server has already settled every outcome it hands us. This file only decides WHICH settled
 * outcome plays next and what the readouts say meanwhile (Law I). Two queues: `side` (a freeze spin
 * and whatever it expanded into) drains before `main` (the bought tape) resumes, so a freeze bought
 * mid-tape never skips, voids or reorders a tape outcome, and never moves the tape's cursor. */

import { compOffer } from './feel.js';

export const TAPE_DEFAULT = 10;   // the default tape at a healthy balance (50 SP and up)
export const TAPE_MAX = 20;       // the most spins the server sells in one tape

/** Owner rule: a small balance gets a short default tape so it lasts. clamp(floor(sp / 5), 1, 10). */
export function defaultTapeCount(sp) {
  const n = Math.floor((Number(sp) || 0) / 5);
  return Math.max(1, Math.min(TAPE_DEFAULT, n));
}

/** Any count the page offers or sends: whole, at most TAPE_MAX, never above the balance (1 SP a spin).
 *  0 means not even one spin is affordable. */
export function affordableTapeCount(count, sp) {
  const want = Math.floor(Number(count) || 0), have = Math.floor(Number(sp) || 0);
  return Math.max(0, Math.min(TAPE_MAX, want, have));
}

const RETRYABLE = new Set(['timeout', 'offline', 'busy']);
const MAX_RETRIES = 3;     // per press, for timeout/offline/busy
const MAX_WAITS = 12;      // per press, for too_fast (the floor is honest, this only stops a loop)

export function mintId() {
  const bytes = new Uint8Array(16);
  globalThis.crypto.getRandomValues(bytes);
  return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
}

/** station-result -> {ok, reason, body}. A host refusal and a server refusal read the same. */
export function normalize(res) {
  const body = (res && typeof res.body === 'object' && res.body) || {};
  const ok = !!res && res.ok !== false && body.ok !== false;
  return { ok, reason: ok ? null : (res && res.reason) || body.reason || 'unknown', body };
}

/** THE BANK formula (section 2): the server balance minus every win still on a tape. */
export function shownSpOf(serverSp, ...tapes) {
  return tapes.reduce((s, t) => s - (t ? t.outcomes.slice(t.played).reduce((a, o) => a + (o.pay || 0), 0) : 0), serverSp);
}

/** Rest stop per reel for a row of symbol ids (first cell carrying that id). */
export function stopsFor(strips, shown) {
  return [0, 1, 2].map(r => {
    const k = strips && strips[r] && shown ? strips[r].indexOf(shown[r]) : -1;
    return k < 0 ? 0 : k;
  });
}

function adoptTape(t) {
  if (!t || !Array.isArray(t.outcomes)) return null;
  const played = Math.max(0, Math.min(t.outcomes.length, Math.floor(Number(t.played) || 0)));
  return { id: String(t.id || ''), played, outcomes: t.outcomes };
}

const unplayed = t => !!t && t.played < t.outcomes.length;

export function createTape({ request, sleep = ms => new Promise(r => setTimeout(r, ms)),
                             mint = mintId, onMelt = () => {}, chaseSession = null }) {
  let sp = 0, table = null, strips = null, shown = null, floorMs = 800;
  let main = null, side = null, wheelChase = null;
  let chaseEnabled = !!chaseSession;
  let hold = null;          // ONE frozen column (9.1), null when none
  let pending = null;       // { key, idem, op, body }: kept until a definitive answer
  let melt = 0, free = 0, lastWin = 0, last = null;
  // 10.16: the jar count and the welcome-back comp, both the server's (Law I). `jar` follows the tape cursor
  // exactly as `melt` does; `comp` is spent on the first tape BUY of the sit-down and never comes back.
  let jar = 0, comp = null, compSpent = false;
  let epoch = 0;            // abort() bumps it; a press from an older epoch resolves 'aborted'
  let reported = null;
  let picked = null;        // a count the player chose this sit-down; null follows defaultTapeCount(sp)

  const tapeCount = () => affordableTapeCount(picked ?? defaultTapeCount(sp), sp);

  function reportMelt() {
    if (melt === reported) return;
    reported = melt;
    onMelt(melt);
  }

  function adopt(body) {
    if (Number.isFinite(body.sp)) sp = body.sp;
    if (body.table) table = body.table;
    if (body.wheelChase?.id && Array.isArray(body.wheelChase.symbols)) {
      // Credit totals settle ahead of playback. Expose only the target, never future bonus awards.
      const { id, symbols, oneIn } = body.wheelChase;
      wheelChase = { id, symbols: symbols.slice(0, 3), oneIn };
    }
    if (Number.isFinite(body.jackpot) && table) table = { ...table, jackpot: body.jackpot };
    if (Array.isArray(body.strips)) strips = body.strips;
    if (Array.isArray(body.shown)) shown = body.shown;
    if (Number.isFinite(body.floorMs)) floorMs = body.floorMs;
  }

  /** GET state. Resumes an unplayed tape and carries melt over from the server. */
  async function open() {
    const my = ++epoch;
    pending = null; side = null; hold = null; main = null; last = null; picked = null;
    jar = 0; comp = null; compSpent = false;
    let res, tries = 0;
    for (;;) {
      res = normalize(await request('state', {}));
      if (my !== epoch) return { ok: false, reason: 'aborted' };
      if (res.ok || !RETRYABLE.has(res.reason) || tries++ >= MAX_RETRIES) break;
      await sleep(400 * tries);
      if (my !== epoch) return { ok: false, reason: 'aborted' };
    }
    if (!res.ok) return { ok: false, reason: res.reason };
    adopt(res.body);
    main = adoptTape(res.body.tape);
    // Played part of a stored tape: the readouts continue from its last landed spin.
    // Nothing played yet: the server's own melt (section 3.1) is the carried state.
    last = main && main.played > 0 ? main.outcomes[main.played - 1] : null;
    melt = last ? last.meltLeft || 0 : Number(res.body.melt) || 0;
    // 10.16.A: the jar the tape's last landed spin left behind, else the stored count off `state`.
    jar = last && Number.isFinite(Number(last.jarN)) ? Math.max(0, Math.floor(Number(last.jarN))) : Math.max(0, Math.floor(Number(res.body.jar) || 0));
    comp = compOffer(res.body.comp);   // 10.16.C: `state` reports what the room's visit ping stored
    free = last ? last.freeLeft || 0 : 0;
    lastWin = last ? last.pay || 0 : 0;
    if (chaseEnabled) {
      const chase = await send('chase', 'chase', { session: chaseSession }, my);
      if (chase.kind) return { ok: false, reason: chase.kind };
      if (!chase.ok && ['bad_op', '404', 'http_404'].includes(String(chase.reason))) { chaseEnabled = false; wheelChase = null; }
      else if (!chase.ok) return { ok: false, reason: chase.reason };
      else adopt(chase.body);
    }
    reported = null;
    reportMelt();
    return { ok: true };
  }

  function next() {
    if (unplayed(side)) return side.outcomes[side.played];
    if (unplayed(main)) return main.outcomes[main.played];
    return null;
  }

  const freezeCost = () => { const f = Number(table && table.freezeCost); return 1 + (table && table.freezeCost != null && Number.isFinite(f) ? f : 1); };
  const canFreeze = () => !unplayed(side);

  /** Send one intent, reusing its idem verbatim on every retry of that same intent. */
  async function send(key, op, body, my) {
    if (!pending || pending.key !== key) pending = { key, idem: mint(), op, body };
    let tries = 0, waits = 0;
    for (;;) {
      const res = normalize(await request(pending.op, pending.body, pending.idem));
      if (my !== epoch) return { kind: 'aborted' };
      if (res.ok) { pending = null; return { ok: true, body: res.body }; }
      let wait = 0;
      if (res.reason === 'too_fast' && waits++ < MAX_WAITS) {
        wait = Math.min(20000, Math.max(50, Number(res.body.retryInMs) || floorMs));
      } else if (RETRYABLE.has(res.reason) && tries++ < MAX_RETRIES) {
        wait = 400 * tries;
      } else {
        if (!RETRYABLE.has(res.reason) && res.reason !== 'too_fast') pending = null;
        if (Number.isFinite(res.body.sp)) sp = res.body.sp;
        return { ok: false, reason: res.reason, body: res.body };
      }
      await sleep(wait);   // silently: the page shows nothing for a floor wait
      if (my !== epoch) return { kind: 'aborted' };
    }
  }

  /** The next outcome, and which queue it comes off (a freeze tape drains first). */
  const play = () => {
    const outcome = next(), fromSide = unplayed(side);
    // A freeze and its re-spins hold the column (10.3); free spins it won draw all three reels.
    const held = fromSide && (outcome.kind === 'freeze' || outcome.kind === 'respin') ? side.col : null;
    return { kind: 'play', outcome, from: fromSide ? 'side' : 'main', held };
  };

  const cursorBody = () => (main ? { cursor: { tapeId: main.id, played: main.played } } : {});
  const refuse = reason => ({ kind: 'refused', reason });

  /** 10.16.C: the comp tape. 0 SP, so a player at 0 SP can still play it, which is the whole point; the
   *  server draws its five spins from the normal plain table and `settle()` clears the stored comp. */
  async function buyComp(my) {
    const id = comp.id;
    const r = await send(`comp:${id}`, 'tape', { comp: id, ...cursorBody() }, my);
    if (r.kind) return r;
    if (!r.ok && (r.reason === 'comp_none' || r.reason === 'comp_used')) {
      comp = null; compSpent = true;         // already spent, or from an older grant: fall back to a paid tape
      return buyTape(my);
    }
    if (!r.ok && r.reason === 'tape_unplayed' && r.body.tape) {
      adopt(r.body);                          // finish the tape you have; the comp is untouched and waits
      main = adoptTape(r.body.tape);
      return next() ? play() : refuse('tape_unplayed');
    }
    if (!r.ok) return refuse(r.reason);
    adopt(r.body);
    comp = null; compSpent = true;
    main = adoptTape(r.body.tape);
    return next() ? play() : refuse('empty');
  }

  async function buyTape(my) {
    if (comp) return buyComp(my);
    const count = tapeCount();
    if (count < 1) return refuse('insufficient');
    if (chaseEnabled && pending?.op !== 'tape') {
      const chase = await send('chase', 'chase', { session: chaseSession, ...cursorBody() }, my);
      if (chase.kind) return chase;
      if (!chase.ok && ['bad_op', '404', 'http_404'].includes(String(chase.reason))) { chaseEnabled = false; wheelChase = null; }
      else if (!chase.ok) return refuse(chase.reason);
      else adopt(chase.body);
    }
    const r = await send('tape', 'tape', { count, ...cursorBody() }, my);
    if (r.kind) return r;
    if (!r.ok && r.reason === 'tape_unplayed' && r.body.tape) {
      adopt(r.body);
      main = adoptTape(r.body.tape);
      return next() ? play() : refuse('tape_unplayed');
    }
    if (!r.ok) return refuse(r.reason);
    adopt(r.body);
    main = adoptTape(r.body.tape);
    return next() ? play() : refuse('empty');
  }

  async function buyFreeze(my) {
    const col = hold;
    if (!canFreeze()) return refuse('busy');
    if (sp < freezeCost()) return refuse('insufficient');
    const r = await send(`freeze:${col}`, 'tape', { count: 1, freeze: { col }, ...cursorBody() }, my);
    if (r.kind) return r;
    if (!r.ok) return refuse(r.reason);
    adopt(r.body);
    // 10.10: the spins are in `freeze.outcomes`; `tape` is only the stored tape's {id, played}, so `main`
    // and its cursor are left exactly where they are and resume once the freeze has played out.
    const f = r.body.freeze;
    side = f && Array.isArray(f.outcomes) && f.outcomes.length
      ? { id: `freeze:${f.col}`, played: 0, outcomes: f.outcomes, col: Number.isInteger(f.col) ? f.col : col } : null;
    hold = null;
    return next() ? play() : refuse('empty');
  }

  return {
    open,
    next,
    /** One press = one outcome (section 8). Buys a tape or a freeze spin only when needed. */
    async press() {
      const my = epoch;
      if (hold !== null) return buyFreeze(my);
      return next() ? play() : buyTape(my);
    },
    /** The reels have stopped on `outcome`: its pay lands, readouts move to its after-state. */
    land(outcome) {
      let fromMain = false;
      if (unplayed(side) && side.outcomes[side.played] === outcome) {
        side.played++;
        if (!unplayed(side)) side = null;
      } else if (unplayed(main) && main.outcomes[main.played] === outcome) {
        main.played++;
        fromMain = true;
      } else {
        return false;
      }
      last = outcome;
      // The shown melt follows the tape cursor. A freeze and everything it expands into are sealed from
      // melt (10.2) and carry the melt left at the END of the stored tape (3.4, settled up front), so a
      // freeze landing mid-tape leaves the readout where the tape's last landed spin put it.
      if (fromMain) melt = outcome.meltLeft || 0;
      // 10.16.A: every outcome carries the jar AFTER it, a freeze carrying back the one it did not change.
      if (Number.isFinite(Number(outcome.jarN))) jar = Math.max(0, Math.floor(Number(outcome.jarN)));
      free = outcome.freeLeft || 0;
      lastWin = outcome.pay || 0;
      if (Array.isArray(outcome.symbols)) shown = outcome.symbols;
      reportMelt();
      return true;
    },
    /** Light a column; lighting another moves the hold, lighting the same one clears it. */
    toggleHold(col) {
      if (![0, 1, 2].includes(col)) return hold;
      hold = hold === col ? null : col;
      return hold;
    },
    clearHold() { hold = null; },
    /** The player picks a tape length (1..TAPE_MAX) for this sit-down; null goes back to the default.
     *  Returns the count the next tape would actually buy. */
    pickCount(n) {
      const v = Math.floor(Number(n));
      picked = n == null || !(v >= 1) ? null : Math.min(TAPE_MAX, v);
      return tapeCount();
    },
    abort() { epoch++; },
    setServerSp(v) { if (Number.isFinite(v)) sp = v; },
    cursor() { return main ? { tapeId: main.id, played: main.played } : null; },
    snapshot() {
      const n = next();
      return {
        sp, shownSp: shownSpOf(sp, main, side), melt, free, lastWin, last, hold, wheelChase,
        // 10.16: the jar as of the cursor, the table's jar block, and the comp still standing.
        jar, jarSize: (table && table.jar && table.jar.size) || 0, jarFree: (table && table.jar && table.jar.free) || 0,
        comp, compSpent, jackpotOdds: (table && table.jackpotOdds) || null,
        // What the chip still owes (Law I): every unplayed pay, and the stored tape's share alone (a reopen sees only that).
        owed: sp - shownSpOf(sp, main, side), tapeOwed: sp - shownSpOf(sp, main),
        jackpot: table ? table.jackpot : 0, lines: table ? table.lines || [] : [],
        stake: table ? table.stake || 1 : 1, freezeCost: freezeCost(), canFreeze: canFreeze(),
        strips, shown, floorMs, nextKind: n ? n.kind : null, tapeCount: tapeCount(), tapePicked: picked,
        onTape: (unplayed(main) ? main.outcomes.length - main.played : 0) +
                (unplayed(side) ? side.outcomes.length - side.played : 0),
      };
    },
  };
}
