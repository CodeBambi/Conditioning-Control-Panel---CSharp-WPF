import { createHotseat } from './hotseat.js';
import { soloOptions, saveSolo } from './save.js';
import { handoffSeconds } from '../ui/turn-handoff.js';

/** A local human seat around the same referee and animation path as two-player play. */
export function createSolo({ bus, board, hud = null, options = {}, restore = null, workerFactory = () => new Worker(new URL('./solo-worker.js', import.meta.url), { type: 'module' }) }) {
  const setup = soloOptions(restore?.options || options);
  const me = restore?.me || (setup.side === 'random' ? (Math.random() < .5 ? 'w' : 'b') : setup.side);
  const local = createHotseat({ bus, board: { ...board, setSide: (_side, instant) => board.setSide(me, instant) }, hud,
    clockMs: setup.clockMs, restore, fen: restore?.fen });
  let worker = null, disposed = false, started = false, pending = false, serial = 0, reply = null, quietFor = 0;
  const startedAt = Date.now(), elapsed = Number(restore?.durationMs) || 0;
  function record() { return { ...local.record(), mode: 'solo', me, options: setup, fen: restore?.fen,
    opponent: 'Computer · ' + ({ relaxed: 'Relaxed', club: 'Club', sharp: 'Sharp' }[setup.level]), durationMs: elapsed + Date.now() - startedAt }; }
  function save() { if (started) saveSolo(record()); }
  function stopThinking() {
    serial++; pending = false;
    reply = null; quietFor = 0;
    if (worker) worker.terminate(); worker = null;
    bus.emit('thinking', { active: false });
  }
  function receive(id, fen, move) {
    if (disposed || id !== serial || local.rules.fen() !== fen || local.isOver() || !pending) return;
    reply = { id, fen, move };
    bus.emit('thinking', { active: false });
  }
  function playReply(dt) {
    if (!pending) return;
    quietFor = board.anim?.busy?.() ? 0 : quietFor + Math.max(0, dt);
    if (!reply) return;
    const wait = handoffSeconds(local.clock);
    if (local.plies() && wait > 0) {
      if (board.anim?.busy?.()) return;
      if (board.turnHandoff ? !board.turnHandoff.ready() : quietFor < wait) return;
    }
    if (!wait) board.anim?.skip?.();
    const result = reply;
    reply = null; pending = false;
    if (disposed || result.id !== serial || local.rules.fen() !== result.fen || local.isOver()) return;
    if (result.move) local.tryMove(result.move.from, result.move.to, result.move.promotion || 'q');
    save();
  }
  function think() {
    if (!started || disposed || pending || local.isOver() || local.turn() === me) return;
    pending = true; quietFor = 0;
    const id = ++serial, fen = local.rules.fen();
    bus.emit('thinking', { active: true });
    // Search while the previous move is still performing; only applying its reply waits.
    const fallback = () => {
      if (disposed || id !== serial || local.rules.fen() !== fen) return;
      if (worker) worker.terminate(); worker = null;
      const moves = local.rules.chess.moves({ verbose: true });
      receive(id, fen, moves[Math.floor(Math.random() * moves.length)]);
      bus.emit('notice', { text: 'Computer search unavailable. Playing a simple move.' });
    };
    try {
      worker ||= workerFactory();
      worker.onmessage = ({ data }) => { if (data.id === id) data.error ? fallback() : receive(id, fen, data.move); };
      worker.onerror = fallback;
      worker.postMessage({ id, fen: restore?.fen, moves: local.record().moves, level: setup.level });
    } catch { fallback(); }
  }
  const offEnd = bus.on('gameover', () => { if (started && local.isOver()) { stopThinking(); save(); } });
  const leave = () => save();
  if (typeof window !== 'undefined') window.addEventListener('pagehide', leave);
  return {
    ...local, seats: [me], isSolo: true, settings: setup, record,
    start() { started = true; local.start(); save(); think(); },
    update(dt) { local.update(dt); playReply(dt); think(); },
    canPick(square) { return local.turn() === me && local.canPick(square); },
    legalTargets(square) { return local.turn() === me ? local.legalTargets(square) : []; },
    tryMove(from, to, promotion) {
      if (local.turn() !== me) return null;
      const move = local.tryMove(from, to, promotion); if (move) { save(); think(); } return move;
    },
    takeBack() {
      if (local.isOver() || !local.plies()) return null;
      stopThinking();
      const now = Date.now(), back = local.takeBack(now);
      if (back && local.turn() !== me && local.plies()) local.takeBack(now + 401);
      save(); think(); return back;
    },
    resign() { stopThinking(); local.resign(me); save(); },
    reset() { stopThinking(); local.reset(); save(); },
    dispose() {
      save(); disposed = true; stopThinking(); offEnd(); local.clock.stop();
      if (typeof window !== 'undefined') window.removeEventListener('pagehide', leave);
    },
  };
}
