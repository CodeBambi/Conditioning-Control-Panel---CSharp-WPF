/* tape.js - the slot's tape client (CONTRACT.md sections 2, 3, 9.1). PURE: no DOM, no three, no
 * fetch; request(op, body, idem?), sleep(ms), mint() and onMelt(left) arrive injected, so node:test
 * drives it against mock-server.js.
 *
 * The server has already settled every outcome it hands us. This file only decides WHICH settled
 * outcome plays next and what the readouts say meanwhile (Law I). Two queues: `side` (a freeze spin
 * and whatever it expanded into) drains before `main` (the bought tape) resumes, so a freeze bought
 * mid-tape never skips, voids or reorders a tape outcome. */

export const TAPE_DEFAULT = 10;
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
                             mint = mintId, onMelt = () => {} }) {
  let sp = 0, table = null, strips = null, shown = null, floorMs = 800;
  let main = null, side = null;
  let hold = null;          // ONE frozen column (9.1), null when none
  let pending = null;       // { key, idem, op, body }: kept until a definitive answer
  let melt = 0, free = 0, lastWin = 0, last = null;
  let epoch = 0;            // abort() bumps it; a press from an older epoch resolves 'aborted'
  let reported = null;

  function reportMelt() {
    if (melt === reported) return;
    reported = melt;
    onMelt(melt);
  }

  function adopt(body) {
    if (Number.isFinite(body.sp)) sp = body.sp;
    if (body.table) table = body.table;
    if (Number.isFinite(body.jackpot) && table) table = { ...table, jackpot: body.jackpot };
    if (Array.isArray(body.strips)) strips = body.strips;
    if (Array.isArray(body.shown)) shown = body.shown;
    if (Number.isFinite(body.floorMs)) floorMs = body.floorMs;
  }

  /** GET state. Resumes an unplayed tape and carries melt over from the server. */
  async function open() {
    const my = ++epoch;
    pending = null; side = null; hold = null; main = null; last = null;
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
    free = last ? last.freeLeft || 0 : 0;
    lastWin = last ? last.pay || 0 : 0;
    reported = null;
    reportMelt();
    return { ok: true };
  }

  function next() {
    if (unplayed(side)) return side.outcomes[side.played];
    if (unplayed(main)) return main.outcomes[main.played];
    return null;
  }

  const freezeCost = () => 1 + (Number(table && table.freezeCost) || 1);
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
  const play = () => ({ kind: 'play', outcome: next(), from: unplayed(side) ? 'side' : 'main' });

  const cursorBody = () => (main ? { cursor: { tapeId: main.id, played: main.played } } : {});
  const refuse = reason => ({ kind: 'refused', reason });

  async function buyTape(my) {
    const count = Math.min(TAPE_DEFAULT, Math.floor(sp));
    if (count < 1) return refuse('insufficient');
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
    const t = adoptTape(r.body.tape);
    if (t && main && t.id === main.id) {
      // Defensive: a server that answers with the stored tape keeps our place in it.
      main = { ...t, played: Math.max(t.played, main.played) };
    } else {
      side = t ? { ...t, played: 0 } : null;
    }
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
      if (unplayed(side) && side.outcomes[side.played] === outcome) {
        side.played++;
        if (!unplayed(side)) side = null;
      } else if (unplayed(main) && main.outcomes[main.played] === outcome) {
        main.played++;
      } else {
        return false;
      }
      last = outcome;
      melt = outcome.meltLeft || 0;
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
    abort() { epoch++; },
    setServerSp(v) { if (Number.isFinite(v)) sp = v; },
    cursor() { return main ? { tapeId: main.id, played: main.played } : null; },
    snapshot() {
      const n = next();
      return {
        sp, shownSp: shownSpOf(sp, main, side), melt, free, lastWin, last, hold,
        jackpot: table ? table.jackpot : 0, lines: table ? table.lines || [] : [],
        stake: table ? table.stake || 1 : 1, freezeCost: freezeCost(), canFreeze: canFreeze(),
        strips, shown, floorMs, nextKind: n ? n.kind : null,
        onTape: (unplayed(main) ? main.outcomes.length - main.played : 0) +
                (unplayed(side) ? side.outcomes.length - side.played : 0),
      };
    },
  };
}
