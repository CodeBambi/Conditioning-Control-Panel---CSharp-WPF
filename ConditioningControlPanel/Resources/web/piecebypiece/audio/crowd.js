import { Chess } from '../vendor/chess.js';
// Audience reactions to completed moves, never engine evaluations or tactical hints.
const VALUE = { p: 1, n: 3, b: 3, r: 5, q: 9, k: 0 };
function safeBefore(move) {
  if (!move?.before) return false;
  try { const before = new Chess(move.before); return !before.inCheck() && !before.isAttacked(move.from, move.color === 'w' ? 'b' : 'w'); }
  catch { return false; }
}
const quiet = m => m && !m.captured && !m.promotion && !/[+#]/.test(m.san || '');
export function createCrowd({ bus, game, play, cancel = () => {}, canPlay = () => true, settled = () => true,
  now = () => performance.now(), later = setTimeout, clear = clearTimeout } = {}) {
  const history = () => game?.rules?.chess?.history?.({ verbose: true }) || [];
  let seen = history().length, timer = null, pending = null, candidate = null;
  let lastAt = -Infinity, lastKind = '', lastBoo = -Infinity, lastTurnAt = -Infinity, batchUntil = 0;
  const offs = [];
  const on = (type, fn) => { if (bus?.on) offs.push(bus.on(type, fn)); };
  function stop() { if (timer != null) clear(timer); timer = null; pending = null; candidate = null; cancel(); }
  function reset() { stop(); seen = history().length; lastTurnAt = -Infinity; batchUntil = 0; lastAt = lastBoo = -Infinity; }
  function queue(kind, terminal = false) {
    if (pending === 'crowdBoo' && !terminal && kind !== 'crowdBoo') return;
    if (timer != null) clear(timer);
    pending = kind;
    const deadline = now() + 6500;
    const attempt = () => {
      timer = null;
      if (!canPlay()) { pending = null; return; }
      if (!settled()) {
        if (now() < deadline) timer = later(attempt, 120);
        else pending = null;
        return;
      }
      pending = null;
      const t = now(), negative = kind === 'crowdBoo';
      if (!canPlay() || t < batchUntil || (!terminal && !negative && t - lastAt < 7000)
        || (negative && t - lastBoo < 12000)) return;
      if (negative || terminal) cancel();
      if (play(kind)) { lastAt = t; lastKind = kind; if (negative) lastBoo = t; }
    };
    timer = later(attempt, terminal ? 320 : 550);
  }
  on('turn', () => {
    const h = history(), n = h.length, t = now();
    if (n === seen) return;
    if (n !== seen + 1) { reset(); return; }
    if (t - lastTurnAt < 100) { stop(); batchUntil = t + 1200; }
    lastTurnAt = t; seen = n;
    if (!canPlay() || t < batchUntil) { candidate = null; return; }
    const move = h[n - 1], prior = h[n - 2];
    // A heavy piece walked onto the captured square on a quiet move. Wait for
    // its owner's actual reply: a recapture, promotion or check cancels the boo.
    const negative = candidate && n === candidate.ply + 1 && move.color === candidate.side && quiet(move);
    candidate = null;
    if (negative) { queue('crowdBoo'); return; }
    if (move.captured && VALUE[move.captured] >= 5 && quiet(prior) && quiet({ ...move, captured: null })
      && safeBefore(prior) && prior.color !== move.color && prior.to === move.to && prior.piece === move.captured) {
      candidate = { side: prior.color, ply: n };
      // Hold applause until the reply settles a possible unreciprocated loss.
      if (timer != null) clear(timer); timer = null; pending = null;
      return;
    }
    if (move.promotion) queue('crowdCheer');
    else if (move.captured) queue('crowdApplause');
  });
  on('gameover', p => { stop(); if (p?.result === 'checkmate') queue('crowdCheer', true); });
  for (const type of ['newgame', 'local', 'resync', 'menu-request', 'replay-start', 'takeback']) on(type, reset);
  return { cancel: stop, reset, update() { if (!canPlay()) stop(); }, debug: () => ({ seen, pending, candidate: !!candidate, lastKind }),
    dispose() { stop(); for (const off of offs) off(); }, };
}
