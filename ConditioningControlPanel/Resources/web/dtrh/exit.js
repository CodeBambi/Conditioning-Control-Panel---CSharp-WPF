/* ============================================================================
 * exit.js - the page's ways out, in one place so they can be executed in a test.
 *
 * There are three of them (the Escape hold, the pause menu's requestExit, the
 * host asking with end-run) and they used to be three copies of "send exit,
 * shutdown". None of them banked the descent, which is why an endless run left
 * with Escape never reached the run counter (Beppu, 2026-09-20).
 * ==========================================================================*/

/**
 * Build the one leave(): bank the descent, tell the host, wind the page down.
 * The order matters - the booking has to be on the wire before `exit`, because
 * `exit` is what arms the host's teardown.
 *
 * @param {(msg: object) => void} send   the bridge's send
 * @param {() => object|null} getGame    the run brain, which does not exist at boot
 * @param {() => void} shutdown          the page's own teardown
 */
export function createExit({ send, getGame, shutdown }) {
  return function leave() {
    let booked = false;
    try {
      const game = getGame && getGame();
      booked = !!(game && game.abandonRun && game.abandonRun());
    } catch (e) { /* a failed booking must never trap the player in the page */ }
    send({ type: 'exit' });
    if (shutdown) shutdown();
    return booked;
  };
}

/**
 * HOLD Escape ~1.2s to leave; a tap stays the engine's pause toggle. Returns a
 * disposer. The timer functions are injectable so a test can run the hold on a
 * clock of its own.
 */
export function installExitHold({ target, leave, holdMs = 1200,
                                  setTimer = setTimeout, clearTimer = clearTimeout }) {
  let timer = 0;
  const onDown = (e) => {
    if (!e || e.key !== 'Escape' || e.repeat) return;
    clearTimer(timer);
    timer = setTimer(() => { timer = 0; leave(); }, holdMs);
  };
  const onUp = (e) => {
    if (!e || e.key !== 'Escape') return;
    clearTimer(timer);
    timer = 0;
  };
  target.addEventListener('keydown', onDown);
  target.addEventListener('keyup', onUp);
  return () => {
    clearTimer(timer);
    target.removeEventListener('keydown', onDown);
    target.removeEventListener('keyup', onUp);
  };
}
