/* ============================================================================
 * game/iq.js - the IQ score: what the player's own moves cost them.
 *
 * Every game starts at IQ.start. Each move a LOCAL player makes is graded
 * against the best move this engine can see in the same position (search.js
 * gradeMove, run in game/grade-worker.js), and the centipawns it gave away come
 * off the score: small slips are free, a hung piece hurts, no single move costs
 * more than IQ.cap, and the score never goes under IQ.floor. It only ever goes
 * down. A good move keeps what you have; nothing gives it back.
 *
 * Only the local seats are graded. Online, the opponent's moves never reach
 * the grader, and nothing on this page ever holds their score.
 *
 *   Grade = { ply, side, san, best, cp, loss, value }
 *     ply   rules.ply() after the move (1-based half-move count)
 *     best  the grader's best move in the same position, SAN (= san when it was best)
 *     cp    centipawns given away, >= 0
 *     loss  IQ points the move cost, before the floor; value: IQ after the move
 *
 * iqLoss and createIqTracker are pure (smoke/iq-smoke.mjs). createIqLive drives
 * the worker off the bus and is what boot.js hangs on window.PBP.iq.
 * ==========================================================================*/

/** Every number the score is made of. free: centipawns that cost nothing;
 *  bend: how fast a bigger blunder runs up to the cap. */
export const IQ = Object.freeze({ start: 140, floor: 40, free: 50, cap: 25, bend: 300 });

/** Centipawns given away -> whole IQ points. 0 under `free`; a hung knight is
 *  about 16, a hung queen 24, a missed mate the cap. */
export function iqLoss(cp, t = IQ) {
  const over = Number(cp) - t.free;
  if (!(over > 0)) return 0;
  return Math.min(t.cap, Math.round(t.cap * (1 - Math.exp(-over / t.bend))));
}

/** One side's fall: the grades in ply order and the score they leave. */
export function createIqTracker({ tuning = IQ } = {}) {
  let moves = [];
  function recount() {
    let v = tuning.start;
    for (const g of moves) {
      g.loss = iqLoss(g.cp, tuning);
      v = Math.max(tuning.floor, v - g.loss);
      g.value = v;
    }
  }
  const value = () => (moves.length ? moves[moves.length - 1].value : tuning.start);
  /** The move that cost the most; a bigger cp breaks a tie, then the earlier one. */
  function worst() {
    let w = null;
    for (const g of moves) if (g.loss > 0 && (!w || g.loss > w.loss || (g.loss === w.loss && g.cp > w.cp))) w = g;
    return w ? { ...w } : null;
  }
  return {
    /** A graded move. A second grade for the same ply replaces the first. */
    note({ ply, side, san, best, cp }) {
      const g = { ply: Number(ply) || 0, side, san: String(san || ''), best: String(best || san || ''), cp: Math.max(0, Math.round(Number(cp) || 0)), loss: 0, value: 0 };
      moves = moves.filter(m => m.ply !== g.ply);
      moves.push(g);
      moves.sort((a, b) => a.ply - b.ply);
      recount();
      return { ...g };
    },
    /** A take-back: every grade past `ply` goes with the moves it graded. */
    dropAfter(ply) { moves = moves.filter(m => m.ply <= ply); recount(); },
    has: (ply, san) => moves.some(m => m.ply === ply && m.san === san),
    get value() { return value(); },
    worst,
    track: () => ({ start: tuning.start, value: value(), low: value(), moves: moves.map(m => ({ ...m })) }),
    toRecord: () => ({ start: tuning.start, end: value(), low: value(), worst: worst(), moves: moves.map(m => ({ ...m })) }),
    restore(list) { moves = []; for (const g of list || []) if (g && Number(g.ply) > 0) moves.push({ ...g, cp: Math.max(0, Number(g.cp) || 0) }); moves.sort((a, b) => a.ply - b.ply); recount(); },
    reset() { moves = []; },
  };
}

const SIDES = ['w', 'b'];
const STUCK_MS = 3000;   // a grade that has not come back by then is dropped and the next one goes

/**
 * createIqLive({ bus, game, workerFactory }) - the page side.
 *
 * Listens for `local` (which seats are ours), `turn` and `gameover` (grade
 * what is new), `takeback` and `newgame`. Grades go one at a time, in ply
 * order, and each lands as a bus `iq` event. Every landing is checked against
 * the game's own history, so a grade for a move that was taken back, or for a
 * game that has since been replaced, is dropped rather than booked.
 *
 * Returns window.PBP.iq: { start, sides, value, track, worst, settled } plus
 * record() / restore() for the shelf and the solo save.
 */
export function createIqLive({ bus, game, tuning = IQ, workerFactory = () => new Worker(new URL('./grade-worker.js', import.meta.url), { type: 'module' }) } = {}) {
  const trackers = { w: createIqTracker({ tuning }), b: createIqTracker({ tuning }) };
  let sides = [];
  let queue = [];
  let busy = null;          // { req, timer }
  let epoch = 0, serial = 0;
  let worker = null, broken = false;
  let waiters = [];

  const history = () => { try { return game.rules.chess.history({ verbose: true }); } catch { return []; } };
  const idle = () => !busy && !queue.length;
  function wake() { if (!idle()) return; const w = waiters; waiters = []; for (const r of w) r(); }

  function reset() {
    epoch++;
    queue = [];
    for (const s of SIDES) trackers[s].reset();
    wake();
  }

  /** The seats the switch says are ours right now (an online seat can be corrected after the deal). */
  function refreshSides() {
    let live = null;
    try { live = game.seats; } catch { live = null; }
    if (!Array.isArray(live) || !live.length) return;
    const next = live.filter(s => SIDES.includes(s));
    for (const s of sides) if (!next.includes(s)) trackers[s].reset();
    sides = next;
  }

  /** Queue every local move in the history that has no grade yet. */
  function sync() {
    refreshSides();
    if (broken || !sides.length) return;
    const list = history();
    for (let i = 0; i < list.length; i++) {
      const mv = list[i], ply = i + 1;
      if (!sides.includes(mv.color) || trackers[mv.color].has(ply, mv.san)) continue;
      if ((busy && busy.req.ply === ply && busy.req.epoch === epoch) || queue.some(q => q.ply === ply)) continue;
      queue.push({ ply, side: mv.color, san: mv.san, fen: mv.before, move: { from: mv.from, to: mv.to, promotion: mv.promotion }, epoch });
    }
    pump();
  }

  function pump() {
    if (busy || broken) return;
    const req = queue.shift();
    if (!req) { wake(); return; }
    if (req.epoch !== epoch) { pump(); return; }
    try { worker ||= workerFactory(); } catch { fail(); return; }
    const id = ++serial;
    const timer = setTimeout(() => {
      if (!busy || busy.id !== id) return;
      try { worker.terminate(); } catch { /* gone */ }
      worker = null; busy = null; pump();
    }, STUCK_MS);
    timer?.unref?.();   // a node smoke never waits on it
    busy = { id, req, timer };
    worker.onmessage = ({ data }) => { if (data && data.id === id) land(id, data); };
    worker.onerror = fail;
    try { worker.postMessage({ id, fen: req.fen, move: req.move }); } catch { fail(); }
  }

  function land(id, data) {
    if (!busy || busy.id !== id) return;
    const { req, timer } = busy;
    clearTimeout(timer);
    busy = null;
    const now = history()[req.ply - 1];
    // still the same move in the same game, on a seat that is still ours
    if (req.epoch === epoch && !data.error && data.best && now && now.before === req.fen && now.san === req.san && sides.includes(req.side)) {
      const g = trackers[req.side].note({ ply: req.ply, side: req.side, san: req.san, best: data.best, cp: data.cp });
      if (bus) bus.emit('iq', g);
    }
    pump();
  }

  function fail() {
    if (broken) return;
    broken = true;
    try { console.warn('[pbp] the IQ grader could not start; the score stays where it is'); } catch { /* no console */ }
    if (busy) clearTimeout(busy.timer);
    try { worker?.terminate(); } catch { /* gone */ }
    worker = null; busy = null; queue = [];
    wake();
  }

  const offs = [];
  if (bus) {
    offs.push(bus.on('newgame', reset));
    offs.push(bus.on('local', (p) => {
      reset();
      sides = Array.isArray(p?.sides) ? p.sides.filter(s => SIDES.includes(s)) : [];
    }));
    offs.push(bus.on('turn', sync));
    offs.push(bus.on('gameover', sync));
    offs.push(bus.on('takeback', (p) => {
      const ply = Number(p?.ply) || 0;
      for (const s of SIDES) trackers[s].dropAfter(ply);
      queue = queue.filter(q => q.ply <= ply);
    }));
  }

  const mine = (side) => sides.includes(side);
  return {
    start: tuning.start,
    sides: () => sides.slice(),
    value: (side) => (mine(side) ? trackers[side].value : null),
    track: (side) => (mine(side) ? trackers[side].track() : null),
    worst: (side) => (mine(side) ? trackers[side].worst() : null),
    /** Resolves once every queued grade is in, or after `ms`, whichever is first. */
    settled(ms = 2500) {
      if (idle()) return Promise.resolve();
      return new Promise((resolve) => { waiters.push(resolve); setTimeout(resolve, ms)?.unref?.(); });
    },
    /** { w?, b? } for the local seats, the shape the shelf and the solo save keep; null with no seat. */
    record() {
      if (!sides.length) return null;
      const out = {};
      for (const s of sides) out[s] = trackers[s].toRecord();
      return out;
    },
    /** A saved fall, kept only where it still matches the moves on the board. */
    restore(saved) {
      const list = history();
      for (const s of sides) {
        const ok = (saved?.[s]?.moves || []).filter(g => list[g.ply - 1] && list[g.ply - 1].color === s && list[g.ply - 1].san === g.san);
        trackers[s].restore(ok);
      }
    },
    dispose() {
      for (const off of offs) { try { off(); } catch { /* gone */ } }
      if (busy) clearTimeout(busy.timer);
      try { worker?.terminate(); } catch { /* gone */ }
      worker = null; busy = null; queue = [];
      wake();
    },
  };
}
