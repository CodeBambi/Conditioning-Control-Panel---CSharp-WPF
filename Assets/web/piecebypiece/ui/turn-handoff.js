// A short visual handoff. It never changes turns, clocks, or human input.
export function handoffSeconds(clock) {
  const state = clock?.snapshot?.();
  if (!state || state.untimed || state.total === 0) return .65;
  const least = Math.min(state.w ?? Infinity, state.b ?? Infinity);
  return least < 10000 ? 0 : least < 30000 ? .20 : .65;
}

export function createTurnHandoff({ bus, game, board, root = null, menuOpen = () => false }) {
  let lastPly = null, pending = null;
  const card = typeof document === 'undefined' ? null : document.createElement('div');
  if (card) {
    card.className = 'turn-handoff';
    card.setAttribute('role', 'status');
    card.setAttribute('aria-live', 'polite');
    card.style.cssText = 'position:fixed;z-index:20;left:50%;top:22%;transform:translateX(-50%);pointer-events:none;padding:13px 24px;border:1px solid #b89be5;border-radius:16px;background:#231a35f5;color:#fff;font:600 18px system-ui;box-shadow:0 10px 32px #0005;text-align:center';
    card.hidden = true;
    (root || document.body).append(card);
  }
  function clear() { pending = null; if (card) card.hidden = true; }
  function reset() { clear(); lastPly = null; }
  function label(side) {
    if (game.isSolo || game.isOnline) {
      if (game.seats?.includes(side)) return 'Your turn';
      return game.isSolo ? "Computer's turn" : "Opponent's turn";
    }
    return side === 'w' ? 'White to move' : 'Black to move';
  }
  const off = [bus.on('turn', ({ side, ply }) => {
    const previous = lastPly;
    lastPly = ply;
    if (previous === ply) return;
    if (previous === null || ply < previous || game.isOver() || menuOpen()) { clear(); return; }
    pending = { side, ply, age: 0, shown: false };
    if (card) card.hidden = true;
  }), ...['local', 'newgame', 'gameover', 'menu', 'replay'].map(name => bus.on(name, reset)),
  bus.on('takeback', () => { clear(); lastPly = game.plies(); })];
  return {
    clear: reset,
    ready: () => !pending || handoffSeconds(game.clock) === 0,
    update(dt) {
      if (menuOpen() || game.isOver()) { reset(); return; }
      if (!pending) return;
      const duration = handoffSeconds(game.clock);
      if (!duration) { clear(); return; }
      if (board.anim?.busy?.()) { pending.age = 0; return; }
      if (!pending.shown) {
        pending.shown = true;
        const text = label(pending.side);
        if (card) {
          card.textContent = text; card.hidden = false;
          const still = globalThis.window?.PBP?.reducedMotion || globalThis.window?.PBP?.settings?.reducedMotion || globalThis.window?.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
          if (!still) card.animate?.([{ opacity: 0, transform: 'translate(-50%, 5px)' }, { opacity: 1, transform: 'translate(-50%, 0)' }], { duration: 140, easing: 'ease-out' });
        }
      }
      pending.age += Math.max(0, dt);
      if (pending.age >= duration) clear();
    },
    dispose() { for (const stop of off) stop(); reset(); card?.remove(); },
    debug: () => pending ? { ...pending, text: label(pending.side) } : null,
  };
}
